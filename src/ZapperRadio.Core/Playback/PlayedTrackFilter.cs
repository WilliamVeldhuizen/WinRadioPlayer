using ZapperRadio.Core.Models;

namespace ZapperRadio.Core.Playback;

/// <summary>
/// Search over the play history. Every word of the query has to appear in the song, which carries the artist
/// as well as the title, or in the name of the station that played it, so "abba q" finds Waterloo on Qmusic
/// without typing the two in the order they are written. Unlike the station search this has no typo tolerance:
/// a song heard an hour ago is remembered well enough to type, and a wrong hit is far harder to notice in a
/// list of songs than in a list of station names.
/// </summary>
public sealed class PlayedTrackFilter
{
    private readonly string[] _terms;

    public PlayedTrackFilter(string? query) =>
        _terms = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public bool IsEmpty => _terms.Length == 0;

    public bool Matches(PlayedTrack track)
    {
        foreach (var term in _terms)
        {
            if (!track.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                && !track.StationName.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
