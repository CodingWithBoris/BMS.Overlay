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
        private readonly SignalRService? _signalRService;

        private List<BmsOrder> _orders = new();
        private List<BmsOrder> _allOrders = new(); // unfiltered orders from API
        private List<FactionInfo> _factions = new();
        private int _currentOrderIndex = 0;
        private string _currentTab = "BMS";
        private BmsOrder? _currentOrder;
        private string? _selectedFactionId;
        private string? _selectedRoleId;
        private string _defaultMessage = "Welcome to BMS Overlay\n\nNo faction selected. Please select a faction from the BMS Options tab.";

        public ObservableCollection<FactionInfo> Factions { get; } = new();
        public ObservableCollection<BmsOrder> Orders { get; } = new();
        public ObservableCollection<RoleInfo> Roles { get; } = new();

        public MainViewModel(ApiService apiService, SettingsService settingsService, SignalRService? signalRService = null)
        {
            _apiService = apiService;
            _settingsService = settingsService;
            _signalRService = signalRService;

            // Subscribe to SignalR real-time updates
            if (_signalRService != null)
            {
                _signalRService.OnOrdersUpdated += OnOrdersUpdatedFromSignalR;
            }
        }

        private async void OnOrdersUpdatedFromSignalR(string factionId, string orderId, string action)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"SignalR event: OrdersUpdated for faction={factionId}, action={action}");

                // Only refresh if we're viewing the same faction
                if (string.IsNullOrEmpty(SelectedFactionId) ||
                    (!string.IsNullOrEmpty(factionId) && factionId != SelectedFactionId))
                {
                    System.Diagnostics.Debug.WriteLine($"Ignoring SignalR update: viewing {SelectedFactionId}, update for {factionId}");
                    return;
                }

                // Refresh orders from server
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    await LoadOrdersAsync();
                    System.Diagnostics.Debug.WriteLine($"Orders refreshed via SignalR (action={action})");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling SignalR update: {ex.Message}");
            }
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
                    // Re-filter orders when role changes
                    FilterOrdersByRole();
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
                    _allOrders.Clear();
                    _orders.Clear();
                    Orders.Clear();
                    CurrentOrder = null;
                    return;
                }

                _allOrders = await _apiService.GetOrdersAsync(SelectedFactionId);
                System.Diagnostics.Debug.WriteLine($"Loaded {_allOrders.Count} orders from API");
                FilterOrdersByRole();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading orders: {ex.Message}");
            }
        }

        private void FilterOrdersByRole()
        {
            // Filter orders: show orders matching selected role, or orders with no role assigned (visible to all)
            if (string.IsNullOrEmpty(SelectedRoleId) || SelectedRoleId == "__all__")
            {
                // No role filter - show all orders
                _orders = _allOrders.ToList();
            }
            else
            {
                _orders = _allOrders
                    .Where(o => string.IsNullOrEmpty(o.RoleId) || o.RoleId == SelectedRoleId)
                    .ToList();
            }

            Orders.Clear();
            foreach (var order in _orders.OrderBy(o => o.OrderIndex))
            {
                Orders.Add(order);
            }

            System.Diagnostics.Debug.WriteLine($"Filtered to {_orders.Count} orders (role={SelectedRoleId ?? "all"})");

            CurrentOrderIndex = 0;
            OnPropertyChanged(nameof(TotalOrders));
            UpdateCurrentOrder();
        }

        public async Task SelectFactionAsync(FactionInfo faction)
        {
            SelectedFactionId = faction.Id;

            // Populate roles for the selected faction
            Roles.Clear();
            Roles.Add(new RoleInfo { Id = "__all__", Name = "All Roles", IsDefault = false });
            if (faction.Roles != null)
            {
                foreach (var role in faction.Roles)
                {
                    Roles.Add(role);
                }
            }
            SelectedRoleId = "__all__";
            OnPropertyChanged(nameof(Roles));
            await LoadOrdersAsync();

            // Subscribe to real-time updates for this faction
            if (_signalRService != null)
            {
                await _signalRService.SubscribeToFactionAsync(faction.Id);
            }
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
