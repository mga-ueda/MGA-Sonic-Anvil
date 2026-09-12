using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

/// <summary>
/// サラウンドメーターを実際に描画して、右端 1px 列に一切インクが無いことを確認する。
/// PNG も %TEMP% に保存するので目視確認にも使える。
/// </summary>
public sealed class LevelMeterRenderTests
{
    private static readonly Color TransportBack = Color.FromRgb(0x1B, 0x1D, 0x24);

    [Fact]
    public void SurroundTrack_LeavesStereoReadoutGap()
    {
        var bounds = new Rect(0, 0, DesignMetrics.LevelMeterWidth, 400);
        Assert.Equal(24, LevelMeterView.TrackBottomGap);
        Assert.Equal(bounds.Height - 4 - LevelMeterView.TrackBottomGap, LevelMeterView.TrackHeight(bounds));
    }

    [Fact]
    public void SurroundMeter_KeepsInkInsideRightEdge()
    {
        RunSta(() =>
        {
            EnsureWpfResources();
            foreach (var channels in new[] { 6, 8, 16 })
            {
                RenderAndAssert(channels);
            }
        });
    }

    private static void RenderAndAssert(int channels)
    {
        var engine = new LevelMeterEngine();
        var peaks = new float[channels];
        var rms = new float[channels];
        for (var i = 0; i < channels; i++)
        {
            peaks[i] = 0.4f + 0.5f * i / channels;
            rms[i] = peaks[i] * 0.6f;
        }

        var snapshot = engine.Update(peaks, rms, nowSeconds: 1.0, hasSamples: true);
        var view = new LevelMeterView();
        view.Apply(snapshot);

        var width = (int)DesignMetrics.LevelMeterWidth;
        const int height = 626;
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        SavePng(bitmap, $"mga-meter-{channels}ch.png");

        var pixels = new int[width * height];
        bitmap.CopyPixels(pixels, width * 4, 0);

        var back = (0xFF << 24) | (TransportBack.R << 16) | (TransportBack.G << 8) | TransportBack.B;
        // 何かは描かれている。
        Assert.Contains(pixels, pixel => pixel != back);
        // 右端 1px 列は背景のみ（インクがあれば見切れ）。
        for (var y = 0; y < height; y++)
        {
            var pixel = pixels[y * width + (width - 1)];
            Assert.True(
                pixel == back,
                $"{channels}ch: ink at right edge y={y} pixel=0x{pixel:X8}");
        }
    }

    private static void EnsureWpfResources()
    {
        if (Application.Current is null)
        {
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        }

        // LevelMeterView が参照するキーだけを用意する（テーマ全体は読まない）。
        Application.Current!.Resources["TransportBackBrush"] = new SolidColorBrush(TransportBack);
        Application.Current.Resources["MutedForeBrush"] = new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xAD));
        Application.Current.Resources["DbScaleForeBrush"] = new SolidColorBrush(Color.FromRgb(0x96, 0x96, 0x96));
        Application.Current.Resources["LevelMeterTrackBackBrush"] = new SolidColorBrush(Color.FromRgb(0x24, 0x26, 0x29));
        Application.Current.Resources["LevelMeterTrackBorderBrush"] = new SolidColorBrush(Color.FromRgb(0x1A, 0x22, 0x1A));
        Application.Current.Resources["LevelMeterHoldBorderBrush"] = new SolidColorBrush(Color.FromRgb(0x1A, 0x22, 0x1A));
        Application.Current.Resources["LevelMeterTickBrush"] = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
        Application.Current.Resources["LevelMeterClipOffBrush"] = new SolidColorBrush(Color.FromRgb(0x28, 0x08, 0x08));
        Application.Current.Resources["LevelMeterClipOffBorderBrush"] = new SolidColorBrush(Color.FromRgb(0x3A, 0x15, 0x15));
    }

    private static void SavePng(BitmapSource bitmap, string fileName)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(Path.GetTempPath(), fileName));
        encoder.Save(stream);
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
