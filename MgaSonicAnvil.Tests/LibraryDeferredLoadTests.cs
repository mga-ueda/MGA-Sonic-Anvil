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
