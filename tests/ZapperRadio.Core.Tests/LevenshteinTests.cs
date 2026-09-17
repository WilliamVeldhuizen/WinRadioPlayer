using ZapperRadio.Core.Catalog;
using ZapperRadio.Core.Models;

namespace ZapperRadio.Core.Tests;

public class LevenshteinTests
{
    [Theory]
    [InlineData("", "", 0)]
    [InlineData("radio", "", 5)]
    [InlineData("", "radio", 5)]
    [InlineData("radio", "RADIO", 0)]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("flaw", "lawn", 2)]
    [InlineData("radio", "raido", 2)]
    public void Distance_CountsEdits(string a, string b, int expected)
    {
        Assert.Equal(expected, Levenshtein.Distance(a, b));
        Assert.Equal(expected, Levenshtein.Distance(b, a));
    }

    [Theory]
    [InlineData("nederl", "Nederland", 0)]
    [InlineData("nedrl", "Nederland", 1)]
    [InlineData("klassiek", "klasiek", 1)]
    [InlineData("radio", "rad", 2)]
    public void PrefixDistance_ComparesWithTheClosestPrefix(string term, string word, int expected)
    {
        Assert.Equal(expected, Levenshtein.PrefixDistance(term, word));
    }

    [Theory]
    [InlineData("Q music Nederland", StationMatch.Exact)]
    [InlineData("qmusik", StationMatch.Fuzzy)]
    [InlineData("qmuisc nederland", StationMatch.None)]
    [InlineData("qmusic nedrland", StationMatch.Fuzzy)]
    [InlineData("netherlnds", StationMatch.Fuzzy)]
    [InlineData("rock", StationMatch.None)]
    [InlineData("popp", StationMatch.None)]
    [InlineData("music", StationMatch.Exact)]
    [InlineData("wmusic", StationMatch.None)]
    [InlineData("classical", StationMatch.None)]
    public void StationFilter_ToleratesTypos(string query, StationMatch expected)
    {
        var station = new Station("Q music Nederland", "pop", "Netherlands", "", "https://stream.qmusic.nl/qmusic/aachigh");

        Assert.Equal(expected, new StationFilter(query).Match(station));
    }
}
