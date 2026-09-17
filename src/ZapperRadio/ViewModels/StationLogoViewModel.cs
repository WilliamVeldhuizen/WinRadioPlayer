using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ZapperRadio.ViewModels;

/// <summary>Shows a station's logo once one is found, or its initials in a colored circle until then.</summary>
public sealed partial class StationLogoViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Initials { get; set; } = "";

    // Built from the logo URL in code rather than bound to it directly: x:Bind's implicit
    // string-to-ImageSource conversion is buggy for a null string and can crash the app.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLogo))]
    [NotifyPropertyChangedFor(nameof(ShowFallback))]
    public partial ImageSource? Source { get; set; }

    // Assigning Source only starts loading it; a URL that is reachable but is not a format Image can
    // decode (e.g. an SVG favicon) would otherwise leave both the image and the fallback hidden.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLogo))]
    [NotifyPropertyChangedFor(nameof(ShowFallback))]
    public partial bool IsLoaded { get; set; }

    public bool HasLogo => Source is not null && IsLoaded;

    /// <summary>Whether the initials circle should show; hidden once a logo loads, so a logo with
    /// transparent areas does not let the initials show through behind it.</summary>
    public bool ShowFallback => !HasLogo;

    /// <summary>Sets the fallback initials for a (new) station and clears any logo from a previous one.</summary>
    public void Reset(string name)
    {
        Initials = ComputeInitials(name);
        Source = null;
        IsLoaded = false;
    }

    public void SetLogoUrl(string? url)
    {
        IsLoaded = false;
        Source = url is null ? null : new BitmapImage(new Uri(url));
    }

    /// <summary>Called from the Image's ImageOpened/ImageFailed events, since Source being set only means loading started.</summary>
    public void OnImageOpened() => IsLoaded = true;

    public void OnImageFailed() => IsLoaded = false;

    public static string ComputeInitials(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // "Radio" tells stations apart the least (half the list starts with it), so skip it when there is another word.
        var significant = words.Where(word => !word.Equals("Radio", StringComparison.OrdinalIgnoreCase)).ToList();
        if (significant.Count == 0)
        {
            significant = [.. words];
        }

        if (significant.Count == 0)
        {
            return "?";
        }

        // A single leftover word (e.g. "538" from "Radio 538") reads better as two characters than one.
        return significant.Count == 1
            ? significant[0][..Math.Min(2, significant[0].Length)].ToUpperInvariant()
            : string.Concat(significant.Take(2).Select(word => char.ToUpperInvariant(word[0])));
    }
}
