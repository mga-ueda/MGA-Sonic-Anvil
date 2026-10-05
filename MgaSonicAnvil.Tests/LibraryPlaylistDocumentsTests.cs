using System.IO;
using System.Text.Json;
using MgaSonicAnvil;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibraryPlaylistDocumentsTests
{
    [Fact]
    public void Settings_DefaultOnAndRoundTrips()
    {
        Assert.True(new AppSettings().LibraryShowPlaylistPdf);
        Assert.True(new AppSettings().LibraryShowPlaylistMov);
        Assert.True(new AppSettings().LibraryShowPlaylistMp4);
        Assert.True(AppSettings.CreateDefault().LibraryShowPlaylistPdf);
        var json = JsonSerializer.Serialize(
            new AppSettings { LibraryShowPlaylistPdf = false, LibraryShowPlaylistMov = true, LibraryShowPlaylistMp4 = true },
            AppSettingsJsonContext.Default.AppSettings);
        var back = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
        Assert.False(back!.LibraryShowPlaylistPdf);
        Assert.True(back.LibraryShowPlaylistMov);
        var missing = JsonSerializer.Deserialize(
            """{"SettingsGeneration":1}""",
            AppSettingsJsonContext.Default.AppSettings);
        Assert.True(missing!.LibraryShowPlaylistPdf);
        Assert.True(missing.LibraryShowPlaylistMov);
        Assert.True(missing.LibraryShowPlaylistMp4);
    }

    [Fact]
    public void DetectKind_MapsDocumentExtensions()
    {
        Assert.Equal(AudioFileKind.Pdf, AudioCodec.DetectKind("notes.pdf"));
        Assert.Equal(AudioFileKind.Mp4, AudioCodec.DetectKind("clip.mp4"));
        Assert.Equal(AudioFileKind.Mov, AudioCodec.DetectKind("clip.MOV"));
        Assert.Equal("PDF", LibraryColumnFilter.KindFromFileName("a.pdf"));
        Assert.Equal("MP4", LibraryColumnFilter.KindFromFileName("a.mp4"));
        Assert.Equal("MOV", LibraryColumnFilter.KindFromFileName("a.mov"));
    }

    [Fact]
    public void PlayerCollect_HonorsEnabledFlag()
    {
        var previousPdf = LibraryPlaylistDocuments.ShowPdf;
        var previousMov = LibraryPlaylistDocuments.ShowMov;
        var previousMp4 = LibraryPlaylistDocuments.ShowMp4;
        var root = Path.Combine(Path.GetTempPath(), "mga-docs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pdf = Path.Combine(root, "doc.pdf");
        var mp4 = Path.Combine(root, "clip.mp4");
        var wav = Path.Combine(root, "tone.wav");
        File.WriteAllText(pdf, "pdf");
        File.WriteAllText(mp4, "mp4");
        File.WriteAllText(wav, "wav");
        try
        {
            LibraryPlaylistDocuments.ApplyEnabled(true);
            var on = AudioCodec.CollectPlayerOpenable([root]);
            Assert.Contains(pdf, on);
            Assert.Contains(mp4, on);
            Assert.Contains(wav, on);
            Assert.True(AudioCodec.IsPlayerOpenable(pdf));
            Assert.False(AudioCodec.IsOpenable(pdf));
            Assert.True(AudioCodec.CanStreamPlay(mp4));

            LibraryPlaylistDocuments.ApplyEnabled(false);
            var off = AudioCodec.CollectPlayerOpenable([root]);
            Assert.DoesNotContain(pdf, off);
            Assert.DoesNotContain(mp4, off);
            Assert.Contains(wav, off);
            Assert.False(AudioCodec.IsPlayerOpenable(pdf));

            LibraryPlaylistDocuments.Apply(pdf: true, mov: false, mp4: false);
            Assert.True(AudioCodec.IsPlayerOpenable(pdf));
            Assert.False(AudioCodec.IsPlayerOpenable(mp4));
        }
        finally
        {
            LibraryPlaylistDocuments.Apply(previousPdf, previousMov, previousMp4);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FileAssociations_DoNotIncludeDocuments()
    {
        Assert.Equal(AudioCodec.PlayerOpenExtensions, FileAssociations.Extensions);
        Assert.DoesNotContain(".pdf", FileAssociations.Extensions);
        Assert.DoesNotContain(".mp4", FileAssociations.Extensions);
        Assert.DoesNotContain(".mov", FileAssociations.Extensions);
        Assert.False(FileAssociations.IsPlayerOnlyExtension(".pdf"));
    }

    [Fact]
    public void EditorHandoff_BlocksVisualDocuments()
    {
        var wav = new DocumentSession(AudioDocument.CreateDeferred("a.wav"));
        var pdf = new DocumentSession(AudioDocument.CreateDeferred("b.pdf"));
        var mp4 = new DocumentSession(AudioDocument.CreateDeferred("c.mp4"));
        Assert.True(LibraryPlayerMode.NeedsEditorPcmUpgrade(wav.Document));
        Assert.False(LibraryPlayerMode.NeedsEditorPcmUpgrade(pdf.Document));
        Assert.False(LibraryPlayerMode.NeedsEditorPcmUpgrade(mp4.Document));
        Assert.True(LibraryPlaylistDocuments.IsPdf(pdf.Document));
        Assert.True(LibraryPlaylistDocuments.IsVideo(mp4.Document));
        var keep = LibraryPlayerMode.ExcludeEditorBlocked(
            LibraryPlayerMode.SessionsToKeep([wav, pdf, mp4], pdf));
        Assert.Equal(new[] { wav }, keep);
    }

    [Fact]
    public void LaunchContainsMp3_IncludesDocumentsWhenEnabled()
    {
        var previousPdf = LibraryPlaylistDocuments.ShowPdf;
        var previousMov = LibraryPlaylistDocuments.ShowMov;
        var previousMp4 = LibraryPlaylistDocuments.ShowMp4;
        try
        {
            LibraryPlaylistDocuments.ApplyEnabled(true);
            Assert.True(LaunchFiles.ContainsMp3([Path.GetFullPath("clip.mp4")]));
            Assert.True(LaunchFiles.ContainsMp3([Path.GetFullPath("doc.pdf")]));
            LibraryPlaylistDocuments.ApplyEnabled(false);
            Assert.False(LaunchFiles.ContainsMp3([Path.GetFullPath("clip.mp4")]));
        }
        finally
        {
            LibraryPlaylistDocuments.Apply(previousPdf, previousMov, previousMp4);
        }
    }
}
