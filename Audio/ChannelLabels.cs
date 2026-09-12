using System.Globalization;

namespace MgaSonicAnvil.Audio;

/// <summary>ファイル上のチャンネル番号。表示名は割り当て経由の ChannelLayout.ForFile。</summary>
internal static class ChannelLabels
{
    /// <summary>配置名は LFE / Tfl など。番号表示は 16 まで。</summary>
    public const int MaxChars = 3;

    public static int LongestCharCount()
    {
        var max = 0;
        foreach (var layout in ChannelLayout.All)
        {
            foreach (var label in layout.Labels)
            {
                max = Math.Max(max, label.Length);
            }
        }

        for (var i = 0; i < ChannelLayout.MaxChannels; i++)
        {
            max = Math.Max(max, Name(i, ChannelLayout.MaxChannels).Length);
        }

        return max;
    }

    public static string Name(int index, int channelCount)
    {
        _ = channelCount;
        return (index + 1).ToString(CultureInfo.InvariantCulture);
    }

    public static string[] Numbered(int channels)
    {
        channels = Math.Clamp(channels, 1, ChannelLayout.MaxChannels);
        var names = new string[channels];
        for (var i = 0; i < channels; i++)
        {
            names[i] = (i + 1).ToString(CultureInfo.InvariantCulture);
        }

        return names;
    }
}
