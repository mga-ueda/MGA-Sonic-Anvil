using MgaSonicAnvil.Audio;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LevelMeterSurroundLayoutTests
{
    private const double MeterWidth = 100;
    private const double ScaleWidth = 22;
    private const double Eps = 1e-9;

    private static (double BarW, double BarsLeft) Bars(int channels)
    {
        var barW = LevelMeterSurroundLayout.BarWidth(channels);
        return (barW, LevelMeterSurroundLayout.BarsLeft(0, MeterWidth, barW, channels));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    public void BarStrip_LeavesRoomForBothScales(int channels)
    {
        var (barW, barsLeft) = Bars(channels);

        Assert.True(barW >= LevelMeterSurroundLayout.MinBarWidth);
        Assert.Equal(Math.Floor(barW), barW);
        // 左右とも目盛り列 22px が丸ごと入る。
        Assert.True(barsLeft >= ScaleWidth, $"バー左端 {barsLeft} が目盛り幅 {ScaleWidth} 未満");
        Assert.True(
            barsLeft + (barW * channels) + ScaleWidth <= MeterWidth + Eps,
            $"バー右端 {barsLeft + (barW * channels)} + 目盛り {ScaleWidth} が {MeterWidth} を超えた");
    }

    [Fact]
    public void BarWidth_ShrinksWithChannelCount()
    {
        Assert.True(LevelMeterSurroundLayout.BarWidth(16) < LevelMeterSurroundLayout.BarWidth(6));
    }

    [Fact]
    public void BarWidth_NeverExceedsStereoBar()
    {
        Assert.True(LevelMeterSurroundLayout.BarWidth(3) <= LevelMeterSurroundLayout.MaxBarWidth);
    }

    [Fact]
    public void BarWidth_GrowsWhenBlockIsWider()
    {
        Assert.True(LevelMeterSurroundLayout.BarWidth(16, 160) > LevelMeterSurroundLayout.BarWidth(16));
        Assert.True(LevelMeterSurroundLayout.BarWidth(16, 160) <= LevelMeterSurroundLayout.MaxBarWidth);
    }

    [Fact]
    public void FilledColumnWidth_StopsWhenBarsReachFullThickness()
    {
        var min = DesignMetrics.LevelMeterWidth;
        Assert.Equal(min, LevelMeterSurroundLayout.FilledColumnWidth(2));
        Assert.Equal(min, LevelMeterSurroundLayout.FilledColumnWidth(4));
        Assert.Equal(
            (LevelMeterSurroundLayout.ScaleColWidth * 2) + (LevelMeterSurroundLayout.MaxBarWidth * 6),
            LevelMeterSurroundLayout.FilledColumnWidth(6));
        Assert.Equal(
            (LevelMeterSurroundLayout.ScaleColWidth * 2) + (LevelMeterSurroundLayout.MaxBarWidth * ChannelLayout.MaxChannels),
            LevelMeterSurroundLayout.FilledColumnWidth(ChannelLayout.MaxChannels));
        Assert.Equal(min, DesignMetrics.ClampMeterColumnWidth(180, 2));
        Assert.Equal(LevelMeterSurroundLayout.FilledColumnWidth(6), DesignMetrics.ClampMeterColumnWidth(999, 6));
    }

    [Fact]
    public void ClampMeterColumnWidth_KeepsDefaultAsMinimum()
    {
        var min = DesignMetrics.LevelMeterWidth;
        var max = DesignMetrics.LevelMeterWidthMax;
        Assert.Equal(min, DesignMetrics.ClampMeterColumnWidth(0));
        Assert.Equal(min, DesignMetrics.ClampMeterColumnWidth(-10));
        Assert.Equal(min, DesignMetrics.ClampMeterColumnWidth(min));
        Assert.Equal(min + 80, DesignMetrics.ClampMeterColumnWidth(min + 80));
        Assert.Equal(max, DesignMetrics.ClampMeterColumnWidth(max + 100));
        Assert.Equal(max, LevelMeterSurroundLayout.FilledColumnWidth(ChannelLayout.MaxChannels));
    }
}
