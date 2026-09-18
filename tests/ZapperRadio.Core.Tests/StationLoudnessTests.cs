using ZapperRadio.Core.Audio;

namespace ZapperRadio.Core.Tests;

public class StationLoudnessTests
{
    private static StationLoudness Heard(params double[] windows)
    {
        var loudness = new StationLoudness();
        foreach (var window in windows)
        {
            loudness.Add(window);
        }

        return loudness;
    }

    private static StationLoudness Heard(double window, int count) =>
        Heard(Enumerable.Repeat(window, count).ToArray());

    [Fact]
    public void SaysNothingUntilItHasHeardAMinuteOfMusic()
    {
        var loudness = Heard(-10, StationLoudness.MinWindows - 1);
        Assert.Null(loudness.Value);
        Assert.Null(loudness.GainDb);
    }

    [Fact]
    public void SettlesOnTheLevelItKeepsHearing()
    {
        Assert.Equal(-10, Heard(-10, StationLoudness.MinWindows).Value!.Value, 0.1);
    }

    [Fact]
    public void TurnsALoudStationDownToTheTarget()
    {
        // Mastered 4 dB above the target, so it is played 4 dB quieter.
        Assert.Equal(-4, Heard(StationLoudness.Target + 4, 20).GainDb!.Value, 0.1);
    }

    [Fact]
    public void TurnsAQuietStationUp()
    {
        Assert.Equal(3, Heard(StationLoudness.Target - 3, 20).GainDb!.Value, 0.1);
    }

    [Theory]
    // A station far louder or far quieter than the rest is only corrected as far as the limits allow, because
    // beyond that the correction itself becomes the thing you hear.
    [InlineData(-1, StationLoudness.MinGainDb)]
    [InlineData(-40, StationLoudness.MaxGainDb)]
    public void CorrectsNoFurtherThanTheLimits(double measured, double expected)
    {
        Assert.Equal(expected, Heard(measured, 20).GainDb!.Value, 0.1);
    }

    [Fact]
    public void IgnoresSilentWindowsRatherThanCountingThemAsVeryQuiet()
    {
        var loudness = Heard(-80, 20);
        Assert.Equal(0, loudness.WindowCount);
        Assert.Null(loudness.Value);
    }

    /// <summary>The relative gate of BS.1770: a quiet passage does not drag the level of the station down.</summary>
    [Fact]
    public void LeavesTheQuietPassagesOutOfTheAverage()
    {
        var loudness = Heard(Enumerable.Repeat(-8.0, 20).Concat(Enumerable.Repeat(-45.0, 10)).ToArray());
        Assert.Equal(30, loudness.WindowCount);
        Assert.Equal(-8, loudness.Value!.Value, 0.2);
    }

    /// <summary>A chorus and a verse are averaged over their power, not their decibels, as BS.1770 prescribes.</summary>
    [Fact]
    public void AveragesTheLoudAndTheQuietOverTheirPower()
    {
        var loudness = Heard(Enumerable.Repeat(-12.0, 10).Concat(Enumerable.Repeat(-18.0, 10)).ToArray());
        Assert.Equal(-14.1, loudness.Value!.Value, 0.2);
    }
}
