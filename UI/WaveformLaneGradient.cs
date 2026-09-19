using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>
/// プレイヤー波形。レーン中心が明るく、上下端へやや暗くなる。
/// ライトでは描画色を白へ寄せて薄くする。
/// </summary>
internal static class WaveformLaneGradient
{
    /// <summary>端での輝度倍率。1 がそのまま、小さいほど暗い。</summary>
    public const double EdgeBrightness = 0.55;

    /// <summary>
    /// ライトのプレイヤーでは、色設定の PlayerWaveFill を使う。
    /// </summary>
    public static int PlayerFill(int bgra, UiTheme theme)
    {
        var fill = PlayerChrome.Get("PlayerWaveFillBrush", theme);
        var a = (bgra >> 24) & 0xFF;
        return (a << 24) | (fill.R << 16) | (fill.G << 8) | fill.B;
    }

    /// <summary>
    /// mid を中心に、|y-mid| / halfHeight で端へ暗い色へ寄せる。
    /// ライトは端を白へ寄せて、ピークがインクのように沈まないようにする。
    /// </summary>
    public static int Shade(int bgra, double y, double mid, double halfHeight) =>
        Shade(bgra, y, mid, halfHeight, UiTheme.Dark);

    public static int Shade(int bgra, double y, double mid, double halfHeight, UiTheme theme)
    {
        if (halfHeight <= 1e-6)
        {
            return bgra;
        }

        var t = Math.Clamp(Math.Abs(y - mid) / halfHeight, 0, 1);
        if (theme == UiTheme.Light)
        {
            return MixTowardWhite(bgra, t * (1d - EdgeBrightness));
        }

        var scale = 1d + (EdgeBrightness - 1d) * t;
        var a = (bgra >> 24) & 0xFF;
        var r = ScaleChannel((bgra >> 16) & 0xFF, scale);
        var g = ScaleChannel((bgra >> 8) & 0xFF, scale);
        var b = ScaleChannel(bgra & 0xFF, scale);
        return (a << 24) | (r << 16) | (g << 8) | b;
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
