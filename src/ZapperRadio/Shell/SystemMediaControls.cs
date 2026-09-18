using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Windows.Media;
using Windows.Storage.Streams;
using ZapperRadio.ViewModels;

namespace ZapperRadio.Shell;

/// <summary>
/// The Windows media card: the station, the song and its logo in the volume flyout and on the lock screen,
/// with play, pause and next, which is also what the media keys of a keyboard or a Bluetooth headset press.
/// <para>
/// The streams cannot provide it themselves. Every favorite is its own <see cref="Windows.Media.Playback.MediaPlayer"/>
/// with the per-player overlay switched off, because twenty of them would each claim the card for themselves,
/// so the app owns one card and feeds it from what the window shows.
/// </para>
/// <para>
/// A WinUI 3 desktop app has no view to ask for the controls, so <c>GetForCurrentView</c> does not apply;
/// they are obtained for the window handle through <c>ISystemMediaTransportControlsInterop</c> instead.
/// </para>
/// </summary>
public sealed class SystemMediaControls : IDisposable
{
    private readonly SystemMediaTransportControls _controls;
    private readonly DispatcherQueue _dispatcher;
    private readonly MainViewModel _viewModel;

    /// <summary>The logo the card is showing, so it is only reloaded when the station changes.</summary>
    private string? _thumbnailUrl;

    private bool _disposed;

    private SystemMediaControls(SystemMediaTransportControls controls, DispatcherQueue dispatcher, MainViewModel viewModel)
    {
        _controls = controls;
        _dispatcher = dispatcher;
        _viewModel = viewModel;

        _controls.IsEnabled = true;
        _controls.IsPlayEnabled = true;
        _controls.IsPauseEnabled = true;
        _controls.IsStopEnabled = true;
        // Next and previous move through the favorites, which is what zapping is.
        _controls.IsNextEnabled = true;
        _controls.IsPreviousEnabled = true;
        _controls.ButtonPressed += OnButtonPressed;

        _viewModel.PropertyChanged += OnViewModelChanged;
        Update();
    }

    /// <summary>
    /// Attaches the media card to a window, or returns null when Windows will not give it out. The card is a
    /// convenience; the app plays radio just as well without it.
    /// </summary>
    public static SystemMediaControls? TryCreate(nint hwnd, DispatcherQueue dispatcher, MainViewModel viewModel)
    {
        try
        {
            return GetForWindow(hwnd) is { } controls ? new SystemMediaControls(controls, dispatcher, viewModel) : null;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or EntryPointNotFoundException or DllNotFoundException)
        {
            return null;
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IsPlaying):
            case nameof(MainViewModel.NowPlayingName):
            case nameof(MainViewModel.NowPlayingSong):
            case nameof(MainViewModel.NowPlayingLogoUrl):
                Update();
                break;
        }
    }

    /// <summary>Puts what the window shows on the card. Without a song, the station itself is the title.</summary>
    private void Update()
    {
        _controls.PlaybackStatus = _viewModel.IsPlaying ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;

        var updater = _controls.DisplayUpdater;
        updater.Type = MediaPlaybackType.Music;
        var song = _viewModel.NowPlayingSong;
        var station = _viewModel.NowPlayingName;
        updater.MusicProperties.Title = song.Length > 0 ? song : station;
        updater.MusicProperties.Artist = song.Length > 0 ? station : "";

        var logoUrl = _viewModel.NowPlayingLogoUrl;
        if (logoUrl != _thumbnailUrl)
        {
            _thumbnailUrl = logoUrl;
            updater.Thumbnail = logoUrl is not null && Uri.TryCreate(logoUrl, UriKind.Absolute, out var uri)
                ? RandomAccessStreamReference.CreateFromUri(uri)
                : null;
        }

        updater.Update();
    }

    private void OnButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        // The card is pressed on a thread of its own, while the commands belong to the window.
        var button = args.Button;
        _dispatcher.TryEnqueue(() =>
        {
            switch (button)
            {
                // Pausing live radio is stopping it: there is nothing to resume from.
                case SystemMediaTransportControlsButton.Play when !_viewModel.IsPlaying:
                case SystemMediaTransportControlsButton.Pause when _viewModel.IsPlaying:
                case SystemMediaTransportControlsButton.Stop when _viewModel.IsPlaying:
                    _viewModel.TogglePlaybackCommand.Execute(null);
                    break;
                case SystemMediaTransportControlsButton.Next:
                    _viewModel.PlayNextFavoriteCommand.Execute(null);
                    break;
                case SystemMediaTransportControlsButton.Previous:
                    _viewModel.PlayPreviousFavoriteCommand.Execute(null);
                    break;
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _viewModel.PropertyChanged -= OnViewModelChanged;
        _controls.ButtonPressed -= OnButtonPressed;
        try
        {
            _controls.DisplayUpdater.ClearAll();
            _controls.PlaybackStatus = MediaPlaybackStatus.Closed;
            _controls.IsEnabled = false;
        }
        catch (COMException)
        {
            // The card is gone with the window; there is nothing left to clear.
        }
    }

    private const string ControlsClassName = "Windows.Media.SystemMediaTransportControls";

    /// <summary>The identifier of ISystemMediaTransportControls, which is what the interop hands back.</summary>
    private static readonly Guid ControlsIid = new("99FA3FF4-1742-42A6-902E-087D41F965EC");

    /// <summary>The identifier of ISystemMediaTransportControlsInterop, the static side of the class.</summary>
    private static readonly Guid InteropIid = new("ddb0472d-c911-4a1f-86d9-dc3d71a95f5a");

    /// <summary>Its one method sits right after the six of IUnknown and IInspectable.</summary>
    private const int GetForWindowSlot = 6;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetForWindowProc(nint self, nint hwnd, ref Guid iid, out nint controls);

    /// <summary>
    /// The controls of a window, through the interop interface of the class. It is called through its vtable
    /// rather than declared as a COM interface, because .NET does not marshal an IInspectable interface.
    /// </summary>
    private static SystemMediaTransportControls? GetForWindow(nint hwnd)
    {
        var interop = ActivationFactory(InteropIid);
        if (interop == 0)
        {
            return null;
        }

        try
        {
            var vtable = Marshal.ReadIntPtr(interop);
            var getForWindow = Marshal.GetDelegateForFunctionPointer<GetForWindowProc>(
                Marshal.ReadIntPtr(vtable, GetForWindowSlot * nint.Size));
            var iid = ControlsIid;
            if (getForWindow(interop, hwnd, ref iid, out var abi) != 0 || abi == 0)
            {
                return null;
            }

            try
            {
                return WinRT.MarshalInspectable<SystemMediaTransportControls>.FromAbi(abi);
            }
            finally
            {
                // The projection holds a reference of its own now.
                Marshal.Release(abi);
            }
        }
        finally
        {
            Marshal.Release(interop);
        }
    }

    /// <summary>Asks Windows for the static side of the class, which is where GetForWindow lives.</summary>
    private static nint ActivationFactory(Guid iid)
    {
        if (WindowsCreateString(ControlsClassName, ControlsClassName.Length, out var name) != 0)
        {
            return 0;
        }

        try
        {
            return RoGetActivationFactory(name, ref iid, out var factory) == 0 ? factory : 0;
        }
        finally
        {
            WindowsDeleteString(name);
        }
    }

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string source, int length, out nint value);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(nint value);

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(nint className, ref Guid iid, out nint factory);
}
