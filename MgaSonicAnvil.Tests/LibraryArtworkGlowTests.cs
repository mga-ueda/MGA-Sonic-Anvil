using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibraryArtworkGlowTests
{
    [Fact]
    public void SampleArtworkColors_PrefersSaturatedHues()
    {
        RunSta(() =>
        {
            var pixels = new byte[8 * 8 * 4];
            for (var i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 20;
                pixels[i + 1] = 20;
                pixels[i + 2] = 220;
                pixels[i + 3] = 255;
            }

            for (var y = 0; y < 3; y++)
            {
                for (var x = 5; x < 8; x++)
                {
                    var i = (y * 8 + x) * 4;
                    pixels[i] = 220;
                    pixels[i + 1] = 40;
                    pixels[i + 2] = 40;
                    pixels[i + 3] = 255;
                }
            }

            var source = BitmapSource.Create(8, 8, 96, 96, PixelFormats.Bgra32, null, pixels, 8 * 4);
            var colors = LibraryBrowserView.SampleArtworkColors(source, 3);
            Assert.Equal(3, colors.Length);
            Assert.True(colors[0].R > colors[0].B);
            Assert.Contains(colors, c => c.B > c.R);
        });
    }

    [Fact]
    public void CreateAmbientWash_ReturnsFrozenDrawingBrush()
    {
        RunSta(() =>
        {
            var pixels = new byte[4 * 4 * 4];
            for (var i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = 40;
                pixels[i + 1] = 180;
                pixels[i + 2] = 40;
                pixels[i + 3] = 255;
            }

            var source = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, pixels, 16);
            var brush = LibraryBrowserView.CreateAmbientWash(source);
            Assert.IsType<DrawingBrush>(brush);
            Assert.True(brush.IsFrozen);
        });
    }

    [Fact]
    public void FallbackWash_IsNavyCyanWhite()
    {
        RunSta(() =>
        {
            var brush = Assert.IsType<DrawingBrush>(LibraryBrowserView.CreateFallbackAmbientWash());
            Assert.True(brush.IsFrozen);
            Assert.Same(brush, LibraryBrowserView.CreateFallbackAmbientWash());
            var colors = new List<Color>();
            CollectColors(brush.Drawing, colors);
            Assert.Contains(colors, color => SameRgb(color, LibraryBrowserView.FallbackWashNavy));
            Assert.Contains(colors, color => SameRgb(color, LibraryBrowserView.FallbackWashCyan));
            Assert.Contains(colors, color => SameRgb(color, LibraryBrowserView.FallbackWashWhite));
        });
    }

    private static void CollectColors(Drawing? drawing, List<Color> colors)
    {
        switch (drawing)
        {
            case DrawingGroup group:
                foreach (var child in group.Children)
                {
                    CollectColors(child, colors);
                }

                break;
            case GeometryDrawing geometry:
                CollectBrush(geometry.Brush, colors);
                break;
        }
    }

    private static void CollectBrush(Brush? brush, List<Color> colors)
    {
        switch (brush)
        {
            case SolidColorBrush solid:
                colors.Add(solid.Color);
                break;
            case GradientBrush gradient:
                foreach (var stop in gradient.GradientStops)
                {
                    colors.Add(stop.Color);
                }

                break;
        }
    }

    private static bool SameRgb(Color color, Color expected) =>
        color.R == expected.R && color.G == expected.G && color.B == expected.B;

    [Fact]
    public void GlowVeilOpacity_IsBrighterInLight()
    {
        Assert.Equal(0.42, LibraryBrowserView.GlowVeilOpacityFor(UiTheme.Dark), 3);
        Assert.Equal(0.58, LibraryBrowserView.GlowVeilOpacityFor(UiTheme.Light), 3);
        Assert.True(LibraryBrowserView.GlowVeilOpacityFor(UiTheme.Light)
            > LibraryBrowserView.GlowVeilOpacityFor(UiTheme.Dark));
    }

    [Fact]
    public void GlowDrift_IsSlowAndNoticeable()
    {
        Assert.True(LibraryBrowserView.GlowDriftScaleTo - LibraryBrowserView.GlowDriftScaleFrom >= 0.1);
        Assert.True(LibraryBrowserView.GlowDriftScaleTo - LibraryBrowserView.GlowDriftScaleFrom <= 0.2);
        Assert.True(LibraryBrowserView.GlowDriftScaleFrom >= 1.0);
        Assert.True(LibraryBrowserView.GlowDriftX >= 48);
        Assert.True(LibraryBrowserView.GlowDriftY >= 36);
        Assert.True(LibraryBrowserView.GlowDriftScaleSeconds >= 6);
        Assert.True(LibraryBrowserView.GlowDriftScaleSeconds <= 16);
        Assert.True(LibraryBrowserView.GlowDriftXSeconds >= 6);
        Assert.True(LibraryBrowserView.GlowDriftYSeconds >= 10);
        Assert.NotEqual(LibraryBrowserView.GlowDriftXSeconds, LibraryBrowserView.GlowDriftYSeconds);
        Assert.True(LibraryBrowserView.GlowDriftFrameRate >= 12);
        Assert.True(LibraryBrowserView.GlowDriftFrameRate <= 24);
        Assert.True(LibraryBrowserView.GlowSampleEdge >= 8);
        Assert.True(LibraryBrowserView.GlowSampleEdge <= 24);

        RunSta(() =>
        {
            var pulse = LibraryBrowserView.CreateGlowDriftPulse(-80, 80, 16);
            Assert.True(pulse.AutoReverse);
            Assert.Equal(RepeatBehavior.Forever, pulse.RepeatBehavior);
            Assert.Equal(TimeSpan.FromSeconds(16), pulse.Duration.TimeSpan);
            Assert.Equal(16, Timeline.GetDesiredFrameRate(pulse));
            Assert.IsType<SineEase>(pulse.EasingFunction);
        });
    }

    [Fact]
    public void GlowHost_SpansTreeFavoritesAndPlaylist()
    {
        RunSta(() =>
        {
            if (Application.Current is null)
            {
                _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            }

            Application.Current!.Resources["AccentCyanBrush"] = new SolidColorBrush(Color.FromRgb(0x6C, 0xB6, 0xFF));
            Application.Current.Resources["PrimaryForeBrush"] = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xEA));
            Application.Current.Resources["MenuHighlightBackBrush"] = new SolidColorBrush(Color.FromRgb(0x37, 0x37, 0x3A));
            var view = new LibraryBrowserView();
            Assert.True(view.GlowFillsLibraryChrome);
        });
    }

    [Fact]
    public void JacketReflectionMask_FadesDownward()
    {
        var brush = Assert.IsType<LinearGradientBrush>(LibraryJacketReflectionView.CreateOpacityMask());
        Assert.True(brush.IsFrozen);
        Assert.True(brush.GradientStops[0].Color.A > brush.GradientStops[^1].Color.A);
        Assert.Equal(0, brush.GradientStops[^1].Color.A);
        Assert.InRange(LibraryJacketReflection.HeightFactor, 0.35, 0.45);
        Assert.Equal(1, LibraryJacketReflection.GapDip);
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
