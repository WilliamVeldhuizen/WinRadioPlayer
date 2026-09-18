using ZapperRadio.Core.Catalog;
using ZapperRadio.Core.Models;

namespace ZapperRadio.Core.Tests;

public class StationLogosTests
{
    private static Station Station(string url) => new("Radio X", "pop", "Netherlands", "Dutch", url);

    [Fact]
    public void BuildQuery_IncludesCountry_SoACommonNameLikeRadio10PicksTheRightCountry()
    {
        var query = StationLogos.BuildQuery(new Station("Radio 10", "", "Netherlands", "", "http://station.example/"));

        Assert.Contains("name=Radio%2010", query);
        Assert.Contains("countrycode=NL", query);
    }

    [Fact]
    public void BuildQuery_OmitsCountryFilter_WhenStationHasNoCountry()
    {
        var query = StationLogos.BuildQuery(new Station("Radio 10", "", "", "", "http://station.example/"));

        Assert.DoesNotContain("country", query);
    }

    [Fact]
    public void FindCandidates_PrefersMatchingUrl()
    {
        const string json = """
            [
                {"url":"http://other.example/", "favicon":"http://other.example/logo.png"},
                {"url":"http://station.example/", "favicon":"http://station.example/logo.png"}
            ]
            """;

        var candidates = StationLogos.FindCandidates(json, "http://station.example/");

        Assert.Equal(["http://station.example/logo.png", "http://other.example/logo.png"], candidates);
    }

    [Fact]
    public void FindCandidates_SkipsMissingOrInvalidFavicons()
    {
        const string json = """[{"url":"http://a.example/"}, {"url":"http://b.example/", "favicon":""}, {"url":"http://c.example/", "favicon":"not-a-url"}]""";

        Assert.Empty(StationLogos.FindCandidates(json, "http://a.example/"));
    }

    [Fact]
    public async Task GetLogoUrlAsync_UsesTheFirstReachableCandidateAndCaches()
    {
        var cache = Path.Combine(Path.GetTempPath(), "ZapperRadioTests", Guid.NewGuid().ToString("N"));
        try
        {
            var searchRequests = 0;
            var handler = new FakeHandler(uri =>
            {
                if (uri.AbsolutePath.Contains("stations/search"))
                {
                    searchRequests++;
                    return """[{"url":"http://station.example/", "favicon":"http://broken.example/logo.png"}, {"url":"http://other.example/", "favicon":"http://good.example/logo.png"}]""";
                }

                // The reachability check: only the second candidate resolves.
                return uri.ToString() == "http://good.example/logo.png" ? "" : null;
            });
            var logos = new StationLogos(new HttpClient(handler), cache, [new Uri("https://api.example/")]);

            var logo = await logos.GetLogoUrlAsync(Station("http://station.example/"));
            Assert.Equal("http://good.example/logo.png", logo);

            // Cached to disk, so a fresh instance does not call the API again.
            var reloaded = new StationLogos(new HttpClient(handler), cache, [new Uri("https://api.example/")]);
            Assert.Equal(logo, await reloaded.GetLogoUrlAsync(Station("http://station.example/")));
            Assert.Equal(1, searchRequests);
        }
        finally
        {
            if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true);
        }
    }

    [Fact]
    public async Task Clear_ForgetsTheLogosOnDiskAndInThisSession_SoTheyAreLookedUpAgain()
    {
        var cache = Path.Combine(Path.GetTempPath(), "ZapperRadioTests", Guid.NewGuid().ToString("N"));
        try
        {
            var searchRequests = 0;
            var handler = new FakeHandler(uri =>
            {
                if (uri.AbsolutePath.Contains("stations/search"))
                {
                    searchRequests++;
                    return """[{"url":"http://station.example/", "favicon":"http://good.example/logo.png"}]""";
                }

                return "";
            });
            var logos = new StationLogos(new HttpClient(handler), cache, [new Uri("https://api.example/")]);

            Assert.Equal("http://good.example/logo.png", await logos.GetLogoUrlAsync(Station("http://station.example/")));
            Assert.Equal(1, searchRequests);

            // The station list lives in the same folder and must survive.
            var stationList = Path.Combine(cache, "stations-2026-09-17.rsd");
            await File.WriteAllTextAsync(stationList, "the station list");

            logos.Clear();

            Assert.Empty(Directory.GetFiles(cache, "logo-*.txt"));
            Assert.True(File.Exists(stationList));

            // The same instance looks it up again rather than answering from what it found before.
            Assert.Equal("http://good.example/logo.png", await logos.GetLogoUrlAsync(Station("http://station.example/")));
            Assert.Equal(2, searchRequests);
        }
        finally
        {
            if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true);
        }
    }

    [Fact]
    public void Clear_DoesNothingWhenNothingWasCachedYet()
    {
        var cache = Path.Combine(Path.GetTempPath(), "ZapperRadioTests", Guid.NewGuid().ToString("N"));

        new StationLogos(new HttpClient(new FakeHandler(_ => null)), cache).Clear();

        Assert.False(Directory.Exists(cache));
    }

    [Fact]
    public async Task GetLogoUrlAsync_ReturnsNullWhenNoFaviconIsReachable()
    {
        var cache = Path.Combine(Path.GetTempPath(), "ZapperRadioTests", Guid.NewGuid().ToString("N"));
        try
        {
            var handler = new FakeHandler(uri => uri.AbsolutePath.Contains("stations/search")
                ? """[{"url":"http://station.example/", "favicon":"http://broken.example/logo.png"}]"""
                : null);
            var logos = new StationLogos(new HttpClient(handler), cache, [new Uri("https://api.example/")]);

            Assert.Null(await logos.GetLogoUrlAsync(Station("http://station.example/")));
        }
        finally
        {
            if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true);
        }
    }
}
