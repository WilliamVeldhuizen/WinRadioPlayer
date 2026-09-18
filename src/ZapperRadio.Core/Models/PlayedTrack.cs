using System.Globalization;

namespace ZapperRadio.Core.Models;

/// <summary>A song a station played, as it was heard while the station was streaming in the background.</summary>
public sealed record PlayedTrack(string Title, string StationName, string StationUrl, DateTimeOffset PlayedAt)
{
    private static readonly CultureInfo EnglishCulture = new("en-US");

    /// <summary>Which station played it and when, e.g. "Qmusic · 21:48".</summary>
    public string Details => $"{StationName} · {PlayedAt.ToLocalTime().ToString("HH:mm", EnglishCulture)}";
}
