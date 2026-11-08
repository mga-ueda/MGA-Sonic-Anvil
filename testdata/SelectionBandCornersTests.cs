using System.Windows;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class SelectionBandCornersTests
{
    [Theory]
    [InlineData(0, 1, (byte)SelectionBandEdge.Solo)]
    [InlineData(0, 3, (byte)SelectionBandEdge.First)]
    [InlineData(1, 3, (byte)SelectionBandEdge.Middle)]
    [InlineData(2, 3, (byte)SelectionBandEdge.Last)]
    [InlineData(0, 2, (byte)SelectionBandEdge.First)]
    [InlineData(1, 2, (byte)SelectionBandEdge.Last)]
    public void EdgeAt_MapsIndexInRun(int index, int count, byte expected) =>
        Assert.Equal((SelectionBandEdge)expected, SelectionBandCorners.EdgeAt(index, count));

    [Fact]
    public void Of_RoundsOnlyExposedEnds()
    {
        const double radius = 5;
        Assert.Equal(new CornerRadius(radius), SelectionBandCorners.Of(SelectionBandEdge.Solo, radius));
        Assert.Equal(new CornerRadius(radius, radius, 0, 0), SelectionBandCorners.Of(SelectionBandEdge.First, radius));
        Assert.Equal(default, SelectionBandCorners.Of(SelectionBandEdge.Middle, radius));
        Assert.Equal(new CornerRadius(0, 0, radius, radius), SelectionBandCorners.Of(SelectionBandEdge.Last, radius));
        Assert.Equal(default, SelectionBandCorners.Of(SelectionBandEdge.Middle, 0));
    }
}
