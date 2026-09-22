using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>
/// 埋め込みジャケットが無いときの表示用。上下グラデに No Image。
/// WAVE 等にも出すが、ファイルへは書き込まない。
/// </summary>
internal static class LibraryPlaceholderJacket
{
    internal const int PixelSize = 256;
    internal const string Label = "No Image";

    private static UiTheme _theme;
    private static BitmapSource? _bitmap;

    public static void Invalidate() => _bitmap = null;

    public static BitmapSource Bitmap
    {
        get
        {
            var theme = UiThemeService.Painted;
            if (_bitmap is null || _theme != theme)
            {
                _theme = theme;
                _bitmap = Render(theme);
            }

            return _bitmap;
        }
    }

    internal static BitmapSource Render(UiTheme theme)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var rect = new Rect(0, 0, PixelSize, PixelSize);
            var top = PlayerChrome.Get("PlayerPlaceholderJacketTopBrush", theme);
            var bottom = PlayerChrome.Get("PlayerPlaceholderJacketBottomBrush", theme);
            var label = PlayerChrome.Get("PlayerPlaceholderJacketForeBrush", theme);

            var fill = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 0),
                EndPoint = new Point(0.5, 1),
            };
            fill.GradientStops.Add(new GradientStop(top, 0));
            fill.GradientStops.Add(new GradientStop(bottom, 1));
            fill.Freeze();
            dc.DrawRectangle(fill, null, rect);

            var brush = new SolidColorBrush(label);
            brush.Freeze();
            var text = new FormattedText(
                Label,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                WpfControlHelpers.UiTypeface,
                26,
                brush,
                1);
            dc.DrawText(
                text,
                new Point((PixelSize - text.Width) / 2, (PixelSize - text.Height) / 2));
        }

        var bmp = new RenderTargetBitmap(PixelSize, PixelSize, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }
}
