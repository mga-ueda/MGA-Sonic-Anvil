using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class OverviewSeekTrailTests
{
    [Fact]
    public void Draw_PaintsAfterglowBandBehindPlayhead()
    {
        RunSta(() =>
        {
            var now = Environment.TickCount64;
            var samples = new List<(long Frame, long TickMs)>
            {
                (0, now - 200),
                (4000, now - 100),
                (8000, now),
            };
            var band = new Rect(0, 0, 200, 24);
            var playheadX = 160d;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Black, null, band);
                SeekPlaybackTrailPaint.Draw(
                    dc,
                    band,
                    playheadX,
                    contentLeft: 0,
                    samples,
                    frame => frame / 10000d * band.Width,
                    fadeMs: 2000,
                    Colors.Cyan);
            }

            var bmp = new RenderTargetBitmap(200, 24, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            var pixels = new byte[200 * 24 * 4];
            bmp.CopyPixels(pixels, 200 * 4, 0);

            var midY = 12;
            var nearHead = CyanAt(pixels, 200, (int)playheadX - 6, midY);
            var farLeft = CyanAt(pixels, 200, 8, midY);
            Assert.True(nearHead > 8, $"near playhead cyan={nearHead}");
            Assert.True(nearHead > farLeft, $"near={nearHead} far={farLeft}");
        });
    }

    private static int CyanAt(byte[] pixels, int width, int x, int y)
    {
        var i = ((y * width) + x) * 4;
        var b = pixels[i];
        var g = pixels[i + 1];
        var r = pixels[i + 2];
        return b > 20 && g > 20 && r < 40 ? b + g : 0;
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
