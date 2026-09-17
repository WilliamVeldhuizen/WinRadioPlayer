using System.Globalization;
using System.Runtime.InteropServices;

namespace ZapperRadio;

/// <summary>Reads the "Country or region" chosen in the Windows settings.</summary>
internal static class WindowsRegion
{
    /// <summary>Returns the two-letter ISO code (e.g. "NL"), or null when it is unknown.</summary>
    public static string? GetIsoCode()
    {
        var buffer = new char[16];
        var length = GetUserDefaultGeoName(buffer, buffer.Length);
        // The setting can also be a numeric region such as "001" (World), which is not a country.
        var code = length > 1 ? new string(buffer, 0, length - 1) : null;
        if (code is [_, _] && code.All(char.IsAsciiLetter))
        {
            return code.ToUpperInvariant();
        }

        // Falls back to the region of the display format, which is usually the same country.
        var region = RegionInfo.CurrentRegion.TwoLetterISORegionName;
        return region is [_, _] && region.All(char.IsAsciiLetter) ? region : null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetUserDefaultGeoName([Out] char[] geoName, int geoNameCount);
}
