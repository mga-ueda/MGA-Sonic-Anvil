using System.IO;
using MgaSonicAnvil.Audio;
using NAudio.MediaFoundation;
using NAudio.Wave;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class AudioStreamSourceTests
{
    [Fact]
    public void Open_AndRead_YieldsNonSilentFramesFromWave()
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-stream-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WriteToneWave(path, sampleRate: 44100, frames: 4410, frequency: 440);
            using var source = AudioStreamSource.Open(path);
            Assert.Equal(44100, source.SampleRate);
            Assert.Equal(2, source.Channels);
            Assert.True(source.FrameCount >= 4400);

            var frame = new float[2];
            var peak = 0f;
            var read = 0;
            while (read < 2000 && source.TryReadFrame(frame, timeoutMs: 1000))
            {
                peak = Math.Max(peak, Math.Max(Math.Abs(frame[0]), Math.Abs(frame[1])));
                read++;
            }

            Assert.True(read >= 1000);
            Assert.True(peak > 0.1f);

            source.SeekFrame(100);
            Assert.Equal(100, source.Frame);
            Assert.True(source.TryReadFrame(frame, timeoutMs: 1000));
            Assert.Equal(101, source.Frame);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PlaybackSampleProvider_BindStream_EmitsAudio()
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-stream-play-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WriteToneWave(path, sampleRate: 48000, frames: 4800, frequency: 880);
            var document = AudioDocument.CreateDeferred(path);
            Assert.True(AudioCodec.TryActivateStreamPlayback(document));
            Assert.True(document.IsStreamPlayback);
            Assert.True(document.FrameCount > 0);

            using var source = AudioStreamSource.Open(path);
            var provider = new PlaybackSampleProvider();
            provider.SetDeviceSampleRate(48000);
            provider.BindStream(source, document, startFrame: 0, playRange: null, loop: false);
            provider.SetSilentSkip(false, -60);

            var buffer = new float[48000 * 2];
            var written = provider.Read(buffer, 0, buffer.Length);
            Assert.True(written > 1000);

            var peak = 0f;
            for (var i = 0; i < written; i++)
            {
                peak = Math.Max(peak, Math.Abs(buffer[i]));
            }

            Assert.True(peak > 0.1f);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BindStream_SilentSkip_JumpsShortLeadingSilenceFromRing()
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-stream-skip-ring-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            const int rate = 48000;
            const int lead = 4000;
            WriteSilenceThenToneWave(path, rate, lead, toneFrames: 8000, frequency: 440);
            var document = AudioDocument.CreateDeferred(path);
            Assert.True(AudioCodec.TryActivateStreamPlayback(document));
            Assert.True(document.Peaks.IsEmpty);

            using var source = AudioStreamSource.Open(path);
            var provider = new PlaybackSampleProvider();
            provider.SetDeviceSampleRate(rate);
            provider.BindStream(source, document, startFrame: 0, playRange: null, loop: false);
            provider.SetSilentSkip(true, -60);

            Assert.True(ReadUntilAudible(provider, minPeak: 0.05f, maxReads: 8));
            Assert.InRange(provider.CursorFrame, lead, lead + 3000);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BindStream_SilentSkip_JumpsLongLeadingSilenceUsingPeaks()
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-stream-skip-peaks-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            const int rate = 48000;
            const int lead = rate * 2;
            WriteSilenceThenToneWave(path, rate, lead, toneFrames: rate / 5, frequency: 440);
            var document = AudioDocument.CreateDeferred(path);
            Assert.True(AudioCodec.TryActivateStreamPlayback(document));
            document.ReplacePeaks(PeakPyramid.BuildPlayerDisplayFromPath(path));
            Assert.False(document.Peaks.IsEmpty);

            using var source = AudioStreamSource.Open(path);
            var provider = new PlaybackSampleProvider();
            provider.SetDeviceSampleRate(rate);
            provider.BindStream(source, document, startFrame: 0, playRange: null, loop: false);
            provider.SetSilentSkip(true, -60);

            Assert.True(ReadUntilAudible(provider, minPeak: 0.05f, maxReads: 12));
            var hold = SilentSkip.PeakWindowRadiusFrames(rate);
            Assert.InRange(
                provider.CursorFrame,
                lead - hold - document.Peaks.BaseBucketFrames,
                lead + rate / 5);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BindStream_SilentSkipOff_PlaysLeadingSilence()
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-stream-skip-off-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            const int rate = 48000;
            const int lead = 4000;
            WriteSilenceThenToneWave(path, rate, lead, toneFrames: 4000, frequency: 440);
            var document = AudioDocument.CreateDeferred(path);
            Assert.True(AudioCodec.TryActivateStreamPlayback(document));

            using var source = AudioStreamSource.Open(path);
            var provider = new PlaybackSampleProvider();
            provider.SetDeviceSampleRate(rate);
            provider.BindStream(source, document, startFrame: 0, playRange: null, loop: false);
            provider.SetSilentSkip(false, -60);

            var buffer = new float[200 * 2];
            Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
            Assert.All(buffer, value => Assert.Equal(0f, value, 3));
            Assert.Equal(200, provider.CursorFrame);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BindStream_SilentSkip_DoesNotJumpThroughTone()
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-stream-skip-tone-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            const int rate = 48000;
            WriteToneWave(path, rate, frames: rate, frequency: 440);
            var document = AudioDocument.CreateDeferred(path);
            Assert.True(AudioCodec.TryActivateStreamPlayback(document));

            using var source = AudioStreamSource.Open(path);
            var provider = new PlaybackSampleProvider();
            provider.SetDeviceSampleRate(rate);
            provider.BindStream(source, document, startFrame: 0, playRange: null, loop: false);
            provider.SetSilentSkip(true, -60);

            var buffer = new float[2400 * 2];
            Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
            Assert.Equal(2400, provider.CursorFrame);
            var peak = 0f;
            for (var i = 0; i < buffer.Length; i++)
            {
                peak = Math.Max(peak, Math.Abs(buffer[i]));
            }

            Assert.True(peak > 0.1f, $"peak={peak}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BindStream_SilentSkip_FastSpeedDoesNotSkipSilence()
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-stream-skip-ff-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            const int rate = 48000;
            const int lead = 8000;
            WriteSilenceThenToneWave(path, rate, lead, toneFrames: 8000, frequency: 440);
            var document = AudioDocument.CreateDeferred(path);
            Assert.True(AudioCodec.TryActivateStreamPlayback(document));

            using var source = AudioStreamSource.Open(path);
            var provider = new PlaybackSampleProvider();
            provider.SetDeviceSampleRate(rate);
            provider.BindStream(source, document, startFrame: 0, playRange: null, loop: false);
            provider.SetSilentSkip(true, -60);
            provider.SetPlaybackSpeed(PlaybackSampleProvider.FastSpeed);

            var buffer = new float[200 * 2];
            Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
            Assert.All(buffer, value => Assert.Equal(0f, value, 3));
            Assert.Equal(200 * 3, provider.CursorFrame);
            Assert.True(provider.CursorFrame < lead);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BindStream_FastSpeed_UsesVariableRateNotGrainPitch()
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-stream-ff-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WriteRampWave(path, sampleRate: 48000, frames: 48000);
            var document = AudioDocument.CreateDeferred(path);
            Assert.True(AudioCodec.TryActivateStreamPlayback(document));

            using var source = AudioStreamSource.Open(path);
            var provider = new PlaybackSampleProvider();
            provider.SetDeviceSampleRate(48000);
            provider.BindStream(source, document, startFrame: 0, playRange: null, loop: false);
            provider.SetPlaybackSpeed(PlaybackSampleProvider.FastSpeed);

            var frames = 200;
            var buffer = new float[frames * 2];
            Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
            // 可変速: 3 倍なので 0,3,6…（グレイン据え置きの 0,1,2 ではない）。
            Assert.Equal(0f, buffer[0], 3);
            Assert.Equal(3f, buffer[2], 3);
            Assert.Equal(6f, buffer[4], 3);
            Assert.Equal(frames * 3, provider.CursorFrame);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadedPcm_BindStream_FastSpeed_KeepsPeaksAndUsesVariableRate()
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-pcm-ff-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WriteRampWave(path, sampleRate: 48000, frames: 48000);
            var document = AudioCodec.Load(path, buildPeaks: true);
            Assert.False(document.IsStreamPlayback);
            Assert.False(document.Peaks.IsEmpty);
            var samples = document.Interleaved.Length;

            Assert.False(AudioPlayer.ShouldBindPlaybackStream(document, preferStream: false));
            Assert.True(AudioPlayer.ShouldBindPlaybackStream(document, preferStream: true));

            using var source = AudioStreamSource.Open(path);
            var provider = new PlaybackSampleProvider();
            provider.SetDeviceSampleRate(48000);
            provider.BindStream(source, document, startFrame: 0, playRange: null, loop: false);
            provider.SetPlaybackSpeed(PlaybackSampleProvider.FastSpeed);

            var frames = 200;
            var buffer = new float[frames * 2];
            Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
            Assert.Equal(0f, buffer[0], 3);
            Assert.Equal(3f, buffer[2], 3);
            Assert.Equal(6f, buffer[4], 3);
            Assert.Equal(samples, document.Interleaved.Length);
            Assert.False(document.Peaks.IsEmpty);
            Assert.False(document.IsStreamPlayback);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BindStream_RewindSpeed_MovesCursorBackward()
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-stream-rw-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WriteRampWave(path, sampleRate: 48000, frames: 48000);
            var document = AudioDocument.CreateDeferred(path);
            Assert.True(AudioCodec.TryActivateStreamPlayback(document));

            using var source = AudioStreamSource.Open(path);
            var provider = new PlaybackSampleProvider();
            provider.SetDeviceSampleRate(48000);
            provider.BindStream(source, document, startFrame: 3000, playRange: null, loop: false);
            provider.SetPlaybackSpeed(-PlaybackSampleProvider.FastSpeed);

            var frames = 100;
            var buffer = new float[frames * 2];
            Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
            // 逆方向可変速: 3000, 2997, 2994…
            Assert.Equal(3000f, buffer[0], 1);
            Assert.Equal(2997f, buffer[2], 1);
            Assert.Equal(2994f, buffer[4], 1);
            Assert.Equal(3000 - frames * 3, provider.CursorFrame);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BuildPlayerDisplayFromPath_ReportsProgress()
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-stream-peak-fast-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WriteToneWave(path, sampleRate: 48000, frames: 48000, frequency: 220);

            var progress = 0;
            var fromPath = PeakPyramid.BuildPlayerDisplayFromPath(
                path,
                onProgress: _ => Interlocked.Increment(ref progress));

            Assert.False(fromPath.IsEmpty);
            Assert.True(progress > 0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task BuildPlayerDisplayFromPath_Mp3CompletesUnderTwoSecondsForThreeMinutes()
    {
        var wav = Path.Combine(Path.GetTempPath(), "mga-peak-mp3-" + Guid.NewGuid().ToString("N") + ".wav");
        var mp3 = Path.ChangeExtension(wav, ".mp3");
        try
        {
            WriteToneWave(wav, sampleRate: 44100, frames: 44100 * 180, frequency: 440);
            EncodeMp3(wav, mp3);

            var firstProgressMs = -1L;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var peaks = PeakPyramid.BuildPlayerDisplayFromPath(
                mp3,
                onProgress: _ =>
                {
                    if (firstProgressMs < 0)
                    {
                        firstProgressMs = sw.ElapsedMilliseconds;
                    }
                });
            sw.Stop();

            Assert.False(peaks.IsEmpty);
            Assert.True(firstProgressMs >= 0 && firstProgressMs < 300, $"firstProgressMs={firstProgressMs}");
            Assert.True(sw.ElapsedMilliseconds < 2000, $"fullScanMs={sw.ElapsedMilliseconds}");

            // 再生用ストリームと並行してもピークが取れること。
            using var play = AudioStreamSource.Open(mp3);
            var buf = new float[play.Channels * 4096];
            var playTask = Task.Run(() =>
            {
                long total = 0;
                while (total < play.SampleRate * 3)
                {
                    var n = play.ReadFrames(buf, 0, 4096, 2000);
                    if (n <= 0)
                    {
                        break;
                    }

                    total += n;
                }

                return total;
            });

            var concurrent = PeakPyramid.BuildPlayerDisplayFromPath(mp3);
            var played = await playTask;
            Assert.False(concurrent.IsEmpty);
            Assert.True(played > 1000, $"played={played}");
        }
        finally
        {
            TryDelete(wav);
            TryDelete(mp3);
        }
    }

    [Fact]
    public void PlaybackSampleProvider_BindStream_EmitsAudioFromMp3()
    {
        var wav = Path.Combine(Path.GetTempPath(), "mga-stream-mp3-" + Guid.NewGuid().ToString("N") + ".wav");
        var mp3 = Path.ChangeExtension(wav, ".mp3");
        try
        {
            WriteToneWave(wav, sampleRate: 44100, frames: 44100, frequency: 440);
            EncodeMp3(wav, mp3);

            var document = AudioDocument.CreateDeferred(mp3);
            Assert.True(AudioCodec.TryActivateStreamPlayback(document));
            Assert.True(document.IsStreamPlayback);

            using var source = AudioStreamSource.Open(mp3);
            var provider = new PlaybackSampleProvider();
            provider.SetDeviceSampleRate(48000);
            provider.BindStream(source, document, startFrame: 0, playRange: null, loop: false);
            provider.SetSilentSkip(true, -60);

            var buffer = new float[48000 * 2];
            var written = provider.Read(buffer, 0, buffer.Length);
            Assert.True(written > 1000, $"written={written}");

            var peak = 0f;
            for (var i = 0; i < written; i++)
            {
                peak = Math.Max(peak, Math.Abs(buffer[i]));
            }

            Assert.True(peak > 0.01f, $"peak={peak}");
        }
        finally
        {
            TryDelete(wav);
            TryDelete(mp3);
        }
    }

    private static void EncodeMp3(string wavPath, string mp3Path)
    {
        NAudio.MediaFoundation.MediaFoundationApi.Startup();
        using var reader = new AudioFileReader(wavPath);
        MediaFoundationEncoder.EncodeToMp3(reader, mp3Path, 192000);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    private static bool ReadUntilAudible(PlaybackSampleProvider provider, float minPeak, int maxReads)
    {
        var buffer = new float[512 * 2];
        var peak = 0f;
        for (var i = 0; i < maxReads && !provider.Ended; i++)
        {
            var n = provider.Read(buffer, 0, buffer.Length);
            if (n <= 0)
            {
                break;
            }

            for (var s = 0; s < n; s++)
            {
                peak = Math.Max(peak, Math.Abs(buffer[s]));
            }

            if (peak >= minPeak)
            {
                return true;
            }
        }

        return false;
    }

    private static void WriteSilenceThenToneWave(
        string path,
        int sampleRate,
        int silenceFrames,
        int toneFrames,
        double frequency)
    {
        var format = new WaveFormat(sampleRate, 16, 2);
        using var writer = new WaveFileWriter(path, format);
        var frames = silenceFrames + toneFrames;
        var buffer = new byte[frames * format.BlockAlign];
        for (var i = 0; i < toneFrames; i++)
        {
            var t = i / (double)sampleRate;
            var sample = (short)(Math.Sin(2 * Math.PI * frequency * t) * 0.5 * short.MaxValue);
            var at = (silenceFrames + i) * 4;
            buffer[at] = (byte)sample;
            buffer[at + 1] = (byte)(sample >> 8);
            buffer[at + 2] = (byte)sample;
            buffer[at + 3] = (byte)(sample >> 8);
        }

        writer.Write(buffer, 0, buffer.Length);
    }

    private static void WriteToneWave(string path, int sampleRate, int frames, double frequency)
    {
        var format = new WaveFormat(sampleRate, 16, 2);
        using var writer = new WaveFileWriter(path, format);
        var buffer = new byte[frames * format.BlockAlign];
        for (var i = 0; i < frames; i++)
        {
            var t = i / (double)sampleRate;
            var sample = (short)(Math.Sin(2 * Math.PI * frequency * t) * 0.5 * short.MaxValue);
            var at = i * 4;
            buffer[at] = (byte)sample;
            buffer[at + 1] = (byte)(sample >> 8);
            buffer[at + 2] = (byte)sample;
            buffer[at + 3] = (byte)(sample >> 8);
        }

        writer.Write(buffer, 0, buffer.Length);
    }

    private static void WriteRampWave(string path, int sampleRate, int frames)
    {
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2));
        var buffer = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            buffer[i * 2] = i;
            buffer[i * 2 + 1] = i;
        }

        writer.WriteSamples(buffer, 0, buffer.Length);
    }
}
