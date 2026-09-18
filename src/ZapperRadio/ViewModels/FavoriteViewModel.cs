using CommunityToolkit.Mvvm.ComponentModel;
using ZapperRadio.Core.Audio;
using ZapperRadio.Core.Models;
using ZapperRadio.Playback;

namespace ZapperRadio.ViewModels;

public sealed partial class FavoriteViewModel(Station station) : ObservableObject
{
    public Station Station { get; } = station;

    public StationLogoViewModel Logo { get; } = new() { Initials = StationLogoViewModel.ComputeInitials(station.Name) };

    public string Name => Station.Name;

    /// <summary>Whether the mouse is over the row.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowActions))]
    public partial bool IsPointerOver { get; set; }

    /// <summary>Whether the row, or a button in it, has the keyboard focus.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowActions))]
    public partial bool HasFocus { get; set; }

    /// <summary>Whether the row shows its buttons, which stay out of the way until the row is pointed at or focused.</summary>
    public bool ShowActions => IsPointerOver || HasFocus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial bool IsActive { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial StreamStatus Status { get; set; } = StreamStatus.Connecting;

    /// <summary>Whether the station plays music or speech right now, as far as it is heard.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial Sound Sound { get; set; }

    /// <summary>The song the station is playing right now, or empty when it does not say.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSong))]
    public partial string Song { get; set; } = "";

    public bool HasSong => Song.Length > 0;

    /// <summary>Whether the station marks an ad break right now, which is shown in red.</summary>
    [ObservableProperty]
    public partial bool IsAd { get; set; }

    /// <summary>Whether an ad break is only assumed, which is shown in yellow.</summary>
    [ObservableProperty]
    public partial bool IsAssumedAdBreak { get; set; }

    public string StatusText => StatusTexts.For(Status, IsActive, Sound);
}

public static class SongTexts
{
    public static string For(StationStream stream) => stream switch
    {
        { Metadata.IsAd: true } => "Advertisement",
        { IsAssumedAdBreak: true } => "Probably an ad break",
        { Metadata.Title: { } title } => title,
        _ => "",
    };
}

public static class StatusTexts
{
    public static string For(StreamStatus status, bool isActive, Sound sound) => status switch
    {
        StreamStatus.Connecting => "Connecting…",
        StreamStatus.Live => (isActive ? "Now playing" : "Live · muted") + SoundSuffix(sound),
        StreamStatus.Buffering => "Buffering…",
        StreamStatus.Reconnecting => "Reconnecting…",
        StreamStatus.Failed => "Unreachable, still retrying",
        _ => "",
    };

    private static string SoundSuffix(Sound sound) => sound switch
    {
        Sound.Music => " · music",
        Sound.Speech => " · speech",
        _ => "",
    };
}
