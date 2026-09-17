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
        "adbreak", "adbreakstart", "advert", "adverts", "advertisement", "advertising",
        "commercial", "commercials", "commercialin", "commercialout", "commercialbreak",
        "reclame", "reclameblok", "werbung", "publicite", "publicidad", "pubblicita",
    ];

    // A value ends at "';" followed by the next key or the end, so titles like "Don't Stop" survive.
    [GeneratedRegex(@"(?<key>\w+)='(?<value>.*?)';(?=\s*\w+='|\s*$)", RegexOptions.Singleline)]
    private static partial Regex Field();

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
                case "adw_ad":
                    isAd = value.Equals("true", StringComparison.OrdinalIgnoreCase);
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
    /// </summary>
    public static bool IsAdTitle(string title)
    {
        Span<char> letters = stackalloc char[Math.Min(title.Length, 64)];
        var length = 0;
        foreach (var c in title)
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
