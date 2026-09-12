using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class TimeScrollRangeTests
{
    [Fact]
    public void HandleWidth_CapsAtHalfThumb()
    {
        Assert.Equal(4, TimeScrollRange.HandleWidth(8, 8), 6);
        Assert.Equal(8, TimeScrollRange.HandleWidth(40, 8), 6);
        Assert.Equal(1, TimeScrollRange.HandleWidth(0, 8), 6);
    }

    [Fact]
    public void HitTest_SplitsEndsAndThumb()
    {
        Assert.Equal(TimeScrollHit.None, TimeScrollRange.HitTest(4, thumbLeft: 10, thumbWidth: 40, preferredHandle: 8));
        Assert.Equal(TimeScrollHit.Left, TimeScrollRange.HitTest(14, thumbLeft: 10, thumbWidth: 40, preferredHandle: 8));
        Assert.Equal(TimeScrollHit.Thumb, TimeScrollRange.HitTest(30, thumbLeft: 10, thumbWidth: 40, preferredHandle: 8));
        Assert.Equal(TimeScrollHit.Right, TimeScrollRange.HitTest(46, thumbLeft: 10, thumbWidth: 40, preferredHandle: 8));
        Assert.Equal(TimeScrollHit.None, TimeScrollRange.HitTest(51, thumbLeft: 10, thumbWidth: 40, preferredHandle: 8));
    }

    [Fact]
    public void HitTest_TinyThumb_UsesHalves()
    {
        Assert.Equal(TimeScrollHit.Left, TimeScrollRange.HitTest(1, thumbLeft: 0, thumbWidth: 6, preferredHandle: 8));
        Assert.Equal(TimeScrollHit.Right, TimeScrollRange.HitTest(5, thumbLeft: 0, thumbWidth: 6, preferredHandle: 8));
    }

    [Fact]
    public void FrameAt_MapsTrackToWholeFile()
    {
        Assert.Equal(0, TimeScrollRange.FrameAt(0, 100, 8000), 6);
        Assert.Equal(4000, TimeScrollRange.FrameAt(50, 100, 8000), 6);
        Assert.Equal(8000, TimeScrollRange.FrameAt(100, 100, 8000), 6);
        Assert.Equal(0, TimeScrollRange.FrameAt(50, 0, 8000), 6);
    }

    [Fact]
    public void ResizeLeft_KeepsRightEdge()
    {
        var next = TimeScrollRange.ResizeLeft(viewStart: 1000, viewSpan: 2000, totalFrames: 8000, newStart: 1500);
        Assert.Equal(1500, next.ViewStart, 6);
        Assert.Equal(1500, next.ViewSpan, 6);
    }

    [Fact]
    public void ResizeLeft_ClampsToZeroAndMinSpan()
    {
        var wider = TimeScrollRange.ResizeLeft(viewStart: 1000, viewSpan: 2000, totalFrames: 8000, newStart: -100);
        Assert.Equal(0, wider.ViewStart, 6);
        Assert.Equal(3000, wider.ViewSpan, 6);

        var min = TimeScrollRange.MinSpan(8000);
        var tighter = TimeScrollRange.ResizeLeft(viewStart: 1000, viewSpan: 2000, totalFrames: 8000, newStart: 2999.5);
        Assert.Equal(3000 - min, tighter.ViewStart, 6);
        Assert.Equal(min, tighter.ViewSpan, 6);
    }

    [Fact]
    public void ResizeRight_KeepsLeftEdge()
    {
        var next = TimeScrollRange.ResizeRight(viewStart: 1000, viewSpan: 2000, totalFrames: 8000, newEnd: 4000);
        Assert.Equal(1000, next.ViewStart, 6);
        Assert.Equal(3000, next.ViewSpan, 6);
    }

    [Fact]
    public void ResizeRight_ClampsToFileEndAndMinSpan()
    {
        var wider = TimeScrollRange.ResizeRight(viewStart: 1000, viewSpan: 2000, totalFrames: 8000, newEnd: 9000);
        Assert.Equal(1000, wider.ViewStart, 6);
        Assert.Equal(7000, wider.ViewSpan, 6);

        var min = TimeScrollRange.MinSpan(8000);
        var tighter = TimeScrollRange.ResizeRight(viewStart: 1000, viewSpan: 2000, totalFrames: 8000, newEnd: 1000.2);
        Assert.Equal(1000, tighter.ViewStart, 6);
        Assert.Equal(min, tighter.ViewSpan, 6);
    }

    [Fact]
    public void ResizeByDelta_MovesTheGrabbedEdge()
    {
        var left = TimeScrollRange.ResizeByDelta(
            leftEdge: true,
            originStart: 1000,
            originSpan: 2000,
            totalFrames: 8000,
            frameDelta: 250);
        Assert.Equal(1250, left.ViewStart, 6);
        Assert.Equal(1750, left.ViewSpan, 6);

        var right = TimeScrollRange.ResizeByDelta(
            leftEdge: false,
            originStart: 1000,
            originSpan: 2000,
            totalFrames: 8000,
            frameDelta: -250);
        Assert.Equal(1000, right.ViewStart, 6);
        Assert.Equal(1750, right.ViewSpan, 6);
    }

    [Fact]
    public void MinSpan_RespectsTimeZoomMax()
    {
        Assert.Equal(1, TimeScrollRange.MinSpan(0), 6);
        Assert.Equal(1, TimeScrollRange.MinSpan(8000), 6);
        Assert.Equal(10, TimeScrollRange.MinSpan((long)(WaveformView.TimeZoomMax * 10)), 6);
    }
}
