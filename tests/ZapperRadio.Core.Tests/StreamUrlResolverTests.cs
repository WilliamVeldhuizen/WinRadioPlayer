using ZapperRadio.Core.Settings;
using ZapperRadio.Core.Models;
using ZapperRadio.Core.Streaming;

namespace ZapperRadio.Core.Tests;

public class StreamUrlResolverTests
{
    [Fact]
    public async Task ResolvesPlsToFirstEntry()
    {
        var resolver = Resolver(uri => uri.AbsolutePath == "/listen.pls"
            ? "[playlist]\nNumberOfEntries=2\nFile1=http://stream.example:8000/live\nTitle1=Live\nFile2=http://backup.example/live\n"
            : null);

        var result = await resolver.ResolveAsync(new Uri("http://193.197.85.26:8000/listen.pls"));

        Assert.Equal(new Uri("http://stream.example:8000/live"), result);
    }

    [Fact]
    public async Task ResolvesNestedAndRelativeM3u()
    {
        var resolver = Resolver(uri => uri.AbsolutePath switch
        {
            "/radio.m3u" => "#EXTM3U\n#EXTINF:-1,Radio\nsub/inner.pls\n",
            "/sub/inner.pls" => "File1=https://cdn.example/radio.mp3",
            _ => null,
        });

        var result = await resolver.ResolveAsync(new Uri("http://host.example/radio.m3u"));

        Assert.Equal(new Uri("https://cdn.example/radio.mp3"), result);
    }

    [Fact]
    public async Task LeavesHlsAndDirectStreamsAlone()
    {
        var resolver = Resolver(_ => "#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-STREAM-INF:BANDWIDTH=64000\nchunk.m3u8\n");

        var hlsInM3u = new Uri("http://host.example/live.m3u");
        Assert.Equal(hlsInM3u, await resolver.ResolveAsync(hlsInM3u));

        var hls = new Uri("http://edge.iono.fm/xhls/fmr_live_medium.m3u8");
        Assert.Equal(hls, await resolver.ResolveAsync(hls));

        var direct = new Uri("http://vibration.stream2net.eu:8350/;stream/1");
        Assert.Equal(direct, await resolver.ResolveAsync(direct));
    }

    [Fact]
    public void ParsesAsx()
    {
        const string asx = """<asx version="3.0"><entry><ref href="http://a.example/stream?x=1&amp;y=2" /></entry></asx>""";

        Assert.Equal(["http://a.example/stream?x=1&y=2"], StreamUrlResolver.ParseAsx(asx));
    }

    [Fact]
    public void SettingsRoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), "ZapperRadioTests", Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            var store = new SettingsStore(path);
            Assert.Empty(store.Load().Favorites);

            store.Save(new AppSettings
            {
                Volume = 0.5,
                Country = "Netherlands",
                Favorites = [new Station("Qmusic", "pop", "Netherlands", "", "https://stream.qmusic.nl/qmusic/mp3")],
            });

            var loaded = store.Load();
            Assert.Equal(0.5, loaded.Volume);
            Assert.Equal("Netherlands", loaded.Country);
            Assert.Equal("Qmusic", Assert.Single(loaded.Favorites).Name);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    private static StreamUrlResolver Resolver(Func<Uri, string?> content) => new(new HttpClient(new FakeHandler(content)));
}
