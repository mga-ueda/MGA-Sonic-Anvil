namespace MgaSonicAnvil.Audio;

/// <summary>
/// WAVE dwChannelMask / Core Audio channel bitmap の既知ビット。
/// ビット順＝ファイル内チャンネル順（低いビットが先）。
/// </summary>
internal static class ChannelMaskBits
{
    public const int UseChannelBitmapTag = 0x00010000;

    public static readonly (int Mask, string Name)[] Speakers =
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

    public static int PopCount(int mask)
    {
        var n = (uint)mask;
        var count = 0;
        while (n != 0)
        {
            n &= n - 1;
            count++;
        }

        return count;
    }

    /// <summary>マスクのセットビットを昇順で最大 <paramref name="channels"/> 本返す。</summary>
    public static int[] BitsFromMask(int mask, int channels)
    {
        channels = Math.Clamp(channels, 0, MaxChannels);
        if (channels == 0 || mask == 0)
        {
            return new int[channels];
        }

        var bits = new int[channels];
        var filled = 0;
        foreach (var (bit, _) in Speakers)
        {
            if ((mask & bit) == 0)
            {
                continue;
            }

            bits[filled++] = bit;
            if (filled == channels)
            {
                return bits;
            }
        }

        return bits;
    }

    /// <summary>チャンネル順のスピーカービットからマスクを作る。0 や重複は無視しない（呼び出し側で検証）。</summary>
    public static int MaskFromBits(IReadOnlyList<int> bits)
    {
        var mask = 0;
        foreach (var bit in bits)
        {
            mask |= bit;
        }

        return mask;
    }

    /// <summary>
    /// WAVE/CAF のビットマップ順（昇順）として妥当か。
    /// 全要素が非 0・ユニーク・ビット値がチャンネル順に厳密増加。
    /// </summary>
    public static bool IsValidWaveChannelOrder(IReadOnlyList<int> bits)
    {
        if (bits.Count == 0)
        {
            return true;
        }

        var seen = new HashSet<int>();
        var previous = 0;
        foreach (var bit in bits)
        {
            if (bit == 0 || !seen.Add(bit) || bit <= previous)
            {
                return false;
            }

            previous = bit;
        }

        return true;
    }

    public static string? NameForBit(int bit)
    {
        foreach (var (mask, name) in Speakers)
        {
            if (mask == bit)
            {
                return name;
            }
        }

        return null;
    }

    /// <summary>
    /// エディタ／配置カタログ向けの短いラベル（L/R/C/Sl…）。
    /// WAVE 名（FL/FC/SL）や大小のゆれをここに寄せる。
    /// </summary>
    public static string CanonicalLayoutLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        var trimmed = label.Trim();
        return BitForLabel(trimmed) switch
        {
            0x1 => "L",
            0x2 => "R",
            0x4 => "C",
            0x8 => "LFE",
            0x10 => "Ls",
            0x20 => "Rs",
            0x40 => "Lc",
            0x80 => "Rc",
            0x100 => "Cs",
            0x200 => "Sl",
            0x400 => "Sr",
            0x800 => "Tfl",
            0x1000 => "Tfc",
            0x2000 => "Tfr",
            0x4000 => "Tbl",
            0x8000 => "Tbc",
            0x10000 => "Tbr",
            0x20000 => "Tsl",
            0x40000 => "Tsr",
            _ => trimmed,
        };
    }

    /// <summary>コンボ用。ビット昇順の配置ラベル。</summary>
    public static string[] LayoutLabelChoices()
    {
        var list = new List<string>(Speakers.Length);
        foreach (var (bit, _) in Speakers)
        {
            var name = CanonicalLayoutLabel(NameForBit(bit));
            if (!string.IsNullOrEmpty(name))
            {
                list.Add(name);
            }
        }

        return [.. list];
    }

    public const int MaxChannels = 16;

    /// <summary>
    /// ChannelLayout ラベル → 標準ビット。
    /// Ls/Rs は WAVE 5.1 互換で Back、Sl/Sr は Side。未対応は 0。
    /// </summary>
    public static int BitForLabel(string? label) =>
        label?.Trim() switch
        {
            "M" or "L" or "FL" => 0x1,
            "R" or "FR" => 0x2,
            "C" or "FC" => 0x4,
            "LFE" => 0x8,
            "Ls" or "Lb" or "BL" => 0x10,
            "Rs" or "Rb" or "BR" => 0x20,
            "Lc" or "FLC" or "Lw" => 0x40,
            "Rc" or "FRC" or "Rw" => 0x80,
            "S" or "Cs" or "BC" => 0x100,
            "Sl" or "SL" => 0x200,
            "Sr" or "SR" => 0x400,
            "Tfl" or "TFL" => 0x800,
            "Tfc" or "TFC" => 0x1000,
            "Tfr" or "TFR" => 0x2000,
            "Tbl" or "TBL" => 0x4000,
            "Tbc" or "TBC" => 0x8000,
            "Tbr" or "TBR" => 0x10000,
            "Tsl" or "TSL" => 0x20000,
            "Tsr" or "TSR" => 0x40000,
            _ => 0,
        };
}
