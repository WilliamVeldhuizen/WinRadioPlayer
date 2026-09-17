using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ZapperRadio.Core.Streaming;

/// <summary>
/// Looks up how long a song lasts, so a station that keeps showing a song long after it must have ended
/// can be assumed to be in an (unmarked) ad break. Stream titles have no length, so this asks the
/// iTunes Search API for "Artist - Title" and only trusts a result whose artist and title match.
/// Results are kept in memory; requests are spaced out to stay well within the API's rate limit.
/// </summary>
public sealed class TrackDurations(HttpClient http, TimeSpan? requestSpacing = null)
{
    public static readonly Uri DefaultSearchUrl = new("https://itunes.apple.com/search");

    private const int MaxCachedTitles = 1000;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MinDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(15);

    // The API allows about 20 calls per minute per IP address; this keeps it at 15.
    private readonly TimeSpan _requestSpacing = requestSpacing ?? TimeSpan.FromSeconds(4);
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly Dictionary<string, Task<TimeSpan?>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public Uri SearchUrl { get; init; } = DefaultSearchUrl;

    /// <summary>
    /// Returns the length of the song in a stream title like "Artist - Title",
    /// or null when the title is not a song or the song could not be found.
    /// </summary>
    public Task<TimeSpan?> GetAsync(string streamTitle)
    {
        if (SplitTitle(streamTitle) is not { } song)
        {
            return Task.FromResult<TimeSpan?>(null);
        }

        lock (_cache)
        {
            if (!_cache.TryGetValue(streamTitle, out var lookup))
            {
                if (_cache.Count >= MaxCachedTitles)
                {
                    _cache.Clear();
                }

                // Started on the thread pool, so a lookup that fails at once can only forget itself after it is cached.
                lookup = _cache[streamTitle] = Task.Run(() => LookUpAsync(streamTitle, song.Artist, song.Title));
            }

            return lookup;
        }
    }

    private async Task<TimeSpan?> LookUpAsync(string streamTitle, string artist, string title)
    {
        await _requestGate.WaitAsync();
        try
        {
            using var timeout = new CancellationTokenSource(RequestTimeout);
            var term = Uri.EscapeDataString($"{artist} {title}");
            var json = await http.GetStringAsync(new Uri($"{SearchUrl}?term={term}&media=music&entity=song&limit=10"), timeout.Token);
            // Some stations (such as Qmusic) send "Title - Artist"; the search term matches either order.
            return FindDuration(json, artist, title) ?? FindDuration(json, title, artist);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            // Forget the failure, so the song is looked up again the next time it plays.
            lock (_cache)
            {
                _cache.Remove(streamTitle);
            }

            return null;
        }
        finally
        {
            _ = Task.Delay(_requestSpacing).ContinueWith(_ => _requestGate.Release(), TaskScheduler.Default);
        }
    }

    /// <summary>Splits "Artist - Title" at the first dash; titles without one (station names, shows) are not songs.</summary>
    public static (string Artist, string Title)? SplitTitle(string streamTitle)
    {
        var separator = streamTitle.IndexOf(" - ", StringComparison.Ordinal);
        if (separator <= 0)
        {
            return null;
        }

        var artist = streamTitle[..separator].Trim();
        var title = streamTitle[(separator + 3)..].Trim();
        return artist.Length > 0 && title.Length > 0 ? (artist, title) : null;
    }

    /// <summary>
    /// Finds the length of the first search result whose artist and title match the song exactly,
    /// or else of the first one where they only overlap (such as an extra featured artist).
    /// </summary>
    public static TimeSpan? FindDuration(string json, string artist, string title)
    {
        var wantedArtist = Normalize(artist);
        var wantedTitle = Normalize(title);
        if (wantedArtist.Length == 0 || wantedTitle.Length == 0)
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        TimeSpan? overlapping = null;
        foreach (var result in results.EnumerateArray())
        {
            if (!result.TryGetProperty("artistName", out var artistName) || artistName.ValueKind != JsonValueKind.String
                || !result.TryGetProperty("trackName", out var trackName) || trackName.ValueKind != JsonValueKind.String
                || !result.TryGetProperty("trackTimeMillis", out var millis) || !millis.TryGetInt64(out var ms))
            {
                continue;
            }

            var duration = TimeSpan.FromMilliseconds(ms);
            var foundArtist = Normalize(artistName.GetString()!);
            var foundTitle = Normalize(trackName.GetString()!);
            if (duration < MinDuration || duration > MaxDuration
                || !Overlaps(foundArtist, wantedArtist) || !Overlaps(foundTitle, wantedTitle))
            {
                continue;
            }

            if (foundArtist == wantedArtist && foundTitle == wantedTitle)
            {
                return duration;
            }

            overlapping ??= duration;
        }

        return overlapping;
    }

    // "Artist feat. Other" matches "Artist", and "Title (Radio Edit)" matches "Title".
    private static bool Overlaps(string found, string wanted) =>
        found.Length > 0 && (found == wanted
                             || found.StartsWith(wanted + " ", StringComparison.Ordinal)
                             || wanted.StartsWith(found + " ", StringComparison.Ordinal));

    /// <summary>Lower case letters and digits separated by single spaces, without accents or bracketed additions.</summary>
    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        var depth = 0;
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (c is '(' or '[')
            {
                depth++;
            }
            else if (c is ')' or ']')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (depth == 0 && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
            }
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
