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

        return new IcyMetadata(title, isAd);
    }
}
