using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using WinRadioPlayer.Core.Catalog;
using WinRadioPlayer.Core.Models;
using WinRadioPlayer.Core.Settings;
using WinRadioPlayer.Core.Shell;
using WinRadioPlayer.Core.Streaming;
using WinRadioPlayer.Playback;
using WinRadioPlayer.Shell;

namespace WinRadioPlayer.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    public const string AllCountries = "All countries";

    private static readonly System.Globalization.CultureInfo EnglishCulture = new("en-US");

    private readonly DispatcherQueue _dispatcher;
    private readonly HttpClient _http;
    private readonly SettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly StationDirectory _directory;
    private readonly StationPopularity _popularity;
    private readonly Dictionary<string, IReadOnlyDictionary<string, int>> _popularityByCountry = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _streamHttp;
    private readonly IcyProxy _proxy;
    private readonly RadioEngine _engine;
    private readonly DispatcherQueueTimer _searchDebounce;
    private readonly DispatcherQueueTimer _jumpListTimer;

    private List<StationResultViewModel> _allStations = [];
    private CancellationTokenSource? _searchCts;
    private bool _favoritesSyncPending;
    private Station? _lastPlayed;
    private StationStream? _chosenDuringAd;
    private bool _isFirstRun;
    private string? _jumpListShown;
    private Task _jumpListUpdates = Task.CompletedTask;

    public MainViewModel(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;

        var dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinRadioPlayer");
        _http = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromMinutes(2),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("WinRadioPlayer/1.0");

        var settingsPath = Path.Combine(dataFolder, "settings.json");
        _isFirstRun = !File.Exists(settingsPath);
        _settingsStore = new SettingsStore(settingsPath);
        _settings = _settingsStore.Load();
        _directory = new StationDirectory(_http, Path.Combine(dataFolder, "cache"));
        _popularity = new StationPopularity(_http, Path.Combine(dataFolder, "cache"));

        // Streams run for hours, so the relay gets a client without the overall request timeout.
        _streamHttp = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _streamHttp.DefaultRequestHeaders.UserAgent.ParseAdd("WinRadioPlayer/1.0");
        _proxy = new IcyProxy(_streamHttp);

        _engine = new RadioEngine(new StreamUrlResolver(_http), _proxy, new TrackDurations(_http), dispatcher) { Volume = Math.Clamp(_settings.Volume, 0, 1) };
        _engine.ActiveChanged += (_, _) => UpdateNowPlaying();
        _engine.StreamStatusChanged += (_, stream) => OnStreamStatusChanged(stream);
        _engine.StreamMetadataChanged += (_, stream) => OnStreamMetadataChanged(stream);

        _searchDebounce = dispatcher.CreateTimer();
        _searchDebounce.Interval = TimeSpan.FromMilliseconds(250);
        _searchDebounce.IsRepeating = false;
        _searchDebounce.Tick += (_, _) => _ = ApplySearchAsync();

        _jumpListTimer = dispatcher.CreateTimer();
        _jumpListTimer.Interval = TimeSpan.FromSeconds(1);
        _jumpListTimer.IsRepeating = false;
        _jumpListTimer.Tick += (_, _) => UpdateJumpList(withSongs: true);

        Volume = _engine.Volume * 100;
        SelectedCountry = _settings.Country ?? AllCountries;
        SkipAdBreaks = _settings.SkipAdBreaks;

        foreach (var station in _settings.Favorites.DistinctBy(s => s.Url).Take(AppSettings.MaxFavorites))
        {
            Favorites.Add(new FavoriteViewModel(station));
        }

        foreach (var track in _settings.FavoriteTracks)
        {
            FavoriteTracks.Add(track);
        }

        FavoriteTracks.CollectionChanged += OnFavoriteTracksChanged;
        Favorites.CollectionChanged += OnFavoritesChanged;
        SyncFavorites();
    }

    public ObservableCollection<FavoriteViewModel> Favorites { get; } = [];

    public string FavoritesHeader => $"Favorites ({Favorites.Count}/{AppSettings.MaxFavorites})";

    public bool HasNoFavorites => Favorites.Count == 0;

    /// <summary>Songs saved with the heart while listening, newest first.</summary>
    public ObservableCollection<FavoriteTrack> FavoriteTracks { get; } = [];

    public string FavoriteTracksHeader => $"Favorite tracks ({FavoriteTracks.Count})";

    public bool HasNoFavoriteTracks => FavoriteTracks.Count == 0;

    /// <summary>Whether the favorite tracks are shown instead of the station search.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingStations))]
    public partial bool IsShowingFavoriteTracks { get; set; }

    public bool IsShowingStations => !IsShowingFavoriteTracks;

    [ObservableProperty]
    public partial IReadOnlyList<StationResultViewModel> Results { get; set; } = [];

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial IReadOnlyList<string> Countries { get; set; } = [AllCountries];

    [ObservableProperty]
    public partial string SelectedCountry { get; set; } = AllCountries;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string CatalogStatus { get; set; } = "";

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = "";

    [ObservableProperty]
    public partial bool IsErrorOpen { get; set; }

    [ObservableProperty]
    public partial string NowPlayingName { get; set; } = "Choose a station";

    [ObservableProperty]
    public partial string NowPlayingStatus { get; set; } = "Click a favorite to listen live instantly";

    /// <summary>The current song of the station being listened to, or empty when unknown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNowPlayingSong))]
    public partial string NowPlayingSong { get; set; } = "";

    public bool HasNowPlayingSong => NowPlayingSong.Length > 0;

    /// <summary>The song the heart saves: the one shown as playing, or null during an ad break.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveNowPlayingTrack))]
    public partial string? NowPlayingTrack { get; set; }

    public bool CanSaveNowPlayingTrack => NowPlayingTrack is not null;

    [ObservableProperty]
    public partial bool IsNowPlayingTrackSaved { get; set; }

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    [ObservableProperty]
    public partial double Volume { get; set; }

    [ObservableProperty]
    public partial bool IsMuted { get; set; }

    /// <summary>When the station being listened to starts an ad break, switch to the highest favorite without one.</summary>
    [ObservableProperty]
    public partial bool SkipAdBreaks { get; set; }

    public async Task LoadCatalogAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        _popularityByCountry.Clear();
        CatalogStatus = "Loading station list…";
        try
        {
            var catalog = await _directory.LoadAsync();

            var stations = await Task.Run(() =>
            {
                var all = catalog.Stations
                    .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(s => new StationResultViewModel(s))
                    .ToList();
                var countries = catalog.Stations
                    .Select(s => s.Country)
                    .Where(c => c.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.CurrentCultureIgnoreCase)
                    .Prepend(AllCountries)
                    .ToList();
                return (all, countries);
            });

            _allStations = stations.all;
            RefreshFavoriteMarks();

            var country = SelectedCountry;
            if (_isFirstRun && country == AllCountries && WindowsRegion.GetIsoCode() is { } region)
            {
                // Without saved settings, start with the country set in Windows.
                country = StationPopularity.FindCountry(stations.countries, region) ?? country;
            }

            _isFirstRun = false;
            Countries = stations.countries;
            SelectedCountry = stations.countries.Contains(country) ? country : AllCountries;
            // Refresh the country box, which may still show text typed while the list was loading.
            OnPropertyChanged(nameof(SelectedCountry));

            var date = catalog.GeneratedAt?.ToString("MMMM d, yyyy", EnglishCulture) ?? catalog.FileName;
            CatalogStatus = $"{_allStations.Count:N0} stations · list from {date}" + (catalog.FromCache ? " (offline copy)" : "");
            await ApplySearchAsync();
        }
        catch (Exception ex)
        {
            CatalogStatus = "Station list unavailable";
            ShowError(ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private Task ReloadCatalog() => LoadCatalogAsync();

    public void Play(Station station)
    {
        _lastPlayed = station;
        _engine.Play(station);
        // Picking a station during its ad break means you want to hear it anyway, so that break is not skipped.
        _chosenDuringAd = _engine.Active is { IsInAdBreak: true } active ? active : null;
    }

    [RelayCommand]
    private void TogglePlayback()
    {
        if (_engine.Active is not null)
        {
            _engine.Stop();
        }
        else if ((_lastPlayed ?? Favorites.FirstOrDefault()?.Station) is { } station)
        {
            Play(station);
        }
    }

    public void ToggleFavorite(Station station)
    {
        var existing = Favorites.FirstOrDefault(f => f.Station.Url == station.Url);
        if (existing is not null)
        {
            Favorites.Remove(existing);
            return;
        }

        if (Favorites.Count >= AppSettings.MaxFavorites)
        {
            ShowError($"You can have at most {AppSettings.MaxFavorites} favorites, because they all keep streaming in the background. Remove one first.");
            return;
        }

        Favorites.Add(new FavoriteViewModel(station));
    }

    public void RemoveFavorite(FavoriteViewModel favorite) => Favorites.Remove(favorite);

    /// <summary>Saves the song playing right now to the favorite tracks, or removes it when it is already there.</summary>
    [RelayCommand]
    private void ToggleNowPlayingTrackSaved()
    {
        if (NowPlayingTrack is not { } title || _engine.Active is not { } active)
        {
            return;
        }

        if (FavoriteTracks.FirstOrDefault(t => t.IsSameSong(title)) is { } saved)
        {
            FavoriteTracks.Remove(saved);
        }
        else
        {
            FavoriteTracks.Insert(0, new FavoriteTrack(title, active.Station.Name, DateTimeOffset.Now));
        }
    }

    public void RemoveFavoriteTrack(FavoriteTrack track) => FavoriteTracks.Remove(track);

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    /// <summary>
    /// The countries containing <paramref name="text"/>, those starting with it first.
    /// Empty text matches every country, including <see cref="AllCountries"/>.
    /// </summary>
    public IReadOnlyList<string> MatchCountries(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return Countries;
        }

        return Countries
            .Where(c => c.Contains(text, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(c => !c.StartsWith(text, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
    }

    partial void OnSelectedCountryChanged(string value)
    {
        if (value is null)
        {
            return;
        }

        _settings.Country = value == AllCountries ? null : value;
        _ = ApplySearchAsync();
    }

    partial void OnVolumeChanged(double value)
    {
        _engine.Volume = Math.Clamp(value / 100, 0, 1);
        // Turning the volume up is a clear sign you want to hear something.
        if (value > 0)
        {
            IsMuted = false;
        }
    }

    partial void OnIsMutedChanged(bool value)
    {
        _engine.IsMuted = value;
        UpdateNowPlaying();
        ScheduleJumpListUpdate();
    }

    [RelayCommand]
    private void ToggleMute() => IsMuted = !IsMuted;

    partial void OnSkipAdBreaksChanged(bool value)
    {
        _settings.SkipAdBreaks = value;
        if (value && _engine.Active is { IsInAdBreak: true } active)
        {
            SkipAdBreak(active);
        }
    }

    /// <summary>Switches to the highest favorite that is live and not in an ad break, if there is one.</summary>
    private void SkipAdBreak(StationStream active)
    {
        var next = Favorites
            .Select(f => _engine.Find(f.Station.Url))
            .FirstOrDefault(s => s is not null && s != active && s.Status == StreamStatus.Live && !s.IsInAdBreak);
        if (next is not null)
        {
            Play(next.Station);
        }
    }

    private async Task ApplySearchAsync()
    {
        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        var country = SelectedCountry == AllCountries ? null : SelectedCountry;
        var filter = new StationFilter(SearchText, country);
        var source = _allStations;

        IReadOnlyDictionary<string, int>? ranks = null;
        if (country is not null && !_popularityByCountry.TryGetValue(country, out ranks))
        {
            _ = LoadPopularityAsync(country);
        }

        try
        {
            var results = filter.IsEmpty
                ? source
                : await Task.Run(() =>
                {
                    var matches = source.Where(s => filter.Matches(s.Station));
                    // Within a country, the most popular stations come first; unranked ones keep name order.
                    if (ranks is { Count: > 0 })
                    {
                        matches = matches.OrderBy(s => ranks.TryGetValue(s.Station.Url, out var rank) ? rank : int.MaxValue);
                    }

                    return matches.ToList();
                }, cts.Token);

            if (!cts.IsCancellationRequested)
            {
                Results = results;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task LoadPopularityAsync(string country)
    {
        // Mark as loading so repeated searches don't start the same request again.
        _popularityByCountry[country] = new Dictionary<string, int>();
        try
        {
            _popularityByCountry[country] = await _popularity.GetRanksAsync(country);
        }
        catch (Exception)
        {
            // Popularity is a nice-to-have; without it the list stays sorted by name.
            _popularityByCountry.Remove(country);
            return;
        }

        if (string.Equals(SelectedCountry, country, StringComparison.OrdinalIgnoreCase))
        {
            await ApplySearchAsync();
        }
    }

    private void OnFavoritesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(FavoritesHeader));
        OnPropertyChanged(nameof(HasNoFavorites));

        // Drag-reordering in the list is a Remove followed by an Add. Syncing after the current
        // dispatcher turn avoids closing and reopening that station's stream (and hearing an ad).
        if (!_favoritesSyncPending)
        {
            _favoritesSyncPending = true;
            _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, SyncFavorites);
        }
    }

    private void OnFavoriteTracksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(FavoriteTracksHeader));
        OnPropertyChanged(nameof(HasNoFavoriteTracks));
        UpdateNowPlaying();
        SaveSettings();
    }

    private void SyncFavorites()
    {
        _favoritesSyncPending = false;
        _engine.SetFavorites(Favorites.Select(f => f.Station));

        foreach (var favorite in Favorites)
        {
            if (_engine.Find(favorite.Station.Url) is { } stream)
            {
                favorite.Status = stream.Status;
                favorite.Song = SongTexts.For(stream);
            }
        }

        RefreshFavoriteMarks();
        UpdateNowPlaying();
        ScheduleJumpListUpdate();
        SaveSettings();
    }

    /// <summary>Carries out a jump list item clicked while the app runs, or the one it was started with.</summary>
    public void Execute(JumpListCommand command)
    {
        switch (command.Action)
        {
            case JumpListAction.Play when Favorites.FirstOrDefault(f => f.Station.Url == command.Url) is { } favorite:
                Play(favorite.Station);
                break;
            case JumpListAction.Mute:
                IsMuted = true;
                break;
            case JumpListAction.Unmute:
                IsMuted = false;
                break;
        }
    }

    private void ScheduleJumpListUpdate()
    {
        // At most one update per interval, because songs change often across many favorites.
        if (!_jumpListTimer.IsRunning)
        {
            _jumpListTimer.Start();
        }
    }

    private void UpdateJumpList(bool withSongs)
    {
        var favorites = Favorites
            .Select(f => new JumpListItem(
                JumpListCommand.Title(f.Name, withSongs ? f.Song : ""),
                withSongs && f.HasSong ? $"Listen to {f.Name}\n{f.Song}" : $"Listen to {f.Name}",
                JumpListCommand.Play(f.Station.Url)))
            .ToList();
        var muted = withSongs && IsMuted;
        var muteTask = new JumpListItem(
            muted ? "Unmute" : "Mute",
            muted ? "Hear the station again" : "Silence the station without stopping it",
            new JumpListCommand(muted ? JumpListAction.Unmute : JumpListAction.Mute),
            // The speaker icons of the Windows volume mixer: 1 is a speaker, 2 a muted speaker.
            Path.Combine(Environment.SystemDirectory, "SndVol.exe"),
            muted ? 1 : 2);

        var key = string.Join("\n", favorites.Append(muteTask));
        if (key == _jumpListShown)
        {
            return;
        }

        _jumpListShown = key;
        // In the background and in order, so a slow update never blocks the window or overwrites a newer one.
        _jumpListUpdates = _jumpListUpdates.ContinueWith(_ => TaskbarJumpList.Update(favorites, muteTask), TaskScheduler.Default);
    }

    private void RefreshFavoriteMarks()
    {
        var favoriteUrls = Favorites.Select(f => f.Station.Url).ToHashSet(StringComparer.Ordinal);
        foreach (var result in _allStations)
        {
            result.IsFavorite = favoriteUrls.Contains(result.Station.Url);
        }
    }

    private void OnStreamStatusChanged(StationStream stream)
    {
        foreach (var favorite in Favorites.Where(f => f.Station.Url == stream.Station.Url))
        {
            favorite.Status = stream.Status;
        }

        if (stream == _engine.Active)
        {
            UpdateNowPlaying();
        }
    }

    private void OnStreamMetadataChanged(StationStream stream)
    {
        foreach (var favorite in Favorites.Where(f => f.Station.Url == stream.Station.Url))
        {
            favorite.Song = SongTexts.For(stream);
            ScheduleJumpListUpdate();
        }

        var isAd = stream.IsInAdBreak;
        if (stream == _chosenDuringAd && !isAd)
        {
            _chosenDuringAd = null;
        }

        if (stream == _engine.Active)
        {
            UpdateNowPlaying();
            if (isAd && SkipAdBreaks && stream != _chosenDuringAd)
            {
                SkipAdBreak(stream);
            }
        }
    }

    private void UpdateNowPlaying()
    {
        var active = _engine.Active;
        foreach (var favorite in Favorites)
        {
            favorite.IsActive = active is not null && favorite.Station.Url == active.Station.Url;
        }

        IsPlaying = active is not null;
        if (active is null)
        {
            NowPlayingName = _lastPlayed?.Name ?? "Choose a station";
            NowPlayingSong = "";
            NowPlayingTrack = null;
            IsNowPlayingTrackSaved = false;
            NowPlayingStatus = _lastPlayed is null ? "Click a favorite to listen live instantly" : "Stopped";
            return;
        }

        NowPlayingName = active.Station.Name;
        var isFavorite = Favorites.Any(f => f.Station.Url == active.Station.Url);
        NowPlayingSong = SongTexts.For(active);
        NowPlayingTrack = active is { IsInAdBreak: false, Metadata.Title: { } title } ? title : null;
        IsNowPlayingTrackSaved = NowPlayingTrack is { } track && FavoriteTracks.Any(t => t.IsSameSong(track));
        NowPlayingStatus = StatusTexts.For(active.Status, isActive: true)
                           + (IsMuted ? " · muted" : "")
                           + (isFavorite ? "" : " · not a favorite, stream stops when switching")
                           + (active.Status is StreamStatus.Reconnecting or StreamStatus.Failed && active.LastError is { } error ? $" ({error})" : "");
    }

    private void ShowError(string message)
    {
        ErrorMessage = message;
        IsErrorOpen = true;
    }

    private void SaveSettings()
    {
        _settings.Favorites = Favorites.Select(f => f.Station).ToList();
        _settings.FavoriteTracks = FavoriteTracks.ToList();
        _settings.Volume = _engine.Volume;
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError($"Could not save settings: {ex.Message}");
        }
    }

    public void Dispose()
    {
        SaveSettings();
        // Songs and the mute state are outdated once the app is closed.
        _jumpListTimer.Stop();
        UpdateJumpList(withSongs: false);
        _jumpListUpdates.Wait(TimeSpan.FromSeconds(5));
        _searchCts?.Cancel();
        _engine.Dispose();
        _proxy.Dispose();
        _streamHttp.Dispose();
        _http.Dispose();
    }
}
