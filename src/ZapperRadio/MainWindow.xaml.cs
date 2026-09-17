using System.Runtime.InteropServices;
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
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "ZapperRadio.ico"));

        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(1100 * scale), (int)(720 * scale)));

        AddKeyboardShortcuts();
        Closed += (_, _) => ViewModel.Dispose();

        _ = ViewModel.LoadCatalogAsync();
    }

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
