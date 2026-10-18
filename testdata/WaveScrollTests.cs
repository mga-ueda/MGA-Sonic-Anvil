using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class WaveScrollTests
{
    [Fact]
    public void Quantize_AddsExtraColumnAndSnapsStart()
    {
        WaveScroll.Quantize(viewStart: 10.4, viewSpan: 100, width: 10, out var start, out var span, out var bmpWidth);
        Assert.Equal(11, bmpWidth);
        Assert.Equal(10, start, 6);
        Assert.Equal(110, span, 6);
    }

    [Fact]
    public void TryPixelShift_ForwardScrollMovesContentLeft()
    {
        Assert.True(WaveScroll.TryPixelShift(oldStart: 0, newStart: 10, span: 100, width: 10, out var shiftPx));
        Assert.Equal(1, shiftPx);
    }

    [Fact]
    public void ShiftPacked_ForwardKeepsRightEdgeNew()
    {
        var pixels = new[]
        {
            1, 2, 3, 4,
            5, 6, 7, 8,
        };
        WaveScroll.ShiftPacked(pixels, width: 4, height: 2, shiftPx: 1);
        Assert.Equal(new[] { 2, 3, 4, 0, 6, 7, 8, 0 }, pixels);
    }
}
