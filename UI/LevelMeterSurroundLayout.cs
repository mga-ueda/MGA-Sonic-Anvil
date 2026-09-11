using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.UI;

/// <summary>
/// サラウンド時のバー配置。ステレオと同じ額縁
/// （中央のバー群 + 左右の目盛り列）になるよう、バー群は
/// <see cref="BarsBlockWidth"/> 以内に収めて中央へ置く。
/// </summary>
internal static class LevelMeterSurroundLayout
{
    /// <summary>左右の dB 目盛り列。ステレオと同じ 22px。</summary>
    public const double ScaleColWidth = 22;

    /// <summary>バー群の最大幅。ステレオの 14px × 4 本と同じ。</summary>
    public const double BarsBlockWidth = 56;

    /// <summary>1 本の最大幅（ステレオと同じ）と最小幅。</summary>
    public const double MaxBarWidth = 14;
    public const double MinBarWidth = 2;

    /// <summary>
    /// メーター本体がこれ以上広がらない列幅。2ch 以下は Peak/RMS の固定幅。
    /// 3ch 以上は目盛り 2 列 + バー最大太さ × 本数（既定幅を下回らない）。
    /// </summary>
    public static double FilledColumnWidth(int channels)
    {
        channels = Math.Clamp(channels, 1, ChannelLayout.MaxChannels);
        if (channels <= 2)
        {
            return DesignMetrics.LevelMeterWidth;
        }

        return Math.Max(DesignMetrics.LevelMeterWidth, (ScaleColWidth * 2) + (MaxBarWidth * channels));
    }

    /// <summary>バー群を等分。端数は切り捨ててバーを細くする。既定は 56px。</summary>
    public static double BarWidth(int channels) => BarWidth(channels, BarsBlockWidth);

    public static double BarWidth(int channels, double blockWidth)
    {
        if (channels <= 0)
        {
            return 0;
        }

        var block = Math.Max(0, blockWidth);
        return Math.Clamp(Math.Floor(block / channels), MinBarWidth, MaxBarWidth);
    }

    /// <summary>バー群を列の中央へ。左右に目盛り列ぶんの空きが必ず残る。</summary>
    public static double BarsLeft(double x, double width, double barWidth, int channels) =>
        x + Math.Floor((width - (barWidth * channels)) * 0.5);
}
