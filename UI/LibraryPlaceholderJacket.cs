using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>
/// 埋め込みジャケットが無いときの表示用。グレーのグラデに八分音符。
/// WAVE 等にも出すが、ファイルへは書き込まない。
/// </summary>
internal static class LibraryPlaceholderJacket
{
    internal const int PixelSize = 256;

    private static UiTheme _theme;
    private static BitmapSource? _bitmap;

    public static BitmapSource Bitmap
    {
        get
        {
            var theme = UiThemeService.Current;
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
            var light = theme == UiTheme.Light;
            var top = light
                ? Color.FromRgb(0xE8, 0xE8, 0xEC)
                : Color.FromRgb(0x5C, 0x5C, 0x62);
            var bottom = light
                ? Color.FromRgb(0xB0, 0xB0, 0xB6)
                : Color.FromRgb(0x2A, 0x2A, 0x2E);
            var mid = light
                ? Color.FromRgb(0xCC, 0xCC, 0xD2)
                : Color.FromRgb(0x40, 0x40, 0x46);
            var note = light
                ? Color.FromRgb(0x4A, 0x4A, 0x50)
                : Color.FromRgb(0xD6, 0xD6, 0xDA);
            var gloss = light
                ? Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF);
            var edge = light
                ? Color.FromArgb(0x28, 0x00, 0x00, 0x00)
                : Color.FromArgb(0x66, 0x00, 0x00, 0x00);

            var fill = new LinearGradientBrush
            {
                StartPoint = new Point(0.12, 0),
                EndPoint = new Point(0.92, 1),
            };
            fill.GradientStops.Add(new GradientStop(top, 0));
            fill.GradientStops.Add(new GradientStop(mid, 0.48));
            fill.GradientStops.Add(new GradientStop(bottom, 1));
            fill.Freeze();
            dc.DrawRectangle(fill, null, rect);

            var shine = new LinearGradientBrush
            {
                StartPoint = new Point(0.5, 0),
                EndPoint = new Point(0.5, 0.46),
            };
            shine.GradientStops.Add(new GradientStop(gloss, 0));
            shine.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            shine.Freeze();
            dc.DrawRectangle(shine, null, new Rect(0, 0, PixelSize, PixelSize * 0.46));

            var vignette = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.5),
                GradientOrigin = new Point(0.5, 0.42),
                RadiusX = 0.78,
                RadiusY = 0.78,
            };
            vignette.GradientStops.Add(new GradientStop(Colors.Transparent, 0.55));
            vignette.GradientStops.Add(new GradientStop(edge, 1));
            vignette.Freeze();
            dc.DrawRectangle(vignette, null, rect);

            DrawEighthNote(dc, note);
        }

        var bmp = new RenderTargetBitmap(PixelSize, PixelSize, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    private static void DrawEighthNote(DrawingContext dc, Color color)
    {
        const double s = PixelSize;
        var brush = new SolidColorBrush(color);
        brush.Freeze();

        dc.PushTransform(new TranslateTransform(s * 0.50, s * 0.52));
        dc.PushTransform(new RotateTransform(-16));

        dc.DrawEllipse(brush, null, new Point(-16, 30), 36, 24);

        const double stemW = 8;
        const double stemX = 14;
        const double stemTop = -70;
        dc.DrawRoundedRectangle(
            brush,
            null,
            new Rect(stemX, stemTop, stemW, 102),
            1.6,
            1.6);

        var flag = new StreamGeometry();
        using (var ctx = flag.Open())
        {
            ctx.BeginFigure(new Point(stemX + stemW, stemTop), isFilled: true, isClosed: true);
            ctx.BezierTo(
                new Point(stemX + 64, stemTop + 4),
                new Point(stemX + 70, stemTop + 46),
                new Point(stemX + 24, stemTop + 76),
                true,
                false);
            ctx.BezierTo(
                new Point(stemX + 54, stemTop + 40),
                new Point(stemX + 46, stemTop + 16),
                new Point(stemX + stemW, stemTop + 32),
                true,
                false);
        }

        flag.Freeze();
        dc.DrawGeometry(brush, null, flag);

        dc.Pop();
        dc.Pop();
    }
}
