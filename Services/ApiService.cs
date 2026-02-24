using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using BMS.Shared.Models;

namespace BMS.Overlay.Services;

// Local DTOs for API responses (mirrors BMS.API.Models.DTOs)
internal class ApiResponse<T> where T : class
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
}

internal class FactionDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public List<RoleDto> Roles { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

internal class RoleDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}

public class ApiService
{
    private readonly HttpClient _httpClient;
    private string _baseUrl;
    private bool _isOfflineMode = false;

    public ApiService(string? baseUrl = null)
    {
        // Allow override via constructor, otherwise use defaults
        // For local development: http://localhost:8080/api/v1
        // For production: https://bms-production-f22e.up.railway.app/api/v1/
        _baseUrl = baseUrl ?? "http://localhost:8080/api/v1";
        
        var handler = new HttpClientHandler();
        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(_baseUrl),
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    public void SetBaseUrl(string baseUrl)
    {
        _baseUrl = baseUrl;
        _httpClient.BaseAddress = new Uri(_baseUrl);
    }

    public void SetAuthToken(string jwtToken)
    {
        _httpClient.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", jwtToken);
    }

    public async Task<List<BmsOrder>> GetOrdersAsync(string factionId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"factions/{factionId}/orders");
            
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

    public async Task<BmsOrder?> GetOrderByIndexAsync(string factionId, int index)
    {
        try
        {
            var response = await _httpClient.GetAsync($"factions/{factionId}/orders/{index}");
            
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
            System.Diagnostics.Debug.WriteLine($"Fetching factions from: {_httpClient.BaseAddress}factions");
            var response = await _httpClient.GetAsync("factions");
            var json = await response.Content.ReadAsStringAsync();
            
            System.Diagnostics.Debug.WriteLine($"Faction response [{response.StatusCode}]: {json[..Math.Min(json.Length, 200)]}");

            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"API Error: {response.StatusCode} - {response.ReasonPhrase}");
                _isOfflineMode = true;
                return GetMockFactions();
            }

            // Parse the ApiResponse wrapper
            var apiResponse = JsonSerializer.Deserialize<ApiResponse<List<FactionDto>>>(json, 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (apiResponse?.Success == true && apiResponse.Data != null)
            {
                _isOfflineMode = false;
                // Convert FactionDto to FactionInfo
                return apiResponse.Data.Select(f => new FactionInfo
                {
                    Id = f.Id,
                    Title = f.Title,
                    OwnerId = f.OwnerId,
                    CreatedAt = f.CreatedAt,
                    Roles = f.Roles?.Select(r => new RoleInfo
                    {
                        Id = r.Id,
                        Name = r.Name,
                        IsDefault = r.IsDefault
                    }).ToList() ?? new()
                }).ToList();
            }

            System.Diagnostics.Debug.WriteLine($"Failed to parse faction response: Success={apiResponse?.Success}");
            _isOfflineMode = true;
            return GetMockFactions();
        }
        catch (HttpRequestException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Network Error: {ex.Message}");
            _isOfflineMode = true;
            return GetMockFactions();
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"JSON Parse Error: {ex.Message}");
            _isOfflineMode = true;
            return GetMockFactions();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"API Error: {ex.Message}\n{ex.StackTrace}");
            return new();
        }
    }

    public async Task<bool> VerifyFactionPasswordAsync(string factionId, string password)
    {
        try
        {
            var request = new { password };
            var content = new StringContent(
                JsonSerializer.Serialize(request),
                System.Text.Encoding.UTF8,
                "application/json");
            
            var response = await _httpClient.PostAsync($"factions/{factionId}/verify-view", content);
            
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
                Id = "550e8400-e29b-41d4-a716-446655440000",
                Title = "Command",
                OwnerId = Guid.NewGuid().ToString(),
                CreatedAt = DateTime.UtcNow,
                Roles = new List<RoleInfo>
                {
                    new RoleInfo { Id = Guid.NewGuid().ToString(), Name = "Officer", IsDefault = true },
                    new RoleInfo { Id = Guid.NewGuid().ToString(), Name = "Scout", IsDefault = false }
                }
            },
            new FactionInfo
            {
                Id = "550e8400-e29b-41d4-a716-446655440001",
                Title = "Support",
                OwnerId = Guid.NewGuid().ToString(),
                CreatedAt = DateTime.UtcNow,
                Roles = new List<RoleInfo>
                {
                    new RoleInfo { Id = Guid.NewGuid().ToString(), Name = "Support", IsDefault = true }
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
                Id = Guid.NewGuid().ToString(),
                FactionId = Guid.NewGuid().ToString(),
                OrderIndex = 0,
                Title = "Operation Briefing",
                Content = "Welcome to the BMS System.\n\nThis is a mock order for demonstration purposes.\n\n• Objective: Test overlay functionality\n• Timeline: Ongoing\n• Status: Active",
                IsPublished = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new BmsOrder
            {
                Id = Guid.NewGuid().ToString(),
                FactionId = Guid.NewGuid().ToString(),
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
