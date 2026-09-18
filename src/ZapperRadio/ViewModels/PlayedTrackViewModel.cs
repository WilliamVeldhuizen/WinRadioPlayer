using CommunityToolkit.Mvvm.ComponentModel;
using ZapperRadio.Core.Models;

namespace ZapperRadio.ViewModels;

/// <summary>A row of the play history: the song, where and when it played, and whether it is a favorite track.</summary>
public sealed partial class PlayedTrackViewModel(PlayedTrack track) : ObservableObject
{
    public PlayedTrack Track { get; } = track;

    public string Title => Track.Title;

    public string Details => Track.Details;

    /// <summary>Whether the song is in the favorite tracks, so the heart in the row is filled.</summary>
    [ObservableProperty]
    public partial bool IsSaved { get; set; }
}
