using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

/// <summary>TimeCaster のフェードカーブメニューと同じアイコン式・並び。</summary>
internal static class FadeCurveIcons
{
    public const int IconSize = 18;
    public const int CanvasPad = 1;

    public static int WidthFor(int pixelSize) => Math.Max(8, pixelSize);

    public static int CanvasSize(int pixelSize) => Math.Max(8, pixelSize) + CanvasPad * 2;

    private static Color CurveColor()
    {
        try
        {
            return Theme.Get("PrimaryForeBrush");
        }
        catch (InvalidOperationException)
        {
            return Color.FromArgb(220, 220, 220, 220);
        }
    }

    public static ImageSource Create(
        FadeShape shape,
        bool isFadeIn,
        int pixelSize = IconSize)
    {
        var inner = Math.Max(8, pixelSize);
        var canvas = CanvasSize(inner);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, canvas, canvas));

            var pen = new Pen(WpfControlHelpers.FrozenBrush(CurveColor()), 1.4)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            pen.Freeze();

            var inset = CanvasPad + 1.5;
            var span = canvas - inset * 2;
            DrawCurve(dc, shape, isFadeIn, new Rect(inset, inset, span, span), pen);
        }

        var bmp = new RenderTargetBitmap(canvas, canvas, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    public static void DrawCurve(
        DrawingContext dc,
        FadeShape shape,
        bool isFadeIn,
        Rect bounds,
        Pen pen)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            if (shape == FadeShape.Constant)
            {
                var y = bounds.Y + bounds.Height * 0.5;
                ctx.BeginFigure(new Point(bounds.X, y), false, false);
                ctx.LineTo(new Point(bounds.X + bounds.Width, y), true, true);
            }
            else
            {
                const int samples = 16;
                for (var i = 0; i <= samples; i++)
                {
                    var t = i / (double)samples;
                    var rising = IconRising(shape, t);
                    var yGain = isFadeIn ? rising : 1d - rising;
                    var p = new Point(
                        bounds.X + t * bounds.Width,
                        bounds.Y + (1d - yGain) * bounds.Height);
                    if (i == 0)
                    {
                        ctx.BeginFigure(p, false, false);
                    }
                    else
                    {
                        ctx.LineTo(p, true, true);
                    }
                }
            }
        }

        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }

    /// <summary>TimeCaster と同様、S / InvS だけ二度がけして 18px でも形が分かる。</summary>
    private static double IconRising(FadeShape shape, double t)
    {
        var once = FadeCurves.Apply01(shape, t);
        return shape is FadeShape.SCurve or FadeShape.InvSCurve
            ? FadeCurves.Apply01(shape, once)
            : once;
    }

    public static void AddCurveChoices(
        ItemCollection items,
        FadeShape current,
        bool isFadeIn,
        Action<FadeShape> onSelected,
        int iconSize = IconSize)
    {
        var order = FadeCurves.MenuOrder(isFadeIn);
        var canvas = CanvasSize(iconSize);
        var cyan = WpfControlHelpers.FrozenBrush(Theme.Get("AccentCyanBrush"));
        var index = 0;
        foreach (var shape in order)
        {
            var captured = shape;
            var selected = shape == current;
            index++;
            var item = new MenuItem
            {
                Header = PickerChrome.Numbered(index, UiStrings.LabelFadeCurve((int)shape)),
                Tag = captured,
                Icon = new Border
                {
                    Width = canvas,
                    Height = canvas,
                    BorderThickness = new Thickness(1),
                    BorderBrush = selected ? cyan : Brushes.Transparent,
                    SnapsToDevicePixels = true,
                    Child = new Image
                    {
                        Source = Create(shape, isFadeIn, iconSize),
                        Width = canvas,
                        Height = canvas,
                        Stretch = Stretch.None,
                        SnapsToDevicePixels = true,
                    },
                },
            };
            TipService.Set(item, UiStrings.TipFadeShape((int)shape));
            item.Click += (_, _) => onSelected(captured);
            items.Add(item);
        }
    }

    public static void SyncSelectedBorder(ItemCollection items, FadeShape selected)
    {
        var cyan = WpfControlHelpers.FrozenBrush(Theme.Get("AccentCyanBrush"));
        foreach (var item in items.OfType<MenuItem>())
        {
            if (item.Tag is not FadeShape shape || item.Icon is not Border border)
            {
                continue;
            }

            border.BorderBrush = shape == selected ? cyan : Brushes.Transparent;
        }
    }

    public static ContextMenu ShowPicker(
        FrameworkElement owner,
        Point clientLocation,
        FadeShape current,
        bool isFadeIn,
        Action<FadeShape> onSelected,
        ref ContextMenu? menuSlot)
    {
        if (menuSlot is not null)
        {
            menuSlot.IsOpen = false;
        }

        var menu = new ContextMenu();
        menuSlot = menu;
        AddCurveChoices(menu.Items, current, isFadeIn, onSelected);
        menu.PlacementTarget = owner;
        menu.Placement = PlacementMode.RelativePoint;
        menu.HorizontalOffset = clientLocation.X;
        menu.VerticalOffset = clientLocation.Y;
        PickerChrome.FitListMenu(menu);
        menu.IsOpen = true;
        return menu;
    }
}
