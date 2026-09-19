using System.IO;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibraryExplorerPathsTests
{
    [Fact]
    public void Resolve_Empty_IsDefaultRootFolder()
    {
        var expected = LibraryExplorerPaths.DefaultRootFolder();
        Assert.Equal(expected, LibraryExplorerPaths.Resolve(null));
        Assert.Equal(expected, LibraryExplorerPaths.Resolve(""));
        Assert.Equal(expected, LibraryExplorerPaths.Resolve("   "));
    }

    [Fact]
    public void Resolve_Missing_IsDefaultRootFolder()
    {
        var missing = Path.Combine(Path.GetTempPath(), "mga-anvil-missing-explorer-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(LibraryExplorerPaths.DefaultRootFolder(), LibraryExplorerPaths.Resolve(missing));
    }

    [Fact]
    public void Resolve_Existing_KeepsFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "mga-anvil-explorer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.Equal(Path.GetFullPath(root), LibraryExplorerPaths.Resolve(root));
        }
        finally
        {
            Directory.Delete(root);
        }
    }

    [Fact]
    public void ResolveRoots_Empty_IsMusic()
    {
        var expected = LibraryExplorerPaths.DefaultRootFolder();
        Assert.Equal([expected], LibraryExplorerPaths.ResolveRoots(null));
        Assert.Equal([expected], LibraryExplorerPaths.ResolveRoots([]));
    }

    [Fact]
    public void ResolveRoots_KeepsExistingUnique()
    {
        var a = Path.Combine(Path.GetTempPath(), "mga-anvil-root-a-" + Guid.NewGuid().ToString("N"));
        var b = Path.Combine(Path.GetTempPath(), "mga-anvil-root-b-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);
        try
        {
            var roots = LibraryExplorerPaths.ResolveRoots([a, b, a]);
            Assert.Equal(2, roots.Length);
            Assert.Contains(Path.GetFullPath(a).TrimEnd('\\', '/'), roots);
            Assert.Contains(Path.GetFullPath(b).TrimEnd('\\', '/'), roots);
        }
        finally
        {
            Directory.Delete(a);
            Directory.Delete(b);
        }
    }

    [Fact]
    public void MoveSelected_Up_ShiftsBlockAndStopsAtTop()
    {
        var items = new List<string> { "a", "b", "c", "d" };
        LibraryExplorerPaths.MoveSelected(items, new HashSet<string> { "b", "c" }, -1);
        Assert.Equal(["b", "c", "a", "d"], items);

        LibraryExplorerPaths.MoveSelected(items, new HashSet<string> { "b" }, -1);
        Assert.Equal(["b", "c", "a", "d"], items);
    }

    [Fact]
    public void MoveSelected_Down_ShiftsBlockAndStopsAtBottom()
    {
        var items = new List<string> { "a", "b", "c", "d" };
        LibraryExplorerPaths.MoveSelected(items, new HashSet<string> { "b", "c" }, 1);
        Assert.Equal(["a", "d", "b", "c"], items);

        LibraryExplorerPaths.MoveSelected(items, new HashSet<string> { "c" }, 1);
        Assert.Equal(["a", "d", "b", "c"], items);
    }

    [Fact]
    public void FavoritePaths_Resolve_DropsMissing_KeepsNameOnlyDisplay()
    {
        var file = Path.Combine(Path.GetTempPath(), "mga-fav-" + Guid.NewGuid().ToString("N") + ".mp3");
        File.WriteAllBytes(file, [0]);
        try
        {
            var resolved = LibraryFavoritePaths.Resolve([file, file, Path.Combine(Path.GetTempPath(), "missing-fav.mp3")]);
            Assert.Equal([Path.GetFullPath(file)], resolved);
            Assert.Equal(Path.GetFileName(file), LibraryFavoritePaths.DisplayName(file));
            Assert.DoesNotContain('\\', LibraryFavoritePaths.DisplayName(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void AppSettings_EmptyExplorerPath_ResolvesToDefaultRoot()
    {
        var settings = new AppSettings();
        Assert.Equal(LibraryExplorerPaths.DefaultRootFolder(), settings.ResolvedLibraryExplorerPath());
        Assert.Equal([LibraryExplorerPaths.DefaultRootFolder()], settings.ResolvedLibraryExplorerRoots());
        settings.ApplyLibraryExplorerPath(Path.Combine(Path.GetTempPath(), "mga-anvil-missing-apply-" + Guid.NewGuid().ToString("N")));
        Assert.Equal(LibraryExplorerPaths.DefaultRootFolder(), settings.LibraryExplorerPath);
    }

    [Fact]
    public void ClampLibraryExplorerWidth_UsesDefaultAndBounds()
    {
        var def = DesignMetrics.LibraryExplorerWidth;
        var min = DesignMetrics.LibraryExplorerMinWidth;
        var max = DesignMetrics.LibraryExplorerMaxWidth;
        Assert.Equal(def, DesignMetrics.ClampLibraryExplorerWidth(0));
        Assert.Equal(def, DesignMetrics.ClampLibraryExplorerWidth(-1));
        Assert.Equal(min, DesignMetrics.ClampLibraryExplorerWidth(min - 10));
        Assert.Equal(max, DesignMetrics.ClampLibraryExplorerWidth(max + 50));
        Assert.Equal(min + 40, DesignMetrics.ClampLibraryExplorerWidth(min + 40));
        Assert.Equal(1, DesignMetrics.LibraryExplorerSplitterWidth);
    }

    [Fact]
    public void ClampLibraryFavoritesSplit_UsesHalfAndBounds()
    {
        Assert.Equal(DesignMetrics.LibraryFavoritesSplitDefault, DesignMetrics.ClampLibraryFavoritesSplit(0));
        Assert.Equal(DesignMetrics.LibraryFavoritesSplitDefault, DesignMetrics.ClampLibraryFavoritesSplit(-1));
        Assert.Equal(DesignMetrics.LibraryFavoritesSplitDefault, DesignMetrics.ClampLibraryFavoritesSplit(1));
        Assert.Equal(DesignMetrics.LibraryFavoritesSplitMin, DesignMetrics.ClampLibraryFavoritesSplit(0.01));
        Assert.Equal(DesignMetrics.LibraryFavoritesSplitMax, DesignMetrics.ClampLibraryFavoritesSplit(0.99));
        Assert.Equal(0.35, DesignMetrics.ClampLibraryFavoritesSplit(0.35));
    }
}
