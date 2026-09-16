using System.IO;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class DocumentFileNamesTests
{
    [Fact]
    public void NameForEdit_PrefersFileName()
    {
        Assert.Equal("tone.wav", DocumentFileNames.NameForEdit(@"D:\src\tone.wav", "tone.wav"));
        Assert.Equal("untitled", DocumentFileNames.NameForEdit(null, "untitled"));
    }

    [Fact]
    public void StemSelectLength_StopsBeforeExtension()
    {
        Assert.Equal(4, DocumentFileNames.StemSelectLength("tone.wav"));
        Assert.Equal(4, DocumentFileNames.StemSelectLength("tone"));
        Assert.Equal(4, DocumentFileNames.StemSelectLength("tone.aiff"));
        Assert.Equal(9, DocumentFileNames.StemSelectLength("file.name.wav"));
    }

    [Fact]
    public void TryBuildRenamePath_KeepsFolderAndFillsExtension()
    {
        var source = Path.Combine(Path.GetTempPath(), "tone.wav");
        Assert.True(DocumentFileNames.TryBuildRenamePath(
            source,
            "copy",
            fallbackDirectory: null,
            fallbackExtension: ".wav",
            out var dest,
            out var error));
        Assert.Equal(DocumentFileNameError.None, error);
        Assert.Equal("copy.wav", Path.GetFileName(dest));
        Assert.Equal(Path.GetDirectoryName(source), Path.GetDirectoryName(dest));
    }

    [Fact]
    public void TryBuildRenamePath_RejectsEmptyAndInvalid()
    {
        var source = Path.Combine(Path.GetTempPath(), "tone.wav");
        Assert.False(DocumentFileNames.TryBuildRenamePath(
            source, "   ", null, ".wav", out _, out var empty));
        Assert.Equal(DocumentFileNameError.Empty, empty);
        Assert.False(DocumentFileNames.TryBuildRenamePath(
            source, "a:b", null, ".wav", out _, out var invalid));
        Assert.Equal(DocumentFileNameError.Invalid, invalid);
    }

    [Fact]
    public void TryBuildRenamePath_UntitledUsesFallbackDirectory()
    {
        var folder = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Assert.True(DocumentFileNames.TryBuildRenamePath(
            null,
            "new file",
            folder,
            ".wav",
            out var dest,
            out var error));
        Assert.Equal(DocumentFileNameError.None, error);
        Assert.Equal("new file.wav", Path.GetFileName(dest));
        Assert.True(DocumentFileNames.IsSamePath(folder, Path.GetDirectoryName(dest)));
    }

    [Fact]
    public void IsCaseOnlyChange_DetectsRenamesThatOnlyChangeCase()
    {
        var dir = Path.GetTempPath();
        var a = Path.Combine(dir, "Tone.wav");
        var b = Path.Combine(dir, "tone.wav");
        Assert.True(DocumentFileNames.IsCaseOnlyChange(a, b));
        Assert.False(DocumentFileNames.IsCaseOnlyChange(a, Path.Combine(dir, "other.wav")));
    }
}

public sealed class AudioDocumentCopyTests
{
    [Fact]
    public void CopyWorking_DuplicatesSamplesMarkersAndRegions()
    {
        var source = new AudioDocument(new float[] { 0.1f, -0.2f, 0.3f, -0.4f }, 48000, 2, 16, AudioFileKind.Wave, @"D:\src\tone.wav");
        source.ReplaceMarkers([new MarkerSnapshot(0, "A")], markDirty: false);
        source.SetRegions([new WaveRegion(new WaveSelection(0, 1), "intro")], markDirty: false);
        source.SetSampleLoop(new WaveSelection(0, 2), markDirty: false);
        source.SetDirty(true);

        var copy = source.CopyWorking();
        Assert.Null(copy.SourcePath);
        Assert.True(copy.IsDirty);
        Assert.Equal(source.Interleaved, copy.Interleaved);
        Assert.Single(copy.Markers);
        Assert.Equal("A", copy.Markers[0].Comment);
        Assert.Equal("intro", copy.SnapshotRegions()[0].Name);
        Assert.Equal(2, copy.SampleLoop.EndFrame);
        Assert.NotSame(source.Interleaved, copy.Interleaved);

        copy.Interleaved[0] = 9;
        Assert.Equal(0.1f, source.Interleaved[0]);
    }
}
