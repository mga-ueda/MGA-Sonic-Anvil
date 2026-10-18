using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using Xunit;


namespace MgaSonicAnvil.Tests;

public sealed class ClickGuardTests
{
    [Fact]
    public void FadeFrames_UsesGivenMilliseconds()
    {
        Assert.Equal(20, ClickGuard.DefaultFadeMilliseconds);
        Assert.Equal(960, ClickGuard.FadeFrames(48000));
        Assert.Equal(20, ClickGuard.FadeFrames(1000));
        Assert.Equal(96, ClickGuard.FadeFrames(48000, 2));
        Assert.Equal(1, ClickGuard.FadeFrames(200, 1));
    }

    [Fact]
    public void ClampFadeMs_KeepsOneToOneHundred()
    {
        Assert.Equal(1, ClickGuard.ClampFadeMs(0));
        Assert.Equal(20, ClickGuard.ClampFadeMs(20));
        Assert.Equal(100, ClickGuard.ClampFadeMs(1000));
        Assert.True(ClickGuard.TryParseFadeMs("20", out var parsed));
        Assert.Equal(20, parsed);
        Assert.False(ClickGuard.TryParseFadeMs("0", out _));
        Assert.False(ClickGuard.TryParseFadeMs("101", out _));
    }

    [Fact]
    public void FadeRangeEdges_StartsAndEndsAtZero()
    {
        var samples = new float[8];
        Array.Fill(samples, 1f);
        ClickGuard.FadeRangeEdges(
            samples,
            channels: 1,
            bufferStartFrame: 0,
            new WaveSelection(0, 8),
            mask: 0,
            fadeIn: true,
            fadeOut: true,
            fadeFrames: 3);

        Assert.Equal(0f, samples[0], 5);
        Assert.Equal(1f, samples[3], 5);
        Assert.Equal(0f, samples[7], 5);
        Assert.True(samples[1] > 0f && samples[1] < 1f);
        Assert.True(samples[6] > 0f && samples[6] < 1f);
    }

    [Fact]
    public void FadeRecordedAudio_FadesAudibleSpansOnly()
    {
        var take = new float[10];
        Array.Fill(take, 0.5f);
        take[4] = 0f;
        take[5] = 0f;
        ClickGuard.FadeRecordedAudio(
            take,
            channels: 1,
            [
                new RecordedSpan(0, 4, Silent: false),
                new RecordedSpan(4, 6, Silent: true),
                new RecordedSpan(6, 10, Silent: false),
            ],
            takeSampleRate: 1000,
            spanSampleRate: 1000,
            fadeOpenTail: true);

        Assert.Equal(0f, take[0], 5);
        Assert.Equal(0f, take[3], 5);
        Assert.Equal(0f, take[4], 5);
        Assert.Equal(0f, take[6], 5);
        Assert.Equal(0f, take[9], 5);
    }
}
