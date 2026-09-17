using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using ZapperRadio.Playback;

namespace ZapperRadio;

/// <summary>Functions used from x:Bind expressions.</summary>
public static class UiHelpers
{
    private static readonly SolidColorBrush LiveBrush = new(ColorHelper.FromArgb(255, 16, 185, 90));
    private static readonly SolidColorBrush BusyBrush = new(ColorHelper.FromArgb(255, 234, 162, 30));
    private static readonly SolidColorBrush FailedBrush = new(ColorHelper.FromArgb(255, 220, 60, 60));
    private static readonly SolidColorBrush StarOnBrush = new(ColorHelper.FromArgb(255, 245, 184, 0));
    private static readonly SolidColorBrush HeartOnBrush = new(ColorHelper.FromArgb(255, 232, 64, 87));
    private static readonly SolidColorBrush StarOffBrush = new(ColorHelper.FromArgb(255, 138, 138, 138));

    public static Brush StatusBrush(StreamStatus status) => status switch
    {
        StreamStatus.Live => LiveBrush,
        StreamStatus.Failed => FailedBrush,
        _ => BusyBrush,
    };

    public static string StarGlyph(bool isFavorite) => Glyph(isFavorite ? 0xE735 : 0xE734); // FavoriteStarFill / FavoriteStar

    public static Brush StarBrush(bool isFavorite) => isFavorite ? StarOnBrush : StarOffBrush;

    public static string HeartGlyph(bool isSaved) => Glyph(isSaved ? 0xE00B : 0xE006); // HeartFill / Heart

    public static Brush HeartBrush(bool isSaved) => isSaved ? HeartOnBrush : StarOffBrush;

    public static string SaveTrackToolTip(bool isSaved) => isSaved ? "Remove from favorite tracks" : "Add to favorite tracks";

    public static string PlayGlyph(bool isPlaying) => Glyph(isPlaying ? 0xE71A : 0xE768); // Stop / Play

    public static string MuteGlyph(bool isMuted) => Glyph(isMuted ? 0xE74F : 0xE767); // Mute / Volume

    public static string MuteToolTip(bool isMuted) => isMuted ? "Unmute (Ctrl+M)" : "Mute (Ctrl+M)";

    private static string Glyph(int codePoint) => ((char)codePoint).ToString();
}
