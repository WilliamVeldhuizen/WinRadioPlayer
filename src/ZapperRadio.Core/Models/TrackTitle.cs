using System.Text;
using System.Text.RegularExpressions;

namespace ZapperRadio.Core.Models;

/// <summary>
/// Repairs the song titles stations send. Many playout systems shout their library, either whole
/// ("QUEEN - BOHEMIAN RHAPSODY") or one field of it ("MELISSA ETHERIDGE - Like The Way I Do", which is
/// what Dutch stations send), while other capitals are meant the way they are written ("AC/DC - T.N.T.",
/// "ABBA", "R.E.M."). Telling the two apart is guesswork, so the artist and the song are judged on their
/// own and only the ones without a single lower case letter are considered at all: whoever wrote that
/// field wrote it in capitals throughout, which is what shouting looks like. Such a field is left as it
/// is when it is one short word, a dotted abbreviation or a name with a number in it, because that is
/// what a deliberate name looks like. The price of the guess is a name that really is all capitals and
/// long enough to look like a sentence (BLACKPINK); the gain is every shouted title in the list.
/// </summary>
public static partial class TrackTitle
{
    /// <summary>Capitals of at least this many letters in a word are more likely shouting than an abbreviation.</summary>
    private const int ShoutedWordLetters = 5;

    /// <summary>Words that stay in capitals, because that is how they are written everywhere.</summary>
    private static readonly HashSet<string> AlwaysUpper = new(StringComparer.Ordinal) { "DJ", "MC", "TV", "UK", "USA", "NYC" };

    /// <summary>The "Artist - Title" separator, with the hyphen, en dash and em dash stations use for it.</summary>
    [GeneratedRegex(@"( [-–—] )")]
    private static partial Regex Separator();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>
    /// The title as it should be shown: spacing tidied up, and capitals toned down when the station
    /// shouted them. Returns an empty string for a title that is null or blank.
    /// </summary>
    public static string Normalize(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "";
        }

        var text = Whitespace().Replace(title.Trim(), " ");
        // Split on the separator, which the capturing group keeps in the result at the odd positions.
        var parts = Separator().Split(text);
        for (var i = 0; i < parts.Length; i += 2)
        {
            parts[i] = NormalizePart(parts[i]);
        }

        return string.Concat(parts);
    }

    /// <summary>
    /// One side of the separator, usually the artist or the song. A side with a lower case letter in it was
    /// written normally and is never touched. A side that is one short word is kept as it is too, because
    /// "AC/DC", "ABBA", "U2", "TNT" and "YMCA" are written that way on purpose. Several words in a row are a
    /// sentence rather than an abbreviation, so those are toned down however short the words are.
    /// </summary>
    private static string NormalizePart(string part)
    {
        if (part.Any(char.IsLower))
        {
            return part;
        }

        var words = part.Split(' ');
        if (words.Length == 1 && !IsShouted(words[0]))
        {
            return part;
        }

        return string.Join(' ', words.Select(NormalizeWord));
    }

    /// <summary>A word long enough to be shouting rather than an abbreviation, and not a number or an initialism.</summary>
    private static bool IsShouted(string word)
    {
        var capitals = 0;
        foreach (var c in word)
        {
            if (char.IsUpper(c))
            {
                capitals++;
            }
            else if (char.IsDigit(c))
            {
                return false;
            }
        }

        return capitals >= ShoutedWordLetters && !IsInitialism(word);
    }

    /// <summary>A dot followed by another letter, as in "R.E.M." or "T.N.T.", which is not shouting.</summary>
    private static bool IsInitialism(string word)
    {
        for (var i = 0; i + 1 < word.Length; i++)
        {
            if (word[i] == '.' && char.IsLetter(word[i + 1]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A name a number is part of, such as "2PAC" or "3OH!3": a digit with a word of its own behind it.
    /// "80S" is not one, and "U2" and "MP3" come out right by simply capitalizing their first letter.
    /// </summary>
    private static bool IsNameWithNumber(string word)
    {
        for (var i = 0; i + 2 < word.Length; i++)
        {
            if (char.IsDigit(word[i]) && char.IsLetter(word[i + 1]) && char.IsLetter(word[i + 2]))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeWord(string word)
    {
        if (AlwaysUpper.Contains(word) || IsInitialism(word) || IsNameWithNumber(word))
        {
            return word;
        }

        var builder = new StringBuilder(word.Length);
        for (var i = 0; i < word.Length; i++)
        {
            var c = word[i];
            builder.Append(char.IsLetter(c) && StartsWord(word, i) ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Whether the letter at <paramref name="index"/> begins a word, and so gets the capital. A letter after
    /// a hyphen, a slash or a bracket does ("DDU-DU", "(LIVE)"), a letter after a digit does not ("80S").
    /// An apostrophe only begins a word when a single letter comes before it, which separates a name like
    /// "O'CONNOR" from an ending like "DON'T" or "THEY'RE".
    /// </summary>
    private static bool StartsWord(string word, int index)
    {
        if (index == 0)
        {
            return true;
        }

        var previous = word[index - 1];
        if (char.IsLetter(previous) || char.IsDigit(previous))
        {
            return false;
        }

        if (previous is '\'' or '’')
        {
            return index == 2 && index + 1 < word.Length && char.IsLetter(word[index + 1]);
        }

        return true;
    }
}
