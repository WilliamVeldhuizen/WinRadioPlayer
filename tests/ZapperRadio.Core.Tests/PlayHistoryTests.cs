using ZapperRadio.Core.Models;
using ZapperRadio.Core.Playback;

namespace ZapperRadio.Core.Tests;

public class PlayHistoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 21, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Add_KeepsTheNewestSongFirst()
    {
        var history = new PlayHistory();

        history.Add("Queen - Bohemian Rhapsody", "Radio 2", "http://radio2", Now);
        history.Add("ABBA - Waterloo", "Qmusic", "http://qmusic", Now.AddMinutes(1));

        Assert.Equal(["ABBA - Waterloo", "Queen - Bohemian Rhapsody"], history.Entries.Select(e => e.Title));
    }

    [Fact]
    public void Add_NormalizesTheTitle()
    {
        var history = new PlayHistory();

        var track = history.Add("QUEEN - BOHEMIAN RHAPSODY", "Radio 2", "http://radio2", Now);

        Assert.Equal("Queen - Bohemian Rhapsody", track?.Title);
    }

    [Fact]
    public void Add_SkipsTheTitleAStationIsAlreadyOn()
    {
        var history = new PlayHistory();
        history.Add("Queen - Bohemian Rhapsody", "Radio 2", "http://radio2", Now);

        // A reconnect repeats the title of the song that is still playing.
        Assert.Null(history.Add("QUEEN - Bohemian Rhapsody", "Radio 2", "http://radio2", Now.AddSeconds(10)));
        // Another station playing the same song is a separate thing to hear.
        Assert.NotNull(history.Add("Queen - Bohemian Rhapsody", "Qmusic", "http://qmusic", Now.AddSeconds(20)));
        // And so is the same station playing it again later.
        Assert.NotNull(history.Add("ABBA - Waterloo", "Radio 2", "http://radio2", Now.AddMinutes(4)));
        Assert.NotNull(history.Add("Queen - Bohemian Rhapsody", "Radio 2", "http://radio2", Now.AddMinutes(8)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Radio 2")]
    [InlineData("radio 2")]
    public void Add_SkipsWhatIsNotASong(string? title) =>
        Assert.Null(new PlayHistory().Add(title, "Radio 2", "http://radio2", Now));

    [Fact]
    public void Prune_DropsSongsOlderThanTheRetention()
    {
        var history = new PlayHistory();
        history.Add("Queen - Bohemian Rhapsody", "Radio 2", "http://radio2", Now - PlayHistory.Retention - TimeSpan.FromMinutes(1));
        history.Add("ABBA - Waterloo", "Radio 2", "http://radio2", Now - TimeSpan.FromHours(11));

        history.Prune(Now);

        Assert.Equal(["ABBA - Waterloo"], history.Entries.Select(e => e.Title));
    }

    [Fact]
    public void Add_DropsTheOldestBeyondTheCeiling()
    {
        var history = new PlayHistory();
        for (var i = 0; i <= PlayHistory.MaxEntries; i++)
        {
            history.Add($"Song {i}", "Radio 2", "http://radio2", Now.AddSeconds(i));
        }

        Assert.Equal(PlayHistory.MaxEntries, history.Count);
        Assert.Equal($"Song {PlayHistory.MaxEntries}", history.Entries[0].Title);
        Assert.Equal("Song 1", history.Entries[^1].Title);
    }

    [Fact]
    public void Load_SortsAndPrunesAndRemembersWhatEachStationWasOn()
    {
        var history = new PlayHistory();

        history.Load(
        [
            new PlayedTrack("ABBA - Waterloo", "Radio 2", "http://radio2", Now - TimeSpan.FromHours(1)),
            new PlayedTrack("Queen - Bohemian Rhapsody", "Radio 2", "http://radio2", Now - TimeSpan.FromHours(2)),
            new PlayedTrack("Pixies - Where Is My Mind", "Radio 2", "http://radio2", Now - TimeSpan.FromHours(13)),
        ], Now);

        Assert.Equal(["ABBA - Waterloo", "Queen - Bohemian Rhapsody"], history.Entries.Select(e => e.Title));
        // The station is still on the song it was on when the app closed, so it is not recorded twice.
        Assert.Null(history.Add("ABBA - Waterloo", "Radio 2", "http://radio2", Now));
    }

    [Fact]
    public void PlayHistoryStore_KeepsTheSongs()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ZapperRadio-{Guid.NewGuid():N}", "play-history.json");
        var track = new PlayedTrack("Queen - Bohemian Rhapsody", "Radio 2", "http://radio2", Now);
        try
        {
            var store = new PlayHistoryStore(path);
            store.Save([track]);

            Assert.Equal([track], store.Load());
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void PlayHistoryStore_StartsEmptyWithoutAFile() =>
        Assert.Empty(new PlayHistoryStore(Path.Combine(Path.GetTempPath(), $"ZapperRadio-{Guid.NewGuid():N}", "play-history.json")).Load());
}
