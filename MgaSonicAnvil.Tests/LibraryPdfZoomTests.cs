using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibraryPdfZoomTests
{
    [Fact]
    public void SnapFactor_IsFillOverFit()
    {
        // 縦長ページを正方形ビューへ: fit=高さ、fill=幅 → snap=2
        var snap = LibraryPdfZoom.SnapFactor(
            viewportWidth: 400,
            viewportHeight: 400,
            pageWidth: 100,
            pageHeight: 200);
        Assert.Equal(2.0, snap, 6);
    }

    [Fact]
    public void SnapFactor_MatchesAspect_IsOne()
    {
        var snap = LibraryPdfZoom.SnapFactor(800, 600, 800, 600);
        Assert.Equal(1.0, snap, 6);
    }

    [Fact]
    public void Step_StopsAtSnapOnCross_AndHoldsOnRepeat()
    {
        var latched = false;
        var zoom = 1.16;
        var snap = 1.2;

        // スナップをまたぐとそこに止まり、押しっぱなしでは進まない
        zoom = LibraryPdfZoom.Step(zoom, direction: 1, snap, isRepeat: false, ref latched);
        Assert.Equal(snap, zoom, 6);
        Assert.True(latched);

        var held = LibraryPdfZoom.Step(zoom, direction: 1, snap, isRepeat: true, ref latched);
        Assert.Equal(snap, held, 6);

        // 離して押し直すとスナップを越える
        latched = false;
        zoom = LibraryPdfZoom.Step(zoom, direction: 1, snap, isRepeat: false, ref latched);
        Assert.True(zoom > snap);
    }

    [Fact]
    public void Step_ClampsToMinAndMax()
    {
        var latched = false;
        Assert.Equal(1.0, LibraryPdfZoom.Step(1.0, -1, snap: 1.5, isRepeat: false, ref latched), 6);
        Assert.Equal(3.0, LibraryPdfZoom.Step(3.0, 1, snap: 1.5, isRepeat: false, ref latched), 6);
    }

    [Fact]
    public void Step_ZoomOutStopsAtSnapFromAbove()
    {
        var latched = false;
        var zoom = LibraryPdfZoom.Step(1.22, direction: -1, snap: 1.2, isRepeat: false, ref latched);
        Assert.Equal(1.2, zoom, 6);
        Assert.True(latched);
    }
}
