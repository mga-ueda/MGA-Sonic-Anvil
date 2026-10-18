using System;
using System.IO;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
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
    public void CreateRow_KeepsTagDurationWhenStreamLengthDiffersByEncoderDelay()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp3");
        var document = AudioDocument.CreateDeferred(path);
        document.ApplyTags(new AudioFileTags
        {
            Probed = true,
            DurationSeconds = 10,
            SampleRate = 44100,
            Channels = 2,
            BitsPerSample = 16,
        });
        // Xing 10s に対し、デコード側だけ 1 フレーム（1152 samples @ 44.1kHz ≈ 26ms）長い。
        document.ActivateStreamPlayback(44100, 2, 16, 441000 + 1152);

        var row = LibraryBrowserView.CreateRow(new DocumentSession(document));
        Assert.Equal(10, row.DurationSeconds, 3);
        Assert.Equal(UiStrings.FormatDuration(10), row.DurationText);
        Assert.True(document.DurationSeconds > 10);
    }

    [Fact]
    public void CreateRow_Mp3_BlanksBitDepthKeepsBitRate()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp3");
        var document = AudioDocument.CreateDeferred(path);
        document.ApplyTags(new AudioFileTags
        {
            Probed = true,
            DurationSeconds = 12,
            SampleRate = 44100,
            Channels = 2,
            BitsPerSample = 16,
            BitRateKbps = 320,
        });

        var row = LibraryBrowserView.CreateRow(new DocumentSession(document));
        Assert.Equal("MP3", row.Kind);
        Assert.Equal(0, row.BitDepth);
        Assert.Equal(string.Empty, row.BitDepthText);
        Assert.False(row.BitDepthNonDefault);
        Assert.Equal(320, row.BitRateKbps);
        Assert.Equal(UiStrings.FormatBitRate(320), row.BitRateText);
        Assert.Equal("44.1kHz", row.SampleRateText);
        Assert.Equal("2ch", row.ChannelsText);
    }

    [Fact]
    public void CreateRow_Wave_BlanksBitRateKeepsBitDepth()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
        var document = AudioDocument.CreateDeferred(path);
        document.ApplyTags(new AudioFileTags
        {
            Probed = true,
            DurationSeconds = 1,
            SampleRate = 48000,
            Channels = 2,
            BitsPerSample = 24,
            BitRateKbps = 2304,
        });

        var row = LibraryBrowserView.CreateRow(new DocumentSession(document));
        Assert.Equal("WAVE", row.Kind);
        Assert.Equal(24, row.BitDepth);
        Assert.Equal("24bit", row.BitDepthText);
        Assert.Equal(0, row.BitRateKbps);
        Assert.Equal(string.Empty, row.BitRateText);
        Assert.Equal("48kHz", row.SampleRateText);
        Assert.Equal("2ch", row.ChannelsText);
        Assert.Null(row.IntegratedLufs);
        Assert.Equal(string.Empty, row.LoudnessText);
    }

    [Fact]
    public void CreateRow_Wave_ShowsIntegratedLufsWhenTagged()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
        var document = AudioDocument.CreateDeferred(path);
        document.ApplyTags(new AudioFileTags
        {
            Probed = true,
            DurationSeconds = 1,
            SampleRate = 48000,
            Channels = 2,
            BitsPerSample = 24,
            IntegratedLufs = -23.5f,
        });

        var row = LibraryBrowserView.CreateRow(new DocumentSession(document));
        Assert.Equal(-23.5f, row.IntegratedLufs);
        Assert.Equal("-23.5 LKFS", row.LoudnessText);
        Assert.Equal(LoudnessTraffic.Caution, row.LoudnessTone);
    }

    [Fact]
    public void PlaylistLoudnessTone_UsesSettingsTargetBands()
    {
        const double target = -24;
        Assert.Equal(LoudnessTraffic.Idle, LibraryFileRow.ToneFor(null, target));
        Assert.Equal(LoudnessTraffic.Safe, LibraryFileRow.ToneFor(-26f, target));
        Assert.Equal(LoudnessTraffic.Safe, LibraryFileRow.ToneFor(-24f, target));
        Assert.Equal(LoudnessTraffic.Caution, LibraryFileRow.ToneFor(-23.5f, target));
        Assert.Equal(LoudnessTraffic.Caution, LibraryFileRow.ToneFor(-21f, target));
        Assert.Equal(LoudnessTraffic.Danger, LibraryFileRow.ToneFor(-20.9f, target));
    }

    [Fact]
    public void CreateRow_Wave_HydratesIntegratedLufsFromMediaCache()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mga-lufs-row-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        LibraryMediaCache.UseDirectoryForTests(dir);
        try
        {
            var path = Path.Combine(dir, "tone.wav");
            File.WriteAllBytes(path, new byte[64]);
            LibraryMediaCache.PutLoudness(path, -21.2f);

            var document = AudioDocument.CreateDeferred(path);
            document.ApplyTags(new AudioFileTags
            {
                Probed = true,
                DurationSeconds = 1,
                SampleRate = 48000,
                Channels = 2,
                BitsPerSample = 16,
            });
            Assert.Null(document.Tags.IntegratedLufs);

            var row = LibraryBrowserView.CreateRow(new DocumentSession(document));
            Assert.Equal(-21.2f, row.IntegratedLufs);
            Assert.Equal("-21.2 LKFS", row.LoudnessText);
            Assert.Equal(-21.2f, document.Tags.IntegratedLufs);
        }
        finally
        {
            LibraryMediaCache.ResetForTests();
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateRow_Mp3_BlanksLoudnessEvenIfTagged()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp3");
        var document = AudioDocument.CreateDeferred(path);
        document.ApplyTags(new AudioFileTags
        {
            Probed = true,
            DurationSeconds = 1,
            SampleRate = 44100,
            Channels = 2,
            BitRateKbps = 320,
            IntegratedLufs = -18f,
        });

        var row = LibraryBrowserView.CreateRow(new DocumentSession(document));
        Assert.Null(row.IntegratedLufs);
        Assert.Equal(string.Empty, row.LoudnessText);
    }

    [Fact]
    public void CreateRow_M4a_BlanksBitDepthKeepsBitRate()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".m4a");
        var document = AudioDocument.CreateDeferred(path);
        document.ApplyTags(new AudioFileTags
        {
            Probed = true,
            DurationSeconds = 8,
            SampleRate = 48000,
            Channels = 2,
            BitsPerSample = 16,
            BitRateKbps = 256,
        });

        var row = LibraryBrowserView.CreateRow(new DocumentSession(document));
        Assert.Equal("M4A", row.Kind);
        Assert.Equal(0, row.BitDepth);
        Assert.Equal(string.Empty, row.BitDepthText);
        Assert.False(row.BitDepthNonDefault);
        Assert.Equal(256, row.BitRateKbps);
        Assert.Equal(UiStrings.FormatBitRate(256), row.BitRateText);
        Assert.Equal("48kHz", row.SampleRateText);
        Assert.Equal("2ch", row.ChannelsText);
    }

    [Fact]
    public void CreateRow_Aiff_BlanksBitRateKeepsBitDepth()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".aiff");
        var document = AudioDocument.CreateDeferred(path);
        document.ApplyTags(new AudioFileTags
        {
            Probed = true,
            DurationSeconds = 0.5,
            SampleRate = 48000,
            Channels = 1,
            BitsPerSample = 16,
            BitRateKbps = 768,
        });

        var row = LibraryBrowserView.CreateRow(new DocumentSession(document));
        Assert.Equal("AIFF", row.Kind);
        Assert.Equal(16, row.BitDepth);
        Assert.Equal("16bit", row.BitDepthText);
        Assert.Equal(0, row.BitRateKbps);
        Assert.Equal(string.Empty, row.BitRateText);
        Assert.Equal("48kHz", row.SampleRateText);
        Assert.Equal("1ch", row.ChannelsText);
        Assert.Null(row.IntegratedLufs);
        Assert.Equal(string.Empty, row.LoudnessText);
    }

    [Fact]
    public void CreateRow_Aiff_ShowsIntegratedLufsWhenTagged()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".aif");
        var document = AudioDocument.CreateDeferred(path);
        Assert.Equal(AudioFileKind.Aiff, document.SourceKind);
        document.ApplyTags(new AudioFileTags
        {
            Probed = true,
            DurationSeconds = 1,
            SampleRate = 48000,
            Channels = 2,
            BitsPerSample = 24,
            IntegratedLufs = -20.1f,
        });

        var row = LibraryBrowserView.CreateRow(new DocumentSession(document));
        Assert.Equal("AIFF", row.Kind);
        Assert.Equal(-20.1f, row.IntegratedLufs);
        Assert.Equal("-20.1 LKFS", row.LoudnessText);
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

    [Fact]
    public void CreateRow_Pdf_HidesDummyAudioFormatAfterActivate()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf");
        File.WriteAllBytes(path, "%PDF-1.4"u8.ToArray());
        try
        {
            var document = AudioDocument.CreateDeferred(path);
            document.ApplyTags(new AudioFileTags { Probed = true });
            document.ActivateStreamPlayback(48000, 2, 16, 240000);
            document.ApplyTags(document.Tags.WithDurationSeconds(25));
            var row = LibraryBrowserView.CreateRow(new DocumentSession(document));
            Assert.Equal("PDF", row.Kind);
            Assert.Equal(0, row.DurationSeconds);
            Assert.Equal(string.Empty, row.DurationText);
            Assert.Equal(string.Empty, row.SampleRateText);
            Assert.Equal(string.Empty, row.BitDepthText);
            Assert.Equal(string.Empty, row.ChannelsText);
            Assert.Equal(string.Empty, row.BitRateText);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CreateRow_Video_KeepsTagFormatWhenStreamProbeIsGarbage()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mov");
        var document = AudioDocument.CreateDeferred(path);
        document.ApplyTags(new AudioFileTags
        {
            Probed = true,
            DurationSeconds = 26,
            SampleRate = 48000,
            Channels = 2,
            BitsPerSample = 16,
            AudioCodec = "mp4a",
        });
        // ホバー時の MediaFoundation が 0→旧 1Hz / 3ch を返す想定。
        document.ActivateStreamPlayback(1, 3, 16, 26);

        var row = LibraryBrowserView.CreateRow(new DocumentSession(document));
        Assert.Equal("MOV", row.Kind);
        Assert.Equal(26, row.DurationSeconds, 3);
        Assert.Equal(48000, row.SampleRate);
        Assert.Equal("48kHz", row.SampleRateText);
        Assert.Equal(0, row.BitDepth);
        Assert.Equal(string.Empty, row.BitDepthText);
        Assert.Equal(2, row.Channels);
        Assert.Equal("2ch", row.ChannelsText);
    }

    [Fact]
    public void CreateRow_Video_ShowsBitDepthOnlyForUncompressedPcmAudio()
    {
        var pcmPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mov");
        var pcm = AudioDocument.CreateDeferred(pcmPath);
        pcm.ApplyTags(new AudioFileTags
        {
            Probed = true,
            DurationSeconds = 10,
            SampleRate = 48000,
            Channels = 2,
            BitsPerSample = 24,
            AudioCodec = "ipcm",
        });
        var pcmRow = LibraryBrowserView.CreateRow(new DocumentSession(pcm));
        Assert.Equal(24, pcmRow.BitDepth);
        Assert.Equal("24bit", pcmRow.BitDepthText);

        var aacPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp4");
        var aac = AudioDocument.CreateDeferred(aacPath);
        aac.ApplyTags(new AudioFileTags
        {
            Probed = true,
            DurationSeconds = 10,
            SampleRate = 48000,
            Channels = 2,
            BitsPerSample = 16,
            AudioCodec = "mp4a",
        });
        var aacRow = LibraryBrowserView.CreateRow(new DocumentSession(aac));
        Assert.Equal(0, aacRow.BitDepth);
        Assert.Equal(string.Empty, aacRow.BitDepthText);

        var unknown = AudioDocument.CreateDeferred(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mov"));
        unknown.ApplyTags(new AudioFileTags
        {
            Probed = true,
            DurationSeconds = 10,
            SampleRate = 48000,
            Channels = 2,
            BitsPerSample = 16,
        });
        var unknownRow = LibraryBrowserView.CreateRow(new DocumentSession(unknown));
        Assert.Equal(0, unknownRow.BitDepth);
        Assert.Equal(string.Empty, unknownRow.BitDepthText);
    }

    [Fact]
    public void IsPlausibleAudioSampleRate_RejectsOneHertzDisplayBug()
    {
        Assert.False(AudioCodec.IsPlausibleAudioSampleRate(0));
        Assert.False(AudioCodec.IsPlausibleAudioSampleRate(1));
        Assert.False(AudioCodec.IsPlausibleAudioSampleRate(7_999));
        Assert.True(AudioCodec.IsPlausibleAudioSampleRate(8_000));
        Assert.True(AudioCodec.IsPlausibleAudioSampleRate(48_000));
        Assert.True(AudioCodec.IsPlausibleAudioSampleRate(384_000));
        Assert.False(AudioCodec.IsPlausibleAudioSampleRate(384_001));
        Assert.Equal("0.001kHz", UiStrings.FormatSampleRate(1));
    }
}
