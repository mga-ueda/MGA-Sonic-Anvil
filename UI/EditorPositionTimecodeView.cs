using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace MgaSonicAnvil.UI;

/// <summary>エディタ下段の大きな現在位置タイムコード。</summary>
internal sealed class EditorPositionTimecodeView : FrameworkElement
{
    private static readonly Typeface Typeface = new("Consolas");

    private string _text = string.Empty;

    public EditorPositionTimecodeView()
    {
        Focusable = false;
        IsHitTestVisible = true;
        SnapsToDevicePixels = true;
    }

    public void SetDisplay(string text)
    {
        text ??= string.Empty;
        if (string.Equals(_text, text, StringComparison.Ordinal))
        {
            return;
        }

        _text = text;
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void Clear() => SetDisplay(string.Empty);

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_text.Length == 0)
        {
            return new Size(0, DesignMetrics.EditorPositionTimecodeFontSize);
        }

        var formatted = CreateText(_text);
        return new Size(
            Math.Max(1, formatted.WidthIncludingTrailingWhitespace),
            Math.Max(1, formatted.Height));
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_text.Length == 0)
        {
            return;
        }

        var formatted = CreateText(_text);
        var x = Math.Max(0, (ActualWidth - formatted.WidthIncludingTrailingWhitespace) * 0.5);
        var y = Math.Max(0, (ActualHeight - formatted.Height) * 0.5);
        dc.DrawText(formatted, new Point(x, y));
    }

    private FormattedText CreateText(string text)
    {
        var color = Theme.Get("PrimaryForeBrush");
        var brush = WpfControlHelpers.FrozenBrush(color);
        return new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface,
            DesignMetrics.EditorPositionTimecodeFontSize,
            brush,
            UiDpi.Get(this).PixelsPerDip);
    }
}
