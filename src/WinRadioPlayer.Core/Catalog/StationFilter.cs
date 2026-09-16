using WinRadioPlayer.Core.Models;

namespace WinRadioPlayer.Core.Catalog;

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

    public bool Matches(Station station)
    {
        if (_country is not null && !string.Equals(station.Country, _country, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string? compactName = null;
        foreach (var term in _terms)
        {
            if (!station.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                && !station.Tags.Contains(term, StringComparison.OrdinalIgnoreCase)
                && !station.Country.Contains(term, StringComparison.OrdinalIgnoreCase)
                // "qmusic" should find "Q music" and "Q-Music".
                && !(compactName ??= Compact(station.Name)).Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
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
