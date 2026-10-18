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
    public void Settings_VideoDefaultsOffAndRoundTrips()
    {
        Assert.True(new AppSettings().LibraryShowPlaylistPdf);
        Assert.False(new AppSettings().LibraryShowPlaylistMov);
        Assert.False(new AppSettings().LibraryShowPlaylistMp4);
        Assert.False(new AppSettings().LibraryShowPlaylistAvi);
        Assert.False(new AppSettings().LibraryShowPlaylistMkv);
        Assert.False(new AppSettings().LibraryShowPlaylistWebm);
        Assert.False(new AppSettings().LibraryShowPlaylistMpg);
        Assert.False(new AppSettings().VideoLaunchWindowHasPosition);
        Assert.True(AppSettings.CreateDefault().LibraryShowPlaylistPdf);
        var json = JsonSerializer.Serialize(
            new AppSettings
            {
                LibraryShowPlaylistPdf = false,
                LibraryShowPlaylistMov = true,
                LibraryShowPlaylistMp4 = true,
                LibraryShowPlaylistAvi = false,
                LibraryShowPlaylistMkv = true,
                LibraryShowPlaylistWebm = false,
                LibraryShowPlaylistMpg = true,
            },
            AppSettingsJsonContext.Default.AppSettings);
        var back = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
        Assert.False(back!.LibraryShowPlaylistPdf);
        Assert.True(back.LibraryShowPlaylistMov);
        Assert.False(back.LibraryShowPlaylistAvi);
        Assert.True(back.LibraryShowPlaylistMkv);
        Assert.False(back.LibraryShowPlaylistWebm);
        Assert.True(back.LibraryShowPlaylistMpg);
        var missing = JsonSerializer.Deserialize(
            """{"SettingsGeneration":1}""",
            AppSettingsJsonContext.Default.AppSettings);
        Assert.True(missing!.LibraryShowPlaylistPdf);
        Assert.False(missing.LibraryShowPlaylistMov);
        Assert.False(missing.LibraryShowPlaylistMp4);
        Assert.False(missing.LibraryShowPlaylistAvi);
        Assert.False(missing.LibraryShowPlaylistMkv);
        Assert.False(missing.LibraryShowPlaylistWebm);
        Assert.False(missing.LibraryShowPlaylistMpg);
    }

    [Fact]
    public void ApplyFromSettings_GatesProxyVideosOnFfmpeg()
    {
        var previousPdf = LibraryPlaylistDocuments.ShowPdf;
        var previousMov = LibraryPlaylistDocuments.ShowMov;
        var previousMp4 = LibraryPlaylistDocuments.ShowMp4;
        var previousAvi = LibraryPlaylistDocuments.ShowAvi;
        var previousMkv = LibraryPlaylistDocuments.ShowMkv;
        var previousWebm = LibraryPlaylistDocuments.ShowWebm;
        var previousMpg = LibraryPlaylistDocuments.ShowMpg;
        try
        {
            LibraryPlaylistDocuments.ApplyFromSettings(new AppSettings
            {
                LibraryShowPlaylistPdf = true,
                LibraryShowPlaylistMov = true,
                LibraryShowPlaylistMp4 = true,
                LibraryShowPlaylistAvi = true,
                LibraryShowPlaylistMkv = true,
                LibraryShowPlaylistWebm = true,
                LibraryShowPlaylistMpg = true,
                FfmpegExePath = string.Empty,
            });
            Assert.True(LibraryPlaylistDocuments.ShowPdf);
            Assert.True(LibraryPlaylistDocuments.ShowMov);
            Assert.True(LibraryPlaylistDocuments.ShowMp4);
            Assert.False(LibraryPlaylistDocuments.ShowAvi);
            Assert.False(LibraryPlaylistDocuments.ShowMkv);
            Assert.False(LibraryPlaylistDocuments.ShowWebm);
            Assert.False(LibraryPlaylistDocuments.ShowMpg);
        }
        finally
        {
            LibraryPlaylistDocuments.Apply(
                previousPdf,
                previousMov,
                previousMp4,
                previousAvi,
                previousMkv,
                previousWebm,
                previousMpg);
        }
    }

    [Fact]
    public void DetectKind_MapsDocumentExtensions()
    {
        Assert.Equal(AudioFileKind.Pdf, AudioCodec.DetectKind("notes.pdf"));
        Assert.Equal(AudioFileKind.Mp4, AudioCodec.DetectKind("clip.mp4"));
        Assert.Equal(AudioFileKind.Mov, AudioCodec.DetectKind("clip.MOV"));
        Assert.Equal(AudioFileKind.Avi, AudioCodec.DetectKind("clip.avi"));
        Assert.Equal(AudioFileKind.Mkv, AudioCodec.DetectKind("clip.mkv"));
        Assert.Equal(AudioFileKind.Webm, AudioCodec.DetectKind("clip.WEBM"));
        Assert.Equal(AudioFileKind.Mpg, AudioCodec.DetectKind("clip.mpg"));
        Assert.Equal(AudioFileKind.Mpg, AudioCodec.DetectKind("clip.MPEG"));
        Assert.Equal("PDF", LibraryColumnFilter.KindFromFileName("a.pdf"));
        Assert.Equal("MP4", LibraryColumnFilter.KindFromFileName("a.mp4"));
        Assert.Equal("MOV", LibraryColumnFilter.KindFromFileName("a.mov"));
        Assert.Equal("AVI", LibraryColumnFilter.KindFromFileName("a.avi"));
        Assert.Equal("MKV", LibraryColumnFilter.KindFromFileName("a.mkv"));
        Assert.Equal("WEBM", LibraryColumnFilter.KindFromFileName("a.webm"));
        Assert.Equal("MPG", LibraryColumnFilter.KindFromFileName("a.mpg"));
        Assert.Equal("MPG", LibraryColumnFilter.KindFromFileName("a.mpeg"));
        Assert.True(LibraryColumnFilter.IsVideoFamilyKind("AVI"));
        Assert.True(LibraryColumnFilter.IsVideoFamilyKind("MKV"));
        Assert.True(LibraryColumnFilter.IsVideoFamilyKind("WEBM"));
        Assert.True(LibraryColumnFilter.IsVideoFamilyKind("MPG"));
        Assert.Equal("AVI", LibraryBrowserView.FormatKind(AudioFileKind.Avi));
        Assert.Equal("MKV", LibraryBrowserView.FormatKind(AudioFileKind.Mkv));
        Assert.Equal("WEBM", LibraryBrowserView.FormatKind(AudioFileKind.Webm));
        Assert.Equal("MPG", LibraryBrowserView.FormatKind(AudioFileKind.Mpg));
        Assert.Contains("*.avi", UiStrings.FilterOpenPlayer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("*.mkv", UiStrings.FilterOpenPlayer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("*.webm", UiStrings.FilterOpenPlayer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("*.mpg", UiStrings.FilterOpenPlayer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("*.mpeg", UiStrings.FilterOpenPlayer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlayerCollect_HonorsEnabledFlag_ButLaunchIgnoresVideoToggle()
    {
        var previousPdf = LibraryPlaylistDocuments.ShowPdf;
        var previousMov = LibraryPlaylistDocuments.ShowMov;
        var previousMp4 = LibraryPlaylistDocuments.ShowMp4;
        var previousAvi = LibraryPlaylistDocuments.ShowAvi;
        var previousMkv = LibraryPlaylistDocuments.ShowMkv;
        var previousWebm = LibraryPlaylistDocuments.ShowWebm;
        var previousMpg = LibraryPlaylistDocuments.ShowMpg;
        var root = Path.Combine(Path.GetTempPath(), "mga-docs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pdf = Path.Combine(root, "doc.pdf");
        var mp4 = Path.Combine(root, "clip.mp4");
        var avi = Path.Combine(root, "clip.avi");
        var mkv = Path.Combine(root, "clip.mkv");
        var webm = Path.Combine(root, "clip.webm");
        var mpg = Path.Combine(root, "clip.mpg");
        var wav = Path.Combine(root, "tone.wav");
        File.WriteAllText(pdf, "pdf");
        File.WriteAllText(mp4, "mp4");
        File.WriteAllText(avi, "avi");
        File.WriteAllText(mkv, "mkv");
        File.WriteAllText(webm, "webm");
        File.WriteAllText(mpg, "mpg");
        File.WriteAllText(wav, "wav");
        try
        {
            LibraryPlaylistDocuments.ApplyEnabled(false);
            var off = AudioCodec.CollectPlayerOpenable([root]);
            Assert.DoesNotContain(pdf, off);
            Assert.DoesNotContain(mp4, off);
            Assert.DoesNotContain(mpg, off);
            Assert.Contains(wav, off);

            var launch = AudioCodec.CollectLaunchOpenable([root]);
            Assert.Contains(pdf, launch);
            Assert.Contains(mp4, launch);
            Assert.Contains(avi, launch);
            Assert.Contains(mkv, launch);
            Assert.Contains(webm, launch);
            Assert.Contains(mpg, launch);
            Assert.Contains(wav, launch);
            Assert.True(AudioCodec.IsLaunchOpenable(mpg));
            Assert.False(AudioCodec.IsPlayerOpenable(mpg));
            Assert.True(LaunchFiles.ContainsVideo([mpg]));
            Assert.True(LaunchFiles.ContainsMp3([Path.GetFullPath("clip.mpg")]));

            LibraryPlaylistDocuments.ApplyEnabled(true);
            var on = AudioCodec.CollectPlayerOpenable([root]);
            Assert.Contains(mpg, on);
            Assert.True(AudioCodec.CanStreamPlay(mpg));
        }
        finally
        {
            LibraryPlaylistDocuments.Apply(
                previousPdf,
                previousMov,
                previousMp4,
                previousAvi,
                previousMkv,
                previousWebm,
                previousMpg);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FileAssociations_IncludeVideosAsPlayerOnly()
    {
        Assert.Contains(".m4a", FileAssociations.Extensions);
        Assert.Contains(".mp4", FileAssociations.Extensions);
        Assert.Contains(".mov", FileAssociations.Extensions);
        Assert.Contains(".avi", FileAssociations.Extensions);
        Assert.Contains(".mkv", FileAssociations.Extensions);
        Assert.Contains(".webm", FileAssociations.Extensions);
        Assert.Contains(".mpg", FileAssociations.Extensions);
        Assert.Contains(".mpeg", FileAssociations.Extensions);
        Assert.DoesNotContain(".pdf", FileAssociations.Extensions);
        Assert.True(FileAssociations.IsPlayerOnlyExtension(".m4a"));
        Assert.True(FileAssociations.IsPlayerOnlyExtension(".mp4"));
        Assert.True(FileAssociations.IsPlayerOnlyExtension(".mpg"));
        Assert.True(FileAssociations.IsPlayerOnlyExtension(".mpeg"));
        Assert.False(FileAssociations.IsPlayerOnlyExtension(".mp3"));
        Assert.Contains("MPEG", FileAssociations.FormatLabel(".mpg"), StringComparison.Ordinal);
        Assert.Contains(
            UiStrings.LabelFileAssociationPlayerOnly,
            FileAssociations.FormatLabel(".mpg"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void EditorHandoff_BlocksVisualDocuments()
    {
        var wav = new DocumentSession(AudioDocument.CreateDeferred("a.wav"));
        var pdf = new DocumentSession(AudioDocument.CreateDeferred("b.pdf"));
        var mp4 = new DocumentSession(AudioDocument.CreateDeferred("c.mp4"));
        var mpg = new DocumentSession(AudioDocument.CreateDeferred("d.mpg"));
        Assert.True(LibraryPlayerMode.NeedsEditorPcmUpgrade(wav.Document));
        Assert.False(LibraryPlayerMode.NeedsEditorPcmUpgrade(pdf.Document));
        Assert.False(LibraryPlayerMode.NeedsEditorPcmUpgrade(mp4.Document));
        Assert.False(LibraryPlayerMode.NeedsEditorPcmUpgrade(mpg.Document));
        Assert.True(LibraryPlaylistDocuments.IsPdf(pdf.Document));
        Assert.True(LibraryPlaylistDocuments.IsVideo(mp4.Document));
        Assert.True(LibraryPlaylistDocuments.IsVideo(mpg.Document));
        var keep = LibraryPlayerMode.ExcludeEditorBlocked(
            LibraryPlayerMode.SessionsToKeep([wav, pdf, mp4, mpg], pdf));
        Assert.Equal(new[] { wav }, keep);
        // 動画だけ選んでいたときは、旧アクティブへフォールバックせず空にする。
        Assert.Null(LibraryPlayerMode.ResolveLibraryHandoffCurrent([], preferred: mp4));
        Assert.Same(wav, LibraryPlayerMode.ResolveLibraryHandoffCurrent([wav], preferred: mp4));
        Assert.Same(wav, LibraryPlayerMode.ResolveLibraryHandoffCurrent([wav], preferred: wav));
    }

    [Fact]
    public void LaunchContainsMp3_IncludesDocumentsEvenWhenToggleOff()
    {
        var previousPdf = LibraryPlaylistDocuments.ShowPdf;
        var previousMov = LibraryPlaylistDocuments.ShowMov;
        var previousMp4 = LibraryPlaylistDocuments.ShowMp4;
        var previousAvi = LibraryPlaylistDocuments.ShowAvi;
        var previousMkv = LibraryPlaylistDocuments.ShowMkv;
        var previousWebm = LibraryPlaylistDocuments.ShowWebm;
        var previousMpg = LibraryPlaylistDocuments.ShowMpg;
        try
        {
            LibraryPlaylistDocuments.ApplyEnabled(false);
            Assert.True(LaunchFiles.ContainsMp3([Path.GetFullPath("clip.mp4")]));
            Assert.True(LaunchFiles.ContainsMp3([Path.GetFullPath("clip.mpg")]));
            Assert.True(LaunchFiles.ContainsMp3([Path.GetFullPath("doc.pdf")]));
            Assert.True(LaunchFiles.ContainsVideo([Path.GetFullPath("clip.webm")]));
        }
        finally
        {
            LibraryPlaylistDocuments.Apply(
                previousPdf,
                previousMov,
                previousMp4,
                previousAvi,
                previousMkv,
                previousWebm,
                previousMpg);
        }
    }
}
