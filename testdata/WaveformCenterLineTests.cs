using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class WaveformCenterLineTests
{
    private const int ZeroLine = 0x5A_CC_CC_CC;
    private const int WaveFill = unchecked((int)0xFF_C6_D9_FF);

    [Fact]
    public void Envelope_KeepsZeroLineAboveFullScaleWave()
    {
        RunSta(() =>
        {
            var view = CreateView(frames: 4096);
            const int width = 48;
            const int height = 40;
            var pixels = new int[width * height];
            view.RasterizeWaveformForTest(pixels, width, height, 0, width, 0, 4096);

            AssertCenterLine(pixels, width, height);
            Assert.Equal(WaveFill, pixels[8 * width + 4]);
        });
    }

    [Fact]
    public void PartialScrollStrip_RestoresZeroLineOnNewColumns()
    {
        RunSta(() =>
        {
            var view = CreateView(frames: 4096);
            const int width = 48;
            const int height = 40;
            var pixels = new int[width * height];
            const int x0 = 40;
            view.RasterizeWaveformForTest(pixels, width, height, x0, width, 0, 4096);

            AssertCenterLine(pixels, width, height);
            Assert.Equal(0, pixels[8 * width + 4]);
            Assert.Equal(WaveFill, pixels[8 * width + 44]);
        });
    }

    [Fact]
    public void Polyline_KeepsZeroLineAboveSampleStroke()
    {
        RunSta(() =>
        {
            var view = CreateView(frames: 80);
            const int width = 40;
            const int height = 40;
            var pixels = new int[width * height];
            view.RasterizeWaveformForTest(pixels, width, height, 0, width, 0, 80);

            AssertCenterLine(pixels, width, height);
            Assert.Contains(pixels, pixel => pixel == WaveFill);
        });
    }

    [Fact]
    public void CenterLockLeadIn_DoesNotStretchWaveIntoBlank()
    {
        RunSta(() =>
        {
            const int width = 48;
            const int height = 40;
            var frames = width * (WaveformView.PolylineMaxSamplesPerPixel + 4);
            var view = CreateView(frames);
            var pixels = new int[width * height];
            view.RasterizeWaveformForTest(pixels, width, height, 0, width, -frames / 2d, frames);

            var y = 8;
            Assert.Equal(0, pixels[y * width + 4]);
            Assert.Equal(WaveFill, pixels[y * width + (width - 4)]);
        });
    }

    private static void AssertCenterLine(int[] pixels, int width, int height)
    {
        var y = height / 2;
        for (var x = 0; x < width; x++)
        {
            Assert.Equal(ZeroLine, pixels[y * width + x]);
        }
    }

    private static WaveformView CreateView(int frames)
    {
        EnsureTheme();
        var samples = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            samples[i] = (i & 1) == 0 ? 1f : -1f;
        }

        var view = new WaveformView
        {
            Document = new AudioDocument(samples, 48000, 1, 16, AudioFileKind.Wave, null),
        };
        return view;
    }

    private static void EnsureTheme()
    {
        if (Application.Current is null)
        {
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        }

        var resources = Application.Current!.Resources;
        resources["MouseGuideBrush"] = Frozen(0xDC, 0xFF, 0xFF, 0xFF);
        resources["MouseGuideOnSelectionBrush"] = Frozen(0xDC, 0x00, 0x00, 0x00);
        resources["WaveFillBrush"] = Frozen(0xFF, 0xC6, 0xD9, 0xFF);
        resources["WaveFillOverlayBrush"] = Frozen(0xFF, 0x3A, 0xB8, 0xE8);
        resources["LoudnessWaveFillBrush"] = Frozen(0xFF, 0x4A, 0x4A, 0x50);
        resources["WaveZeroLineBrush"] = Frozen(0x5A, 0xCC, 0xCC, 0xCC);
    }

    private static SolidColorBrush Frozen(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
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
