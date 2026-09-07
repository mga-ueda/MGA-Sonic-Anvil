using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class AudioOutputFlushTests
{
    [Fact]
    public void Asio_TypicalBuffer_IsAboutHalfASecond()
    {
        // 512 / 48kHz の先読みは約 21ms。3 hop + 150ms でも床の 0.5s。
        var ms = AudioOutputFlush.EstimateMilliseconds(
            AudioOutputApi.Asio,
            sampleRate: 48000,
            framesPerBuffer: 512,
            playbackLatencySamples: 1024);

        Assert.Equal(AudioOutputFlush.MinMilliseconds, ms);
    }

    [Fact]
    public void Asio_LargeBuffer_ScalesWithQueuedLatency()
    {
        // 4096 / 48kHz の 2 バッファ ≒ 171ms。3 hop + 150ms ≒ 0.66s。
        var ms = AudioOutputFlush.EstimateMilliseconds(
            AudioOutputApi.Asio,
            sampleRate: 48000,
            framesPerBuffer: 4096,
            playbackLatencySamples: 8192);

        Assert.InRange(ms, 600, 900);
    }

    [Fact]
    public void Wasapi_StaysUnderOneSecond()
    {
        var ms = AudioOutputFlush.EstimateMilliseconds(AudioOutputApi.Wasapi, 48000);
        Assert.InRange(ms, 800, 1000);
    }

    [Fact]
    public void WaveOut_UsesRequestedLatency()
    {
        var ms = AudioOutputFlush.EstimateMilliseconds(
            AudioOutputApi.WaveOut,
            sampleRate: 44100,
            waveDesiredLatencyMs: 300,
            waveBufferCount: 2);

        Assert.InRange(ms, 1200, 1400);
    }

    [Fact]
    public void FadeFrames_IsAboutTwentyMilliseconds()
    {
        Assert.Equal(960, AudioOutputFlush.FadeFrames(48000));
        Assert.Equal(32, AudioOutputFlush.FadeFrames(1000));
    }
}
