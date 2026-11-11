using System.Windows;
using System.Windows.Controls;

namespace MgaSonicAnvil.UI;

/// <summary>
/// 通常の WrapPanel と同様に折り返しつつ、各行を左／中央／右へ揃える。
/// 中央寄せで 2 行目が短いとき、1 行目幅の中で中央に置く（素の WrapPanel は常に左詰め）。
/// </summary>
internal sealed class AlignedWrapPanel : Panel
{
    public static readonly DependencyProperty LineAlignmentProperty = DependencyProperty.Register(
        nameof(LineAlignment),
        typeof(HorizontalAlignment),
        typeof(AlignedWrapPanel),
        new FrameworkPropertyMetadata(
            HorizontalAlignment.Left,
            FrameworkPropertyMetadataOptions.AffectsArrange));

    public HorizontalAlignment LineAlignment
    {
        get => (HorizontalAlignment)GetValue(LineAlignmentProperty);
        set => SetValue(LineAlignmentProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var widthLimit = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : availableSize.Width;
        double lineWidth = 0;
        double lineHeight = 0;
        double panelWidth = 0;
        double panelHeight = 0;

        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            child.Measure(availableSize);
            var size = child.DesiredSize;
            if (lineWidth > 0 && lineWidth + size.Width > widthLimit)
            {
                panelWidth = Math.Max(panelWidth, lineWidth);
                panelHeight += lineHeight;
                lineWidth = 0;
                lineHeight = 0;
            }

            lineWidth += size.Width;
            lineHeight = Math.Max(lineHeight, size.Height);
        }

        panelWidth = Math.Max(panelWidth, lineWidth);
        panelHeight += lineHeight;
        return new Size(panelWidth, panelHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var widthLimit = finalSize.Width;
        if (widthLimit <= 0 || double.IsInfinity(widthLimit) || double.IsNaN(widthLimit))
        {
            widthLimit = DesiredSize.Width;
        }

        var line = new List<UIElement>();
        double lineWidth = 0;
        double lineHeight = 0;
        double y = 0;

        void FlushLine()
        {
            if (line.Count == 0)
            {
                return;
            }

            var x = LineStartX(lineWidth, widthLimit);
            foreach (var child in line)
            {
                var size = child.DesiredSize;
                child.Arrange(new Rect(x, y, size.Width, size.Height));
                x += size.Width;
            }

            y += lineHeight;
            line.Clear();
            lineWidth = 0;
            lineHeight = 0;
        }

        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            var size = child.DesiredSize;
            if (line.Count > 0 && lineWidth + size.Width > widthLimit)
            {
                FlushLine();
            }

            line.Add(child);
            lineWidth += size.Width;
            lineHeight = Math.Max(lineHeight, size.Height);
        }

        FlushLine();
        return finalSize;
    }

    private double LineStartX(double lineWidth, double widthLimit) =>
        LineAlignment switch
        {
            HorizontalAlignment.Center => Math.Max(0, (widthLimit - lineWidth) * 0.5),
            HorizontalAlignment.Right => Math.Max(0, widthLimit - lineWidth),
            _ => 0,
        };
}
