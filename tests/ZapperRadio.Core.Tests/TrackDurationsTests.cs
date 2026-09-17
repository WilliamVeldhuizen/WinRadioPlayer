using ZapperRadio.Core.Streaming;

namespace ZapperRadio.Core.Tests;

public class TrackDurationsTests
{
    private const string QueenResults = """
        {"resultCount":3,"results":[
          {"artistName":"Queen Tribute Band","trackName":"Bohemian Rhapsody","trackTimeMillis":400000},
          {"artistName":"Queen","trackName":"Bohemian Rhapsody (Remastered 2011)","trackTimeMillis":354320},
          {"artistName":"Queen","trackName":"Bohemian Rhapsody","trackTimeMillis":355000}
        ]}
        """;

    [Theory]
    [InlineData("Queen - Bohemian Rhapsody", "Queen", "Bohemian Rhapsody")]
    [InlineData("AC/DC - Back In Black - Live", "AC/DC", "Back In Black - Live")]
    [InlineData("Radio 538", null, null)]
    [InlineData(" - Title", null, null)]
    [InlineData("Artist - ", null, null)]
    public void SplitTitle_SplitsAtFirstDash(string streamTitle, string? artist, string? title)
    {
        var song = TrackDurations.SplitTitle(streamTitle);

        Assert.Equal(artist, song?.Artist);
        Assert.Equal(title, song?.Title);
    }

    [Theory]
    [InlineData("Queen", "Bohemian Rhapsody", 354320)]
    [InlineData("QUEEN", "Bohemian Rhapsody (Radio Edit)", 354320)]
    [InlineData("Queen feat. Nobody", "Bohemian Rhapsody", 354320)]
    [InlineData("Que", "Bohemian Rhapsody", null)]
    [InlineData("Queen", "Killer Queen", null)]
    public void FindDuration_OnlyTrustsMatchingArtistAndTitle(string artist, string title, int? expectedMs)
    {
        var expected = expectedMs is { } ms ? TimeSpan.FromMilliseconds(ms) : (TimeSpan?)null;

        Assert.Equal(expected, TrackDurations.FindDuration(QueenResults, artist, title));
    }

    [Fact]
    public void FindDuration_IgnoresAccentsAndImplausibleLengths()
    {
        const string json = """
            {"results":[
              {"artistName":"Beyonce","trackName":"Intro","trackTimeMillis":5000},
              {"artistName":"Beyoncé","trackName":"Halo","trackTimeMillis":261640}
            ]}
            """;

        Assert.Null(TrackDurations.FindDuration(json, "Beyonce", "Intro"));
        Assert.Equal(TimeSpan.FromMilliseconds(261640), TrackDurations.FindDuration(json, "Beyonce", "Halo"));
    }

    [Fact]
    public async Task GetAsync_CachesSongsAndRetriesFailures()
    {
        var requests = 0;
        var handler = new FakeHandler(uri =>
        {
            requests++;
            return Uri.UnescapeDataString(uri.Query).Contains("Queen Bohemian Rhapsody") ? QueenResults : """{"results":[]}""";
        });
        var durations = new TrackDurations(new HttpClient(handler), TimeSpan.Zero) { SearchUrl = new Uri("https://search.example/") };

        Assert.Equal(TimeSpan.FromMilliseconds(354320), await durations.GetAsync("Queen - Bohemian Rhapsody"));
        Assert.Equal(TimeSpan.FromMilliseconds(354320), await durations.GetAsync("Queen - Bohemian Rhapsody"));
        Assert.Null(await durations.GetAsync("Nobody - Unknown Song"));
        Assert.Null(await durations.GetAsync("Radio 538"));
        Assert.Equal(2, requests);

        handler.Offline = true;
        Assert.Null(await durations.GetAsync("Queen - We Will Rock You"));
        handler.Offline = false;
        Assert.Null(await durations.GetAsync("Queen - We Will Rock You"));
        // The failed lookup was not cached, so the song was asked for again once online.
        Assert.Equal(3, requests);
    }
}
