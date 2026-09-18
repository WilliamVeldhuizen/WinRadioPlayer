using System.Text.Json;
using System.Text.Json.Serialization;
using ZapperRadio.Core.Models;

namespace ZapperRadio.Core.Playback;

/// <summary>
/// What every station streaming in the background played, newest first. Every favorite sends its song titles
/// whether or not it is the one being listened to, so this answers "what was that song four minutes ago?"
/// for the station you were on as well as for the one you just zapped away from.
/// It is a short-lived list, not a library: entries older than <see cref="Retention"/> are dropped, and so are
/// the oldest ones beyond <see cref="MaxEntries"/>, which is the ceiling twenty stations reach in half a day.
/// </summary>
public sealed class PlayHistory
{
    /// <summary>How long a song stays in the list.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromHours(12);

    /// <summary>A ceiling on top of <see cref="Retention"/>, so a stuttering station cannot fill the list.</summary>
    public const int MaxEntries = 2000;

    private readonly List<PlayedTrack> _entries = [];
    private readonly Dictionary<string, string> _lastTitleByStation = new(StringComparer.Ordinal);

    /// <summary>The songs, newest first.</summary>
    public IReadOnlyList<PlayedTrack> Entries => _entries;

    public int Count => _entries.Count;

    /// <summary>
    /// Records a song a station started playing, and returns it, or null when it is not worth recording:
    /// a blank title, the station's own name (which some stations send between songs), or the title that
    /// station is already on, because a reconnect repeats the title of the song that is playing.
    /// </summary>
    public PlayedTrack? Add(string? title, string stationName, string stationUrl, DateTimeOffset at)
    {
        var text = TrackTitle.Normalize(title);
        if (text.Length == 0 || IsSameText(text, stationName))
        {
            return null;
        }

        if (_lastTitleByStation.TryGetValue(stationUrl, out var last) && IsSameText(text, last))
        {
            return null;
        }

        _lastTitleByStation[stationUrl] = text;
        var track = new PlayedTrack(text, stationName, stationUrl, at);
        _entries.Insert(0, track);
        Prune(at);
        return track;
    }

    /// <summary>Drops the songs that have grown too old. Only ever removes from the end of the list.</summary>
    public void Prune(DateTimeOffset now)
    {
        var oldest = now - Retention;
        while (_entries.Count > 0 && (_entries.Count > MaxEntries || _entries[^1].PlayedAt < oldest))
        {
            _entries.RemoveAt(_entries.Count - 1);
        }
    }

    public void Clear()
    {
        _entries.Clear();
        _lastTitleByStation.Clear();
    }

    /// <summary>Fills the list from a saved one, keeping only what is still recent enough.</summary>
    public void Load(IEnumerable<PlayedTrack> entries, DateTimeOffset now)
    {
        Clear();
        _entries.AddRange(entries.OrderByDescending(e => e.PlayedAt));
        Prune(now);
        // A station that was already playing a song before the app closed should not record it again.
        foreach (var entry in _entries)
        {
            _lastTitleByStation.TryAdd(entry.StationUrl, entry.Title);
        }
    }

    private static bool IsSameText(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Keeps the play history in its own file next to the settings: it changes with every song of every station,
/// which is far too often to rewrite the settings for, and it is thrown away after half a day anyway.
/// </summary>
public sealed class PlayHistoryStore(string path)
{
    public List<PlayedTrack> Load()
    {
        try
        {
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                return JsonSerializer.Deserialize(stream, PlayHistoryJsonContext.Default.ListPlayedTrack) ?? [];
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // History is a convenience; losing it is never worth an error.
        }

        return [];
    }

    public void Save(IReadOnlyList<PlayedTrack> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
        {
            JsonSerializer.Serialize(stream, entries.ToList(), PlayHistoryJsonContext.Default.ListPlayedTrack);
        }

        File.Move(temp, path, overwrite: true);
    }
}

// Not indented: the file holds thousands of entries and is never read by hand.
[JsonSourceGenerationOptions(WriteIndented = false, IgnoreReadOnlyProperties = true)]
[JsonSerializable(typeof(List<PlayedTrack>))]
internal sealed partial class PlayHistoryJsonContext : JsonSerializerContext;
