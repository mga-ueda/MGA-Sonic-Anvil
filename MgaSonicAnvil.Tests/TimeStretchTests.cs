using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class TimeStretchTests
{
    [Fact]
    public void DestFrameCount_ScalesByPercent()
    {
        Assert.Equal(48000, TimeStretch.DestFrameCount(48000, 100));
        Assert.Equal(24000, TimeStretch.DestFrameCount(48000, 50));
        Assert.Equal(96000, TimeStretch.DestFrameCount(48000, 200));
    }

    [Fact]
    public void DestFrameCount_ClampsToTenThroughThousandPercent()
    {
        Assert.Equal(4800, TimeStretch.DestFrameCount(48000, 1));
        Assert.Equal(480000, TimeStretch.DestFrameCount(48000, 5000));
    }

    [Fact]
    public void PercentOf_MatchesDestRatio()
    {
        Assert.Equal(100, TimeStretch.PercentOf(48000, 48000));
        Assert.Equal(50, TimeStretch.PercentOf(48000, 24000));
        Assert.Equal(200, TimeStretch.PercentOf(48000, 96000));
    }

    [Fact]
    public void IsNoOp_WhenLengthUnchanged()
    {
        Assert.True(TimeStretch.IsNoOp(48000, 48000));
        Assert.False(TimeStretch.IsNoOp(48000, 24000));
        Assert.False(TimeStretch.IsNoOp(0, 0));
    }

    [Fact]
    public void PercentNudgeStep_UsesModifiers()
    {
        Assert.Equal(0.1, TimeStretch.PercentNudgeStep(shift: false, control: false));
        Assert.Equal(1, TimeStretch.PercentNudgeStep(shift: true, control: false));
        Assert.Equal(10, TimeStretch.PercentNudgeStep(shift: false, control: true));
        Assert.Equal(25, TimeStretch.PercentNudgeStep(shift: true, control: true));
    }

    [Fact]
    public void Apply_SameLengthIsCopy()
    {
        var source = MakeSine(440, frames: 2048, channels: 1);
        var after = TimeStretch.Apply(source, 1, 48000, 2048);
        Assert.Equal(source, after);
        Assert.False(ReferenceEquals(source, after));
    }

    [Fact]
    public void Apply_DoublesLengthAtTwoHundredPercent()
    {
        var source = MakeSine(440, frames: 48000, channels: 1);
        var after = TimeStretch.Apply(source, 1, 48000, 96000);
        Assert.Equal(96000, after.Length);
        var root = Goertzel(after, 1, 0, 48000, 440, start: 8192, count: 8192);
        var octave = Goertzel(after, 1, 0, 48000, 880, start: 8192, count: 8192);
        Assert.True(root > octave * 4, $"440={root}, 880={octave}");
    }

    [Fact]
    public void Apply_ReportsProgress()
    {
        var values = new List<double>();
        var source = MakeSine(440, frames: 48000, channels: 1);
        TimeStretch.Apply(source, 1, 48000, 24000, new CollectProgress(values));
        Assert.True(values.Count >= 2, $"count={values.Count}");
        Assert.Equal(1, values[^1], 3);
    }

    private static float[] MakeSine(double hertz, int frames, int channels)
    {
        var samples = new float[frames * channels];
        for (var i = 0; i < frames; i++)
        {
            var value = (float)Math.Sin(2 * Math.PI * hertz * i / 48000d);
            for (var ch = 0; ch < channels; ch++)
            {
                samples[i * channels + ch] = value;
            }
        }

        return samples;
    }

    private static double Goertzel(
        float[] interleaved,
        int channels,
        int channel,
        int sampleRate,
        double hertz,
        int start,
        int count)
    {
        var omega = 2 * Math.PI * hertz / sampleRate;
        var coeff = 2 * Math.Cos(omega);
        double s0 = 0;
        double s1 = 0;
        double s2 = 0;
        for (var i = 0; i < count; i++)
        {
            s0 = interleaved[(start + i) * channels + channel] + coeff * s1 - s2;
            s2 = s1;
            s1 = s0;
        }

        return s1 * s1 + s2 * s2 - coeff * s1 * s2;
    }

    private sealed class CollectProgress(List<double> values) : IProgress<double>
    {
        public void Report(double value) => values.Add(value);
    }
}
