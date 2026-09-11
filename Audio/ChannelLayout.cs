namespace MgaSonicAnvil.Audio;

/// <summary>
/// アプリが用意するスピーカー配置。ファイルの正体は推測しない。
/// 波形の表示名は、スピーカー→波形番号の割り当てが指すレーンだけ配置のラベル。
/// </summary>
internal readonly record struct ChannelLayout(string Id, int Channels, string[] Labels)
{
    public const int MaxChannels = 16;

    public static readonly ChannelLayout Mono = Named("Mono", "M");
    public static readonly ChannelLayout Stereo = Named("Stereo", "L", "R");

    public static readonly ChannelLayout[] All =
    [
        Mono,
        Stereo,
        Named("2.1", "L", "R", "LFE"),
        Named("3.0", "L", "C", "R"),
        Named("3.1", "L", "C", "R", "LFE"),
        Named("Quad", "L", "R", "Ls", "Rs"),
        Named("Quad-Back", "L", "R", "Lb", "Rb"),
        Named("LCRS", "L", "C", "R", "S"),
        Named("4.1", "L", "R", "LFE", "Ls", "Rs"),
        Named("5.0", "L", "R", "C", "Ls", "Rs"),
        Named("5.1", "L", "R", "C", "LFE", "Ls", "Rs"),
        Named("5.1-Side", "L", "R", "C", "LFE", "Sl", "Sr"),
        Named("6.0", "L", "R", "C", "Ls", "Rs", "Cs"),
        Named("6.1", "L", "R", "C", "LFE", "Ls", "Rs", "Cs"),
        Named("7.0", "L", "R", "C", "Ls", "Rs", "Lb", "Rb"),
        Named("7.1", "L", "R", "C", "LFE", "Ls", "Rs", "Lb", "Rb"),
        Named("7.1-SDDS", "L", "R", "C", "LFE", "Ls", "Rs", "Lc", "Rc"),
        Named("8.0", "L", "R", "C", "Ls", "Rs", "Lb", "Rb", "Cs"),
        Named("4.0.2", "L", "R", "Ls", "Rs", "Tfl", "Tfr"),
        Named("5.0.2", "L", "R", "C", "Ls", "Rs", "Tfl", "Tfr"),
        Named("5.1.2", "L", "R", "C", "LFE", "Ls", "Rs", "Tfl", "Tfr"),
        Named("5.1.2-Side", "L", "R", "C", "LFE", "Ls", "Rs", "Tsl", "Tsr"),
        Named("7.0.2", "L", "R", "C", "Ls", "Rs", "Lb", "Rb", "Tfl", "Tfr"),
        Named("7.1.2", "L", "R", "C", "LFE", "Ls", "Rs", "Lb", "Rb", "Tfl", "Tfr"),
        Named("5.1.4", "L", "R", "C", "LFE", "Ls", "Rs", "Tfl", "Tfr", "Tbl", "Tbr"),
        Named("7.1.4", "L", "R", "C", "LFE", "Ls", "Rs", "Lb", "Rb", "Tfl", "Tfr", "Tbl", "Tbr"),
        Named("7.1.6", "L", "R", "C", "LFE", "Ls", "Rs", "Lb", "Rb", "Tfl", "Tfr", "Tsl", "Tsr", "Tbl", "Tbr"),
        Named("9.1.4", "L", "R", "C", "LFE", "Lw", "Rw", "Ls", "Rs", "Lb", "Rb", "Tfl", "Tfr", "Tbl", "Tbr"),
        Named("9.1.6", "L", "R", "C", "LFE", "Lw", "Rw", "Ls", "Rs", "Lb", "Rb", "Tfl", "Tfr", "Tsl", "Tsr", "Tbl", "Tbr"),
    ];

    public static ChannelLayout Default => Stereo;

    /// <summary>プルダウン用。Mono / Stereo は名前だけ。5.1 などは本数を足さず "5.1 ch"。</summary>
    public string MenuLabel =>
        EnglishName is "Mono" or "Stereo"
            ? EnglishName
            : char.IsAsciiDigit(EnglishName[0]) ? $"{EnglishName} ch" : $"{EnglishName} {Channels}ch";

    public string EnglishName => Id switch
    {
        "Mono" => "Mono",
        "Stereo" => "Stereo",
        "Quad-Back" => "Quad Back",
        "5.1-Side" => "5.1 Side",
        "7.1-SDDS" => "7.1 SDDS",
        "5.1.2-Side" => "5.1.2 Side",
        _ => Id,
    };

    public static ChannelLayout FromChannels(int channels)
    {
        channels = Math.Clamp(channels < 1 ? 1 : channels, 1, MaxChannels);
        return new(IdFor(channels), channels, ChannelLabels.Numbered(channels));
    }

    public static bool TryGet(string? id, out ChannelLayout layout)
    {
        layout = Default;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        foreach (var item in All)
        {
            if (item.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                layout = item;
                return true;
            }
        }

        return false;
    }

    public static ChannelLayout Parse(string? id)
    {
        if (TryGet(id, out var named))
        {
            return named;
        }

        if (id is not null
            && id.StartsWith("Ch", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(id[2..], out var numbered))
        {
            return FromChannels(numbered);
        }

        return Default;
    }

    /// <summary>ファイル用。チャンネル数だけ見て番号ラベル。配置は当てない。</summary>
    public static ChannelLayout Guess(int channels) => FromChannels(channels);

    /// <summary>
    /// 割り当てた波形レーンだけ配置の名前。空の割り当ては 1 から順（初期値）。
    /// ヘッダや本数から 5.1 などを当てない。
    /// </summary>
    public static ChannelLayout ForFile(int fileChannels, ChannelLayout speaker, int[]? fileChannelMap = null)
    {
        fileChannels = Math.Clamp(fileChannels < 1 ? 1 : fileChannels, 1, MaxChannels);
        var labels = ChannelLabels.Numbered(fileChannels);
        var map = ChannelRouter.Normalize(fileChannelMap, speaker.Channels, fileChannels);
        for (var i = 0; i < map.Length; i++)
        {
            var lane = map[i];
            if ((uint)lane >= (uint)fileChannels)
            {
                continue;
            }

            if (labels[lane] == ChannelLabels.Name(lane, fileChannels))
            {
                labels[lane] = speaker.LabelAt(i);
            }
        }

        var numbered = FromChannels(fileChannels);
        return new ChannelLayout(numbered.Id, fileChannels, labels);
    }

    /// <summary>同じ本数のとき、いちばん普通の配置。移行用。</summary>
    public static ChannelLayout PreferredForChannels(int channels) =>
        Math.Clamp(channels < 1 ? 2 : channels, 1, MaxChannels) switch
        {
            1 => Mono,
            2 => Stereo,
            3 => Parse("2.1"),
            4 => Parse("Quad"),
            5 => Parse("5.0"),
            6 => Parse("5.1"),
            7 => Parse("6.1"),
            8 => Parse("7.1"),
            9 => Parse("7.0.2"),
            10 => Parse("7.1.2"),
            12 => Parse("7.1.4"),
            14 => Parse("7.1.6"),
            16 => Parse("9.1.6"),
            _ => FromChannels(channels),
        };

    public string LabelAt(int index) =>
        (uint)index < (uint)Labels.Length ? Labels[index] : ChannelLabels.Name(index, Channels);

    private static ChannelLayout Named(string id, params string[] labels) =>
        new(id, labels.Length, labels);

    private static string IdFor(int channels) => channels switch
    {
        1 => "Mono",
        2 => "Stereo",
        _ => $"Ch{channels}",
    };
}
