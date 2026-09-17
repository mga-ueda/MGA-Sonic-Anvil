using System.IO;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibraryExplorerPathsTests
{
    [Fact]
    public void Resolve_Empty_IsDefaultFolder()
    {
        var expected = LibraryExplorerPaths.DefaultFolder();
        Assert.Equal(expected, LibraryExplorerPaths.Resolve(null));
        Assert.Equal(expected, LibraryExplorerPaths.Resolve(""));
        Assert.Equal(expected, LibraryExplorerPaths.Resolve("   "));
    }

    [Fact]
    public void Resolve_Missing_IsDefaultFolder()
    {
        var missing = Path.Combine(Path.GetTempPath(), "mga-anvil-missing-explorer-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(LibraryExplorerPaths.DefaultFolder(), LibraryExplorerPaths.Resolve(missing));
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
    public void AppSettings_EmptyExplorerPath_ResolvesToDefault()
    {
        var settings = new AppSettings();
        Assert.Equal(LibraryExplorerPaths.DefaultFolder(), settings.ResolvedLibraryExplorerPath());
        settings.ApplyLibraryExplorerPath(Path.Combine(Path.GetTempPath(), "mga-anvil-missing-apply-" + Guid.NewGuid().ToString("N")));
        Assert.Equal(LibraryExplorerPaths.DefaultFolder(), settings.LibraryExplorerPath);
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
    }
}
