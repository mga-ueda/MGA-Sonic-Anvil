using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class WaveformTileLayoutTests
{
    [Fact]
    public void Parse_ReadsStoredArrange()
    {
        Assert.Equal(WaveformTileArrange.Off, WaveformTileLayout.Parse(null));
        Assert.Equal(WaveformTileArrange.Off, WaveformTileLayout.Parse(""));
        Assert.Equal(WaveformTileArrange.Off, WaveformTileLayout.Parse("off"));
        Assert.Equal(WaveformTileArrange.Vertical, WaveformTileLayout.Parse("vertical"));
        Assert.Equal(WaveformTileArrange.Horizontal, WaveformTileLayout.Parse("Horizontal"));
        Assert.Equal(WaveformTileArrange.Grid, WaveformTileLayout.Parse("GRID"));
        Assert.Equal(WaveformTileArrange.Off, WaveformTileLayout.Parse("unknown"));
    }

    [Fact]
    public void Format_RoundTripsKnownArranges()
    {
        foreach (var arrange in new[]
        {
            WaveformTileArrange.Off,
            WaveformTileArrange.Vertical,
            WaveformTileArrange.Horizontal,
            WaveformTileArrange.Grid,
        })
        {
            Assert.Equal(arrange, WaveformTileLayout.Parse(WaveformTileLayout.Format(arrange)));
        }
    }

    [Fact]
    public void Next_CyclesHorizontalVerticalGridThenOff()
    {
        Assert.Equal(WaveformTileArrange.Horizontal, WaveformTileLayout.Next(WaveformTileArrange.Off));
        Assert.Equal(WaveformTileArrange.Vertical, WaveformTileLayout.Next(WaveformTileArrange.Horizontal));
        Assert.Equal(WaveformTileArrange.Grid, WaveformTileLayout.Next(WaveformTileArrange.Vertical));
        Assert.Equal(WaveformTileArrange.Off, WaveformTileLayout.Next(WaveformTileArrange.Grid));
    }

    [Fact]
    public void Next_TwoFiles_SkipsGridThatMatchesHorizontal()
    {
        Assert.True(WaveformTileLayout.SameShape(
            WaveformTileArrange.Horizontal,
            WaveformTileArrange.Grid,
            count: 2));
        Assert.Equal(WaveformTileArrange.Horizontal, WaveformTileLayout.Next(WaveformTileArrange.Off, 2));
        Assert.Equal(WaveformTileArrange.Vertical, WaveformTileLayout.Next(WaveformTileArrange.Horizontal, 2));
        Assert.Equal(WaveformTileArrange.Off, WaveformTileLayout.Next(WaveformTileArrange.Vertical, 2));
        Assert.Equal(WaveformTileArrange.Off, WaveformTileLayout.Next(WaveformTileArrange.Grid, 2));
    }

    [Fact]
    public void Next_FourFiles_KeepsDistinctGrid()
    {
        Assert.False(WaveformTileLayout.SameShape(
            WaveformTileArrange.Horizontal,
            WaveformTileArrange.Grid,
            count: 4));
        Assert.Equal(WaveformTileArrange.Vertical, WaveformTileLayout.Next(WaveformTileArrange.Horizontal, 4));
        Assert.Equal(WaveformTileArrange.Grid, WaveformTileLayout.Next(WaveformTileArrange.Vertical, 4));
        Assert.Equal(WaveformTileArrange.Off, WaveformTileLayout.Next(WaveformTileArrange.Grid, 4));
    }

    [Fact]
    public void Fits_Off_AlwaysTrue()
    {
        Assert.True(WaveformTileLayout.Fits(WaveformTileArrange.Off, 1, 10, 10));
        Assert.True(WaveformTileLayout.Fits(WaveformTileArrange.Off, 20, 1, 1));
    }

    [Fact]
    public void Fits_NeedsTwoFilesAndRoomForSmallestCell()
    {
        var minW = WaveformTileLayout.MinTileWidth;
        var minH = WaveformTileLayout.MinTileHeight;
        Assert.False(WaveformTileLayout.Fits(WaveformTileArrange.Horizontal, 1, minW * 8, minH * 8));
        Assert.True(WaveformTileLayout.Fits(WaveformTileArrange.Horizontal, 2, minW * 2, minH));
        Assert.False(WaveformTileLayout.Fits(WaveformTileArrange.Horizontal, 2, minW * 2 - 1, minH));
        Assert.True(WaveformTileLayout.Fits(WaveformTileArrange.Vertical, 2, minW, minH * 2));
        Assert.False(WaveformTileLayout.Fits(WaveformTileArrange.Vertical, 2, minW, minH * 2 - 1));
        Assert.True(WaveformTileLayout.Fits(WaveformTileArrange.Grid, 9, minW * 3, minH * 3));
        Assert.False(WaveformTileLayout.Fits(WaveformTileArrange.Grid, 9, minW * 3 - 1, minH * 3));
        Assert.False(WaveformTileLayout.Fits(WaveformTileArrange.Horizontal, 9, minW * 3, minH * 3));
        Assert.False(WaveformTileLayout.Fits(WaveformTileArrange.Vertical, 9, minW * 3, minH * 3));
    }

    [Fact]
    public void Fits_UnknownHostSize_DoesNotBlock()
    {
        Assert.True(WaveformTileLayout.Fits(WaveformTileArrange.Grid, 8, 0, 0));
        Assert.True(WaveformTileLayout.Fits(WaveformTileArrange.Horizontal, 8, 1, 400));
    }

    [Fact]
    public void Next_SkipsArrangesThatDoNotFit()
    {
        var minW = WaveformTileLayout.MinTileWidth;
        var minH = WaveformTileLayout.MinTileHeight;
        Assert.Equal(
            WaveformTileArrange.Vertical,
            WaveformTileLayout.Next(WaveformTileArrange.Off, 2, minW, minH * 2));
        Assert.Equal(
            WaveformTileArrange.Off,
            WaveformTileLayout.Next(WaveformTileArrange.Horizontal, 2, minW * 2, minH));
        Assert.Equal(
            WaveformTileArrange.Grid,
            WaveformTileLayout.Next(WaveformTileArrange.Off, 9, minW * 3, minH * 3));
        Assert.Equal(
            WaveformTileArrange.Off,
            WaveformTileLayout.Next(WaveformTileArrange.Off, 9, minW, minH));
        Assert.Equal(
            WaveformTileArrange.Off,
            WaveformTileLayout.Next(WaveformTileArrange.Off, 2, minW - 1, minH * 8));
        Assert.Equal(
            WaveformTileArrange.Grid,
            WaveformTileLayout.Next(WaveformTileArrange.Off, 4, minW * 2, minH * 4));
        Assert.Equal(
            WaveformTileArrange.Grid,
            WaveformTileLayout.Next(WaveformTileArrange.Horizontal, 4, minW * 2, minH * 2));
        Assert.Equal(
            WaveformTileArrange.Off,
            WaveformTileLayout.Next(WaveformTileArrange.Horizontal, 4, minW * 2, minH));
    }

    [Fact]
    public void Fallback_LinearUnusable_TriesGridThenOff()
    {
        var minW = WaveformTileLayout.MinTileWidth;
        var minH = WaveformTileLayout.MinTileHeight;
        Assert.Equal(
            WaveformTileArrange.Horizontal,
            WaveformTileLayout.Fallback(WaveformTileArrange.Horizontal, 2, minW * 2, minH));
        Assert.Equal(
            WaveformTileArrange.Grid,
            WaveformTileLayout.Fallback(WaveformTileArrange.Horizontal, 4, minW * 2, minH * 4));
        Assert.Equal(
            WaveformTileArrange.Grid,
            WaveformTileLayout.Fallback(WaveformTileArrange.Vertical, 9, minW * 3, minH * 3));
        Assert.Equal(
            WaveformTileArrange.Off,
            WaveformTileLayout.Fallback(WaveformTileArrange.Horizontal, 9, minW, minH));
        Assert.Equal(
            WaveformTileArrange.Off,
            WaveformTileLayout.Fallback(WaveformTileArrange.Grid, 9, minW, minH));
        Assert.Equal(
            WaveformTileArrange.Off,
            WaveformTileLayout.Fallback(WaveformTileArrange.Off, 9, minW * 3, minH * 3));
    }

    [Theory]
    [InlineData(2, 2, 1)]
    [InlineData(3, 3, 1)]
    [InlineData(4, 4, 1)]
    public void ChooseGrid_Vertical_IsOneColumn(int count, int rows, int cols)
    {
        WaveformTileLayout.ChooseGrid(WaveformTileArrange.Vertical, count, out var actualRows, out var actualCols);
        Assert.Equal(rows, actualRows);
        Assert.Equal(cols, actualCols);
    }

    [Theory]
    [InlineData(2, 1, 2)]
    [InlineData(3, 1, 3)]
    [InlineData(4, 1, 4)]
    public void ChooseGrid_Horizontal_IsOneRow(int count, int rows, int cols)
    {
        WaveformTileLayout.ChooseGrid(WaveformTileArrange.Horizontal, count, out var actualRows, out var actualCols);
        Assert.Equal(rows, actualRows);
        Assert.Equal(cols, actualCols);
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 1, 2)]
    [InlineData(3, 2, 2)]
    [InlineData(4, 2, 2)]
    [InlineData(5, 2, 3)]
    [InlineData(6, 2, 3)]
    [InlineData(7, 3, 3)]
        [InlineData(8, 3, 3)]
        [InlineData(9, 3, 3)]
        [InlineData(11, 3, 4)]
        public void ChooseGrid_Grid_IsNearlySquare(int count, int rows, int cols)
    {
        WaveformTileLayout.ChooseGrid(WaveformTileArrange.Grid, count, out var actualRows, out var actualCols);
        Assert.Equal(rows, actualRows);
        Assert.Equal(cols, actualCols);
    }

    [Fact]
    public void ChooseGrid_EmptyOrOff_IsZero()
    {
        WaveformTileLayout.ChooseGrid(WaveformTileArrange.Grid, 0, out var rows, out var cols);
        Assert.Equal(0, rows);
        Assert.Equal(0, cols);
        WaveformTileLayout.ChooseGrid(WaveformTileArrange.Off, 4, out rows, out cols);
        Assert.Equal(0, rows);
        Assert.Equal(0, cols);
    }

    [Fact]
    public void Cell_LastSingleItem_SpansFullRow()
    {
        var cell = WaveformTileLayout.Cell(2, count: 3, rows: 2, cols: 2);
        Assert.Equal(1, cell.Row);
        Assert.Equal(0, cell.Column);
        Assert.Equal(2, cell.ColumnSpan);
    }

    [Fact]
    public void Cell_FilledRow_DoesNotSpan()
    {
        var cell = WaveformTileLayout.Cell(3, count: 4, rows: 2, cols: 2);
        Assert.Equal(1, cell.Row);
        Assert.Equal(1, cell.Column);
        Assert.Equal(1, cell.ColumnSpan);
    }

    [Fact]
    public void UnusedCells_ElevenFiles_HasBottomRightHole()
    {
        var unused = WaveformTileLayout.UnusedCells(11, rows: 3, cols: 4);
        var cell = Assert.Single(unused);
        Assert.Equal(2, cell.Row);
        Assert.Equal(3, cell.Column);
        Assert.Equal(1, cell.RowSpan);
        Assert.Equal(1, cell.ColumnSpan);
    }

    [Fact]
    public void UnusedCells_LastItemSpansRow_HasNoHole()
    {
        Assert.Empty(WaveformTileLayout.UnusedCells(3, rows: 2, cols: 2));
        Assert.Empty(WaveformTileLayout.UnusedCells(7, rows: 3, cols: 3));
    }

    [Fact]
    public void UnusedCells_FiveFiles_HasOneHole()
    {
        var unused = WaveformTileLayout.UnusedCells(5, rows: 2, cols: 3);
        var cell = Assert.Single(unused);
        Assert.Equal(1, cell.Row);
        Assert.Equal(2, cell.Column);
    }

    [Fact]
    public void ResolveAnalysis_UsesStoredThenFallback()
    {
        Assert.Equal(
            WaveformAnalysisView.Spectrogram,
            WaveformTileLayout.ResolveAnalysis(WaveformAnalysisView.Spectrogram, WaveformAnalysisView.Waveform));
        Assert.Equal(
            WaveformAnalysisView.Loudness,
            WaveformTileLayout.ResolveAnalysis(null, WaveformAnalysisView.Loudness));
    }
}
