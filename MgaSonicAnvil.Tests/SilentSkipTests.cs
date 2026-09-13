using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class SilentSkipTests
{
    [Fact]
    public void Threshold_DefaultsAndRejectsOutOfRange()
    {
        Assert.Equal(-60, SilentSkip.DefaultThresholdDb);
        Assert.Equal(-60, SilentSkip.ClampThresholdDb(-60));
        Assert.Equal(-120, SilentSkip.ClampThresholdDb(-200));
        Assert.Equal(0, SilentSkip.ClampThresholdDb(3));
        Assert.True(SilentSkip.TryParseThresholdDb("-60", out var db));
        Assert.Equal(-60, db);
        Assert.True(SilentSkip.TryParseThresholdDb("-12.5", out db));
        Assert.Equal(-12.5, db);
        Assert.False(SilentSkip.TryParseThresholdDb("", out _));
        Assert.False(SilentSkip.TryParseThresholdDb("x", out _));
        Assert.False(SilentSkip.TryParseThresholdDb("1", out _));
        Assert.False(SilentSkip.TryParseThresholdDb("-130", out _));
    }

    [Fact]
    public void LinearFromDb_MatchesMinusSixty()
    {
        Assert.Equal(0.001f, SilentSkip.LinearFromDb(-60), 6);
    }

    [Fact]
    public void FindNextAudible_SkipsLeadingSilence()
    {
        var samples = new float[20];
        samples[10] = 0.5f;
        samples[11] = 0.5f;
        var threshold = SilentSkip.LinearFromDb(-60);
        Assert.True(SilentSkip.IsFrameSilent(samples, 2, 0, threshold, 0));
        Assert.Equal(5, SilentSkip.FindNextAudible(samples, 2, 0, 10, threshold, 0));
        Assert.Equal(10, SilentSkip.FindNextAudible(samples, 2, 6, 10, threshold, 0));
    }

    [Fact]
    public void FindNextAudible_HonorsSoloMask()
    {
        var samples = new float[8];
        samples[1] = 0.5f;
        samples[4] = 0.5f;
        var threshold = SilentSkip.LinearFromDb(-60);
        Assert.False(SilentSkip.IsFrameSilent(samples, 2, 0, threshold, 0));
        Assert.True(SilentSkip.IsFrameSilent(samples, 2, 0, threshold, ChannelSolo.MaskOf(0)));
        Assert.Equal(2, SilentSkip.FindNextAudible(samples, 2, 0, 4, threshold, ChannelSolo.MaskOf(0)));
    }

    [Fact]
    public void Provider_JumpsOverSilenceWhenEnabled()
    {
        var samples = new float[200];
        for (var i = 80; i < 200; i++)
        {
            samples[i] = 0.25f;
        }

        var document = new AudioDocument(samples, 48000, 2, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(48000);
        provider.Bind(document, 0, playRange: null, loop: false);
        provider.SetSilentSkip(true, -60);

        var buffer = new float[20];
        Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
        Assert.All(buffer, value => Assert.Equal(0.25f, value, 3));
        Assert.Equal(50, provider.CursorFrame);
    }

    [Fact]
    public void Provider_PlaysSilenceWhenDisabled()
    {
        var samples = new float[200];
        for (var i = 80; i < 200; i++)
        {
            samples[i] = 0.25f;
        }

        var document = new AudioDocument(samples, 48000, 2, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(48000);
        provider.Bind(document, 0, playRange: null, loop: false);
        provider.SetSilentSkip(false, -60);

        var buffer = new float[20];
        Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
        Assert.All(buffer, value => Assert.Equal(0f, value, 3));
        Assert.Equal(10, provider.CursorFrame);
    }

    [Fact]
    public void Provider_SkipsSilenceInTheMiddleOfARead()
    {
        var samples = new float[80];
        for (var frame = 0; frame < 5; frame++)
        {
            samples[frame * 2] = 0.25f;
            samples[frame * 2 + 1] = 0.25f;
        }

        for (var frame = 30; frame < 40; frame++)
        {
            samples[frame * 2] = 0.5f;
            samples[frame * 2 + 1] = 0.5f;
        }

        var document = new AudioDocument(samples, 48000, 2, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(48000);
        provider.Bind(document, 0, playRange: null, loop: false);
        provider.SetSilentSkip(true, -60);

        var buffer = new float[20];
        Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(0.25f, buffer[i], 3);
        }

        for (var i = 10; i < 20; i++)
        {
            Assert.Equal(0.5f, buffer[i], 3);
        }
    }

    [Fact]
    public void Provider_LoopWrapsToNextAudible()
    {
        var samples = new float[40];
        for (var frame = 0; frame < 5; frame++)
        {
            samples[frame * 2] = 0.4f;
            samples[frame * 2 + 1] = 0.4f;
        }

        var document = new AudioDocument(samples, 48000, 2, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(48000);
        provider.Bind(document, 8, new WaveSelection(0, 20), loop: true);
        provider.SetSilentSkip(true, -60);

        var buffer = new float[8];
        Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
        Assert.All(buffer, value => Assert.Equal(0.4f, value, 3));
        Assert.Equal(4, provider.CursorFrame);
    }
}
