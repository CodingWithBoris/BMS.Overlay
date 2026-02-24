using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using BMS.Overlay.Services;
using BMS.Shared.Models;

namespace BMS.Overlay.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly ApiService _apiService;
        private readonly SettingsService _settingsService;

        private List<BmsOrder> _orders = new();
        private List<FactionInfo> _factions = new();
        private int _currentOrderIndex = 0;
        private string _currentTab = "BMS";
        private BmsOrder? _currentOrder;
        private string? _selectedFactionId;
        private string? _selectedRoleId;
        private string _defaultMessage = "Welcome to BMS Overlay\n\nNo faction selected. Please select a faction from the BMS Options tab.";

        public ObservableCollection<FactionInfo> Factions { get; } = new();
        public ObservableCollection<BmsOrder> Orders { get; } = new();

        public MainViewModel(ApiService apiService, SettingsService settingsService)
        {
            _apiService = apiService;
            _settingsService = settingsService;
        }

        public string CurrentTab
        {
            get => _currentTab;
            set
            {
                if (_currentTab != value)
                {
                    _currentTab = value;
                    OnPropertyChanged();
                }
            }
        }

        public BmsOrder? CurrentOrder
        {
            get => _currentOrder;
            set
            {
                if (_currentOrder != value)
                {
                    _currentOrder = value;
                    OnPropertyChanged();
                }
            }
        }

        public int CurrentOrderIndex
        {
            get => _currentOrderIndex;
            set
            {
                if (_currentOrderIndex != value)
                {
                    _currentOrderIndex = value;
                    OnPropertyChanged();
                    UpdateCurrentOrder();
                }
            }
        }

        public string? SelectedFactionId
        {
            get => _selectedFactionId;
            set
            {
                if (_selectedFactionId != value)
                {
                    _selectedFactionId = value;
                    OnPropertyChanged();
                }
            }
        }

        public string? SelectedRoleId
        {
            get => _selectedRoleId;
            set
            {
                if (_selectedRoleId != value)
                {
                    _selectedRoleId = value;
                    OnPropertyChanged();
                }
            }
        }

        public string DefaultMessage
        {
            get => _defaultMessage;
            set
            {
                if (_defaultMessage != value)
                {
                    _defaultMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        public int TotalOrders => _orders.Count;

        public async Task LoadFactionsAsync()
        {
            try
            {
                _factions = await _apiService.GetFactionsAsync();
                Factions.Clear();
                foreach (var faction in _factions)
                {
                    Factions.Add(faction);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading factions: {ex.Message}");
            }
        }

        public async Task LoadOrdersAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(SelectedFactionId))
                {
                    _orders.Clear();
                    Orders.Clear();
                    CurrentOrder = null;
                    return;
                }

                _orders = await _apiService.GetOrdersAsync(SelectedFactionId);
                Orders.Clear();
                foreach (var order in _orders.OrderBy(o => o.OrderIndex))
                {
                    Orders.Add(order);
                }

                CurrentOrderIndex = 0;
                UpdateCurrentOrder();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading orders: {ex.Message}");
            }
        }

        public async Task SelectFactionAsync(FactionInfo faction)
        {
            SelectedFactionId = faction.Id;
            await LoadOrdersAsync();
        }

        public void NextOrder()
        {
            if (_orders.Count == 0) return;
            CurrentOrderIndex = (CurrentOrderIndex + 1) % _orders.Count;
        }

        public void PreviousOrder()
        {
            if (_orders.Count == 0) return;
            CurrentOrderIndex = CurrentOrderIndex == 0 ? _orders.Count - 1 : CurrentOrderIndex - 1;
        }

        private void UpdateCurrentOrder()
        {
            if (_orders.Count > 0 && CurrentOrderIndex >= 0 && CurrentOrderIndex < _orders.Count)
            {
                CurrentOrder = _orders[CurrentOrderIndex];
            }
            else
            {
                CurrentOrder = null;
            }
        }
    }

    public class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
