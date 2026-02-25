using System.Windows;
using System.Windows.Controls;

namespace BMS.Overlay.Views
{
    /// <summary>
    /// Simple password prompt dialog for faction access
    /// </summary>
    public partial class PasswordPromptWindow : Window
    {
        private bool _isPasswordVisible;

        public PasswordPromptWindow()
        {
            InitializeComponent();
        }

        public string? Password { get; private set; }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Password = _isPasswordVisible ? PlainTextPasswordBox.Text : PasswordBox.Password;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void TogglePasswordButton_Click(object sender, RoutedEventArgs e)
        {
            _isPasswordVisible = !_isPasswordVisible;

            if (_isPasswordVisible)
            {
                PlainTextPasswordBox.Text = PasswordBox.Password;
                PasswordBox.Visibility = Visibility.Collapsed;
                PlainTextPasswordBox.Visibility = Visibility.Visible;
                TogglePasswordButton.Content = "🙈";
                TogglePasswordButton.ToolTip = "Hide password";
                PlainTextPasswordBox.Focus();
                PlainTextPasswordBox.CaretIndex = PlainTextPasswordBox.Text.Length;
            }
            else
            {
                PasswordBox.Password = PlainTextPasswordBox.Text;
                PlainTextPasswordBox.Visibility = Visibility.Collapsed;
                PasswordBox.Visibility = Visibility.Visible;
                TogglePasswordButton.Content = "👁";
                TogglePasswordButton.ToolTip = "Show password";
                PasswordBox.Focus();
            }
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isPasswordVisible)
                return;

            if (PlainTextPasswordBox != null)
                PlainTextPasswordBox.Text = PasswordBox.Password;
        }

        private void PlainTextPasswordBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isPasswordVisible)
                return;

            if (PasswordBox != null)
                PasswordBox.Password = PlainTextPasswordBox.Text;
        }
    }
}
