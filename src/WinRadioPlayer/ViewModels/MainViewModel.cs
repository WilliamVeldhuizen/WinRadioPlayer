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
    public const string AllCountries = "Alle landen";

    private static readonly System.Globalization.CultureInfo DutchCulture = new("nl-NL");

    private readonly DispatcherQueue _dispatcher;
    private readonly HttpClient _http;
    private readonly SettingsStore _settingsStore;
    private readonly AppSettings _settings;
    private readonly StationDirectory _directory;
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

        _engine = new RadioEngine(new StreamUrlResolver(_http), dispatcher) { Volume = Math.Clamp(_settings.Volume, 0, 1) };
        _engine.ActiveChanged += (_, _) => UpdateNowPlaying();
        _engine.StreamStatusChanged += (_, stream) => OnStreamStatusChanged(stream);

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

    public string FavoritesHeader => $"Favorieten ({Favorites.Count}/{AppSettings.MaxFavorites})";

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
    public partial string NowPlayingName { get; set; } = "Kies een zender";

    [ObservableProperty]
    public partial string NowPlayingStatus { get; set; } = "Klik op een favoriet om direct live te luisteren";

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    [ObservableProperty]
    public partial double Volume { get; set; }

    public async Task LoadCatalogAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        CatalogStatus = "Zenderlijst ophalen…";
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
            // Replacing the ComboBox items clears its selection, even if the value itself did not change.
            OnPropertyChanged(nameof(SelectedCountry));

            var date = catalog.GeneratedAt?.ToString("d MMMM yyyy", DutchCulture) ?? catalog.FileName;
            CatalogStatus = $"{_allStations.Count:N0} zenders · lijst van {date}" + (catalog.FromCache ? " (offline kopie)" : "");
            await ApplySearchAsync();
        }
        catch (Exception ex)
        {
            CatalogStatus = "Zenderlijst niet beschikbaar";
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
            ShowError($"Je kunt maximaal {AppSettings.MaxFavorites} favorieten hebben, omdat ze allemaal op de achtergrond doorspelen. Verwijder er eerst een.");
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

    partial void OnSelectedCountryChanged(string value)
    {
        if (value is null)
        {
            return;
        }

        _settings.Country = value == AllCountries ? null : value;
        _ = ApplySearchAsync();
    }

    partial void OnVolumeChanged(double value) => _engine.Volume = Math.Clamp(value / 100, 0, 1);

    private async Task ApplySearchAsync()
    {
        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        var filter = new StationFilter(SearchText, SelectedCountry == AllCountries ? null : SelectedCountry);
        var source = _allStations;

        try
        {
            var results = filter.IsEmpty
                ? source
                : await Task.Run(() => source.Where(s => filter.Matches(s.Station)).ToList(), cts.Token);

            if (!cts.IsCancellationRequested)
            {
                Results = results;
            }
        }
        catch (OperationCanceledException)
        {
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
            NowPlayingName = _lastPlayed?.Name ?? "Kies een zender";
            NowPlayingStatus = _lastPlayed is null ? "Klik op een favoriet om direct live te luisteren" : "Gestopt";
            return;
        }

        NowPlayingName = active.Station.Name;
        var isFavorite = Favorites.Any(f => f.Station.Url == active.Station.Url);
        NowPlayingStatus = StatusTexts.For(active.Status, isActive: true)
                           + (isFavorite ? "" : " · geen favoriet, stream stopt bij wisselen")
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
            ShowError($"Instellingen opslaan mislukt: {ex.Message}");
        }
    }

    public void Dispose()
    {
        SaveSettings();
        _searchCts?.Cancel();
        _engine.Dispose();
        _http.Dispose();
    }
}
