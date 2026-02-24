using System.Windows;

namespace BMS.Overlay.Views
{
    /// <summary>
    /// Simple password prompt dialog for faction access
    /// </summary>
    public partial class PasswordPromptWindow : Window
    {
        public PasswordPromptWindow()
        {
            InitializeComponent();
        }

        public string? Password { get; private set; }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Password = PasswordBox.Password;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
