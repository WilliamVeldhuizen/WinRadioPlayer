namespace ZapperRadio.Core.Models;

/// <summary>A radio station as listed in the rb2rs station directory.</summary>
public sealed record Station(string Name, string Tags, string Country, string Language, string Url)
{
    /// <summary>Short description for display, e.g. "Netherlands · pop, hits".</summary>
    public string Subtitle => string.Join(" · ", new[] { Country, Tags }.Where(s => !string.IsNullOrWhiteSpace(s)));
}
