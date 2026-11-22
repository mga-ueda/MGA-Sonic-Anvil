namespace MgaSonicAnvil.Audio;

/// <summary>アプリの ChannelLayout Id → 標準 channel bitmap。</summary>
internal static class ChannelLayoutMasks
{
    public readonly record struct Preset(string Id, string MenuLabel, int Mask, int Channels);

    /// <summary>ビットへ全部マップできる配置だけ。Mono は FL=0x1。</summary>
    public static IReadOnlyList<Preset> KnownPresets
    {
        get
        {
            var list = new List<Preset>(ChannelLayout.All.Length);
            foreach (var layout in ChannelLayout.All)
            {
                if (!TryMaskFromLayout(layout, out var mask))
                {
                    continue;
                }

                list.Add(new Preset(layout.Id, layout.MenuLabel, mask, layout.Channels));
            }

            return list;
        }
    }

    public static bool TryMaskFromLayout(ChannelLayout layout, out int mask)
    {
        mask = 0;
        foreach (var label in layout.Labels)
        {
            var bit = ChannelMaskBits.BitForLabel(label);
            if (bit == 0 || (mask & bit) != 0)
            {
                mask = 0;
                return false;
            }

            mask |= bit;
        }

        return mask != 0 || layout.Channels == 0;
    }

    public static bool TryGetMask(string? id, out int mask)
    {
        mask = 0;
        if (!ChannelLayout.TryGet(id, out var layout))
        {
            return false;
        }

        return TryMaskFromLayout(layout, out mask);
    }

    public static string? MatchPresetId(int mask)
    {
        if (mask == 0)
        {
            return null;
        }

        foreach (var preset in KnownPresets)
        {
            if (preset.Mask == mask)
            {
                return preset.Id;
            }
        }

        return null;
    }
}
