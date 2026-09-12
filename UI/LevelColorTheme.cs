using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>スペアナのレベル色。グラデはダーク／ライトで同じ停止点。</summary>
internal static class LevelColorTheme
{
    public const double LightPeakHoldShade = 0.28;
    public const float GradientStepDb = 1f;

    public static ColorRgb Of(double db) => Sample(ColorNorm(db));

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

        return Blend(color, new ColorRgb(16, 62, 86), LightPeakHoldShade);
    }

    /// <summary>暗いシアン→中間→明るい端を二次ベジェでつなぎ、途中の段を作らない。</summary>
    private static ColorRgb Sample(double t)
    {
        t = Math.Clamp(t, 0, 1);
        var a = new ColorRgb(16, 62, 86);
        var b = new ColorRgb(58, 184, 232);
        var c = new ColorRgb(248, 254, 255);
        var u = 1 - t;
        return new ColorRgb(
            Channel((u * u * a.R) + (2 * u * t * b.R) + (t * t * c.R)),
            Channel((u * u * a.G) + (2 * u * t * b.G) + (t * t * c.G)),
            Channel((u * u * a.B) + (2 * u * t * b.B) + (t * t * c.B)));
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
