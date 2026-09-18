using ZapperRadio.Core.Models;
using ZapperRadio.Core.Playback;

namespace ZapperRadio.Core.Tests;

public class PlayedTrackFilterTests
{
    private static readonly PlayedTrack Track =
        new("Queen - Bohemian Rhapsody", "Radio 2", "http://radio2", new DateTimeOffset(2026, 9, 18, 21, 0, 0, TimeSpan.FromHours(2)));

    [Fact]
    public void Matches_FindsTheArtistAndTheTitle()
    {
        Assert.True(new PlayedTrackFilter("queen").Matches(Track));
        Assert.True(new PlayedTrackFilter("rhapsody").Matches(Track));
        Assert.False(new PlayedTrackFilter("waterloo").Matches(Track));
    }

    [Fact]
    public void Matches_FindsTheStation()
    {
        Assert.True(new PlayedTrackFilter("radio 2").Matches(Track));
        Assert.False(new PlayedTrackFilter("qmusic").Matches(Track));
    }

    [Fact]
    public void Matches_NeedsEveryWord_ButNotInOrder()
    {
        // The song and the station can be mixed, so the query is not one phrase.
        Assert.True(new PlayedTrackFilter("rhapsody queen").Matches(Track));
        Assert.True(new PlayedTrackFilter("radio bohemian").Matches(Track));
        Assert.False(new PlayedTrackFilter("queen waterloo").Matches(Track));
    }

    [Fact]
    public void Matches_IgnoresCaseAndSurroundingSpace()
    {
        Assert.True(new PlayedTrackFilter("  QUEEN   bOhEmIaN ").Matches(Track));
    }

    [Fact]
    public void Matches_TakesEverythingWhenTheQueryIsEmpty()
    {
        foreach (var query in new string?[] { null, "", "   " })
        {
            var filter = new PlayedTrackFilter(query);
            Assert.True(filter.IsEmpty);
            Assert.True(filter.Matches(Track));
        }
    }

    [Fact]
    public void Matches_HasNoTypoTolerance()
    {
        // Unlike the station search: a wrong song is harder to spot than a wrong station.
        Assert.False(new PlayedTrackFilter("bohemain").Matches(Track));
    }
}
