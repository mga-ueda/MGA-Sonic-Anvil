using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace MgaSonicAnvil.UI;

/// <summary>
/// 右下リサイズつまみの共通見た目（3×3 下三角の 6 ドット）。
/// </summary>
internal sealed class WindowResizeGripMark : FrameworkElement
{
    // 右下へ寄る階段状: 上段1・中段2・下段3。
    private static readonly (int Col, int Row)[] DotCells =
    [
        (2, 0),
        (1, 1), (2, 1),
        (0, 2), (1, 2), (2, 2),
    ];

    public WindowResizeGripMark()
    {
        Width = DesignMetrics.WindowResizeGripSize;
        Height = DesignMetrics.WindowResizeGripSize;
        IsHitTestVisible = false;
        Focusable = false;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0)
        {
            return;
        }

        // ドット2・隙間1・余白2 程度（14px 基準）。
        var inset = size * (2.0 / 14.0);
        var cell = (size - inset * 2) / 3.0;
        var dot = cell * (2.0 / 3.0);
        var fill = ResolveBrush("PrimaryForeBrush", Brushes.WhiteSmoke);
        var shade = ResolveBrush("ChromeBorderBrush", Brushes.Black);

        foreach (var (col, row) in DotCells)
        {
            var x = inset + col * cell + (cell - dot) * 0.5;
            var y = inset + row * cell + (cell - dot) * 0.5;
            // わずかな凹み（影を右下へずらす）。
            dc.DrawRectangle(shade, null, new Rect(x + 0.6, y + 0.6, dot, dot));
            dc.DrawRectangle(fill, null, new Rect(x, y, dot, dot));
        }
    }

    private Brush ResolveBrush(string key, Brush fallback)
    {
        if (TryFindResource(key) is Brush brush)
        {
            return brush;
        }

        return fallback;
    }

    /// <summary>Popup 等の Thumb 用テンプレート（操作は呼び出し側）。</summary>
    public static ControlTemplate CreateThumbTemplate(double size)
    {
        var template = new ControlTemplate(typeof(Thumb));
        var root = new FrameworkElementFactory(typeof(Grid));

        var hit = new FrameworkElementFactory(typeof(Border));
        hit.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        root.AppendChild(hit);

        var mark = new FrameworkElementFactory(typeof(WindowResizeGripMark));
        mark.SetValue(WidthProperty, size);
        mark.SetValue(HeightProperty, size);
        mark.SetValue(IsHitTestVisibleProperty, false);
        mark.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Right);
        mark.SetValue(VerticalAlignmentProperty, VerticalAlignment.Bottom);
        root.AppendChild(mark);

        template.VisualTree = root;
        return template;
    }
}
