using Microsoft.UI.Dispatching;
using WinRadioPlayer.Core.Models;
using WinRadioPlayer.Core.Streaming;

namespace WinRadioPlayer.Playback;

/// <summary>
/// Keeps every favorite streaming (muted) in the background and unmutes the one being listened to.
/// A station that is not a favorite gets a temporary stream that is closed when you switch away,
/// unless it is added to the favorites while playing, in which case its stream is kept.
/// </summary>
public sealed class RadioEngine(StreamUrlResolver resolver, IcyProxy? proxy, DispatcherQueue dispatcher) : IDisposable
{
    private readonly Dictionary<string, StationStream> _favorites = new(StringComparer.Ordinal);
    private StationStream? _transient;
    private double _volume = 0.8;
    private bool _isMuted;

    public StationStream? Active { get; private set; }

    public event EventHandler? ActiveChanged;

    public event EventHandler<StationStream>? StreamStatusChanged;

    public event EventHandler<StationStream>? StreamMetadataChanged;

    public double Volume
    {
        get => _volume;
        set
        {
            _volume = value;
            foreach (var stream in AllStreams())
            {
                stream.Volume = value;
            }
        }
    }

    /// <summary>Silences the station being listened to, also after switching, without stopping it.</summary>
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            _isMuted = value;
            if (Active is not null)
            {
                Active.IsMuted = value;
            }
        }
    }

    public StationStream? Find(string url) =>
        _favorites.GetValueOrDefault(url) ?? (_transient?.Station.Url == url ? _transient : null);

    /// <summary>Starts streams for new favorites and stops streams for removed ones.</summary>
    public void SetFavorites(IEnumerable<Station> favorites)
    {
        var wanted = favorites.DistinctBy(s => s.Url).ToDictionary(s => s.Url, StringComparer.Ordinal);

        foreach (var (url, stream) in _favorites.Where(f => !wanted.ContainsKey(f.Key)).ToList())
        {
            _favorites.Remove(url);
            if (stream == Active)
            {
                // Keep listening; it just won't stay warm after switching away.
                if (_transient is not null)
                {
                    Release(_transient);
                }

                _transient = stream;
            }
            else
            {
                Release(stream);
            }
        }

        foreach (var (url, station) in wanted.Where(w => !_favorites.ContainsKey(w.Key)))
        {
            if (_transient?.Station.Url == url)
            {
                _favorites[url] = _transient;
                _transient = null;
            }
            else
            {
                _favorites[url] = CreateAndStart(station);
            }
        }
    }

    public void Play(Station station)
    {
        var stream = Find(station.Url) ?? CreateAndStart(station);
        var previous = Active;
        if (previous == stream)
        {
            return;
        }

        if (previous is not null)
        {
            previous.IsMuted = true;
        }

        if (_transient is not null && _transient != stream)
        {
            Release(_transient);
            _transient = null;
        }

        if (!_favorites.ContainsKey(stream.Station.Url))
        {
            _transient = stream;
        }

        stream.IsMuted = _isMuted;
        Active = stream;
        ActiveChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        if (Active is null)
        {
            return;
        }

        Active.IsMuted = true;
        if (Active == _transient)
        {
            Release(_transient);
            _transient = null;
        }

        Active = null;
        ActiveChanged?.Invoke(this, EventArgs.Empty);
    }

    private StationStream CreateAndStart(Station station)
    {
        var stream = new StationStream(station, resolver, proxy, dispatcher, _volume);
        stream.StatusChanged += OnStreamStatusChanged;
        stream.MetadataChanged += OnStreamMetadataChanged;
        stream.Start();
        return stream;
    }

    private void Release(StationStream stream)
    {
        stream.StatusChanged -= OnStreamStatusChanged;
        stream.MetadataChanged -= OnStreamMetadataChanged;
        stream.Dispose();
    }

    private void OnStreamStatusChanged(object? sender, EventArgs e) =>
        StreamStatusChanged?.Invoke(this, (StationStream)sender!);

    private void OnStreamMetadataChanged(object? sender, EventArgs e) =>
        StreamMetadataChanged?.Invoke(this, (StationStream)sender!);

    private IEnumerable<StationStream> AllStreams() =>
        _transient is null ? _favorites.Values : _favorites.Values.Append(_transient);

    public void Dispose()
    {
        foreach (var stream in AllStreams().ToList())
        {
            Release(stream);
        }

        _favorites.Clear();
        _transient = null;
        Active = null;
    }
}
