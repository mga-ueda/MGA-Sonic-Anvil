using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

/// <summary>ラウドネス左の履歴プレビュー。クリックで本履歴を開く。</summary>
internal sealed class HistoryStripView : FrameworkElement
{
    private const double FontSize = 11;
    private const double PadX = 6;
    private const double PadY = 2;

    private IReadOnlyList<EditHistoryEntry> _items = [];
    private int _currentIndex;
    private bool _hover;

    public event EventHandler? OpenRequested;

    public HistoryStripView()
    {
        Width = DesignMetrics.HistoryStripWidth;
        MinWidth = DesignMetrics.HistoryStripWidth;
        Height = DesignMetrics.SpectrumHeight;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Cursor = Cursors.Hand;
        Focusable = false;
        SizeChanged += (_, _) => InvalidateVisual();
    }

    public void SetItems(IReadOnlyList<EditHistoryEntry> items, int currentIndex)
    {
        _items = items ?? [];
        _currentIndex = currentIndex;
        InvalidateVisual();
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenRequested?.Invoke(this, EventArgs.Empty);
        base.OnMouseLeftButtonUp(e);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRectangle(
            ThemeBrush(_hover ? "HistoryStripHoverBackBrush" : "HistoryStripBackBrush"),
            null,
            bounds);

        var rowHeight = DesignMetrics.HistoryStripRowHeight;
        var visible = HistoryStripLayout.VisibleCount(ActualHeight, rowHeight, PadY);
        if (visible <= 0 || _items.Count == 0)
        {
            return;
        }

        var start = HistoryStripLayout.VisibleStart(_items.Count, _currentIndex, visible);
        var dpi = UiDpi.Get(this).PixelsPerDip;
        var textWidth = Math.Max(0, ActualWidth - (PadX * 2));
        var current = ThemeBrush("HistoryStripCurrentForeBrush");
        var past = ThemeBrush("HistoryStripPastForeBrush");
        var future = ThemeBrush("HistoryStripFutureForeBrush");
        var y = PadY;
        for (var i = 0; i < visible && start + i < _items.Count; i++)
        {
            var item = _items[start + i];
            var brush = item.Index == _currentIndex
                ? current
                : item.Index > _currentIndex ? future : past;
            var text = new FormattedText(
                item.Title,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                WpfControlHelpers.UiTypeface,
                FontSize,
                brush,
                dpi)
            {
                MaxTextWidth = textWidth,
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis,
            };
            var baseline = y + ((rowHeight - text.Height) * 0.5);
            dc.DrawText(text, new Point(PadX, baseline));
            y += rowHeight;
        }
    }

    private static Brush ThemeBrush(string key) =>
        WpfControlHelpers.FrozenBrush(Theme.Get(key));
}
