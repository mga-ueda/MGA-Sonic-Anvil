using System.Reflection;
using NAudio.Wave;

namespace MgaSonicAnvil.Audio;

/// <summary>デバイスが報告するポート名。取れなければ番号付きの予備。</summary>
internal static class DevicePortNames
{
    private static readonly (int Mask, string Name)[] Speakers =
    [
        (0x1, "FL"),
        (0x2, "FR"),
        (0x4, "FC"),
        (0x8, "LFE"),
        (0x10, "BL"),
        (0x20, "BR"),
        (0x40, "FLC"),
        (0x80, "FRC"),
        (0x100, "BC"),
        (0x200, "SL"),
        (0x400, "SR"),
        (0x800, "TFL"),
        (0x1000, "TFC"),
        (0x2000, "TFR"),
        (0x4000, "TBL"),
        (0x8000, "TBC"),
        (0x10000, "TBR"),
        (0x20000, "TSL"),
        (0x40000, "TSR"),
    ];

    private static readonly FieldInfo? ChannelMaskField =
        typeof(WaveFormatExtensible).GetField("dwChannelMask", BindingFlags.Instance | BindingFlags.NonPublic);

    public static string[] Numbered(int count, bool input)
    {
        count = Math.Clamp(count < 1 ? ChannelLayout.MaxChannels : count, 1, ChannelLayout.MaxChannels);
        var names = new string[count];
        for (var i = 0; i < count; i++)
        {
            names[i] = NumberedAt(i, input);
        }

        return names;
    }

    public static string NumberedAt(int index, bool input) =>
        input ? $"In {index + 1}" : $"Out {index + 1}";

    public static string[] FromChannelCount(int count)
    {
        count = Math.Clamp(count, 1, ChannelLayout.MaxChannels);
        var names = new string[count];
        for (var i = 0; i < count; i++)
        {
            names[i] = ChannelLabels.Name(i, count);
        }

        return names;
    }

    public static string[] FromChannelMask(int mask, int channels)
    {
        channels = Math.Clamp(channels, 1, ChannelLayout.MaxChannels);
        if (mask == 0)
        {
            return FromChannelCount(channels);
        }

        var names = new List<string>(channels);
        foreach (var speaker in Speakers)
        {
            if ((mask & speaker.Mask) != 0)
            {
                names.Add(speaker.Name);
                if (names.Count == channels)
                {
                    return [.. names];
                }
            }
        }

        while (names.Count < channels)
        {
            names.Add($"Ch{names.Count + 1}");
        }

        return [.. names];
    }

    public static int ReadChannelMask(WaveFormat format)
    {
        if (format is not WaveFormatExtensible extensible || ChannelMaskField is null)
        {
            return 0;
        }

        return ChannelMaskField.GetValue(extensible) is int mask ? mask : 0;
    }

    public static string[] FromWaveFormat(WaveFormat format)
    {
        var channels = Math.Max(1, format.Channels);
        return FromChannelMask(ReadChannelMask(format), channels);
    }

    public static string[] FromAsio(AsioOut asio, bool input)
    {
        var count = input ? asio.DriverInputChannelCount : asio.DriverOutputChannelCount;
        count = Math.Clamp(count, 0, ChannelLayout.MaxChannels);
        if (count < 1)
        {
            return [];
        }

        var names = new string[count];
        for (var i = 0; i < count; i++)
        {
            var name = input ? asio.AsioInputChannelName(i) : asio.AsioOutputChannelName(i);
            names[i] = string.IsNullOrWhiteSpace(name) ? NumberedAt(i, input) : name.Trim();
        }

        return names;
    }
}
