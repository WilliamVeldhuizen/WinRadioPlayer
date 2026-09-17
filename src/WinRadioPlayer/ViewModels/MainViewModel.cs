using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using WinRadioPlayer.Core.Catalog;
using WinRadioPlayer.Core.Models;
using WinRadioPlayer.Core.Settings;
using WinRadioPlayer.Core.Streaming;
using WinRadioPlayer.Playback;

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

    private List<StationResultViewModel> _allStations = [];
    private CancellationTokenSource? _searchCts;
    private bool _favoritesSyncPending;
    private Station? _lastPlayed;

    public MainViewModel(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;

        var dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinRadioPlayer");
        _http = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromMinutes(2),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("WinRadioPlayer/1.0");

        _settingsStore = new SettingsStore(Path.Combine(dataFolder, "settings.json"));
        _settings = _settingsStore.Load();
        _directory = new StationDirectory(_http, Path.Combine(dataFolder, "cache"));
        _popularity = new StationPopularity(_http, Path.Combine(dataFolder, "cache"));

        // Streams run for hours, so the relay gets a client without the overall request timeout.
        _streamHttp = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _streamHttp.DefaultRequestHeaders.UserAgent.ParseAdd("WinRadioPlayer/1.0");
        _proxy = new IcyProxy(_streamHttp);

        _engine = new RadioEngine(new StreamUrlResolver(_http), _proxy, dispatcher) { Volume = Math.Clamp(_settings.Volume, 0, 1) };
        _engine.ActiveChanged += (_, _) => UpdateNowPlaying();
        _engine.StreamStatusChanged += (_, stream) => OnStreamStatusChanged(stream);
        _engine.StreamMetadataChanged += (_, stream) => OnStreamMetadataChanged(stream);

        _searchDebounce = dispatcher.CreateTimer();
        _searchDebounce.Interval = TimeSpan.FromMilliseconds(250);
        _searchDebounce.IsRepeating = false;
        _searchDebounce.Tick += (_, _) => _ = ApplySearchAsync();

        Volume = _engine.Volume * 100;
        SelectedCountry = _settings.Country ?? AllCountries;

        foreach (var station in _settings.Favorites.DistinctBy(s => s.Url).Take(AppSettings.MaxFavorites))
        {
            Favorites.Add(new FavoriteViewModel(station));
        }

        Favorites.CollectionChanged += OnFavoritesChanged;
        SyncFavorites();
    }

    public ObservableCollection<FavoriteViewModel> Favorites { get; } = [];

    public string FavoritesHeader => $"Favorites ({Favorites.Count}/{AppSettings.MaxFavorites})";

    public bool HasNoFavorites => Favorites.Count == 0;

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

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    [ObservableProperty]
    public partial double Volume { get; set; }

    [ObservableProperty]
    public partial bool IsMuted { get; set; }

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
    }

    public void PlayFavoriteAt(int index)
    {
        if (index >= 0 && index < Favorites.Count)
        {
            Play(Favorites[index].Station);
        }
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
    }

    [RelayCommand]
    private void ToggleMute() => IsMuted = !IsMuted;

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

    private void SyncFavorites()
    {
        _favoritesSyncPending = false;
        _engine.SetFavorites(Favorites.Select(f => f.Station));

        for (var i = 0; i < Favorites.Count; i++)
        {
            var favorite = Favorites[i];
            favorite.ShortcutText = i < 10 ? $"Ctrl+{(i + 1) % 10}" : "";
            if (_engine.Find(favorite.Station.Url) is { } stream)
            {
                favorite.Status = stream.Status;
                favorite.Song = SongTexts.For(stream.Metadata);
            }
        }

        RefreshFavoriteMarks();
        UpdateNowPlaying();
        SaveSettings();
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
            favorite.Song = SongTexts.For(stream.Metadata);
        }

        if (stream == _engine.Active)
        {
            UpdateNowPlaying();
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
            NowPlayingStatus = _lastPlayed is null ? "Click a favorite to listen live instantly" : "Stopped";
            return;
        }

        NowPlayingName = active.Station.Name;
        var isFavorite = Favorites.Any(f => f.Station.Url == active.Station.Url);
        NowPlayingSong = SongTexts.For(active.Metadata);
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
        _searchCts?.Cancel();
        _engine.Dispose();
        _proxy.Dispose();
        _streamHttp.Dispose();
        _http.Dispose();
    }
}
