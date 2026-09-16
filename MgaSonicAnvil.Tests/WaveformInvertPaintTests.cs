using MgaSonicAnvil.Domain;
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
    public void ChannelScaleLeft_HidesLaneWhenFullscreen()
    {
        var bounds = new System.Windows.Rect(0, 0, 800, 400);
        Assert.Equal(DesignMetrics.DbScaleWidth, WaveformView.ChannelScaleLeft(bounds, showScaleLane: true));
        Assert.Equal(0, WaveformView.ChannelScaleLeft(bounds, showScaleLane: false));
        Assert.Equal(800 - DesignMetrics.DbScaleWidth, WaveformView.ChannelScaleContentWidth(bounds, showScaleLane: true));
        Assert.Equal(800, WaveformView.ChannelScaleContentWidth(bounds, showScaleLane: false));

        var narrow = new System.Windows.Rect(0, 0, 20, 400);
        Assert.Equal(20, WaveformView.ChannelScaleLeft(narrow, showScaleLane: true));
        Assert.Equal(0, WaveformView.ChannelScaleLeft(narrow, showScaleLane: false));
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

    [Fact]
    public void SpectrogramSelectionFill_LightIsBrightWash()
    {
        var fill = WaveformView.SpectrogramSelectionFill();
        var old = UiThemePalette.ColorFor(UiTheme.Light, "LoopRangeFillBrush");
        Assert.True(fill.R + fill.G + fill.B > old.R + old.G + old.B);
        Assert.Equal(255, fill.R);
    }
}
