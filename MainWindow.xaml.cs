using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using BMS.Overlay.Services;
using BMS.Overlay.ViewModels;
using BMS.Overlay.Views;
using BMS.Shared.Models;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace BMS.Overlay
{
    public partial class MainWindow : Window
    {
        private readonly ApiService _apiService;
        private readonly SignalRService _signalRService;
        private KeyboardHookService? _keyboardHookService;
        private readonly SettingsService _settingsService;
        private readonly NotepadService _notepadService;
        private readonly SharedNotepadService _sharedNotepadService;
        private MainViewModel? _viewModel;
        private NotepadWindow? _notepadWindow;

        private const string ApiBaseUrl = "https://bms-production-f22e.up.railway.app/api/v1";
        private const double MinimizedHeight = 44;

        // Positioning percentages
        private const double DefaultTopPercent = 0.15;
        private const double JtacTopPercent = 0.20;
        private const double JtacLeftPercent = 0.05;
        private bool _jtacMode = false;

        // Roblox detection + M-key toggle state
        private DispatcherTimer? _robloxCheckTimer;
        private bool _isHiddenByUser = false;      // M key toggled off
        private bool _robloxIsForeground = false;   // Roblox is the active window

        public MainWindow()
        {
            InitializeComponent();

            _apiService = new ApiService(ApiBaseUrl);
            _signalRService = new SignalRService(ApiBaseUrl);
            _settingsService = new SettingsService();
            _notepadService = new NotepadService();
            _sharedNotepadService = new SharedNotepadService(ApiBaseUrl);

            // Set window properties — position below the Roblox game nav bar
            ApplyPosition();

            SourceInitialized += (_, _) =>
            {
                MakeClickThrough(false);
            };

            // Initialize services
            Loaded += MainWindow_Loaded;
        }

        // ──────────────────────────────────────────
        // Roblox Detection & Visibility
        // ──────────────────────────────────────────
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        private void StartRobloxDetection()
        {
            _robloxCheckTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _robloxCheckTimer.Tick += (_, _) => CheckRobloxForeground();
            _robloxCheckTimer.Start();

            // Do an initial check
            CheckRobloxForeground();
        }

        private void CheckRobloxForeground()
        {
            bool wasRoblox = _robloxIsForeground;
            _robloxIsForeground = IsRobloxOrOverlayForeground();

            if (wasRoblox != _robloxIsForeground)
                UpdateOverlayVisibility();
        }

        private bool IsRobloxOrOverlayForeground()
        {
            try
            {
                var fgWindow = GetForegroundWindow();
                if (fgWindow == IntPtr.Zero) return false;

                // Check if our own overlay window is foreground
                var overlayHandle = new WindowInteropHelper(this).Handle;
                if (overlayHandle != IntPtr.Zero && fgWindow == overlayHandle)
                    return true;

                // Check if the notepad window is foreground
                if (_notepadWindow != null && _notepadWindow.IsLoaded)
                {
                    var notepadHandle = new WindowInteropHelper(_notepadWindow).Handle;
                    if (notepadHandle != IntPtr.Zero && fgWindow == notepadHandle)
                        return true;
                }

                GetWindowThreadProcessId(fgWindow, out uint processId);
                if (processId == 0) return false;

                var process = System.Diagnostics.Process.GetProcessById((int)processId);
                var name = process.ProcessName.ToLowerInvariant();

                // Roblox process names
                return name == "robloxplayerbeta" ||
                       name == "robloxplayerlauncher" ||
                       name.Contains("roblox");
            }
            catch
            {
                return false;
            }
        }

        private void ToggleVisibility()
        {
            _isHiddenByUser = !_isHiddenByUser;
            UpdateOverlayVisibility();
        }

        private void UpdateOverlayVisibility()
        {
            // Only show when: Roblox is foreground AND user hasn't pressed M to hide
            // Use Opacity instead of Visibility so the window handle stays alive
            // for global hotkey processing (WM_HOTKEY requires a valid HWND)
            if (_robloxIsForeground && !_isHiddenByUser)
            {
                if (Opacity < 1.0)
                {
                    Opacity = 1.0;
                    IsHitTestVisible = true;
                    System.Diagnostics.Debug.WriteLine("[Overlay] Shown (Roblox active)");
                }
            }
            else
            {
                if (Opacity > 0.0)
                {
                    Opacity = 0.0;
                    IsHitTestVisible = false;
                    var reason = _isHiddenByUser ? "user toggled off (M)" : "Roblox not active";
                    System.Diagnostics.Debug.WriteLine($"[Overlay] Hidden ({reason})");
                }
            }

            // Sync notepad window visibility
            if (_notepadWindow != null && _notepadWindow.IsLoaded)
            {
                _notepadWindow.SetRobloxVisible(_robloxIsForeground && !_isHiddenByUser);
            }
        }

        /// <summary>
        /// Makes the window non-interactive (click-through) or interactive.
        /// Not currently used but available for future use.
        /// </summary>
        private void MakeClickThrough(bool clickThrough)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            const int GWL_EXSTYLE = -20;
            const int WS_EX_TRANSPARENT = 0x00000020;

            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            if (clickThrough)
                exStyle |= WS_EX_TRANSPARENT;
            else
                exStyle &= ~WS_EX_TRANSPARENT;
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
        }

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await InitializeAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Window loaded initialization error: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"Window initialization error: {ex.Message}\n\nStack:\n{ex.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task InitializeAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Starting initialization...");

                // Load settings
                System.Diagnostics.Debug.WriteLine("Loading settings...");
                await _settingsService.LoadAsync();
                var settings = _settingsService.GetSettings();

                if (string.IsNullOrWhiteSpace(settings.VoterId))
                {
                    settings.VoterId = Guid.NewGuid().ToString();
                    _settingsService.UpdateSettings(settings);
                    await _settingsService.SaveAsync();
                }

                _apiService.SetVoterId(settings.VoterId);
                System.Diagnostics.Debug.WriteLine("Settings loaded successfully");

                // Apply saved overlay width
                Width = settings.OverlayWidth;
                MaxWidth = settings.OverlayWidth;

                // Apply JTAC mode positioning
                _jtacMode = settings.JtacMode;
                ApplyPosition();

                // Initialize ViewModel
                System.Diagnostics.Debug.WriteLine("Creating ViewModel...");
                _viewModel = new MainViewModel(_apiService, _settingsService, _signalRService);
                DataContext = _viewModel;
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;
                System.Diagnostics.Debug.WriteLine("ViewModel created and DataContext set");

                // Connect SignalR for real-time updates
                System.Diagnostics.Debug.WriteLine("Connecting SignalR...");
                await _signalRService.ConnectAsync();
                System.Diagnostics.Debug.WriteLine($"SignalR connected: {_signalRService.IsConnected}");

                // Load data
                System.Diagnostics.Debug.WriteLine("Loading factions...");
                await _viewModel.LoadFactionsAsync();
                System.Diagnostics.Debug.WriteLine("Factions loaded");

                System.Diagnostics.Debug.WriteLine("Loading orders...");
                await _viewModel.LoadOrdersAsync();
                System.Diagnostics.Debug.WriteLine("Orders loaded");

                // Setup low-level keyboard hook (passthrough — does NOT steal keys from Roblox)
                try
                {
                    _keyboardHookService = new KeyboardHookService();

                    // Register all keybinds from settings
                    RegisterAllKeybinds();

                    System.Diagnostics.Debug.WriteLine($"Keyboard hook registered (toggle: {settings.KeyToggleOverlay})");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Keyboard hook initialization failed: {ex.Message}");
                }

                // Start Roblox foreground detection
                StartRobloxDetection();

                // Initialize UI
                try
                {
                    System.Diagnostics.Debug.WriteLine("Loading UI...");
                    LoadBmsTab();
                    UpdateOrderBoundaryButtonStyles();
                    System.Diagnostics.Debug.WriteLine("UI loaded successfully");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"UI initialization failed: {ex.Message}\n{ex.StackTrace}");
                    MessageBox.Show($"UI initialization error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                System.Diagnostics.Debug.WriteLine("Initialization completed successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Initialization error: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"Initialization error: {ex.Message}\n\nStack trace:\n{ex.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnBmsTab_Click(object sender, RoutedEventArgs e)
        {
            LoadBmsTab();
        }

        private void OnOptionsTab_Click(object sender, RoutedEventArgs e)
        {
            LoadOptionsTab();
        }

        private void LoadBmsTab()
        {
            if (_viewModel == null) return;

            _viewModel.CurrentTab = "BMS";
            BmsTabButton.IsEnabled = true;
            OptionsTabButton.IsEnabled = true;
            BmsTabButton.Style = (Style)FindResource("TacticalGoldButton");
            OptionsTabButton.Style = (Style)FindResource("TacticalDarkButton");
            PrevButton.Visibility = Visibility.Visible;
            NextButton.Visibility = Visibility.Visible;
            Grid.SetColumn(MinimizeButton, 2);
            Grid.SetColumnSpan(MinimizeButton, 1);
            UpdateOrderBoundaryButtonStyles();

            ContentFrame.Navigate(new Views.BmsTabView { DataContext = _viewModel });
        }

        private void LoadOptionsTab()
        {
            if (_viewModel == null) return;

            _viewModel.CurrentTab = "Options";
            BmsTabButton.IsEnabled = true;
            OptionsTabButton.IsEnabled = true;
            BmsTabButton.Style = (Style)FindResource("TacticalDarkButton");
            OptionsTabButton.Style = (Style)FindResource("TacticalGoldButton");
            PrevButton.Visibility = Visibility.Collapsed;
            NextButton.Visibility = Visibility.Collapsed;
            Grid.SetColumn(MinimizeButton, 0);
            Grid.SetColumnSpan(MinimizeButton, 5);
            PrevButton.Style = (Style)FindResource("TacticalDarkButton");
            NextButton.Style = (Style)FindResource("TacticalDarkButton");

            ContentFrame.Navigate(new Views.BmsOptionsView { DataContext = _viewModel });
        }

        private void OnPrev_Click(object? sender, RoutedEventArgs? e)
        {
            if (_viewModel == null) return;
            _viewModel.PreviousOrder();
            UpdateOrderBoundaryButtonStyles();
        }

        private void OnNext_Click(object? sender, RoutedEventArgs? e)
        {
            if (_viewModel == null) return;
            _viewModel.NextOrder();
            UpdateOrderBoundaryButtonStyles();
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentOrder) ||
                e.PropertyName == nameof(MainViewModel.CurrentOrderIndex) ||
                e.PropertyName == nameof(MainViewModel.TotalOrders))
            {
                Dispatcher.Invoke(UpdateOrderBoundaryButtonStyles);
            }
        }

        private void UpdateOrderBoundaryButtonStyles()
        {
            if (_viewModel == null)
                return;

            if (_viewModel.CurrentTab != "BMS")
                return;

            PrevButton.Style = (Style)FindResource("TacticalDarkButton");
            NextButton.Style = (Style)FindResource("TacticalDarkButton");

            if (_viewModel.TotalOrders <= 0)
                return;

            if (_viewModel.CurrentOrderIndex == 0)
                PrevButton.Style = (Style)FindResource("TacticalGoldButton");

            if (_viewModel.CurrentOrderIndex == _viewModel.TotalOrders - 1)
                NextButton.Style = (Style)FindResource("TacticalGoldButton");
        }

        private void OnMinimize_Click(object? sender, RoutedEventArgs? e)
        {
            MainPanel.Visibility = Visibility.Collapsed;
            RestoreBar.Visibility = Visibility.Visible;

            Height = MinimizedHeight;
            Top = SystemParameters.PrimaryScreenHeight - MinimizedHeight;
        }

        private void OnRestore_Click(object sender, RoutedEventArgs e)
        {
            MainPanel.Visibility = Visibility.Visible;
            RestoreBar.Visibility = Visibility.Collapsed;

            ApplyPosition();
        }

        /// <summary>
        /// Apply overlay position based on JTAC mode.
        /// </summary>
        public void ApplyPosition()
        {
            double topOffset = Math.Round(SystemParameters.PrimaryScreenHeight * (_jtacMode ? JtacTopPercent : DefaultTopPercent));
            double leftOffset = _jtacMode ? Math.Round(SystemParameters.PrimaryScreenWidth * JtacLeftPercent) : 0;

            Height = SystemParameters.PrimaryScreenHeight - topOffset;
            Top = topOffset;
            Left = leftOffset;
        }

        /// <summary>
        /// Update JTAC mode and reposition the overlay.
        /// </summary>
        public void SetJtacMode(bool enabled)
        {
            _jtacMode = enabled;
            ApplyPosition();
        }

        private void OnNotepad_Click(object sender, RoutedEventArgs e)
        {
            if (_notepadWindow == null || !_notepadWindow.IsLoaded)
            {
                _notepadWindow = new NotepadWindow(_notepadService, _sharedNotepadService, _signalRService, _settingsService)
                {
                    Left = Left + Width + 10,
                    Top = Top
                };
                _notepadWindow.Closed += (_, _) => _notepadWindow = null;
                _notepadWindow.Show();
            }
            else
            {
                if (_notepadWindow.Visibility == Visibility.Visible)
                {
                    _notepadWindow.Activate();
                }
                else
                {
                    _notepadWindow.Show();
                    _notepadWindow.Activate();
                }
            }
        }

        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _robloxCheckTimer?.Stop();
            _keyboardHookService?.Dispose();

            if (_viewModel != null)
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;

            // Close notepad window if open
            if (_notepadWindow != null && _notepadWindow.IsLoaded)
            {
                _notepadWindow.Close();
            }

            await _signalRService.DisposeAsync();
        }

        /// <summary>
        /// Re-register ALL keybinds from current settings on the keyboard hook.
        /// </summary>
        public void RegisterAllKeybinds()
        {
            if (_keyboardHookService == null) return;

            _keyboardHookService.UnregisterAll();

            var settings = _settingsService.GetSettings();

            void TryRegister(string keyName, Action handler)
            {
                if (Enum.TryParse<Key>(keyName, true, out var k))
                    _keyboardHookService.Register(k, handler);
                else
                    System.Diagnostics.Debug.WriteLine($"[Overlay] Invalid key name: {keyName}");
            }

            TryRegister(settings.KeyPrevious, () => OnPrev_Click(null, null));
            TryRegister(settings.KeyNext, () => OnNext_Click(null, null));
            TryRegister(settings.KeyMinimize, () => OnMinimize_Click(null, null));
            // Only register restore if it's a different key from minimize
            if (!string.Equals(settings.KeyRestore, settings.KeyMinimize, StringComparison.OrdinalIgnoreCase))
                TryRegister(settings.KeyRestore, () => OnRestore_Click(null!, null!));
            TryRegister(settings.KeyToggleOverlay, () => ToggleVisibility());

            System.Diagnostics.Debug.WriteLine($"[Overlay] All keybinds registered (prev={settings.KeyPrevious}, next={settings.KeyNext}, min={settings.KeyMinimize}, restore={settings.KeyRestore}, toggle={settings.KeyToggleOverlay})");
        }

        /// <summary>
        /// Register (or re-register) the toggle overlay key on the keyboard hook.
        /// Kept for backwards compatibility — now delegates to RegisterAllKeybinds.
        /// </summary>
        public void RegisterToggleKey(string keyName)
        {
            RegisterAllKeybinds();
        }
    }
}
