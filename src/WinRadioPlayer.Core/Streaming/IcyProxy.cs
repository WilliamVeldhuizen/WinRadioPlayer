using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace WinRadioPlayer.Core.Streaming;

/// <summary>
/// A local HTTP relay between the media player and a radio stream. The player cannot read the song
/// titles that Shoutcast/Icecast servers interleave with the audio, so the relay requests them
/// (<c>Icy-MetaData: 1</c>), reports them and passes only the audio on. It is the same single
/// connection per station, so it costs no extra bandwidth.
/// When the station cannot be relayed (playlists, errors, unusual servers) the player is redirected
/// to the original URL, so playback is never worse than without the relay; only the titles are missing.
/// </summary>
public sealed class IcyProxy : IDisposable
{
    private const int MaxRequestHeadBytes = 16 * 1024;
    private static readonly TimeSpan RequestHeadTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan UpstreamConnectTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<string, Registration> _registrations = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _shutdown = new();
    private int _addressCounter;

    /// <param name="http">Used for the upstream streams; should have an infinite <see cref="HttpClient.Timeout"/>.</param>
    public IcyProxy(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// Returns a local URL that relays <paramref name="upstream"/>. <paramref name="onMetadata"/> is called
    /// on a background thread for every metadata block, until <see cref="Unregister"/> is called.
    /// </summary>
    public Uri Register(Uri upstream, Action<IcyMetadata> onMetadata)
    {
        ObjectDisposedException.ThrowIf(_shutdown.IsCancellationRequested, this);

        var id = Guid.NewGuid().ToString("N");
        var listener = StartListener();
        var registration = _registrations[id] = new Registration(upstream, onMetadata, listener);
        _ = AcceptLoopAsync(registration);

        // Keep the original file name, in case the player uses the extension as a format hint.
        var name = upstream.Segments.LastOrDefault()?.Trim('/') is { Length: > 0 } segment ? segment : "stream";
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        return new Uri($"http://{endpoint.Address}:{endpoint.Port}/{id}/{name}");
    }

    /// <summary>Stops relaying the URL and ends a connection that is still using it.</summary>
    public void Unregister(Uri relayUrl)
    {
        if (GetId(relayUrl.AbsolutePath) is { } id && _registrations.TryRemove(id, out var registration))
        {
            registration.Stop();
        }
    }

    /// <summary>
    /// The Windows media player opens at most two connections per host, so with every favorite streaming
    /// through one address most of them would never start. Each relay therefore gets its own loopback
    /// address: all of 127.0.0.0/8 reaches this PC and none of it is reachable from the network.
    /// </summary>
    private TcpListener StartListener()
    {
        var n = (Interlocked.Increment(ref _addressCounter) & 0xFFFF) % 0xFFFE + 1;
        try
        {
            var listener = new TcpListener(new IPAddress([127, 1, (byte)(n >> 8), (byte)n]), 0);
            listener.Start();
            return listener;
        }
        catch (SocketException)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return listener;
        }
    }

    /// <summary>
    /// Copies <paramref name="source"/> to <paramref name="audio"/> without the metadata blocks that
    /// follow every <paramref name="metaInterval"/> audio bytes (0 = no metadata), until the source ends.
    /// </summary>
    public static async Task RelayAsync(
        Stream source, Stream audio, int metaInterval, Action<IcyMetadata> onMetadata, CancellationToken cancellationToken)
    {
        if (metaInterval <= 0)
        {
            await source.CopyToAsync(audio, cancellationToken);
            return;
        }

        var buffer = new byte[16 * 1024];
        var metadata = new byte[255 * 16];
        while (true)
        {
            for (var remaining = metaInterval; remaining > 0;)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, Math.Min(remaining, buffer.Length)), cancellationToken);
                if (read == 0)
                {
                    return;
                }

                await audio.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                remaining -= read;
            }

            if (await source.ReadAtLeastAsync(metadata.AsMemory(0, 1), 1, throwOnEndOfStream: false, cancellationToken) == 0)
            {
                return;
            }

            var length = metadata[0] * 16;
            if (length == 0)
            {
                // Most servers only send a block when the title changes.
                continue;
            }

            if (await source.ReadAtLeastAsync(metadata.AsMemory(0, length), length, throwOnEndOfStream: false, cancellationToken) < length)
            {
                return;
            }

            onMetadata(IcyMetadata.Parse(metadata.AsSpan(0, length)));
        }
    }

    private async Task AcceptLoopAsync(Registration registration)
    {
        var token = registration.Cancellation.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                var client = await registration.Listener.AcceptTcpClientAsync(token);
                _ = HandleAsync(client);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                // A client that gave up before being accepted; keep listening.
            }
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using var connection = client;
        client.NoDelay = true;
        var network = client.GetStream();
        try
        {
            var requestLine = await ReadRequestLineAsync(network);
            var parts = requestLine?.Split(' ');
            if (parts is not { Length: >= 2 } || GetId(parts[1]) is not { } id || !_registrations.TryGetValue(id, out var registration))
            {
                await WriteHeadAsync(network, "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n", _shutdown.Token);
                return;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token, registration.Cancellation.Token);
            // The player closes its connection when it switches source or reconnects; that ends the relay.
            _ = CancelOnDisconnectAsync(network, cts);

            using var request = new HttpRequestMessage(HttpMethod.Get, registration.Upstream);
            request.Headers.Add("Icy-MetaData", "1");

            HttpResponseMessage response;
            try
            {
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                connectCts.CancelAfter(UpstreamConnectTimeout);
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, connectCts.Token);
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !cts.IsCancellationRequested))
            {
                await RedirectAsync(network, registration.Upstream, cts.Token);
                return;
            }

            using (response)
            {
                var contentType = response.Content.Headers.ContentType;
                if (!response.IsSuccessStatusCode || IsPlaylist(contentType?.MediaType))
                {
                    await RedirectAsync(network, registration.Upstream, cts.Token);
                    return;
                }

                var metaInterval = response.Headers.TryGetValues("icy-metaint", out var values)
                                   && int.TryParse(values.FirstOrDefault(), out var interval) && interval > 0
                    ? interval
                    : 0;

                await WriteHeadAsync(
                    network,
                    $"HTTP/1.1 200 OK\r\nContent-Type: {contentType?.ToString() ?? "application/octet-stream"}\r\n"
                    + "Cache-Control: no-cache, no-store\r\nConnection: close\r\n\r\n",
                    cts.Token);

                if (parts[0].Equals("HEAD", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                await using var body = await response.Content.ReadAsStreamAsync(cts.Token);
                await RelayAsync(body, network, metaInterval, registration.OnMetadata, cts.Token);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException
                                       or OperationCanceledException or HttpRequestException)
        {
            // The player or the station went away; the player's own reconnect logic takes it from here.
        }
    }

    private static async Task<string?> ReadRequestLineAsync(NetworkStream network)
    {
        using var timeout = new CancellationTokenSource(RequestHeadTimeout);
        var buffer = new byte[MaxRequestHeadBytes];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await network.ReadAsync(buffer.AsMemory(total), timeout.Token);
            if (read == 0)
            {
                return null;
            }

            total += read;
            if (buffer.AsSpan(0, total).IndexOf("\r\n\r\n"u8) >= 0)
            {
                var head = Encoding.ASCII.GetString(buffer, 0, total);
                return head[..head.IndexOf("\r\n", StringComparison.Ordinal)];
            }
        }

        return null;
    }

    private static async Task CancelOnDisconnectAsync(NetworkStream network, CancellationTokenSource cts)
    {
        var token = cts.Token;
        try
        {
            var buffer = new byte[256];
            while (await network.ReadAsync(buffer, token) > 0)
            {
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static Task RedirectAsync(NetworkStream network, Uri target, CancellationToken cancellationToken) =>
        WriteHeadAsync(network, $"HTTP/1.1 307 Temporary Redirect\r\nLocation: {target.AbsoluteUri}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n", cancellationToken);

    private static async Task WriteHeadAsync(NetworkStream network, string head, CancellationToken cancellationToken) =>
        await network.WriteAsync(Encoding.ASCII.GetBytes(head), cancellationToken);

    private static bool IsPlaylist(string? mediaType) =>
        mediaType is not null
        && (mediaType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("scpls", StringComparison.OrdinalIgnoreCase)
            || mediaType.Contains("ms-asf", StringComparison.OrdinalIgnoreCase));

    private static string? GetId(string path) =>
        path.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

    public void Dispose()
    {
        _shutdown.Cancel();
        foreach (var registration in _registrations.Values)
        {
            registration.Stop();
        }

        _registrations.Clear();
    }

    private sealed record Registration(Uri Upstream, Action<IcyMetadata> OnMetadata, TcpListener Listener)
    {
        public CancellationTokenSource Cancellation { get; } = new();

        public void Stop()
        {
            Cancellation.Cancel();
            Listener.Stop();
        }
    }
}
