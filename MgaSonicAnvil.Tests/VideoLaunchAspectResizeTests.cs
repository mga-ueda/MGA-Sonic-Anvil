using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class VideoLaunchAspectResizeTests
{
    [Fact]
    public void ConstrainRect_RightEdge_KeepsAspect()
    {
        var left = 100;
        var top = 100;
        var right = 100 + 16 + 800;
        var bottom = 100 + 39 + 600;
        var constraints = new VideoLaunchAspectResize.Constraints(
            clientAspect: 16.0 / 9.0,
            nonClientWidthPx: 16,
            nonClientHeightPx: 39,
            minClientWidthPx: 160,
            minClientHeightPx: 90,
            maxClientWidthPx: 3840,
            maxClientHeightPx: 2160);

        VideoLaunchAspectResize.ConstrainRect(
            ref left,
            ref top,
            ref right,
            ref bottom,
            edge: 2, // WMSZ_RIGHT
            constraints);

        var clientW = right - left - 16;
        var clientH = bottom - top - 39;
        Assert.Equal(16.0 / 9.0, clientW / (double)clientH, 3);
        Assert.Equal(100, left);
        Assert.Equal(100, top);
    }

    [Fact]
    public void ApplyEdge_LeftKeepsRight()
    {
        var left = 0;
        var top = 0;
        var right = 500;
        var bottom = 400;
        VideoLaunchAspectResize.ApplyEdge(ref left, ref top, ref right, ref bottom, edge: 1, outerWidth: 300, outerHeight: 200);
        Assert.Equal(200, left);
        Assert.Equal(500, right);
        Assert.Equal(0, top);
        Assert.Equal(200, bottom);
    }
}
