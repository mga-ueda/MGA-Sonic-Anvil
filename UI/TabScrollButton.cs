using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MgaSonicAnvil.UI;

internal sealed class TabScrollButton : Button
{
    public static readonly DependencyProperty PointLeftProperty = DependencyProperty.Register(
        nameof(PointLeft),
        typeof(bool),
        typeof(TabScrollButton),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool PointLeft
    {
        get => (bool)GetValue(PointLeftProperty);
        set => SetValue(PointLeftProperty, value);
    }

    public TabScrollButton()
    {
        Width = DesignMetrics.DocumentTabScrollButtonWidth;
        Height = DesignMetrics.DocumentTabBarHeight;
        Focusable = false;
        FocusVisualStyle = null;
        Cursor = Cursors.Hand;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        OverridesDefaultStyle = true;
        Template = new ControlTemplate(typeof(Button));
        SnapsToDevicePixels = true;
        IsEnabledChanged += (_, _) => Cursor = IsEnabled ? Cursors.Hand : Cursors.Arrow;
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

        dc.DrawRectangle(WpfControlHelpers.FrozenBrush(Theme.Get("TransportBackBrush")), null, bounds);
        DrawChevron(dc, bounds, PointLeft, Theme.Get("TransportDisabledForeBrush"));
    }

    private static void DrawChevron(DrawingContext dc, Rect bounds, bool left, Color fore)
    {
        const double design = 16d;
        var scale = Math.Min(bounds.Width, bounds.Height) / design;
        if (scale <= 0d)
        {
            return;
        }

        dc.PushTransform(new TranslateTransform(
            (bounds.Width - design * scale) * 0.5,
            (bounds.Height - design * scale) * 0.5));
        dc.PushTransform(new ScaleTransform(scale, scale));

        var pen = new Pen(WpfControlHelpers.FrozenBrush(fore), 1.25)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();
        if (left)
        {
            dc.DrawLine(pen, new Point(10.2, 4.2), new Point(5.2, 8));
            dc.DrawLine(pen, new Point(5.2, 8), new Point(10.2, 11.8));
        }
        else
        {
            dc.DrawLine(pen, new Point(5.8, 4.2), new Point(10.8, 8));
            dc.DrawLine(pen, new Point(10.8, 8), new Point(5.8, 11.8));
        }

        dc.Pop();
        dc.Pop();
    }
}
