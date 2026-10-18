using System.IO;
using MgaSonicAnvil.Config;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class ExportFolderMemoryTests
{
    [Fact]
    public void Resolve_PrefersLastFolder()
    {
        var last = NewTempDir("export");
        var source = NewTempDir("source");
        try
        {
            var file = Path.Combine(source, "a.wav");
            Assert.Equal(
                Path.GetFullPath(last),
                ExportFolderMemory.Resolve(last, file));
        }
        finally
        {
            TryDeleteDir(last);
            TryDeleteDir(source);
        }
    }

    [Fact]
    public void Resolve_OpenPrefersLastOpenFolderOverDocument()
    {
        var open = NewTempDir("open");
        var docs = NewTempDir("docs");
        try
        {
            var docFile = Path.Combine(docs, "b.wav");
            Assert.Equal(
                Path.GetFullPath(open),
                ExportFolderMemory.Resolve(open, docFile));
            Assert.Equal(
                Path.GetFullPath(docs),
                ExportFolderMemory.Resolve(@"C:\missing-open", docFile));
            Assert.Equal(string.Empty, ExportFolderMemory.Resolve(null, @"C:\missing\b.wav"));
        }
        finally
        {
            TryDeleteDir(open);
            TryDeleteDir(docs);
        }
    }

    [Fact]
    public void Resolve_FallsBackToSourceOnly()
    {
        var source = NewTempDir("src");
        try
        {
            var sourceFile = Path.Combine(source, "a.wav");
            Assert.Equal(
                Path.GetFullPath(source),
                ExportFolderMemory.Resolve(@"C:\missing-export", sourceFile));
            Assert.Equal(string.Empty, ExportFolderMemory.Resolve(null, @"C:\missing\a.wav"));
        }
        finally
        {
            TryDeleteDir(source);
        }
    }

    [Fact]
    public void TryResolveBounceStart_UsesLastPathWhenFolderRemains()
    {
        var dir = NewTempDir("bounce");
        try
        {
            var last = Path.Combine(dir, "Mix.wav");
            Assert.True(ExportFolderMemory.TryResolveBounceStart(last, out var directory, out var fileName));
            Assert.Equal(Path.GetFullPath(dir), directory);
            Assert.Equal("Mix.wav", fileName);
            Assert.False(ExportFolderMemory.TryResolveBounceStart(@"C:\missing-bounce\Mix.wav", out _, out _));
            Assert.False(ExportFolderMemory.TryResolveBounceStart(null, out _, out _));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void TryNormalize_RejectsMissingFolder()
    {
        Assert.False(ExportFolderMemory.TryNormalize(@"C:\mga-no-such-export-folder", out _));
        Assert.False(ExportFolderMemory.TryNormalize("  ", out _));
        var dir = NewTempDir("ok");
        try
        {
            Assert.True(ExportFolderMemory.TryNormalize(dir, out var path));
            Assert.Equal(Path.GetFullPath(dir), path);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    private static string NewTempDir(string suffix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mga-export-{suffix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDir(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // 一時フォルダの削除失敗はテストを落とさない。
        }
    }
}
