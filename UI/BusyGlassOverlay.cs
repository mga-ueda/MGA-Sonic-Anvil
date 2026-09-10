using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.UI;

/// <summary>
/// 重い処理中にクライアント領域全体を覆うすりガラス。Wwise IM Importer と同じフロスト合成。
/// </summary>
internal sealed class BusyGlassOverlay : FrameworkElement
{
    private const int MaxDots = 3;
    private const int FadeOutDurationMs = 300;
    private const double ProgressBarWidth = 280;
    private const double ProgressBarHeight = 4;
    private const double JobNameWidth = 168;
    private const double JobBarWidth = 100;
    private const double JobPctWidth = 40;
    private const double JobRowWidth = JobNameWidth + 8 + JobBarWidth + 8 + JobPctWidth;

    private readonly DispatcherTimer _dotsTimer;
    private readonly DispatcherTimer _fadeTimer;
    private ImageBrush? _frostedBrush;
    private string _baseText = string.Empty;
    private string _statusText = string.Empty;
    private ExportJobProgress[] _jobs = [];
    private int _dotCount = 1;
    private int _percent = -1;
    private bool _fading;
    private long _fadeStartTickMs;
    private float _fadeStartOpacity = 1f;
    private float _paintOpacity = 1f;
    private Panel? _host;

    public BusyGlassOverlay()
    {
        Focusable = false;
        Visibility = Visibility.Collapsed;
        IsHitTestVisible = true;

        _dotsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _dotsTimer.Tick += (_, _) =>
        {
            _dotCount = _dotCount % MaxDots + 1;
            InvalidateVisual();
        };
        _fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _fadeTimer.Tick += (_, _) => AdvanceFade();
    }

    public bool IsShowingBusy => Visibility == Visibility.Visible && !_fading;

    public void ShowOverlay(Panel host, FrameworkElement captureSource, Rect coverBounds, string baseText)
    {
        CancelFade();
        EnsureParent(host, coverBounds);

        _frostedBrush = CaptureFrostedBrush(captureSource, coverBounds);
        _baseText = NormalizeMessage(baseText);
        _statusText = string.Empty;
        _jobs = [];
        _dotCount = 1;
        _percent = 0;
        _paintOpacity = 1f;

        Visibility = Visibility.Visible;
        IsHitTestVisible = true;
        InvalidateVisual();
        Panel.SetZIndex(this, int.MaxValue);
        _dotsTimer.Start();
    }

    public void SyncBounds(Rect coverBounds)
    {
        if (!IsShowingBusy || _host is null)
        {
            return;
        }

        ApplyBounds(coverBounds);
        InvalidateVisual();
    }

    public void SetProgress(double progress)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SetProgress(progress));
            return;
        }

        var percent = (int)Math.Round(Math.Clamp(progress, 0d, 1d) * 100);
        if (percent == _percent)
        {
            return;
        }

        _percent = percent;
        InvalidateVisual();
    }

    public void SetExportView(double overall, string status, IReadOnlyList<ExportJobProgress> running)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => SetExportView(overall, status, running));
            return;
        }

        _percent = (int)Math.Round(Math.Clamp(overall, 0d, 1d) * 100);
        _statusText = status ?? string.Empty;
        _jobs = running.Count == 0 ? [] : running.ToArray();

        InvalidateVisual();
    }

    public void BeginFadeOut()
    {
        if (Visibility != Visibility.Visible || _fading)
        {
            return;
        }

        _dotsTimer.Stop();
        IsHitTestVisible = false;
        _fading = true;
        _fadeStartOpacity = _paintOpacity;
        _fadeStartTickMs = Environment.TickCount64;
        _fadeTimer.Start();
    }

    public void HideOverlay()
    {
        CancelFade();
        _dotsTimer.Stop();
        _paintOpacity = 1f;
        Visibility = Visibility.Collapsed;
        _frostedBrush = null;
        if (_host?.Children.Contains(this) == true)
        {
            _host.Children.Remove(this);
        }

        _host = null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Visibility != Visibility.Visible)
        {
            return;
        }

        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        var opacity = Math.Clamp(_paintOpacity, 0f, 1f);

        if (_frostedBrush is not null)
        {
            dc.PushOpacity(opacity);
            dc.DrawRectangle(_frostedBrush, null, bounds);
            dc.Pop();
        }
        else
        {
            var tint = Theme.Get("SurfaceBackBrush");
            dc.DrawRectangle(
                WpfControlHelpers.FrozenBrush(Color.FromArgb((byte)Math.Round(255 * opacity), tint.R, tint.G, tint.B)),
                null,
                bounds);
        }

        var tintOverlay = Theme.Get("SurfaceBackBrush");
        dc.DrawRectangle(
            WpfControlHelpers.FrozenBrush(
                Color.FromArgb((byte)Math.Round(140 * opacity), tintOverlay.R, tintOverlay.G, tintOverlay.B)),
            null,
            bounds);

        DrawMessage(dc, opacity);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        double.IsInfinity(availableSize.Width) || double.IsInfinity(availableSize.Height)
            ? new Size(0, 0)
            : availableSize;

    protected override Size ArrangeOverride(Size finalSize) => finalSize;

    private void EnsureParent(Panel host, Rect coverBounds)
    {
        if (!ReferenceEquals(_host, host))
        {
            _host?.Children.Remove(this);
            _host = host;
        }

        if (host.Children.Contains(this))
        {
            host.Children.Remove(this);
        }

        host.Children.Add(this);
        Panel.SetZIndex(this, int.MaxValue);
        ApplyBounds(coverBounds);
    }

    private void ApplyBounds(Rect coverBounds)
    {
        Width = coverBounds.Width;
        Height = coverBounds.Height;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(coverBounds.Left, coverBounds.Top, 0, 0);
    }

    private static string NormalizeMessage(string baseText) =>
        string.IsNullOrWhiteSpace(baseText) ? string.Empty : baseText.Trim();

    private void AdvanceFade()
    {
        var progress = Math.Clamp((Environment.TickCount64 - _fadeStartTickMs) / (float)FadeOutDurationMs, 0f, 1f);
        _paintOpacity = _fadeStartOpacity * (1f - progress);
        InvalidateVisual();
        if (progress >= 1f)
        {
            HideOverlay();
        }
    }

    private void CancelFade()
    {
        _fadeTimer.Stop();
        _fading = false;
        _paintOpacity = 1f;
        if (Visibility == Visibility.Visible)
        {
            IsHitTestVisible = true;
        }
    }

    private void DrawMessage(DrawingContext dc, float opacity)
    {
        var typeface = WpfControlHelpers.UiBoldTypeface;
        var regular = WpfControlHelpers.UiTypeface;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var fore = WpfControlHelpers.FrozenBrush(Theme.Get("PrimaryForeBrush"));
        var culture = CultureInfo.CurrentUICulture;
        var baseFormatted = new FormattedText(
            _baseText,
            culture,
            FlowDirection.LeftToRight,
            typeface,
            15,
            fore,
            dpi);
        var dotsReserve = new FormattedText(
            " " + new string('.', MaxDots),
            culture,
            FlowDirection.LeftToRight,
            typeface,
            15,
            fore,
            dpi);
        var dotsText = " " + new string('.', _dotCount);
        var dotsFormatted = new FormattedText(
            dotsText,
            culture,
            FlowDirection.LeftToRight,
            typeface,
            15,
            fore,
            dpi);

        var statusText = _statusText;
        var percentText = _percent >= 0
            ? (string.IsNullOrEmpty(statusText) ? $"{_percent}%" : $"{statusText}  {_percent}%")
            : statusText;
        var percentFormatted = new FormattedText(
            percentText,
            culture,
            FlowDirection.LeftToRight,
            typeface,
            13,
            fore,
            dpi);

        var headerHeight = baseFormatted.Height
            + (percentText.Length > 0 ? 8 + percentFormatted.Height : 0)
            + 16 + ProgressBarHeight;
        var jobsGap = _jobs.Length > 0 ? 14d : 0d;
        var availableJobsHeight = Math.Max(
            BusyGlassJobLayout.MinRowHeight,
            ActualHeight - headerHeight - jobsGap - 24);
        var layout = BusyGlassJobLayout.Arrange(
            _jobs.Length,
            Math.Max(0, ActualWidth - 32),
            availableJobsHeight,
            JobRowWidth);
        var jobsHeight = layout.Rows * layout.RowHeight;
        var blockHeight = headerHeight + jobsGap + jobsHeight;
        var x = (ActualWidth - (baseFormatted.Width + dotsReserve.Width)) / 2;
        var y = (ActualHeight - blockHeight) / 2;

        dc.PushOpacity(opacity);
        dc.DrawText(baseFormatted, new Point(x, y));
        dc.DrawText(dotsFormatted, new Point(x + baseFormatted.Width, y));

        var barY = y + baseFormatted.Height;
        if (percentText.Length > 0)
        {
            var percentX = (ActualWidth - percentFormatted.Width) / 2;
            var percentY = y + baseFormatted.Height + 8;
            dc.DrawText(percentFormatted, new Point(percentX, percentY));
            barY = percentY + percentFormatted.Height + 12;
        }
        else
        {
            barY += 16;
        }

        DrawBar(dc, (ActualWidth - ProgressBarWidth) / 2, barY, ProgressBarWidth, Math.Clamp(_percent, 0, 100) / 100d, opacity);

        if (_jobs.Length > 0)
        {
            var gridWidth = layout.Columns * JobRowWidth
                + Math.Max(0, layout.Columns - 1) * BusyGlassJobLayout.ColumnGap;
            var gridX = (ActualWidth - gridWidth) / 2;
            var rowY = barY + ProgressBarHeight + 14;
            for (var i = 0; i < _jobs.Length; i++)
            {
                var col = i / layout.Rows;
                var row = i % layout.Rows;
                DrawJobRow(
                    dc,
                    _jobs[i],
                    gridX + col * (JobRowWidth + BusyGlassJobLayout.ColumnGap),
                    rowY + row * layout.RowHeight,
                    opacity,
                    regular,
                    culture,
                    dpi);
            }
        }

        dc.Pop();
    }

    private void DrawJobRow(
        DrawingContext dc,
        ExportJobProgress job,
        double x,
        double y,
        float opacity,
        Typeface typeface,
        CultureInfo culture,
        double dpi)
    {
        var name = FitText(job.Name, JobNameWidth, typeface, 12, culture, dpi);
        var nameText = new FormattedText(name, culture, FlowDirection.LeftToRight, typeface, 12,
            WpfControlHelpers.FrozenBrush(Theme.Get("PrimaryForeBrush")), dpi);
        dc.DrawText(nameText, new Point(x, y));

        var barY = y + Math.Max(0, (nameText.Height - ProgressBarHeight) / 2);
        DrawBar(dc, x + JobNameWidth + 8, barY, JobBarWidth, Math.Clamp(job.Progress, 0, 1), opacity);

        var pct = $"{(int)Math.Round(Math.Clamp(job.Progress, 0, 1) * 100)}%";
        var pctText = new FormattedText(pct, culture, FlowDirection.LeftToRight, typeface, 12,
            WpfControlHelpers.FrozenBrush(Theme.Get("PrimaryForeBrush")), dpi);
        dc.DrawText(pctText, new Point(x + JobNameWidth + 8 + JobBarWidth + 8, y));
    }

    private void DrawBar(DrawingContext dc, double x, double y, double width, double fill, float opacity)
    {
        var muted = Theme.Get("MutedForeBrush");
        dc.DrawRoundedRectangle(
            WpfControlHelpers.FrozenBrush(Color.FromArgb((byte)Math.Round(90 * opacity), muted.R, muted.G, muted.B)),
            null,
            new Rect(x, y, width, ProgressBarHeight),
            2,
            2);
        var fillWidth = width * Math.Clamp(fill, 0, 1);
        if (fillWidth > 0)
        {
            var accent = Theme.Get("AccentCyanBrush");
            dc.DrawRoundedRectangle(
                WpfControlHelpers.FrozenBrush(
                    Color.FromArgb((byte)Math.Round(255 * opacity), accent.R, accent.G, accent.B)),
                null,
                new Rect(x, y, fillWidth, ProgressBarHeight),
                2,
                2);
        }
    }

    private static string FitText(
        string text,
        double maxWidth,
        Typeface typeface,
        double fontSize,
        CultureInfo culture,
        double dpi)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var full = new FormattedText(text, culture, FlowDirection.LeftToRight, typeface, fontSize, Brushes.White, dpi);
        if (full.Width <= maxWidth)
        {
            return text;
        }

        for (var n = text.Length - 1; n > 1; n--)
        {
            var cut = text[..n] + "…";
            var trial = new FormattedText(cut, culture, FlowDirection.LeftToRight, typeface, fontSize, Brushes.White, dpi);
            if (trial.Width <= maxWidth)
            {
                return cut;
            }
        }

        return "…";
    }

    private static ImageBrush? CaptureFrostedBrush(FrameworkElement captureSource, Rect coverBounds)
    {
        if (coverBounds.Width <= 0 || coverBounds.Height <= 0)
        {
            return null;
        }

        try
        {
            captureSource.UpdateLayout();
            var fullW = Math.Max(1, (int)Math.Ceiling(captureSource.ActualWidth));
            var fullH = Math.Max(1, (int)Math.Ceiling(captureSource.ActualHeight));
            if (fullW <= 1 || fullH <= 1)
            {
                return null;
            }

            var rtb = new RenderTargetBitmap(fullW, fullH, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(captureSource);

            var cropX = Math.Clamp((int)Math.Floor(coverBounds.X), 0, fullW - 1);
            var cropY = Math.Clamp((int)Math.Floor(coverBounds.Y), 0, fullH - 1);
            var cropW = Math.Clamp((int)Math.Ceiling(coverBounds.Width), 1, fullW - cropX);
            var cropH = Math.Clamp((int)Math.Ceiling(coverBounds.Height), 1, fullH - cropY);

            BitmapSource source = rtb;
            if (cropX != 0 || cropY != 0 || cropW != fullW || cropH != fullH)
            {
                var cropped = new CroppedBitmap(rtb, new Int32Rect(cropX, cropY, cropW, cropH));
                cropped.Freeze();
                source = cropped;
            }

            var scaled = ScaleBitmap(source, Math.Max(1, cropW / 6), Math.Max(1, cropH / 6));
            var tiny = ScaleBitmap(scaled, Math.Max(1, cropW / 20), Math.Max(1, cropH / 20));
            var brush = new ImageBrush(tiny)
            {
                Stretch = Stretch.Fill,
                Opacity = 1,
            };
            brush.Freeze();
            return brush;
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource ScaleBitmap(BitmapSource source, int width, int height)
    {
        var scaled = new TransformedBitmap(
            source,
            new ScaleTransform(
                width / (double)source.PixelWidth,
                height / (double)source.PixelHeight));
        scaled.Freeze();
        return scaled;
    }
}

/// <summary>すりガラス上の並列ジョブ行。高さに収まらなければ列を増やす。</summary>
internal static class BusyGlassJobLayout
{
    public const double DefaultRowHeight = 20;
    public const double MinRowHeight = 16;
    public const double ColumnGap = 16;
    public const int MaxColumns = 3;

    public static (int Columns, int Rows, double RowHeight) Arrange(
        int jobCount,
        double availableWidth,
        double availableHeight,
        double rowWidth,
        double preferredRowHeight = DefaultRowHeight)
    {
        if (jobCount <= 0)
        {
            return (1, 0, preferredRowHeight);
        }

        var maxByWidth = 1;
        if (rowWidth > 0 && availableWidth > 0)
        {
            maxByWidth = Math.Max(1, (int)((availableWidth + ColumnGap) / (rowWidth + ColumnGap)));
            maxByWidth = Math.Min(MaxColumns, maxByWidth);
        }

        var columns = 1;
        var rows = jobCount;
        var rowHeight = preferredRowHeight;
        while (columns < maxByWidth && rows * rowHeight > availableHeight)
        {
            columns++;
            rows = (int)Math.Ceiling(jobCount / (double)columns);
        }

        if (rows > 0 && rows * rowHeight > availableHeight)
        {
            rowHeight = Math.Max(MinRowHeight, availableHeight / rows);
        }

        return (columns, rows, rowHeight);
    }
}
