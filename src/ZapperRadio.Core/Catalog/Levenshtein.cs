namespace ZapperRadio.Core.Catalog;

/// <summary>Levenshtein edit distance: the number of inserted, deleted or replaced characters, ignoring case.</summary>
public static class Levenshtein
{
    /// <summary>Edits needed to turn <paramref name="a"/> into <paramref name="b"/>.</summary>
    public static int Distance(ReadOnlySpan<char> a, ReadOnlySpan<char> b) => Compute(a, b, toPrefix: false);

    /// <summary>
    /// Smallest distance between <paramref name="a"/> and any prefix of <paramref name="b"/>,
    /// so a search term still matches a word that is only partly typed ("nedrl" finds "Nederland").
    /// </summary>
    public static int PrefixDistance(ReadOnlySpan<char> a, ReadOnlySpan<char> b) => Compute(a, b, toPrefix: true);

    private static int Compute(ReadOnlySpan<char> a, ReadOnlySpan<char> b, bool toPrefix)
    {
        // Rows are the characters of a, columns the characters of b; only two rows are kept.
        Span<int> previous = b.Length < 128 ? stackalloc int[b.Length + 1] : new int[b.Length + 1];
        Span<int> current = b.Length < 128 ? stackalloc int[b.Length + 1] : new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var ca = char.ToLowerInvariant(a[i - 1]);
            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (ca == char.ToLowerInvariant(b[j - 1]) ? 0 : 1);
                current[j] = Math.Min(substitution, Math.Min(previous[j], current[j - 1]) + 1);
            }

            var swap = previous;
            previous = current;
            current = swap;
        }

        if (!toPrefix)
        {
            return previous[b.Length];
        }

        // The last row holds the distance from a to every prefix of b.
        var best = int.MaxValue;
        foreach (var distance in previous)
        {
            best = Math.Min(best, distance);
        }

        return best;
    }
}
