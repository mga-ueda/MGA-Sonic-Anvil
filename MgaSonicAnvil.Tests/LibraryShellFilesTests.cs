using System.IO;
using System.Windows;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibraryShellFilesTests
{
    [Fact]
    public void ExistingPaths_KeepsRealUnique_DropsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "mga-shell-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "track.mp3");
        File.WriteAllBytes(file, [0]);
        try
        {
            var paths = LibraryShellFiles.ExistingPaths(
            [
                file,
                file,
                Path.Combine(root, "missing.wav"),
                "   ",
                null,
                root,
            ]);
            Assert.Equal(2, paths.Length);
            Assert.Contains(Path.GetFullPath(file), paths);
            Assert.Contains(Path.GetFullPath(root), paths);
        }
        finally
        {
            File.Delete(file);
            Directory.Delete(root);
        }
    }

    [Fact]
    public void ExplorerArguments_OpensFolder_SelectsFile_DedupesSamePlace()
    {
        var root = Path.Combine(Path.GetTempPath(), "mga-shell-open-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var first = Path.Combine(root, "a.mp3");
        var second = Path.Combine(root, "b.mp3");
        File.WriteAllBytes(first, [0]);
        File.WriteAllBytes(second, [0]);
        try
        {
            var folderArgs = LibraryShellFiles.ExplorerArguments([root]);
            Assert.Equal(["\"" + Path.GetFullPath(root) + "\""], folderArgs);

            var fileArgs = LibraryShellFiles.ExplorerArguments([first, second]);
            Assert.Single(fileArgs);
            Assert.Equal("/select,\"" + Path.GetFullPath(first) + "\"", fileArgs[0]);

            var mixed = LibraryShellFiles.ExplorerArguments([first, root]);
            Assert.Single(mixed);
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
            Directory.Delete(root);
        }
    }

    [Fact]
    public void DropEffectCopy_IsOleCopyNotMove()
    {
        Assert.Equal(1, LibraryShellFiles.DropEffectCopy);
        Assert.Equal(2, LibraryShellFiles.DropEffectMove);
    }

    [Fact]
    public void CreateCopyEffectStream_IsCopyDword()
    {
        using var stream = LibraryShellFiles.CreateCopyEffectStream();
        Assert.Equal(0, stream.Position);
        var bytes = stream.ToArray();
        Assert.Equal([LibraryShellFiles.DropEffectCopy, 0, 0, 0], bytes);
    }

    [Fact]
    public void TryOpenInExplorer_UsesFormattedArguments()
    {
        var root = Path.Combine(Path.GetTempPath(), "mga-shell-start-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var seen = new List<string>();
            Assert.True(LibraryShellFiles.TryOpenInExplorer([root], args =>
            {
                seen.Add(args);
                return true;
            }));
            Assert.Equal(["\"" + Path.GetFullPath(root) + "\""], seen);
        }
        finally
        {
            Directory.Delete(root);
        }
    }

    [Fact]
    public void OleCopyData_PreferredDropEffect_StaysCopyOnRepeatedGet()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var root = Path.Combine(Path.GetTempPath(), "mga-shell-ole-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                try
                {
                    var data = LibraryShellFiles.CreateOleCopyData([root]);
                    Assert.IsAssignableFrom<IDataObject>(data);
                    var ole = Assert.IsAssignableFrom<System.Runtime.InteropServices.ComTypes.IDataObject>(data);
                    Assert.Equal(LibraryShellFiles.DropEffectCopy, LibraryCopyDropDataObject.ReadPreferredDropEffect(ole));
                    Assert.Equal(LibraryShellFiles.DropEffectCopy, LibraryCopyDropDataObject.ReadPreferredDropEffect(ole));

                    ole.EnumFormatEtc(System.Runtime.InteropServices.ComTypes.DATADIR.DATADIR_GET)
                        .Reset();
                    var one = new System.Runtime.InteropServices.ComTypes.FORMATETC[1];
                    var fetched = new int[1];
                    Assert.Equal(0, ole.EnumFormatEtc(System.Runtime.InteropServices.ComTypes.DATADIR.DATADIR_GET)
                        .Next(1, one, fetched));
                    Assert.Equal(1, fetched[0]);
                    Assert.Equal(
                        System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL,
                        one[0].tymed);
                }
                finally
                {
                    Directory.Delete(root);
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            throw error;
        }
    }

    [Fact]
    public void CreateCopyData_SetsFileDropAndCopyEffect()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var root = Path.Combine(Path.GetTempPath(), "mga-shell-data-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                try
                {
                    var data = LibraryShellFiles.CreateCopyData([root]);
                    Assert.NotNull(data);
                    Assert.True(data!.GetDataPresent(DataFormats.FileDrop));
                    var dropped = Assert.IsType<string[]>(data.GetData(DataFormats.FileDrop));
                    Assert.Equal([Path.GetFullPath(root)], dropped);
                    Assert.True(data.GetDataPresent(LibraryShellFiles.PreferredDropEffectFormat));
                    using var effect = Assert.IsType<MemoryStream>(
                        data.GetData(LibraryShellFiles.PreferredDropEffectFormat));
                    Assert.Equal(LibraryShellFiles.DropEffectCopy, effect.ReadByte());
                }
                finally
                {
                    Directory.Delete(root);
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            throw error;
        }
    }
}
