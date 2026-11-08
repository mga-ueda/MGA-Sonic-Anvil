using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MgaSonicAnvil.UI;

/// <summary>ドキュメントタブ列の直後に置く新規タブ（＋）ボタン。</summary>
internal sealed class TabNewButton : Button
{
    public TabNewButton()
    {
        var side = DesignMetrics.DocumentTabNewButtonWidth;
        Width = side;
        Height = side;
        Margin = new Thickness(2, 0, 0, 0);
        VerticalAlignment = VerticalAlignment.Center;
        Focusable = false;
        FocusVisualStyle = null;
        Cursor = Cursors.Hand;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        OverridesDefaultStyle = true;
        Template = new ControlTemplate(typeof(Button));
        SnapsToDevicePixels = true;
        TransportHover.Attach(this);
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

        // ホバーはボタン全面より少し内側の小さな角丸。
        var pad = Math.Max(1, bounds.Width * 0.08);
        var hover = new Rect(
            pad,
            pad,
            Math.Max(1, bounds.Width - pad * 2),
            Math.Max(1, bounds.Height - pad * 2));
        TransportChrome.Paint(dc, bounds, IsEnabled, IsMouseOver, IsPressed, hover);
        DrawPlus(dc, bounds, TransportChrome.Fore(IsEnabled));
    }

    private static void DrawPlus(DrawingContext dc, Rect bounds, Color fore)
    {
        const double design = 12d;
        var scale = Math.Min(bounds.Width, bounds.Height) / design;
        if (scale <= 0d)
        {
            return;
        }

        dc.PushTransform(new TranslateTransform(
            (bounds.Width - design * scale) * 0.5,
            (bounds.Height - design * scale) * 0.5));
        dc.PushTransform(new ScaleTransform(scale, scale));

        var pen = new Pen(WpfControlHelpers.FrozenBrush(fore), 1.2)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        pen.Freeze();
        dc.DrawLine(pen, new Point(3.2, 6), new Point(8.8, 6));
        dc.DrawLine(pen, new Point(6, 3.2), new Point(6, 8.8));

        dc.Pop();
        dc.Pop();
    }
}
