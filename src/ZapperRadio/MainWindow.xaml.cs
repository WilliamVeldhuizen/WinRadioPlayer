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

        _fullSize = Scaled(FullWidth, FullHeight);
        ShowCurrentView(rememberFullSize: false);
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsCompact))
            {
                ShowCurrentView(rememberFullSize: true);
            }
        };

        AddKeyboardShortcuts();
        Closed += (_, _) => ViewModel.Dispose();

        _ = ViewModel.LoadCatalogAsync();
    }

    private const int FullWidth = 1100;
    private const int FullHeight = 720;

    /// <summary>The compact window shows about eight favorites; the rest is scrolled to.</summary>
    private const int CompactWidth = 340;
    private const int CompactHeight = 520;

    /// <summary>The size of the full window, to return to when the compact view is left again.</summary>
    private SizeInt32 _fullSize;

    /// <summary>The text field inside the country box while it has focus.</summary>
    private TextBox? _countryText;

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

    /// <summary>Gives the window the size of the view that is shown now.</summary>
    private void ShowCurrentView(bool rememberFullSize)
    {
        if (!ViewModel.IsCompact)
        {
            ResizeOnScreen(_fullSize);
            return;
        }

        if (rememberFullSize)
        {
            _fullSize = AppWindow.Size;
        }

        ResizeOnScreen(Scaled(CompactWidth, CompactHeight));
    }

    /// <summary>Resizes the window, keeping it on the screen it is on: growing it near an edge must not push it off.</summary>
    private void ResizeOnScreen(SizeInt32 size)
    {
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        var x = Math.Clamp(AppWindow.Position.X, work.X, Math.Max(work.X, work.X + work.Width - size.Width));
        var y = Math.Clamp(AppWindow.Position.Y, work.Y, Math.Max(work.Y, work.Y + work.Height - size.Height));
        AppWindow.MoveAndResize(new RectInt32(x, y, size.Width, size.Height));
    }

    /// <summary>Keeps the view button left of the minimize, maximize and close buttons, whose width varies.</summary>
    private void KeepViewButtonClearOfCaptionButtons() =>
        ViewButton.Margin = new Thickness(0, 0, AppWindow.TitleBar.RightInset / Scale, 0);

    private SizeInt32 Scaled(int width, int height) => new((int)(width * Scale), (int)(height * Scale));

    private double Scale => GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;

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

    private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) =>
        ViewModel.IsShowingFavoriteTracks = sender.SelectedItem == FavoriteTracksTab;

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

    private void CopyFavoriteTrack_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(((FavoriteTrack)((FrameworkElement)sender).DataContext).Title);
        Clipboard.SetContent(package);
    }

    private void RemoveFavoriteTrack_Click(object sender, RoutedEventArgs e) =>
        ViewModel.RemoveFavoriteTrack((FavoriteTrack)((FrameworkElement)sender).DataContext);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hwnd);
}
