using System.IO;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using NAudio.Wave;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class GaplessPlaybackTests
{
    [Fact]
    public void Read_SpansIntoPrefetchedNext_WithoutEnding()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mga-gapless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var firstPath = Path.Combine(dir, "a.wav");
        var secondPath = Path.Combine(dir, "b.wav");
        try
        {
            const int rate = 48000;
            const int firstFrames = 64;
            const int secondFrames = 64;
            WriteDc(firstPath, rate, firstFrames, 0);
            WriteDc(secondPath, rate, secondFrames, 16000);

            var firstDoc = StreamDocument(firstPath, rate, firstFrames);
            var secondDoc = StreamDocument(secondPath, rate, secondFrames);
            var first = AudioStreamSource.Open(firstPath);
            var second = AudioStreamSource.Open(secondPath);
            var provider = new PlaybackSampleProvider();
            provider.SetDeviceSampleRate(rate);
            provider.BindStream(first, firstDoc, 0, null, loop: false);
            Assert.True(provider.TryArmGapless(second, secondDoc));

            var frames = firstFrames + 24;
            var buffer = new float[frames * 2];
            var written = provider.Read(buffer, 0, buffer.Length);
            Assert.Equal(buffer.Length, written);
            Assert.False(provider.Ended);
            Assert.True(provider.TryTakeGaplessAdvance(out var advanced));
            Assert.Same(secondDoc, advanced);
            Assert.True(Math.Abs(buffer[firstFrames * 2]) > 0.4f);
            Assert.True(Math.Abs(buffer[0]) < 0.05f);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public void Stream_IsDrainedOnlyAfterRealSamplesAreGone()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mga-gapless-drain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "a.wav");
        try
        {
            WriteDc(path, 48000, 128, 8000);
            using var source = AudioStreamSource.Open(path);
            Assert.False(source.IsDrained);
            var frame = new float[2];
            var read = 0;
            while (source.TryReadFrame(frame, timeoutMs: 1000))
            {
                read++;
            }

            Assert.Equal(128, read);
            Assert.True(source.IsDrained);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    private static AudioDocument StreamDocument(string path, int rate, int frames)
    {
        var document = new AudioDocument([], rate, 2, 16, AudioFileKind.Wave, path, buildPeaks: false);
        document.ActivateStreamPlayback(rate, 2, 16, frames);
        return document;
    }

    private static void WriteDc(string path, int sampleRate, int frames, short sample)
    {
        var format = new WaveFormat(sampleRate, 16, 2);
        using var writer = new WaveFileWriter(path, format);
        var buffer = new byte[frames * format.BlockAlign];
        for (var i = 0; i < frames; i++)
        {
            var at = i * 4;
            buffer[at] = (byte)sample;
            buffer[at + 1] = (byte)(sample >> 8);
            buffer[at + 2] = (byte)sample;
            buffer[at + 3] = (byte)(sample >> 8);
        }

        writer.Write(buffer, 0, buffer.Length);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
