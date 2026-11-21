using System.IO;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibraryFileTransferTests
{
    [Fact]
    public void IsSameOrUnder_MatchesFolderAndChildOnly()
    {
        Assert.True(LibraryFileTransfer.IsSameOrUnder(@"C:\Music\A", @"C:\Music\A"));
        Assert.True(LibraryFileTransfer.IsSameOrUnder(@"C:\Music\A\song.wav", @"C:\Music\A"));
        Assert.False(LibraryFileTransfer.IsSameOrUnder(@"C:\Music\A2\song.wav", @"C:\Music\A"));
        Assert.False(LibraryFileTransfer.IsSameOrUnder(@"C:\Other\song.wav", @"C:\Music\A"));
    }

    [Fact]
    public void TryMapPath_RewritesPrefixAndLeavesSiblings()
    {
        Assert.True(LibraryFileTransfer.TryMapPath(
            @"C:\Music\A\song.wav",
            @"C:\Music\A",
            @"C:\Music\A 2",
            out var mapped));
        Assert.Equal(@"C:\Music\A 2\song.wav", mapped);
        Assert.False(LibraryFileTransfer.TryMapPath(
            @"C:\Music\A2\song.wav",
            @"C:\Music\A",
            @"C:\Music\A 2",
            out _));
    }

    [Fact]
    public void UniquePath_UsesCopySuffix()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mga-transfer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var first = Path.Combine(dir, "Album");
            Directory.CreateDirectory(first);
            var next = LibraryFileTransfer.UniquePath(dir, "Album", directoryName: true);
            Assert.Equal(Path.Combine(dir, "Album_copy"), next);
            Directory.CreateDirectory(next);
            var third = LibraryFileTransfer.UniquePath(dir, "Album", directoryName: true);
            Assert.Equal(Path.Combine(dir, "Album_copy 2"), third);

            var file = Path.Combine(dir, "song.wav");
            File.WriteAllBytes(file, [0]);
            Assert.Equal(
                Path.Combine(dir, "song_copy.wav"),
                LibraryFileTransfer.UniquePath(dir, "song.wav", directoryName: false));

            File.WriteAllBytes(Path.Combine(dir, "song_copy.wav"), [0]);
            Assert.Equal(
                Path.Combine(dir, "song_copy 2.wav"),
                LibraryFileTransfer.UniquePath(dir, "song_copy.wav", directoryName: false));
            File.WriteAllBytes(Path.Combine(dir, "song_copy 2.wav"), [0]);
            Assert.Equal(
                Path.Combine(dir, "song_copy 3.wav"),
                LibraryFileTransfer.UniquePath(dir, "song_copy 2.wav", directoryName: false));
            Assert.Equal(
                Path.Combine(dir, "song_copy 3.wav"),
                LibraryFileTransfer.UniquePath(dir, "song_copy_copy.wav", directoryName: false));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("song", "song")]
    [InlineData("song_copy", "song")]
    [InlineData("song_copy 2", "song")]
    [InlineData("song_copy_copy", "song")]
    [InlineData("my_copy_notes", "my_copy_notes")]
    public void StripTrailingCopySuffix_OnlyTrailing(string input, string expected)
    {
        Assert.Equal(expected, LibraryFileTransfer.StripTrailingCopySuffix(input));
        Assert.Equal(expected + "_copy", LibraryFileTransfer.CopyStem(input));
    }

    [Fact]
    public void Classify_MapsWin32Codes()
    {
        Assert.Equal(LibraryIoFailureKind.NotFound, Classify(2));
        Assert.Equal(LibraryIoFailureKind.AccessDenied, Classify(5));
        Assert.Equal(LibraryIoFailureKind.DiskFull, Classify(112));
        Assert.Equal(LibraryIoFailureKind.Other, Classify(32));
        Assert.Equal(LibraryIoFailureKind.NotFound, LibraryFileTransfer.Classify(new FileNotFoundException()));
        Assert.Equal(LibraryIoFailureKind.AccessDenied, LibraryFileTransfer.Classify(new UnauthorizedAccessException()));
    }

    private static LibraryIoFailureKind Classify(int code) =>
        LibraryFileTransfer.Classify(new IOException("os", unchecked((int)0x80070000) | code));

    [Fact]
    public void Describe_NotFound_StartsWithExplicitLine()
    {
        var text = LibraryFileTransfer.Describe(new FileNotFoundException(null, @"C:\gone.wav"));
        Assert.StartsWith(UiStrings.ErrorLibraryNotFound, text);
    }
}

public sealed class LibraryCatalogRetargetTests : IDisposable
{
    private readonly string _dir;

    public LibraryCatalogRetargetTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mga-catalog-retarget-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        LibraryCatalog.UseDirectoryForTests(_dir);
    }

    public void Dispose()
    {
        LibraryCatalog.ResetForTests();
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void RetargetAndClone_KeepTagsOnNewPath()
    {
        var root = Path.Combine(_dir, "music");
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "song.wav");
        File.WriteAllBytes(source, [1, 2, 3, 4]);
        LibraryCatalog.UpsertTags(source, new AudioFileTags { Probed = true, Title = "Hello", SampleRate = 44100 });

        var copied = Path.Combine(root, "song 2.wav");
        File.Copy(source, copied);
        LibraryCatalog.CloneEntry(source, copied);
        Assert.True(LibraryCatalog.TryGetFreshTags(copied, out var cloned));
        Assert.Equal("Hello", cloned.Title);

        var moved = Path.Combine(root, "renamed.wav");
        File.Move(source, moved);
        LibraryCatalog.RetargetPrefix(source, moved);
        Assert.True(LibraryCatalog.TryGetFreshTags(moved, out var retargeted));
        Assert.Equal("Hello", retargeted.Title);
        Assert.False(LibraryCatalog.TryGetFreshTags(source, out _));

        LibraryCatalog.RemovePrefix(moved);
        Assert.False(LibraryCatalog.TryGetFreshTags(moved, out _));
        Assert.True(LibraryCatalog.TryGetFreshTags(copied, out _));
    }
}
