using ZapperRadio.Core.Models;
using ZapperRadio.Core.Settings;

namespace ZapperRadio.Core.Tests;

public class FavoriteTrackTests
{
    [Theory]
    [InlineData("Queen - Bohemian Rhapsody", true)]
    [InlineData("QUEEN  -  bohemian rhapsody ", true)]
    [InlineData("Queen - Killer Queen", false)]
    public void IsSameSong_IgnoresCapitalsAndSpacing(string title, bool expected)
    {
        var track = new FavoriteTrack("Queen - Bohemian Rhapsody", "Radio 2", DateTimeOffset.Now);

        Assert.Equal(expected, track.IsSameSong(title));
    }

    [Fact]
    public void SettingsStore_KeepsFavoriteTracks()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ZapperRadio-{Guid.NewGuid():N}", "settings.json");
        var track = new FavoriteTrack("Queen - Bohemian Rhapsody", "Radio 2", new DateTimeOffset(2026, 9, 17, 20, 15, 0, TimeSpan.FromHours(2)));
        try
        {
            var store = new SettingsStore(path);
            store.Save(new AppSettings { FavoriteTracks = [track] });

            Assert.Equal([track], store.Load().FavoriteTracks);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void SettingsStore_KeepsTheAdBreakChoiceFromItsOldName()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ZapperRadio-{Guid.NewGuid():N}", "settings.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """{ "Volume": 0.5, "SkipAdBreaks": true }""");
            var store = new SettingsStore(path);

            var settings = store.Load();
            Assert.True(settings.ZappOnAdBreaks);

            store.Save(settings);
            var saved = File.ReadAllText(path);
            Assert.DoesNotContain("SkipAdBreaks", saved);
            Assert.True(store.Load().ZappOnAdBreaks);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
