using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>ラウドネス曲線の左下。カーソルをペンにする。</summary>
internal sealed class LoudnessPenButton : ToggleButton
{
    public const string AutomationId = "LoudnessPen";

    private const double Icon = 22;
    private const double Gap = 6;
    private const double Pad = 8;

    public LoudnessPenButton()
    {
        Margin = new Thickness(6, 0, 0, 6);
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Bottom;
        Focusable = false;
        FocusVisualStyle = null;
        Cursor = Cursors.Hand;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        OverridesDefaultStyle = true;
        Template = new ControlTemplate(typeof(ToggleButton));
        SnapsToDevicePixels = true;
        Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(this, AutomationId);
        Panel.SetZIndex(this, 8);
        Checked += (_, _) => InvalidateVisual();
        Unchecked += (_, _) => InvalidateVisual();
        MouseEnter += (_, _) => InvalidateVisual();
        MouseLeave += (_, _) => InvalidateVisual();
    }

    public void ApplyLocalizedText()
    {
        ToolTip = UiStrings.TooltipLoudnessPen;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var text = HintText(VisualTreeHelper.GetDpi(this).PixelsPerDip);
        return new Size(Icon + Gap + text.Width + Pad, Math.Max(Icon, text.Height + 6));
    }

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(RenderSize);
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return;
        }

        var latched = IsChecked == true;
        var back = WpfControlHelpers.FrozenBrush(Theme.Get(latched ? "ScrollThumbBackBrush" : "ColorPanelBackBrush"));
        var border = WpfControlHelpers.FrozenBrush(
            Theme.Get(latched || IsMouseOver ? "ControlHoverBorderBrush" : "ChromeBorderBrush"));
        var outline = new Pen(border, 1);
        outline.Freeze();
        dc.DrawRoundedRectangle(back, outline, new Rect(0.5, 0.5, bounds.Width - 1, bounds.Height - 1), 2, 2);

        var fore = WpfControlHelpers.FrozenBrush(Theme.Get(latched ? "PrimaryForeBrush" : "MutedForeBrush"));
        var hint = HintText(VisualTreeHelper.GetDpi(this).PixelsPerDip, fore);
        dc.DrawText(hint, new Point(Icon + Gap, Math.Max(0, (bounds.Height - hint.Height) / 2)));
        DrawCursorPen(dc, fore, bounds.Height);
    }

    /// <summary>システムのペンカーソル（先が左上、軸は右下、末端が離れた形）と同じシルエット。</summary>
    private const string CursorPen =
        "..#............................." +
        "...##..........................." +
        "...####........................." +
        "....####........................" +
        "....#####......................." +
        ".....#####......................" +
        "......#####....................." +
        ".......#####...................." +
        "........#####..................." +
        ".........#####.................." +
        "..........#####................." +
        "...........#####................" +
        "............#####..............." +
        ".............#####.............." +
        "..............####.............." +
        "...............##++#............" +
        "..................###..........." +
        ".................#####.........." +
        "..................###..........." +
        "...................#............";

    private static void DrawCursorPen(DrawingContext dc, Brush fore, double height)
    {
        const int cols = 32;
        var rows = CursorPen.Length / cols;
        var minX = cols;
        var minY = rows;
        var maxX = 0;
        var maxY = 0;
        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < cols; x++)
            {
                if (CursorPen[y * cols + x] != '#')
                {
                    continue;
                }

                if (x < minX)
                {
                    minX = x;
                }

                if (y < minY)
                {
                    minY = y;
                }

                if (x > maxX)
                {
                    maxX = x;
                }

                if (y > maxY)
                {
                    maxY = y;
                }
            }
        }

        var inkW = maxX - minX + 1;
        var inkH = maxY - minY + 1;
        const double size = 15;
        var scale = size / Math.Max(inkW, inkH);
        var ox = (Icon - inkW * scale) / 2 - minX * scale;
        var oy = (height - inkH * scale) / 2 - minY * scale;
        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < cols; x++)
            {
                if (CursorPen[y * cols + x] != '#')
                {
                    continue;
                }

                dc.DrawRectangle(fore, null, new Rect(ox + x * scale, oy + y * scale, scale + 0.15, scale + 0.15));
            }
        }
    }

    private static FormattedText HintText(double pixelsPerDip, Brush? brush = null) =>
        new(
            UiStrings.LabelLoudnessPenToggle,
            UiStrings.IsJapanese ? CultureInfo.GetCultureInfo("ja-JP") : CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            WpfControlHelpers.UiTypeface,
            12,
            brush ?? Brushes.Transparent,
            pixelsPerDip);
}
