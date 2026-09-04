using System.Windows;
using System.Windows.Controls;
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

    public static int CanvasSize(int pixelSize) => Math.Max(8, pixelSize) + CanvasPad * 2;

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

            var pen = new Pen(new SolidColorBrush(Color.FromArgb(220, 220, 220, 220)), 1.4)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            pen.Freeze();

            var inset = CanvasPad + 1.5;
            var span = canvas - inset * 2;
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                if (shape == FadeShape.Constant)
                {
                    var y = canvas * 0.5;
                    ctx.BeginFigure(new Point(inset, y), false, false);
                    ctx.LineTo(new Point(inset + span, y), true, true);
                }
                else
                {
                    const int samples = 16;
                    for (var i = 0; i <= samples; i++)
                    {
                        var t = i / (double)samples;
                        var rising = IconRising(shape, t);
                        var yGain = isFadeIn ? rising : 1d - rising;
                        var p = new Point(inset + t * span, canvas - inset - yGain * span);
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

        var bmp = new RenderTargetBitmap(canvas, canvas, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
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
        foreach (var shape in order)
        {
            var captured = shape;
            var selected = shape == current;
            var item = new MenuItem
            {
                Header = UiStrings.LabelFadeCurve((int)shape),
                Tag = captured,
                ToolTip = UiStrings.TipFadeShape((int)shape),
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
}
