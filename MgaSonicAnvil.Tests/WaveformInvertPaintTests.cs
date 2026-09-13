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
    public void InvertPixel_SwapsWaveAndBackground()
    {
        var wave = unchecked((int)0xFF626262);
        var back = unchecked((int)0xFFFAFAFA);
        Assert.Equal(back, WaveformInvertPaint.InvertPixel(wave, back, wave));
        Assert.Equal(wave, WaveformInvertPaint.InvertPixel(0, back, wave));
    }
}
