namespace MgaSonicAnvil.UI;

/// <summary>
/// プレイヤー波形。レーン中心が明るく、上下端へやや暗くなる。
/// </summary>
internal static class WaveformLaneGradient
{
    /// <summary>端での輝度倍率。1 がそのまま、小さいほど暗い。</summary>
    public const double EdgeBrightness = 0.55;

    /// <summary>
    /// mid を中心に、|y-mid| / halfHeight で端へ暗い色へ寄せる。
    /// </summary>
    public static int Shade(int bgra, double y, double mid, double halfHeight)
    {
        if (halfHeight <= 1e-6)
        {
            return bgra;
        }

        var t = Math.Clamp(Math.Abs(y - mid) / halfHeight, 0, 1);
        var scale = 1d + (EdgeBrightness - 1d) * t;
        var a = (bgra >> 24) & 0xFF;
        var r = ScaleChannel((bgra >> 16) & 0xFF, scale);
        var g = ScaleChannel((bgra >> 8) & 0xFF, scale);
        var b = ScaleChannel(bgra & 0xFF, scale);
        return (a << 24) | (r << 16) | (g << 8) | b;
    }

    private static int ScaleChannel(int channel, double scale) =>
        Math.Clamp((int)Math.Round(channel * scale), 0, 255);
}
