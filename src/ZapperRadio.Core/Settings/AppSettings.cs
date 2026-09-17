using System.Text.Json;
using System.Text.Json.Serialization;
using ZapperRadio.Core.Models;

namespace ZapperRadio.Core.Settings;

public sealed class AppSettings
{
    /// <summary>Favorites keep streaming (muted) in the background, so their number is capped.</summary>
    public const int MaxFavorites = 20;

    public List<Station> Favorites { get; set; } = [];

    /// <summary>Songs saved from the station being listened to, newest first.</summary>
    public List<FavoriteTrack> FavoriteTracks { get; set; } = [];

    public double Volume { get; set; } = 0.8;

    public string? Country { get; set; }

    /// <summary>Zap to another favorite during the ad breaks of the station being listened to, and back afterwards.</summary>
    public bool ZappOnAdBreaks { get; set; }

    /// <summary>What <see cref="ZappOnAdBreaks"/> was called before; only read, so older settings files keep the choice.</summary>
    [JsonPropertyName("SkipAdBreaks")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacySkipAdBreaks
    {
        get => null;
        set
        {
            if (value is true)
            {
                ZappOnAdBreaks = true;
            }
        }
    }
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
