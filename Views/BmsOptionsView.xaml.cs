using System.Windows.Controls;
using BMS.Overlay.ViewModels;

namespace BMS.Overlay.Views
{
    public partial class BmsOptionsView : Page
    {
        public BmsOptionsView()
        {
            InitializeComponent();
        }

        private async void FactionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is MainViewModel vm && FactionCombo.SelectedItem is BMS.Shared.Models.FactionInfo faction)
            {
                // Prompt for faction password
                var passwordWindow = new PasswordPromptWindow();
                if (passwordWindow.ShowDialog() == true)
                {
                    await vm.SelectFactionAsync(faction);
                }
            }
        }
    }
}
