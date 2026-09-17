using System.Net;
using System.Text;
using WinRadioPlayer.Core.Streaming;

namespace WinRadioPlayer.Core.Tests;

public class IcyProxyTests
{
    [Theory]
    [InlineData("StreamTitle='Artist - Title';StreamUrl='';", "Artist - Title", false)]
    [InlineData("StreamTitle='Don't Stop Me Now';", "Don't Stop Me Now", false)]
    [InlineData("StreamTitle='';StreamUrl='';adw_ad='true';durationMilliseconds='20053';insertionType='preroll';", null, true)]
    [InlineData("StreamTitle='  ';", null, false)]
    [InlineData("", null, false)]
    public void ParsesMetadata(string text, string? title, bool isAd)
    {
        Assert.Equal(new IcyMetadata(title, isAd), IcyMetadata.Parse(text));
    }

    [Fact]
    public void ParsesUtf8AndFallsBackToLatin1()
    {
        Assert.Equal("Café", IcyMetadata.Parse(Block(Encoding.UTF8, "StreamTitle='Café';")).Title);
        Assert.Equal("Café", IcyMetadata.Parse(Block(Encoding.Latin1, "StreamTitle='Café';")).Title);
    }

    [Fact]
    public async Task RelayStripsMetadataBlocks()
    {
        var titles = new List<string?>();
        var output = new MemoryStream();

        await IcyProxy.RelayAsync(new MemoryStream(IcyStream(out var audio)), output, 4, m => titles.Add(m.Title), CancellationToken.None);

        Assert.Equal(audio, output.ToArray());
        Assert.Equal(["First", "Second"], titles);
    }

    [Fact]
    public async Task ProxyRelaysAudioAndReportsTitles()
    {
        using var upstream = new HttpClient(new StreamHandler(request =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(IcyStream(out _)) };
            response.Content.Headers.ContentType = new("audio/mpeg");
            response.Headers.Add("icy-metaint", "4");
            return response;
        }));
        using var proxy = new IcyProxy(upstream);
        var titles = new List<string?>();
        var url = proxy.Register(new Uri("http://radio.example/live.mp3"), m => { lock (titles) titles.Add(m.Title); });

        using var client = new HttpClient();
        using var response = await client.GetAsync(url);

        IcyStream(out var audio);
        Assert.Equal("audio/mpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(audio, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(["First", "Second"], titles);
        Assert.EndsWith("/live.mp3", url.AbsolutePath);
        Assert.True(url.IsLoopback);
    }

    [Fact]
    public async Task ProxyRedirectsToStationWhenItCannotRelay()
    {
        using var upstream = new HttpClient(new StreamHandler(_ => throw new HttpRequestException("ICY 200 OK")));
        using var proxy = new IcyProxy(upstream);
        var url = proxy.Register(new Uri("http://radio.example/live"), _ => { });

        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal(new Uri("http://radio.example/live"), response.Headers.Location);
    }

    [Fact]
    public async Task UnregisteredUrlStopsListening()
    {
        using var proxy = new IcyProxy(new HttpClient(new StreamHandler(_ => throw new InvalidOperationException())));
        var url = proxy.Register(new Uri("http://radio.example/live"), _ => { });
        proxy.Unregister(url);

        using var client = new HttpClient();
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(url));
    }

    /// <summary>Audio "abcdefghij" with a metadata block after the first and second 4 bytes.</summary>
    private static byte[] IcyStream(out byte[] audio)
    {
        audio = "abcdefghij"u8.ToArray();
        var stream = new MemoryStream();
        stream.Write(audio, 0, 4);
        WriteBlock(stream, "StreamTitle='First';");
        stream.Write(audio, 4, 4);
        WriteBlock(stream, "StreamTitle='Second';StreamUrl='';");
        stream.Write(audio, 8, 2);
        return stream.ToArray();
    }

    private static void WriteBlock(Stream stream, string text)
    {
        var block = Block(Encoding.UTF8, text);
        stream.WriteByte((byte)(block.Length / 16));
        stream.Write(block);
    }

    private static byte[] Block(Encoding encoding, string text)
    {
        var bytes = encoding.GetBytes(text);
        var block = new byte[(bytes.Length + 15) / 16 * 16];
        bytes.CopyTo(block, 0);
        return block;
    }

    private sealed class StreamHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
