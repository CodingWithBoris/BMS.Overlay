using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BMS.Overlay.ViewModels;

namespace BMS.Overlay.Views
{
    public partial class BmsOptionsView : Page
    {
        private bool _isInitializing = true;
        private bool _isProgrammaticSelectionChange;
        private string? _lastAuthenticatedFactionId;
        private List<BMS.Shared.Models.FactionInfo> _allFactions = new();

        // Keybind capture state
        private Border? _activeKeybindBorder;
        private string? _activeKeybindSetting;

        // Keybind button -> TextBlock lookup
        private readonly Dictionary<string, TextBlock> _keybindTextBlocks = new();
        private readonly Dictionary<string, Border> _keybindBorders = new();

        public BmsOptionsView()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                // Build lookup tables
                _keybindTextBlocks["KeyPrevious"] = KeyPrevText;
                _keybindTextBlocks["KeyNext"] = KeyNextText;
                _keybindTextBlocks["KeyMinimize"] = KeyMinimizeText;
                _keybindTextBlocks["KeyRestore"] = KeyRestoreText;
                _keybindTextBlocks["KeyToggleOverlay"] = KeyToggleText;

                _keybindBorders["KeyPrevious"] = KeyPrevBorder;
                _keybindBorders["KeyNext"] = KeyNextBorder;
                _keybindBorders["KeyMinimize"] = KeyMinimizeBorder;
                _keybindBorders["KeyRestore"] = KeyRestoreBorder;
                _keybindBorders["KeyToggleOverlay"] = KeyToggleBorder;

                if (DataContext is MainViewModel vm)
                {
                    if (!string.IsNullOrWhiteSpace(vm.SelectedFactionId) &&
                        vm.AuthenticatedFactionIds.Contains(vm.SelectedFactionId))
                    {
                        _lastAuthenticatedFactionId = vm.SelectedFactionId;
                    }

                    // Load all keybind values from settings
                    var settings = vm.GetSettings();
                    KeyPrevText.Text = FormatKeyName(settings.KeyPrevious);
                    KeyNextText.Text = FormatKeyName(settings.KeyNext);
                    KeyMinimizeText.Text = FormatKeyName(settings.KeyMinimize);
                    KeyRestoreText.Text = FormatKeyName(settings.KeyRestore);
                    KeyToggleText.Text = FormatKeyName(settings.KeyToggleOverlay);

                    // Load current overlay width
                    WidthInput.Text = $"{settings.OverlayWidth:0}";

                    // Load current font size
                    FontSizeInput.Text = $"{settings.OverlayFontSize:0}";

                    // Load JTAC mode
                    ApplyJtacVisuals(settings.JtacMode);

                    // Cache factions and take control of ItemsSource for inline filtering
                    _allFactions = vm.Factions.ToList();
                    FactionCombo.ItemsSource = _allFactions;
                    FactionCombo.AddHandler(
                        System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
                        new System.Windows.Controls.TextChangedEventHandler(OnFactionComboTextChanged));
                    vm.PropertyChanged += (_, pe) =>
                    {
                        if (pe.PropertyName == nameof(vm.Factions))
                            Dispatcher.Invoke(() =>
                            {
                                _allFactions = vm.Factions.ToList();
                                FactionCombo.ItemsSource = _allFactions;
                            });
                    };
                }

                _isInitializing = false;

                // Capture key events at page level for keybind capture
                PreviewKeyDown += BmsOptionsView_PreviewKeyDown;
                PreviewMouseDown += BmsOptionsView_PreviewMouseDown;
            };
        }

        /// <summary>
        /// Convert a Key enum name to a human-readable uppercase display string.
        /// </summary>
        private static string FormatKeyName(string keyName)
        {
            return keyName switch
            {
                "Left" => "LEFT ARROW",
                "Right" => "RIGHT ARROW",
                "Up" => "UP ARROW",
                "Down" => "DOWN ARROW",
                "OemQuestion" => "SLASH",
                "OemPeriod" => "PERIOD",
                "OemComma" => "COMMA",
                "OemMinus" => "MINUS",
                "OemPlus" => "PLUS",
                "OemTilde" => "TILDE",
                "OemOpenBrackets" => "LEFTBRACKET",
                "Oem6" => "RIGHTBRACKET",
                "OemPipe" => "BACKSLASH",
                "OemSemicolon" => "SEMICOLON",
                "OemQuotes" => "QUOTE",
                "Back" => "BACKSPACE",
                "Return" => "ENTER",
                "Capital" => "CAPSLOCK",
                "LeftShift" => "LEFTSHIFT",
                "RightShift" => "RIGHTSHIFT",
                "LeftCtrl" => "LEFTCONTROL",
                "RightCtrl" => "RIGHTCONTROL",
                "LeftAlt" => "LEFTALT",
                "RightAlt" => "RIGHTALT",
                "Space" => "SPACE",
                "Next" => "PAGEDOWN",
                "Prior" => "PAGEUP",
                _ => keyName.ToUpperInvariant()
            };
        }

        /// <summary>
        /// Clicked on any keybind button — start capturing.
        /// </summary>
        private void KeybindButton_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not Border border) return;
            var settingName = border.Tag as string;
            if (string.IsNullOrEmpty(settingName)) return;

            // If we're already capturing this one, cancel
            if (_activeKeybindBorder == border)
            {
                CancelKeybindCapture();
                return;
            }

            // Cancel any previous capture
            CancelKeybindCapture();

            _activeKeybindBorder = border;
            _activeKeybindSetting = settingName;

            // Highlight the active button
            border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CCCCCC"));
            if (_keybindTextBlocks.TryGetValue(settingName, out var tb))
            {
                tb.Text = "PRESS A KEY...";
                tb.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF"));
            }

            // Grab keyboard focus so we get key events
            Focusable = true;
            Focus();
            Keyboard.Focus(this);

            e.Handled = true;
        }

        /// <summary>
        /// Key pressed while capturing a keybind.
        /// </summary>
        private void BmsOptionsView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_activeKeybindBorder == null || _activeKeybindSetting == null) return;

            e.Handled = true;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            // Escape cancels capture
            if (key == Key.Escape)
            {
                CancelKeybindCapture();
                return;
            }

            // Don't allow modifier-only keys
            if (key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LWin || key == Key.RWin)
                return;

            var keyName = key.ToString();
            var settingName = _activeKeybindSetting;

            // Update the text display
            if (_keybindTextBlocks.TryGetValue(settingName, out var tb))
            {
                tb.Text = FormatKeyName(keyName);
                tb.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A0A0A0"));
            }

            // Reset border style
            if (_keybindBorders.TryGetValue(settingName, out var border))
            {
                border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A2A"));
            }

            _activeKeybindBorder = null;
            _activeKeybindSetting = null;

            // Save the keybind
            if (DataContext is MainViewModel vm)
            {
                vm.UpdateKeybind(settingName, keyName);
            }
        }

        /// <summary>
        /// Mouse click outside the active keybind button cancels capture.
        /// </summary>
        private void BmsOptionsView_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_activeKeybindBorder == null) return;

            // Check if the click is on the active border — if so, let KeybindButton_Click handle it
            if (_activeKeybindBorder.IsMouseOver) return;

            CancelKeybindCapture();
        }

        private void CancelKeybindCapture()
        {
            if (_activeKeybindBorder == null || _activeKeybindSetting == null) return;

            var settingName = _activeKeybindSetting;

            // Restore text from settings
            if (DataContext is MainViewModel vm && _keybindTextBlocks.TryGetValue(settingName, out var tb))
            {
                var settings = vm.GetSettings();
                var currentKey = settingName switch
                {
                    "KeyPrevious" => settings.KeyPrevious,
                    "KeyNext" => settings.KeyNext,
                    "KeyMinimize" => settings.KeyMinimize,
                    "KeyRestore" => settings.KeyRestore,
                    "KeyToggleOverlay" => settings.KeyToggleOverlay,
                    _ => ""
                };
                tb.Text = FormatKeyName(currentKey);
                tb.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A0A0A0"));
            }

            // Reset border style
            if (_keybindBorders.TryGetValue(settingName, out var border))
            {
                border.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A2A"));
            }

            _activeKeybindBorder = null;
            _activeKeybindSetting = null;
        }

        private void WidthApplyButton_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ApplyWidthFromInput();
        }

        private void WidthInput_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ApplyWidthFromInput();
                FactionCombo.Focus();
                e.Handled = true;
            }
        }

        private void WidthInput_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            // Only allow digits
            e.Handled = !int.TryParse(e.Text, out _);
        }

        private void ApplyWidthFromInput()
        {
            if (_isInitializing || DataContext is not MainViewModel vm) return;

            if (double.TryParse(WidthInput.Text, out var width))
            {
                width = Math.Clamp(width, 250, 600);
                WidthInput.Text = $"{width:0}";
                vm.UpdateOverlayWidth(width);
            }
            else
            {
                // Revert to current setting
                var settings = vm.GetSettings();
                WidthInput.Text = $"{settings.OverlayWidth:0}";
            }
        }

        private void FontSizeApplyButton_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ApplyFontSizeFromInput();
        }

        private void FontSizeInput_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ApplyFontSizeFromInput();
                FactionCombo.Focus();
                e.Handled = true;
            }
        }

        private void FontSizeInput_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            e.Handled = !int.TryParse(e.Text, out _);
        }

        private void ApplyFontSizeFromInput()
        {
            if (_isInitializing || DataContext is not MainViewModel vm) return;

            if (double.TryParse(FontSizeInput.Text, out var size))
            {
                size = Math.Clamp(size, 8, 24);
                FontSizeInput.Text = $"{size:0}";
                vm.UpdateOverlayFontSize(size);
            }
            else
            {
                var settings = vm.GetSettings();
                FontSizeInput.Text = $"{settings.OverlayFontSize:0}";
            }
        }

        private void JtacOn_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_isInitializing || DataContext is not MainViewModel vm) return;
            ApplyJtacVisuals(true);
            vm.UpdateJtacMode(true);
        }

        private void JtacOff_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_isInitializing || DataContext is not MainViewModel vm) return;
            ApplyJtacVisuals(false);
            vm.UpdateJtacMode(false);
        }

        private void ApplyJtacVisuals(bool jtacOn)
        {
            var activeBorder = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#D4880A"));
            var activeText = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFB300"));
            var dimBorder = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2A2A2A"));
            var dimText = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#6A6A6A"));
            var activeBg = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0A0A0A"));
            var inactiveBg = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0A0A0A"));

            JtacOnBorder.BorderBrush = jtacOn ? activeBorder : dimBorder;
            JtacOnBorder.Background = jtacOn ? activeBg : inactiveBg;
            JtacOnText.Foreground = jtacOn ? activeText : dimText;

            JtacOffBorder.BorderBrush = !jtacOn ? activeBorder : dimBorder;
            JtacOffBorder.Background = !jtacOn ? activeBg : inactiveBg;
            JtacOffText.Foreground = !jtacOn ? activeText : dimText;
        }

        private void OnFactionComboTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            // When WPF sets text after item selection, text == selected item's title — restore full list and skip
            var selectedTitle = (FactionCombo.SelectedItem as BMS.Shared.Models.FactionInfo)?.Title;
            if (FactionCombo.Text == selectedTitle)
            {
                FactionCombo.ItemsSource = _allFactions;
                return;
            }

            var query = FactionCombo.Text.Trim();
            FactionCombo.ItemsSource = string.IsNullOrEmpty(query)
                ? _allFactions
                : _allFactions.Where(f => f.Title.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

            if (!string.IsNullOrEmpty(query))
                FactionCombo.IsDropDownOpen = true;
        }

        private async void FactionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Skip selection changes during initial page load (binding restoration)
            if (_isInitializing || _isProgrammaticSelectionChange) return;

            if (DataContext is MainViewModel vm && FactionCombo.SelectedItem is BMS.Shared.Models.FactionInfo faction)
            {
                // Skip password for factions already authenticated this app session
                if (vm.AuthenticatedFactionIds.Contains(faction.Id))
                {
                    System.Diagnostics.Debug.WriteLine($"Faction {faction.Title} already selected, skipping password prompt");
                    _lastAuthenticatedFactionId = faction.Id;

                    if (vm.SelectedFactionId != faction.Id || vm.Roles.Count == 0)
                    {
                        await vm.SelectFactionAsync(faction);
                    }

                    return;
                }

                // Prompt for faction password
                var passwordWindow = new PasswordPromptWindow();
                if (passwordWindow.ShowDialog() == true)
                {
                    await vm.SelectFactionAsync(faction);
                    vm.AuthenticatedFactionIds.Add(faction.Id);
                    _lastAuthenticatedFactionId = faction.Id;
                }
                else
                {
                    _isProgrammaticSelectionChange = true;
                    if (!string.IsNullOrWhiteSpace(_lastAuthenticatedFactionId))
                        FactionCombo.SelectedValue = _lastAuthenticatedFactionId;
                    else
                        FactionCombo.SelectedIndex = -1;

                    _isProgrammaticSelectionChange = false;
                }
            }
        }
    }
}
