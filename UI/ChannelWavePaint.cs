using System.Windows.Media;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>3ch 以上の波形レーンの薄いチャンネル色。選択反転も同じ色を使う。</summary>
internal static class ChannelWavePaint
{
    public static int FillBgra(int channel, int channels, bool muted = false)
    {
        var baseColor = ToBgra(Theme.Get("WaveFillBrush"));
        var color = ChannelColors.UsesLaneTint(channels)
            ? Tint(baseColor, ToBgra(ChannelSwatch.Of(channel)))
            : baseColor;
        return muted ? Dim(color, MuteTheme()) : color;
    }

    public static int Tint(int baseColor, int tint)
    {
        const int amount = 120;
        const int rest = 255 - amount;
        var b = (((tint & 0xFF) * amount) + ((baseColor & 0xFF) * rest)) / 255;
        var g = ((((tint >> 8) & 0xFF) * amount) + (((baseColor >> 8) & 0xFF) * rest)) / 255;
        var r = ((((tint >> 16) & 0xFF) * amount) + (((baseColor >> 16) & 0xFF) * rest)) / 255;
        return b | (g << 8) | (r << 16) | (baseColor & unchecked((int)0xFF000000));
    }

    public static int Dim(int bgra) => Dim(bgra, MuteTheme());

    public static int Dim(int bgra, UiTheme theme)
    {
        var a = (bgra >> 24) & 0xFF;
        var r = (bgra >> 16) & 0xFF;
        var g = (bgra >> 8) & 0xFF;
        var b = bgra & 0xFF;
        if (theme == UiTheme.Light)
        {
            r = MixTowardLightGray(r);
            g = MixTowardLightGray(g);
            b = MixTowardLightGray(b);
        }
        else
        {
            r = r * 2 / 7;
            g = g * 2 / 7;
            b = b * 2 / 7;
        }

        return (a << 24) | (r << 16) | (g << 8) | b;
    }

    /// <summary>
    /// リソース上の波形背景が明るいときはライト扱い。
    /// Current だけだと、配色適用後に Dark のまま残ってミュートが黒くなる。
    /// </summary>
    private static UiTheme MuteTheme()
    {
        if (UiThemeService.Current == UiTheme.Light || IsLightWaveformBack())
        {
            return UiTheme.Light;
        }

        return UiTheme.Dark;
    }

    private static bool IsLightWaveformBack()
    {
        try
        {
            var back = Theme.Get("WaveformBackBrush");
            return (0.2126 * back.R) + (0.7152 * back.G) + (0.0722 * back.B) >= 128;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>ライトモードは #D2 のライトグレーへ寄せる。色相は薄く残す。</summary>
    private static int MixTowardLightGray(int channel)
    {
        const int gray = 0xD2;
        return (channel + gray * 7) / 8;
    }

    public static int LaneAt(int y, int height, int channels, double laneGapPx)
    {
        channels = Math.Max(1, channels);
        if (channels == 1 || height <= 0)
        {
            return 0;
        }

        var gap = Math.Max(0, laneGapPx);
        var laneHeight = (height - gap * (channels - 1)) / channels;
        var stride = laneHeight + gap;
        if (stride <= 0)
        {
            return 0;
        }

        return Math.Clamp((int)(y / stride), 0, channels - 1);
    }

    private static int ToBgra(Color color) =>
        color.B | (color.G << 8) | (color.R << 16) | (color.A << 24);
}
