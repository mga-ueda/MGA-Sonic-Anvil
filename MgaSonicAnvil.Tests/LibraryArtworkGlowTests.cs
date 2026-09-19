using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using MgaSonicAnvil.Audio;
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
    public void CreateAmbientWash_BakesQuarterTurnsWithoutHostSpin()
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
            var upright = Assert.IsType<DrawingBrush>(LibraryBrowserView.CreateAmbientWash(source));
            Assert.Null(Assert.IsType<DrawingGroup>(upright.Drawing).Transform);

            var turns = LibraryBrowserView.CreateAmbientWashTurns(source);
            Assert.Equal(LibraryBrowserView.GlowWashTurnSteps, turns.Length);
            for (var i = 0; i < turns.Length; i++)
            {
                var wash = Assert.IsType<DrawingBrush>(turns[i]);
                Assert.True(wash.IsFrozen);
                var group = Assert.IsType<DrawingGroup>(wash.Drawing);
                if (i == 0)
                {
                    Assert.Null(group.Transform);
                }
                else
                {
                    var rotate = Assert.IsType<RotateTransform>(group.Transform);
                    Assert.Equal(i * 90, rotate.Angle);
                    Assert.Equal(0.5, rotate.CenterX);
                    Assert.Equal(0.5, rotate.CenterY);
                }
            }

            var fallback = LibraryBrowserView.CreateFallbackWashTurns();
            Assert.Equal(LibraryBrowserView.GlowWashTurnSteps, fallback.Length);
            Assert.Same(fallback, LibraryBrowserView.CreateFallbackWashTurns());
            Assert.Same(fallback[0], LibraryBrowserView.CreateFallbackAmbientWash());
        });
    }

    [Fact]
    public void PaneFocusLineBrush_FadesDownward()
    {
        RunSta(() =>
        {
            var brush = Assert.IsType<LinearGradientBrush>(LibraryBrowserView.CreatePaneFocusLineBrush(UiTheme.Dark));
            Assert.True(brush.IsFrozen);
            Assert.Equal(new Point(0.5, 0), brush.StartPoint);
            Assert.Equal(new Point(0.5, 1), brush.EndPoint);
            Assert.True(brush.GradientStops[0].Color.A > brush.GradientStops[^1].Color.A);
            Assert.Equal(LibraryBrowserView.PaneFocusLineAlpha, brush.GradientStops[0].Color.A);
            Assert.True(LibraryBrowserView.PaneFocusLineAlpha >= 0x10);
            Assert.True(LibraryBrowserView.PaneFocusLineAlpha <= 0x28);
            Assert.Equal(255, brush.GradientStops[0].Color.R);
            Assert.Equal(0, brush.GradientStops[^1].Color.A);
            Assert.Equal(36, LibraryBrowserView.PaneFocusLineHeight);

            var light = Assert.IsType<LinearGradientBrush>(LibraryBrowserView.CreatePaneFocusLineBrush(UiTheme.Light));
            Assert.Equal(LibraryBrowserView.PaneFocusLineAlphaLight, light.GradientStops[0].Color.A);
            Assert.True(light.GradientStops[0].Color.A > LibraryBrowserView.PaneFocusLineAlpha);
            Assert.True(light.GradientStops[0].Color.R < 0x80);
            Assert.Equal(0, light.GradientStops[^1].Color.A);
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

    [Fact]
    public void SetArtwork_JacketToJacket_DoesNotInsertFallbackWash()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var view = new LibraryBrowserView();
            Assert.True(view.GlowUsesFallback);

            var first = new AudioDocument(new float[48], 48000, 1, 16, AudioFileKind.Mp3, "a.mp3");
            first.SetArtwork(PngPixel(220, 40, 40));
            view.SetArtwork(first);
            Assert.False(view.GlowUsesFallback);
            Assert.False(view.IsPlaceholderJacket);

            var broken = new AudioDocument(new float[48], 48000, 1, 16, AudioFileKind.Mp3, "bad.mp3");
            broken.SetArtwork(new byte[] { 1, 2, 3, 4 });
            view.SetArtwork(broken);
            Assert.False(view.GlowUsesFallback);
            Assert.False(view.IsPlaceholderJacket);

            var second = new AudioDocument(new float[48], 48000, 1, 16, AudioFileKind.Mp3, "b.mp3");
            second.SetArtwork(PngPixel(40, 40, 220));
            view.SetArtwork(second);
            Assert.False(view.GlowUsesFallback);
            Assert.False(view.IsPlaceholderJacket);

            view.SetArtwork(null, keepCurrentIfEmpty: true);
            Assert.False(view.GlowUsesFallback);
            Assert.False(view.IsPlaceholderJacket);

            view.SetArtwork(null);
            Assert.True(view.GlowUsesFallback);
            Assert.True(view.IsPlaceholderJacket);
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
        Assert.True(LibraryBrowserView.GlowDriftScaleFrom >= 1.05);
        Assert.True(LibraryBrowserView.GlowDriftScaleFrom < 1.3);
        Assert.True(LibraryBrowserView.GlowDriftX >= 48);
        Assert.True(LibraryBrowserView.GlowDriftY >= 36);
        Assert.True(LibraryBrowserView.GlowDriftScaleSeconds >= 6);
        Assert.True(LibraryBrowserView.GlowDriftScaleSeconds <= 16);
        Assert.True(LibraryBrowserView.GlowDriftXSeconds >= 6);
        Assert.True(LibraryBrowserView.GlowDriftYSeconds >= 10);
        Assert.Equal(4, LibraryBrowserView.GlowWashTurnSteps);
        Assert.True(LibraryBrowserView.GlowWashTurnSeconds >= 12);
        Assert.True(LibraryBrowserView.GlowWashTurnSeconds <= 30);
        Assert.True(LibraryBrowserView.GlowWashTurnFadeSeconds >= 6);
        Assert.True(LibraryBrowserView.GlowWashTurnFadeSeconds < LibraryBrowserView.GlowWashTurnSeconds);
        Assert.NotEqual(LibraryBrowserView.GlowDriftXSeconds, LibraryBrowserView.GlowDriftYSeconds);
        Assert.True(LibraryBrowserView.GlowDriftFrameRate >= 12);
        Assert.True(LibraryBrowserView.GlowDriftFrameRate <= 24);
        Assert.True(LibraryBrowserView.GlowSampleEdge >= 8);
        Assert.True(LibraryBrowserView.GlowSampleEdge <= 24);
        Assert.Equal(1, LibraryBrowserView.GlowCrossfadeSeconds);
        Assert.True(LibraryBrowserView.GlowCrossfadeFrameRate >= 24);
        Assert.True(LibraryBrowserView.GlowCrossfadeFrameRate <= 60);

        RunSta(() =>
        {
            var pulse = LibraryBrowserView.CreateGlowDriftPulse(-80, 80, 16);
            Assert.True(pulse.AutoReverse);
            Assert.Equal(RepeatBehavior.Forever, pulse.RepeatBehavior);
            Assert.Equal(TimeSpan.FromSeconds(16), pulse.Duration.TimeSpan);
            Assert.Equal(16, Timeline.GetDesiredFrameRate(pulse));
            Assert.IsType<SineEase>(pulse.EasingFunction);

            var fadeIn = LibraryBrowserView.CreateGlowCrossfade(0, 1);
            var fadeOut = LibraryBrowserView.CreateGlowCrossfade(1, 0);
            Assert.Equal(TimeSpan.FromSeconds(1), fadeIn.Duration.TimeSpan);
            Assert.Equal(0, fadeIn.From);
            Assert.Equal(1, fadeIn.To);
            Assert.Equal(1, fadeOut.From);
            Assert.Equal(0, fadeOut.To);
            Assert.Equal(FillBehavior.HoldEnd, fadeIn.FillBehavior);
            var fadeInEase = Assert.IsType<SineEase>(fadeIn.EasingFunction);
            var fadeOutEase = Assert.IsType<SineEase>(fadeOut.EasingFunction);
            Assert.Equal(EasingMode.EaseOut, fadeInEase.EasingMode);
            Assert.Equal(EasingMode.EaseIn, fadeOutEase.EasingMode);
            Assert.InRange(fadeInEase.Ease(0.5), 0.70, 0.72);
            Assert.InRange(1 - fadeOutEase.Ease(0.5), 0.70, 0.72);
            Assert.False(fadeIn.AutoReverse);
            Assert.Equal(LibraryBrowserView.GlowCrossfadeFrameRate, Timeline.GetDesiredFrameRate(fadeIn));
            Assert.Equal(LibraryBrowserView.GlowCrossfadeFrameRate, Timeline.GetDesiredFrameRate(fadeOut));

            var turnFade = LibraryBrowserView.CreateGlowCrossfade(0, 1, LibraryBrowserView.GlowWashTurnFadeSeconds);
            Assert.Equal(TimeSpan.FromSeconds(LibraryBrowserView.GlowWashTurnFadeSeconds), turnFade.Duration.TimeSpan);
            Assert.Equal(FillBehavior.HoldEnd, turnFade.FillBehavior);
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
    public void SetGlowExtendsWaveform_ShowsBoundHost()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var host = new Grid { Visibility = Visibility.Collapsed };
            var view = new LibraryBrowserView();
            view.BindWaveformGlow(host);
            Assert.Equal(Visibility.Collapsed, host.Visibility);
            view.SetGlowExtendsWaveform(true);
            Assert.Equal(Visibility.Visible, host.Visibility);
            view.SetGlowExtendsWaveform(false);
            Assert.Equal(Visibility.Collapsed, host.Visibility);
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

    private static byte[] PngPixel(byte r, byte g, byte b)
    {
        var bmp = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { b, g, r, 255 }, 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void EnsureTheme()
    {
        if (Application.Current is null)
        {
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        }

        Application.Current!.Resources["AccentCyanBrush"] = new SolidColorBrush(Color.FromRgb(0x6C, 0xB6, 0xFF));
        Application.Current.Resources["PrimaryForeBrush"] = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xEA));
        Application.Current.Resources["MenuHighlightBackBrush"] = new SolidColorBrush(Color.FromRgb(0x37, 0x37, 0x3A));
        Application.Current.Resources["SurfaceBackBrush"] = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
    }
}
