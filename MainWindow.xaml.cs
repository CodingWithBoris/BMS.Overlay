using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using BMS.Overlay.Services;
using BMS.Overlay.ViewModels;
using BMS.Shared.Models;

namespace BMS.Overlay
{
    public partial class MainWindow : Window
    {
        private readonly ApiService _apiService;
        private readonly SignalRService _signalRService;
        private HotkeyService? _hotkeyService;
        private readonly SettingsService _settingsService;
        private MainViewModel? _viewModel;

        private const string ApiBaseUrl = "https://bms-production-f22e.up.railway.app/api/v1";

        public MainWindow()
        {
            InitializeComponent();

            _apiService = new ApiService(ApiBaseUrl);
            _signalRService = new SignalRService(ApiBaseUrl);
            _settingsService = new SettingsService();
            
            // Set window properties
            Height = SystemParameters.PrimaryScreenHeight;
            Top = 0;
            Left = 0;

            // Initialize services
            Loaded += MainWindow_Loaded;
        }

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
                System.Diagnostics.Debug.WriteLine("Settings loaded successfully");

                // Initialize ViewModel
                System.Diagnostics.Debug.WriteLine("Creating ViewModel...");
                _viewModel = new MainViewModel(_apiService, _settingsService, _signalRService);
                DataContext = _viewModel;
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

                // Setup hotkeys
                try
                {
                    System.Diagnostics.Debug.WriteLine("Setting up hotkeys...");
                    _hotkeyService = new HotkeyService(this);
                    _hotkeyService.Register(Key.Left, () => OnPrev_Click(null, null), ModifierKeys.None);
                    _hotkeyService.Register(Key.Right, () => OnNext_Click(null, null), ModifierKeys.None);
                    _hotkeyService.Register(Key.F9, () => OnMinimize_Click(null, null), ModifierKeys.None);
                    System.Diagnostics.Debug.WriteLine("Hotkeys registered successfully");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Hotkey initialization failed: {ex.Message}");
                }

                // Initialize UI
                try
                {
                    System.Diagnostics.Debug.WriteLine("Loading UI...");
                    LoadBmsTab();
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
            BmsTabButton.IsEnabled = false;
            OptionsTabButton.IsEnabled = true;
            PrevButton.Visibility = Visibility.Visible;
            NextButton.Visibility = Visibility.Visible;

            ContentFrame.Navigate(new Views.BmsTabView { DataContext = _viewModel });
        }

        private void LoadOptionsTab()
        {
            if (_viewModel == null) return;

            _viewModel.CurrentTab = "Options";
            BmsTabButton.IsEnabled = true;
            OptionsTabButton.IsEnabled = false;
            PrevButton.Visibility = Visibility.Collapsed;
            NextButton.Visibility = Visibility.Collapsed;

            ContentFrame.Navigate(new Views.BmsOptionsView { DataContext = _viewModel });
        }

        private void OnPrev_Click(object? sender, RoutedEventArgs? e)
        {
            if (_viewModel == null) return;
            _viewModel.PreviousOrder();
        }

        private void OnNext_Click(object? sender, RoutedEventArgs? e)
        {
            if (_viewModel == null) return;
            _viewModel.NextOrder();
        }

        private void OnMinimize_Click(object? sender, RoutedEventArgs? e)
        {
            MainPanel.Visibility = Visibility.Collapsed;
            RestoreBar.Visibility = Visibility.Visible;
        }

        private void OnRestore_Click(object sender, RoutedEventArgs e)
        {
            MainPanel.Visibility = Visibility.Visible;
            RestoreBar.Visibility = Visibility.Collapsed;
        }

        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _hotkeyService?.Dispose();
            await _signalRService.DisposeAsync();
        }
    }
}
