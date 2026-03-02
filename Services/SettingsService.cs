using System.IO;
using System.Text.Json;

namespace BMS.Overlay.Services;

public class SettingsService
{
    private readonly string _settingsPath;
    private Settings _settings = new();

    public SettingsService()
    {
        _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BMS", "overlay-settings.json");
    }

    public async Task LoadAsync()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = await File.ReadAllTextAsync(_settingsPath);
                _settings = JsonSerializer.Deserialize<Settings>(json) ?? new();
            }
            else
            {
                _settings = new();
            }
        }
        catch
        {
            _settings = new();
        }
    }

    public async Task SaveAsync()
    {
        try
        {
            var dir = Path.GetDirectoryName(_settingsPath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir!);
            }

            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_settingsPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Settings error: {ex.Message}");
        }
    }

    public Settings GetSettings() => _settings ?? new();
    public void UpdateSettings(Settings settings) => _settings = settings;
}

public class Settings
{
    public Guid? SelectedFactionId { get; set; }
    public Guid? SelectedRoleId { get; set; }
    public string VoterId { get; set; } = string.Empty;
    public string KeyPrevious { get; set; } = "Left";
    public string KeyNext { get; set; } = "Right";
    public string KeyMinimize { get; set; } = "F9";
    public string KeyRestore { get; set; } = "F9";
    public string KeyToggleOverlay { get; set; } = "M";
    public double OverlayWidth { get; set; } = 400;
    public double OverlayFontSize { get; set; } = 12;
    public double OverlayOpacity { get; set; } = 0.8;
    public bool JtacMode { get; set; } = false;
    public string ObjectivesPosition { get; set; } = "TopLeft"; // "TopLeft" | "TopRight"

    // Map window region — fractions of screen (0.0–1.0). Defaults match previous hardcoded values.
    public double MapLeft   { get; set; } = 0.0;
    public double MapTop    { get; set; } = 0.15;
    public double MapWidth  { get; set; } = 1.0;
    public double MapHeight { get; set; } = 0.85;

    // VC Roster display mode — "compact" (text only) | "detailed" (with avatars)
    public string VcRosterDisplayMode { get; set; } = "compact";

    // Username filtering — remove rank prefixes/suffixes from display names
    public bool FilterUsernames { get; set; } = false;
    public string UsernameFilterPrefix { get; set; } = string.Empty; // Text to remove from start of username
    public string UsernameFilterSuffix { get; set; } = string.Empty; // Text to remove from end of username
}
