namespace MgaSonicAnvil.Audio;

/// <summary>
/// プラグイン名からチャンネル系統を推測し、トラック幅に合わせて候補を絞る。
/// 一般的な DAW と同様、チャンネル非依存のプラグインは常に選べる。
/// Waves 等の「同一製品の Mono / Stereo 併記」だけ、トラックに合う方を残す。
/// </summary>
internal enum VstChannelHint
{
    Unknown = 0,
    Mono = 1,
    Stereo = 2,
    /// <summary>Mono/Stereo・Mono→Stereo など、どちらのトラックでも使う想定。</summary>
    MonoOrStereo = 3,
    Surround50 = 5,
    Surround51 = 6,
}

internal static class VstPluginChannelFit
{
    private static readonly (string Suffix, VstChannelHint Hint)[] Suffixes =
    [
        (" mono/stereo", VstChannelHint.MonoOrStereo),
        (" mono to stereo", VstChannelHint.MonoOrStereo),
        (" (mono/stereo)", VstChannelHint.MonoOrStereo),
        (" 5.0/5.1", VstChannelHint.Surround51),
        (" 5.1", VstChannelHint.Surround51),
        (" 5.0", VstChannelHint.Surround50),
        (" (mono)", VstChannelHint.Mono),
        (" (stereo)", VstChannelHint.Stereo),
        (" mono", VstChannelHint.Mono),
        (" stereo", VstChannelHint.Stereo),
    ];

    public static VstChannelHint GuessFromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return VstChannelHint.Unknown;
        }

        var lower = name.Trim().ToLowerInvariant();
        foreach (var (suffix, hint) in Suffixes)
        {
            if (lower.EndsWith(suffix, StringComparison.Ordinal))
            {
                return hint;
            }
        }

        // 末尾以外（"LoAir 5.1" の途中表記など）
        if (lower.Contains("5.1", StringComparison.Ordinal))
        {
            return VstChannelHint.Surround51;
        }

        if (lower.Contains("5.0", StringComparison.Ordinal))
        {
            return VstChannelHint.Surround50;
        }

        if (lower.Contains("mono/stereo", StringComparison.Ordinal)
            || lower.Contains("mono to stereo", StringComparison.Ordinal))
        {
            return VstChannelHint.MonoOrStereo;
        }

        return VstChannelHint.Unknown;
    }

    /// <summary>併記グループ用のベース名（末尾の Mono / Stereo / 5.1 等を除く）。</summary>
    public static string BaseName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var trimmed = name.Trim();
        var lower = trimmed.ToLowerInvariant();
        foreach (var (suffix, _) in Suffixes)
        {
            if (lower.EndsWith(suffix, StringComparison.Ordinal))
            {
                return trimmed[..^suffix.Length].TrimEnd();
            }
        }

        return trimmed;
    }

    /// <summary>
    /// 単独プラグインは常に可。併記があるときだけ、ホストチャンネルに合う方を残す。
    /// </summary>
    public static List<T> FilterSiblings<T>(
        IEnumerable<T> entries,
        Func<T, string> nameSelector,
        int hostChannels)
    {
        hostChannels = Math.Max(1, hostChannels);
        var list = entries.ToList();
        if (list.Count <= 1)
        {
            return list;
        }

        var annotated = list
            .Select(e => (Entry: e, Name: nameSelector(e) ?? string.Empty))
            .Select(x => (x.Entry, x.Name, Hint: GuessFromName(x.Name), Base: BaseName(x.Name)))
            .ToList();

        var keep = new HashSet<T>();
        foreach (var group in annotated.GroupBy(a => a.Base, StringComparer.OrdinalIgnoreCase))
        {
            var members = group.ToList();
            if (members.Count == 1 || members.All(m => m.Hint == VstChannelHint.Unknown))
            {
                foreach (var m in members)
                {
                    keep.Add(m.Entry);
                }

                continue;
            }

            var preferred = PreferredHint(hostChannels);
            var exact = members.Where(m => m.Hint == preferred).ToList();
            if (exact.Count > 0)
            {
                foreach (var m in exact)
                {
                    keep.Add(m.Entry);
                }

                // Mono/Stereo や無印は併せて残す（どちらのトラックでも使える想定）。
                foreach (var m in members.Where(m =>
                             m.Hint is VstChannelHint.Unknown or VstChannelHint.MonoOrStereo))
                {
                    keep.Add(m.Entry);
                }

                continue;
            }

            // ピッタリが無い（例: モノラルトラックに Stereo しかない）→ DAW 同様すべて残す。
            foreach (var m in members)
            {
                keep.Add(m.Entry);
            }
        }

        return list.Where(keep.Contains).ToList();
    }

    private static VstChannelHint PreferredHint(int hostChannels) =>
        hostChannels switch
        {
            1 => VstChannelHint.Mono,
            5 => VstChannelHint.Surround50,
            >= 6 => VstChannelHint.Surround51,
            _ => VstChannelHint.Stereo,
        };
}
