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
    [NotifyPropertyChangedFor(nameof(ShowSoundIndicator))]
    [NotifyPropertyChangedFor(nameof(ShowDotIndicator))]
    public partial StreamStatus Status { get; set; } = StreamStatus.Connecting;

    /// <summary>Whether the station plays music or speech right now, as far as it is heard.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(ShowSoundIndicator))]
    [NotifyPropertyChangedFor(nameof(ShowDotIndicator))]
    public partial Sound Sound { get; set; }

    /// <summary>The song the station is playing right now, or empty when it does not say.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSong))]
    [NotifyPropertyChangedFor(nameof(HasNoSong))]
    public partial string Song { get; set; } = "";

    public bool HasSong => Song.Length > 0;

    /// <summary>Whether the compact view shows the status instead, because the station does not say what it plays.</summary>
    public bool HasNoSong => !HasSong;

    /// <summary>Whether the station marks an ad break right now, which is shown in red.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSoundIndicator))]
    [NotifyPropertyChangedFor(nameof(ShowDotIndicator))]
    public partial bool IsAd { get; set; }

    /// <summary>Whether an ad break is only assumed, which is shown in yellow.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSoundIndicator))]
    [NotifyPropertyChangedFor(nameof(ShowDotIndicator))]
    public partial bool IsAssumedAdBreak { get; set; }

    /// <summary>
    /// Whether the green indicator of the compact view shows what is heard (a note for music, a speech bubble for
    /// talking) instead of a plain dot. Only while the station is live and is not in an ad break.
    /// </summary>
    public bool ShowSoundIndicator =>
        Status == StreamStatus.Live && !IsAd && !IsAssumedAdBreak && Sound is Sound.Music or Sound.Speech;

    public bool ShowDotIndicator => !ShowSoundIndicator;

    public string StatusText => StatusTexts.For(Status, IsActive, Sound);

    /// <summary>What was measured of this station's loudness, as the settings show it.</summary>
    [ObservableProperty]
    public partial string LoudnessText { get; set; } = LoudnessTexts.NotMeasured;
}

public static class SongTexts
{
    public static string For(StationStream stream) => stream switch
    {
        { Metadata.IsAd: true } => "Advertisement",
        { IsAssumedAdBreak: true } => "Probably an ad break",
        // Stations that shout their whole library are toned down, so the list does not shout along.
        { Metadata.Title: { } title } => TrackTitle.Normalize(title),
        _ => "",
    };
}

/// <summary>What the loudness section of the settings shows per station.</summary>
public static class LoudnessTexts
{
    public const string NotMeasured = "Not measured yet";

    /// <summary>The numbers are the same in every language the app might be read in, so they are not localized.</summary>
    private static readonly System.Globalization.CultureInfo Numbers = System.Globalization.CultureInfo.InvariantCulture;

    /// <summary>
    /// The loudness of a station and what is done about it, e.g. "-9.3 LUFS · turned down 4.7 dB". The correction
    /// is left out while it is switched off, because the measurement is still worth showing. While the station is
    /// measured again, the old numbers are still the ones in use, and the text says another measurement is coming.
    /// </summary>
    public static string For(double? loudness, double gainDb, bool normalize, bool remeasuring = false)
    {
        if (loudness is not { } measured)
        {
            return NotMeasured;
        }

        var level = string.Format(Numbers, "{0:0.0} LUFS", measured);
        if (normalize && Math.Abs(gainDb) >= 0.05)
        {
            level += string.Format(Numbers, " · turned {0} {1:0.0} dB", gainDb < 0 ? "down" : "up", Math.Abs(gainDb));
        }

        return remeasuring ? level + " · measuring again…" : level;
    }
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
