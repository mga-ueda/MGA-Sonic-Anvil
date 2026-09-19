using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibraryPlaceholderJacketTests
{
    [Fact]
    public void AllowsJacketReplace_OnlyMp3()
    {
        Assert.True(LibraryBrowserView.AllowsJacketReplace(AudioFileKind.Mp3));
        Assert.False(LibraryBrowserView.AllowsJacketReplace(AudioFileKind.Wave));
        Assert.False(LibraryBrowserView.AllowsJacketReplace(AudioFileKind.Aiff));
        Assert.False(LibraryBrowserView.AllowsJacketReplace(AudioFileKind.M4a));
    }

    [Fact]
    public void Render_IsSquareVerticalGradientWithNoImage()
    {
        RunSta(() =>
        {
            Assert.Equal("No Image", LibraryPlaceholderJacket.Label);
            var dark = LibraryPlaceholderJacket.Render(UiTheme.Dark);
            var light = LibraryPlaceholderJacket.Render(UiTheme.Light);
            Assert.True(dark.IsFrozen);
            Assert.Equal(LibraryPlaceholderJacket.PixelSize, dark.PixelWidth);
            Assert.Equal(LibraryPlaceholderJacket.PixelSize, dark.PixelHeight);
            Assert.Equal(dark.PixelWidth, light.PixelWidth);

            var darkPixels = Copy(dark);
            var lightPixels = Copy(light);
            Assert.NotEqual(darkPixels[0], lightPixels[0]);
            AssertVerticalWash(darkPixels, dark.PixelWidth);
            AssertVerticalWash(lightPixels, light.PixelWidth);
        });
    }

    [Fact]
    public void SetArtwork_ShowsPlaceholder_ReplaceOnlyForMp3()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var view = new LibraryBrowserView();
            Assert.NotNull(view.JacketDisplaySource);
            Assert.True(view.IsPlaceholderJacket);
            Assert.False(view.JacketReplaceEnabled);

            var wave = new AudioDocument(new float[48], 48000, 1, 16, AudioFileKind.Wave, "a.wav");
            view.SetArtwork(wave);
            Assert.True(view.IsPlaceholderJacket);
            Assert.False(view.JacketReplaceEnabled);

            var mp3 = new AudioDocument(new float[48], 48000, 1, 16, AudioFileKind.Mp3, "a.mp3");
            view.SetArtwork(mp3);
            Assert.True(view.IsPlaceholderJacket);
            Assert.True(view.JacketReplaceEnabled);

            mp3.SetArtwork(OnePixelPng);
            view.SetArtwork(mp3);
            Assert.False(view.IsPlaceholderJacket);
            Assert.True(view.JacketReplaceEnabled);
            Assert.NotNull(view.JacketDisplaySource);

            var m4a = new AudioDocument(new float[48], 48000, 1, 16, AudioFileKind.M4a, "a.m4a");
            view.SetArtwork(m4a);
            Assert.True(view.IsPlaceholderJacket);
            Assert.False(view.JacketReplaceEnabled);

            m4a.SetArtwork(OnePixelPng);
            view.SetArtwork(m4a);
            Assert.False(view.IsPlaceholderJacket);
            Assert.False(view.JacketReplaceEnabled);
            Assert.NotNull(view.JacketDisplaySource);
        });
    }

    private static readonly byte[] OnePixelPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54,
        0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00, 0x05, 0x00, 0x01,
        0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00,
        0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    ];

    private static byte[] Copy(BitmapSource source)
    {
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        var pixels = new byte[width * height * 4];
        source.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    private static void AssertVerticalWash(byte[] pixels, int width)
    {
        var top = Luma(pixels, width, 8, 8);
        var topCenter = Luma(pixels, width, width / 2, 8);
        var bottom = Luma(pixels, width, 8, width - 9);
        Assert.True(Math.Abs(top - topCenter) < 12);
        Assert.True(top > bottom + 16);
    }

    private static int Luma(byte[] pixels, int width, int x, int y)
    {
        var i = ((y * width) + x) * 4;
        return ((pixels[i + 2] * 2) + (pixels[i + 1] * 3) + pixels[i]) / 6;
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
