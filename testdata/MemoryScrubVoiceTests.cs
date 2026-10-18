using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class MemoryScrubVoiceTests
{
    [Fact]
    public void EdgeTaper_LeavesCenterFullAndEndsQuiet()
    {
        var stereo = new float[32];
        for (var i = 0; i < 16; i++)
        {
            stereo[i * 2] = 1f;
            stereo[i * 2 + 1] = 1f;
        }

        MemoryScrubVoice.ApplyEdgeTaper(stereo, 16, 4);
        Assert.True(stereo[0] < 0.2f);
        Assert.True(stereo[15 * 2] < 0.2f);
        Assert.Equal(1f, stereo[8 * 2], 3);
    }

    [Fact]
    public void CaptureThenRead_EmitsDocumentAudio()
    {
        var document = MakeSine(frames: 48000, rate: 48000);
        var voice = new MemoryScrubVoice();
        voice.Bind(document);
        voice.Capture(0);
        var dest = new float[2048];
        voice.Read(dest, 0, 1024, 1f);
        Assert.Contains(dest, sample => Math.Abs(sample) > 0.05f);
    }

    [Fact]
    public void HoldThenRelease_FadesWhenCaptureStops()
    {
        var document = MakeSine(frames: 48000, rate: 48000);
        var voice = new MemoryScrubVoice();
        voice.Bind(document);
        voice.Capture(0);

        var hold = (int)(MemoryScrubVoice.HoldSeconds * 48000) + 8;
        var first = new float[hold * 2];
        voice.Read(first, 0, hold, 1f);
        Assert.Contains(first, sample => Math.Abs(sample) > 0.05f);

        var release = (int)(MemoryScrubVoice.ReleaseSeconds * 48000) + 64;
        var tail = new float[release * 2];
        voice.Read(tail, 0, release, 1f);
        var last = Math.Abs(tail[^2]) + Math.Abs(tail[^1]);
        Assert.True(last < 0.02f);
    }

    [Fact]
    public void TinyMove_DoesNotRetriggerCapture()
    {
        var document = MakeSine(frames: 48000, rate: 48000);
        var voice = new MemoryScrubVoice();
        voice.Bind(document);
        voice.Capture(0);
        voice.Capture(100);
        var dest = new float[2048];
        voice.Read(dest, 0, 1024, 1f);
        Assert.Contains(dest, sample => Math.Abs(sample) > 0.05f);
    }

    [Fact]
    public void SwitchingToScrub_StopsAdvancingPlaybackCursor()
    {
        var document = MakeSine(frames: 8000, rate: 48000);
        var provider = new PlaybackSampleProvider();
        provider.Bind(document, 0, null, loop: false);
        var buffer = new float[512];
        Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
        var afterPlay = provider.CursorFrame;
        Assert.True(afterPlay > 0);

        provider.SetScrubbing(true);
        provider.CaptureScrub(document, 100);
        Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
        Assert.Equal(afterPlay, provider.CursorFrame);
        Assert.False(provider.Ended);
    }

    [Fact]
    public void SetScrubbingAgain_KeepsActiveGrain()
    {
        var document = MakeSine(frames: 8000, rate: 48000);
        var provider = new PlaybackSampleProvider();
        provider.Bind(document, 0, null, loop: false);
        provider.SetScrubbing(true);
        provider.CaptureScrub(document, 0);
        var first = new float[512];
        Assert.Equal(first.Length, provider.Read(first, 0, first.Length));
        Assert.Contains(first, sample => Math.Abs(sample) > 0.01f);

        provider.SetScrubbing(true);
        var second = new float[512];
        Assert.Equal(second.Length, provider.Read(second, 0, second.Length));
        Assert.Contains(second, sample => Math.Abs(sample) > 0.01f);
    }

    [Fact]
    public void PlaybackProvider_ScrubFillsBufferWithoutEnding()
    {
        var document = MakeSine(frames: 8000, rate: 48000);
        var provider = new PlaybackSampleProvider();
        provider.Bind(document, 0, null, loop: false);
        provider.SetScrubbing(true);
        provider.CaptureScrub(document, 0);
        var buffer = new float[512];
        var written = provider.Read(buffer, 0, buffer.Length);
        Assert.Equal(buffer.Length, written);
        Assert.False(provider.Ended);
        Assert.Contains(buffer, sample => Math.Abs(sample) > 0.01f);
    }

    [Fact]
    public void SurroundScrub_ClearsResidualChannelsInReusedBuffer()
    {
        // サラウンド直出し(6ch ソース → 8 ポート)で再生中にスクラブへ切り替えたとき、
        // 再利用バッファの ch2 以降に残った直前の再生音が鳴り続けない(ビー音対策)。
        var frames = 48000;
        var samples = new float[frames * 6];
        for (var i = 0; i < frames; i++)
        {
            var s = MathF.Sin(2 * MathF.PI * 440f * i / 48000f);
            for (var ch = 0; ch < 6; ch++)
            {
                samples[i * 6 + ch] = s;
            }
        }

        var document = new AudioDocument(samples, 48000, 6, 24, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.ConfigureOutput(8, null);
        provider.Bind(document, 0, null, loop: false);
        Assert.Equal(8, provider.WaveFormat.Channels);

        provider.SetScrubbing(true);
        provider.CaptureScrub(document, 0);

        // NAudio / ASIO アダプタのバッファ再利用を模して、直前の再生音の残骸で埋めておく。
        var buffer = new float[8 * 256];
        Array.Fill(buffer, 0.7f);
        Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));

        var heard = false;
        for (var frame = 0; frame < 256; frame++)
        {
            heard |= Math.Abs(buffer[frame * 8]) > 0.01f || Math.Abs(buffer[frame * 8 + 1]) > 0.01f;
            for (var ch = 2; ch < 8; ch++)
            {
                Assert.Equal(0f, buffer[frame * 8 + ch]);
            }
        }

        Assert.True(heard, "スクラブ音が L/R に出ていない");
    }

    [Fact]
    public void PlaybackStoppedDuringScrub_IsIgnored()
    {
        Assert.True(AudioPlayer.ShouldIgnorePlaybackStopped(suppress: false, playing: true, scrubbing: true));
        Assert.True(AudioPlayer.ShouldIgnorePlaybackStopped(suppress: true, playing: true, scrubbing: false));
        Assert.True(AudioPlayer.ShouldIgnorePlaybackStopped(suppress: false, playing: false, scrubbing: false));
        Assert.False(AudioPlayer.ShouldIgnorePlaybackStopped(suppress: false, playing: true, scrubbing: false));
    }

    private static AudioDocument MakeSine(int frames, int rate)
    {
        var samples = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var s = MathF.Sin(2 * MathF.PI * 440f * i / rate);
            samples[i * 2] = s;
            samples[i * 2 + 1] = s;
        }

        return new AudioDocument(samples, rate, 2, 24, AudioFileKind.Wave, null);
    }
}
