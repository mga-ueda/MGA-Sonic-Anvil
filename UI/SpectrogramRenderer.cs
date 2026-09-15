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
    private ushort[] _units = [];
    private readonly int[] _boostMap = new int[SpectrogramEngine.LinearUnitScale + 1];
    private float _mapBoostDb = float.NaN;
    private bool _hasUnits;
    private double[] _rowHertz = [];
    private int _rowHeight;
    private int[] _rowBin0 = [];
    private int[] _rowBin1 = [];
    private float[] _rowBinT = [];
    private bool[] _rowSilent = [];
    private int _rowBinsHeight;
    private int _rowBinsRate;
    private readonly byte[] _cacheCol0 = new byte[SpectrogramEngine.BinCount];
    private readonly byte[] _cacheCol1 = new byte[SpectrogramEngine.BinCount];
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
    private bool _drewSharp;
    private float[]? _gainSamples;
    private float _gainDb;
    private float _displayBoostDb;
    private float _appliedBoostDb;

    public SpectrogramRenderer()
    {
        SpectrogramEngine.FillHann(_window, out _windowSum);
    }

    public float DisplayBoostDb
    {
        get => _displayBoostDb;
        set
        {
            var next = SpectrogramEngine.ClampDisplayBoostDb(value);
            if (Math.Abs(next - _displayBoostDb) < 0.01f)
            {
                return;
            }

            _displayBoostDb = next;
        }
    }

    public event Action? InvalidateRequested;

    public void RequestCache(AudioDocument document, Action onUpdated) =>
        _cache.Ensure(document, onUpdated);

    public void InvalidateCache()
    {
        _cache.Invalidate();
        // 編集はサンプル配列を同じ参照のまま書き換えるので、参照比較の早期リターンに
        // 頼らず、次回の描画で必ず再ラスタライズさせる。表示用ゲインも測り直す。
        _samples = null;
        _gainSamples = null;
        _hasUnits = false;
        _mapBoostDb = float.NaN;
    }

    public void Dispose() => _cache.Dispose();

    public void Draw(
        DrawingContext dc,
        Rect wave,
        AudioDocument document,
        double viewStart,
        double viewSpan,
        Visual host,
        double imageOpacity = 1,
        bool reuseBitmap = false)
    {
        if (wave.Width <= 1 || wave.Height <= 1 || document.FrameCount <= 0)
        {
            return;
        }

        _cache.Ensure(document, () => host.Dispatcher.BeginInvoke(() => InvalidateRequested?.Invoke()));
        if (!reuseBitmap || _bitmap is null)
        {
            EnsureBitmap(wave, document, viewStart, viewSpan, UiDpi.Get(host));
        }
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
        // ズーム中は幅 1920px のビットマップを引き伸ばすので従来どおり Fant で滑らかに。
        // フィット表示のライブ FFT は 1:1 なので補間せずシャープに描く。
        RenderOptions.SetBitmapScalingMode(
            group,
            _drewSharp ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.Fant);
        var context = group.Open();
        context.DrawImage(_bitmap, dest);
        context.Close();
        dc.PushClip(new RectangleGeometry(wave));
        dc.DrawDrawing(group);
        dc.Pop();
    }

    private void EnsureBitmap(Rect wave, AudioDocument document, double viewStart, double viewSpan, DpiScale dpi)
    {
        var widthFull = Math.Max(1, (int)Math.Round(wave.Width * Math.Max(1e-6, dpi.DpiScaleX)));
        var height = Math.Clamp((int)Math.Round(wave.Height * Math.Max(1e-6, dpi.DpiScaleY)), 1, MaxPixelHeight);
        // フィット表示（シャープに描く）だけ等倍。ズーム中は従来どおり幅を 1920px に抑え、
        // 追従スクロールの列生成と転送を軽く保つ（Fant で引き伸ばすので見た目は滑らかなまま）。
        var sharp = SpectrogramEngine.PreferLiveFft(
            viewSpan / widthFull, viewSpan, document.FrameCount);
        var width = sharp ? widthFull : Math.Min(widthFull, MaxPixelWidth);
        // ビュー開始位置を 1 デバイス px = framesPerPx の格子に量子化する。
        // スクロールが常に整数 px 差になるため、列シフト＋差分 FFT が毎回効く。
        // 右端の欠けを防ぐため 1 列余分に持つ。
        var framesPerPx = viewSpan / width;
        var quantStart = Math.Floor(viewStart / framesPerPx) * framesPerPx;
        var bmpWidth = width + 1;
        var bmpSpan = bmpWidth * framesPerPx;
        var fromCache = _cache.IsReady && !sharp;
        var sameView = ReferenceEquals(_samples, document.Interleaved)
            && _hasUnits
            && _bitmap is not null
            && _bitmap.PixelWidth == bmpWidth
            && _bitmap.PixelHeight == height
            && Math.Abs(_dpiX - dpi.DpiScaleX) < 0.001
            && Math.Abs(_dpiY - dpi.DpiScaleY) < 0.001
            && Math.Abs(_viewStart - quantStart) < framesPerPx * 0.01
            && Math.Abs(_viewSpan - bmpSpan) < framesPerPx * 0.01
            && _sampleRate == document.SampleRate
            && _drewFromCache == fromCache
            && _drewSharp == sharp;
        if (sameView && Math.Abs(_appliedBoostDb - _displayBoostDb) < 0.01f)
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

        if (_units.Length < needed)
        {
            _units = new ushort[needed];
        }

        EnsureRowHertz(height);
        if (!ReferenceEquals(_gainSamples, document.Interleaved))
        {
            // 表示用ノーマライズ。読み込み・編集の後に一度だけピークを測る（描画ごとには追従しない）。
            _gainDb = SpectrogramEngine.NormalizeGainDb(document.Interleaved);
            _gainSamples = document.Interleaved;
        }

        var mapDirty = float.IsNaN(_mapBoostDb) || Math.Abs(_mapBoostDb - _displayBoostDb) >= 0.01f;
        if (mapDirty)
        {
            SpectrogramEngine.FillBoostLinearMap(_boostMap, _displayBoostDb);
            _mapBoostDb = _displayBoostDb;
        }

        // シフト再利用できた列はピクセルも一緒にずらしてあるので、ブースト（LUT 適用）は
        // 差分列だけでよい。ブースト値が変わったときだけ全列を塗り直す。
        var dirtyX0 = 0;
        var dirtyX1 = bmpWidth;
        if (!sameView)
        {
            if (TryShiftColumns(document, quantStart, bmpSpan, bmpWidth, height, fromCache, out var freshX0, out var freshX1))
            {
                if (!mapDirty && Math.Abs(_appliedBoostDb - _displayBoostDb) < 0.01f)
                {
                    dirtyX0 = freshX0;
                    dirtyX1 = freshX1;
                }
            }
            else
            {
                RasterizeSpan(document, quantStart, bmpSpan, bmpWidth, height, 0, bmpWidth, fromCache);
            }

            _hasUnits = true;
        }

        ApplyBoostToPixels(dirtyX0, dirtyX1, bmpWidth, height);
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
        _drewSharp = sharp;
        _appliedBoostDb = _displayBoostDb;
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
        bool fromCache,
        out int freshX0,
        out int freshX1)
    {
        freshX0 = 0;
        freshX1 = width;
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

        ShiftColumns(width, height, shiftPx);
        if (shiftPx > 0)
        {
            freshX0 = width - shiftPx;
            freshX1 = width;
        }
        else
        {
            freshX0 = 0;
            freshX1 = -shiftPx;
        }

        RasterizeSpan(document, viewStart, viewSpan, width, height, freshX0, freshX1, fromCache);
        return true;
    }

    private void ShiftColumns(int width, int height, int shiftPx)
    {
        if (shiftPx > 0)
        {
            for (var y = 0; y < height; y++)
            {
                var row = y * width;
                Array.Copy(_units, row + shiftPx, _units, row, width - shiftPx);
                Array.Copy(_pixels, row + shiftPx, _pixels, row, width - shiftPx);
            }

            return;
        }

        var left = -shiftPx;
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            Array.Copy(_units, row, _units, row + left, width - left);
            Array.Copy(_pixels, row, _pixels, row + left, width - left);
        }
    }

    private void ApplyBoostToPixels(int x0, int x1, int width, int height)
    {
        x0 = Math.Clamp(x0, 0, width);
        x1 = Math.Clamp(x1, x0, width);
        if (x1 <= x0)
        {
            return;
        }

        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = x0; x < x1; x++)
            {
                _pixels[row + x] = _boostMap[_units[row + x]];
            }
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
        // 画素ごとの TryLinearUnit（ロック＋メモリマップ読み×4＋二分探索）は
        // 深いズームの追従スクロールで支配的なコストになる。列ごとに LUT 列を
        // 一括で読み、行のビン補間係数は事前計算表で済ませる。
        EnsureRowBins(height, sampleRate);
        var unitLut = SpectrogramEngine.LinearUnitFromLutTable;
        for (var x = x0; x < x1; x++)
        {
            var center = (long)Math.Round(viewStart + (x + 0.5) / width * viewSpan);
            if (!_cache.TryReadColumnPair(center, _cacheCol0, _cacheCol1, out var txd))
            {
                for (var y = 0; y < height; y++)
                {
                    _units[y * width + x] = 0;
                }

                continue;
            }

            var tx = (float)txd;
            var row = 0;
            for (var y = 0; y < height; y++)
            {
                if (_rowSilent[y])
                {
                    _units[row + x] = 0;
                    row += width;
                    continue;
                }

                var b0 = _rowBin0[y];
                var b1 = _rowBin1[y];
                var ty = _rowBinT[y];
                var u00 = unitLut[_cacheCol0[b0]];
                var u01 = unitLut[_cacheCol0[b1]];
                var u10 = unitLut[_cacheCol1[b0]];
                var u11 = unitLut[_cacheCol1[b1]];
                var u0 = u00 + (u01 - u00) * ty;
                var u1 = u10 + (u11 - u10) * ty;
                var unit = u0 + (u1 - u0) * tx;
                _units[row + x] = (ushort)(unit * SpectrogramEngine.LinearUnitScale + 0.5f);
                row += width;
            }
        }
    }

    /// <summary>行→FFT ビンの補間係数。高さとサンプルレートが変わったときだけ作り直す。</summary>
    private void EnsureRowBins(int height, int sampleRate)
    {
        EnsureRowHertz(height);
        if (_rowBinsHeight == height && _rowBinsRate == sampleRate)
        {
            return;
        }

        if (_rowBin0.Length < height)
        {
            _rowBin0 = new int[height];
            _rowBin1 = new int[height];
            _rowBinT = new float[height];
            _rowSilent = new bool[height];
        }

        var binHz = Math.Max(1, sampleRate) / (double)SpectrogramEngine.FftSize;
        var nyquist = SpectrogramEngine.ContentNyquist(sampleRate);
        var lastBin = SpectrogramEngine.BinCount - 1;
        for (var y = 0; y < height; y++)
        {
            var hz = _rowHertz[y];
            if (hz > nyquist)
            {
                _rowSilent[y] = true;
                _rowBin0[y] = 0;
                _rowBin1[y] = 0;
                _rowBinT[y] = 0;
                continue;
            }

            _rowSilent[y] = false;
            var binF = Math.Clamp(hz / binHz, 0, lastBin);
            var b0 = (int)Math.Floor(binF);
            _rowBin0[y] = b0;
            _rowBin1[y] = Math.Min(lastBin, b0 + 1);
            _rowBinT[y] = (float)(binF - b0);
        }

        _rowBinsHeight = height;
        _rowBinsRate = sampleRate;
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

        for (var x = x0; x < x1; x++)
        {
            var center = (long)Math.Round(viewStart + (x + 0.5) / width * viewSpan);
            SpectrogramEngine.FillMonoMix(samples, channels, center - half, frames, _mix);
            SpectrogramEngine.AnalyzeWindow(_mix, _window, _windowSum, _re, _im);
            var row = 0;
            for (var y = 0; y < height; y++)
            {
                var hz = _rowHertz[y];
                _units[row + x] = hz > nyquist
                    ? (ushort)0
                    : SpectrogramEngine.LinearUnitFromMagnitude(
                        SpectrogramEngine.BinMagnitude(_re.AsSpan(0, bins), hz / binHz),
                        _gainDb);
                row += width;
            }
        }
    }

    private readonly Dictionary<string, (FormattedText Fill, FormattedText Outline)> _scaleLabels =
        new(StringComparer.Ordinal);

    private double _scaleLabelDpi;
    private Color _scaleLabelColor;

    private void DrawFrequencyScale(DrawingContext dc, Rect wave, Visual host)
    {
        var maxHertz = SpectrogramEngine.DisplayMaxHertz;
        var dpi = UiDpi.Get(host).PixelsPerDip;
        var fillColor = Theme.Get("SpectrogramScaleForeBrush");
        // FormattedText の生成（文字整形）は毎ペイントだと高くつく。ラベルは固定なのでキャッシュする。
        if (Math.Abs(dpi - _scaleLabelDpi) > 0.001 || fillColor != _scaleLabelColor)
        {
            _scaleLabels.Clear();
            _scaleLabelDpi = dpi;
            _scaleLabelColor = fillColor;
        }

        var fill = WpfControlHelpers.FrozenBrush(fillColor);
        var edge = WpfControlHelpers.FrozenBrush(FrequencyLabelEdge);
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
            var label = SpectrogramEngine.FormatHertz(mark);
            if (!_scaleLabels.TryGetValue(label, out var texts))
            {
                texts = (
                    WpfControlHelpers.MonoText(label, 8, fill, dpi),
                    WpfControlHelpers.MonoText(label, 8, edge, dpi));
                _scaleLabels[label] = texts;
            }

            var x = wave.Right - texts.Fill.Width - 4;
            var ty = Math.Clamp(y - texts.Fill.Height * 0.5, wave.Y, wave.Bottom - texts.Fill.Height);
            DrawHaloLabel(dc, texts.Outline, texts.Fill, new Point(x, ty));
        }
    }

    internal static Color FrequencyLabelFill => Color.FromRgb(0xEB, 0xEB, 0xEB);

    internal static Color FrequencyLabelEdge => Colors.Black;

    internal static readonly (double X, double Y)[] FrequencyLabelHalo =
    [
        (-1, 0), (1, 0), (0, -1), (0, 1),
        (-1, -1), (1, -1), (-1, 1), (1, 1),
    ];

    private static void DrawHaloLabel(
        DrawingContext dc,
        FormattedText outline,
        FormattedText fill,
        Point origin)
    {
        foreach (var (dx, dy) in FrequencyLabelHalo)
        {
            dc.DrawText(outline, new Point(origin.X + dx, origin.Y + dy));
        }

        dc.DrawText(fill, origin);
    }
}
