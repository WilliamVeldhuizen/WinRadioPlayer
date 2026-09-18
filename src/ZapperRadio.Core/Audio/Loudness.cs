namespace ZapperRadio.Core.Audio;

/// <summary>
/// Measures how loud audio is, the way EBU R128 / ITU-R BS.1770 do it: the samples are K-weighted (a high shelf
/// that models the head and a high pass that drops the rumble the ear barely hears), and the mean square of
/// overlapping 400 ms blocks is averaged, leaving out the blocks that are practically silent.
/// The result is in LUFS, where a full-scale sine reads about -3 and everything a station broadcasts is well below it.
/// One channel is enough, because the streams are mixed down to mono before they are heard.
/// </summary>
public static class Loudness
{
    /// <summary>Blocks quieter than this are silence and are left out of the average, as in BS.1770.</summary>
    public const double AbsoluteGate = -70.0;

    /// <summary>The offset of BS.1770, which cancels the gain the K-weighting gives a 1 kHz tone.</summary>
    private const double Offset = -0.691;

    private const double BlockSeconds = 0.4;

    /// <summary>Blocks overlap by 75%, so each one starts a quarter of a block after the previous.</summary>
    private const int BlockOverlap = 4;

    /// <summary>
    /// The loudness of one stretch of audio in LUFS, or null when it is shorter than a block or is silent.
    /// </summary>
    public static double? Measure(ReadOnlySpan<float> samples, int sampleRate)
    {
        var blockSize = (int)(BlockSeconds * sampleRate);
        var step = blockSize / BlockOverlap;
        if (sampleRate <= 0 || step <= 0 || samples.Length < blockSize)
        {
            return null;
        }

        var weighted = KWeight(samples, sampleRate);
        var power = 0.0;
        var blocks = 0;
        for (var start = 0; start + blockSize <= weighted.Length; start += step)
        {
            var block = 0.0;
            for (var i = start; i < start + blockSize; i++)
            {
                block += weighted[i] * weighted[i];
            }

            block /= blockSize;
            if (FromPower(block) > AbsoluteGate)
            {
                power += block;
                blocks++;
            }
        }

        return blocks == 0 ? null : FromPower(power / blocks);
    }

    /// <summary>The loudness that belongs to a mean square, and the mean square that belongs to a loudness.</summary>
    public static double FromPower(double power) => power <= 0 ? double.NegativeInfinity : Offset + 10 * Math.Log10(power);

    public static double ToPower(double loudness) => Math.Pow(10, (loudness - Offset) / 10);

    /// <summary>The factor a gain in decibels multiplies the volume by: 0 dB is 1, -6 dB is about a half.</summary>
    public static double Linear(double decibels) => Math.Pow(10, decibels / 20);

    /// <summary>Runs the two filters of the K-weighting curve over the samples, in the order BS.1770 gives them.</summary>
    private static double[] KWeight(ReadOnlySpan<float> samples, int sampleRate)
    {
        var shelf = HighShelf(sampleRate);
        var highPass = HighPass(sampleRate);
        var weighted = new double[samples.Length];
        for (var i = 0; i < samples.Length; i++)
        {
            weighted[i] = highPass.Process(shelf.Process(samples[i]));
        }

        return weighted;
    }

    /// <summary>The +4 dB shelf above 1682 Hz that stands in for the head the sound arrives at.</summary>
    private static Biquad HighShelf(int sampleRate)
    {
        const double f0 = 1681.974450955533;
        const double gain = 3.999843853973347;
        const double q = 0.7071752369554196;

        var k = Math.Tan(Math.PI * f0 / sampleRate);
        var vh = Math.Pow(10, gain / 20);
        var vb = Math.Pow(vh, 0.4996667741545416);
        var a0 = 1 + k / q + k * k;
        return new Biquad(
            (vh + vb * k / q + k * k) / a0,
            2 * (k * k - vh) / a0,
            (vh - vb * k / q + k * k) / a0,
            2 * (k * k - 1) / a0,
            (1 - k / q + k * k) / a0);
    }

    /// <summary>The RLB high pass that takes the rumble below about 38 Hz out of the measurement.</summary>
    private static Biquad HighPass(int sampleRate)
    {
        const double f0 = 38.13547087602444;
        const double q = 0.5003270373238773;

        var k = Math.Tan(Math.PI * f0 / sampleRate);
        var a0 = 1 + k / q + k * k;
        return new Biquad(1, -2, 1, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0);
    }

    /// <summary>One second-order section, kept as a struct so a window costs no allocation beyond its samples.</summary>
    private struct Biquad(double b0, double b1, double b2, double a1, double a2)
    {
        private double _x1, _x2, _y1, _y2;

        public double Process(double x)
        {
            var y = (b0 * x) + (b1 * _x1) + (b2 * _x2) - (a1 * _y1) - (a2 * _y2);
            _x2 = _x1;
            _x1 = x;
            _y2 = _y1;
            _y1 = y;
            return y;
        }
    }
}
