using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BMS.Overlay.ViewModels;
using BMS.Shared.Models;

namespace BMS.Overlay.Views
{
    public partial class BmsTabView : Page
    {
        public BmsTabView()
        {
            InitializeComponent();
            DataContextChanged += BmsTabView_DataContextChanged;
        }

        private void BmsTabView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is MainViewModel oldVm)
                oldVm.PropertyChanged -= ViewModel_PropertyChanged;

            if (e.NewValue is MainViewModel newVm)
            {
                newVm.PropertyChanged += ViewModel_PropertyChanged;
                RenderOrder(newVm.CurrentOrder);
            }
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentOrder))
            {
                var vm = DataContext as MainViewModel;
                Dispatcher.Invoke(() => RenderOrder(vm?.CurrentOrder));
            }
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as MainViewModel;
            RenderOrder(vm?.CurrentOrder);
        }

        // ──── Main Render ─────────────────────────────────────────────

        private void RenderOrder(BmsOrder? order)
        {
            SectionsPanel.Children.Clear();
            LegacyContentBorder.Visibility = Visibility.Collapsed;

            if (order == null)
            {
                NoOrdersPanel.Visibility = Visibility.Visible;
                return;
            }

            NoOrdersPanel.Visibility = Visibility.Collapsed;

            // If order has sections, render them
            if (order.Sections != null && order.Sections.Count > 0)
            {
                foreach (var section in order.Sections.OrderBy(s => s.Index))
                {
                    RenderSection(section, order);
                }
            }
            else if (!string.IsNullOrEmpty(order.Content))
            {
                // Legacy: render flat content
                LegacyContentBorder.Visibility = Visibility.Visible;
                LoadLegacyContent(order.Content);
            }
        }

        // ──── Section Rendering ───────────────────────────────────────

        private void RenderSection(OrderSection section, BmsOrder order)
        {
            var container = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

            // Section Title with horizontal rule
            if (!string.IsNullOrWhiteSpace(section.Title))
            {
                var titleRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };

                var titleText = new TextBlock
                {
                    Text = section.Title,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                };
                DockPanel.SetDock(titleText, Dock.Left);
                titleRow.Children.Add(titleText);

                var line = new Border
                {
                    Height = 1,
                    Background = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0),
                    CornerRadius = new CornerRadius(1),
                };
                titleRow.Children.Add(line);

                container.Children.Add(titleRow);
            }

            // Section body based on type
            switch (section.Type)
            {
                case "text":
                    RenderTextSection(container, section);
                    break;
                case "image":
                    RenderImageSection(container, section);
                    break;
                case "video":
                    RenderVideoSection(container, section);
                    break;
                case "poll":
                    RenderPollSection(container, section, order);
                    break;
                case "checklist":
                    RenderChecklistSection(container, section, order);
                    break;
            }

            SectionsPanel.Children.Add(container);
        }

        private void RenderTextSection(StackPanel parent, OrderSection section)
        {
            if (string.IsNullOrEmpty(section.Content)) return;

            var border = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x26, 0x30, 0x3A)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0x00, 0x00)),
            };

            var rtb = new RichTextBox
            {
                FontSize = 12,
                Foreground = Brushes.White,
                Background = Brushes.Transparent,
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                IsDocumentEnabled = true,
                Padding = new Thickness(10),
            };

            // Load XAML content
            try
            {
                var doc = new FlowDocument();
                var range = new TextRange(doc.ContentStart, doc.ContentEnd);
                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(section.Content));
                range.Load(stream, DataFormats.Xaml);
                SetDocumentStyle(doc);
                rtb.Document = doc;
            }
            catch
            {
                var doc = new FlowDocument(new Paragraph(new Run(section.Content)));
                SetDocumentStyle(doc);
                rtb.Document = doc;
            }

            border.Child = rtb;
            parent.Children.Add(border);
        }

        private void RenderImageSection(StackPanel parent, OrderSection section)
        {
            if (string.IsNullOrEmpty(section.ImageUrl)) return;

            try
            {
                var image = new Image
                {
                    Source = new BitmapImage(new Uri(section.ImageUrl, UriKind.Absolute)),
                    Stretch = Stretch.Uniform,
                    MaxHeight = 300,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 0, 4),
                };
                var border = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x26, 0x30, 0x3A)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0x00, 0x00)),
                    Padding = new Thickness(6),
                    Child = image,
                };
                parent.Children.Add(border);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading image: {ex.Message}");
                parent.Children.Add(new TextBlock
                {
                    Text = $"[Image: {section.ImageUrl}]",
                    Foreground = Brushes.Gray,
                    FontStyle = FontStyles.Italic,
                });
            }
        }

        private void RenderVideoSection(StackPanel parent, OrderSection section)
        {
            if (string.IsNullOrEmpty(section.VideoUrl)) return;

            var url = section.VideoUrl.Trim();
            var isGif = url.Split('?')[0].EndsWith(".gif", StringComparison.OrdinalIgnoreCase);

            if (isGif)
            {
                // GIFs: render as Image (MediaElement doesn't support GIF format)
                RenderGifAsImage(parent, url);
            }
            else
            {
                // Video: render via MediaElement (.mp4, .wmv, etc.)
                RenderVideoMediaElement(parent, url);
            }
        }

        private void RenderGifAsImage(StackPanel parent, string url)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(url, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();

                var image = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.Uniform,
                    MaxHeight = 350,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 0, 4),
                };

                var border = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x26, 0x30, 0x3A)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0x00, 0x00)),
                    Padding = new Thickness(6),
                    Child = image,
                };
                parent.Children.Add(border);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading GIF: {ex.Message}");
                parent.Children.Add(new TextBlock
                {
                    Text = $"[GIF: {url}]",
                    Foreground = Brushes.Gray,
                    FontStyle = FontStyles.Italic,
                });
            }
        }

        private void RenderVideoMediaElement(StackPanel parent, string url)
        {
            try
            {
                var mediaElement = new MediaElement
                {
                    LoadedBehavior = MediaState.Manual,
                    UnloadedBehavior = MediaState.Close,
                    Stretch = Stretch.Uniform,
                    MaxHeight = 350,
                    MinHeight = 80,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 0, 4),
                };

                var border = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x26, 0x30, 0x3A)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0x00, 0x00)),
                    Padding = new Thickness(6),
                    Child = mediaElement,
                };

                // Add to visual tree first, then set source and play
                parent.Children.Add(border);

                mediaElement.MediaOpened += (s, e) =>
                {
                    System.Diagnostics.Debug.WriteLine($"Video opened: {url}");
                    mediaElement.Play();
                };
                mediaElement.MediaEnded += (s, e) =>
                {
                    mediaElement.Position = TimeSpan.Zero;
                    mediaElement.Play();
                };
                mediaElement.MediaFailed += (s, e) =>
                {
                    System.Diagnostics.Debug.WriteLine($"Video failed: {e.ErrorException?.Message}");
                    Dispatcher.Invoke(() =>
                    {
                        border.Child = new TextBlock
                        {
                            Text = $"[Video failed to load: {url}]",
                            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x66, 0x66)),
                            FontStyle = FontStyles.Italic,
                            TextWrapping = TextWrapping.Wrap,
                            Padding = new Thickness(4),
                        };
                    });
                };

                mediaElement.Source = new Uri(url, UriKind.Absolute);
                mediaElement.Play();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading video: {ex.Message}");
                parent.Children.Add(new TextBlock
                {
                    Text = $"[Video: {url}]",
                    Foreground = Brushes.Gray,
                    FontStyle = FontStyles.Italic,
                });
            }
        }

        private void RenderPollSection(StackPanel parent, OrderSection section, BmsOrder order)
        {
            if (section.PollOptions == null || section.PollOptions.Count == 0) return;

            var totalVotes = section.PollOptions.Sum(p => p.VoteCount);
            var pollPanel = new StackPanel();

            foreach (var option in section.PollOptions)
            {
                var pct = totalVotes > 0 ? (double)option.VoteCount / totalVotes : 0;

                var optionRow = new Grid { Margin = new Thickness(0, 0, 0, 4), Height = 32 };
                optionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Background bar
                var barBg = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x50, 0x00, 0x00, 0x00)),
                    CornerRadius = new CornerRadius(4),
                };
                optionRow.Children.Add(barBg);

                // Fill bar
                var barFill = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x66, 0xD4, 0xAF, 0x37)),
                    CornerRadius = new CornerRadius(4),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Width = 0, // Will be set on render
                };
                optionRow.Children.Add(barFill);

                // Text + votes overlay
                var textPanel = new DockPanel { Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
                var voteLabel = new TextBlock
                {
                    Text = $"{option.VoteCount} ({pct:P0})",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                DockPanel.SetDock(voteLabel, Dock.Right);
                textPanel.Children.Add(voteLabel);

                var optionText = new TextBlock
                {
                    Text = option.Text,
                    Foreground = Brushes.White,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                textPanel.Children.Add(optionText);
                optionRow.Children.Add(textPanel);

                var vm = DataContext as MainViewModel;
                var voterId = vm?.CurrentVoterId;
                var hasVotedAnyOption = !string.IsNullOrWhiteSpace(voterId)
                    && section.PollOptions.Any(o => o.VoterIds?.Contains(voterId) == true);
                var hasVotedThisOption = !string.IsNullOrWhiteSpace(voterId)
                    && option.VoterIds?.Contains(voterId) == true;
                var isOptionLocked = section.AllowMultipleChoice ? hasVotedThisOption : hasVotedAnyOption;

                // Hover/click
                optionRow.Cursor = isOptionLocked ? System.Windows.Input.Cursors.Arrow : System.Windows.Input.Cursors.Hand;
                if (isOptionLocked)
                {
                    barBg.Background = new SolidColorBrush(Color.FromArgb(0x50, 0x00, 0x00, 0x00));
                    optionRow.Opacity = 0.85;
                }
                else
                {
                    optionRow.MouseEnter += (s, e) => barBg.Background = new SolidColorBrush(Color.FromArgb(0x66, 0x00, 0x00, 0x00));
                    optionRow.MouseLeave += (s, e) => barBg.Background = new SolidColorBrush(Color.FromArgb(0x50, 0x00, 0x00, 0x00));
                }

                var capturedOption = option;
                optionRow.MouseLeftButtonDown += async (s, e) =>
                {
                    if (isOptionLocked)
                        return;

                    var vm = DataContext as MainViewModel;
                    if (vm != null)
                    {
                        await vm.VotePollAsync(order.Id, section.Id, capturedOption.Id);
                        // Refresh will happen via SignalR
                    }
                };

                // Set bar width after layout
                optionRow.SizeChanged += (s, e) =>
                {
                    barFill.Width = optionRow.ActualWidth * pct;
                };

                pollPanel.Children.Add(optionRow);
            }

            // Total votes label
            pollPanel.Children.Add(new TextBlock
            {
                Text = $"{totalVotes} total vote{(totalVotes != 1 ? "s" : "")}",
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                FontSize = 10,
                Margin = new Thickness(0, 4, 0, 0),
            });

            parent.Children.Add(pollPanel);
        }

        private void RenderChecklistSection(StackPanel parent, OrderSection section, BmsOrder order)
        {
            if (section.ChecklistItems == null || section.ChecklistItems.Count == 0) return;

            var checkPanel = new StackPanel();

            foreach (var item in section.ChecklistItems)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };

                var cb = new CheckBox
                {
                    IsChecked = item.IsChecked,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };

                var textBlock = new TextBlock
                {
                    Text = item.Text,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                // Apply checked style
                ApplyChecklistStyle(textBlock, item.IsChecked);

                var capturedItem = item;
                cb.Checked += async (s, e) =>
                {
                    ApplyChecklistStyle(textBlock, true);
                    var vm = DataContext as MainViewModel;
                    if (vm != null)
                        await vm.ToggleChecklistAsync(order.Id, section.Id, capturedItem.Id);
                };
                cb.Unchecked += async (s, e) =>
                {
                    ApplyChecklistStyle(textBlock, false);
                    var vm = DataContext as MainViewModel;
                    if (vm != null)
                        await vm.ToggleChecklistAsync(order.Id, section.Id, capturedItem.Id);
                };

                row.Children.Add(cb);
                row.Children.Add(textBlock);
                checkPanel.Children.Add(row);
            }

            parent.Children.Add(checkPanel);
        }

        private void ApplyChecklistStyle(TextBlock textBlock, bool isChecked)
        {
            if (isChecked)
            {
                textBlock.TextDecorations = TextDecorations.Strikethrough;
                textBlock.Foreground = new SolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)); // 50% opacity white
            }
            else
            {
                textBlock.TextDecorations = null;
                textBlock.Foreground = Brushes.White;
            }
        }

        // ──── Legacy Content ──────────────────────────────────────────

        private void LoadLegacyContent(string? xamlContent)
        {
            try
            {
                if (string.IsNullOrEmpty(xamlContent))
                {
                    OrderContent.Document = new FlowDocument(
                        new Paragraph(new Run("Select a faction from BMS Options to view orders"))
                        { Foreground = Brushes.Gray });
                    SetDocumentStyle(OrderContent.Document);
                    return;
                }

                try
                {
                    var doc = new FlowDocument();
                    var range = new TextRange(doc.ContentStart, doc.ContentEnd);
                    using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xamlContent));
                    range.Load(stream, DataFormats.Xaml);
                    SetDocumentStyle(doc);
                    OrderContent.Document = doc;
                }
                catch
                {
                    var doc = new FlowDocument(new Paragraph(new Run(xamlContent)));
                    SetDocumentStyle(doc);
                    OrderContent.Document = doc;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading content: {ex.Message}");
                var doc = new FlowDocument(new Paragraph(new Run(xamlContent ?? "Error loading content")));
                SetDocumentStyle(doc);
                OrderContent.Document = doc;
            }
        }

        private static void SetDocumentStyle(FlowDocument doc)
        {
            doc.Foreground = Brushes.White;
            doc.Background = Brushes.Transparent;
            doc.PagePadding = new Thickness(10);
            doc.FontSize = 12;
            doc.FontFamily = new FontFamily("Segoe UI");
        }
    }
}
