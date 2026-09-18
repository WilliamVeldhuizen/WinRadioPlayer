using ZapperRadio.Core.Models;

namespace ZapperRadio.Core.Tests;

public class TrackTitleTests
{
    [Theory]
    // A station shouting its whole library is toned down.
    [InlineData("QUEEN - BOHEMIAN RHAPSODY", "Queen - Bohemian Rhapsody")]
    [InlineData("THE VILLAGE PEOPLE - IN THE NAVY", "The Village People - In The Navy")]
    [InlineData("DJ TIËSTO - ADAGIO FOR STRINGS", "DJ Tiësto - Adagio For Strings")]
    // The parts that look like an abbreviation keep their capitals.
    [InlineData("AC/DC - THUNDERSTRUCK", "AC/DC - Thunderstruck")]
    [InlineData("R.E.M. - LOSING MY RELIGION", "R.E.M. - Losing My Religion")]
    [InlineData("ABBA - GIMME GIMME GIMME", "ABBA - Gimme Gimme Gimme")]
    [InlineData("U2 - BEAUTIFUL DAY", "U2 - Beautiful Day")]
    [InlineData("2PAC - CALIFORNIA LOVE", "2PAC - California Love")]
    // Nothing is touched when the capitals are written on purpose, in either part.
    [InlineData("AC/DC - T.N.T.", "AC/DC - T.N.T.")]
    [InlineData("ABBA - SOS", "ABBA - SOS")]
    [InlineData("Village People - YMCA", "Village People - YMCA")]
    [InlineData("MGMT - Kids", "MGMT - Kids")]
    [InlineData("Coldplay - Viva La Vida", "Coldplay - Viva La Vida")]
    public void Normalize_TonesDownShoutingOnly(string title, string expected) =>
        Assert.Equal(expected, TrackTitle.Normalize(title));

    [Theory]
    // Stations that shout one of their two fields; these came off the air as they are written here.
    [InlineData("MELISSA ETHERIDGE - Like The Way I Do", "Melissa Etheridge - Like The Way I Do")]
    [InlineData("Drill Instructor - CAPTAIN JACK", "Drill Instructor - Captain Jack")]
    [InlineData("Iko Iko (My Bestie) - JUSTIN WELLINGTON & SMALL JAM", "Iko Iko (My Bestie) - Justin Wellington & Small Jam")]
    // The other field being written normally says nothing about a name that is a name.
    [InlineData("ABBA - Dancing Queen", "ABBA - Dancing Queen")]
    [InlineData("AC/DC - Back In Black", "AC/DC - Back In Black")]
    [InlineData("Wham! - LAST CHRISTMAS", "Wham! - Last Christmas")]
    public void Normalize_JudgesTheArtistAndTheSongOnTheirOwn(string title, string expected) =>
        Assert.Equal(expected, TrackTitle.Normalize(title));

    [Theory]
    [InlineData("THE PRODIGY - DON'T STOP", "The Prodigy - Don't Stop")]
    [InlineData("SINÉAD O'CONNOR - NOTHING COMPARES 2 U", "Sinéad O'Connor - Nothing Compares 2 U")]
    [InlineData("THEY'RE COMING HOME", "They're Coming Home")]
    [InlineData("BLACKPINK - DDU-DU DDU-DU", "Blackpink - Ddu-Du Ddu-Du")]
    [InlineData("BOWIE - REBEL REBEL (LIVE)", "Bowie - Rebel Rebel (Live)")]
    [InlineData("HITS OF THE 80S", "Hits Of The 80s")]
    public void Normalize_CapitalizesWhereAWordStarts(string title, string expected) =>
        Assert.Equal(expected, TrackTitle.Normalize(title));

    [Theory]
    [InlineData("  QUEEN   -   BOHEMIAN RHAPSODY  ", "Queen - Bohemian Rhapsody")]
    [InlineData("  Queen  -  Bohemian   Rhapsody ", "Queen - Bohemian Rhapsody")]
    [InlineData("QUEEN – BOHEMIAN RHAPSODY", "Queen – Bohemian Rhapsody")]
    public void Normalize_TidiesSpacing(string title, string expected) =>
        Assert.Equal(expected, TrackTitle.Normalize(title));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_ReturnsEmptyForNothing(string? title) =>
        Assert.Equal("", TrackTitle.Normalize(title));
}
