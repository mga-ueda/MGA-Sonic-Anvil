using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class AudioOutputFlushTests
{
    [Fact]
    public void Asio_TypicalBuffer_HitsVirtualMixerFloor()
    {
        // 512 / 48kHz の先読みは約 21ms。3 hop + 150ms でも床の 2s。
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
        // 16384 / 48kHz の 2 バッファ ≒ 683ms。3 hop + 150ms ≒ 2.2s。
        var ms = AudioOutputFlush.EstimateMilliseconds(
            AudioOutputApi.Asio,
            sampleRate: 48000,
            framesPerBuffer: 16384,
            playbackLatencySamples: 32768);

        Assert.InRange(ms, 2100, 2500);
    }

    [Fact]
    public void Wasapi_HitsVirtualMixerFloor()
    {
        var ms = AudioOutputFlush.EstimateMilliseconds(AudioOutputApi.Wasapi, 48000);
        Assert.Equal(AudioOutputFlush.MinMilliseconds, ms);
    }

    [Fact]
    public void WaveOut_TypicalLatency_HitsVirtualMixerFloor()
    {
        var ms = AudioOutputFlush.EstimateMilliseconds(
            AudioOutputApi.WaveOut,
            sampleRate: 44100,
            waveDesiredLatencyMs: 300,
            waveBufferCount: 2);

        Assert.Equal(AudioOutputFlush.MinMilliseconds, ms);
    }

    [Fact]
    public void WaveOut_HugeLatency_IsCapped()
    {
        var ms = AudioOutputFlush.EstimateMilliseconds(
            AudioOutputApi.WaveOut,
            sampleRate: 44100,
            waveDesiredLatencyMs: 3000,
            waveBufferCount: 3);

        Assert.Equal(AudioOutputFlush.MaxMilliseconds, ms);
    }

    [Fact]
    public void FadeFrames_IsAboutTwentyMilliseconds()
    {
        Assert.Equal(960, AudioOutputFlush.FadeFrames(48000));
        Assert.Equal(32, AudioOutputFlush.FadeFrames(1000));
    }

    [Fact]
    public void MutedSettle_IsMuchShorterThanPipelineWait()
    {
        Assert.InRange(AudioOutputFlush.MutedSettleMilliseconds, 1, 200);
        Assert.True(AudioOutputFlush.MutedSettleMilliseconds < AudioOutputFlush.MinMilliseconds);
    }

    [Fact]
    public void Bind_KeepsPausedSoAsioDoesNotResume()
    {
        var samples = new float[4800];
        Array.Fill(samples, 0.5f);
        var document = new AudioDocument(samples, 48000, 2, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.Bind(document, 0, null, loop: false);
        provider.SetPaused(true);
        provider.Bind(document, 0, null, loop: false);

        var buffer = new float[96];
        Assert.Equal(96, provider.Read(buffer, 0, buffer.Length));
        Assert.All(buffer, sample => Assert.Equal(0f, sample));
        Assert.Equal(0, provider.CursorFrame);
    }

    [Fact]
    public void BeginSilenceFlush_ReturnsSilenceWithoutAdvancingCursor()
    {
        var samples = new float[4800];
        Array.Fill(samples, 0.5f);
        var document = new AudioDocument(samples, 48000, 2, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.Bind(document, 0, null, loop: false);

        var before = provider.CursorFrame;
        provider.BeginSilenceFlush();
        var buffer = new float[960];
        Assert.Equal(960, provider.Read(buffer, 0, buffer.Length));
        Assert.All(buffer, sample => Assert.Equal(0f, sample));
        Assert.Equal(before, provider.CursorFrame);
    }
}
