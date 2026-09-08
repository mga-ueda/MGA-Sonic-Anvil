using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace MgaSonicAnvil.UI;

/// <summary>Wwise IM Importer と同じ歯車。押すと設定を開く。</summary>
internal sealed class SettingsGearButton : Button
{
    public SettingsGearButton()
    {
        Width = DesignMetrics.ToolbarButtonSide;
        Height = DesignMetrics.ToolbarButtonSide;
        MinWidth = DesignMetrics.ToolbarButtonSide;
        MinHeight = DesignMetrics.ToolbarButtonSide;
        Padding = new Thickness(0);
        BorderThickness = new Thickness(0);
        Background = Brushes.Transparent;
        Focusable = false;
        FocusVisualStyle = null;
        Cursor = System.Windows.Input.Cursors.Hand;
        OverridesDefaultStyle = true;
        Template = new ControlTemplate(typeof(Button));
        SnapsToDevicePixels = true;
        SetValue(AutomationProperties.NameProperty, Domain.UiStrings.AccessibleAudioSettingsButton);
        TransportHover.Attach(this);
    }

    public void RefreshAppearance() => InvalidateVisual();

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
        DrawGear(dc, TransportChrome.Fore(IsEnabled), HoleColor());
    }

    private Color HoleColor()
    {
        if (IsEnabled && (IsMouseOver || IsPressed))
        {
            return IsPressed
                ? Theme.Get("TransportPressedBackBrush")
                : Theme.Get("TransportHoverBackBrush");
        }

        return Theme.Get("TransportBackBrush");
    }

    private static void DrawGear(DrawingContext dc, Color color, Color holeColor)
    {
        const int teeth = 8;
        const double side = 24;
        var cx = side * 0.5;
        var cy = side * 0.5;
        var outer = side * 0.30;
        var inner = side * 0.19;
        var hub = side * 0.09;
        var points = new Point[teeth * 4];
        for (var i = 0; i < teeth; i++)
        {
            var baseAngle = i / (double)teeth * Math.PI * 2d - Math.PI / teeth;
            var step = Math.PI * 2d / teeth;
            points[i * 4] = Polar(cx, cy, inner, baseAngle);
            points[i * 4 + 1] = Polar(cx, cy, outer, baseAngle + step * 0.28);
            points[i * 4 + 2] = Polar(cx, cy, outer, baseAngle + step * 0.72);
            points[i * 4 + 3] = Polar(cx, cy, inner, baseAngle + step);
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], isFilled: true, isClosed: true);
            for (var i = 1; i < points.Length; i++)
            {
                ctx.LineTo(points[i], true, false);
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(WpfControlHelpers.FrozenBrush(color), null, geometry);
        dc.DrawEllipse(WpfControlHelpers.FrozenBrush(holeColor), null, new Point(cx, cy), hub, hub);
        var ringPen = new Pen(WpfControlHelpers.FrozenBrush(color), Math.Max(1d, side * 0.05))
        {
            LineJoin = PenLineJoin.Round,
        };
        ringPen.Freeze();
        dc.DrawEllipse(null, ringPen, new Point(cx, cy), hub * 1.7, hub * 1.7);
    }

    private static Point Polar(double cx, double cy, double radius, double angle) =>
        new(cx + Math.Cos(angle) * radius, cy + Math.Sin(angle) * radius);
}
