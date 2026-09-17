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
    public void BuildPlayerDisplayStreaming_ProducesNonEmptyPeaks()
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-stream-peak-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WriteToneWave(path, sampleRate: 48000, frames: 9600, frequency: 220);
            using var source = AudioStreamSource.Open(path);
            var peaks = PeakPyramid.BuildPlayerDisplayStreaming(source);
            Assert.False(peaks.IsEmpty);
            Assert.Equal(1, peaks.Channels);
            Assert.Equal(source.FrameCount, peaks.FrameCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BuildPlayerDisplayFromPath_IsFasterThanStreamSourceAndReportsProgress()
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-stream-peak-fast-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            // ~30 秒相当の PCM（48k）。リングバッファ経路との差が出やすい長さ。
            WriteToneWave(path, sampleRate: 48000, frames: 48000 * 30, frequency: 220);

            var progress = 0;
            var swPath = System.Diagnostics.Stopwatch.StartNew();
            var fromPath = PeakPyramid.BuildPlayerDisplayFromPath(
                path,
                onProgress: _ => Interlocked.Increment(ref progress));
            swPath.Stop();

            Assert.False(fromPath.IsEmpty);
            Assert.True(progress > 0);

            var swStream = System.Diagnostics.Stopwatch.StartNew();
            using (var source = AudioStreamSource.Open(path))
            {
                var fromStream = PeakPyramid.BuildPlayerDisplayStreaming(source);
                swStream.Stop();
                Assert.Equal(fromStream.FrameCount, fromPath.FrameCount);
            }

            // 直接走査はポンプ経由より明らかに速いはず（環境差を見て 2 倍以上）。
            Assert.True(
                swPath.ElapsedMilliseconds * 2 < swStream.ElapsedMilliseconds
                || swPath.ElapsedMilliseconds < 500,
                $"path={swPath.ElapsedMilliseconds}ms stream={swStream.ElapsedMilliseconds}ms");
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
}
