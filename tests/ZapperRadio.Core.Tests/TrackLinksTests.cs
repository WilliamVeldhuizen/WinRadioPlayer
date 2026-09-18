using ZapperRadio.Core.Models;

namespace ZapperRadio.Core.Tests;

public class TrackLinksTests
{
    [Theory]
    [InlineData("Queen - Bohemian Rhapsody", "Queen Bohemian Rhapsody")]
    // The stations that send the two the other way around need nothing special: a search ignores the order.
    [InlineData("Bohemian Rhapsody - Queen", "Bohemian Rhapsody Queen")]
    [InlineData("Coldplay – Yellow", "Coldplay Yellow")]
    [InlineData("Coldplay | Yellow", "Coldplay Yellow")]
    [InlineData("  AC/DC   -   T.N.T.  ", "AC/DC T.N.T.")]
    // A dash inside a name is part of it, and an addition names the recording.
    [InlineData("Jay-Z - 99 Problems", "Jay-Z 99 Problems")]
    [InlineData("Queen - Bohemian Rhapsody (Radio Edit)", "Queen Bohemian Rhapsody (Radio Edit)")]
    [InlineData("Radio 538", "Radio 538")]
    [InlineData("   ", null)]
    [InlineData("- | -", null)]
    [InlineData(null, null)]
    public void SearchTerm_KeepsTheWordsAndDropsTheSeparator(string? title, string? expected)
    {
        Assert.Equal(expected, TrackLinks.SearchTerm(title));
    }

    [Fact]
    public void SpotifyIsSearchedInItsAppAndOnTheWeb()
    {
        Assert.Equal(
            new Uri("spotify:search:Queen%20Bohemian%20Rhapsody"),
            TrackLinks.App(MusicService.Spotify, "Queen - Bohemian Rhapsody"));
        Assert.Equal(
            new Uri("https://open.spotify.com/search/Queen%20Bohemian%20Rhapsody"),
            TrackLinks.Web(MusicService.Spotify, "Queen - Bohemian Rhapsody"));
    }

    [Fact]
    public void YouTubeIsSearchedOnTheWebOnly()
    {
        Assert.Null(TrackLinks.App(MusicService.YouTube, "Queen - Bohemian Rhapsody"));
        Assert.Equal(
            new Uri("https://www.youtube.com/results?search_query=Queen%20Bohemian%20Rhapsody"),
            TrackLinks.Web(MusicService.YouTube, "Queen - Bohemian Rhapsody"));
    }

    [Fact]
    public void SpecialCharactersAreEscaped()
    {
        Assert.Equal(
            new Uri("https://www.youtube.com/results?search_query=AC%2FDC%20T.N.T."),
            TrackLinks.Web(MusicService.YouTube, "AC/DC - T.N.T."));
    }

    [Theory]
    [InlineData(MusicService.Spotify)]
    [InlineData(MusicService.YouTube)]
    public void ATitleWithoutWordsHasNoLink(MusicService service)
    {
        Assert.Null(TrackLinks.App(service, " - "));
        Assert.Null(TrackLinks.Web(service, " - "));
        Assert.Null(TrackLinks.Web(service, null));
    }
}
