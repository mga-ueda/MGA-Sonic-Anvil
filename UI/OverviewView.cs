using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.UI;

internal sealed class OverviewView : FrameworkElement
{
    private AudioDocument? _document;
    private WriteableBitmap? _bitmap;
    private WriteableBitmap? _invertBitmap;
    private int[] _invertPixels = [];
    private float[] _mins = [];
    private float[] _maxs = [];
    private bool _waveDirty = true;
    private double _viewStart;
    private double _viewSpan = 1;
    private bool _dragging;
    private double _dragOffsetX;
    private int _waveBgra;
    private int _zeroBgra;
    private HashSet<long>? _selectedMarkerFrames;

    public event EventHandler<double>? ViewStartChanged;

    public event EventHandler? DragEnded;

    public bool IsDragging => _dragging;

    public OverviewView()
    {
        ClipToBounds = true;
        Focusable = false;
        SnapsToDevicePixels = true;
        Cursor = Cursors.Hand;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        SizeChanged += (_, _) =>
        {
            _waveDirty = true;
            InvalidateVisual();
        };
    }

    public AudioDocument? Document
    {
        get => _document;
        set
        {
            _document = value;
            _viewStart = 0;
            _viewSpan = value?.FrameCount ?? 1;
            _selectedMarkerFrames = null;
            _waveDirty = true;
            InvalidateVisual();
        }
    }

    public double FrameAt(double x)
    {
        if (_document is null || ActualWidth <= 0)
        {
            return 0;
        }

        return Math.Clamp(x / ActualWidth * _document.FrameCount, 0, _document.FrameCount);
    }

    public void Refresh()
    {
        _waveDirty = true;
        InvalidateVisual();
    }

    public void RefreshAppearance()
    {
        _waveBgra = 0;
        _zeroBgra = 0;
        _waveDirty = true;
        InvalidateVisual();
    }

    public void SetSelectedMarkerFrames(IReadOnlyCollection<long>? frames)
    {
        if (frames is null || frames.Count == 0)
        {
            if (_selectedMarkerFrames is null)
            {
                return;
            }

            _selectedMarkerFrames = null;
            InvalidateVisual();
            return;
        }

        _selectedMarkerFrames = new HashSet<long>(frames);
        InvalidateVisual();
    }

    public void SetView(double viewStart, double viewSpan)
    {
        if (Math.Abs(viewStart - _viewStart) < 0.0001 && Math.Abs(viewSpan - _viewSpan) < 0.0001)
        {
            return;
        }

        _viewStart = viewStart;
        _viewSpan = Math.Max(1, viewSpan);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(RenderSize);
        dc.DrawRectangle(WpfControlHelpers.FrozenBrush(Theme.Get("WaveformBackBrush")), null, bounds);
        if (_document is null || _document.FrameCount <= 0 || bounds.Width <= 1 || bounds.Height <= 1)
        {
            return;
        }

        MarkerRolePaint.DrawRegion(dc, _document, bounds, 0, _document.FrameCount, "RegionWaveFillBrush");
        MarkerRolePaint.DrawSampleLoop(dc, _document, bounds, 0, _document.FrameCount, "SampleLoopWaveFillBrush");
        MarkerRolePaint.DrawBackgrounds(dc, _document, bounds, 0, _document.FrameCount);
        EnsureWaveform(bounds);
        if (_bitmap is not null)
        {
            dc.DrawImage(_bitmap, bounds);
        }

        MarkerRolePaint.DrawRemoveOverlays(dc, _document, bounds, 0, _document.FrameCount);

        DrawInvertedSelection(dc, bounds);
        DrawVisibleWindow(dc, bounds);
        DrawMarkerLines(dc, bounds);
        DrawRegionLines(dc, bounds);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_document is null || ActualWidth <= 1)
        {
            return;
        }

        if (!CaptureMouse())
        {
            return;
        }

        _dragging = true;
        var x = e.GetPosition(this).X;
        var window = VisibleWindow(ActualWidth);
        if (x >= window.X && x <= window.X + window.Width)
        {
            _dragOffsetX = x - window.X;
        }
        else
        {
            _dragOffsetX = window.Width * 0.5;
            ApplyViewFromWindowLeft(x - _dragOffsetX);
        }

        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging || !IsMouseCaptured)
        {
            return;
        }

        ApplyViewFromWindowLeft(e.GetPosition(this).X - _dragOffsetX);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        EndDrag(notify: true);
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        EndDrag(notify: true);
        base.OnLostMouseCapture(e);
    }

    public void CancelDrag() => EndDrag(notify: false);

    private void EndDrag(bool notify)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        if (notify)
        {
            DragEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ApplyViewFromWindowLeft(double windowLeft)
    {
        if (_document is null || ActualWidth <= 1)
        {
            return;
        }

        var frames = (double)_document.FrameCount;
        var span = Math.Min(_viewSpan, frames);
        var start = windowLeft / ActualWidth * frames;
        start = Math.Clamp(start, 0, Math.Max(0, frames - span));
        if (Math.Abs(start - _viewStart) < 0.0001)
        {
            return;
        }

        _viewStart = start;
        InvalidateVisual();
        ViewStartChanged?.Invoke(this, start);
    }

    private Rect VisibleWindow(double width)
    {
        if (_document is null || _document.FrameCount <= 0)
        {
            return new Rect(0, 0, width, ActualHeight);
        }

        var frames = (double)_document.FrameCount;
        var x = _viewStart / frames * width;
        var w = Math.Max(2, _viewSpan / frames * width);
        x = Math.Clamp(x, 0, Math.Max(0, width - 2));
        if (x + w > width)
        {
            w = width - x;
        }

        return new Rect(x, 0, w, ActualHeight);
    }

    private void DrawInvertedSelection(DrawingContext dc, Rect bounds)
    {
        if (_document is null
            || _document.Selection.IsEmpty
            || _invertBitmap is null
            || _document.FrameCount <= 0)
        {
            return;
        }

        var frames = (double)_document.FrameCount;
        var x0 = _document.Selection.StartFrame / frames * bounds.Width;
        var x1 = _document.Selection.EndFrame / frames * bounds.Width;
        if (x1 < 0 || x0 > bounds.Width)
        {
            return;
        }

        x0 = Math.Clamp(x0, 0, bounds.Width);
        x1 = Math.Clamp(x1, 0, bounds.Width);
        dc.PushClip(new RectangleGeometry(new Rect(x0, 0, Math.Max(1, x1 - x0), bounds.Height)));
        dc.DrawImage(_invertBitmap, bounds);
        dc.Pop();
    }

    private void DrawMarkerLines(DrawingContext dc, Rect bounds)
    {
        if (_document is null || _document.Markers.Count == 0 || _document.FrameCount <= 0)
        {
            return;
        }

        var frames = (double)_document.FrameCount;
        var pen = new Pen(WpfControlHelpers.FrozenBrush(Theme.Get("MarkerBrush")), 1);
        pen.Freeze();
        var selectedPen = new Pen(WpfControlHelpers.FrozenBrush(Theme.Get("MarkerSelectedBorderBrush")), 1.5);
        selectedPen.Freeze();
        foreach (var marker in _document.Markers)
        {
            var x = marker.Frame / frames * bounds.Width;
            if (x < -1 || x > bounds.Width + 1)
            {
                continue;
            }

            var selected = _selectedMarkerFrames is not null && _selectedMarkerFrames.Contains(marker.Frame);
            dc.DrawLine(selected ? selectedPen : pen, new Point(x, 0), new Point(x, bounds.Height));
        }
    }

    private void DrawRegionLines(DrawingContext dc, Rect bounds)
    {
        if (_document is null || _document.Regions.Count == 0 || _document.FrameCount <= 0)
        {
            return;
        }

        var frames = (double)_document.FrameCount;
        var pen = new Pen(WpfControlHelpers.FrozenBrush(Theme.Get("RegionTimelineBrush")), 1);
        pen.Freeze();
        foreach (var region in _document.Regions)
        {
            DrawRegionLine(dc, bounds, frames, pen, region.StartFrame);
            DrawRegionLine(dc, bounds, frames, pen, region.EndFrame);
        }
    }

    private static void DrawRegionLine(DrawingContext dc, Rect bounds, double frames, Pen pen, long frame)
    {
        var x = frame / frames * bounds.Width;
        if (x < -1 || x > bounds.Width + 1)
        {
            return;
        }

        dc.DrawLine(pen, new Point(x, 0), new Point(x, bounds.Height));
    }

    private void DrawVisibleWindow(DrawingContext dc, Rect bounds)
    {
        var window = VisibleWindow(bounds.Width);
        if (window.Width >= bounds.Width - 0.5)
        {
            return;
        }

        var dim = WpfControlHelpers.FrozenBrush(Color.FromArgb(150, 0, 0, 0));
        if (window.X > 0.5)
        {
            dc.DrawRectangle(dim, null, new Rect(0, 0, window.X, bounds.Height));
        }

        var right = window.X + window.Width;
        if (right < bounds.Width - 0.5)
        {
            dc.DrawRectangle(dim, null, new Rect(right, 0, bounds.Width - right, bounds.Height));
        }
    }

    private void EnsureWaveform(Rect bounds)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = Math.Max(1, (int)Math.Round(bounds.Width * dpi.DpiScaleX));
        var height = Math.Max(1, (int)Math.Round(bounds.Height * dpi.DpiScaleY));
        if (!_waveDirty
            && _bitmap is not null
            && _bitmap.PixelWidth == width
            && _bitmap.PixelHeight == height)
        {
            return;
        }

        RebuildWaveform(width, height, dpi);
        _waveDirty = false;
    }

    private void RebuildWaveform(int width, int height, DpiScale dpi)
    {
        var document = _document!;
        if (_bitmap is null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
        {
            _bitmap = new WriteableBitmap(
                width,
                height,
                dpi.PixelsPerInchX,
                dpi.PixelsPerInchY,
                PixelFormats.Bgra32,
                null);
        }

        EnsureColors();
        if (_mins.Length < width)
        {
            _mins = new float[width];
            _maxs = new float[width];
        }

        var channels = Math.Max(1, document.Channels);
        var packedMin = new float[width * channels];
        var packedMax = new float[width * channels];
        var count = document.Peaks.ReadRangePacked(0, document.FrameCount, width, packedMin, packedMax);
        for (var i = 0; i < width; i++)
        {
            var min = 0f;
            var max = 0f;
            if (count > 0)
            {
                var bucket = count == width
                    ? i
                    : (int)Math.Clamp((long)i * count / width, 0, count - 1);
                min = float.MaxValue;
                max = float.MinValue;
                var src = bucket * channels;
                for (var ch = 0; ch < channels; ch++)
                {
                    var lo = packedMin[src + ch];
                    var hi = packedMax[src + ch];
                    if (lo < min)
                    {
                        min = lo;
                    }

                    if (hi > max)
                    {
                        max = hi;
                    }
                }

                if (min > max)
                {
                    min = 0;
                    max = 0;
                }
            }

            _mins[i] = min;
            _maxs[i] = max;
        }

        var mid = height * 0.5;
        var amp = height * 0.48;
        _bitmap.Lock();
        try
        {
            unsafe
            {
                var buffer = (int*)_bitmap.BackBuffer;
                var stride = _bitmap.BackBufferStride / 4;
                new Span<int>(buffer, stride * height).Clear();
                var yMid = (int)Math.Round(mid);
                if ((uint)yMid < (uint)height)
                {
                    var row = buffer + yMid * stride;
                    for (var x = 0; x < width; x++)
                    {
                        row[x] = _zeroBgra;
                    }
                }

                for (var x = 0; x < width; x++)
                {
                    var y1 = (int)Math.Floor(Math.Clamp(mid - _maxs[x] * amp, 0, height - 1));
                    var y2 = (int)Math.Ceiling(Math.Clamp(mid - _mins[x] * amp, 0, height - 1));
                    if (y2 < y1)
                    {
                        (y1, y2) = (y2, y1);
                    }

                    if (y2 <= y1)
                    {
                        y2 = Math.Min(height - 1, y1 + 1);
                    }

                    var p = buffer + y1 * stride + x;
                    for (var y = y1; y <= y2; y++)
                    {
                        *p = _waveBgra;
                        p += stride;
                    }
                }
            }

            _bitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
        }
        finally
        {
            _bitmap.Unlock();
        }

        RebuildInvertBitmap(width, height, dpi);
    }

    private void RebuildInvertBitmap(int width, int height, DpiScale dpi)
    {
        if (_bitmap is null)
        {
            return;
        }

        if (_invertBitmap is null
            || _invertBitmap.PixelWidth != width
            || _invertBitmap.PixelHeight != height)
        {
            _invertBitmap = new WriteableBitmap(
                width,
                height,
                dpi.PixelsPerInchX,
                dpi.PixelsPerInchY,
                PixelFormats.Bgra32,
                null);
        }

        var needed = width * height;
        if (_invertPixels.Length < needed)
        {
            _invertPixels = new int[needed];
        }

        _bitmap.CopyPixels(new Int32Rect(0, 0, width, height), _invertPixels, width * 4, 0);
        WaveformInvertPaint.RebuildInPlace(
            _invertPixels,
            width,
            height,
            _document,
            viewStart: 0,
            viewSpan: _document?.FrameCount ?? 0);
        _invertBitmap.WritePixels(new Int32Rect(0, 0, width, height), _invertPixels, width * 4, 0);
    }

    private void EnsureColors()
    {
        if (_waveBgra != 0)
        {
            return;
        }

        _waveBgra = ToBgra(Theme.Get("WaveFillBrush"));
        _zeroBgra = ToBgra(Theme.Get("WaveZeroLineBrush"));
    }

    private static int ToBgra(Color color) =>
        color.B | (color.G << 8) | (color.R << 16) | (color.A << 24);
}
