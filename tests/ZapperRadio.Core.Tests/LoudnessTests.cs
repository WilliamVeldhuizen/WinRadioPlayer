using ZapperRadio.Core.Audio;

namespace ZapperRadio.Core.Tests;

public class LoudnessTests
{
    private const int SampleRate = 16000;

    /// <summary>Three seconds of a tone, which is several of the 400 ms blocks the loudness is averaged over.</summary>
    private static float[] Tone(double frequency, double amplitude, double seconds = 3)
    {
        var samples = new float[(int)(seconds * SampleRate)];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)(amplitude * Math.Sin(2 * Math.PI * frequency * i / SampleRate));
        }

        return samples;
    }

    /// <summary>
    /// The calibration of BS.1770: one channel of a 1 kHz sine reads its own RMS, which is 3.01 dB below its
    /// amplitude. Full scale is therefore -3.01 LUFS and a tenth of that is -23.01.
    /// </summary>
    [Theory]
    [InlineData(1.0, -3.01)]
    [InlineData(0.1, -23.01)]
    [InlineData(0.01, -43.01)]
    public void MeasuresAToneAtItsOwnLevel(double amplitude, double expected)
    {
        Assert.Equal(expected, Loudness.Measure(Tone(1000, amplitude), SampleRate)!.Value, 0.2);
    }

    [Fact]
    public void DoublingTheAmplitudeAddsSixDecibels()
    {
        var quiet = Loudness.Measure(Tone(1000, 0.1), SampleRate)!.Value;
        var loud = Loudness.Measure(Tone(1000, 0.2), SampleRate)!.Value;
        Assert.Equal(6.02, loud - quiet, 0.05);
    }

    /// <summary>The RLB high pass of the K-weighting: rumble counts for far less than what is actually heard.</summary>
    [Fact]
    public void HardlyCountsTheRumbleBelowHearing()
    {
        var rumble = Loudness.Measure(Tone(20, 0.5), SampleRate)!.Value;
        var tone = Loudness.Measure(Tone(1000, 0.5), SampleRate)!.Value;
        Assert.True(tone - rumble > 12, $"20 Hz read {rumble:0.0} LUFS against {tone:0.0} for 1 kHz");
    }

    /// <summary>The high shelf of the K-weighting: the top end counts about 4 dB heavier, as the head hears it.</summary>
    [Fact]
    public void CountsTheTopEndHeavier()
    {
        var high = Loudness.Measure(Tone(6000, 0.5), SampleRate)!.Value;
        var tone = Loudness.Measure(Tone(1000, 0.5), SampleRate)!.Value;
        Assert.InRange(high - tone, 2.5, 5.0);
    }

    [Fact]
    public void HasNothingToSayAboutSilence()
    {
        Assert.Null(Loudness.Measure(new float[3 * SampleRate], SampleRate));
    }

    [Fact]
    public void HasNothingToSayAboutLessThanABlock()
    {
        Assert.Null(Loudness.Measure(Tone(1000, 0.5, 0.3), SampleRate));
    }

    [Fact]
    public void LeavesTheSilenceOutOfWhatItAverages()
    {
        // A tone with three seconds of silence after it is as loud as the tone alone, give or take the one block
        // that straddles the two. Without the gate, half of the power being silence would cost a full 3 dB.
        var tone = Tone(1000, 0.5);
        var withSilence = tone.Concat(new float[3 * SampleRate]).ToArray();
        Assert.Equal(Loudness.Measure(tone, SampleRate)!.Value, Loudness.Measure(withSilence, SampleRate)!.Value, 0.5);
    }

    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(-6.02, 0.5)]
    [InlineData(6.02, 2.0)]
    public void TurnsDecibelsIntoAFactorToMultiplyTheVolumeBy(double decibels, double expected)
    {
        Assert.Equal(expected, Loudness.Linear(decibels), 0.005);
    }
}
