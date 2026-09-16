using CommunityToolkit.Mvvm.ComponentModel;
using WinRadioPlayer.Core.Models;

namespace WinRadioPlayer.ViewModels;

public sealed partial class StationResultViewModel(Station station) : ObservableObject
{
    public Station Station { get; } = station;

    public string Name => Station.Name;

    public string Subtitle => Station.Subtitle;

    [ObservableProperty]
    public partial bool IsFavorite { get; set; }
}
