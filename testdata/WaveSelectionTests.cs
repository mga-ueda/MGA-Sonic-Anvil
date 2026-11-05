using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class WaveSelectionTests
{
    [Fact]
    public void Union_EmptyLeavesOther()
    {
        var span = new WaveSelection(10, 40);
        Assert.Equal(span, WaveSelection.Empty.Union(span));
        Assert.Equal(span, span.Union(WaveSelection.Empty));
        Assert.True(WaveSelection.Empty.Union(WaveSelection.Empty).IsEmpty);
    }

    [Fact]
    public void Union_JoinsAdjacentAndOverlapping()
    {
        Assert.Equal(new WaveSelection(10, 40), new WaveSelection(10, 20).Union(new WaveSelection(20, 40)));
        Assert.Equal(new WaveSelection(10, 40), new WaveSelection(20, 40).Union(new WaveSelection(10, 20)));
        Assert.Equal(new WaveSelection(10, 40), new WaveSelection(10, 30).Union(new WaveSelection(20, 40)));
    }

    [Fact]
    public void Union_IncludesGapBetweenSeparatedSpans()
    {
        Assert.Equal(new WaveSelection(10, 80), new WaveSelection(10, 20).Union(new WaveSelection(60, 80)));
    }

    [Fact]
    public void ClampVisualPlayhead_StopsAtExclusiveEnd_KeepsIntro()
    {
        var loop = new WaveSelection(100, 400);
        Assert.Equal(0, loop.ClampVisualPlayhead(0));
        Assert.Equal(50, loop.ClampVisualPlayhead(50));
        Assert.Equal(100, loop.ClampVisualPlayhead(100));
        Assert.Equal(399, loop.ClampVisualPlayhead(399));
        Assert.Equal(399, loop.ClampVisualPlayhead(400));
        Assert.Equal(399, loop.ClampVisualPlayhead(410));
        Assert.Equal(0, loop.ClampVisualPlayhead(-3));
        Assert.Equal(0, WaveSelection.Empty.ClampVisualPlayhead(-1));
        Assert.Equal(50, WaveSelection.Empty.ClampVisualPlayhead(50));
        Assert.Equal(399, WaveSelection.ClampVisualPlayhead(400, 400));
        Assert.Equal(0, WaveSelection.ClampVisualPlayhead(-1, 400));
        Assert.Equal(200, WaveSelection.ClampVisualPlayhead(200, 400));
    }

    [Fact]
    public void JumpFrame_PrefersSelectionBoundsOverFartherCues()
    {
        var selection = new WaveSelection(20, 80);

        Assert.Equal(20, selection.JumpFrame(0, 100, 1));
        Assert.Equal(80, selection.JumpFrame(20, 100, 1));
        Assert.Equal(100, selection.JumpFrame(80, 100, 1));
        Assert.Equal(80, selection.JumpFrame(100, 0, -1));
        Assert.Equal(20, selection.JumpFrame(80, 0, -1));
        Assert.Equal(0, selection.JumpFrame(20, 0, -1));
    }

    [Fact]
    public void JumpFrame_StopsAtCueBetweenSelectionBounds()
    {
        var selection = new WaveSelection(20, 80);

        Assert.Equal(40, selection.JumpFrame(20, 40, 1));
        Assert.Equal(80, selection.JumpFrame(40, 100, 1));
        Assert.Equal(40, selection.JumpFrame(80, 40, -1));
    }

    [Fact]
    public void JumpFrame_PrefersSelectionWhenCueWrapsBackward()
    {
        var selection = new WaveSelection(60, 80);

        Assert.Equal(60, selection.JumpFrame(50, 10, 1));
        Assert.Equal(10, selection.JumpFrame(90, 10, 1));
    }

    [Fact]
    public void JumpFrame_EmptySelectionKeepsCue()
    {
        Assert.Equal(40, WaveSelection.Empty.JumpFrame(10, 40, 1));
        Assert.False(WaveSelection.Empty.HasBoundInDirection(10, 1));
    }

    [Fact]
    public void HasBoundInDirection_IsFalseOnceBothEdgesAreBehind()
    {
        var selection = new WaveSelection(20, 80);

        Assert.True(selection.HasBoundInDirection(0, 1));
        Assert.True(selection.HasBoundInDirection(20, 1));
        Assert.False(selection.HasBoundInDirection(80, 1));
        Assert.True(selection.HasBoundInDirection(100, -1));
        Assert.True(selection.HasBoundInDirection(80, -1));
        Assert.False(selection.HasBoundInDirection(20, -1));
    }
}
