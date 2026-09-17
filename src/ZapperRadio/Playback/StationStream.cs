using Microsoft.UI.Dispatching;
using Windows.Media.Core;
using Windows.Media.Playback;
using ZapperRadio.Core.Models;
using ZapperRadio.Core.Streaming;

namespace ZapperRadio.Playback;

public enum StreamStatus
{
    Connecting,
    Live,
    Buffering,
    Reconnecting,
    Failed,
}

/// <summary>
/// A single station stream that keeps running for as long as it exists. It starts muted;
/// listening to it is a matter of unmuting, so switching never opens a new connection
/// (which is where most stations insert their pre-roll ads).
/// Dropped connections are re-established automatically with a back-off.
/// All members must be used on the UI thread; player events are marshalled to it.
/// </summary>
public sealed class StationStream : IDisposable
{
    private static readonly TimeSpan[] RetryDelays =
        [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    /// <summary>How long a stream may be opening or buffering before it is considered stuck.</summary>
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(45);

    /// <summary>
    /// How long a song may run past its known length before an ad break is assumed. Covers titles sent a bit
    /// late, the station playing a longer version, and a short announcement after the song.
    /// </summary>
    private static readonly TimeSpan SongOverrun = TimeSpan.FromSeconds(30);

    private readonly DispatcherQueue _dispatcher;
    private readonly StreamUrlResolver _resolver;
    private readonly IcyProxy? _proxy;
    private readonly TrackDurations? _durations;
    private readonly MediaPlayer _player;
    private readonly DispatcherQueueTimer _watchdog;
    private readonly DispatcherQueueTimer _retryTimer;
    private readonly DispatcherQueueTimer _songEndTimer;
    private CancellationTokenSource? _connectCts;
    private MediaSource? _source;
    private Uri? _relayUrl;
    private int _failedAttempts;
    private DateTime _lastPlayingUtc;
    private int _songNumber;
    private bool _disposed;

    /// <param name="proxy">Relays the stream to read its song titles; null plays the station directly.</param>
    /// <param name="durations">Looks up song lengths to recognize unmarked ad breaks; null relies on ad markers only.</param>
    public StationStream(Station station, StreamUrlResolver resolver, IcyProxy? proxy, TrackDurations? durations, DispatcherQueue dispatcher, double volume)
    {
        Station = station;
        _resolver = resolver;
        _proxy = proxy;
        _durations = durations;
        _dispatcher = dispatcher;

        _player = new MediaPlayer
        {
            AudioCategory = MediaPlayerAudioCategory.Media,
            AutoPlay = true,
            IsMuted = true,
            Volume = volume,
        };
        // With several players alive, the system media overlay would only get confused.
        _player.CommandManager.IsEnabled = false;
        _player.MediaOpened += OnMediaOpened;
        _player.MediaFailed += OnMediaFailed;
        _player.MediaEnded += OnMediaEnded;
        _player.PlaybackSession.PlaybackStateChanged += OnPlaybackStateChanged;

        _watchdog = dispatcher.CreateTimer();
        _watchdog.Interval = TimeSpan.FromSeconds(5);
        _watchdog.Tick += (_, _) => CheckForStall();

        _retryTimer = dispatcher.CreateTimer();
        _retryTimer.IsRepeating = false;
        _retryTimer.Tick += (_, _) => _ = ConnectAsync();

        _songEndTimer = dispatcher.CreateTimer();
        _songEndTimer.IsRepeating = false;
        _songEndTimer.Tick += (_, _) => MarkSongOverdue();
    }

    public Station Station { get; }

    public StreamStatus Status { get; private set; } = StreamStatus.Connecting;

    public string? LastError { get; private set; }

    public event EventHandler? StatusChanged;

    /// <summary>The latest song title (or ad marker) the station sent, or null when there is none.</summary>
    public IcyMetadata? Metadata { get; private set; }

    /// <summary>
    /// True when the current song should have ended a while ago and no new title came, which usually
    /// means the station is playing ads without marking them.
    /// </summary>
    public bool IsSongOverdue { get; private set; }

    /// <summary>True during an ad break the station marks, or one assumed from an overdue song.</summary>
    public bool IsInAdBreak => Metadata?.IsAd == true || IsSongOverdue;

    /// <summary>Raised when <see cref="Metadata"/> or <see cref="IsSongOverdue"/> changes.</summary>
    public event EventHandler? MetadataChanged;

    public bool IsMuted
    {
        get => _player.IsMuted;
        set => _player.IsMuted = value;
    }

    public double Volume
    {
        get => _player.Volume;
        set => _player.Volume = value;
    }

    public void Start() => _ = ConnectAsync();

    private async Task ConnectAsync()
    {
        if (_disposed)
        {
            return;
        }

        _retryTimer.Stop();
        _connectCts?.Cancel();
        var cts = _connectCts = new CancellationTokenSource();
        SetStatus(_failedAttempts == 0 ? StreamStatus.Connecting : StreamStatus.Reconnecting);

        try
        {
            var uri = await _resolver.ResolveAsync(new Uri(Station.Url), cts.Token);
            if (cts.IsCancellationRequested || _disposed)
            {
                return;
            }

            ReleaseSource();
            _source = MediaSource.CreateFromUri(RelayForTitles(uri));
            _player.Source = _source;
            _lastPlayingUtc = DateTime.UtcNow;
            _watchdog.Start();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ScheduleReconnect(ex.Message);
        }
    }

    private Uri RelayForTitles(Uri uri)
    {
        // HLS playlists reference their segments relatively, so they cannot go through the relay.
        if (_proxy is null || StreamUrlResolver.GetPlaylistKind(uri) != PlaylistKind.None
                           || uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
        {
            return uri;
        }

        Uri? relayUrl = null;
        relayUrl = _relayUrl = _proxy.Register(uri, metadata => OnUiThread(() =>
        {
            // Ignore a relay that is being replaced by a new connection.
            if (_relayUrl == relayUrl)
            {
                SetMetadata(metadata.Title is null && !metadata.IsAd ? null : metadata);
            }
        }));
        return relayUrl;
    }

    private void SetMetadata(IcyMetadata? metadata)
    {
        if (Metadata == metadata)
        {
            return;
        }

        Metadata = metadata;
        _songNumber++;
        _songEndTimer.Stop();
        IsSongOverdue = false;
        MetadataChanged?.Invoke(this, EventArgs.Empty);

        if (_durations is not null && metadata is { IsAd: false, Title: { } title })
        {
            _ = WatchSongEndAsync(title);
        }
    }

    /// <summary>
    /// Times the song from the moment its title arrived. After tuning in halfway through a song it has
    /// really been playing longer, so the ad break is then noticed a bit late rather than too early.
    /// </summary>
    private async Task WatchSongEndAsync(string title)
    {
        var songNumber = _songNumber;
        var startedUtc = DateTime.UtcNow;
        var duration = await _durations!.GetAsync(title);
        OnUiThread(() =>
        {
            if (duration is null || songNumber != _songNumber)
            {
                return;
            }

            var remaining = startedUtc + duration.Value + SongOverrun - DateTime.UtcNow;
            _songEndTimer.Interval = remaining > TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1);
            _songEndTimer.Start();
        });
    }

    private void MarkSongOverdue()
    {
        if (!IsSongOverdue)
        {
            IsSongOverdue = true;
            MetadataChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ScheduleReconnect(string reason)
    {
        if (_disposed)
        {
            return;
        }

        _watchdog.Stop();
        ReleaseSource();
        LastError = reason;

        var delay = RetryDelays[Math.Min(_failedAttempts, RetryDelays.Length - 1)];
        _failedAttempts++;
        SetStatus(_failedAttempts > RetryDelays.Length ? StreamStatus.Failed : StreamStatus.Reconnecting);

        _retryTimer.Interval = delay;
        _retryTimer.Start();
    }

    private void CheckForStall()
    {
        if (_player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
        {
            _lastPlayingUtc = DateTime.UtcNow;
        }
        else if (DateTime.UtcNow - _lastPlayingUtc > StallTimeout)
        {
            ScheduleReconnect("The stream stopped responding.");
        }
    }

    private void OnMediaOpened(MediaPlayer sender, object args) => OnUiThread(() =>
    {
        _failedAttempts = 0;
        LastError = null;
        SetStatus(StreamStatus.Live);
    });

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        var message = string.IsNullOrWhiteSpace(args.ErrorMessage) ? args.Error.ToString() : args.ErrorMessage;
        OnUiThread(() => ScheduleReconnect(message));
    }

    private void OnMediaEnded(MediaPlayer sender, object args) =>
        OnUiThread(() => ScheduleReconnect("The stream ended."));

    private void OnPlaybackStateChanged(MediaPlaybackSession sender, object args)
    {
        var state = sender.PlaybackState;
        OnUiThread(() =>
        {
            switch (state)
            {
                case MediaPlaybackState.Playing:
                    _lastPlayingUtc = DateTime.UtcNow;
                    SetStatus(StreamStatus.Live);
                    break;
                case MediaPlaybackState.Buffering when Status == StreamStatus.Live:
                    SetStatus(StreamStatus.Buffering);
                    break;
            }
        });
    }

    private void OnUiThread(Action action) => _dispatcher.TryEnqueue(() =>
    {
        if (!_disposed)
        {
            action();
        }
    });

    private void SetStatus(StreamStatus status)
    {
        if (Status != status)
        {
            Status = status;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ReleaseSource()
    {
        if (_source is null)
        {
            return;
        }

        _player.Source = null;
        _source.Dispose();
        _source = null;

        if (_relayUrl is not null)
        {
            _proxy?.Unregister(_relayUrl);
            _relayUrl = null;
        }

        SetMetadata(null);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connectCts?.Cancel();
        _watchdog.Stop();
        _retryTimer.Stop();
        _songEndTimer.Stop();

        _player.MediaOpened -= OnMediaOpened;
        _player.MediaFailed -= OnMediaFailed;
        _player.MediaEnded -= OnMediaEnded;
        _player.PlaybackSession.PlaybackStateChanged -= OnPlaybackStateChanged;

        ReleaseSource();
        _player.Dispose();
    }
}
