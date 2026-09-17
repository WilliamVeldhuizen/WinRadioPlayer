using System.Globalization;

namespace WinRadioPlayer.Core.Models;

/// <summary>A song saved while a station played it, as the station titled it (usually "Artist - Title").</summary>
public sealed record FavoriteTrack(string Title, string StationName, DateTimeOffset SavedAt)
{
    private static readonly CultureInfo EnglishCulture = new("en-US");

    /// <summary>Where and when the song was heard, e.g. "Qmusic · Sep 17, 2026".</summary>
    public string Details => $"{StationName} · {SavedAt.ToLocalTime().ToString("MMM d, yyyy", EnglishCulture)}";

    /// <summary>Stations differ in capitals and spacing, so the same song is recognized regardless.</summary>
    public bool IsSameSong(string title) =>
        string.Equals(Normalize(Title), Normalize(title), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string title) =>
        string.Join(' ', title.Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
