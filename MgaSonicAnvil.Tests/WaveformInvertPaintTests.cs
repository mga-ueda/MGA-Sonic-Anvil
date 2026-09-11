using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class WaveformInvertPaintTests
{
    [Fact]
    public void LaneAt_SplitsHeightWithoutGap()
    {
        Assert.Equal(0, ChannelWavePaint.LaneAt(0, 4, 2, 0));
        Assert.Equal(0, ChannelWavePaint.LaneAt(1, 4, 2, 0));
        Assert.Equal(1, ChannelWavePaint.LaneAt(2, 4, 2, 0));
        Assert.Equal(1, ChannelWavePaint.LaneAt(3, 4, 2, 0));
    }

    [Fact]
    public void LaneAt_KeepsGapOnPreviousLane()
    {
        Assert.Equal(0, ChannelWavePaint.LaneAt(2, 6, 2, 2));
        Assert.Equal(1, ChannelWavePaint.LaneAt(4, 6, 2, 2));
    }
}
