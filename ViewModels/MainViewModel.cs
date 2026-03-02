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
        private bool _isPopulatingRoles = false;
        private string _defaultMessage = "Welcome to BMS Overlay\n\nNo faction selected. Please select a faction from the BMS Options tab.";

        public ObservableCollection<FactionInfo> Factions { get; } = new();
        public ObservableCollection<BmsOrder> Orders { get; } = new();
        public ObservableCollection<RoleInfo> Roles { get; } = new();
        public ObservableCollection<VcMemberDto> VcRosterMembers { get; } = new();
        public string CurrentVoterId => _apiService.VoterId;
        public string ApiBaseUrl => _apiService.BaseUrl;
        public HashSet<string> AuthenticatedFactionIds { get; } = new();

        public MainViewModel(ApiService apiService, SettingsService settingsService, SignalRService? signalRService = null)
        {
            _apiService = apiService;
            _settingsService = settingsService;
            _signalRService = signalRService;

            // Subscribe to SignalR real-time updates
            if (_signalRService != null)
            {
                _signalRService.OnOrdersUpdated += OnOrdersUpdatedFromSignalR;
                _signalRService.OnVcRosterUpdated += OnVcRosterUpdatedFromSignalR;
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

        private async void OnVcRosterUpdatedFromSignalR(string factionId, string action, System.Text.Json.JsonElement data)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"SignalR event: VcRosterUpdated for faction={factionId}, action={action}");

                // Only refresh if we're viewing the same faction
                if (string.IsNullOrEmpty(SelectedFactionId) ||
                    (!string.IsNullOrEmpty(factionId) && factionId != SelectedFactionId))
                {
                    return;
                }

                // Refresh VC roster from server
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    await LoadVcRosterAsync();
                    System.Diagnostics.Debug.WriteLine($"VC roster refreshed via SignalR (action={action})");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling VC roster SignalR update: {ex.Message}");
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
                    // Re-filter orders when role changes (but not during role population)
                    if (!_isPopulatingRoles)
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

        public async Task LoadVcRosterAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(SelectedFactionId))
                {
                    VcRosterMembers.Clear();
                    return;
                }

                var rosters = await _apiService.GetVcRosterFullAsync(SelectedFactionId);
                var allMembers = new List<VcMemberDto>();

                foreach (var roster in rosters)
                {
                    allMembers.AddRange(roster.Members);
                }

                // Filter out hidden members
                var visibleMembers = allMembers.Where(m => !m.IsHidden).ToList();

                VcRosterMembers.Clear();
                foreach (var member in visibleMembers.OrderBy(m => m.Team).ThenBy(m => m.DisplayName))
                {
                    VcRosterMembers.Add(member);
                }

                System.Diagnostics.Debug.WriteLine($"Loaded {VcRosterMembers.Count} members to overlay (hidden {allMembers.Count - VcRosterMembers.Count})");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading VC roster: {ex.Message}");
            }
        }

        public async Task SelectFactionAsync(FactionInfo faction)
        {
            System.Diagnostics.Debug.WriteLine($"SelectFactionAsync called: {faction.Title} (ID: {faction.Id})");
            System.Diagnostics.Debug.WriteLine($"  Faction has {faction.Roles?.Count ?? 0} roles");
            if (faction.Roles != null)
            {
                foreach (var r in faction.Roles)
                {
                    System.Diagnostics.Debug.WriteLine($"    Role: {r.Name} (ID: {r.Id})");
                }
            }

            SelectedFactionId = faction.Id;
            AuthenticatedFactionIds.Add(faction.Id);

            // Populate roles for the selected faction
            _isPopulatingRoles = true;
            Roles.Clear();
            if (faction.Roles != null)
            {
                foreach (var role in faction.Roles)
                {
                    Roles.Add(role);
                }
            }
            System.Diagnostics.Debug.WriteLine($"  Roles collection now has {Roles.Count} items");
            var defaultRole = Roles.FirstOrDefault(r => r.IsDefault) ?? Roles.FirstOrDefault();
            SelectedRoleId = defaultRole?.Id;
            _isPopulatingRoles = false;
            OnPropertyChanged(nameof(Roles));
            await LoadOrdersAsync();
            await LoadVcRosterAsync();

            // Subscribe to real-time updates for this faction
            if (_signalRService != null)
            {
                await _signalRService.SubscribeToFactionAsync(faction.Id);
            }
        }

        public Task<bool> VerifyFactionPasswordAsync(string factionId, string password)
            => _apiService.VerifyFactionPasswordAsync(factionId, password);

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

        public async Task VotePollAsync(string orderId, string sectionId, string optionId)
        {
            if (string.IsNullOrEmpty(SelectedFactionId)) return;
            await _apiService.VotePollAsync(SelectedFactionId, orderId, sectionId, optionId);
            // SignalR will trigger a refresh via OrdersUpdated event
        }

        public async Task ToggleChecklistAsync(string orderId, string sectionId, string itemId)
        {
            if (string.IsNullOrEmpty(SelectedFactionId)) return;
            await _apiService.ToggleChecklistAsync(SelectedFactionId, orderId, sectionId, itemId);
            // SignalR will trigger a refresh via OrdersUpdated event
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
            OnPropertyChanged(nameof(CurrentObjectives));
        }

        public List<MissionObjective> CurrentObjectives => CurrentOrder?.Objectives ?? new();

        public async Task ToggleObjectiveAsync(string objectiveId)
        {
            if (string.IsNullOrEmpty(SelectedFactionId) || CurrentOrder == null) return;
            var orderId = CurrentOrder.Id;

            // Optimistic update
            var objective = CurrentOrder.Objectives?.FirstOrDefault(o => o.Id == objectiveId);
            if (objective != null)
            {
                objective.IsChecked = !objective.IsChecked;
                OnPropertyChanged(nameof(CurrentObjectives));
            }

            await _apiService.ToggleObjectiveAsync(SelectedFactionId, orderId, objectiveId);
            // SignalR will trigger a full refresh via OrdersUpdated event
        }

        // ──────────────────────────────────────────
        // Settings Access
        // ──────────────────────────────────────────
        public Settings GetSettings() => _settingsService.GetSettings();

        public double ContentFontSize => _settingsService.GetSettings().OverlayFontSize;

        public string FilterUsername(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                return displayName;

            var settings = _settingsService.GetSettings();
            if (!settings.FilterUsernames)
                return displayName;

            var filtered = displayName;

            // Remove prefix if set
            if (!string.IsNullOrEmpty(settings.UsernameFilterPrefix))
            {
                // Check if this is a separator-based filter (e.g., "remove before ' | '")
                if (settings.UsernameFilterPrefix.StartsWith("__SEPARATOR__", StringComparison.Ordinal))
                {
                    var separator = settings.UsernameFilterPrefix.Substring("__SEPARATOR__".Length);
                    var separatorIndex = filtered.IndexOf(separator, StringComparison.Ordinal);
                    if (separatorIndex >= 0)
                    {
                        filtered = filtered.Substring(separatorIndex + separator.Length);
                    }
                }
                else if (filtered.StartsWith(settings.UsernameFilterPrefix, StringComparison.Ordinal))
                {
                    filtered = filtered.Substring(settings.UsernameFilterPrefix.Length);
                }
            }

            // Remove suffix if set
            if (!string.IsNullOrEmpty(settings.UsernameFilterSuffix))
            {
                // Check if this is a separator-based filter (e.g., "remove after ' - '")
                if (settings.UsernameFilterSuffix.StartsWith("__SEPARATOR__", StringComparison.Ordinal))
                {
                    var separator = settings.UsernameFilterSuffix.Substring("__SEPARATOR__".Length);
                    var separatorIndex = filtered.IndexOf(separator, StringComparison.Ordinal);
                    if (separatorIndex >= 0)
                    {
                        filtered = filtered.Substring(0, separatorIndex);
                    }
                }
                else if (filtered.EndsWith(settings.UsernameFilterSuffix, StringComparison.Ordinal))
                {
                    filtered = filtered.Substring(0, filtered.Length - settings.UsernameFilterSuffix.Length);
                }
            }

            return filtered.Trim();
        }

        public void UpdateToggleKey(string keyName)
        {
            var settings = _settingsService.GetSettings();
            settings.KeyToggleOverlay = keyName;
            _settingsService.UpdateSettings(settings);
            _ = _settingsService.SaveAsync();

            // Tell MainWindow to re-register all hooks
            if (System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.RegisterAllKeybinds();
            }
        }

        /// <summary>
        /// Update any keybind setting by name and re-register all hooks.
        /// </summary>
        public void UpdateKeybind(string settingName, string keyName)
        {
            var settings = _settingsService.GetSettings();

            switch (settingName)
            {
                case "KeyPrevious": settings.KeyPrevious = keyName; break;
                case "KeyNext": settings.KeyNext = keyName; break;
                case "KeyMinimize": settings.KeyMinimize = keyName; break;
                case "KeyRestore": settings.KeyRestore = keyName; break;
                case "KeyToggleOverlay": settings.KeyToggleOverlay = keyName; break;
                default: return;
            }

            _settingsService.UpdateSettings(settings);
            _ = _settingsService.SaveAsync();

            if (System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.RegisterAllKeybinds();
            }
        }

        public void UpdateOverlayWidth(double width)
        {
            var settings = _settingsService.GetSettings();
            settings.OverlayWidth = width;
            _settingsService.UpdateSettings(settings);
            _ = _settingsService.SaveAsync();

            if (System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.Width = width;
                mainWindow.MaxWidth = width;
            }
        }

        public void UpdateOverlayFontSize(double size)
        {
            var settings = _settingsService.GetSettings();
            settings.OverlayFontSize = size;
            _settingsService.UpdateSettings(settings);
            _ = _settingsService.SaveAsync();
            OnPropertyChanged(nameof(ContentFontSize));
        }

        public void UpdateJtacMode(bool enabled)
        {
            var settings = _settingsService.GetSettings();
            settings.JtacMode = enabled;
            _settingsService.UpdateSettings(settings);
            _ = _settingsService.SaveAsync();

            if (System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.SetJtacMode(enabled);
            }
        }

        public void UpdateObjectivesPosition(string position)
        {
            var settings = _settingsService.GetSettings();
            settings.ObjectivesPosition = position;
            _settingsService.UpdateSettings(settings);
            _ = _settingsService.SaveAsync();

            if (System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.RepositionObjectivesWindow();
            }
        }

        public void UpdateVcRosterDisplayMode(string mode)
        {
            var settings = _settingsService.GetSettings();
            settings.VcRosterDisplayMode = mode;
            _settingsService.UpdateSettings(settings);
            _ = _settingsService.SaveAsync();

            // Trigger a re-render by raising a property changed on CurrentOrder
            OnPropertyChanged(nameof(CurrentOrder));
        }

        public void UpdateUsernameFilterEnabled(bool enabled)
        {
            var settings = _settingsService.GetSettings();
            settings.FilterUsernames = enabled;
            _settingsService.UpdateSettings(settings);
            _ = _settingsService.SaveAsync();

            // Trigger a re-render to apply filter changes
            OnPropertyChanged(nameof(CurrentOrder));
        }

        public void UpdateUsernameFilterPrefix(string prefix)
        {
            var settings = _settingsService.GetSettings();
            settings.UsernameFilterPrefix = prefix;
            _settingsService.UpdateSettings(settings);
            _ = _settingsService.SaveAsync();

            // Trigger a re-render to apply filter changes
            OnPropertyChanged(nameof(CurrentOrder));
        }

        public void UpdateUsernameFilterSuffix(string suffix)
        {
            var settings = _settingsService.GetSettings();
            settings.UsernameFilterSuffix = suffix;
            _settingsService.UpdateSettings(settings);
            _ = _settingsService.SaveAsync();

            // Trigger a re-render to apply filter changes
            OnPropertyChanged(nameof(CurrentOrder));
        }

        public void UpdateMapRegion(double left, double top, double width, double height)
        {
            var settings = _settingsService.GetSettings();
            settings.MapLeft   = left;
            settings.MapTop    = top;
            settings.MapWidth  = width;
            settings.MapHeight = height;
            _settingsService.UpdateSettings(settings);
            _ = _settingsService.SaveAsync();

            if (System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.RepositionMapWindow();
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
