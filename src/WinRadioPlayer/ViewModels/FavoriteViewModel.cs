using CommunityToolkit.Mvvm.ComponentModel;
using WinRadioPlayer.Core.Models;
using WinRadioPlayer.Playback;

namespace WinRadioPlayer.ViewModels;

public sealed partial class FavoriteViewModel(Station station) : ObservableObject
{
    public Station Station { get; } = station;

    public string Name => Station.Name;

    public string Subtitle => Station.Subtitle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial bool IsActive { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial StreamStatus Status { get; set; } = StreamStatus.Connecting;

    [ObservableProperty]
    public partial string ShortcutText { get; set; } = "";

    public string StatusText => StatusTexts.For(Status, IsActive);
}

public static class StatusTexts
{
    public static string For(StreamStatus status, bool isActive) => status switch
    {
        StreamStatus.Connecting => "Verbinden…",
        StreamStatus.Live => isActive ? "Speelt nu" : "Live · gedempt",
        StreamStatus.Buffering => "Bufferen…",
        StreamStatus.Reconnecting => "Opnieuw verbinden…",
        StreamStatus.Failed => "Niet bereikbaar, blijft proberen",
        _ => "",
    };
}
