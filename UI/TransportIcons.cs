using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

internal enum TransportIcon
{
    PlayPause,
    Stop,
    Record,
    JumpToTime,
    GoToStart,
    PreviousPage,
    PreviousMarker,
    NextMarker,
    NextPage,
    GoToEnd,
    TimeZoomIn,
    TimeZoomOut,
    TimeZoomMax,
    TimeZoomReset,
    AmpZoomIn,
    AmpZoomOut,
    AmpZoomMax,
    AmpZoomReset,
    WaveformHeight,
    Folder,
    FadeIn,
    FadeOut,
    FadeAround,
    Normalize,
    Volume,
    Pitch,
    TimeStretch,
    Reverse,
    Delete,
    Undo,
    Redo,
    AddMarker,
    SetLoop,
    SetRegion,
    Save,
    SaveAs,
    SaveMp3,
    Analysis,
    Overlay,
    Loudness,
    Center,
    History,
    ThemeSun,
    ThemeMoon,
    Tips,
    Settings,
    Help,
    Lock,
    Unlock,
    Waapi,
}

internal enum TransportCommand
{
    TogglePlayback,
    Stop,
    Record,
    JumpToTime,
    GoToStart,
    PreviousPage,
    PreviousMarker,
    NextMarker,
    NextPage,
    GoToEnd,
    TimeZoomIn,
    TimeZoomOut,
    TimeZoomMax,
    TimeZoomReset,
    AmpZoomIn,
    AmpZoomOut,
    AmpZoomMax,
    AmpZoomReset,
    CycleWaveformHeight,
    FadeIn,
    FadeOut,
    FadeAround,
    Normalize,
    Volume,
    Pitch,
    TimeStretch,
    Reverse,
    Delete,
    Undo,
    Redo,
    AddMarker,
    SetLoop,
    SetRegion,
    Open,
    Save,
    SaveAs,
    SaveMp3,
    ToggleAnalysis,
    ToggleSpectrogram,
    ToggleLoudnessView,
    CenterPlayhead,
    History,
    ToggleUiTheme,
    ToggleTips,
    OpenSettings,
    OpenManual,
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

    private int _waveformHeightScale = 1;

    public int WaveformHeightScale
    {
        get => _waveformHeightScale;
        set
        {
            var next = value is >= 1 and <= 3 ? value : 1;
            if (_waveformHeightScale == next)
            {
                return;
            }

            _waveformHeightScale = next;
            InvalidateVisual();
        }
    }

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
        TransportHover.Attach(this);
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

        var backKey = QuietChrome ? "ProjectBarBackBrush" : "TransportBackBrush";
        TransportChrome.Paint(
            dc,
            bounds,
            IsEnabled,
            IsMouseOver,
            IsPressed,
            backKey,
            fillSlot: !QuietChrome,
            hoverBounds: QuietChrome && Icon == TransportIcon.Folder
                ? TransportIconDrawing.FolderHoverBounds(bounds)
                : null);
        var fore = IconForeOverride ?? TransportChrome.Fore(IsEnabled);
        if (Icon == TransportIcon.WaveformHeight)
        {
            DrawWaveformHeightLabel(dc, bounds, fore, WaveformHeightScale);
            return;
        }

        if (Icon == TransportIcon.Tips)
        {
            var balloonFore = IsLatched ? fore : TransportChrome.Fore(false);
            var hole = ChromeHole(backKey);
            TransportIconDrawing.DrawTipsBalloon(
                dc,
                bounds,
                balloonFore,
                hole,
                outline: true);
            return;
        }

        if (Icon == TransportIcon.Help)
        {
            DrawHelpQuestion(dc, bounds, fore);
            return;
        }
        if (Icon == TransportIcon.Record && IsLatched)
        {
            fore = Color.FromRgb(0xE2, 0x4B, 0x4A);
        }

        if (Icon is TransportIcon.Analysis or TransportIcon.Overlay or TransportIcon.Loudness)
        {
            fore = IsLatched ? fore : TransportChrome.Fore(false);
        }

        TransportIconDrawing.Draw(dc, Icon, bounds, fore, IsPlaying, UiThemeService.Current);
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

        var slotKey = QuietChrome ? "ProjectBarBackBrush" : "TransportBackBrush";
        dc.DrawRectangle(WpfControlHelpers.FrozenBrush(Theme.Get(slotKey)), null, bounds);
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

    private Color ChromeHole(string backKey)
    {
        if (IsEnabled && (IsMouseOver || IsPressed))
        {
            return Theme.Get(IsPressed ? "TransportPressedBackBrush" : "TransportHoverBackBrush");
        }

        return Theme.Get(backKey);
    }

    private void DrawHelpQuestion(DrawingContext dc, Rect bounds, Color fore)
    {
        var formatted = new FormattedText(
            "?",
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Semibold"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            Math.Max(12d, Math.Min(bounds.Width, bounds.Height) * 0.52),
            WpfControlHelpers.FrozenBrush(fore),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(
            formatted,
            new Point((bounds.Width - formatted.Width) * 0.5, (bounds.Height - formatted.Height) * 0.5));
    }

    private void DrawWaveformHeightLabel(DrawingContext dc, Rect bounds, Color fore, int scale)
    {
        scale = scale is >= 1 and <= 3 ? scale : 1;
        var formatted = new FormattedText(
            "x" + scale.ToString(CultureInfo.InvariantCulture),
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
    public static void Draw(
        DrawingContext dc,
        TransportIcon icon,
        Rect bounds,
        Color fore,
        bool isPlaying,
        UiTheme theme)
    {
        var designW = bounds.Width < 28 ? 28d : 34d;
        var designH = bounds.Height < 28 ? 30d : 36d;
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
        _ = theme;
        Brush? fill = null;
        var stroke = pen;
        var cx = designW * 0.5;
        var cy = designH * 0.5;

        switch (icon)
        {
            case TransportIcon.PlayPause:
                if (isPlaying)
                {
                    dc.DrawRectangle(fill, stroke, new Rect(12, 12, 10, 12));
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
                    dc.DrawGeometry(fill, stroke, play);
                }

                break;
            case TransportIcon.Stop:
                dc.DrawRectangle(fill, stroke, new Rect(12, 12, 10, 12));
                break;
            case TransportIcon.Record:
                dc.DrawEllipse(fill, stroke, new Point(cx, cy), 6.2, 6.2);
                break;
            case TransportIcon.JumpToTime:
                dc.DrawLine(pen, new Point(11, 11), new Point(23, 11));
                dc.DrawLine(pen, new Point(11, 25), new Point(23, 25));
                dc.DrawLine(pen, new Point(14, 9), new Point(12, 27));
                dc.DrawLine(pen, new Point(22, 9), new Point(20, 27));
                break;
            case TransportIcon.GoToStart:
            case TransportIcon.GoToEnd:
                var start = icon == TransportIcon.GoToStart;
                var lineX = start ? 9d : 25d;
                dc.DrawLine(pen, new Point(lineX, 9), new Point(lineX, 27));
                DrawChevron(dc, pen, cx + (start ? 2 : -2), cy, start);
                break;
            case TransportIcon.PreviousMarker:
            case TransportIcon.NextMarker:
                var prevMarker = icon == TransportIcon.PreviousMarker;
                DrawChevron(dc, pen, cx + (prevMarker ? 3 : -3), cy, prevMarker);
                dc.DrawLine(pen, new Point(prevMarker ? 10 : 24, 10), new Point(prevMarker ? 10 : 24, 26));
                dc.DrawLine(pen, new Point(prevMarker ? 13 : 21, 13), new Point(prevMarker ? 13 : 21, 23));
                break;
            case TransportIcon.PreviousPage:
            case TransportIcon.NextPage:
                var prevPage = icon == TransportIcon.PreviousPage;
                DrawChevron(dc, pen, cx + (prevPage ? -2 : 2), cy, prevPage);
                DrawChevron(dc, pen, cx + (prevPage ? 5 : -5), cy, prevPage);
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
            case TransportIcon.Folder:
                DrawFolder(dc, pen, cx, cy);
                break;
            case TransportIcon.Settings:
                DrawGear(dc, pen, cx, cy);
                break;
            case TransportIcon.FadeIn:
                FadeCurveIcons.DrawCurve(dc, FadeShape.SCurve, isFadeIn: true, new Rect(8, 10, 18, 16), pen);
                break;
            case TransportIcon.FadeOut:
                FadeCurveIcons.DrawCurve(dc, FadeShape.SCurve, isFadeIn: false, new Rect(8, 10, 18, 16), pen);
                break;
            case TransportIcon.FadeAround:
                dc.DrawLine(pen, new Point(8, 12), new Point(17, 26));
                dc.DrawLine(pen, new Point(17, 26), new Point(26, 12));
                break;
            case TransportIcon.Volume:
                dc.DrawLine(pen, new Point(9, 16), new Point(14, 16));
                dc.DrawLine(pen, new Point(14, 16), new Point(19, 11));
                dc.DrawLine(pen, new Point(19, 11), new Point(19, 25));
                dc.DrawLine(pen, new Point(19, 25), new Point(14, 20));
                dc.DrawLine(pen, new Point(14, 20), new Point(9, 20));
                dc.DrawLine(pen, new Point(9, 20), new Point(9, 16));
                dc.DrawLine(pen, new Point(22, 14), new Point(25, 18));
                dc.DrawLine(pen, new Point(25, 18), new Point(22, 22));
                break;
            case TransportIcon.Pitch:
                dc.DrawEllipse(null, pen, new Point(13, 24), 3.2, 2.2);
                dc.DrawLine(pen, new Point(16.2, 24), new Point(16.2, 10));
                dc.DrawLine(pen, new Point(16.2, 10), new Point(23, 13));
                break;
            case TransportIcon.TimeStretch:
                dc.DrawLine(pen, new Point(8, 18), new Point(26, 18));
                dc.DrawLine(pen, new Point(8, 18), new Point(12, 14));
                dc.DrawLine(pen, new Point(8, 18), new Point(12, 22));
                dc.DrawLine(pen, new Point(26, 18), new Point(22, 14));
                dc.DrawLine(pen, new Point(26, 18), new Point(22, 22));
                dc.DrawLine(pen, new Point(14, 11), new Point(20, 11));
                dc.DrawLine(pen, new Point(14, 25), new Point(20, 25));
                break;
            case TransportIcon.Reverse:
                DrawChevron(dc, pen, 13, cy, left: true);
                DrawChevron(dc, pen, 22, cy, left: true);
                break;
            case TransportIcon.Undo:
                dc.DrawLine(pen, new Point(11, 14), new Point(11, 22));
                dc.DrawLine(pen, new Point(11, 22), new Point(23, 22));
                dc.DrawLine(pen, new Point(11, 14), new Point(16, 10));
                dc.DrawLine(pen, new Point(11, 14), new Point(16, 18));
                break;
            case TransportIcon.Redo:
                dc.DrawLine(pen, new Point(23, 14), new Point(23, 22));
                dc.DrawLine(pen, new Point(11, 22), new Point(23, 22));
                dc.DrawLine(pen, new Point(23, 14), new Point(18, 10));
                dc.DrawLine(pen, new Point(23, 14), new Point(18, 18));
                break;
            case TransportIcon.AddMarker:
                dc.DrawLine(pen, new Point(17, 8), new Point(17, 27));
                dc.DrawLine(pen, new Point(17, 8), new Point(25, 12));
                dc.DrawLine(pen, new Point(25, 12), new Point(17, 16));
                break;
            case TransportIcon.SetLoop:
                DrawCassetteAutoReverse(dc, pen);
                break;
            case TransportIcon.SetRegion:
                dc.DrawLine(pen, new Point(10, 10), new Point(10, 26));
                dc.DrawLine(pen, new Point(10, 10), new Point(14, 10));
                dc.DrawLine(pen, new Point(10, 26), new Point(14, 26));
                dc.DrawLine(pen, new Point(24, 10), new Point(24, 26));
                dc.DrawLine(pen, new Point(24, 10), new Point(20, 10));
                dc.DrawLine(pen, new Point(24, 26), new Point(20, 26));
                break;
            case TransportIcon.Analysis:
                DrawSpectrumBars(dc, pen);
                break;
            case TransportIcon.Overlay:
                DrawSpectrumBars(dc, pen);
                DrawWaveformOverlay(dc, pen);
                break;
            case TransportIcon.Loudness:
                DrawLoudnessCurve(dc, pen);
                break;
            case TransportIcon.Center:
                dc.DrawLine(pen, new Point(17, 9), new Point(17, 14));
                dc.DrawLine(pen, new Point(17, 22), new Point(17, 27));
                dc.DrawLine(pen, new Point(9, 18), new Point(14, 18));
                dc.DrawLine(pen, new Point(20, 18), new Point(25, 18));
                dc.DrawEllipse(null, pen, new Point(cx, cy), 4.5, 4.5);
                break;
            case TransportIcon.History:
                dc.DrawEllipse(null, pen, new Point(cx, cy), 8, 8);
                dc.DrawLine(pen, new Point(17, 13), new Point(17, 18));
                dc.DrawLine(pen, new Point(17, 18), new Point(21, 21));
                break;
            case TransportIcon.ThemeSun:
                DrawThemeSun(dc, pen, cx, cy);
                break;
            case TransportIcon.ThemeMoon:
                DrawThemeMoon(dc, pen, cx, cy);
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
            case TransportIcon.SaveMp3:
                DrawSaveMp3(dc, pen);
                break;
            case TransportIcon.SaveAs:
                dc.DrawLine(pen, new Point(17, 7), new Point(17, 18));
                dc.DrawLine(pen, new Point(12, 14), new Point(17, 19));
                dc.DrawLine(pen, new Point(22, 14), new Point(17, 19));
                dc.DrawLine(pen, new Point(9, 26), new Point(25, 26));
                dc.DrawLine(pen, new Point(21, 8), new Point(26, 8));
                dc.DrawLine(pen, new Point(21, 12), new Point(26, 12));
                break;
            case TransportIcon.Lock:
                DrawPadlock(dc, stroke, brush, open: false, designW * 0.5, designH * 0.5);
                break;
            case TransportIcon.Unlock:
                DrawPadlock(dc, stroke, brush, open: true, designW * 0.5, designH * 0.5);
                break;
        }

        dc.Pop();
        dc.Pop();
    }

    private static void DrawSpectrumBars(DrawingContext dc, Pen pen)
    {
        dc.DrawLine(pen, new Point(9, 24), new Point(9, 18));
        dc.DrawLine(pen, new Point(13, 24), new Point(13, 12));
        dc.DrawLine(pen, new Point(17, 24), new Point(17, 16));
        dc.DrawLine(pen, new Point(21, 24), new Point(21, 10));
        dc.DrawLine(pen, new Point(25, 24), new Point(25, 14));
    }

    private static void DrawWaveformOverlay(DrawingContext dc, Pen pen)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(8, 20), false, false);
            ctx.LineTo(new Point(11, 16), true, false);
            ctx.LineTo(new Point(14, 22), true, false);
            ctx.LineTo(new Point(17, 14), true, false);
            ctx.LineTo(new Point(20, 21), true, false);
            ctx.LineTo(new Point(23, 17), true, false);
            ctx.LineTo(new Point(26, 20), true, false);
        }

        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }

    private static readonly Rect FolderDesign = new(6, 9, 22, 18);

    internal static Rect FolderHoverBounds(Rect bounds)
    {
        const double chromePad = 3d;
        const double strokePad = 1d;
        var innerW = Math.Max(1, bounds.Width - chromePad * 2);
        var innerH = Math.Max(1, bounds.Height - chromePad * 2);
        var scale = Math.Min(innerW / FolderDesign.Width, innerH / FolderDesign.Height);
        var w = FolderDesign.Width * scale + strokePad * 2;
        var h = FolderDesign.Height * scale + strokePad * 2;
        return new Rect(
            bounds.X + (bounds.Width - w) * 0.5,
            bounds.Y + (bounds.Height - h) * 0.5,
            w,
            h);
    }

    private static void DrawFolder(DrawingContext dc, Pen pen, double cx, double cy)
    {
        var stroke = OutlinePen(pen, 1.25);
        var bounds = FolderDesign;
        dc.PushTransform(new TranslateTransform(
            cx - (bounds.X + bounds.Width * 0.5),
            cy - (bounds.Y + bounds.Height * 0.5)));
        dc.DrawLine(stroke, new Point(6, 12), new Point(6, 9));
        dc.DrawLine(stroke, new Point(6, 9), new Point(14, 9));
        dc.DrawLine(stroke, new Point(14, 9), new Point(16, 12));
        dc.DrawLine(stroke, new Point(16, 12), new Point(28, 12));
        dc.DrawLine(stroke, new Point(28, 12), new Point(28, 27));
        dc.DrawLine(stroke, new Point(28, 27), new Point(6, 27));
        dc.DrawLine(stroke, new Point(6, 27), new Point(6, 12));
        dc.DrawLine(stroke, new Point(6, 15), new Point(28, 15));
        dc.Pop();
    }

    private static void DrawThemeSun(DrawingContext dc, Pen pen, double cx, double cy)
    {
        dc.DrawEllipse(null, pen, new Point(cx, cy), 5.0, 5.0);
        for (var i = 0; i < 8; i++)
        {
            var angle = i * Math.PI / 4d;
            var inner = 7.0;
            var outer = 9.2;
            dc.DrawLine(
                pen,
                new Point(cx + Math.Cos(angle) * inner, cy + Math.Sin(angle) * inner),
                new Point(cx + Math.Cos(angle) * outer, cy + Math.Sin(angle) * outer));
        }
    }

    private static void DrawThemeMoon(DrawingContext dc, Pen pen, double cx, double cy)
    {
        const double bodyR = 7.4;
        const double cutR = 5.5;
        var body = new Point(cx, cy);
        var cut = new Point(cx + 2.8, cy - 2.8);
        var crescent = new CombinedGeometry(
            GeometryCombineMode.Exclude,
            new EllipseGeometry(body, bodyR, bodyR),
            new EllipseGeometry(cut, cutR, cutR));
        crescent.Freeze();
        var bounds = crescent.Bounds;
        if (!bounds.IsEmpty)
        {
            dc.PushTransform(new TranslateTransform(
                cx - (bounds.X + bounds.Width * 0.5),
                cy - (bounds.Y + bounds.Height * 0.5)));
        }

        dc.DrawGeometry(null, OutlinePen(pen, 1.45), crescent);
        if (!bounds.IsEmpty)
        {
            dc.Pop();
        }
    }

    private static void DrawLoudnessCurve(DrawingContext dc, Pen pen)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(8, 23), false, false);
            ctx.LineTo(new Point(12, 17), true, false);
            ctx.LineTo(new Point(16, 13), true, false);
            ctx.LineTo(new Point(20, 16), true, false);
            ctx.LineTo(new Point(24, 12), true, false);
            ctx.LineTo(new Point(27, 14), true, false);
        }

        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
        dc.DrawLine(pen, new Point(8, 20), new Point(27, 20));
    }

    public static void DrawSettingsGear(DrawingContext dc, Rect bounds, Color color, Color holeColor)
    {
        _ = holeColor;
        const double designW = 34d;
        const double designH = 36d;
        var scale = Math.Min(bounds.Width / designW, bounds.Height / designH);
        if (scale <= 0d)
        {
            return;
        }

        dc.PushTransform(new TranslateTransform(
            bounds.X + (bounds.Width - designW * scale) * 0.5,
            bounds.Y + (bounds.Height - designH * scale) * 0.5));
        dc.PushTransform(new ScaleTransform(scale, scale));
        var pen = new Pen(WpfControlHelpers.FrozenBrush(color), 1.8)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();
        DrawGear(dc, pen, 17, 18);
        dc.Pop();
        dc.Pop();
    }

    private static void DrawGear(DrawingContext dc, Pen pen, double cx, double cy)
    {
        const int teeth = 8;
        const double outer = 8.8;
        const double inner = 5.5;
        const double hole = 2.7;
        var step = Math.PI * 2d / teeth;
        var points = new Point[teeth * 4];
        for (var i = 0; i < teeth; i++)
        {
            var start = -Math.PI / 2d + (i * step) - (step * 0.5);
            points[i * 4] = GearPolar(cx, cy, inner, start + (step * 0.18));
            points[i * 4 + 1] = GearPolar(cx, cy, outer, start + (step * 0.32));
            points[i * 4 + 2] = GearPolar(cx, cy, outer, start + (step * 0.68));
            points[i * 4 + 3] = GearPolar(cx, cy, inner, start + (step * 0.82));
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], isFilled: false, isClosed: true);
            for (var i = 1; i < points.Length; i++)
            {
                ctx.LineTo(points[i], true, true);
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
        dc.DrawEllipse(null, pen, new Point(cx, cy), hole, hole);
    }

    private static Pen OutlinePen(Pen source, double thickness)
    {
        var pen = new Pen(source.Brush, thickness)
        {
            StartLineCap = source.StartLineCap,
            EndLineCap = source.EndLineCap,
            LineJoin = source.LineJoin,
        };
        pen.Freeze();
        return pen;
    }

    private static Point GearPolar(double cx, double cy, double radius, double angle) =>
        new(cx + Math.Cos(angle) * radius, cy + Math.Sin(angle) * radius);

    /// <summary>MGA Wwise IM Importer の TipsToggleButton.DrawBalloon と同じ吹き出し＋3点。</summary>
    public static void DrawTipsBalloon(
        DrawingContext dc,
        Rect bounds,
        Color color,
        Color holeColor,
        bool outline = true)
    {
        var side = Math.Min(bounds.Width, bounds.Height);
        if (side <= 0)
        {
            return;
        }

        var originX = bounds.X + (bounds.Width - side) * 0.5;
        var originY = bounds.Y + (bounds.Height - side) * 0.5;
        var w = side * 0.62;
        var h = side * 0.42;
        var x = originX + (side - w) / 2d;
        var y = originY + side * 0.24;
        var r = Math.Max(1.2, h * 0.36);
        var bottom = y + h;
        var tailLeft = x + w * 0.22;
        var tailRight = x + w * 0.40;
        var tailTip = new Point(x + w * 0.14, bottom + side * 0.11);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(x + r, y), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(x + w - r, y), true, false);
            ctx.ArcTo(new Point(x + w, y + r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
            ctx.LineTo(new Point(x + w, bottom - r), true, false);
            ctx.ArcTo(new Point(x + w - r, bottom), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
            ctx.LineTo(new Point(tailRight, bottom), true, false);
            ctx.LineTo(tailTip, true, false);
            ctx.LineTo(new Point(tailLeft, bottom), true, false);
            ctx.LineTo(new Point(x + r, bottom), true, false);
            ctx.ArcTo(new Point(x, bottom - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
            ctx.LineTo(new Point(x, y + r), true, false);
            ctx.ArcTo(new Point(x + r, y), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
        }

        geometry.Freeze();
        if (outline)
        {
            var pen = new Pen(WpfControlHelpers.FrozenBrush(color), Math.Max(1.2, side * 0.045))
            {
                LineJoin = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            };
            pen.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }
        else
        {
            dc.DrawGeometry(WpfControlHelpers.FrozenBrush(color), null, geometry);
        }

        var dot = Math.Max(1.5, side * 0.06);
        var dotY = y + h / 2d - dot / 2d;
        var dotBrush = WpfControlHelpers.FrozenBrush(outline ? color : holeColor);
        for (var i = 0; i < 3; i++)
        {
            var dotX = x + w * (0.26 + 0.24 * i) - dot / 2d;
            dc.DrawEllipse(
                dotBrush,
                null,
                new Point(dotX + dot / 2d, dotY + dot / 2d),
                dot / 2d,
                dot / 2d);
        }
    }

    private static void DrawPadlock(DrawingContext dc, Pen pen, Brush brush, bool open, double cx, double cy)
    {
        var body = new RectangleGeometry(new Rect(10, 16, 14, 11));
        var shackle = PadlockShackleGeometry(open);
        var bounds = Rect.Union(body.Bounds, shackle.Bounds);
        if (!bounds.IsEmpty)
        {
            dc.PushTransform(new TranslateTransform(
                cx - (bounds.X + bounds.Width * 0.5),
                cy - (bounds.Y + bounds.Height * 0.5)));
        }

        dc.DrawGeometry(null, pen, body);
        dc.DrawEllipse(brush, null, new Point(17, 20), 1.4, 1.4);
        dc.DrawLine(pen, new Point(17, 21.4), new Point(17, 24.2));
        dc.DrawGeometry(null, pen, shackle);
        if (!bounds.IsEmpty)
        {
            dc.Pop();
        }
    }

    private static StreamGeometry PadlockShackleGeometry(bool open)
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
        return geo;
    }

    /// <summary>カセットデッキのオートリバース。上下で逆向きの矢印を付けた周回。</summary>
    private static void DrawCassetteAutoReverse(DrawingContext dc, Pen pen)
    {
        const double left = 8.6;
        const double right = 25.4;
        const double top = 12.1;
        const double bottom = 23.9;
        const double radius = 5.9;
        var loop = new StreamGeometry();
        using (var ctx = loop.Open())
        {
            ctx.BeginFigure(new Point(left + radius, top), isFilled: false, isClosed: true);
            ctx.LineTo(new Point(right - radius, top), true, true);
            ctx.ArcTo(
                new Point(right - radius, bottom),
                new Size(radius, radius),
                0,
                false,
                SweepDirection.Clockwise,
                true,
                true);
            ctx.LineTo(new Point(left + radius, bottom), true, true);
            ctx.ArcTo(
                new Point(left + radius, top),
                new Size(radius, radius),
                0,
                false,
                SweepDirection.Clockwise,
                true,
                true);
        }

        loop.Freeze();
        dc.DrawGeometry(null, pen, loop);
        DrawLoopArrow(dc, pen, right - radius + 0.2, top, left: false);
        DrawLoopArrow(dc, pen, left + radius - 0.2, bottom, left: true);
    }

    private static void DrawLoopArrow(DrawingContext dc, Pen pen, double tipX, double tipY, bool left)
    {
        var direction = left ? -1d : 1d;
        var back = tipX - direction * 4.4;
        dc.DrawLine(pen, new Point(back, tipY - 3.5), new Point(tipX + direction * 0.6, tipY));
        dc.DrawLine(pen, new Point(back, tipY + 3.5), new Point(tipX + direction * 0.6, tipY));
    }

    /// <summary>折り角つきのファイルに音符。保存／名前を付けて保存の矢印トレイと区別する。</summary>
    private static void DrawSaveMp3(DrawingContext dc, Pen pen)
    {
        dc.DrawLine(pen, new Point(10, 26), new Point(10, 8));
        dc.DrawLine(pen, new Point(10, 8), new Point(18, 8));
        dc.DrawLine(pen, new Point(18, 8), new Point(24, 14));
        dc.DrawLine(pen, new Point(24, 14), new Point(24, 26));
        dc.DrawLine(pen, new Point(24, 26), new Point(10, 26));
        dc.DrawLine(pen, new Point(18, 8), new Point(18, 14));
        dc.DrawLine(pen, new Point(18, 14), new Point(24, 14));
        dc.DrawEllipse(null, pen, new Point(14.6, 21.4), 2.6, 1.8);
        dc.DrawLine(pen, new Point(17.2, 21.4), new Point(17.2, 12.6));
        dc.DrawLine(pen, new Point(17.2, 12.6), new Point(21.6, 15.2));
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
            dc.DrawEllipse(null, pen, new Point(cx, cy), 5, 5);
            dc.DrawLine(pen, new Point(cx - 3, cy), new Point(cx + 3, cy));
            if (isIn)
            {
                dc.DrawLine(pen, new Point(cx, cy - 3), new Point(cx, cy + 3));
            }
        }
        else if (isMax)
        {
            dc.DrawRectangle(null, pen, new Rect(cx - 3, cy - 3, 6, 6));
        }
        else
        {
            dc.DrawEllipse(null, pen, new Point(cx, cy), 4, 4);
        }
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
        TransportHover.Attach(this);
    }

    public void RefreshAppearance()
    {
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
            Math.Max(12d, Math.Min(bounds.Width, bounds.Height) * 0.52),
            WpfControlHelpers.FrozenBrush(TransportChrome.Fore(IsEnabled)),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(
            formatted,
            new Point((bounds.Width - formatted.Width) * 0.5, (bounds.Height - formatted.Height) * 0.5));
    }
}

internal static class TransportHover
{
    public static void Attach(ButtonBase button)
    {
        button.MouseEnter += (_, _) => button.InvalidateVisual();
        button.MouseLeave += (_, _) => button.InvalidateVisual();
        button.PreviewMouseLeftButtonDown += (_, _) => button.InvalidateVisual();
        button.PreviewMouseLeftButtonUp += (_, _) => button.InvalidateVisual();
        button.LostMouseCapture += (_, _) => button.InvalidateVisual();
        button.IsEnabledChanged += (_, _) => button.InvalidateVisual();
    }
}

internal static class TransportChrome
{
    public static void Paint(
        DrawingContext dc,
        Rect bounds,
        bool enabled,
        bool hover,
        bool pressed,
        string backKey = "TransportBackBrush",
        bool fillSlot = true,
        Rect? hoverBounds = null)
    {
        if (fillSlot)
        {
            dc.DrawRectangle(WpfControlHelpers.FrozenBrush(Theme.Get(backKey)), null, bounds);
        }

        if (enabled && (hover || pressed))
        {
            var fill = pressed
                ? Theme.Get("TransportPressedBackBrush")
                : Theme.Get("TransportHoverBackBrush");
            const double pad = 3d;
            var rect = hoverBounds
                ?? new Rect(pad, pad, Math.Max(1, bounds.Width - pad * 2), Math.Max(1, bounds.Height - pad * 2));
            dc.DrawRectangle(WpfControlHelpers.FrozenBrush(fill), null, rect);
        }
    }

    public static Color Fore(bool enabled) =>
        enabled ? Theme.Get("TransportForeBrush") : Theme.Get("TransportDisabledForeBrush");
}
