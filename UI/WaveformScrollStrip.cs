using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

internal enum WaveformScrollButtonKind
{
    Plus,
    Minus,
    ChevronLeft,
    ChevronRight,
}

/// <summary>波形下の ＋－◀ スクロール ▶＋－。</summary>
internal sealed class WaveformScrollStrip : Grid
{
    private readonly WaveformScrollButton _ampIn = new(WaveformScrollButtonKind.Plus);
    private readonly WaveformScrollButton _ampOut = new(WaveformScrollButtonKind.Minus);
    private readonly WaveformScrollButton _scrollLeft = new(WaveformScrollButtonKind.ChevronLeft);
    private readonly WaveformScrollButton _scrollRight = new(WaveformScrollButtonKind.ChevronRight);
    private readonly WaveformScrollButton _timeIn = new(WaveformScrollButtonKind.Plus);
    private readonly WaveformScrollButton _timeOut = new(WaveformScrollButtonKind.Minus);

    public WaveformScrollStrip()
    {
        Height = DesignMetrics.WaveformScrollBarHeight;
        MinHeight = DesignMetrics.WaveformScrollBarHeight;
        MaxHeight = DesignMetrics.WaveformScrollBarHeight;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        SetResourceReference(BackgroundProperty, "TimelineWellBackBrush");

        ColumnDefinitions.Add(ButtonColumn());
        ColumnDefinitions.Add(ButtonColumn());
        ColumnDefinitions.Add(ButtonColumn());
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(ButtonColumn());
        ColumnDefinitions.Add(ButtonColumn());
        ColumnDefinitions.Add(ButtonColumn());

        Bar = new TimeScrollBar { Margin = new Thickness(2, 0, 2, 0) };
        Place(_ampIn, 0);
        Place(_ampOut, 1);
        Place(_scrollLeft, 2);
        Place(Bar, 3);
        Place(_scrollRight, 4);
        Place(_timeIn, 5);
        Place(_timeOut, 6);

        _ampIn.Click += (_, _) => AmpZoomIn?.Invoke(this, EventArgs.Empty);
        _ampOut.Click += (_, _) => AmpZoomOut?.Invoke(this, EventArgs.Empty);
        _scrollLeft.Click += (_, _) => ScrollLeft?.Invoke(this, EventArgs.Empty);
        _scrollRight.Click += (_, _) => ScrollRight?.Invoke(this, EventArgs.Empty);
        _timeIn.Click += (_, _) => TimeZoomIn?.Invoke(this, EventArgs.Empty);
        _timeOut.Click += (_, _) => TimeZoomOut?.Invoke(this, EventArgs.Empty);
        Bar.IsEnabledChanged += (_, _) => SyncButtonEnabled();
        SyncButtonEnabled();
        ApplyLocalizedTips();
    }

    public TimeScrollBar Bar { get; }

    public event EventHandler? AmpZoomIn;

    public event EventHandler? AmpZoomOut;

    public event EventHandler? TimeZoomIn;

    public event EventHandler? TimeZoomOut;

    public event EventHandler? ScrollLeft;

    public event EventHandler? ScrollRight;

    public void RefreshAppearance()
    {
        InvalidateVisual();
        foreach (UIElement child in Children)
        {
            child.InvalidateVisual();
        }
    }

    public void ApplyLocalizedTips()
    {
        TipService.Set(_ampIn, UiStrings.TipTimeScrollAmpZoomIn);
        TipService.Set(_ampOut, UiStrings.TipTimeScrollAmpZoomOut);
        TipService.Set(_scrollLeft, UiStrings.TipTimeScrollLeft);
        TipService.Set(Bar, UiStrings.TipTimeScroll);
        TipService.Set(_scrollRight, UiStrings.TipTimeScrollRight);
        TipService.Set(_timeIn, UiStrings.TipTimeScrollTimeZoomIn);
        TipService.Set(_timeOut, UiStrings.TipTimeScrollTimeZoomOut);
        _ampIn.SetValue(AutomationProperties.NameProperty, UiStrings.TooltipAmpZoomIn);
        _ampOut.SetValue(AutomationProperties.NameProperty, UiStrings.TooltipAmpZoomOut);
        _scrollLeft.SetValue(AutomationProperties.NameProperty, UiStrings.TipTimeScrollLeft);
        _scrollRight.SetValue(AutomationProperties.NameProperty, UiStrings.TipTimeScrollRight);
        _timeIn.SetValue(AutomationProperties.NameProperty, UiStrings.TooltipTimeZoomIn);
        _timeOut.SetValue(AutomationProperties.NameProperty, UiStrings.TooltipTimeZoomOut);
    }

    private void SyncButtonEnabled()
    {
        var on = Bar.IsEnabled;
        _ampIn.IsEnabled = on;
        _ampOut.IsEnabled = on;
        _scrollLeft.IsEnabled = on;
        _scrollRight.IsEnabled = on;
        _timeIn.IsEnabled = on;
        _timeOut.IsEnabled = on;
    }

    private static ColumnDefinition ButtonColumn() =>
        new()
        {
            Width = DesignMetrics.WaveformScrollButtonWidthGrid,
        };

    private void Place(UIElement child, int column)
    {
        SetColumn(child, column);
        Children.Add(child);
    }
}

internal sealed class WaveformScrollButton : RepeatButton
{
    public WaveformScrollButton(WaveformScrollButtonKind kind)
    {
        Kind = kind;
        Width = DesignMetrics.WaveformScrollButtonWidth;
        Height = DesignMetrics.WaveformScrollBarHeight;
        MinWidth = DesignMetrics.WaveformScrollButtonWidth;
        MinHeight = DesignMetrics.WaveformScrollBarHeight;
        Focusable = false;
        FocusVisualStyle = null;
        Cursor = Cursors.Hand;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        OverridesDefaultStyle = true;
        Template = new ControlTemplate(typeof(RepeatButton));
        SnapsToDevicePixels = true;
        Delay = 400;
        Interval = 50;
        TransportHover.Attach(this);
        IsEnabledChanged += (_, _) => Cursor = IsEnabled ? Cursors.Hand : Cursors.Arrow;
    }

    public WaveformScrollButtonKind Kind { get; }

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

        dc.DrawRectangle(WpfControlHelpers.FrozenBrush(Theme.Get("TimelineWellBackBrush")), null, bounds);
        if (IsEnabled && (IsMouseOver || IsPressed))
        {
            var fill = IsPressed
                ? Theme.Get("TransportPressedBackBrush")
                : Theme.Get("TransportHoverBackBrush");
            dc.DrawRectangle(WpfControlHelpers.FrozenBrush(fill), null, bounds);
        }

        DrawGlyph(dc, bounds, TransportChrome.Fore(IsEnabled));
    }

    private void DrawGlyph(DrawingContext dc, Rect bounds, Color fore)
    {
        var pen = new Pen(WpfControlHelpers.FrozenBrush(fore), 1.2)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();
        var cx = bounds.Width * 0.5;
        var cy = bounds.Height * 0.5;
        const double arm = 3.4;
        switch (Kind)
        {
            case WaveformScrollButtonKind.Plus:
                dc.DrawLine(pen, new Point(cx - arm, cy), new Point(cx + arm, cy));
                dc.DrawLine(pen, new Point(cx, cy - arm), new Point(cx, cy + arm));
                break;
            case WaveformScrollButtonKind.Minus:
                dc.DrawLine(pen, new Point(cx - arm, cy), new Point(cx + arm, cy));
                break;
            case WaveformScrollButtonKind.ChevronLeft:
                dc.DrawLine(pen, new Point(cx + 2.2, cy - 3.4), new Point(cx - 2.2, cy));
                dc.DrawLine(pen, new Point(cx - 2.2, cy), new Point(cx + 2.2, cy + 3.4));
                break;
            default:
                dc.DrawLine(pen, new Point(cx - 2.2, cy - 3.4), new Point(cx + 2.2, cy));
                dc.DrawLine(pen, new Point(cx + 2.2, cy), new Point(cx - 2.2, cy + 3.4));
                break;
        }
    }
}
