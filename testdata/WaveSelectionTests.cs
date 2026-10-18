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
}
