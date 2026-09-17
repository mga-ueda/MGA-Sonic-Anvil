using MgaSonicAnvil.Audio;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class BusyGlassJobLayoutTests
{
    private const double RowWidth = BusyGlassJobLayout.PreferredRowWidth;

    [Fact]
    public void Arrange_FewJobs_StayInOneColumn()
    {
        var layout = BusyGlassJobLayout.Arrange(4, 800, 400, RowWidth);
        Assert.Equal(1, layout.Columns);
        Assert.Equal(4, layout.Rows);
        Assert.Equal(BusyGlassJobLayout.DefaultRowHeight, layout.RowHeight);
        Assert.Equal(RowWidth, layout.RowWidth);
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
    public void Arrange_NarrowWidth_AddsColumnByShrinkingRow()
    {
        var layout = BusyGlassJobLayout.Arrange(32, 400, 400, RowWidth);
        Assert.Equal(2, layout.Columns);
        Assert.Equal(16, layout.Rows);
        Assert.True(layout.RowWidth < RowWidth);
        Assert.True(layout.RowWidth >= BusyGlassJobLayout.MinRowWidth);
        Assert.True(layout.Rows * layout.RowHeight <= 400 + 1e-6);
    }

    [Fact]
    public void Arrange_ManyJobs_ExceedsThreeColumnsWhenNeeded()
    {
        var layout = BusyGlassJobLayout.Arrange(80, 1600, 200, RowWidth);
        Assert.True(layout.Columns > 3);
        Assert.True(layout.Rows * layout.RowHeight <= 200 + 1e-6);
        Assert.True(layout.RowWidth >= BusyGlassJobLayout.MinRowWidth);
    }

    [Fact]
    public void Arrange_Empty_HasNoRows()
    {
        var layout = BusyGlassJobLayout.Arrange(0, 800, 400, RowWidth);
        Assert.Equal(1, layout.Columns);
        Assert.Equal(0, layout.Rows);
    }

    [Fact]
    public void SplitRow_Preferred_KeepsNameAndBar()
    {
        var parts = BusyGlassJobLayout.SplitRow(RowWidth);
        Assert.Equal(BusyGlassJobLayout.PreferredNameWidth, parts.NameWidth);
        Assert.Equal(BusyGlassJobLayout.PreferredBarWidth, parts.BarWidth);
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
