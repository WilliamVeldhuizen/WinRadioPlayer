using System.Text.Json;
using System.Text.Json.Serialization;
using WinRadioPlayer.Core.Models;

namespace WinRadioPlayer.Core.Settings;

public sealed class AppSettings
{
    /// <summary>Favorites keep streaming (muted) in the background, so their number is capped.</summary>
    public const int MaxFavorites = 10;

    public List<Station> Favorites { get; set; } = [];

    public double Volume { get; set; } = 0.8;

    public string? Country { get; set; }
}

public sealed class SettingsStore(string path)
{
    public AppSettings Load()
    {
        try
        {
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                return JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt settings file should not prevent the app from starting.
        }

        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
        {
            JsonSerializer.Serialize(stream, settings, SettingsJsonContext.Default.AppSettings);
        }

        File.Move(temp, path, overwrite: true);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, IgnoreReadOnlyProperties = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
