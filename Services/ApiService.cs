using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using BMS.Shared.Models;

namespace BMS.Overlay.Services;

public class ApiService
{
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "http://bms-production-2d4a.up.railway.app:8080/api/v1";
    private bool _isOfflineMode = false;

    public ApiService()
    {
        var handler = new HttpClientHandler();
        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    public void SetAuthToken(string jwtToken)
    {
        _httpClient.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", jwtToken);
    }

    public async Task<List<BmsOrder>> GetOrdersAsync(Guid factionId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/factions/{factionId}/orders");
            
            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"API Error: {response.StatusCode} - {response.ReasonPhrase}");
                _isOfflineMode = true;
                return GetMockOrders();
            }

            _isOfflineMode = false;
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<BmsOrder>>(json, 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }
        catch (HttpRequestException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Network Error: {ex.Message}");
            _isOfflineMode = true;
            return GetMockOrders();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"API Error: {ex.Message}");
            return new();
        }
    }

    public async Task<BmsOrder?> GetOrderByIndexAsync(Guid factionId, int index)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/factions/{factionId}/orders/{index}");
            
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<BmsOrder>(json, 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"API Error: {ex.Message}");
            return null;
        }
    }

    public async Task<List<FactionInfo>> GetFactionsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/factions");
            
            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"API Error: {response.StatusCode}");
                _isOfflineMode = true;
                return GetMockFactions();
            }

            _isOfflineMode = false;
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<FactionInfo>>(json, 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }
        catch (HttpRequestException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Network Error: {ex.Message}");
            _isOfflineMode = true;
            return GetMockFactions();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"API Error: {ex.Message}");
            return new();
        }
    }

    public async Task<bool> VerifyFactionPasswordAsync(Guid factionId, string password)
    {
        try
        {
            var request = new { password };
            var content = new StringContent(
                JsonSerializer.Serialize(request),
                System.Text.Encoding.UTF8,
                "application/json");
            
            var response = await _httpClient.PostAsync($"/factions/{factionId}/verify-view", content);
            
            if (_isOfflineMode)
                return !string.IsNullOrEmpty(password); // Mock validation for offline mode
            
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"API Error: {ex.Message}");
            return _isOfflineMode && !string.IsNullOrEmpty(password);
        }
    }

    /// <summary>
    /// Mock data for development and offline testing
    /// </summary>
    private List<FactionInfo> GetMockFactions()
    {
        return new List<FactionInfo>
        {
            new FactionInfo
            {
                Id = Guid.Parse("550e8400-e29b-41d4-a716-446655440000"),
                Title = "Command",
                OwnerId = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
                Roles = new List<RoleInfo>
                {
                    new RoleInfo { Id = Guid.NewGuid(), Name = "Officer", IsDefault = true },
                    new RoleInfo { Id = Guid.NewGuid(), Name = "Scout", IsDefault = false }
                }
            },
            new FactionInfo
            {
                Id = Guid.Parse("550e8400-e29b-41d4-a716-446655440001"),
                Title = "Support",
                OwnerId = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow,
                Roles = new List<RoleInfo>
                {
                    new RoleInfo { Id = Guid.NewGuid(), Name = "Support", IsDefault = true }
                }
            }
        };
    }

    private List<BmsOrder> GetMockOrders()
    {
        return new List<BmsOrder>
        {
            new BmsOrder
            {
                Id = Guid.NewGuid(),
                FactionId = Guid.NewGuid(),
                OrderIndex = 0,
                Title = "Operation Briefing",
                Content = "Welcome to the BMS System.\n\nThis is a mock order for demonstration purposes.\n\n• Objective: Test overlay functionality\n• Timeline: Ongoing\n• Status: Active",
                IsPublished = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new BmsOrder
            {
                Id = Guid.NewGuid(),
                FactionId = Guid.NewGuid(),
                OrderIndex = 1,
                Title = "Additional Information",
                Content = "This is another mock order.\n\nFeatures:\n• Bold text support\n• Lists\n• Multiple pages\n• Real-time updates\n\nUse the arrow buttons to navigate between orders.",
                IsPublished = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        };
    }
}
