namespace ZapperRadio.Core.Playback;

/// <summary>
/// Which favorite the next and previous buttons land on. The favorites are a ring: past the last one comes
/// the first again, so pressing the next-track key keeps moving through the list instead of stopping at the end.
/// </summary>
public static class FavoriteRing
{
    /// <summary>
    /// The favorite <paramref name="step"/> places from <paramref name="current"/>, or null when there are no
    /// favorites. A station that is not in the list (nothing is playing, or something outside the favorites is)
    /// has no place to step from, so going forwards starts at the first favorite and going backwards at the last.
    /// </summary>
    public static string? Step(IReadOnlyList<string> urls, string? current, int step)
    {
        if (urls.Count == 0)
        {
            return null;
        }

        var index = current is null ? -1 : IndexOf(urls, current);
        if (index < 0)
        {
            return step >= 0 ? urls[0] : urls[^1];
        }

        // The remainder of a negative number is negative in C#, so it is brought back into range.
        return urls[((index + step) % urls.Count + urls.Count) % urls.Count];
    }

    private static int IndexOf(IReadOnlyList<string> urls, string url)
    {
        for (var i = 0; i < urls.Count; i++)
        {
            if (string.Equals(urls[i], url, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
