using ZapperRadio.Core.Playback;

namespace ZapperRadio.Core.Tests;

public class FavoriteRingTests
{
    private static readonly string[] Favorites = ["http://a", "http://b", "http://c"];

    [Fact]
    public void Step_GoesToTheNextFavorite()
    {
        Assert.Equal("http://b", FavoriteRing.Step(Favorites, "http://a", 1));
    }

    [Fact]
    public void Step_GoesToThePreviousFavorite()
    {
        Assert.Equal("http://a", FavoriteRing.Step(Favorites, "http://b", -1));
    }

    [Fact]
    public void Step_WrapsPastTheLastFavorite()
    {
        Assert.Equal("http://a", FavoriteRing.Step(Favorites, "http://c", 1));
    }

    [Fact]
    public void Step_WrapsBeforeTheFirstFavorite()
    {
        Assert.Equal("http://c", FavoriteRing.Step(Favorites, "http://a", -1));
    }

    [Fact]
    public void Step_StartsAtTheFirstFavoriteWhenNothingIsPlaying()
    {
        Assert.Equal("http://a", FavoriteRing.Step(Favorites, null, 1));
    }

    [Fact]
    public void Step_StartsAtTheLastFavoriteWhenGoingBackFromNothing()
    {
        Assert.Equal("http://c", FavoriteRing.Step(Favorites, null, -1));
    }

    [Fact]
    public void Step_StartsAtAnEndWhenTheStationIsNotAFavorite()
    {
        Assert.Equal("http://a", FavoriteRing.Step(Favorites, "http://elsewhere", 1));
        Assert.Equal("http://c", FavoriteRing.Step(Favorites, "http://elsewhere", -1));
    }

    [Fact]
    public void Step_StaysOnTheOnlyFavorite()
    {
        Assert.Equal("http://a", FavoriteRing.Step(["http://a"], "http://a", 1));
    }

    [Fact]
    public void Step_HasNowhereToGoWithoutFavorites()
    {
        Assert.Null(FavoriteRing.Step([], null, 1));
    }
}
