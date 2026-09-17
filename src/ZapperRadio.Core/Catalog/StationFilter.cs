using ZapperRadio.Core.Models;

namespace ZapperRadio.Core.Catalog;

/// <summary>How well a station matches a search.</summary>
public enum StationMatch
{
    None,
    /// <summary>Every term matches, but at least one only with a typo (see <see cref="Levenshtein"/>).</summary>
    Fuzzy,
    Exact,
}

/// <summary>Search over name, tags and country: every word in the query must match somewhere.</summary>
public sealed class StationFilter
{
    private readonly string[] _terms;
    private readonly string? _country;

    public StationFilter(string? query, string? country = null)
    {
        _terms = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _country = string.IsNullOrWhiteSpace(country) ? null : country;
    }

    public bool IsEmpty => _terms.Length == 0 && _country is null;

    public bool Matches(Station station) => Match(station) != StationMatch.None;

    public StationMatch Match(Station station)
    {
        if (_country is not null && !string.Equals(station.Country, _country, StringComparison.OrdinalIgnoreCase))
        {
            return StationMatch.None;
        }

        var result = StationMatch.Exact;
        string? compactName = null;
        foreach (var term in _terms)
        {
            if (station.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || station.Tags.Contains(term, StringComparison.OrdinalIgnoreCase)
                || station.Country.Contains(term, StringComparison.OrdinalIgnoreCase)
                // "qmusic" should find "Q music" and "Q-Music".
                || (compactName ??= Compact(station.Name)).Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var maxEdits = MaxEdits(term);
            if (maxEdits > 0
                && (ContainsSimilarWord(station.Name, term, maxEdits)
                    || ContainsSimilarWord(station.Tags, term, maxEdits)
                    || ContainsSimilarWord(station.Country, term, maxEdits)
                    || ContainsSimilarWord(compactName ??= Compact(station.Name), term, maxEdits)))
            {
                result = StationMatch.Fuzzy;
                continue;
            }

            return StationMatch.None;
        }

        return result;
    }

    /// <summary>Short terms get no typo tolerance: "rock" would otherwise also find "rick" and "roc".</summary>
    private static int MaxEdits(string term) => term.Length switch
    {
        < 5 => 0,
        < 8 => 1,
        _ => 2,
    };

    /// <summary>True when a word in <paramref name="text"/> starts with something within <paramref name="maxEdits"/> of the term.</summary>
    private static bool ContainsSimilarWord(string text, string term, int maxEdits)
    {
        var i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && !char.IsLetterOrDigit(text[i])) i++;
            var start = i;
            while (i < text.Length && char.IsLetterOrDigit(text[i])) i++;

            var word = text.AsSpan(start, i - start);
            // Typos rarely hit the first letter; requiring it keeps "qmusic" from matching every "music" station.
            if (word.Length >= term.Length - maxEdits
                && char.ToLowerInvariant(word[0]) == char.ToLowerInvariant(term[0])
                && Levenshtein.PrefixDistance(term, word) <= maxEdits)
            {
                return true;
            }
        }

        return false;
    }

    private static string Compact(string value) =>
        string.Create(value.Count(char.IsLetterOrDigit), value, static (span, source) =>
        {
            var i = 0;
            foreach (var c in source)
            {
                if (char.IsLetterOrDigit(c)) span[i++] = c;
            }
        });
}
