using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class BrandLicenseAlignTests
{
    [Fact]
    public void FirstLine_StopsAtNewline()
    {
        Assert.Equal("© line", BrandLicenseAlign.FirstLine("© line\nWwise®"));
        Assert.Equal(3, BrandLicenseAlign.LineCount("a\nb\nc"));
    }

    [Fact]
    public void FirstLineShiftY_MovesTextDownToLogoInk()
    {
        Assert.Equal(11, BrandLicenseAlign.FirstLineShiftY(logoInkBottom: 21, firstBaseline: 10));
    }

    [Fact]
    public void LineBaselineShiftY_UsesWwiseLineBox()
    {
        Assert.Equal(21 - (13 + 10), BrandLicenseAlign.LineBaselineShiftY(21, 13, 1, 10));
        Assert.Equal(1, BrandLicenseAlign.FindLineIndex("a\nWwise\nc", "Wwise"));
    }

    [Fact]
    public void TextShiftY_MovesTextDownWhenGlyphsSitAboveLogoInk()
    {
        var shift = BrandLicenseAlign.TextShiftY(
            imageWidth: 140,
            imageHeight: 21,
            sourceWidth: 140,
            sourceHeight: 21,
            inkBottomFraction: 1,
            textBlockHeight: 13,
            textBaseline: 10);
        Assert.Equal(3, shift);
    }

    [Fact]
    public void LogoInkBottomInBox_AccountsForUniformLetterbox()
    {
        var bottom = BrandLicenseAlign.LogoInkBottomInBox(100, 20, 50, 20, 1);
        Assert.Equal(20, bottom);
        var inset = BrandLicenseAlign.LogoInkBottomInBox(100, 20, 200, 20, 0.5);
        Assert.Equal(10, inset);
    }

    [Fact]
    public void TryInkFractions_ReadsOpaqueContent()
    {
        var bitmap = new WriteableBitmap(4, 4, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[4 * 4 * 4];
        void Set(int x, int y, byte a, byte r)
        {
            var i = (y * 4 + x) * 4;
            pixels[i + 2] = r;
            pixels[i + 3] = a;
        }

        Set(1, 1, 255, 20);
        Set(2, 2, 255, 20);
        bitmap.WritePixels(new Int32Rect(0, 0, 4, 4), pixels, 16, 0);
        Assert.True(BrandLicenseAlign.TryInkFractions(bitmap, out var top, out var bottom));
        Assert.Equal(0.25, top);
        Assert.Equal(0.75, bottom);
    }

    [Fact]
    public void IsLogoInk_UsesAlphaOnly()
    {
        Assert.False(BrandLicenseAlign.IsLogoInk(255, 255, 255, 0));
        Assert.True(BrandLicenseAlign.IsLogoInk(255, 255, 255, 255));
        Assert.True(BrandLicenseAlign.IsLogoInk(40, 40, 40, 255));
    }

    [Fact]
    public void MeasureLine_BaselineSitsAboveLineHeight()
    {
        RunSta(() =>
        {
            var block = new System.Windows.Controls.TextBlock
            {
                FontSize = 10,
                FontFamily = new FontFamily("Segoe UI"),
                Text = "© 2026 MIYABI GAME AUDIO INC.  GitHub"
            };
            var line = BrandLicenseAlign.MeasureLine(block, block.Text, 1);
            Assert.True(line.Baseline > 0);
            Assert.True(line.Height > line.Baseline);
        });
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
