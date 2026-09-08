using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

internal enum TransportIcon
{
    PlayPause,
    Stop,
    GoToStart,
    GoToEnd,
    TimeZoomIn,
    TimeZoomOut,
    TimeZoomMax,
    TimeZoomReset,
    AmpZoomIn,
    AmpZoomOut,
    AmpZoomMax,
    AmpZoomReset,
    Open,
    Folder,
    FadeIn,
    FadeOut,
    Normalize,
    Delete,
    Save,
    Lock,
    Unlock,
    Waapi,
}

internal enum TransportCommand
{
    TogglePlayback,
    Stop,
    GoToStart,
    GoToEnd,
    TimeZoomIn,
    TimeZoomOut,
    TimeZoomMax,
    TimeZoomReset,
    AmpZoomIn,
    AmpZoomOut,
    AmpZoomMax,
    AmpZoomReset,
    Open,
    FadeIn,
    FadeOut,
    Normalize,
    Delete,
    Save,
    ToggleWaapi,
}

internal sealed class TransportIconButton : Button
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon),
        typeof(TransportIcon),
        typeof(TransportIconButton),
        new FrameworkPropertyMetadata(TransportIcon.PlayPause, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsPlayingProperty = DependencyProperty.Register(
        nameof(IsPlaying),
        typeof(bool),
        typeof(TransportIconButton),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsLatchedProperty = DependencyProperty.Register(
        nameof(IsLatched),
        typeof(bool),
        typeof(TransportIconButton),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public TransportCommand CommandKind { get; init; }

    public TransportIcon Icon
    {
        get => (TransportIcon)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public bool IsPlaying
    {
        get => (bool)GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    public bool IsLatched
    {
        get => (bool)GetValue(IsLatchedProperty);
        set => SetValue(IsLatchedProperty, value);
    }

    public bool QuietChrome { get; set; }

    public Color? IconForeOverride { get; set; }

    public TransportIconButton()
    {
        Width = DesignMetrics.TransportButtonSide;
        Height = DesignMetrics.TransportButtonSide;
        Focusable = false;
        FocusVisualStyle = null;
        Cursor = Cursors.Hand;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        OverridesDefaultStyle = true;
        Template = new ControlTemplate(typeof(Button));
        SnapsToDevicePixels = true;
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) =>
        new Rect(RenderSize).Contains(hitTestParameters.HitPoint)
            ? new PointHitTestResult(this, hitTestParameters.HitPoint)
            : null;

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(RenderSize);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        if (Icon == TransportIcon.Waapi)
        {
            DrawWaapiToggle(dc, bounds);
            return;
        }

        var backColor = QuietChrome
            ? Theme.Get("WaapiBarBackBrush")
            : Theme.Get("TransportBackBrush");
        dc.DrawRectangle(WpfControlHelpers.FrozenBrush(backColor), null, bounds);
        if (IsEnabled && (IsMouseOver || IsPressed || IsLatched))
        {
            var hover = IsPressed
                ? Theme.Get("TransportPressedBackBrush")
                : Theme.Get("TransportHoverBackBrush");
            dc.DrawRectangle(WpfControlHelpers.FrozenBrush(hover), null, new Rect(3, 3, bounds.Width - 6, bounds.Height - 6));
        }

        var fore = IconForeOverride
            ?? (!IsEnabled
                ? Theme.Get("TransportDisabledForeBrush")
                : Icon == TransportIcon.PlayPause && IsPlaying
                    ? Theme.Get("AccentCyanBrush")
                    : IsLatched
                        ? Theme.Get("AccentCyanBrush")
                        : Theme.Get("TransportForeBrush"));
        TransportIconDrawing.Draw(dc, Icon, bounds, fore, IsPlaying);
    }

    private void DrawWaapiToggle(DrawingContext dc, Rect bounds)
    {
        var on = IsLatched;
        var hover = IsEnabled && IsMouseOver && !IsPressed;
        var back = on
            ? Theme.Get(hover ? "WaapiToggleOnHoverBackBrush" : "WaapiToggleOnBackBrush")
            : Theme.Get(hover ? "WaapiToggleOffHoverBackBrush" : "WaapiToggleOffBackBrush");
        var fore = on
            ? Theme.Get("WaapiToggleOnForeBrush")
            : Theme.Get("WaapiToggleOffForeBrush");

        dc.DrawRectangle(WpfControlHelpers.FrozenBrush(Theme.Get("TransportBackBrush")), null, bounds);
        var chip = new Rect(2, 6, Math.Max(1, bounds.Width - 4), Math.Max(1, bounds.Height - 12));
        dc.DrawRoundedRectangle(WpfControlHelpers.FrozenBrush(back), null, chip, 3, 3);

        var formatted = new FormattedText(
            UiStrings.WaapiTitle,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            10,
            WpfControlHelpers.FrozenBrush(fore),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(
            formatted,
            new Point(
                (bounds.Width - formatted.Width) * 0.5,
                (bounds.Height - formatted.Height) * 0.5));
    }
}

internal static class TransportIconDrawing
{
    public static void Draw(DrawingContext dc, TransportIcon icon, Rect bounds, Color fore, bool isPlaying)
    {
        const double designW = 34d;
        const double designH = 36d;
        var scale = Math.Min(bounds.Width / designW, bounds.Height / designH);
        if (scale <= 0d)
        {
            return;
        }

        dc.PushTransform(new TranslateTransform(
            (bounds.Width - designW * scale) * 0.5,
            (bounds.Height - designH * scale) * 0.5));
        dc.PushTransform(new ScaleTransform(scale, scale));

        var brush = WpfControlHelpers.FrozenBrush(fore);
        var pen = new Pen(brush, 1.8)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();
        const double cx = 17d;
        const double cy = 18d;

        switch (icon)
        {
            case TransportIcon.PlayPause:
                if (isPlaying)
                {
                    dc.DrawRectangle(brush, null, new Rect(12, 11, 4, 14));
                    dc.DrawRectangle(brush, null, new Rect(19, 11, 4, 14));
                }
                else
                {
                    var play = new StreamGeometry();
                    using (var ctx = play.Open())
                    {
                        ctx.BeginFigure(new Point(12, 9), true, true);
                        ctx.LineTo(new Point(25, 18), true, false);
                        ctx.LineTo(new Point(12, 27), true, false);
                    }

                    play.Freeze();
                    dc.DrawGeometry(brush, null, play);
                }

                break;
            case TransportIcon.Stop:
                dc.DrawRectangle(brush, null, new Rect(12, 12, 10, 12));
                break;
            case TransportIcon.GoToStart:
            case TransportIcon.GoToEnd:
                var start = icon == TransportIcon.GoToStart;
                var lineX = start ? 9d : 25d;
                dc.DrawLine(pen, new Point(lineX, 9), new Point(lineX, 27));
                DrawChevron(dc, pen, cx + (start ? 2 : -2), cy, start);
                break;
            case TransportIcon.TimeZoomIn:
            case TransportIcon.TimeZoomOut:
            case TransportIcon.TimeZoomMax:
            case TransportIcon.TimeZoomReset:
                DrawHorizontalZoom(dc, pen);
                DrawZoomModifier(dc, pen, brush, icon, cx, cy);
                break;
            case TransportIcon.AmpZoomIn:
            case TransportIcon.AmpZoomOut:
            case TransportIcon.AmpZoomMax:
            case TransportIcon.AmpZoomReset:
                DrawVerticalZoom(dc, pen);
                DrawZoomModifier(dc, pen, brush, icon, cx, cy);
                break;
            case TransportIcon.Open:
            case TransportIcon.Folder:
                dc.DrawLine(pen, new Point(9, 12), new Point(9, 10));
                dc.DrawLine(pen, new Point(9, 10), new Point(15, 10));
                dc.DrawLine(pen, new Point(15, 10), new Point(17, 12));
                dc.DrawLine(pen, new Point(17, 12), new Point(25, 12));
                dc.DrawLine(pen, new Point(25, 12), new Point(25, 26));
                dc.DrawLine(pen, new Point(25, 26), new Point(9, 26));
                dc.DrawLine(pen, new Point(9, 26), new Point(9, 12));
                dc.DrawLine(pen, new Point(9, 15), new Point(25, 15));
                break;
            case TransportIcon.FadeIn:
                dc.DrawLine(pen, new Point(8, 26), new Point(26, 10));
                dc.DrawLine(pen, new Point(8, 26), new Point(8, 10));
                break;
            case TransportIcon.FadeOut:
                dc.DrawLine(pen, new Point(8, 10), new Point(26, 26));
                dc.DrawLine(pen, new Point(26, 26), new Point(26, 10));
                break;
            case TransportIcon.Normalize:
                dc.DrawLine(pen, new Point(10, 24), new Point(10, 16));
                dc.DrawLine(pen, new Point(15, 24), new Point(15, 11));
                dc.DrawLine(pen, new Point(20, 24), new Point(20, 18));
                dc.DrawLine(pen, new Point(25, 24), new Point(25, 13));
                dc.DrawLine(pen, new Point(8, 10), new Point(26, 10));
                dc.DrawLine(pen, new Point(22, 8), new Point(26, 10));
                dc.DrawLine(pen, new Point(22, 12), new Point(26, 10));
                break;
            case TransportIcon.Delete:
                dc.DrawLine(pen, new Point(9, 13), new Point(25, 13));
                dc.DrawLine(pen, new Point(14, 10), new Point(20, 10));
                dc.DrawLine(pen, new Point(11, 13), new Point(12, 26));
                dc.DrawLine(pen, new Point(12, 26), new Point(22, 26));
                dc.DrawLine(pen, new Point(22, 26), new Point(23, 13));
                dc.DrawLine(pen, new Point(14, 16), new Point(14, 23));
                dc.DrawLine(pen, new Point(17, 16), new Point(17, 23));
                dc.DrawLine(pen, new Point(20, 16), new Point(20, 23));
                break;
            case TransportIcon.Save:
                dc.DrawLine(pen, new Point(17, 7), new Point(17, 20));
                dc.DrawLine(pen, new Point(12, 16), new Point(17, 21));
                dc.DrawLine(pen, new Point(22, 16), new Point(17, 21));
                dc.DrawLine(pen, new Point(9, 26), new Point(25, 26));
                break;
            case TransportIcon.Lock:
                DrawPadlockBody(dc, pen, brush);
                DrawPadlockShackle(dc, pen, open: false);
                break;
            case TransportIcon.Unlock:
                DrawPadlockBody(dc, pen, brush);
                DrawPadlockShackle(dc, pen, open: true);
                break;
        }

        dc.Pop();
        dc.Pop();
    }

    private static void DrawPadlockBody(DrawingContext dc, Pen pen, Brush brush)
    {
        dc.DrawRectangle(null, pen, new Rect(10, 16, 14, 11));
        dc.DrawEllipse(brush, null, new Point(17, 20), 1.4, 1.4);
        dc.DrawLine(pen, new Point(17, 21.4), new Point(17, 24.2));
    }

    private static void DrawPadlockShackle(DrawingContext dc, Pen pen, bool open)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            if (open)
            {
                ctx.BeginFigure(new Point(12.5, 16), false, false);
                ctx.LineTo(new Point(12.5, 11.5), true, false);
                ctx.ArcTo(new Point(22, 11.5), new Size(4.75, 4.75), 0, false, SweepDirection.Clockwise, true, false);
                ctx.LineTo(new Point(22, 13.5), true, false);
            }
            else
            {
                ctx.BeginFigure(new Point(12.5, 16), false, false);
                ctx.LineTo(new Point(12.5, 12.5), true, false);
                ctx.ArcTo(new Point(21.5, 12.5), new Size(4.5, 4.5), 0, false, SweepDirection.Clockwise, true, false);
                ctx.LineTo(new Point(21.5, 16), true, false);
            }
        }

        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }

    private static void DrawChevron(DrawingContext dc, Pen pen, double centerX, double centerY, bool left)
    {
        var direction = left ? -1d : 1d;
        dc.DrawLine(pen, new Point(centerX - direction * 4, centerY - 7), new Point(centerX + direction * 3, centerY));
        dc.DrawLine(pen, new Point(centerX + direction * 3, centerY), new Point(centerX - direction * 4, centerY + 7));
    }

    private static void DrawHorizontalZoom(DrawingContext dc, Pen pen)
    {
        dc.DrawLine(pen, new Point(7, 18), new Point(27, 18));
        dc.DrawLine(pen, new Point(11, 14), new Point(7, 18));
        dc.DrawLine(pen, new Point(7, 18), new Point(11, 22));
        dc.DrawLine(pen, new Point(23, 14), new Point(27, 18));
        dc.DrawLine(pen, new Point(27, 18), new Point(23, 22));
    }

    private static void DrawVerticalZoom(DrawingContext dc, Pen pen)
    {
        dc.DrawLine(pen, new Point(17, 8), new Point(17, 28));
        dc.DrawLine(pen, new Point(13, 12), new Point(17, 8));
        dc.DrawLine(pen, new Point(17, 8), new Point(21, 12));
        dc.DrawLine(pen, new Point(13, 24), new Point(17, 28));
        dc.DrawLine(pen, new Point(17, 28), new Point(21, 24));
    }

    private static void DrawZoomModifier(
        DrawingContext dc,
        Pen pen,
        Brush brush,
        TransportIcon icon,
        double cx,
        double cy)
    {
        var isIn = icon is TransportIcon.TimeZoomIn or TransportIcon.AmpZoomIn;
        var isOut = icon is TransportIcon.TimeZoomOut or TransportIcon.AmpZoomOut;
        var isMax = icon is TransportIcon.TimeZoomMax or TransportIcon.AmpZoomMax;
        if (isIn || isOut)
        {
            var badge = WpfControlHelpers.FrozenBrush(Theme.Get("ChromeBackBrush"));
            dc.DrawEllipse(badge, null, new Point(cx, cy), 5, 5);
            dc.DrawLine(pen, new Point(cx - 3, cy), new Point(cx + 3, cy));
            if (isIn)
            {
                dc.DrawLine(pen, new Point(cx, cy - 3), new Point(cx, cy + 3));
            }
        }
        else if (isMax)
        {
            dc.DrawRectangle(brush, null, new Rect(cx - 3, cy - 3, 6, 6));
        }
        else
        {
            dc.DrawEllipse(null, pen, new Point(cx, cy), 4, 4);
        }
    }
}

/// <summary>表示言語切替。JP／EN を描画するトランスポートサイズのボタン。</summary>
internal sealed class TransportLanguageButton : Button
{
    public TransportLanguageButton()
    {
        Width = DesignMetrics.TransportButtonSide;
        Height = DesignMetrics.TransportButtonSide;
        Focusable = false;
        FocusVisualStyle = null;
        Cursor = Cursors.Hand;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        OverridesDefaultStyle = true;
        Template = new ControlTemplate(typeof(Button));
        SnapsToDevicePixels = true;
        ToolTip = LanguageTip();
    }

    public void RefreshAppearance()
    {
        ToolTip = LanguageTip();
        InvalidateVisual();
    }

    private static string LanguageTip() =>
        UiStrings.IsJapanese ? UiStrings.TipLanguageJapanese : UiStrings.TipLanguageEnglish;

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) =>
        new Rect(RenderSize).Contains(hitTestParameters.HitPoint)
            ? new PointHitTestResult(this, hitTestParameters.HitPoint)
            : null;

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(RenderSize);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        TransportChrome.Paint(dc, bounds, IsEnabled, IsMouseOver, IsPressed);
        var label = UiStrings.IsJapanese
            ? UiStrings.LanguageBadgeJapanese
            : UiStrings.LanguageBadgeEnglish;
        var formatted = new FormattedText(
            label,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            10,
            WpfControlHelpers.FrozenBrush(TransportChrome.Fore(IsEnabled)),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(
            formatted,
            new Point((bounds.Width - formatted.Width) * 0.5, (bounds.Height - formatted.Height) * 0.5));
    }
}

/// <summary>ユーザーマニュアルを開く。「?」を描画する。</summary>
internal sealed class TransportManualButton : Button
{
    public TransportManualButton()
    {
        Width = DesignMetrics.TransportButtonSide;
        Height = DesignMetrics.TransportButtonSide;
        Focusable = false;
        FocusVisualStyle = null;
        Cursor = Cursors.Hand;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        OverridesDefaultStyle = true;
        Template = new ControlTemplate(typeof(Button));
        SnapsToDevicePixels = true;
        ToolTip = UiStrings.TipManualHelp;
    }

    public void RefreshAppearance()
    {
        ToolTip = UiStrings.TipManualHelp;
        InvalidateVisual();
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) =>
        new Rect(RenderSize).Contains(hitTestParameters.HitPoint)
            ? new PointHitTestResult(this, hitTestParameters.HitPoint)
            : null;

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(RenderSize);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        TransportChrome.Paint(dc, bounds, IsEnabled, IsMouseOver, IsPressed);
        var formatted = new FormattedText(
            "?",
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Semibold"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            Math.Max(12d, Math.Min(bounds.Width, bounds.Height) * 0.42),
            WpfControlHelpers.FrozenBrush(TransportChrome.Fore(IsEnabled)),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(
            formatted,
            new Point((bounds.Width - formatted.Width) * 0.5, (bounds.Height - formatted.Height) * 0.5));
    }
}

internal static class TransportChrome
{
    public static void Paint(DrawingContext dc, Rect bounds, bool enabled, bool hover, bool pressed)
    {
        dc.DrawRectangle(WpfControlHelpers.FrozenBrush(Theme.Get("TransportBackBrush")), null, bounds);
        if (enabled && (hover || pressed))
        {
            var fill = pressed
                ? Theme.Get("TransportPressedBackBrush")
                : Theme.Get("TransportHoverBackBrush");
            dc.DrawRectangle(
                WpfControlHelpers.FrozenBrush(fill),
                null,
                new Rect(3, 3, bounds.Width - 6, bounds.Height - 6));
        }
    }

    public static Color Fore(bool enabled) =>
        enabled ? Theme.Get("TransportForeBrush") : Theme.Get("TransportDisabledForeBrush");
}
