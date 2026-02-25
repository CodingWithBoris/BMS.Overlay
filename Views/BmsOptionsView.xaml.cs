using System.Windows.Controls;
using System.Windows.Input;
using BMS.Overlay.ViewModels;

namespace BMS.Overlay.Views
{
    public partial class BmsOptionsView : Page
    {
        private bool _isInitializing = true;
        private bool _isProgrammaticSelectionChange;
        private string? _lastAuthenticatedFactionId;
        private bool _isCapturingToggleKey;

        public BmsOptionsView()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                if (DataContext is MainViewModel vm)
                {
                    if (!string.IsNullOrWhiteSpace(vm.SelectedFactionId) &&
                        vm.AuthenticatedFactionIds.Contains(vm.SelectedFactionId))
                    {
                        _lastAuthenticatedFactionId = vm.SelectedFactionId;
                    }

                    // Load current toggle key from settings
                    var settings = vm.GetSettings();
                    KeyToggleBox.Text = settings.KeyToggleOverlay;

                    // Load current overlay width
                    WidthInput.Text = $"{settings.OverlayWidth:0}";

                    // Load JTAC mode
                    ApplyJtacVisuals(settings.JtacMode);
                }

                _isInitializing = false;
            };
        }

        private void KeyToggleBox_GotFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            _isCapturingToggleKey = true;
            KeyToggleBox.Text = "Press a key...";
        }

        private void KeyToggleBox_LostFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            _isCapturingToggleKey = false;
            // If user clicked away without pressing a key, restore current value
            if (KeyToggleBox.Text == "Press a key..." && DataContext is MainViewModel vm)
            {
                var settings = vm.GetSettings();
                KeyToggleBox.Text = settings.KeyToggleOverlay;
            }
        }

        private void KeyToggleBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_isCapturingToggleKey) return;

            e.Handled = true;
            _isCapturingToggleKey = false;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            // Don't allow modifier-only keys
            if (key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LWin || key == Key.RWin)
                return;

            KeyToggleBox.Text = key.ToString();

            if (DataContext is MainViewModel vm)
            {
                vm.UpdateToggleKey(key.ToString());
            }

            // Move focus away
            FactionCombo.Focus();
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
            var goldBorder = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#D4AF37"));
            var goldText = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E3C341"));
            var dimBorder = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2E3A46"));
            var dimText = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#7A8290"));
            var activeBg = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2A2315"));
            var inactiveBg = System.Windows.Media.Brushes.Transparent;

            JtacOnBorder.BorderBrush = jtacOn ? goldBorder : dimBorder;
            JtacOnBorder.Background = jtacOn ? activeBg : inactiveBg;
            JtacOnText.Foreground = jtacOn ? goldText : dimText;

            JtacOffBorder.BorderBrush = !jtacOn ? goldBorder : dimBorder;
            JtacOffBorder.Background = !jtacOn ? activeBg : inactiveBg;
            JtacOffText.Foreground = !jtacOn ? goldText : dimText;
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
