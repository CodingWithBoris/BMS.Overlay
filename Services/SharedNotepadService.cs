using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BMS.Overlay.Services;

internal class SharedNotepadJoinResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public SharedNotepadData? Data { get; set; }
}

internal class SharedNotepadData
{
    public string NotepadId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Manages joining, syncing, and leaving a shared notepad session.
/// The password is both the auth credential and the session key.
/// </summary>
public class SharedNotepadService
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public string? CurrentNotepadId { get; private set; }
    public string? CurrentPassword { get; private set; }
    public string? CurrentFactionId { get; private set; }
    public bool IsJoined => CurrentNotepadId != null;

    public SharedNotepadService(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');

        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    /// <summary>
    /// Join (or create) a shared notepad for the given faction + role + password.
    /// Returns the initial RTF content (Base64) on success, null on failure.
    /// </summary>
    public async Task<(string notepadId, string content)?> JoinAsync(string factionId, string roleId, string password)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { roleId, password });
            var content = new StringContent(body, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                $"{_baseUrl}/factions/{factionId}/shared-notepad/join", content);

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<SharedNotepadJoinResponse>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result?.Success == true && result.Data != null)
            {
                CurrentNotepadId = result.Data.NotepadId;
                CurrentPassword = password;
                CurrentFactionId = factionId;
                return (result.Data.NotepadId, result.Data.Content);
            }

            System.Diagnostics.Debug.WriteLine($"[SharedNotepad] Join failed: {result?.Message}");
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SharedNotepad] Join error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Save the current RTF content (Base64) to the shared notepad.
    /// </summary>
    public async Task<bool> SaveAsync(string rtfBase64)
    {
        if (!IsJoined || CurrentNotepadId == null || CurrentFactionId == null || CurrentPassword == null)
            return false;

        try
        {
            var body = JsonSerializer.Serialize(new { password = CurrentPassword, content = rtfBase64 });
            var content = new StringContent(body, Encoding.UTF8, "application/json");

            var response = await _httpClient.PutAsync(
                $"{_baseUrl}/factions/{CurrentFactionId}/shared-notepad/{CurrentNotepadId}", content);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SharedNotepad] Save error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Leave the shared notepad session and clear local state.
    /// </summary>
    public void Leave()
    {
        CurrentNotepadId = null;
        CurrentPassword = null;
        CurrentFactionId = null;
    }
}
