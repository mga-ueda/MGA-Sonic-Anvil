using System.Windows.Media;

namespace MgaSonicAnvil.UI;

/// <summary>RGB と HSV の往復。ピッカーの正方形／色相バー用。</summary>
internal readonly struct HsvColor : IEquatable<HsvColor>
{
    public HsvColor(double h, double s, double v)
    {
        H = WrapHue(h);
        S = Clamp01(s);
        V = Clamp01(v);
    }

    public double H { get; }

    public double S { get; }

    public double V { get; }

    public HsvColor WithHue(double h) => new(h, S, V);

    public HsvColor WithSaturationValue(double s, double v) => new(H, s, v);

    public static HsvColor FromRgb(Color color)
    {
        var r = color.R / 255d;
        var g = color.G / 255d;
        var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        var hue = 0d;
        if (delta > 0d)
        {
            if (max == r)
            {
                hue = (g - b) / delta + (g < b ? 6d : 0d);
            }
            else if (max == g)
            {
                hue = (b - r) / delta + 2d;
            }
            else
            {
                hue = (r - g) / delta + 4d;
            }

            hue *= 60d;
        }

        var sat = max <= 0d ? 0d : delta / max;
        return new HsvColor(hue, sat, max);
    }

    public Color ToRgb()
    {
        var sector = H / 60d;
        var hi = (int)Math.Floor(sector) % 6;
        var f = sector - Math.Floor(sector);
        var p = V * (1d - S);
        var q = V * (1d - f * S);
        var t = V * (1d - (1d - f) * S);
        var (r, g, b) = hi switch
        {
            0 => (V, t, p),
            1 => (q, V, p),
            2 => (p, V, t),
            3 => (p, q, V),
            4 => (t, p, V),
            _ => (V, p, q),
        };
        return Color.FromRgb(ToByte(r), ToByte(g), ToByte(b));
    }

    public Color HueRgb() => new HsvColor(H, 1d, 1d).ToRgb();

    public bool Equals(HsvColor other) =>
        H.Equals(other.H) && S.Equals(other.S) && V.Equals(other.V);

    public override bool Equals(object? obj) => obj is HsvColor other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(H, S, V);

    public static bool operator ==(HsvColor left, HsvColor right) => left.Equals(right);

    public static bool operator !=(HsvColor left, HsvColor right) => !left.Equals(right);

    private static double WrapHue(double h)
    {
        if (double.IsNaN(h) || double.IsInfinity(h))
        {
            return 0d;
        }

        h %= 360d;
        return h < 0d ? h + 360d : h;
    }

    private static double Clamp01(double value) =>
        double.IsNaN(value) ? 0d : Math.Clamp(value, 0d, 1d);

    private static byte ToByte(double unit) =>
        (byte)Math.Clamp((int)Math.Round(unit * 255d), 0, 255);
}
