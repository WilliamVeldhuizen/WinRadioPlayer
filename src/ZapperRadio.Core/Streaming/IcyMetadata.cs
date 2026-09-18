using System.Text;
using System.Text.RegularExpressions;

namespace ZapperRadio.Core.Streaming;

/// <summary>
/// An ICY (Shoutcast/Icecast) metadata block, such as <c>StreamTitle='Artist - Title';StreamUrl='';</c>.
/// Ad platforms add fields like <c>adw_ad='true'</c> to the blocks they insert.
/// </summary>
public sealed partial record IcyMetadata(string? Title, bool IsAd)
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);

    /// <summary>Ad marker titles, in lower case letters only.</summary>
    private static readonly HashSet<string> AdTitles =
    [
        "ad", "ads", "adbreak", "adbreakstart", "adbreakend", "adbreakin", "adbreakout",
        "adstart", "adend", "advert", "adverts", "advertisement", "advertisements", "advertising",
        "commercial", "commercials", "commercialin", "commercialout", "commercialstart",
        "commercialend", "commercialbreak", "sponsormessage",
        "reclame", "reclameblok", "reclamespot", "werbung", "werbespot",
        "publicite", "publicidad", "publicidade", "pubblicita", "anuncio", "anuncios",
        "reklama", "reklame", "reklam", "reklaam", "mainos",
    ];

    // A value ends at the quote before the next key or the end, so titles like "Don't Stop" survive. The
    // semicolon after it is optional, because not every server sends one after the last field.
    [GeneratedRegex(@"(?<key>\w+)='(?<value>.*?)';?(?=\s*\w+='|\s*$)", RegexOptions.Singleline)]
    private static partial Regex Field();

    /// <summary>Whether the title names a song, rather than an ad, a station, a program or a jingle.</summary>
    public bool IsSong => !IsAd && Title is not null && TrackDurations.SplitTitle(Title) is not null;

    public static IcyMetadata Parse(ReadOnlySpan<byte> block)
    {
        var end = block.IndexOf((byte)0);
        if (end >= 0)
        {
            block = block[..end];
        }

        string text;
        try
        {
            text = StrictUtf8.GetString(block);
        }
        catch (DecoderFallbackException)
        {
            // Older servers send Latin-1.
            text = Encoding.Latin1.GetString(block);
        }

        return Parse(text);
    }

    public static IcyMetadata Parse(string text)
    {
        string? title = null;
        var isAd = false;
        foreach (Match match in Field().Matches(text.Trim()))
        {
            var value = match.Groups["value"].Value.Trim();
            switch (match.Groups["key"].Value.ToLowerInvariant())
            {
                case "streamtitle":
                    title = value.Length > 0 ? value : null;
                    break;
                // The field ad platforms add to the blocks they insert; some send "1" rather than "true".
                case "adw_ad":
                    isAd = value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1";
                    break;
            }
        }

        if (title is not null && IsAdTitle(title))
        {
            title = null;
            isAd = true;
        }

        return new IcyMetadata(title, isAd);
    }

    /// <summary>
    /// Some stations mark their ads with a title instead of a field: Qmusic and JOE send "adbreak", or "commercial-in" and "commercial-out" around the break.
    /// Only a title that is nothing but such a marker counts, so a song with "commercial" in its name still plays.
    /// Stations that always fill both halves of "Artist - Title" send the marker twice, so the halves are weighed
    /// on their own as well: "Reclame - Reclame" is an ad, while "The Commercials - Ad Break" stays a song.
    /// </summary>
    public static bool IsAdTitle(string title) =>
        IsMarker(title)
        || (TrackDurations.SplitTitle(title) is { } parts && IsMarker(parts.Artist) && IsMarker(parts.Title));

    /// <summary>Whether the text is nothing but an ad marker, ignoring case, spacing and punctuation.</summary>
    private static bool IsMarker(string text)
    {
        Span<char> letters = stackalloc char[Math.Min(Math.Max(text.Length, 1), 64)];
        var length = 0;
        foreach (var c in text)
        {
            if (!char.IsLetter(c))
            {
                continue;
            }

            if (length == letters.Length)
            {
                return false;
            }

            letters[length++] = char.ToLowerInvariant(c);
        }

        return AdTitles.GetAlternateLookup<ReadOnlySpan<char>>().Contains(letters[..length]);
    }
}
