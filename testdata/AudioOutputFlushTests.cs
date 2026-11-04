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
    public void RemainingMilliseconds_SubtractsElapsedFlushWait()
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        var later = start + (long)(System.Diagnostics.Stopwatch.Frequency * 1.5);
        Assert.Equal(0, AudioOutputFlush.RemainingMilliseconds(0, start, later));
        Assert.Equal(500, AudioOutputFlush.RemainingMilliseconds(2000, start, later));
        Assert.Equal(0, AudioOutputFlush.RemainingMilliseconds(1000, start, later));
    }

    [Fact]
    public void ShouldRestore_RecoversSilencedSession()
    {
        // 旧版の終了洗い流しは Mute と Volume=0 を同時に書くので、Volume≈0 で拾う。
        Assert.True(AudioSessionRestore.ShouldRestore(muted: true, volume: 0f));
        Assert.True(AudioSessionRestore.ShouldRestore(muted: false, volume: 0f));
        Assert.True(AudioSessionRestore.ShouldRestore(
            muted: false,
            volume: AudioSessionRestore.SilenceEpsilon));
    }

    [Fact]
    public void ShouldRestore_LeavesHealthyUserVolumeUntouched()
    {
        // 意図的なミュート（音量は残したまま）や下げた音量は触らない。
        Assert.False(AudioSessionRestore.ShouldRestore(muted: true, volume: 1f));
        Assert.False(AudioSessionRestore.ShouldRestore(muted: false, volume: 1f));
        Assert.False(AudioSessionRestore.ShouldRestore(muted: false, volume: 0.5f));
        Assert.False(AudioSessionRestore.ShouldRestore(muted: false, volume: 0.01f));
    }

    [Fact]
    public void SetPaused_KeepsFeedingSilenceWithoutStoppingDevice()
    {
        // Pause / Stop は WaveOut / WASAPI でもデバイスを止めず、この無音ゲートで
        // 流し続ける。連続した Read が毎回満杯の無音を返し、カーソルも進まないので、
        // 動いたままのデバイスがドライバ／ミキサの先読みを無音で上書きし続けられる
        // （次の再生の頭に前の曲の尾が混じらない＝ジャジャーン対策の土台）。
        var samples = new float[48000 * 2];
        Array.Fill(samples, 0.5f);
        var document = new AudioDocument(samples, 48000, 2, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.Bind(document, 0, null, loop: false);

        var buffer = new float[960];
        Assert.Equal(960, provider.Read(buffer, 0, buffer.Length));
        var advanced = provider.CursorFrame;
        Assert.True(advanced > 0);

        provider.SetPaused(true);
        for (var i = 0; i < 5; i++)
        {
            Array.Fill(buffer, 1f);
            Assert.Equal(960, provider.Read(buffer, 0, buffer.Length));
            Assert.All(buffer, sample => Assert.Equal(0f, sample));
            Assert.Equal(advanced, provider.CursorFrame);
        }

        // 再生再開（ゲートを閉じる）とカーソルがまた進む。
        provider.SetPaused(false);
        Assert.Equal(960, provider.Read(buffer, 0, buffer.Length));
        Assert.True(provider.CursorFrame > advanced);
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
