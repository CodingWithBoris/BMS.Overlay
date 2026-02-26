using Microsoft.AspNetCore.SignalR.Client;
using System.Diagnostics;

namespace BMS.Overlay.Services
{
    public class SignalRService : IAsyncDisposable
    {
        private HubConnection? _connection;
        private string? _currentFactionId;
        private readonly string _hubUrl;

        public event Action<string, string, string>? OnOrdersUpdated; // factionId, orderId, action
        public event Action<string, DateTime>? OnSharedNotepadUpdated; // rtfContent, updatedAt

        public SignalRService(string baseApiUrl)
        {
            // Convert API base URL to SignalR hub URL
            // e.g. "https://bms-production-f22e.up.railway.app/api/v1" -> "https://bms-production-f22e.up.railway.app/hubs/bms"
            var uri = new Uri(baseApiUrl);
            _hubUrl = $"{uri.Scheme}://{uri.Host}:{uri.Port}/hubs/bms";
            if (uri.Port == 80 || uri.Port == 443)
                _hubUrl = $"{uri.Scheme}://{uri.Host}/hubs/bms";

            Debug.WriteLine($"SignalR hub URL: {_hubUrl}");
        }

        public async Task ConnectAsync()
        {
            try
            {
                _connection = new HubConnectionBuilder()
                    .WithUrl(_hubUrl)
                    .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
                    .Build();

                _connection.On<dynamic>("OrdersUpdated", (data) =>
                {
                    try
                    {
                        string factionId = data.GetProperty("factionId").GetString() ?? "";
                        string orderId = data.GetProperty("orderId").GetString() ?? "";
                        string action = data.GetProperty("action").GetString() ?? "";
                        Debug.WriteLine($"SignalR: Received OrdersUpdated - faction={factionId}, order={orderId}, action={action}");
                        OnOrdersUpdated?.Invoke(factionId, orderId, action);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"SignalR: Error parsing OrdersUpdated: {ex.Message}");
                        // Still trigger a refresh even if parsing fails
                        OnOrdersUpdated?.Invoke("", "", "unknown");
                    }
                });

                _connection.On<dynamic>("SharedNotepadUpdated", (data) =>
                {
                    try
                    {
                        string content = data.GetProperty("content").GetString() ?? "";
                        DateTime updatedAt = data.GetProperty("updatedAt").GetDateTime();
                        Debug.WriteLine($"SignalR: Received SharedNotepadUpdated - updatedAt={updatedAt}");
                        OnSharedNotepadUpdated?.Invoke(content, updatedAt);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"SignalR: Error parsing SharedNotepadUpdated: {ex.Message}");
                    }
                });

                _connection.Reconnecting += (error) =>
                {
                    Debug.WriteLine($"SignalR: Reconnecting... {error?.Message}");
                    return Task.CompletedTask;
                };

                _connection.Reconnected += async (connectionId) =>
                {
                    Debug.WriteLine($"SignalR: Reconnected with ID {connectionId}");
                    // Re-subscribe to faction after reconnection
                    if (!string.IsNullOrEmpty(_currentFactionId))
                    {
                        await SubscribeToFactionAsync(_currentFactionId);
                    }
                };

                _connection.Closed += (error) =>
                {
                    Debug.WriteLine($"SignalR: Connection closed. {error?.Message}");
                    return Task.CompletedTask;
                };

                await _connection.StartAsync();
                Debug.WriteLine($"SignalR: Connected successfully. ConnectionId={_connection.ConnectionId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SignalR: Failed to connect: {ex.Message}");
                // Don't throw - the overlay should still work without real-time updates
            }
        }

        public async Task SubscribeToFactionAsync(string factionId)
        {
            if (_connection?.State != HubConnectionState.Connected)
            {
                Debug.WriteLine("SignalR: Cannot subscribe - not connected");
                return;
            }

            try
            {
                // Unsubscribe from previous faction
                if (!string.IsNullOrEmpty(_currentFactionId) && _currentFactionId != factionId)
                {
                    await _connection.InvokeAsync("UnsubscribeFromFaction", _currentFactionId);
                    Debug.WriteLine($"SignalR: Unsubscribed from faction {_currentFactionId}");
                }

                // Subscribe to new faction
                await _connection.InvokeAsync("SubscribeToFaction", factionId);
                _currentFactionId = factionId;
                Debug.WriteLine($"SignalR: Subscribed to faction {factionId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SignalR: Error subscribing to faction: {ex.Message}");
            }
        }

        public async Task SubscribeToSharedNotepadAsync(string notepadId)
        {
            if (_connection?.State != HubConnectionState.Connected)
            {
                Debug.WriteLine("SignalR: Cannot subscribe to shared notepad - not connected");
                return;
            }

            try
            {
                await _connection.InvokeAsync("SubscribeToSharedNotepad", notepadId);
                Debug.WriteLine($"SignalR: Subscribed to shared notepad {notepadId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SignalR: Error subscribing to shared notepad: {ex.Message}");
            }
        }

        public async Task UnsubscribeFromSharedNotepadAsync(string notepadId)
        {
            if (_connection?.State != HubConnectionState.Connected) return;

            try
            {
                await _connection.InvokeAsync("UnsubscribeFromSharedNotepad", notepadId);
                Debug.WriteLine($"SignalR: Unsubscribed from shared notepad {notepadId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SignalR: Error unsubscribing from shared notepad: {ex.Message}");
            }
        }

        public bool IsConnected => _connection?.State == HubConnectionState.Connected;

        public async ValueTask DisposeAsync()
        {
            if (_connection != null)
            {
                try
                {
                    if (!string.IsNullOrEmpty(_currentFactionId))
                    {
                        await _connection.InvokeAsync("UnsubscribeFromFaction", _currentFactionId);
                    }
                    await _connection.DisposeAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SignalR: Error during dispose: {ex.Message}");
                }
            }
        }
    }
}
