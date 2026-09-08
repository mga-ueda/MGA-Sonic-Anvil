using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.UI;

/// <summary>波形エリアのスペクトログラム。パン時は列をずらして足りない分だけ FFT する。</summary>
internal sealed class SpectrogramRenderer
{
    private const int MaxPixelWidth = 1920;
    private const int MaxPixelHeight = 720;

    private readonly float[] _window = new float[SpectrogramEngine.FftSize];
    private readonly float[] _mix = new float[SpectrogramEngine.FftSize];
    private readonly double[] _re = new double[SpectrogramEngine.FftSize];
    private readonly double[] _im = new double[SpectrogramEngine.FftSize];
    private readonly float _windowSum;
    private readonly SpectrogramCache _cache = new();
    private WriteableBitmap? _bitmap;
    private int[] _pixels = [];
    private double[] _rowHertz = [];
    private int _rowHeight;
    private Size _dipSize;
    private double _dpiX;
    private double _dpiY;
    private double _viewStart;
    private double _viewSpan;
    private int _sampleRate;
    private int _pixelWidth;
    private int _pixelHeight;
    private object? _samples;
    private bool _drewFromCache;

    public SpectrogramRenderer()
    {
        SpectrogramEngine.FillHann(_window, out _windowSum);
    }

    public event Action? InvalidateRequested;

    public void RequestCache(AudioDocument document, Action onUpdated) =>
        _cache.Ensure(document, onUpdated);

    public void InvalidateCache() => _cache.Invalidate();

    public void Dispose() => _cache.Dispose();

    public void Draw(
        DrawingContext dc,
        Rect wave,
        AudioDocument document,
        double viewStart,
        double viewSpan,
        Visual host,
        double imageOpacity = 1)
    {
        if (wave.Width <= 1 || wave.Height <= 1 || document.FrameCount <= 0)
        {
            return;
        }

        _cache.Ensure(document, () => host.Dispatcher.BeginInvoke(() => InvalidateRequested?.Invoke()));
        EnsureBitmap(wave, document, viewStart, viewSpan, VisualTreeHelper.GetDpi(host));
        if (_bitmap is not null)
        {
            if (imageOpacity < 0.999)
            {
                dc.PushOpacity(Math.Clamp(imageOpacity, 0, 1));
                DrawSpectrogramImage(dc, wave, viewStart, viewSpan);
                dc.Pop();
            }
            else
            {
                DrawSpectrogramImage(dc, wave, viewStart, viewSpan);
            }
        }

        DrawFrequencyScale(dc, wave, host);
    }

    private void DrawSpectrogramImage(DrawingContext dc, Rect wave, double viewStart, double viewSpan)
    {
        // ビットマップは 1px 格子に量子化した位置（_viewStart）で作られている。
        // 端数はここでサブピクセルオフセットとして吸収し、滑らかにスクロールさせる。
        var visibleWidth = Math.Max(1, _pixelWidth - 1);
        var offsetDip = viewSpan <= 0
            ? 0d
            : (viewStart - _viewStart) / viewSpan * wave.Width;
        var dest = new Rect(
            wave.X - offsetDip,
            wave.Y,
            wave.Width * _pixelWidth / visibleWidth,
            wave.Height);
        var group = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.Fant);
        var context = group.Open();
        context.DrawImage(_bitmap, dest);
        context.Close();
        dc.PushClip(new RectangleGeometry(wave));
        dc.DrawDrawing(group);
        dc.Pop();
    }

    private void EnsureBitmap(Rect wave, AudioDocument document, double viewStart, double viewSpan, DpiScale dpi)
    {
        var width = Math.Clamp((int)Math.Round(wave.Width * Math.Max(1e-6, dpi.DpiScaleX)), 1, MaxPixelWidth);
        var height = Math.Clamp((int)Math.Round(wave.Height * Math.Max(1e-6, dpi.DpiScaleY)), 1, MaxPixelHeight);
        // ビュー開始位置を 1 デバイス px = framesPerPx の格子に量子化する。
        // スクロールが常に整数 px 差になるため、列シフト＋差分 FFT が毎回効く。
        // 右端の欠けを防ぐため 1 列余分に持つ。
        var framesPerPx = viewSpan / width;
        var quantStart = Math.Floor(viewStart / framesPerPx) * framesPerPx;
        var bmpWidth = width + 1;
        var bmpSpan = bmpWidth * framesPerPx;
        if (ReferenceEquals(_samples, document.Interleaved)
            && _bitmap is not null
            && _bitmap.PixelWidth == bmpWidth
            && _bitmap.PixelHeight == height
            && Math.Abs(_dpiX - dpi.DpiScaleX) < 0.001
            && Math.Abs(_dpiY - dpi.DpiScaleY) < 0.001
            && Math.Abs(_viewStart - quantStart) < framesPerPx * 0.01
            && Math.Abs(_viewSpan - bmpSpan) < framesPerPx * 0.01
            && _sampleRate == document.SampleRate
            && _drewFromCache == _cache.IsReady)
        {
            return;
        }

        if (_bitmap is null
            || _bitmap.PixelWidth != bmpWidth
            || _bitmap.PixelHeight != height)
        {
            _bitmap = new WriteableBitmap(
                bmpWidth,
                height,
                dpi.PixelsPerInchX,
                dpi.PixelsPerInchY,
                PixelFormats.Bgra32,
                null);
        }

        var needed = bmpWidth * height;
        if (_pixels.Length < needed)
        {
            _pixels = new int[needed];
        }

        EnsureRowHertz(height);
        var fromCache = _cache.IsReady;
        if (!TryShiftColumns(document, quantStart, bmpSpan, bmpWidth, height, fromCache))
        {
            RasterizeSpan(document, quantStart, bmpSpan, bmpWidth, height, 0, bmpWidth, fromCache);
        }

        _bitmap.WritePixels(new Int32Rect(0, 0, bmpWidth, height), _pixels, bmpWidth * 4, 0);
        _samples = document.Interleaved;
        _dipSize = wave.Size;
        _dpiX = dpi.DpiScaleX;
        _dpiY = dpi.DpiScaleY;
        _viewStart = quantStart;
        _viewSpan = bmpSpan;
        _sampleRate = document.SampleRate;
        _pixelWidth = bmpWidth;
        _pixelHeight = height;
        _drewFromCache = fromCache;
    }

    private void RasterizeSpan(
        AudioDocument document,
        double viewStart,
        double viewSpan,
        int width,
        int height,
        int x0,
        int x1,
        bool fromCache)
    {
        if (fromCache)
        {
            RasterizeFromCache(viewStart, viewSpan, width, height, x0, x1, document.SampleRate);
        }
        else
        {
            RasterizeColumns(document, viewStart, viewSpan, width, height, x0, x1);
        }
    }

    private bool TryShiftColumns(
        AudioDocument document,
        double viewStart,
        double viewSpan,
        int width,
        int height,
        bool fromCache)
    {
        if (!ReferenceEquals(_samples, document.Interleaved)
            || _pixelWidth != width
            || _pixelHeight != height
            || _sampleRate != document.SampleRate
            || _drewFromCache != fromCache
            || Math.Abs(_viewSpan - viewSpan) > 0.01
            || viewSpan <= 1)
        {
            return false;
        }

        var shift = (viewStart - _viewStart) / viewSpan * width;
        var shiftPx = (int)Math.Round(shift);
        if (shiftPx == 0 || Math.Abs(shift - shiftPx) > 0.2 || Math.Abs(shiftPx) >= width)
        {
            return false;
        }

        ShiftPixels(width, height, shiftPx);
        if (shiftPx > 0)
        {
            RasterizeSpan(document, viewStart, viewSpan, width, height, width - shiftPx, width, fromCache);
        }
        else
        {
            RasterizeSpan(document, viewStart, viewSpan, width, height, 0, -shiftPx, fromCache);
        }

        return true;
    }

    private void ShiftPixels(int width, int height, int shiftPx)
    {
        if (shiftPx > 0)
        {
            for (var y = 0; y < height; y++)
            {
                var row = y * width;
                Array.Copy(_pixels, row + shiftPx, _pixels, row, width - shiftPx);
            }

            return;
        }

        var left = -shiftPx;
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            Array.Copy(_pixels, row, _pixels, row + left, width - left);
        }
    }

    private void EnsureRowHertz(int height)
    {
        if (_rowHertz.Length == height && _rowHeight == height)
        {
            return;
        }

        if (_rowHertz.Length != height)
        {
            _rowHertz = new double[height];
        }

        for (var y = 0; y < height; y++)
        {
            var unit = height <= 1 ? 0 : 1d - y / (double)(height - 1);
            _rowHertz[y] = SpectrogramEngine.UnitToHertz(
                unit,
                SpectrogramEngine.MinHertz,
                SpectrogramEngine.DisplayMaxHertz);
        }

        _rowHeight = height;
    }

    private void RasterizeFromCache(
        double viewStart,
        double viewSpan,
        int width,
        int height,
        int x0,
        int x1,
        int sampleRate)
    {
        var binHz = Math.Max(1, sampleRate) / (double)SpectrogramEngine.FftSize;
        var nyquist = SpectrogramEngine.ContentNyquist(sampleRate);
        var floor = SpectrogramEngine.ColorBgra(SpectrogramEngine.FloorDb);
        for (var x = x0; x < x1; x++)
        {
            var center = (long)Math.Round(viewStart + (x + 0.5) / width * viewSpan);
            var row = 0;
            for (var y = 0; y < height; y++)
            {
                var hz = _rowHertz[y];
                if (hz > nyquist)
                {
                    _pixels[row + x] = floor;
                }
                else if (_cache.TryColor(center, hz / binHz, out var bgra))
                {
                    _pixels[row + x] = bgra;
                }

                row += width;
            }
        }
    }

    private void RasterizeColumns(
        AudioDocument document,
        double viewStart,
        double viewSpan,
        int width,
        int height,
        int x0,
        int x1)
    {
        var samples = document.Interleaved;
        var channels = Math.Max(1, document.Channels);
        var frames = document.FrameCount;
        var half = SpectrogramEngine.FftSize / 2;
        var bins = half + 1;
        var binHz = Math.Max(1, document.SampleRate) / (double)SpectrogramEngine.FftSize;
        var nyquist = SpectrogramEngine.ContentNyquist(document.SampleRate);
        var floor = SpectrogramEngine.ColorBgra(SpectrogramEngine.FloorDb);

        for (var x = x0; x < x1; x++)
        {
            var center = (long)Math.Round(viewStart + (x + 0.5) / width * viewSpan);
            SpectrogramEngine.FillMonoMix(samples, channels, center - half, frames, _mix);
            SpectrogramEngine.AnalyzeWindow(_mix, _window, _windowSum, _re, _im);
            var row = 0;
            for (var y = 0; y < height; y++)
            {
                var hz = _rowHertz[y];
                _pixels[row + x] = hz > nyquist
                    ? floor
                    : SpectrogramEngine.ColorBgra(SpectrogramEngine.BinDb(_re.AsSpan(0, bins), hz / binHz));
                row += width;
            }
        }
    }

    private static void DrawFrequencyScale(DrawingContext dc, Rect wave, Visual host)
    {
        var maxHertz = SpectrogramEngine.DisplayMaxHertz;
        var dpi = VisualTreeHelper.GetDpi(host).PixelsPerDip;
        var fore = WpfControlHelpers.FrozenBrush(Theme.Get("MutedForeBrush"));
        var grid = WpfControlHelpers.FrozenHairline(Color.FromArgb(26, 255, 255, 255), dpi);
        foreach (var mark in SpectrogramEngine.FrequencyMarks)
        {
            if (mark < SpectrogramEngine.MinHertz || mark > maxHertz * 1.001)
            {
                continue;
            }

            var unit = SpectrogramEngine.HertzToUnit(mark, SpectrogramEngine.MinHertz, maxHertz);
            var y = WpfControlHelpers.SnapDeviceCenter(wave.Y + (1 - unit) * wave.Height, dpi);
            dc.DrawLine(grid, new Point(wave.X, y), new Point(wave.Right, y));
            var text = new FormattedText(
                SpectrogramEngine.FormatHertz(mark),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                WpfControlHelpers.MonoTypeface,
                8,
                fore,
                dpi);
            var x = wave.Right - text.Width - 4;
            var ty = Math.Clamp(y - text.Height * 0.5, wave.Y, wave.Bottom - text.Height);
            dc.DrawText(text, new Point(x, ty));
        }
    }
}
