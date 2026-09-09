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
}
