using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BMS.Overlay.Services;

namespace BMS.Overlay.Views;

public partial class VcRosterWindow : Window
{
    private readonly ApiService _apiService;
    private readonly SignalRService _signalRService;
    private readonly string _factionId;
    private bool _isRobloxVisible = false;
    private List<VcRosterDto> _rosters = new();

    public VcRosterWindow(ApiService apiService, SignalRService signalRService, string factionId)
    {
        InitializeComponent();
        _apiService = apiService;
        _signalRService = signalRService;
        _factionId = factionId;

        _signalRService.OnVcRosterUpdated += OnVcRosterUpdated;
        Loaded += async (_, _) => await RefreshRosterAsync();
    }

    public void SetRobloxVisible(bool visible)
    {
        _isRobloxVisible = visible;
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        bool hasMembers = _rosters.Any(r => r.Members.Count > 0);
        Opacity = (_isRobloxVisible && hasMembers) ? 1.0 : 0.0;
    }

    private void OnVcRosterUpdated(string factionId, string action, JsonElement data)
    {
        if (factionId != _factionId) return;
        Dispatcher.Invoke(async () => await RefreshRosterAsync());
    }

    private async Task RefreshRosterAsync()
    {
        try
        {
            _rosters = await _apiService.GetVcRosterFullAsync(_factionId);
            RebuildUI();
            UpdateVisibility();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VcRoster] Refresh error: {ex.Message}");
        }
    }

    private void RebuildUI()
    {
        RosterStack.Children.Clear();

        var allMembers = _rosters.SelectMany(r => r.Members).ToList();
        if (allMembers.Count == 0)
        {
            ChannelText.Text = "";
            return;
        }

        // Show channel name(s)
        var channelNames = _rosters
            .Where(r => r.Members.Count > 0)
            .Select(r => r.ChannelName)
            .Distinct();
        ChannelText.Text = string.Join(", ", channelNames);

        // Group by Team (null/empty → "Unassigned")
        var groups = allMembers
            .GroupBy(m => string.IsNullOrWhiteSpace(m.Team) ? "Unassigned" : m.Team)
            .OrderBy(g => g.Key == "Unassigned" ? 1 : 0)
            .ThenBy(g => g.Key);

        foreach (var group in groups)
        {
            // Team header
            RosterStack.Children.Add(new TextBlock
            {
                Text = group.Key.ToUpperInvariant(),
                Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xAA, 0x6E)),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(4, 8, 0, 2)
            });

            // Separator line
            RosterStack.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                Margin = new Thickness(4, 0, 4, 4)
            });

            foreach (var member in group.OrderBy(m => m.DisplayName))
            {
                RosterStack.Children.Add(BuildMemberRow(member));
            }
        }

        // Adjust height based on content
        int totalRows = allMembers.Count + groups.Count(); // members + headers
        Height = Math.Min(60 + totalRows * 24, 600);
    }

    private UIElement BuildMemberRow(VcMemberDto member)
    {
        var row = new DockPanel { Margin = new Thickness(6, 1, 6, 1) };

        // Callsign (if assigned)
        if (!string.IsNullOrWhiteSpace(member.Callsign))
        {
            row.Children.Add(new TextBlock
            {
                Text = $"[{member.Callsign}]",
                Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xAA, 0x6E)),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        // Display name
        row.Children.Add(new TextBlock
        {
            Text = member.DisplayName,
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        });

        // Role (right-aligned, if assigned)
        if (!string.IsNullOrWhiteSpace(member.Role))
        {
            var roleText = new TextBlock
            {
                Text = member.Role,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(roleText, Dock.Right);
            row.Children.Add(roleText);
        }

        return row;
    }

    protected override void OnClosed(EventArgs e)
    {
        _signalRService.OnVcRosterUpdated -= OnVcRosterUpdated;
        base.OnClosed(e);
    }
}
