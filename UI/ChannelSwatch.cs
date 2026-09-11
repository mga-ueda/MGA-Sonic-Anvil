using System.Windows.Media;
using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.UI;

/// <summary>3ch 以上の波形名・メーターバーと、サラウンド塗りで共有するチャンネル色。</summary>
internal static class ChannelSwatch
{
    public static Color Of(int channel, bool muted = false)
    {
        var rgb = muted ? ChannelColors.Dim(channel) : ChannelColors.At(channel);
        return Color.FromRgb(rgb.R, rgb.G, rgb.B);
    }

    public static Brush Brush(int channel, bool muted = false) =>
        WpfControlHelpers.FrozenBrush(Of(channel, muted));
}
