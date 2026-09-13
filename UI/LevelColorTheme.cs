using System.Windows;
using System.Windows.Media;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>スペアナとレベルメーター共通のレベル色。停止点は色設定の LevelGrad*。</summary>
internal static class LevelColorTheme
{
    public const double LightPeakHoldShade = 0.28;
    public const float GradientStepDb = 1f;

    public static readonly (double P, byte R, byte G, byte B)[] DefaultStops =
    [
        (0, 0, 92, 140),
        (0.26, 0, 113, 172),
        (0.55, 58, 184, 232),
        (0.82, 200, 239, 255),
        (1, 200, 239, 255),
    ];

    public static readonly string[] StopKeys =
    [
        "LevelGradFloorBrush",
        "LevelGradLowBrush",
        "LevelGradMidBrush",
        "LevelGradHighBrush",
        "LevelGradCeilBrush",
    ];

    public static ColorRgb Of(double db) => Bezier(ColorNorm(db));

    public static double ColorNorm(double db)
    {
        var span = SpectrumAnalyzer.CeilingDb - SpectrumAnalyzer.FloorDb;
        if (span <= 0)
        {
            return 0;
        }

        return Math.Clamp((db - SpectrumAnalyzer.FloorDb) / span, 0, 1);
    }

    public static ColorRgb PeakHold(double db) => PeakHold(db, UiThemeService.Current);

    public static ColorRgb PeakHold(double db, UiTheme theme)
    {
        var color = Of(db);
        if (theme != UiTheme.Light)
        {
            return color;
        }

        return Blend(color, StopColor(0), LightPeakHoldShade);
    }

    /// <summary>メーターバーと同じ 5 停止の線形補間。</summary>
    public static ColorRgb Sample(double t)
    {
        t = Math.Clamp(t, 0, 1);
        var i = 0;
        for (; i < DefaultStops.Length - 2; i++)
        {
            if (t <= DefaultStops[i + 1].P)
            {
                break;
            }
        }

        var a = StopColor(i);
        var b = StopColor(i + 1);
        var p0 = DefaultStops[i].P;
        var p1 = DefaultStops[i + 1].P;
        var denom = p1 - p0;
        var w = denom < 1e-9 ? 1 : (t - p0) / denom;
        return new ColorRgb(
            Channel(a.R + (b.R - a.R) * w),
            Channel(a.G + (b.G - a.G) * w),
            Channel(a.B + (b.B - a.B) * w));
    }

    /// <summary>スペアナ LED。暗い端・中間・明るい端の二次ベジェ。</summary>
    private static ColorRgb Bezier(double t)
    {
        t = Math.Clamp(t, 0, 1);
        var a = StopColor(0);
        var b = StopColor(2);
        var c = StopColor(4);
        var u = 1 - t;
        return new ColorRgb(
            Channel((u * u * a.R) + (2 * u * t * b.R) + (t * t * c.R)),
            Channel((u * u * a.G) + (2 * u * t * b.G) + (t * t * c.G)),
            Channel((u * u * a.B) + (2 * u * t * b.B) + (t * t * c.B)));
    }

    public static Color StopBrush(int index)
    {
        var color = StopColor(index);
        return Color.FromRgb(color.R, color.G, color.B);
    }

    private static ColorRgb StopColor(int index)
    {
        var fallback = DefaultStops[index];
        if (Application.Current is null)
        {
            return new ColorRgb(fallback.R, fallback.G, fallback.B);
        }

        try
        {
            var color = Theme.Get(StopKeys[index]);
            return new ColorRgb(color.R, color.G, color.B);
        }
        catch (InvalidOperationException)
        {
            return new ColorRgb(fallback.R, fallback.G, fallback.B);
        }
    }

    private static byte Channel(double value) =>
        (byte)Math.Clamp(Math.Round(value), 0, 255);

    private static ColorRgb Blend(ColorRgb a, ColorRgb b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new ColorRgb(
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));
    }
}
