using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class WaveformInvertPaintTests
{
    [Fact]
    public void ChannelLabelBadgeX_LeftAligns()
    {
        Assert.Equal(0, WaveformView.ChannelLabelBadgeX(0));
        Assert.Equal(4, WaveformView.ChannelLabelBadgeX(4));
    }

    [Fact]
    public void ChannelLabelTextX_CentersInFixedBadge()
    {
        Assert.Equal(6, WaveformView.ChannelLabelTextX(badgeX: 0, badgeW: 20, textW: 8));
        Assert.Equal(10, WaveformView.ChannelLabelTextX(badgeX: 4, badgeW: 20, textW: 8));
        Assert.Equal(4, WaveformView.ChannelLabelTextX(badgeX: 4, badgeW: 20, textW: 20));
    }

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

    [Fact]
    public void SoftenSelectionFill_Light_PullsDarkFillTowardBack()
    {
        var fill = unchecked((int)0xFF404040);
        var back = unchecked((int)0xFFFAFAFA);
        Assert.Equal(fill, WaveformInvertPaint.SoftenSelectionFill(fill, back, light: false));
        var softened = WaveformInvertPaint.SoftenSelectionFill(fill, back, light: true);
        Assert.True(Luma(softened) > Luma(fill));
        Assert.True(Luma(softened) < Luma(back));
    }

    private static double Luma(int bgra)
    {
        var r = (bgra >> 16) & 0xFF;
        var g = (bgra >> 8) & 0xFF;
        var b = bgra & 0xFF;
        return (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
    }
}
