using System.IO;
using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class PathUtilTests
{
    [Fact]
    public void TryGetFullPath_RejectsBlank()
    {
        Assert.False(PathUtil.TryGetFullPath(null, out _));
        Assert.False(PathUtil.TryGetFullPath("", out _));
        Assert.False(PathUtil.TryGetFullPath("   ", out _));
    }

    [Fact]
    public void TryGetFullPath_RejectsNullChar()
    {
        Assert.False(PathUtil.TryGetFullPath("C:\\bad\0name", out var full));
        Assert.Equal(string.Empty, full);
    }

    [Fact]
    public void TryGetFullPathTrimmed_MatchesGetFullPath()
    {
        var temp = Path.GetTempPath();
        Assert.True(PathUtil.TryGetFullPathTrimmed("  " + temp + "  ", out var full));
        Assert.Equal(Path.GetFullPath(temp.Trim()), full);
    }
}
