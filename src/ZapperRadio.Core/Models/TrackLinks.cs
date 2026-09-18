namespace ZapperRadio.Core.Models;

/// <summary>A music service a song heard on the radio can be looked up in.</summary>
public enum MusicService
{
    Spotify,

    YouTube,
}

/// <summary>
/// Turns the title a station sends into a search link, which closes the loop from "heard it on the radio" to
/// "saved in my playlist" without typing the song over. A stream title is not a track id, so the link searches
/// rather than opens: "Artist - Title" becomes the words on their own, which also covers the stations that send
/// the two the other way around, because a search does not care about the order.
/// </summary>
public static class TrackLinks
{
    /// <summary>What stations put between the artist and the title, and what is dropped from a search.</summary>
    private static readonly char[] Separators = ['-', '–', '—', '|'];

    /// <summary>
    /// The words to search for, or null when the title holds none. Everything else in the title is kept,
    /// including an addition like "(Radio Edit)": it is part of the name of that recording, and a service
    /// that does not have that version still finds the song by the words around it.
    /// </summary>
    public static string? SearchTerm(string? title)
    {
        if (title is null)
        {
            return null;
        }

        var words = title
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(word => word.AsSpan().IndexOfAnyExcept(Separators) >= 0);
        var term = string.Join(' ', words);
        return term.Length > 0 ? term : null;
    }

    /// <summary>
    /// The link that searches the service in its own app, or null when the service has no such link or the
    /// title holds no words. Windows opens the web page instead when the app is not installed.
    /// </summary>
    public static Uri? App(MusicService service, string? title) =>
        SearchTerm(title) is { } term && service == MusicService.Spotify
            ? new Uri($"spotify:search:{Uri.EscapeDataString(term)}")
            : null;

    /// <summary>The web page that searches the service, or null when the title holds no words.</summary>
    public static Uri? Web(MusicService service, string? title) =>
        SearchTerm(title) is not { } term ? null
        : service == MusicService.Spotify
            ? new Uri($"https://open.spotify.com/search/{Uri.EscapeDataString(term)}")
            : new Uri($"https://www.youtube.com/results?search_query={Uri.EscapeDataString(term)}");
}
