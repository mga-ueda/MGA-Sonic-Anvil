using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MgaSonicAnvil.UI;

/// <summary>Wwise と同じ 2 段×14 色。上段が先、左から右。</summary>
internal sealed class FileNameColorPaletteBoard : WrapPanel
{
    public const int Columns = 14;
    public const double CellWidth = 24;
    public const double CellHeight = 22;

    private readonly List<(int Index, Border Swatch)> _swatches = [];

    public event EventHandler<int>? ColorSelected;

    public FileNameColorPaletteBoard()
    {
        Width = Columns * CellWidth;
        foreach (var choice in FileNameColorMarkerVisual.Choices)
        {
            var swatch = new Border
            {
                Width = 16,
                Height = 14,
                Background = choice.Brush,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                SnapsToDevicePixels = true,
                IsHitTestVisible = false,
            };
            var hit = new Border
            {
                Width = CellWidth,
                Height = CellHeight,
                Cursor = Cursors.Hand,
                ToolTip = choice.Hex,
                Style = SwatchHitStyle,
                Child = swatch,
            };
            var index = choice.Index;
            hit.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                ColorSelected?.Invoke(this, index);
            };
            _swatches.Add((index, swatch));
            Children.Add(hit);
        }
    }

    public void Mark(int? selectedIndex)
    {
        foreach (var (index, swatch) in _swatches)
        {
            swatch.BorderBrush = selectedIndex == index
                ? Brushes.White
                : Brushes.Transparent;
        }
    }

    private static readonly Style SwatchHitStyle = CreateSwatchHitStyle();

    private static Style CreateSwatchHitStyle()
    {
        var style = new Style(typeof(Border));
        style.Setters.Add(new Setter(Border.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Border.BorderBrushProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Border.SnapsToDevicePixelsProperty, true));
        var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        over.Setters.Add(new Setter(
            Border.BackgroundProperty,
            new DynamicResourceExtension("PlayerHoverFillBrush")));
        over.Setters.Add(new Setter(
            Border.BorderBrushProperty,
            new DynamicResourceExtension("PrimaryForeBrush")));
        style.Triggers.Add(over);
        return style;
    }
}
