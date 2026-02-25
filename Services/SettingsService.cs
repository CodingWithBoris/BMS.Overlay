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
    public double OverlayWidth { get; set; } = 280;
    public double OverlayOpacity { get; set; } = 0.8;
    public bool JtacMode { get; set; } = false;
}
