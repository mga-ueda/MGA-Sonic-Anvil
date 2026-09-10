using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Audio;

/// <summary>モノラルから Atmos ベッド 9.1.6 までのチャンネル構成。</summary>
internal readonly record struct ChannelLayout(string Id, int Channels, string[] Labels)
{
    public const int MaxChannels = 16;

    public static readonly ChannelLayout Mono = new("Mono", 1, ["M"]);
    public static readonly ChannelLayout Stereo = new("Stereo", 2, ["L", "R"]);
    public static readonly ChannelLayout Quad = new("Quad", 4, ["L", "R", "Ls", "Rs"]);
    public static readonly ChannelLayout FiveOh = new("5.0", 5, ["L", "R", "C", "Ls", "Rs"]);
    public static readonly ChannelLayout FiveOne = new("5.1", 6, ["L", "R", "C", "LFE", "Ls", "Rs"]);
    public static readonly ChannelLayout FiveOneTwo = new("5.1.2", 8, ["L", "R", "C", "LFE", "Ls", "Rs", "Tfl", "Tfr"]);
    public static readonly ChannelLayout SevenOne = new("7.1", 8, ["L", "R", "C", "LFE", "Ls", "Rs", "Lsr", "Rsr"]);
    public static readonly ChannelLayout FiveOneFour = new("5.1.4", 10, ["L", "R", "C", "LFE", "Ls", "Rs", "Tfl", "Tfr", "Tbl", "Tbr"]);
    public static readonly ChannelLayout SevenOneTwo = new("7.1.2", 10, ["L", "R", "C", "LFE", "Ls", "Rs", "Lsr", "Rsr", "Tfl", "Tfr"]);
    public static readonly ChannelLayout SevenOneFour = new("7.1.4", 12, ["L", "R", "C", "LFE", "Ls", "Rs", "Lsr", "Rsr", "Tfl", "Tfr", "Tbl", "Tbr"]);
    public static readonly ChannelLayout NineOneFour = new("9.1.4", 14, ["L", "R", "C", "LFE", "Ls", "Rs", "Lsr", "Rsr", "Lw", "Rw", "Tfl", "Tfr", "Tbl", "Tbr"]);
    public static readonly ChannelLayout NineOneSix = new("9.1.6", 16, ["L", "R", "C", "LFE", "Ls", "Rs", "Lsr", "Rsr", "Lw", "Rw", "Tfl", "Tfr", "Tsl", "Tsr", "Tbl", "Tbr"]);

    public static readonly ChannelLayout[] All =
    [
        Mono,
        Stereo,
        Quad,
        FiveOh,
        FiveOne,
        FiveOneTwo,
        SevenOne,
        FiveOneFour,
        SevenOneTwo,
        SevenOneFour,
        NineOneFour,
        NineOneSix,
    ];

    public static ChannelLayout Default => Stereo;

    public string DisplayName => $"{UiStrings.LabelChannelLayout(Id)} ({Channels}ch)";

    public static ChannelLayout Parse(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Default;
        }

        foreach (var layout in All)
        {
            if (layout.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                return layout;
            }
        }

        return Default;
    }

    public static ChannelLayout Guess(int channels) => channels switch
    {
        1 => Mono,
        2 => Stereo,
        4 => Quad,
        5 => FiveOh,
        6 => FiveOne,
        8 => SevenOne,
        10 => SevenOneTwo,
        12 => SevenOneFour,
        14 => NineOneFour,
        16 => NineOneSix,
        _ => channels < 1 ? Mono : new($"Ch{channels}", channels, Enumerable.Range(1, channels).Select(i => $"Ch{i}").ToArray()),
    };

    public string LabelAt(int index) =>
        (uint)index < (uint)Labels.Length ? Labels[index] : $"Ch{index + 1}";
}
