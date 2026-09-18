namespace ZapperRadio.Core.Audio;

/// <summary>
/// The running loudness of one station, gathered from the windows of music the sound classifier hears.
/// One window says little: a quiet intro or a loud chorus is several decibels away from the rest of the song.
/// The windows are therefore kept as a histogram and averaged the gated way BS.1770 prescribes, where everything
/// more than 10 LU below the overall average is left out, so quiet passages do not drag the estimate down.
/// A histogram rather than a list, because a favorite streams for hours and its estimate must not grow with it.
/// </summary>
public sealed class StationLoudness
{
    /// <summary>What every station is brought to, in LUFS. Around the level streaming services normalize to.</summary>
    public const double Target = -14.0;

    /// <summary>How far the volume of a station may be corrected, in decibels.</summary>
    public const double MinGainDb = -12.0;

    public const double MaxGainDb = 6.0;

    /// <summary>Windows of music (about 5 seconds each) before the estimate is used; roughly a minute of music.</summary>
    public const int MinWindows = 12;

    /// <summary>How far below the overall average a window may be and still count, as in BS.1770.</summary>
    private const double RelativeGate = 10.0;

    private const double Lowest = Loudness.AbsoluteGate;
    private const double Highest = 5.0;
    private const double BinWidth = 0.1;

    private readonly int[] _bins = new int[(int)((Highest - Lowest) / BinWidth) + 1];

    /// <summary>How many windows were loud enough to count.</summary>
    public int WindowCount { get; private set; }

    /// <summary>Adds the loudness of one window; silence is ignored rather than counted as very quiet.</summary>
    public void Add(double windowLoudness)
    {
        if (double.IsNaN(windowLoudness) || windowLoudness <= Lowest)
        {
            return;
        }

        _bins[Math.Min((int)((Math.Min(windowLoudness, Highest) - Lowest) / BinWidth), _bins.Length - 1)]++;
        WindowCount++;
    }

    /// <summary>The loudness of the station in LUFS, or null while too little music has been heard.</summary>
    public double? Value
    {
        get
        {
            if (WindowCount < MinWindows)
            {
                return null;
            }

            var ungated = Average(double.NegativeInfinity);
            return ungated is not { } average ? null : Average(average - RelativeGate) ?? average;
        }
    }

    /// <summary>The correction this station needs to reach <see cref="Target"/>, or null while it is not known yet.</summary>
    public double? GainDb => Value is { } value ? Math.Clamp(Target - value, MinGainDb, MaxGainDb) : null;

    /// <summary>The loudness of every window above a threshold, averaged over their mean squares rather than their decibels.</summary>
    private double? Average(double threshold)
    {
        var power = 0.0;
        var windows = 0;
        for (var bin = 0; bin < _bins.Length; bin++)
        {
            if (_bins[bin] == 0)
            {
                continue;
            }

            // The middle of the bin stands for every window in it, which is accurate to a twentieth of a decibel.
            var loudness = Lowest + ((bin + 0.5) * BinWidth);
            if (loudness > threshold)
            {
                power += Loudness.ToPower(loudness) * _bins[bin];
                windows += _bins[bin];
            }
        }

        return windows == 0 ? null : Loudness.FromPower(power / windows);
    }
}
