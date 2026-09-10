using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LevelMeterEngineTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(0.5, -6.020599913279624)]
    [InlineData(0, -160)]
    public void ToDb_MatchesTwentyLog10Floor(double linear, double expected)
    {
        Assert.Equal(expected, LevelMeterEngine.ToDb(linear), 9);
    }

    [Fact]
    public void DbToNorm_IsEvenAboveKneeThenCompresses()
    {
        Assert.Equal(0, LevelMeterEngine.DbToNorm(-60), 9);
        Assert.Equal(LevelMeterEngine.KneeNorm, LevelMeterEngine.DbToNorm(-20), 9);
        Assert.Equal(1, LevelMeterEngine.DbToNorm(0), 9);

        var topStep = LevelMeterEngine.DbToNorm(0) - LevelMeterEngine.DbToNorm(-5);
        Assert.Equal(topStep, LevelMeterEngine.DbToNorm(-5) - LevelMeterEngine.DbToNorm(-10), 9);
        Assert.Equal(topStep, LevelMeterEngine.DbToNorm(-15) - LevelMeterEngine.DbToNorm(-20), 9);

        var justBelow = LevelMeterEngine.DbToNorm(-20) - LevelMeterEngine.DbToNorm(-25);
        var midQuiet = LevelMeterEngine.DbToNorm(-35) - LevelMeterEngine.DbToNorm(-40);
        var deep = LevelMeterEngine.DbToNorm(-50) - LevelMeterEngine.DbToNorm(-55);
        Assert.True(justBelow < topStep);
        Assert.True(midQuiet < justBelow);
        Assert.True(deep < midQuiet);
    }

    [Fact]
    public void FormatReadout_FloorsAtMinus60()
    {
        Assert.Equal("-60.0", LevelMeterEngine.FormatReadout(-80));
        Assert.Equal("-60.0", LevelMeterEngine.FormatReadout(-60));
        Assert.Equal("-50.0", LevelMeterEngine.FormatReadout(-50));
        Assert.Equal("0.0", LevelMeterEngine.FormatReadout(0));
        Assert.Equal("-6.0", LevelMeterEngine.FormatReadout(-6.02));
    }

    [Fact]
    public void Update_ComputesRmsOfConstantWindow()
    {
        var left = new float[LevelMeterEngine.WindowFrames];
        var right = new float[LevelMeterEngine.WindowFrames];
        Array.Fill(left, 0.5f);
        Array.Fill(right, 0.25f);

        var engine = new LevelMeterEngine();
        var snap = engine.Update(left, right, nowSeconds: 1);

        Assert.Equal(LevelMeterEngine.ToDb(0.5), snap.Left.InstPeakDb, 6);
        Assert.Equal(LevelMeterEngine.ToDb(0.5), snap.Left.InstRmsDb, 6);
        Assert.Equal(LevelMeterEngine.ToDb(0.25), snap.Right.InstPeakDb, 6);
        Assert.Equal(LevelMeterEngine.ToDb(0.25), snap.Right.InstRmsDb, 6);
    }

    [Fact]
    public void Update_LightsClipWhenPeakHitsFullScale()
    {
        var left = new float[LevelMeterEngine.WindowFrames];
        left[0] = 1f;
        var right = new float[LevelMeterEngine.WindowFrames];

        var engine = new LevelMeterEngine();
        var snap = engine.Update(left, right, nowSeconds: 0.5);

        Assert.True(snap.ClipLeft);
        Assert.False(snap.ClipRight);
        Assert.Equal(0, snap.Left.InstPeakDb, 6);
    }

    [Fact]
    public void PlaybackProvider_CopyMeterWindow_MirrorsStereoTap()
    {
        var samples = new float[LevelMeterEngine.WindowFrames * 2];
        for (var i = 0; i < LevelMeterEngine.WindowFrames; i++)
        {
            samples[i * 2] = 0.4f;
            samples[i * 2 + 1] = -0.2f;
        }

        var document = new AudioDocument(samples, 48000, 2, 24, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.Bind(document, 0, null, loop: false);
        var buffer = new float[samples.Length];
        Assert.Equal(samples.Length, provider.Read(buffer, 0, buffer.Length));

        var left = new float[LevelMeterEngine.WindowFrames];
        var right = new float[LevelMeterEngine.WindowFrames];
        provider.CopyMeterWindow(left, right);
        Assert.All(left, sample => Assert.Equal(0.4f, sample, 5));
        Assert.All(right, sample => Assert.Equal(-0.2f, sample, 5));
    }

    [Fact]
    public void PlaybackProvider_TakeMeterInterval_AggregatesThenResets()
    {
        var samples = new float[] { 0.5f, -0.25f, 0.25f, 0.5f };
        var document = new AudioDocument(samples, 48000, 2, 24, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.Bind(document, 0, null, loop: false);
        var buffer = new float[samples.Length];
        Assert.Equal(samples.Length, provider.Read(buffer, 0, buffer.Length));

        Assert.True(provider.TakeMeterInterval(out var peakL, out var rmsL, out var peakR, out var rmsR));
        Assert.Equal(0.5f, peakL, 5);
        Assert.Equal(0.5f, peakR, 5);
        Assert.Equal(Math.Sqrt((0.25 + 0.0625) / 2), rmsL, 5);
        Assert.Equal(Math.Sqrt((0.0625 + 0.25) / 2), rmsR, 5);
        Assert.False(provider.TakeMeterInterval(out _, out _, out _, out _));
    }

    [Fact]
    public void PlaybackProvider_CopyRecentOutputSamples_IsMonoMixTail()
    {
        var frames = 8;
        var samples = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            samples[i * 2] = 0.4f;
            samples[i * 2 + 1] = -0.2f;
        }

        var document = new AudioDocument(samples, 48000, 2, 24, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.Bind(document, 0, null, loop: false);
        var buffer = new float[samples.Length];
        Assert.Equal(samples.Length, provider.Read(buffer, 0, buffer.Length));

        var dest = new float[16];
        Assert.Equal(frames, provider.CopyRecentOutputSamples(dest));
        for (var i = 0; i < dest.Length - frames; i++)
        {
            Assert.Equal(0f, dest[i]);
        }

        for (var i = dest.Length - frames; i < dest.Length; i++)
        {
            Assert.Equal(0.1f, dest[i], 5);
        }
    }

    [Fact]
    public void Update_SurroundHidesRmsAndKeepsAllPeaks()
    {
        var peaks = new float[] { 0.5f, 0.25f, 1f, 0.1f, 0.2f, 0.3f };
        var rms = new float[] { 0.2f, 0.1f, 0.4f, 0.05f, 0.1f, 0.15f };
        var engine = new LevelMeterEngine();
        var snap = engine.Update(peaks, rms, nowSeconds: 1, hasSamples: true);

        Assert.False(snap.ShowRms);
        Assert.Equal(6, snap.Channels.Length);
        Assert.Equal(LevelMeterEngine.ToDb(1), snap.Channels[2].InstPeakDb, 6);
        Assert.True(snap.Clips[2]);
    }

    [Fact]
    public void PlaybackProvider_DirectRoute_MetersAllSourceChannels()
    {
        var frames = 8;
        var samples = new float[frames * 6];
        for (var i = 0; i < frames; i++)
        {
            samples[i * 6 + 2] = 0.8f;
        }

        var document = new AudioDocument(samples, 48000, 6, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.ConfigureOutput(8, null);
        provider.Bind(document, 0, null, loop: false);
        var buffer = new float[frames * 8];
        Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));

        var dest = new float[frames];
        Assert.Equal(frames, provider.CopyRecentOutputSamples(dest));
        var expected = ChannelMix.Mid([0f, 0f, 0.8f, 0f, 0f, 0f]);
        Assert.All(dest, sample => Assert.Equal(expected, sample, 5));

        var peaks = new float[ChannelLayout.MaxChannels];
        var rms = new float[ChannelLayout.MaxChannels];
        Assert.True(provider.TakeMeterInterval(peaks, rms, out var channels));
        Assert.Equal(6, channels);
        Assert.Equal(0f, peaks[0], 5);
        Assert.Equal(0f, peaks[1], 5);
        Assert.Equal(0.8f, peaks[2], 5);
    }
}
