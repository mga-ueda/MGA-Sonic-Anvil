using System.IO;
using MgaSonicAnvil.Audio;
using NAudio.Wave;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class StreamSourceReleaseTests
{
    [Fact]
    public void ReleaseStreamSource_AllowsDeletingBoundFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mga-stream-release-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "a.wav");
        try
        {
            WriteTone(path, 44100, 4410);
            var document = StreamDocument(path, 44100, 4410);
            var source = AudioStreamSource.Open(path);
            var provider = new PlaybackSampleProvider();
            provider.SetDeviceSampleRate(44100);
            provider.BindStream(source, document, 0, null, loop: false);
            Assert.True(provider.IsStreamBound);
            Assert.True(provider.IsBoundTo(document));

            provider.ReleaseStreamSource();
            Assert.False(provider.IsStreamBound);
            Assert.False(provider.IsBoundTo(document));

            File.Delete(path);
            Assert.False(File.Exists(path));
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public void ReleaseStreamSource_ClosesGaplessPrefetchFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mga-stream-release-gap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var firstPath = Path.Combine(dir, "a.wav");
        var secondPath = Path.Combine(dir, "b.wav");
        try
        {
            WriteTone(firstPath, 48000, 128);
            WriteTone(secondPath, 48000, 128);
            var firstDoc = StreamDocument(firstPath, 48000, 128);
            var secondDoc = StreamDocument(secondPath, 48000, 128);
            var first = AudioStreamSource.Open(firstPath);
            var second = AudioStreamSource.Open(secondPath);
            var provider = new PlaybackSampleProvider();
            provider.SetDeviceSampleRate(48000);
            provider.BindStream(first, firstDoc, 0, null, loop: false);
            Assert.True(provider.TryArmGapless(second, secondDoc));

            provider.ReleaseStreamSource();
            Assert.False(provider.IsStreamBound);

            File.Delete(firstPath);
            File.Delete(secondPath);
            Assert.False(File.Exists(firstPath));
            Assert.False(File.Exists(secondPath));
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public void ClearGaplessNext_AllowsDeletingPrefetchFileWhileCurrentStaysBound()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mga-stream-clear-gap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var firstPath = Path.Combine(dir, "a.wav");
        var secondPath = Path.Combine(dir, "b.wav");
        try
        {
            WriteTone(firstPath, 48000, 128);
            WriteTone(secondPath, 48000, 128);
            var firstDoc = StreamDocument(firstPath, 48000, 128);
            var secondDoc = StreamDocument(secondPath, 48000, 128);
            var first = AudioStreamSource.Open(firstPath);
            var second = AudioStreamSource.Open(secondPath);
            var provider = new PlaybackSampleProvider();
            provider.SetDeviceSampleRate(48000);
            provider.BindStream(first, firstDoc, 0, null, loop: false);
            Assert.True(provider.TryArmGapless(second, secondDoc));

            provider.ClearGaplessNext();
            File.Delete(secondPath);
            Assert.False(File.Exists(secondPath));
            Assert.True(provider.IsStreamBound);
            Assert.True(provider.IsBoundTo(firstDoc));

            provider.ReleaseStreamSource();
            File.Delete(firstPath);
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

    private static void WriteTone(string path, int sampleRate, int frames)
    {
        var format = new WaveFormat(sampleRate, 16, 2);
        using var writer = new WaveFileWriter(path, format);
        var buffer = new byte[frames * format.BlockAlign];
        for (var i = 0; i < frames; i++)
        {
            short sample = (short)(i % 2 == 0 ? 8000 : -8000);
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
