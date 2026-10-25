namespace MgaSonicAnvil.UI;

/// <summary>
/// 波形レーン。中心が明るく、上下端へやや暗くなる。
/// </summary>
internal static class WaveformLaneGradient
{
    /// <summary>端での輝度倍率。1 がそのまま、小さいほど暗い。</summary>
    public const double EdgeBrightness = 0.55;

    /// <summary>色設定の PlayerWaveFill を使う。</summary>
    public static int PlayerFill(int bgra)
    {
        var fill = PlayerChrome.Get("PlayerWaveFillBrush");
        var a = (bgra >> 24) & 0xFF;
        return (a << 24) | (fill.R << 16) | (fill.G << 8) | fill.B;
    }

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

    /// <summary>互換。テーマ引数は無視。</summary>
    public static int Shade(int bgra, double y, double mid, double halfHeight, Domain.UiTheme theme)
    {
        _ = theme;
        return Shade(bgra, y, mid, halfHeight);
    }

    /// <summary>互換。テーマ引数は無視。</summary>
    public static int PlayerFill(int bgra, Domain.UiTheme theme)
    {
        _ = theme;
        return PlayerFill(bgra);
    }

    internal static int MixTowardWhite(int bgra, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        var a = (bgra >> 24) & 0xFF;
        var r = MixChannel((bgra >> 16) & 0xFF, amount);
        var g = MixChannel((bgra >> 8) & 0xFF, amount);
        var b = MixChannel(bgra & 0xFF, amount);
        return (a << 24) | (r << 16) | (g << 8) | b;
    }

    private static int MixChannel(int channel, double amount) =>
        Math.Clamp((int)Math.Round(channel + ((255 - channel) * amount)), 0, 255);

    private static int ScaleChannel(int channel, double scale) =>
        Math.Clamp((int)Math.Round(channel * scale), 0, 255);
}
