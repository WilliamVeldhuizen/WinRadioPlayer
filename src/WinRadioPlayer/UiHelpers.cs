using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using WinRadioPlayer.Playback;

namespace WinRadioPlayer;

/// <summary>Functions used from x:Bind expressions.</summary>
public static class UiHelpers
{
    private static readonly SolidColorBrush LiveBrush = new(ColorHelper.FromArgb(255, 16, 185, 90));
    private static readonly SolidColorBrush BusyBrush = new(ColorHelper.FromArgb(255, 234, 162, 30));
    private static readonly SolidColorBrush FailedBrush = new(ColorHelper.FromArgb(255, 220, 60, 60));
    private static readonly SolidColorBrush StarOnBrush = new(ColorHelper.FromArgb(255, 245, 184, 0));
    private static readonly SolidColorBrush StarOffBrush = new(ColorHelper.FromArgb(255, 138, 138, 138));

    public static Brush StatusBrush(StreamStatus status) => status switch
    {
        StreamStatus.Live => LiveBrush,
        StreamStatus.Failed => FailedBrush,
        _ => BusyBrush,
    };

    public static string StarGlyph(bool isFavorite) => Glyph(isFavorite ? 0xE735 : 0xE734); // FavoriteStarFill / FavoriteStar

    public static Brush StarBrush(bool isFavorite) => isFavorite ? StarOnBrush : StarOffBrush;

    public static string PlayGlyph(bool isPlaying) => Glyph(isPlaying ? 0xE71A : 0xE768); // Stop / Play

    private static string Glyph(int codePoint) => ((char)codePoint).ToString();
}
