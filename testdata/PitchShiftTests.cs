using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class PitchShiftTests
{
    [Fact]
    public void Snap_ClampsToTwoOctaves()
    {
        Assert.Equal(-24, PitchShift.Snap(-100));
        Assert.Equal(24, PitchShift.Snap(100));
        Assert.Equal(0, PitchShift.Snap(0));
        Assert.Equal(7, PitchShift.Snap(7));
    }

    [Fact]
    public void Ratio_IsMusicalTwelfthRoot()
    {
        Assert.Equal(1, PitchShift.Ratio(0), 6);
        Assert.Equal(2, PitchShift.Ratio(12), 6);
        Assert.Equal(0.5, PitchShift.Ratio(-12), 6);
        Assert.Equal(4, PitchShift.Ratio(24), 6);
    }

    [Fact]
    public void NudgeStep_SemitoneOrOctave()
    {
        Assert.Equal(1, PitchShift.NudgeStep(octave: false));
        Assert.Equal(12, PitchShift.NudgeStep(octave: true));
    }

    [Fact]
    public void Apply_ZeroIsCopyOfSameLength()
    {
        var source = MakeSine(440, frames: 2048, channels: 1);
        var after = PitchShift.Apply(source, 1, 48000, 0);
        Assert.Equal(source, after);
        Assert.False(ReferenceEquals(source, after));
    }

    [Fact]
    public void Apply_KeepsAudibleLevel()
    {
        var source = MakeSine(440, frames: 48000, channels: 1);
        var after = PitchShift.Apply(source, 1, 48000, 1);
        var sourceRms = Rms(source, 1, start: 4096, count: 8192);
        var afterRms = Rms(after, 1, start: 4096, count: 8192);
        var afterPeak = Peak(after, 1, start: 4096, count: 8192);
        Assert.True(afterRms > sourceRms * 0.9, $"source={sourceRms}, after={afterRms}");
        Assert.True(afterRms < sourceRms * 1.1, $"source={sourceRms}, after={afterRms}");
        Assert.True(afterPeak < 1.25, $"peak={afterPeak}");
    }

    [Fact]
    public void Apply_KeepsNoiseLoudness()
    {
        var rng = new Random(1);
        var source = new float[48000];
        for (var i = 0; i < source.Length; i++)
        {
            source[i] = (float)((rng.NextDouble() * 2) - 1) * 0.3f;
        }

        var after = PitchShift.Apply(source, 1, 48000, -5);
        var sourceRms = Rms(source, 1, start: 4096, count: 8192);
        var afterRms = Rms(after, 1, start: 4096, count: 8192);
        Assert.True(afterRms > sourceRms * 0.85, $"source={sourceRms}, after={afterRms}");
        Assert.True(afterRms < sourceRms * 1.15, $"source={sourceRms}, after={afterRms}");
    }

    [Fact]
    public void DestFrameCount_KeepsLengthWhenStretching()
    {
        Assert.Equal(48000, PitchShift.DestFrameCount(48000, 12, timeStretch: true));
        Assert.Equal(24000, PitchShift.DestFrameCount(48000, 12, timeStretch: false));
        Assert.Equal(96000, PitchShift.DestFrameCount(48000, -12, timeStretch: false));
        Assert.Equal(48000, PitchShift.DestFrameCount(48000, 0, timeStretch: false));
    }

    [Fact]
    public void Apply_PreservesLengthAndChannelCount()
    {
        var source = MakeSine(440, frames: 4800, channels: 2);
        var after = PitchShift.Apply(source, 2, 48000, 5);
        Assert.Equal(source.Length, after.Length);
    }

    [Fact]
    public void Apply_WithoutStretchShortensWhenPitchUp()
    {
        var source = MakeSine(440, frames: 48000, channels: 1);
        var after = PitchShift.Apply(source, 1, 48000, 12, timeStretch: false);
        Assert.Equal(24000, after.Length);
        var low = Goertzel(after, 1, 0, 48000, 440, start: 1024, count: 4096);
        var high = Goertzel(after, 1, 0, 48000, 880, start: 1024, count: 4096);
        Assert.True(high > low * 4, $"440={low}, 880={high}");
    }

    [Fact]
    public void Apply_OctaveUpMovesSineEnergy()
    {
        var source = MakeSine(440, frames: 48000, channels: 1);
        var after = PitchShift.Apply(source, 1, 48000, 12);
        var root = Goertzel(after, 1, 0, 48000, 440, start: 4096, count: 8192);
        var octave = Goertzel(after, 1, 0, 48000, 880, start: 4096, count: 8192);
        Assert.True(octave > root * 4, $"440={root}, 880={octave}");
    }

    [Fact]
    public void Apply_OctaveDownMovesSineEnergy()
    {
        var source = MakeSine(440, frames: 48000, channels: 1);
        var after = PitchShift.Apply(source, 1, 48000, -12);
        var root = Goertzel(after, 1, 0, 48000, 440, start: 4096, count: 8192);
        var below = Goertzel(after, 1, 0, 48000, 220, start: 4096, count: 8192);
        Assert.True(below > root * 4, $"440={root}, 220={below}");
    }

    [Fact]
    public void Apply_ReportsProgressWhenStretching()
    {
        var values = new List<double>();
        var source = MakeSine(440, frames: 48000, channels: 1);
        PitchShift.Apply(source, 1, 48000, 1, timeStretch: true, new CollectProgress(values));
        Assert.True(values.Count >= 2, $"count={values.Count}");
        Assert.Equal(1, values[^1], 3);
        Assert.Contains(values, value => value > 0 && value < 1);
    }

    [Fact]
    public void Apply_ReportsProgressWithoutStretch()
    {
        var values = new List<double>();
        var source = MakeSine(440, frames: 48000, channels: 1);
        PitchShift.Apply(source, 1, 48000, 12, timeStretch: false, new CollectProgress(values));
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

    private static float Peak(float[] interleaved, int channels, int start, int count)
    {
        var peak = 0f;
        for (var i = 0; i < count; i++)
        {
            var abs = Math.Abs(interleaved[(start + i) * channels]);
            if (abs > peak)
            {
                peak = abs;
            }
        }

        return peak;
    }

    private static double Rms(float[] interleaved, int channels, int start, int count)
    {
        double sum = 0;
        for (var i = 0; i < count; i++)
        {
            var sample = interleaved[(start + i) * channels];
            sum += sample * sample;
        }

        return Math.Sqrt(sum / count);
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
