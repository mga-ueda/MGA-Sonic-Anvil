using MgaSonicAnvil.Audio;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class BusyGlassJobLayoutTests
{
    private const double RowWidth = 324;

    [Fact]
    public void Arrange_FewJobs_StayInOneColumn()
    {
        var layout = BusyGlassJobLayout.Arrange(4, 800, 400, RowWidth);
        Assert.Equal(1, layout.Columns);
        Assert.Equal(4, layout.Rows);
        Assert.Equal(BusyGlassJobLayout.DefaultRowHeight, layout.RowHeight);
    }

    [Fact]
    public void Arrange_ThirtyTwoJobs_UseTwoColumnsWhenTall()
    {
        var layout = BusyGlassJobLayout.Arrange(32, 800, 400, RowWidth);
        Assert.Equal(2, layout.Columns);
        Assert.Equal(16, layout.Rows);
        Assert.Equal(BusyGlassJobLayout.DefaultRowHeight, layout.RowHeight);
    }

    [Fact]
    public void Arrange_NarrowWidth_StaysOneColumn()
    {
        var layout = BusyGlassJobLayout.Arrange(32, 400, 400, RowWidth);
        Assert.Equal(1, layout.Columns);
        Assert.Equal(32, layout.Rows);
        Assert.True(layout.RowHeight <= BusyGlassJobLayout.DefaultRowHeight);
        Assert.True(layout.RowHeight >= BusyGlassJobLayout.MinRowHeight);
    }

    [Fact]
    public void Arrange_Empty_HasNoRows()
    {
        var layout = BusyGlassJobLayout.Arrange(0, 800, 400, RowWidth);
        Assert.Equal(1, layout.Columns);
        Assert.Equal(0, layout.Rows);
    }

    [Fact]
    public void SameJobPaint_IgnoresSubPercentNoise()
    {
        ExportJobProgress[] current = [new("a.wav", 0.081), new("b.wav", 1)];
        ExportJobProgress[] next = [new("a.wav", 0.084), new("b.wav", 1)];
        Assert.True(BusyGlassOverlay.SameJobPaint(current, next));
    }

    [Fact]
    public void SameJobPaint_DetectsPercentStep()
    {
        ExportJobProgress[] current = [new("a.wav", 0.08)];
        ExportJobProgress[] next = [new("a.wav", 1)];
        Assert.False(BusyGlassOverlay.SameJobPaint(current, next));
    }

    [Fact]
    public void FrostPixelSize_DownscalesAndKeepsMinimum()
    {
        Assert.Equal((96, 54), BusyGlassOverlay.FrostPixelSize(1920, 1080, 20));
        Assert.Equal((1, 1), BusyGlassOverlay.FrostPixelSize(0, 0, 20));
    }
}
