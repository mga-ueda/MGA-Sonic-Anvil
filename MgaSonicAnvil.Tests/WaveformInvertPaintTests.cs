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
    public void InvertPixel_PlayerLight_WaveGoesToFillEmptyIsPale()
    {
        var wave = unchecked((int)0xFF405273);
        var back = unchecked((int)0xFFFAFAFA);
        var pale = unchecked((int)0xFFA8B4C4);
        var invertedWave = WaveformInvertPaint.InvertPixel(wave, back, pale, playerLight: true, invertWave: wave);
        var invertedEmpty = WaveformInvertPaint.InvertPixel(0, back, pale, playerLight: true, invertWave: wave);
        Assert.Equal(wave, invertedWave);
        Assert.NotEqual(back, invertedWave);
        Assert.True(((invertedEmpty >> 16) & 0xFF) > ((pale >> 16) & 0xFF));
        Assert.True(((invertedEmpty >> 8) & 0xFF) > ((pale >> 8) & 0xFF));
    }

    [Fact]
    public void ApplyLaneShade_KeepsCenterAndDarkensEdge()
    {
        var fill = unchecked((int)0xFFC6D9FF);
        var center = WaveformInvertPaint.ApplyLaneShade(fill, 50, 101, UiTheme.Dark);
        var edge = WaveformInvertPaint.ApplyLaneShade(fill, 0, 101, UiTheme.Dark);
        Assert.Equal(fill, center);
        Assert.True(((edge >> 16) & 0xFF) < ((center >> 16) & 0xFF));
        var inverted = WaveformInvertPaint.InvertShaded(
            fill,
            back: unchecked((int)0xFF1A1A1A),
            waveFill: fill,
            playerLight: false,
            invertWave: fill,
            y: 0,
            height: 100,
            shadeLanes: true,
            UiTheme.Dark);
        Assert.NotEqual(unchecked((int)0xFF1A1A1A), inverted);
    }

    [Fact]
    public void SelectionInvertOpacity_LetsOriginalShowThrough()
    {
        Assert.True(WaveformInvertPaint.SelectionInvertOpacity < 1);
        Assert.True(WaveformInvertPaint.SelectionInvertOpacity >= 0.4);
        Assert.True(WaveformInvertPaint.SelectionInvertOpacity <= 0.7);
        Assert.True(WaveformInvertPaint.SelectionInvertOpacityFor(playerLight: true)
            > WaveformInvertPaint.SelectionInvertOpacityFor(playerLight: false));
        Assert.True(WaveformInvertPaint.SelectionInvertOpacityFor(playerLight: true) < 1);
    }

    [Fact]
    public void WaveSelectionFill_IsTranslucentNeutralWash()
    {
        var fill = UiThemePalette.ColorFor(UiTheme.Light, "WaveSelectionFillBrush");
        var cyan = UiThemePalette.ColorFor(UiTheme.Light, "AccentCyanBrush");
        Assert.Equal(0x38, fill.A);
        Assert.True(fill.R == fill.G && fill.G == fill.B);
        Assert.False(fill.R == cyan.R && fill.G == cyan.G && fill.B == cyan.B);
    }

    [Fact]
    public void SpectrogramSelectionFill_IsTranslucentWhite()
    {
        var fill = WaveformView.SpectrogramSelectionFill();
        Assert.Equal(56, fill.A);
        Assert.Equal(255, fill.R);
        Assert.Equal(255, fill.G);
        Assert.Equal(255, fill.B);
    }
}
