using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class WaveformZeroLineTests
{
    private const int ZeroLine = 0x5ACCCCCC;
    private const int WaveFill = unchecked((int)0xFFC6D9FF);
    private const int Sentinel = 0x01020304;

    [Fact]
    public void CenterLine_StaysOnTopOfEnvelope_IncludingPartialColumns()
    {
        RunSta(() =>
        {
            EnsureBrushes();
            const int width = 48;
            const int height = 40;
            var frames = width * (WaveformView.PolylineMaxSamplesPerPixel + 4);
            var view = ViewOf(SquareWave(frames));
            var pixels = new int[width * height];
            view.RasterizeWaveformForTest(pixels, width, height, 0, width, 0, frames);

            var mid = height / 2;
            AssertCenterLine(pixels, width, mid);
            Assert.Equal(WaveFill, pixels[(mid - 8) * width + (width / 2)]);

            Array.Fill(pixels, Sentinel);
            var x0 = width - 6;
            view.RasterizeWaveformForTest(pixels, width, height, x0, width, 0, frames);
            AssertCenterLine(pixels, width, mid);
            Assert.Equal(Sentinel, pixels[(mid - 8) * width + 2]);
            Assert.Equal(WaveFill, pixels[(mid - 8) * width + (x0 + 1)]);
        });
    }

    [Fact]
    public void CenterLine_StaysOnTopOfPolyline()
    {
        RunSta(() =>
        {
            EnsureBrushes();
            const int width = 32;
            const int height = 40;
            const int frames = 64;
            var view = ViewOf(SquareWave(frames));
            var pixels = new int[width * height];
            view.RasterizeWaveformForTest(pixels, width, height, 0, width, 0, frames);

            AssertCenterLine(pixels, width, height / 2);
            Assert.Contains(WaveFill, pixels);
        });
    }

    private static void AssertCenterLine(int[] pixels, int width, int mid)
    {
        for (var x = 0; x < width; x++)
        {
            Assert.Equal(ZeroLine, pixels[mid * width + x]);
        }
    }

    private static WaveformView ViewOf(float[] samples) =>
        new()
        {
            Document = new AudioDocument(samples, 48000, 1, 16, AudioFileKind.Wave, null),
        };

    private static float[] SquareWave(int frames)
    {
        var samples = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            samples[i] = (i & 1) == 0 ? 1f : -1f;
        }

        return samples;
    }

    private static void EnsureBrushes()
    {
        if (Application.Current is null)
        {
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        }

        var resources = Application.Current!.Resources;
        resources["MouseGuideBrush"] = new SolidColorBrush(Color.FromArgb(0xDC, 0xFF, 0xFF, 0xFF));
        resources["MouseGuideOnSelectionBrush"] = new SolidColorBrush(Color.FromArgb(0xDC, 0x00, 0x00, 0x00));
        resources["WaveFillBrush"] = new SolidColorBrush(Color.FromArgb(0xFF, 0xC6, 0xD9, 0xFF));
        resources["WaveFillOverlayBrush"] = new SolidColorBrush(Color.FromArgb(0xFF, 0x3A, 0xB8, 0xE8));
        resources["LoudnessWaveFillBrush"] = new SolidColorBrush(Color.FromArgb(0xFF, 0x4A, 0x4A, 0x50));
        resources["WaveZeroLineBrush"] = new SolidColorBrush(Color.FromArgb(0x5A, 0xCC, 0xCC, 0xCC));
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
