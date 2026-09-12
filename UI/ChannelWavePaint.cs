using System.Windows.Media;
using MgaSonicAnvil.Audio;

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
        return muted ? Dim(color) : color;
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

    public static int Dim(int bgra)
    {
        var a = (bgra >> 24) & 0xFF;
        var r = (bgra >> 16) & 0xFF;
        var g = (bgra >> 8) & 0xFF;
        var b = bgra & 0xFF;
        return (a << 24) | ((r * 2 / 7) << 16) | ((g * 2 / 7) << 8) | (b * 2 / 7);
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
