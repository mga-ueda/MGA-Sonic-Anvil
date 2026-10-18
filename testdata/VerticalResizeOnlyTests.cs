using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class VerticalResizeOnlyTests
{
    [Theory]
    [InlineData(VerticalResizeOnly.HtLeft, VerticalResizeOnly.HtBorder)]
    [InlineData(VerticalResizeOnly.HtRight, VerticalResizeOnly.HtBorder)]
    [InlineData(VerticalResizeOnly.HtTopLeft, VerticalResizeOnly.HtTop)]
    [InlineData(VerticalResizeOnly.HtTopRight, VerticalResizeOnly.HtTop)]
    [InlineData(VerticalResizeOnly.HtBottomLeft, VerticalResizeOnly.HtBottom)]
    [InlineData(VerticalResizeOnly.HtBottomRight, VerticalResizeOnly.HtBottom)]
    [InlineData(VerticalResizeOnly.HtTop, VerticalResizeOnly.HtTop)]
    [InlineData(VerticalResizeOnly.HtBottom, VerticalResizeOnly.HtBottom)]
    [InlineData(1, 1)]
    public void RemapWidthLocked_DropsHorizontalResize(int hit, int expected)
    {
        Assert.Equal(expected, VerticalResizeOnly.RemapWidthLocked(hit));
    }
}
