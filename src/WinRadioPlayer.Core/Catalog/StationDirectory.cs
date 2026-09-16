using System.Text.RegularExpressions;
using WinRadioPlayer.Core.Models;

namespace WinRadioPlayer.Core.Catalog;

/// <summary>
/// Finds the newest <c>stations-yyyy-MM-dd.rsd</c> file in the rb2rs directory listing,
/// downloads it into a local cache and parses it. Falls back to the cache when offline.
/// </summary>
public sealed partial class StationDirectory(HttpClient http, string cacheFolder, Uri? indexUri = null)
{
    public static readonly Uri DefaultIndexUri = new("http://rb2rs.freemyip.com/");

    private readonly Uri _indexUri = indexUri ?? DefaultIndexUri;

    [GeneratedRegex("href=\"(?<file>stations-\\d{4}-\\d{2}-\\d{2}\\.rsd)\"", RegexOptions.IgnoreCase)]
    private static partial Regex StationFileLink();

    /// <summary>Returns the newest station file name linked from the directory listing, or null.</summary>
    public static string? FindLatestFileName(string indexHtml) =>
        StationFileLink().Matches(indexHtml)
            .Select(m => m.Groups["file"].Value)
            // The yyyy-MM-dd date in the name sorts lexicographically.
            .OrderDescending(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    public async Task<StationCatalog> LoadAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(cacheFolder);

        string path;
        bool fromCache;
        try
        {
            path = await DownloadLatestAsync(cancellationToken);
            fromCache = false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or StationDirectoryException
                                   && !cancellationToken.IsCancellationRequested)
        {
            path = FindNewestCachedFile() ?? throw new StationDirectoryException(
                $"De zenderlijst kon niet worden opgehaald van {_indexUri} en er is geen lokale kopie. ({ex.Message})", ex);
            fromCache = true;
        }

        var list = await Task.Run(() =>
        {
            using var reader = new StreamReader(path, System.Text.Encoding.UTF8);
            return RsdParser.Parse(reader);
        }, cancellationToken);

        return new StationCatalog(Path.GetFileName(path), list.GeneratedAt, list.Stations, fromCache);
    }

    private async Task<string> DownloadLatestAsync(CancellationToken cancellationToken)
    {
        var indexHtml = await http.GetStringAsync(_indexUri, cancellationToken);
        var fileName = FindLatestFileName(indexHtml)
                       ?? throw new StationDirectoryException($"Geen stations-*.rsd bestand gevonden op {_indexUri}.");

        var target = Path.Combine(cacheFolder, fileName);
        if (!File.Exists(target))
        {
            var temp = target + ".download";
            using (var response = await http.GetAsync(new Uri(_indexUri, fileName), HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var destination = File.Create(temp);
                await source.CopyToAsync(destination, cancellationToken);
            }

            File.Move(temp, target, overwrite: true);
        }

        DeleteCachedFilesExcept(target);
        return target;
    }

    private string? FindNewestCachedFile() =>
        Directory.EnumerateFiles(cacheFolder, "stations-*.rsd")
            .OrderDescending(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    private void DeleteCachedFilesExcept(string keep)
    {
        foreach (var file in Directory.EnumerateFiles(cacheFolder, "stations-*.rsd*"))
        {
            if (!string.Equals(file, keep, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(file); } catch (IOException) { }
            }
        }
    }
}

public sealed record StationCatalog(string FileName, DateTime? GeneratedAt, IReadOnlyList<Station> Stations, bool FromCache);

public sealed class StationDirectoryException(string message, Exception? inner = null) : Exception(message, inner);
