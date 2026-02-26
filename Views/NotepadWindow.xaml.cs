using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using BMS.Overlay.Models;
using BMS.Overlay.Services;

namespace BMS.Overlay.Views;

public partial class NotepadWindow : Window
{
    private readonly NotepadService _notepadService;
    private readonly SharedNotepadService _sharedService;
    private readonly SignalRService _signalRService;
    private readonly SettingsService _settingsService;

    private NotepadNote? _currentNote;
    private bool _suppressTextChanged;
    private readonly DispatcherTimer _autoSaveTimer;

    // Form mode: all interactive controls keyed by field index
    private readonly Dictionary<int, FrameworkElement> _formControls = new();
    private bool _isFormMode;

    // Shared notepad state
    private bool _isSharedMode;
    private DateTime _lastLocalEditAt = DateTime.MinValue;
    private readonly DispatcherTimer _sharedAutoSaveTimer;

    // Roblox visibility — driven by MainWindow
    private bool _robloxVisible = true;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    public NotepadWindow(NotepadService notepadService, SharedNotepadService sharedService,
        SignalRService signalRService, SettingsService settingsService)
    {
        InitializeComponent();
        _notepadService = notepadService;
        _sharedService = sharedService;
        _signalRService = signalRService;
        _settingsService = settingsService;

        // Local auto-save every 3 seconds when dirty
        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _autoSaveTimer.Tick += async (_, _) =>
        {
            _autoSaveTimer.Stop();
            await SaveCurrentNoteAsync();
        };

        // Shared auto-save: push to server after 3 seconds of idle typing
        _sharedAutoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _sharedAutoSaveTimer.Tick += async (_, _) =>
        {
            _sharedAutoSaveTimer.Stop();
            await SaveSharedNotepadAsync();
        };

        // Subscribe to incoming shared notepad updates from SignalR
        _signalRService.OnSharedNotepadUpdated += OnSharedNotepadUpdated;

        Loaded += async (_, _) =>
        {
            await _notepadService.LoadAsync();

            if (_notepadService.Notes.Count > 0)
                LoadNote(_notepadService.Notes[0]);
            else
                CreateAndLoadEmptyNote();
        };
    }

    // ═══════════════════════════════════════════════════════
    //  Window chrome
    // ═══════════════════════════════════════════════════════

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        EditorPanel.Visibility = Visibility.Collapsed;
        MinimizedButton.Visibility = Visibility.Visible;
        Width = 36;
        Height = 36;
        ResizeMode = ResizeMode.NoResize;
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        MinimizedButton.Visibility = Visibility.Collapsed;
        EditorPanel.Visibility = Visibility.Visible;
        Width = 420;
        Height = 500;
        ResizeMode = ResizeMode.CanResizeWithGrip;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        // Save and hide — don't destroy the window so it can be re-shown
        SaveCurrentNoteAsync().ConfigureAwait(false);
        Hide();
    }

    private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _signalRService.OnSharedNotepadUpdated -= OnSharedNotepadUpdated;

        if (_isSharedMode && _sharedService.CurrentNotepadId != null)
            await _signalRService.UnsubscribeFromSharedNotepadAsync(_sharedService.CurrentNotepadId);

        // Persist on real close
        await SaveCurrentNoteAsync();
    }

    // ═══════════════════════════════════════════════════════
    //  Roblox Visibility
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Called by MainWindow when Roblox foreground state changes.
    /// Hides/shows the notepad alongside the main overlay.
    /// </summary>
    public void SetRobloxVisible(bool visible)
    {
        _robloxVisible = visible;
        UpdateNotepadVisibility();
    }

    private void UpdateNotepadVisibility()
    {
        // Also stay visible if the notepad itself is the foreground window
        bool isSelfFocused = false;
        try
        {
            var fg = GetForegroundWindow();
            var myHandle = new WindowInteropHelper(this).Handle;
            if (myHandle != IntPtr.Zero && fg == myHandle)
                isSelfFocused = true;
        }
        catch { /* ignore */ }

        if (_robloxVisible || isSelfFocused)
        {
            if (Opacity < 1.0)
            {
                Opacity = 1.0;
                IsHitTestVisible = true;
            }
        }
        else
        {
            if (Opacity > 0.0)
            {
                Opacity = 0.0;
                IsHitTestVisible = false;
            }
        }
    }

    // ═══════════════════════════════════════════════════════
    //  Note Management
    // ═══════════════════════════════════════════════════════

    private void CreateAndLoadEmptyNote()
    {
        var note = _notepadService.CreateNote();
        LoadNote(note);
    }

    private void LoadNote(NotepadNote note)
    {
        // Save previous note first
        if (_currentNote != null)
            PersistEditorToCurrentNote();

        _currentNote = note;
        _suppressTextChanged = true;

        if (note.IsForm)
        {
            // ── Form mode ──
            _isFormMode = true;
            RenderForm(note);
            NoteTitle.Text = note.Title;
            SavedIndicator.Text = "Saved";
            _suppressTextChanged = false;
            HideAllPanels();
            FormPanel.Visibility = Visibility.Visible;
            return;
        }

        // ── Freeform mode ──
        _isFormMode = false;

        if (!string.IsNullOrEmpty(note.RtfContent))
        {
            try
            {
                var ms = new MemoryStream(Convert.FromBase64String(note.RtfContent));
                Editor.Document = new FlowDocument();
                var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
                range.Load(ms, DataFormats.Rtf);
            }
            catch
            {
                // If RTF is corrupt, just clear
                Editor.Document = new FlowDocument(new Paragraph(new Run(note.RtfContent)));
            }
        }
        else
        {
            Editor.Document = new FlowDocument();
        }

        // Apply default white foreground to newly created content
        var fullRange = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
        if (string.IsNullOrWhiteSpace(fullRange.Text))
        {
            fullRange.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD8)));
        }

        NoteTitle.Text = note.Title;
        SavedIndicator.Text = "Saved";
        _suppressTextChanged = false;

        // Switch to editor view
        HideAllPanels();
        Editor.Visibility = Visibility.Visible;
    }

    private void PersistEditorToCurrentNote()
    {
        if (_currentNote == null) return;

        try
        {
            if (_currentNote.IsForm)
            {
                // Collect values from form controls
                foreach (var kvp in _formControls)
                {
                    string val = kvp.Value switch
                    {
                        TextBox tb => tb.Text,
                        CheckBox cb => (cb.IsChecked == true).ToString(),
                        _ => string.Empty
                    };
                    _currentNote.FormData[kvp.Key.ToString()] = val;
                }
            }
            else
            {
                var range = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd);
                using var ms = new MemoryStream();
                range.Save(ms, DataFormats.Rtf);
                _currentNote.RtfContent = Convert.ToBase64String(ms.ToArray());
            }

            _currentNote.UpdatedAt = DateTime.UtcNow;
            _notepadService.UpdateNote(_currentNote);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Notepad] Persist error: {ex.Message}");
        }
    }

    private async System.Threading.Tasks.Task SaveCurrentNoteAsync()
    {
        PersistEditorToCurrentNote();
        await _notepadService.SaveAsync();
        Dispatcher.Invoke(() => SavedIndicator.Text = "Saved");
    }

    // ═══════════════════════════════════════════════════════
    //  Formatting
    // ═══════════════════════════════════════════════════════

    private void Bold_Click(object sender, RoutedEventArgs e)
    {
        var sel = Editor.Selection;
        if (sel.IsEmpty) return;

        var current = sel.GetPropertyValue(TextElement.FontWeightProperty);
        var newWeight = (current is FontWeight fw && fw == FontWeights.Bold)
            ? FontWeights.Normal : FontWeights.Bold;
        sel.ApplyPropertyValue(TextElement.FontWeightProperty, newWeight);
        Editor.Focus();
    }

    private void Italic_Click(object sender, RoutedEventArgs e)
    {
        var sel = Editor.Selection;
        if (sel.IsEmpty) return;

        var current = sel.GetPropertyValue(TextElement.FontStyleProperty);
        var newStyle = (current is FontStyle fs && fs == FontStyles.Italic)
            ? FontStyles.Normal : FontStyles.Italic;
        sel.ApplyPropertyValue(TextElement.FontStyleProperty, newStyle);
        Editor.Focus();
    }

    private void Underline_Click(object sender, RoutedEventArgs e)
    {
        var sel = Editor.Selection;
        if (sel.IsEmpty) return;

        var current = sel.GetPropertyValue(Inline.TextDecorationsProperty);
        if (current is TextDecorationCollection decs && decs.Contains(TextDecorations.Underline[0]))
            sel.ApplyPropertyValue(Inline.TextDecorationsProperty, new TextDecorationCollection());
        else
            sel.ApplyPropertyValue(Inline.TextDecorationsProperty, TextDecorations.Underline);
        Editor.Focus();
    }

    private void Strike_Click(object sender, RoutedEventArgs e)
    {
        var sel = Editor.Selection;
        if (sel.IsEmpty) return;

        var current = sel.GetPropertyValue(Inline.TextDecorationsProperty);
        if (current is TextDecorationCollection decs && decs.Contains(TextDecorations.Strikethrough[0]))
            sel.ApplyPropertyValue(Inline.TextDecorationsProperty, new TextDecorationCollection());
        else
            sel.ApplyPropertyValue(Inline.TextDecorationsProperty, TextDecorations.Strikethrough);
        Editor.Focus();
    }

    // ═══════════════════════════════════════════════════════
    //  Editor events
    // ═══════════════════════════════════════════════════════

    private void Editor_SelectionChanged(object sender, RoutedEventArgs e)
    {
        // Update toggle button states to reflect current selection formatting
        var sel = Editor.Selection;

        var weight = sel.GetPropertyValue(TextElement.FontWeightProperty);
        BoldBtn.IsChecked = weight is FontWeight fw && fw == FontWeights.Bold;

        var style = sel.GetPropertyValue(TextElement.FontStyleProperty);
        ItalicBtn.IsChecked = style is FontStyle fs && fs == FontStyles.Italic;

        var decs = sel.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
        UnderlineBtn.IsChecked = decs != null && decs.Contains(TextDecorations.Underline[0]);
        StrikeBtn.IsChecked = decs != null && decs.Contains(TextDecorations.Strikethrough[0]);
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTextChanged) return;

        SavedIndicator.Text = "Unsaved";

        if (_isSharedMode)
        {
            _lastLocalEditAt = DateTime.UtcNow;
            _sharedAutoSaveTimer.Stop();
            _sharedAutoSaveTimer.Start();
        }
        else
        {
            _autoSaveTimer.Stop();
            _autoSaveTimer.Start();
        }
    }

    // ═══════════════════════════════════════════════════════
    //  Note Title editing
    // ═══════════════════════════════════════════════════════

    private void NoteTitle_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2 || _currentNote == null) return;

        // Replace the TextBlock with a TextBox for inline rename
        var titleTextBlock = (TextBlock)sender;
        var parent = (DockPanel)titleTextBlock.Parent;

        var textBox = new TextBox
        {
            Text = _currentNote.Title,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD8)),
            Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0A)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 100,
            SelectionBrush = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC))
        };

        var idx = parent.Children.IndexOf(titleTextBlock);
        parent.Children.Remove(titleTextBlock);
        parent.Children.Insert(idx, textBox);
        textBox.Focus();
        textBox.SelectAll();

        void CommitRename()
        {
            var newTitle = textBox.Text.Trim();
            if (string.IsNullOrEmpty(newTitle)) newTitle = "Untitled Note";
            _currentNote.Title = newTitle;
            NoteTitle.Text = newTitle;
            parent.Children.Remove(textBox);
            parent.Children.Insert(idx, titleTextBlock);
            titleTextBlock.Text = newTitle;
            SavedIndicator.Text = "Unsaved";
            _autoSaveTimer.Stop();
            _autoSaveTimer.Start();
        }

        textBox.KeyDown += (_, ke) =>
        {
            if (ke.Key == Key.Enter || ke.Key == Key.Escape) CommitRename();
        };
        textBox.LostFocus += (_, _) => CommitRename();
    }

    // ═══════════════════════════════════════════════════════
    //  Note List
    // ═══════════════════════════════════════════════════════

    private void ShowNoteList_Click(object sender, RoutedEventArgs e)
    {
        // Save current note first
        PersistEditorToCurrentNote();

        HideAllPanels();
        NoteListPanel.Visibility = Visibility.Visible;
        RebuildNoteList();
    }

    private void HideNoteList_Click(object sender, RoutedEventArgs e)
    {
        HideAllPanels();
        ShowActiveEditor();
    }

    private void RebuildNoteList()
    {
        NoteListStack.Children.Clear();

        if (_notepadService.Notes.Count == 0)
        {
            NoteListStack.Children.Add(new TextBlock
            {
                Text = "NO NOTES YET. CLICK '+ NEW' TO CREATE ONE.",
                Foreground = new SolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x6A)),
                FontSize = 12,
                Margin = new Thickness(8, 16, 8, 0)
            });
            return;
        }

        foreach (var note in _notepadService.Notes)
        {
            var isActive = _currentNote != null && _currentNote.Id == note.Id;

            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = note.Title,
                FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal,
                Foreground = isActive
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF))
                    : new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD8))
            });
            sp.Children.Add(new TextBlock
            {
                Text = note.UpdatedAt.ToLocalTime().ToString("g"),
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x6A))
            });

            var btn = new Button
            {
                Content = sp,
                Style = (Style)FindResource("NoteListBtn"),
                Tag = note.Id
            };
            btn.Click += NoteListItem_Click;

            NoteListStack.Children.Add(btn);
        }
    }

    private void NoteListItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            var note = _notepadService.Notes.FirstOrDefault(n => n.Id == id);
            if (note != null)
                LoadNote(note);
        }
    }

    // ═══════════════════════════════════════════════════════
    //  Template Picker
    // ═══════════════════════════════════════════════════════

    private void NewFromTemplate_Click(object sender, RoutedEventArgs e)
    {
        HideAllPanels();
        TemplatePanel.Visibility = Visibility.Visible;
        RebuildTemplateList();
    }

    private void HideTemplatePanel_Click(object sender, RoutedEventArgs e)
    {
        HideAllPanels();
        ShowActiveEditor();
    }

    private void RebuildTemplateList()
    {
        TemplateListStack.Children.Clear();

        var files = _notepadService.GetTemplateFiles();
        if (files.Count == 0)
        {
            TemplateListStack.Children.Add(new TextBlock
            {
                Text = "NO TEMPLATES FOUND IN TEMPLATES FOLDER.",
                Foreground = new SolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x6A)),
                FontSize = 12,
                Margin = new Thickness(8, 16, 8, 0)
            });
            return;
        }

        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var ext = Path.GetExtension(file).ToUpperInvariant();

            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = name,
                Foreground = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD8))
            });
            sp.Children.Add(new TextBlock
            {
                Text = ext,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x6A))
            });

            var btn = new Button
            {
                Content = sp,
                Style = (Style)FindResource("NoteListBtn"),
                Tag = file
            };
            btn.Click += TemplateItem_Click;
            TemplateListStack.Children.Add(btn);
        }
    }

    private void TemplateItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string filePath) return;

        var content = _notepadService.ReadTemplateContent(filePath);
        var title = Path.GetFileNameWithoutExtension(filePath);

        // ── Form template? ──
        if (_notepadService.IsFormTemplate(content))
        {
            var body = _notepadService.GetFormBody(content);
            var note = _notepadService.CreateNote(title);
            note.IsForm = true;
            note.FormTemplate = body;
            // FormData starts empty — fields default to blank / unchecked
            LoadNote(note);
            SavedIndicator.Text = "Unsaved";
            _autoSaveTimer.Stop();
            _autoSaveTimer.Start();
            return;
        }

        // ── Regular template ──
        var regularNote = _notepadService.CreateNote(title);

        // Load template text into a FlowDocument
        _suppressTextChanged = true;
        _currentNote = regularNote;
        _isFormMode = false;
        Editor.Document = new FlowDocument();

        // Insert template text as plain text, preserving line breaks
        var paragraph = new Paragraph();
        paragraph.Foreground = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD8));
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            paragraph.Inlines.Add(new Run(lines[i].TrimEnd('\r')) { Foreground = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD8)) });
            if (i < lines.Length - 1)
                paragraph.Inlines.Add(new LineBreak());
        }
        Editor.Document.Blocks.Add(paragraph);

        NoteTitle.Text = regularNote.Title;
        SavedIndicator.Text = "Unsaved";
        _suppressTextChanged = false;

        HideAllPanels();
        Editor.Visibility = Visibility.Visible;

        // Trigger save
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    // ═══════════════════════════════════════════════════════
    //  New / Delete
    // ═══════════════════════════════════════════════════════

    private void NewNote_Click(object sender, RoutedEventArgs e)
    {
        PersistEditorToCurrentNote();
        CreateAndLoadEmptyNote();
    }

    private async void DeleteNote_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNote == null) return;

        var result = MessageBox.Show(
            $"Delete \"{_currentNote.Title}\"?",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        _notepadService.DeleteNote(_currentNote.Id);
        _currentNote = null;
        await _notepadService.SaveAsync();

        if (_notepadService.Notes.Count > 0)
            LoadNote(_notepadService.Notes[0]);
        else
            CreateAndLoadEmptyNote();
    }

    // ═══════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════

    private void HideAllPanels()
    {
        Editor.Visibility = Visibility.Collapsed;
        FormPanel.Visibility = Visibility.Collapsed;
        NoteListPanel.Visibility = Visibility.Collapsed;
        TemplatePanel.Visibility = Visibility.Collapsed;
        SharedPanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Shows either the RichTextBox editor or the Form panel
    /// depending on the current note type.
    /// </summary>
    private void ShowActiveEditor()
    {
        if (_currentNote?.IsForm == true)
            FormPanel.Visibility = Visibility.Visible;
        else
            Editor.Visibility = Visibility.Visible;
    }

    // ═══════════════════════════════════════════════════════
    //  Form Rendering
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Parses the form template text and builds inline TextBlocks,
    /// TextBoxes (for __) and CheckBoxes (for ☐) in <see cref="FormStack"/>.
    /// Restores previously saved values from <see cref="NotepadNote.FormData"/>.
    /// </summary>
    private void RenderForm(NotepadNote note)
    {
        FormStack.Children.Clear();
        _formControls.Clear();

        int fieldIndex = 0;
        var lines = note.FormTemplate.Split('\n');

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            // Blank line → small spacer
            if (string.IsNullOrWhiteSpace(line))
            {
                FormStack.Children.Add(new Border { Height = 6 });
                continue;
            }

            // Check if the line is a section header (all caps + special chars, no __ or ☐)
            bool isHeader = !line.Contains("__") && !line.Contains("___") && !line.Contains("____") && !line.Contains("☐")
                            && (line == line.ToUpperInvariant() || line.StartsWith("LINE "));

            if (isHeader && !line.Contains(":"))
            {
                // Render as a gold header
                FormStack.Children.Add(new TextBlock
                {
                    Text = line,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD8)),
                    Margin = new Thickness(0, 6, 0, 2),
                    TextWrapping = TextWrapping.Wrap
                });
                continue;
            }

            // Parse the line into segments: static text, __ fields, ☐ checkboxes
            var panel = new WrapPanel
            {
                Margin = new Thickness(0, 2, 0, 2),
                VerticalAlignment = VerticalAlignment.Center
            };

            // Regex splits on ____ / ___ / __ or ☐, keeping the delimiter (longest match first)
            var segments = Regex.Split(line, @"(____|___|__|☐)");

            foreach (var seg in segments)
            {
                if (seg == "____" || seg == "___" || seg == "__")
                {
                    // Determine size: __ = small, ___ = medium, ____ = large (multiline)
                    bool isLarge = seg.Length == 4;
                    var (minW, maxW) = seg.Length switch
                    {
                        4 => (200, 9999), // large — stretches full width
                        3 => (120, 280),  // medium
                        _ => (60, 150),   // small
                    };
                    // Editable text field
                    var saved = note.FormData.TryGetValue(fieldIndex.ToString(), out var v) ? v : "";

                    var tb = new TextBox
                    {
                        Text = saved,
                        MinWidth = minW,
                        MaxWidth = isLarge ? double.PositiveInfinity : maxW,
                        Height = isLarge ? 90 : 22,
                        MinHeight = isLarge ? 90 : 22,
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD8)),
                        CaretBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
                        Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x0A, 0x0A)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                        BorderThickness = isLarge ? new Thickness(1) : new Thickness(0, 0, 0, 1),
                        Padding = new Thickness(4, 2, 4, 2),
                        VerticalContentAlignment = isLarge ? VerticalAlignment.Top : VerticalAlignment.Center,
                        AcceptsReturn = isLarge,
                        TextWrapping = isLarge ? TextWrapping.Wrap : TextWrapping.NoWrap,
                        VerticalScrollBarVisibility = isLarge ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden,
                        HorizontalAlignment = isLarge ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
                        Tag = fieldIndex
                    };
                    tb.TextChanged += FormField_Changed;
                    if (!isLarge) tb.KeyDown += FormField_KeyDown;

                    _formControls[fieldIndex] = tb;
                    panel.Children.Add(tb);
                    fieldIndex++;
                }
                else if (seg == "☐")
                {
                    // Checkbox
                    var saved = note.FormData.TryGetValue(fieldIndex.ToString(), out var v)
                        && bool.TryParse(v, out var b) && b;

                    var cb = new CheckBox
                    {
                        IsChecked = saved,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(2, 0, 4, 0),
                        Tag = fieldIndex
                    };
                    // Style the checkbox for dark theme
                    cb.Foreground = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD8));
                    cb.Checked += FormField_Checked;
                    cb.Unchecked += FormField_Checked;

                    _formControls[fieldIndex] = cb;
                    panel.Children.Add(cb);
                    fieldIndex++;
                }
                else if (!string.IsNullOrEmpty(seg))
                {
                    // Static label text
                    panel.Children.Add(new TextBlock
                    {
                        Text = seg,
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)),
                        VerticalAlignment = VerticalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 0)
                    });
                }
            }

            FormStack.Children.Add(panel);
        }
    }

    private void FormField_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressTextChanged) return;
        SavedIndicator.Text = "Unsaved";
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    private void FormField_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressTextChanged) return;
        SavedIndicator.Text = "Unsaved";
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    /// <summary>
    /// Enter / Tab in a form TextBox jumps to the next editable field.
    /// </summary>
    private void FormField_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Tab) return;

        e.Handled = true;

        if (sender is not FrameworkElement fe || fe.Tag is not int currentIdx) return;

        // Find the next field index
        var nextIdx = _formControls.Keys
            .Where(k => k > currentIdx)
            .OrderBy(k => k)
            .FirstOrDefault(-1);

        if (nextIdx >= 0 && _formControls.TryGetValue(nextIdx, out var nextControl))
        {
            nextControl.Focus();
            if (nextControl is TextBox nextTb)
                nextTb.SelectAll();
        }
    }

    // ═══════════════════════════════════════════════════════
    //  Shared Notepad
    // ═══════════════════════════════════════════════════════

    private void ShowSharedPanel_Click(object sender, RoutedEventArgs e)
    {
        // Reset create section
        CreatePasswordBox.Text = string.Empty;
        CreateErrorText.Visibility = Visibility.Collapsed;
        CreatedSuccessBlock.Visibility = Visibility.Collapsed;

        // Reset join section
        SharedPasswordBox.Password = string.Empty;
        SharedErrorText.Visibility = Visibility.Collapsed;

        HideAllPanels();
        SharedPanel.Visibility = Visibility.Visible;
        CreatePasswordBox.Focus();
    }

    private void HideSharedPanel_Click(object sender, RoutedEventArgs e)
    {
        HideAllPanels();
        ShowActiveEditor();
    }

    // ── Create handlers ──

    private void CreatePassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            CreateShared_Click(sender, e);
    }

    private void GeneratePassword_Click(object sender, RoutedEventArgs e)
    {
        // Generate a readable random code: WORD-WORD-####
        string[] words = ["ALPHA", "BRAVO", "CHARLIE", "DELTA", "ECHO", "FOXTROT",
                          "GHOST", "HAWK", "IRON", "JADE", "KILO", "LIMA",
                          "NOVA", "OSCAR", "PAPA", "ROMEO", "SIERRA", "TANGO",
                          "ULTRA", "VICTOR", "WHISKY", "XRAY", "YANKEE", "ZULU"];
        var rng = Random.Shared;
        var code = $"{words[rng.Next(words.Length)]}-{words[rng.Next(words.Length)]}-{rng.Next(1000, 9999)}";
        CreatePasswordBox.Text = code;
        CreateErrorText.Visibility = Visibility.Collapsed;
        CreatedSuccessBlock.Visibility = Visibility.Collapsed;
    }

    private async void CreateShared_Click(object sender, RoutedEventArgs e)
    {
        var password = CreatePasswordBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(password))
        {
            CreateErrorText.Text = "Please enter or generate a password.";
            CreateErrorText.Visibility = Visibility.Visible;
            return;
        }

        var settings = _settingsService.GetSettings();
        if (settings.SelectedFactionId == null)
        {
            CreateErrorText.Text = "No faction selected. Go to Options and select a faction first.";
            CreateErrorText.Visibility = Visibility.Visible;
            return;
        }
        if (settings.SelectedRoleId == null)
        {
            CreateErrorText.Text = "No role selected. Go to Options and select a role first.";
            CreateErrorText.Visibility = Visibility.Visible;
            return;
        }

        CreateErrorText.Visibility = Visibility.Collapsed;

        var result = await _sharedService.JoinAsync(
            settings.SelectedFactionId.Value.ToString(),
            settings.SelectedRoleId.Value.ToString(),
            password);

        if (result == null)
        {
            CreateErrorText.Text = "Could not create shared notepad. Check your connection.";
            CreateErrorText.Visibility = Visibility.Visible;
            return;
        }

        // Show the password so the user can share it
        CreatedPasswordDisplay.Text = password;
        CreatedSuccessBlock.Visibility = Visibility.Visible;

        var (notepadId, content) = result.Value;
        await EnterSharedModeAsync(notepadId, content);
    }

    private void CopyCreatedPassword_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(CreatedPasswordDisplay.Text))
            Clipboard.SetText(CreatedPasswordDisplay.Text);
    }

    // ── Join handlers ──

    private void SharedPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            JoinShared_Click(sender, e);
    }

    private async void JoinShared_Click(object sender, RoutedEventArgs e)
    {
        var password = SharedPasswordBox.Password;
        if (string.IsNullOrWhiteSpace(password))
        {
            ShowSharedError("Please enter a password.");
            return;
        }

        var settings = _settingsService.GetSettings();
        if (settings.SelectedFactionId == null)
        {
            ShowSharedError("No faction selected. Go to Options and select a faction first.");
            return;
        }
        if (settings.SelectedRoleId == null)
        {
            ShowSharedError("No role selected. Go to Options and select a role first.");
            return;
        }

        SharedErrorText.Visibility = Visibility.Collapsed;
        SavedIndicator.Text = "Joining...";

        var result = await _sharedService.JoinAsync(
            settings.SelectedFactionId.Value.ToString(),
            settings.SelectedRoleId.Value.ToString(),
            password);

        if (result == null)
        {
            ShowSharedError("Could not connect to shared notepad. Check your connection.");
            SavedIndicator.Text = "Error";
            return;
        }

        var (notepadId, content) = result.Value;
        await EnterSharedModeAsync(notepadId, content);
    }

    /// <summary>
    /// Shared entry point for both Create and Join flows.
    /// Subscribes to SignalR, loads content, and switches the UI to shared mode.
    /// </summary>
    private async System.Threading.Tasks.Task EnterSharedModeAsync(string notepadId, string rtfBase64)
    {
        await _signalRService.SubscribeToSharedNotepadAsync(notepadId);

        _suppressTextChanged = true;
        Editor.Document = new System.Windows.Documents.FlowDocument();
        if (!string.IsNullOrEmpty(rtfBase64))
        {
            try
            {
                var ms = new MemoryStream(Convert.FromBase64String(rtfBase64));
                var range = new System.Windows.Documents.TextRange(
                    Editor.Document.ContentStart, Editor.Document.ContentEnd);
                range.Load(ms, DataFormats.Rtf);
            }
            catch { /* corrupt or empty — start fresh */ }
        }
        _suppressTextChanged = false;

        var fullRange = new System.Windows.Documents.TextRange(
            Editor.Document.ContentStart, Editor.Document.ContentEnd);
        if (string.IsNullOrWhiteSpace(fullRange.Text))
        {
            fullRange.ApplyPropertyValue(System.Windows.Documents.TextElement.ForegroundProperty,
                new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD8)));
        }

        _isSharedMode = true;
        NoteTitle.Text = "SHARED NOTEPAD";
        SavedIndicator.Text = "Synced";
        SharedBadge.Visibility = Visibility.Visible;
        SharedBtn.Visibility = Visibility.Collapsed;
        LeaveSharedBtn.Visibility = Visibility.Visible;

        HideAllPanels();
        Editor.Visibility = Visibility.Visible;
    }

    private async void LeaveShared_Click(object sender, RoutedEventArgs e)
    {
        if (_sharedService.CurrentNotepadId != null)
            await _signalRService.UnsubscribeFromSharedNotepadAsync(_sharedService.CurrentNotepadId);

        _sharedAutoSaveTimer.Stop();
        _sharedService.Leave();
        _isSharedMode = false;

        SharedBadge.Visibility = Visibility.Collapsed;
        SharedBtn.Visibility = Visibility.Visible;
        LeaveSharedBtn.Visibility = Visibility.Collapsed;

        // Return to the current local note
        if (_currentNote != null)
            LoadNote(_currentNote);
        else
            CreateAndLoadEmptyNote();
    }

    private async System.Threading.Tasks.Task SaveSharedNotepadAsync()
    {
        if (!_isSharedMode) return;

        try
        {
            var range = new System.Windows.Documents.TextRange(
                Editor.Document.ContentStart, Editor.Document.ContentEnd);
            using var ms = new MemoryStream();
            range.Save(ms, DataFormats.Rtf);
            var rtfBase64 = Convert.ToBase64String(ms.ToArray());

            var ok = await _sharedService.SaveAsync(rtfBase64);
            Dispatcher.Invoke(() => SavedIndicator.Text = ok ? "Synced" : "Sync Failed");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SharedNotepad] Save error: {ex.Message}");
        }
    }

    /// <summary>
    /// Called by SignalR when another user updates the shared notepad.
    /// Only applies the update if the local user hasn't typed recently.
    /// </summary>
    private void OnSharedNotepadUpdated(string rtfBase64, DateTime updatedAt)
    {
        if (!_isSharedMode) return;

        // Ignore if user typed within last 2 seconds (don't overwrite their in-progress edits)
        if ((DateTime.UtcNow - _lastLocalEditAt).TotalSeconds < 2) return;

        Dispatcher.Invoke(() =>
        {
            _suppressTextChanged = true;
            try
            {
                Editor.Document = new System.Windows.Documents.FlowDocument();
                if (!string.IsNullOrEmpty(rtfBase64))
                {
                    var ms = new MemoryStream(Convert.FromBase64String(rtfBase64));
                    var range = new System.Windows.Documents.TextRange(
                        Editor.Document.ContentStart, Editor.Document.ContentEnd);
                    range.Load(ms, DataFormats.Rtf);
                }
                SavedIndicator.Text = "Synced";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SharedNotepad] Update apply error: {ex.Message}");
            }
            finally
            {
                _suppressTextChanged = false;
            }
        });
    }

    private void ShowSharedError(string message)
    {
        SharedErrorText.Text = message;
        SharedErrorText.Visibility = Visibility.Visible;
    }
}

