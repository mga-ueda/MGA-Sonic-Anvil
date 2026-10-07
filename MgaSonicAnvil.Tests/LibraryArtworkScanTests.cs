using System.IO;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibraryArtworkScanTests
{
    [Fact]
    public void Video_AlwaysLoadsEvenWhenGrouped()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Assert.True(LibraryArtworkScan.ShouldLoad(grouped: true, AudioFileKind.Mov, isSelected: false, "folder", seen));
        Assert.True(LibraryArtworkScan.ShouldLoad(grouped: true, AudioFileKind.Mp4, isSelected: false, "folder", seen));
        Assert.True(LibraryArtworkScan.ShouldLoad(grouped: false, AudioFileKind.Mov, isSelected: false, "folder", seen));
        Assert.True(LibraryArtworkScan.CanRead(AudioFileKind.Mov));
        Assert.True(LibraryArtworkScan.CanRead(AudioFileKind.Mp4));
        Assert.True(LibraryArtworkScan.CanRead(AudioFileKind.Pdf));
        Assert.True(LibraryArtworkScan.IsPdfKind(AudioFileKind.Pdf));
        Assert.True(LibraryArtworkScan.ShouldLoad(grouped: true, AudioFileKind.Pdf, isSelected: false, "folder", seen));
        Assert.True(LibraryArtworkScan.ShouldLoad(grouped: false, AudioFileKind.Pdf, isSelected: false, "folder", seen));
    }

    [Fact]
    public void Mp3_LoadsOnePerGroupUnlessSelected()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Assert.False(LibraryArtworkScan.ShouldLoad(grouped: false, AudioFileKind.Mp3, isSelected: false, "a", seen));
        Assert.True(LibraryArtworkScan.ShouldLoad(grouped: true, AudioFileKind.Mp3, isSelected: false, "a", seen));
        Assert.False(LibraryArtworkScan.ShouldLoad(grouped: true, AudioFileKind.Mp3, isSelected: false, "a", seen));
        Assert.True(LibraryArtworkScan.ShouldLoad(grouped: true, AudioFileKind.Mp3, isSelected: true, "a", seen));
        Assert.True(LibraryArtworkScan.ShouldLoad(grouped: true, AudioFileKind.Mp3, isSelected: false, "b", seen));
    }

    [Fact]
    public async Task PdfPage_MissingFile_IsEmpty()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.pdf");
        Assert.Null(await LibraryPdfPages.RenderPngAsync(missing, 0, 256));
    }

    [Fact]
    public void VideoArtwork_MissingFile_IsEmpty()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"missing-video-{Guid.NewGuid():N}.mov");
        Assert.False(VideoArtwork.TryRead(missing, out var art));
        Assert.Empty(art);
        Assert.False(VideoProxy.TryExtractStillJpeg(missing, (int)VideoArtwork.Edge, out var jpeg));
        Assert.Empty(jpeg);
    }

    [Fact]
    public void VideoArtwork_ShellIcon_IsNotUsable()
    {
        Assert.False(VideoArtwork.IsUsableShellThumbnail(Windows.Storage.FileProperties.ThumbnailType.Icon, 4096));
        Assert.False(VideoArtwork.IsUsableShellThumbnail(Windows.Storage.FileProperties.ThumbnailType.Image, 0));
        Assert.True(VideoArtwork.IsUsableShellThumbnail(Windows.Storage.FileProperties.ThumbnailType.Image, 4096));
    }

    [Fact]
    public void UsesRowJackets_PdfAndVideoTogether()
    {
        var pdf = new LibraryFileRow { Name = "doc.pdf", Kind = "PDF" };
        var mov = new LibraryFileRow { Name = "clip.mov", Kind = "MOV" };
        var mp3 = new LibraryFileRow { Name = "song.mp3", Kind = "MP3" };
        Assert.True(LibraryArtworkScan.UsesRowJackets(new[] { pdf, mov }));
        Assert.True(LibraryArtworkScan.UsesRowJackets(new[] { pdf }));
        Assert.False(LibraryArtworkScan.UsesRowJackets(new[] { pdf, mp3 }));
        Assert.False(LibraryArtworkScan.UsesRowJackets(new[] { mov, mp3 }));
    }
}
