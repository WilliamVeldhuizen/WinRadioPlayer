using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.System;
using ZapperRadio.Core.Models;
using ZapperRadio.Core.Settings;
using ZapperRadio.Shell;
using ZapperRadio.ViewModels;

namespace ZapperRadio;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        ViewModel = new MainViewModel(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragArea);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "ZapperRadio.ico"));

        AppTitleBar.SizeChanged += (_, _) => KeepViewButtonClearOfCaptionButtons();
        KeepViewButtonClearOfCaptionButtons();

        PlaceWindow(ViewModel.IsCompact);
        AppWindow.Changed += (_, args) =>
        {
            if (args.DidPositionChange || args.DidSizeChange)
            {
                RememberWindow(ViewModel.IsCompact);
            }
        };

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _hotkeys = new GlobalHotkeys(hwnd, DispatcherQueue);
        ApplyGlobalHotkeys();
        _systemMediaControls = SystemMediaControls.TryCreate(hwnd, DispatcherQueue, ViewModel);

        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsCompact))
            {
                // IsCompact has already flipped, so the view being left is the other one.
                RememberWindow(!ViewModel.IsCompact);
                PlaceWindow(ViewModel.IsCompact);
                ViewModel.Save();
            }
            else if (e.PropertyName == nameof(MainViewModel.GlobalHotkeys))
            {
                ApplyGlobalHotkeys();
            }
        };

        AddKeyboardShortcuts();

        Closed += (_, _) =>
        {
            _hotkeys.Dispose();
            _systemMediaControls?.Dispose();
            ViewModel.Dispose();
        };

        _ = ViewModel.LoadCatalogAsync();
    }

    /// <summary>The size each view gets the first time it is used; after that, the size it was left at.</summary>
    private const int FullWidth = 1100;
    private const int FullHeight = 720;
    private const int CompactWidth = 340;
    private const int CompactHeight = 520;

    /// <summary>The text field inside the country box while it has focus.</summary>
    private TextBox? _countryText;

    /// <summary>The shortcuts that also work while another app has focus.</summary>
    private readonly GlobalHotkeys _hotkeys;

    /// <summary>The Windows media card, or null when Windows would not hand it out.</summary>
    private readonly SystemMediaControls? _systemMediaControls;

    public MainViewModel ViewModel { get; }

    public void BringToFront()
    {
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }

    /// <summary>
    /// Puts the window back where the view being shown was last left. The first time a view is used it gets its
    /// default size, where the window already is.
    /// </summary>
    private void PlaceWindow(bool compact)
    {
        if (ViewModel.WindowPlacement(compact) is { } saved)
        {
            MoveOnScreen(new PointInt32(saved.X, saved.Y), new SizeInt32(saved.Width, saved.Height));
            return;
        }

        var scale = Scale;
        MoveOnScreen(AppWindow.Position, new SizeInt32(
            (int)((compact ? CompactWidth : FullWidth) * scale),
            (int)((compact ? CompactHeight : FullHeight) * scale)));
    }

    /// <summary>Remembers where the window is now, unless it is minimized or maximized, which is no size to return to.</summary>
    private void RememberWindow(bool compact)
    {
        if (AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Restored })
        {
            return;
        }

        ViewModel.RememberWindow(compact, new WindowPlacement
        {
            X = AppWindow.Position.X,
            Y = AppWindow.Position.Y,
            Width = AppWindow.Size.Width,
            Height = AppWindow.Size.Height,
        });
    }

    /// <summary>Moves the window, keeping it on the screen it lands on: a remembered spot may be gone with its monitor.</summary>
    private void MoveOnScreen(PointInt32 position, SizeInt32 size)
    {
        var work = DisplayArea.GetFromPoint(position, DisplayAreaFallback.Nearest).WorkArea;
        var x = Math.Clamp(position.X, work.X, Math.Max(work.X, work.X + work.Width - size.Width));
        var y = Math.Clamp(position.Y, work.Y, Math.Max(work.Y, work.Y + work.Height - size.Height));
        AppWindow.MoveAndResize(new RectInt32(x, y, size.Width, size.Height));
    }

    /// <summary>Keeps the view button left of the minimize, maximize and close buttons, whose width varies.</summary>
    private void KeepViewButtonClearOfCaptionButtons() =>
        ViewButton.Margin = new Thickness(0, 0, AppWindow.TitleBar.RightInset / Scale, 0);

    private double Scale => GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.RefreshCacheSummary();
        SettingsDialog.XamlRoot = Root.XamlRoot;
        await SettingsDialog.ShowAsync();
    }

    private void AddKeyboardShortcuts()
    {
        AddShortcut(VirtualKey.Space, () => ViewModel.TogglePlaybackCommand.Execute(null));
        AddShortcut(VirtualKey.M, () => ViewModel.ToggleMuteCommand.Execute(null));
        AddShortcut(VirtualKey.F, () =>
        {
            // The search box is on the stations tab; it only accepts focus once that tab is shown.
            StationsTab.IsSelected = true;
            DispatcherQueue.TryEnqueue(() => SearchBox.Focus(FocusState.Keyboard));
        });
    }

    /// <summary>
    /// Claims the same actions system wide, on Ctrl+Alt instead of Ctrl: the in-window shortcuts are left where
    /// they are, because taking Ctrl+Space away from every other app would break typing and code completion.
    /// Whatever another app already holds is named in the settings, since only the rest is registered.
    /// </summary>
    private void ApplyGlobalHotkeys()
    {
        _hotkeys.UnregisterAll();
        if (!ViewModel.GlobalHotkeys)
        {
            ViewModel.GlobalHotkeyStatus = "";
            return;
        }

        var taken = new List<string>();
        Claim(VirtualKey.P, "Ctrl+Alt+P", () => ViewModel.TogglePlaybackCommand.Execute(null));
        Claim(VirtualKey.M, "Ctrl+Alt+M", () => ViewModel.ToggleMuteCommand.Execute(null));
        Claim(VirtualKey.Right, "Ctrl+Alt+Right", () => ViewModel.PlayNextFavoriteCommand.Execute(null));
        Claim(VirtualKey.Left, "Ctrl+Alt+Left", () => ViewModel.PlayPreviousFavoriteCommand.Execute(null));

        ViewModel.GlobalHotkeyStatus = taken.Count == 0
            ? ""
            : $"{string.Join(", ", taken)} {(taken.Count == 1 ? "is" : "are")} already in use by another app and will not work from outside the window.";

        void Claim(VirtualKey key, string name, Action action)
        {
            if (!_hotkeys.Register(key, action))
            {
                taken.Add(name);
            }
        }
    }

    private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) =>
        ViewModel.SelectedTab =
            sender.SelectedItem == FavoriteTracksTab ? MainTab.FavoriteTracks
            : sender.SelectedItem == PlayHistoryTab ? MainTab.PlayHistory
            : MainTab.Stations;

    private void AddShortcut(VirtualKey key, Action action)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = VirtualKeyModifiers.Control };
        accelerator.Invoked += (_, args) =>
        {
            action();
            args.Handled = true;
        };
        Root.KeyboardAccelerators.Add(accelerator);
    }

    private void Favorite_ItemClick(object sender, ItemClickEventArgs e) =>
        ViewModel.Play(((FavoriteViewModel)e.ClickedItem).Station);

    private void Station_ItemClick(object sender, ItemClickEventArgs e) =>
        ViewModel.Play(((StationResultViewModel)e.ClickedItem).Station);

    private void Country_GotFocus(object sender, RoutedEventArgs e)
    {
        // The box raises GotFocus again while typing; only entering it should reset the list.
        if (e.OriginalSource is not TextBox text || _countryText is not null)
        {
            return;
        }

        // Select the current country so typing replaces it, and show every country to pick from.
        // Deferred, because a mouse click places the caret after this event.
        _countryText = text;
        DispatcherQueue.TryEnqueue(() => text.SelectAll());
        CountryBox.ItemsSource = ViewModel.Countries;
        CountryBox.IsSuggestionListOpen = true;
    }

    private void Country_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            sender.ItemsSource = ViewModel.MatchCountries(sender.Text);
            sender.IsSuggestionListOpen = true;
        }
    }

    private void Country_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        // Enter picks the best match, so "neth" + Enter is enough.
        if ((args.ChosenSuggestion as string ?? ViewModel.MatchCountries(args.QueryText).FirstOrDefault()) is { } country)
        {
            ViewModel.SelectedCountry = country;
        }

        sender.Text = ViewModel.SelectedCountry;
        sender.IsSuggestionListOpen = false;
        _countryText?.SelectAll();
    }

    private void Country_LostFocus(object sender, RoutedEventArgs e)
    {
        // Checked afterwards, because focus also moves around inside the box.
        DispatcherQueue.TryEnqueue(() =>
        {
            for (var element = FocusManager.GetFocusedElement(CountryBox.XamlRoot) as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
            {
                if (element == CountryBox)
                {
                    return;
                }
            }

            // Leaving the box without choosing keeps the country that is actually selected.
            _countryText = null;
            CountryBox.Text = ViewModel.SelectedCountry;
        });
    }

    private void FavoriteLogo_ImageOpened(object sender, RoutedEventArgs e) =>
        ((FavoriteViewModel)((FrameworkElement)sender).DataContext).Logo.OnImageOpened();

    private void FavoriteLogo_ImageFailed(object sender, ExceptionRoutedEventArgs e) =>
        ((FavoriteViewModel)((FrameworkElement)sender).DataContext).Logo.OnImageFailed();

    private void NowPlayingLogo_ImageOpened(object sender, RoutedEventArgs e) => ViewModel.NowPlayingLogo.OnImageOpened();

    private void NowPlayingLogo_ImageFailed(object sender, ExceptionRoutedEventArgs e) => ViewModel.NowPlayingLogo.OnImageFailed();

    private void Favorite_PointerEntered(object sender, PointerRoutedEventArgs e) => SetFavoritePointerOver(sender, true);

    private void Favorite_PointerExited(object sender, PointerRoutedEventArgs e) => SetFavoritePointerOver(sender, false);

    private static void SetFavoritePointerOver(object sender, bool isPointerOver)
    {
        if (sender is FrameworkElement { DataContext: FavoriteViewModel favorite })
        {
            favorite.IsPointerOver = isPointerOver;
        }
    }

    private void Favorite_GotFocus(object sender, RoutedEventArgs e) => SetFavoriteFocus(e, true);

    private void Favorite_LostFocus(object sender, RoutedEventArgs e) => SetFavoriteFocus(e, false);

    /// <summary>Keeps the buttons of a favorite visible while it is reachable with the keyboard.</summary>
    private static void SetFavoriteFocus(RoutedEventArgs e, bool hasFocus)
    {
        if (e.OriginalSource is FrameworkElement { DataContext: FavoriteViewModel favorite })
        {
            favorite.HasFocus = hasFocus;
        }
    }

    private void RemoveFavorite_Click(object sender, RoutedEventArgs e) =>
        ViewModel.RemoveFavorite((FavoriteViewModel)((FrameworkElement)sender).DataContext);

    private void ToggleFavorite_Click(object sender, RoutedEventArgs e) =>
        ViewModel.ToggleFavorite(((StationResultViewModel)((FrameworkElement)sender).DataContext).Station);

    private void CopyFavoriteTrack_Click(object sender, RoutedEventArgs e) =>
        CopyToClipboard(((FavoriteTrack)((FrameworkElement)sender).DataContext).Title);

    private void RemoveFavoriteTrack_Click(object sender, RoutedEventArgs e) =>
        ViewModel.RemoveFavoriteTrack((FavoriteTrack)((FrameworkElement)sender).DataContext);

    private void CopyHistoryTrack_Click(object sender, RoutedEventArgs e) =>
        CopyToClipboard(((PlayedTrackViewModel)((FrameworkElement)sender).DataContext).Title);

    private void ToggleHistoryTrackSaved_Click(object sender, RoutedEventArgs e) =>
        ViewModel.ToggleHistoryTrackSaved((PlayedTrackViewModel)((FrameworkElement)sender).DataContext);

    private static void CopyToClipboard(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hwnd);
}
