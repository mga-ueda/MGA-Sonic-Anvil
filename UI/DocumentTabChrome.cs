using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MgaSonicAnvil.UI;

/// <summary>ドキュメントタブの塗りと枠。設定の角丸を付けた矩形。</summary>
internal sealed class DocumentTabChrome : Border
{
    public DocumentTabChrome()
    {
        BorderThickness = new Thickness(0);
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(RenderSize);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var r = Math.Min(
            DocumentTabLayout.CornerRadius,
            Math.Min(bounds.Width, bounds.Height) * 0.45);

        var fill = Background;
        if (fill is not null && fill != Brushes.Transparent)
        {
            dc.DrawRoundedRectangle(fill, null, bounds, r, r);
        }

        // BorderThickness はレイアウトを縮めるので使わず、枠は塗りだけ（タイトル幅を削らない）。
        var stroke = BorderBrush;
        if (stroke is null || stroke == Brushes.Transparent)
        {
            return;
        }

        const double thickness = 1;
        // 枠は外形の内側に収め、角丸からはみ出さない。
        var half = thickness * 0.5;
        var strokeBounds = new Rect(
            bounds.X + half,
            bounds.Y + half,
            Math.Max(0, bounds.Width - thickness),
            Math.Max(0, bounds.Height - thickness));
        if (strokeBounds.Width <= 0 || strokeBounds.Height <= 0)
        {
            return;
        }

        var pen = new Pen(stroke, thickness);
        if (pen.CanFreeze)
        {
            pen.Freeze();
        }

        var strokeR = Math.Max(0, r - half);
        dc.DrawRoundedRectangle(null, pen, strokeBounds, strokeR, strokeR);
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) =>
        new Rect(RenderSize).Contains(hitTestParameters.HitPoint)
            ? new PointHitTestResult(this, hitTestParameters.HitPoint)
            : null;
}
