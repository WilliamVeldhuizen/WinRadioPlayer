using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ZapperRadio.Core.Streaming;

/// <summary>
/// Many directory entries point at a playlist (.pls, .m3u, .asx) instead of the audio stream.
/// Media players want the stream itself, so playlists are resolved to their first entry.
/// HLS playlists (.m3u8, or .m3u with #EXT-X- tags) are returned unchanged because the player handles them.
/// </summary>
public sealed partial class StreamUrlResolver(HttpClient http)
{
    private const int MaxPlaylistBytes = 64 * 1024;
    private const int MaxDepth = 3;

    [GeneratedRegex("<ref\\s+href\\s*=\\s*[\"'](?<url>[^\"']+)[\"']", RegexOptions.IgnoreCase)]
    private static partial Regex AsxRef();

    public async Task<Uri> ResolveAsync(Uri url, CancellationToken cancellationToken = default)
    {
        for (var depth = 0; depth < MaxDepth; depth++)
        {
            var kind = GetPlaylistKind(url);
            if (kind == PlaylistKind.None)
            {
                return url;
            }

            var text = await DownloadPlaylistAsync(url, cancellationToken);
            if (kind == PlaylistKind.M3u && text.Contains("#EXT-X-", StringComparison.Ordinal))
            {
                return url;
            }

            var entries = kind switch
            {
                PlaylistKind.Pls => ParsePls(text),
                PlaylistKind.Asx => ParseAsx(text),
                _ => ParseM3u(text),
            };

            url = entries
                      .Select(e => Uri.TryCreate(url, e, out var u) ? u : null)
                      .FirstOrDefault(u => u is not null && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
                  ?? throw new InvalidDataException($"Playlist {url} contains no usable stream.");
        }

        return url;
    }

    public static PlaylistKind GetPlaylistKind(Uri url) =>
        Path.GetExtension(url.AbsolutePath).ToLowerInvariant() switch
        {
            ".pls" => PlaylistKind.Pls,
            ".m3u" => PlaylistKind.M3u,
            ".asx" => PlaylistKind.Asx,
            _ => PlaylistKind.None,
        };

    public static IReadOnlyList<string> ParsePls(string text) =>
        SplitLines(text)
            .Where(l => l.StartsWith("File", StringComparison.OrdinalIgnoreCase) && l.Contains('='))
            .Select(l => l[(l.IndexOf('=') + 1)..].Trim())
            .Where(l => l.Length > 0)
            .ToList();

    public static IReadOnlyList<string> ParseM3u(string text) =>
        SplitLines(text)
            .Where(l => !l.StartsWith('#'))
            .ToList();

    public static IReadOnlyList<string> ParseAsx(string text) =>
        AsxRef().Matches(text)
            .Select(m => WebUtility.HtmlDecode(m.Groups["url"].Value.Trim()))
            .ToList();

    private static string[] SplitLines(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private async Task<string> DownloadPlaylistAsync(Uri url, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        // Some "playlists" are really the audio stream; never read more than a playlist would need.
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[MaxPlaylistBytes];
        var total = 0;
        int read;
        while (total < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken)) > 0)
        {
            total += read;
        }

        return Encoding.UTF8.GetString(buffer, 0, total);
    }
}

public enum PlaylistKind
{
    None,
    Pls,
    M3u,
    Asx,
}
