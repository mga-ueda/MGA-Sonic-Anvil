using System.Globalization;

namespace MgaSonicAnvil.Audio;

/// <summary>ファイル上のチャンネル番号。表示名は割り当て経由の ChannelLayout.ForFile。</summary>
internal static class ChannelLabels
{
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
