using System.IO;
using System.Text.Json;
using BMS.Shared.Models;

namespace BMS.Overlay.Services;

public class MapDataService
{
    private readonly string _baseDir;

    public MapDataService()
    {
        _baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BMS");
    }

    private string GetPath(string orderId) =>
        Path.Combine(_baseDir, $"map-{orderId}.json");

    public async Task<MapData> LoadAsync(string orderId)
    {
        try
        {
            var path = GetPath(orderId);
            if (!File.Exists(path))
                return new MapData { OrderId = orderId };

            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<MapData>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new MapData { OrderId = orderId };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MapData] Load error: {ex.Message}");
            return new MapData { OrderId = orderId };
        }
    }

    public async Task SaveAsync(MapData data)
    {
        try
        {
            Directory.CreateDirectory(_baseDir);
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = false });
            await File.WriteAllTextAsync(GetPath(data.OrderId), json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MapData] Save error: {ex.Message}");
        }
    }

    public void Clear(string orderId)
    {
        try
        {
            var path = GetPath(orderId);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MapData] Clear error: {ex.Message}");
        }
    }
}
