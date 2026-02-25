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

internal class BmsOrderDto
{
    public string Id { get; set; } = string.Empty;
    public int OrderIndex { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? RoleId { get; set; }
    public bool IsPublished { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
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
        // For production: https://bms-production-f22e.up.railway.app/api/v1
        _baseUrl = (baseUrl ?? "http://localhost:8080/api/v1").TrimEnd('/');
        
        var handler = new HttpClientHandler();
        // Disable SSL verification for testing (remove in production for security)
        handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
        
        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(_baseUrl + "/"),
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
            var endpoint = $"{_httpClient.BaseAddress}factions/{factionId}/orders";
            System.Diagnostics.Debug.WriteLine($"[Overlay API] Fetching orders from: {endpoint}");
            
            var response = await _httpClient.GetAsync($"factions/{factionId}/orders");
            var json = await response.Content.ReadAsStringAsync();
            
            System.Diagnostics.Debug.WriteLine($"[Overlay API] Orders response status: {response.StatusCode}");
            System.Diagnostics.Debug.WriteLine($"[Overlay API] Orders response body: {json[..Math.Min(json.Length, 500)]}");
            
            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"[Overlay API] ERROR: Status {response.StatusCode} - {response.ReasonPhrase}");
                System.Diagnostics.Debug.WriteLine($"[Overlay API] Falling back to mock orders");
                _isOfflineMode = true;
                return GetMockOrders();
            }

            // Parse the ApiResponse wrapper (same as GetFactionsAsync)
            var apiResponse = JsonSerializer.Deserialize<ApiResponse<List<BmsOrderDto>>>(json, 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (apiResponse?.Success == true && apiResponse.Data != null)
            {
                _isOfflineMode = false;
                System.Diagnostics.Debug.WriteLine($"[Overlay API] Successfully parsed {apiResponse.Data.Count} orders");
                
                // Convert BmsOrderDto to BmsOrder
                var result = apiResponse.Data.Select(o => new BmsOrder
                {
                    Id = o.Id,
                    OrderIndex = o.OrderIndex,
                    Title = o.Title,
                    Content = o.Content,
                    RoleId = o.RoleId,
                    IsPublished = o.IsPublished,
                    CreatedAt = o.CreatedAt,
                    UpdatedAt = o.UpdatedAt
                }).ToList();
                
                foreach (var order in result)
                {
                    System.Diagnostics.Debug.WriteLine($"[Overlay API]   - Order #{order.OrderIndex}: {order.Title}");
                }
                
                return result;
            }

            System.Diagnostics.Debug.WriteLine($"[Overlay API] Failed to parse orders response: Success={apiResponse?.Success}, Data={apiResponse?.Data == null}");
            System.Diagnostics.Debug.WriteLine($"[Overlay API] Falling back to mock orders");
            _isOfflineMode = true;
            return GetMockOrders();
        }
        catch (HttpRequestException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Overlay API] NETWORK ERROR fetching orders: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[Overlay API] Falling back to mock orders");
            _isOfflineMode = true;
            return GetMockOrders();
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Overlay API] JSON PARSE ERROR for orders: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[Overlay API] Falling back to mock orders");
            _isOfflineMode = true;
            return GetMockOrders();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Overlay API] UNEXPECTED ERROR fetching orders: {ex.Message}");
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
            var endpoint = $"{_httpClient.BaseAddress}factions";
            System.Diagnostics.Debug.WriteLine($"[Overlay API] Fetching factions from: {endpoint}");
            
            var response = await _httpClient.GetAsync("factions");
            var json = await response.Content.ReadAsStringAsync();
            
            System.Diagnostics.Debug.WriteLine($"[Overlay API] Response status: {response.StatusCode}");
            System.Diagnostics.Debug.WriteLine($"[Overlay API] Response body: {json[..Math.Min(json.Length, 300)]}");

            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"[Overlay API] ERROR: Status {response.StatusCode} - {response.ReasonPhrase}");
                System.Diagnostics.Debug.WriteLine($"[Overlay API] Falling back to mock data");
                _isOfflineMode = true;
                return GetMockFactions();
            }

            // Parse the ApiResponse wrapper
            var apiResponse = JsonSerializer.Deserialize<ApiResponse<List<FactionDto>>>(json, 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (apiResponse?.Success == true && apiResponse.Data != null)
            {
                _isOfflineMode = false;
                System.Diagnostics.Debug.WriteLine($"[Overlay API] Successfully parsed {apiResponse.Data.Count} factions");
                
                // Convert FactionDto to FactionInfo
                var result = apiResponse.Data.Select(f => new FactionInfo
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
                
                foreach (var f in result)
                {
                    System.Diagnostics.Debug.WriteLine($"[Overlay API]   - Faction: {f.Title}");
                }
                
                return result;
            }

            System.Diagnostics.Debug.WriteLine($"[Overlay API] Failed to parse response: Success={apiResponse?.Success}, Data={apiResponse?.Data == null}");
            System.Diagnostics.Debug.WriteLine($"[Overlay API] Falling back to mock data");
            _isOfflineMode = true;
            return GetMockFactions();
        }
        catch (HttpRequestException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Overlay API] NETWORK ERROR: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[Overlay API] Falling back to mock data");
            _isOfflineMode = true;
            return GetMockFactions();
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Overlay API] JSON PARSE ERROR: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[Overlay API] Falling back to mock data");
            _isOfflineMode = true;
            return GetMockFactions();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Overlay API] UNEXPECTED ERROR: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[Overlay API] Stack trace: {ex.StackTrace}");
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
