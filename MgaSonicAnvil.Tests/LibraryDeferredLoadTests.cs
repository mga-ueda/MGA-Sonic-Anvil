using System;
using System.IO;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibraryDeferredLoadTests
{
    [Fact]
    public void CreateDeferred_RegistersPathWithoutPcm()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp3");
        File.WriteAllBytes(path, new byte[1234]);
        try
        {
            var document = AudioDocument.CreateDeferred(path);
            Assert.True(document.IsDeferredLoad);
            Assert.Equal(0, document.FrameCount);
            Assert.Equal(1234, document.FileBytes);
            Assert.Equal(AudioFileKind.Mp3, document.SourceKind);
            Assert.Equal(path, document.SourcePath);
            Assert.False(document.IsDirty);
            Assert.False(document.IsStreamPlayback);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ActivateStreamPlayback_SetsLengthWithoutPcm()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp3");
        var document = AudioDocument.CreateDeferred(path);
        document.ActivateStreamPlayback(44100, 2, 16, 88200);
        Assert.True(document.IsStreamPlayback);
        Assert.False(document.IsDeferredLoad);
        Assert.Equal(88200, document.FrameCount);
        Assert.Equal(44100, document.SampleRate);
        Assert.Equal(2, document.Channels);
        Assert.Empty(document.Interleaved);
        Assert.True(document.Peaks.IsEmpty);
        Assert.True(AudioCodec.CanStreamPlay(path));
        Assert.True(AudioCodec.CanStreamPlay(Path.ChangeExtension(path, ".m4a")));
    }

    [Theory]
    [InlineData(8000)]
    [InlineData(11025)]
    [InlineData(16000)]
    [InlineData(22050)]
    public void LowSampleRateWave_StreamMetaMatchesPeaksAndDuration(int sampleRate)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mga-low-sr-{sampleRate}-{Guid.NewGuid():N}.wav");
        try
        {
            var frames = sampleRate * 2;
            WriteMonoToneWave(path, sampleRate, frames, frequency: 440);

            Assert.True(AudioCodec.TryProbeStreamFormat(path, out var rate, out _, out _, out var probedFrames));
            Assert.Equal(sampleRate, rate);
            Assert.Equal(frames, probedFrames);

            var document = AudioDocument.CreateDeferred(path);
            Assert.True(AudioCodec.TryActivateStreamPlayback(document));
            Assert.Equal(sampleRate, document.SampleRate);
            Assert.Equal(frames, document.FrameCount);
            Assert.Equal(2.0, document.DurationSeconds, 3);

            var peaks = PeakPyramid.BuildPlayerDisplayFromPath(path);
            Assert.False(peaks.IsBuilding);
            Assert.Equal(document.FrameCount, peaks.FrameCount);
            Assert.Equal(peaks.FrameCount, peaks.FilledFrames);

            using var stream = AudioStreamSource.Open(path, prebufferTimeoutMs: 0);
            Assert.Equal(document.SampleRate, stream.SampleRate);
            Assert.Equal(document.FrameCount, stream.FrameCount);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void SyncStreamPlaybackMeta_KeepsPeaksWhenAligningLength()
    {
        var document = AudioDocument.CreateDeferred("x.wav");
        document.ActivateStreamPlayback(8000, 1, 16, 16000);
        var peaks = PeakPyramid.BuildPlayerDisplay(new float[8000], channels: 1, sampleCount: 8000);
        document.ReplacePeaks(peaks);
        Assert.False(document.Peaks.IsEmpty);

        document.SyncStreamPlaybackMeta(8000, 1, 16, peaks.FrameCount);
        Assert.Equal(peaks.FrameCount, document.FrameCount);
        Assert.Same(peaks, document.Peaks);
        Assert.True(document.IsStreamPlayback);
        Assert.Empty(document.Interleaved);
    }

    private static void WriteMonoToneWave(string path, int sampleRate, int frames, double frequency)
    {
        using var writer = new NAudio.Wave.WaveFileWriter(path, new NAudio.Wave.WaveFormat(sampleRate, 16, 1));
        var buffer = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            buffer[i] = (float)(Math.Sin(2 * Math.PI * frequency * i / sampleRate) * 0.2);
        }

        writer.WriteSamples(buffer, 0, frames);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignore
        }
    }

    [Fact]
    public void CompressedBitRate_UsesTagsForMp3AndM4a()
    {
        var mp3 = new AudioDocument([], 44100, 2, 16, AudioFileKind.Mp3, "a.mp3", buildPeaks: false);
        mp3.ApplyTags(new AudioFileTags { Probed = true, BitRateKbps = 320 });
        Assert.Equal(320, mp3.CompressedBitRateKbps);

        var m4a = new AudioDocument([], 48000, 2, 16, AudioFileKind.M4a, "a.m4a", buildPeaks: false);
        m4a.ApplyTags(new AudioFileTags { Probed = true, BitRateKbps = 256 });
        Assert.Equal(256, m4a.CompressedBitRateKbps);
    }

    [Fact]
    public void CompressedBitRate_WaveStaysZero()
    {
        var wave = new AudioDocument(new float[48], 48000, 1, 16, AudioFileKind.Wave, null);
        wave.ApplyTags(new AudioFileTags { Probed = true, BitRateKbps = 1411 });
        Assert.Equal(0, wave.CompressedBitRateKbps);
    }

    [Fact]
    public void CompressedBitRate_EstimatesFromFileSizeWhenTagsMissing()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp3");
        File.WriteAllBytes(path, new byte[192_000]);
        try
        {
            var document = AudioDocument.CreateDeferred(path);
            document.ActivateStreamPlayback(48000, 2, 16, 48000);
            Assert.Equal(1536, document.CompressedBitRateKbps);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Constructor_CanSkipPeakBuild()
    {
        var samples = new float[480];
        var document = new AudioDocument(
            samples,
            48000,
            1,
            16,
            AudioFileKind.Wave,
            null,
            buildPeaks: false);
        Assert.True(document.Peaks.IsEmpty);
        Assert.Equal(480, document.FrameCount);

        var peaks = PeakPyramid.Build(samples, 1);
        document.ReplacePeaks(peaks);
        Assert.False(document.Peaks.IsEmpty);
        Assert.Equal(480, document.Peaks.FrameCount);
    }

    [Fact]
    public void EmptyDocument_IsNotDeferred()
    {
        var document = new AudioDocument([], 48000, 2, 24, AudioFileKind.Wave, null);
        Assert.False(document.IsDeferredLoad);
        Assert.Equal(0, document.FrameCount);
    }

    [Fact]
    public void CreateRow_Deferred_BlanksFormatColumnsKeepsSizeAndKind()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
        File.WriteAllBytes(path, new byte[2048]);
        try
        {
            var session = new DocumentSession(AudioDocument.CreateDeferred(path));
            var row = LibraryBrowserView.CreateRow(session);
            Assert.Equal(Path.GetFileName(path), row.Name);
            Assert.Equal("WAVE", row.Kind);
            Assert.Equal(0, row.DurationSeconds);
            Assert.Equal(string.Empty, row.DurationText);
            Assert.Equal(0, row.SampleRate);
            Assert.Equal(string.Empty, row.SampleRateText);
            Assert.Equal(string.Empty, row.BitDepthText);
            Assert.Equal(string.Empty, row.ChannelsText);
            Assert.Equal(2048, row.FileBytes);
            Assert.Equal(Path.GetDirectoryName(path), row.Folder);
            Assert.True(session.Document.FileLastWriteTime.HasValue);
            Assert.Equal(session.Document.FileLastWriteTime.Value, row.FileDate);
            Assert.False(string.IsNullOrEmpty(row.DateText));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReplaceDocument_SwapsLoadedAudioOffDeferred()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".aiff");
        var session = new DocumentSession(AudioDocument.CreateDeferred(path));
        var loaded = new AudioDocument(new float[48], 48000, 1, 16, AudioFileKind.Aiff, path);
        session.ReplaceDocument(loaded);
        Assert.Same(loaded, session.Document);
        Assert.False(session.Document.IsDeferredLoad);
        Assert.Equal(48, session.Document.FrameCount);
    }
}
