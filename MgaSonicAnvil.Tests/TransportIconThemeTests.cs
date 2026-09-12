using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class TransportIconThemeTests
{
    [Fact]
    public void Outline_BothThemes()
    {
        Assert.True(TransportIconTheme.Outline(UiTheme.Light));
        Assert.True(TransportIconTheme.Outline(UiTheme.Dark));
    }

    [Fact]
    public void Folder_IsHollowInBothThemes()
    {
        RunSta(() =>
        {
            foreach (var theme in new[] { UiTheme.Light, UiTheme.Dark })
            {
                var pixels = Render(TransportIcon.Folder, theme, 34, 36);
                Assert.False(IsInk(pixels, 34, 17, 20), $"{theme} folder body should be hollow");
                Assert.True(IsInk(pixels, 34, 6, 18), $"{theme} folder should keep a stroke");
            }
        });
    }

    [Fact]
    public void Folder_WaapiSize_StaysHollow()
    {
        RunSta(() =>
        {
            var pixels = Render(TransportIcon.Folder, UiTheme.Dark, 22, 22);
            Assert.False(IsInk(pixels, 22, 11, 12), "22px Wwise folder body should be hollow");
            Assert.True(CountInk(pixels, 22, 22) >= 20, "22px Wwise folder should still draw a stroke");
        });
    }

    [Fact]
    public void FolderHoverBounds_MatchesGlyphOnWaapiButton()
    {
        var hover = TransportIconDrawing.FolderHoverBounds(new Rect(0, 0, 22, 22));
        Assert.True(hover.Width > hover.Height);
        Assert.InRange(hover.Width, 16, 20);
        Assert.InRange(hover.Height, 13, 17);
        Assert.InRange(hover.X + hover.Width * 0.5, 10.5, 11.5);
        Assert.InRange(hover.Y + hover.Height * 0.5, 10.5, 11.5);
    }

    [Fact]
    public void Folder_IsBboxCenteredInBothThemes()
    {
        RunSta(() =>
        {
            foreach (var theme in new[] { UiTheme.Light, UiTheme.Dark })
            {
                AssertIconCentered(TransportIcon.Folder, theme, 22, 22);
                AssertIconCentered(TransportIcon.Folder, theme, 34, 36);
            }
        });
    }

    [Fact]
    public void Play_IsHollowInBothThemes()
    {
        RunSta(() =>
        {
            foreach (var theme in new[] { UiTheme.Light, UiTheme.Dark })
            {
                var pixels = Render(TransportIcon.PlayPause, theme, 34, 36);
                Assert.False(IsInk(pixels, 34, 16, 18), $"{theme} play should be hollow");
            }
        });
    }

    [Fact]
    public void Moon_IsHollowCenteredWithUpperRightBite()
    {
        RunSta(() =>
        {
            var pixels = Render(TransportIcon.ThemeMoon, UiTheme.Dark, 34, 36);
            Assert.False(IsInk(pixels, 34, 13, 20), "moon body should be hollow");
            Assert.False(IsInk(pixels, 34, 23, 12), "moon bite should stay upper-right");

            var lowerLeft = CountInkIn(pixels, 34, 36, 6, 16, 18, 28);
            var upperRight = CountInkIn(pixels, 34, 36, 18, 28, 6, 16);
            Assert.True(lowerLeft > upperRight, $"upper-right bite expected: ll={lowerLeft} ur={upperRight}");

            Centroid(pixels, 34, 36, out var cx, out var cy);
            Assert.InRange(cx, 15.2, 18.8);
            Assert.InRange(cy, 16.2, 19.8);
        });
    }

    [Fact]
    public void Lock_IsBboxCenteredInBothThemes()
    {
        RunSta(() =>
        {
            foreach (var theme in new[] { UiTheme.Light, UiTheme.Dark })
            {
                foreach (var icon in new[] { TransportIcon.Lock, TransportIcon.Unlock })
                {
                    AssertIconCentered(icon, theme, 22, 22);
                    AssertIconCentered(icon, theme, 34, 36);
                }
            }
        });
    }

    private static void AssertIconCentered(TransportIcon icon, UiTheme theme, int width, int height)
    {
        var pixels = Render(icon, theme, width, height);
        InkBounds(pixels, width, height, out var left, out var top, out var right, out var bottom);
        var cx = (left + right) * 0.5;
        var cy = (top + bottom) * 0.5;
        Assert.True(
            cx >= width * 0.5 - 1.6 && cx <= width * 0.5 + 1.6,
            $"{theme} {icon} {width}x{height} cx={cx:0.0}");
        Assert.True(
            cy >= height * 0.5 - 1.6 && cy <= height * 0.5 + 1.6,
            $"{theme} {icon} {width}x{height} cy={cy:0.0}");
    }

    private static void InkBounds(
        byte[] pixels,
        int width,
        int height,
        out int left,
        out int top,
        out int right,
        out int bottom)
    {
        left = width;
        top = height;
        right = -1;
        bottom = -1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!IsInk(pixels, width, x, y))
                {
                    continue;
                }

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        Assert.True(right >= left && bottom >= top, "icon has no ink");
    }

    private static byte[] Render(TransportIcon icon, UiTheme theme, int width, int height)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));
            TransportIconDrawing.Draw(dc, icon, new Rect(0, 0, width, height), Colors.White, isPlaying: false, theme);
        }

        var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var pixels = new byte[width * height * 4];
        bmp.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    private static bool IsInk(byte[] pixels, int width, int x, int y)
    {
        var i = ((y * width) + x) * 4;
        return pixels[i] > 80 && pixels[i + 1] > 80 && pixels[i + 2] > 80;
    }

    private static int CountInk(byte[] pixels, int width, int height) =>
        CountInkIn(pixels, width, height, 0, width - 1, 0, height - 1);

    private static int CountInkIn(byte[] pixels, int width, int height, int x0, int x1, int y0, int y1)
    {
        var count = 0;
        for (var y = Math.Max(0, y0); y <= Math.Min(height - 1, y1); y++)
        {
            for (var x = Math.Max(0, x0); x <= Math.Min(width - 1, x1); x++)
            {
                if (IsInk(pixels, width, x, y))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static void Centroid(byte[] pixels, int width, int height, out double cx, out double cy)
    {
        var n = 0;
        var sx = 0d;
        var sy = 0d;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!IsInk(pixels, width, x, y))
                {
                    continue;
                }

                sx += x;
                sy += y;
                n++;
            }
        }

        Assert.True(n > 0, "moon has no ink");
        cx = sx / n;
        cy = sy / n;
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
