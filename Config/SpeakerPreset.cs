using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.Config;

/// <summary>カタログ配置に対する、デバイスと入出力ポートの記憶。カスタム配置も可。</summary>
internal sealed class SpeakerPreset
{
    public const string DefaultId = "Stereo";
    public const string CustomIdPrefix = "custom:";

    public string Id { get; set; } = DefaultId;

    /// <summary>カタログは空。カスタムは表示名。</summary>
    public string Name { get; set; } = string.Empty;

    public int Channels { get; set; } = 2;

    public string AudioApi { get; set; } = "WaveOut";

    public string AudioDeviceId { get; set; } = string.Empty;

    public int[] RecordInputMap { get; set; } = [];

    public int[] PlaybackOutputMap { get; set; } = [];

    /// <summary>スピーカー → ファイルの波形番号。録音と再生で共通。空は 1 から順。</summary>
    public int[] FileChannelMap { get; set; } = [];

    /// <summary>カスタム配置のスピーカーラベル。カタログは空。</summary>
    public string[] Labels { get; set; } = [];

    public bool IsCustom =>
        Id.StartsWith(CustomIdPrefix, StringComparison.OrdinalIgnoreCase)
        || (Labels is { Length: > 0 } && !ChannelLayout.TryGet(Id, out _));

    public string DisplayName()
    {
        if (IsCustom)
        {
            return string.IsNullOrWhiteSpace(Name) ? Id : Name.Trim();
        }

        return ChannelLayout.Parse(Id).MenuLabel;
    }

    public ChannelLayout ToLayout()
    {
        if (IsCustom)
        {
            var labels = NormalizeCustomLabels(Labels, Channels);
            return new ChannelLayout(Id, labels.Length, labels);
        }

        return ChannelLayout.Parse(Id);
    }

    public int? ChannelMaskOrNull()
    {
        if (IsCustom)
        {
            return ChannelLayoutMasks.TryMaskFromLayout(ToLayout(), out var mask) ? mask : null;
        }

        return ChannelLayoutMasks.TryGetMask(Id, out var known) ? known : null;
    }

    public AudioOutputSettings ToAudioOutputSettings() =>
        new(AudioOutputSettings.ParseApi(AudioApi), AudioDeviceId ?? string.Empty);

    public void ApplyAudioOutput(AudioOutputSettings settings)
    {
        AudioApi = AudioOutputSettings.ToStoredValue(settings.Api);
        AudioDeviceId = settings.DeviceId ?? string.Empty;
    }

    /// <summary>配置の本数だけコピー。空は未設定（1 から順）。</summary>
    public static int[] SnapshotMap(int[]? map, int channels)
    {
        if (map is not { Length: > 0 })
        {
            return [];
        }

        channels = Math.Clamp(channels, 0, ChannelLayout.MaxChannels);
        if (channels <= 0)
        {
            return [];
        }

        var n = Math.Min(map.Length, channels);
        var copy = new int[n];
        Array.Copy(map, copy, n);
        return copy;
    }

    public static string NewCustomId() => CustomIdPrefix + Guid.NewGuid().ToString("N");

    public static bool IsCustomId(string? id) =>
        !string.IsNullOrWhiteSpace(id)
        && id.StartsWith(CustomIdPrefix, StringComparison.OrdinalIgnoreCase);

    public static SpeakerPreset CreateCustom(string name, IReadOnlyList<string> labels)
    {
        var normalized = NormalizeCustomLabels(labels, labels.Count);
        return new SpeakerPreset
        {
            Id = NewCustomId(),
            Name = string.IsNullOrWhiteSpace(name) ? "Custom" : name.Trim(),
            Channels = normalized.Length,
            Labels = normalized,
        };
    }

    public SpeakerPreset Clone() =>
        new()
        {
            Id = Id,
            Name = Name,
            Channels = Channels,
            AudioApi = AudioApi,
            AudioDeviceId = AudioDeviceId,
            RecordInputMap = [.. RecordInputMap ?? []],
            PlaybackOutputMap = [.. PlaybackOutputMap ?? []],
            FileChannelMap = [.. FileChannelMap ?? []],
            Labels = [.. Labels ?? []],
        };

    public SpeakerPreset Normalized()
    {
        AudioApi ??= "WaveOut";
        AudioDeviceId ??= string.Empty;
        Name ??= string.Empty;
        RecordInputMap ??= [];
        PlaybackOutputMap ??= [];
        FileChannelMap ??= [];
        Labels ??= [];
        if (IsCustom || IsCustomId(Id))
        {
            Labels = NormalizeCustomLabels(Labels, Channels > 0 ? Channels : Labels.Length);
            Channels = Labels.Length;
            Name = string.IsNullOrWhiteSpace(Name) ? "Custom" : Name.Trim();
            if (!IsCustomId(Id))
            {
                Id = NewCustomId();
            }

            return this;
        }

        if (ChannelLayout.TryGet(Id, out var layout))
        {
            Id = layout.Id;
            Channels = layout.Channels;
            Labels = [];
            return this;
        }

        var preferred = ChannelLayout.PreferredForChannels(Channels < 1 ? 2 : Channels);
        Id = preferred.Id;
        Channels = preferred.Channels;
        Labels = [];
        return this;
    }

    public static SpeakerPreset FromLayout(ChannelLayout layout) =>
        new()
        {
            Id = layout.Id,
            Name = string.Empty,
            Channels = layout.Channels,
        };

    public static string[] DefaultVisibleIds { get; } = [DefaultId];

    /// <summary>
    /// 空や不明は Stereo だけ。並びはカタログ順のあとカスタム。
    /// <paramref name="catalog"/> があればカスタム Id も残す。
    /// </summary>
    public static string[] NormalizeVisibleIds(
        IEnumerable<string>? ids,
        IEnumerable<SpeakerPreset>? catalog = null)
    {
        var want = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var customById = new Dictionary<string, SpeakerPreset>(StringComparer.OrdinalIgnoreCase);
        if (catalog is not null)
        {
            foreach (var preset in catalog)
            {
                if (preset is not null && preset.IsCustom)
                {
                    customById[preset.Id] = preset;
                }
            }
        }

        if (ids is not null)
        {
            foreach (var id in ids)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                if (ChannelLayout.TryGet(id, out var layout))
                {
                    want.Add(layout.Id);
                }
                else if (customById.ContainsKey(id) || IsCustomId(id))
                {
                    want.Add(id);
                }
            }
        }

        if (want.Count == 0)
        {
            return [.. DefaultVisibleIds];
        }

        var ordered = new List<string>(want.Count);
        foreach (var layout in ChannelLayout.All)
        {
            if (want.Contains(layout.Id))
            {
                ordered.Add(layout.Id);
            }
        }

        foreach (var preset in catalog ?? [])
        {
            if (preset is not null && preset.IsCustom && want.Contains(preset.Id))
            {
                ordered.Add(preset.Id);
            }
        }

        foreach (var id in want)
        {
            if (!ordered.Contains(id, StringComparer.OrdinalIgnoreCase))
            {
                ordered.Add(id);
            }
        }

        return ordered.Count == 0 ? [.. DefaultVisibleIds] : [.. ordered];
    }

    public static string[] NormalizeCustomLabels(IReadOnlyList<string>? labels, int channels)
    {
        channels = Math.Clamp(channels < 1 ? (labels?.Count ?? 0) : channels, 1, ChannelLayout.MaxChannels);
        var result = new string[channels];
        for (var i = 0; i < channels; i++)
        {
            var raw = labels is not null && i < labels.Count ? labels[i]?.Trim() : null;
            if (string.IsNullOrWhiteSpace(raw))
            {
                result[i] = ChannelLabels.Name(i, channels);
                continue;
            }

            var canonical = ChannelMaskBits.CanonicalLayoutLabel(raw);
            result[i] = string.IsNullOrEmpty(canonical) ? raw : canonical;
        }

        return result;
    }

    /// <summary>
    /// 有効な定義の本数に合わせる。1〜2ch は Stereo。候補が無いか曖昧なら null。
    /// チャンネルマスクが有効な配置（カスタム含む）に一致すれば、同本数の維持より先にそちらへ寄せる。
    /// 今の配置がすでに同じ本数なら、1〜2ch→Stereo とマスク一致以外は切り替えない。
    /// </summary>
    public static string? ResolveAutoSpeakerId(
        int fileChannels,
        IEnumerable<SpeakerPreset>? presets,
        IEnumerable<string>? visibleIds,
        string? currentId,
        int channelMask = 0)
    {
        if (fileChannels < 1)
        {
            return null;
        }

        fileChannels = Math.Clamp(fileChannels, 1, ChannelLayout.MaxChannels);
        var catalog = presets as SpeakerPreset[] ?? presets?.ToArray() ?? [];
        if (catalog.Length == 0)
        {
            return null;
        }

        var visible = new HashSet<string>(NormalizeVisibleIds(visibleIds, catalog), StringComparer.OrdinalIgnoreCase);
        if (channelMask != 0
            && TryResolveFromChannelMask(channelMask, fileChannels, catalog, visible, currentId, out var fromMask))
        {
            return fromMask;
        }

        if (fileChannels is 1 or 2 && visible.Contains(DefaultId))
        {
            return SameId(currentId, DefaultId) ? null : DefaultId;
        }

        var current = FindById(catalog, currentId);
        if (current is not null && current.Channels == fileChannels)
        {
            return null;
        }

        var matches = new List<SpeakerPreset>();
        foreach (var preset in catalog)
        {
            if (preset is null
                || preset.Channels != fileChannels
                || !visible.Contains(preset.Id))
            {
                continue;
            }

            matches.Add(preset);
        }

        if (matches.Count == 0)
        {
            return null;
        }

        if (matches.Count == 1)
        {
            return SameId(currentId, matches[0].Id) ? null : matches[0].Id;
        }

        var preferredId = ChannelLayout.PreferredForChannels(fileChannels).Id;
        foreach (var match in matches)
        {
            if (match.Id.Equals(preferredId, StringComparison.OrdinalIgnoreCase))
            {
                return SameId(currentId, match.Id) ? null : match.Id;
            }
        }

        return null;
    }

    private static bool TryResolveFromChannelMask(
        int channelMask,
        int fileChannels,
        SpeakerPreset[] catalog,
        HashSet<string> visible,
        string? currentId,
        out string? id)
    {
        id = null;
        var matches = new List<SpeakerPreset>();
        foreach (var preset in catalog)
        {
            if (preset is null
                || preset.Channels != fileChannels
                || !visible.Contains(preset.Id)
                || preset.ChannelMaskOrNull() is not int mask
                || mask != channelMask)
            {
                continue;
            }

            matches.Add(preset);
        }

        if (matches.Count == 0)
        {
            return false;
        }

        foreach (var match in matches)
        {
            if (SameId(currentId, match.Id))
            {
                id = null;
                return true;
            }
        }

        // 同マスクならカスタムを優先（ユーザー定義の並びに寄せる）。
        foreach (var match in matches)
        {
            if (match.IsCustom)
            {
                id = match.Id;
                return true;
            }
        }

        var knownId = ChannelLayoutMasks.MatchPresetId(channelMask);
        if (knownId is not null)
        {
            foreach (var match in matches)
            {
                if (match.Id.Equals(knownId, StringComparison.OrdinalIgnoreCase))
                {
                    id = match.Id;
                    return true;
                }
            }
        }

        id = matches[0].Id;
        return true;
    }

    /// <summary>
    /// ファイルの本数／チャンネルマスクと、いまのスピーカー設定が食い違い、
    /// プレビューや再生の割り当てが危ういとき true。
    /// マスクが示す配置がメニューに無い（切り替え不可）場合も含む。
    /// </summary>
    public static bool HasPreviewLayoutConflict(
        int fileChannels,
        int channelMask,
        string? activeId,
        IEnumerable<SpeakerPreset>? presets)
    {
        if (fileChannels < 1)
        {
            return false;
        }

        fileChannels = Math.Clamp(fileChannels, 1, ChannelLayout.MaxChannels);
        var catalog = presets as SpeakerPreset[] ?? presets?.ToArray() ?? [];
        var active = FindById(catalog, activeId);
        var activeChannels = active?.Channels
            ?? (ChannelLayout.TryGet(activeId, out var parsed) ? parsed.Channels : 0);
        if (activeChannels != fileChannels)
        {
            return true;
        }

        if (channelMask == 0)
        {
            return false;
        }

        if (active is not null && active.ChannelMaskOrNull() is int activeMask && activeMask == channelMask)
        {
            return false;
        }

        var maskId = ChannelLayoutMasks.MatchPresetId(channelMask);
        if (maskId is not null)
        {
            return !SameId(activeId, maskId);
        }

        return ChannelMaskBits.PopCount(channelMask) != fileChannels;
    }

    /// <summary>チェックした配置。今使っている配置は、外していても残す。</summary>
    public static SpeakerPreset[] FilterMenu(
        IEnumerable<SpeakerPreset> presets,
        IEnumerable<string>? visibleIds,
        string? activeId)
    {
        var list = presets as SpeakerPreset[] ?? presets.ToArray();
        var allowed = new HashSet<string>(NormalizeVisibleIds(visibleIds, list), StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(activeId))
        {
            if (ChannelLayout.TryGet(activeId, out var active))
            {
                allowed.Add(active.Id);
            }
            else if (FindById(list, activeId) is not null)
            {
                allowed.Add(activeId);
            }
        }

        var menu = new List<SpeakerPreset>();
        foreach (var preset in list)
        {
            if (preset is not null && allowed.Contains(preset.Id))
            {
                menu.Add(preset);
            }
        }

        return [.. menu];
    }

    public static SpeakerPreset[] CreateCatalog()
    {
        var catalog = new SpeakerPreset[ChannelLayout.All.Length];
        for (var i = 0; i < catalog.Length; i++)
        {
            catalog[i] = FromLayout(ChannelLayout.All[i]);
        }

        return catalog;
    }

    public static SpeakerPreset[] CloneAll(IEnumerable<SpeakerPreset>? source) =>
        MergeCatalog(source);

    /// <summary>カタログを正本にし、保存済みのデバイスとマップ／カスタム配置を載せる。</summary>
    public static SpeakerPreset[] MergeCatalog(IEnumerable<SpeakerPreset>? saved)
    {
        var catalog = CreateCatalog();
        if (saved is null)
        {
            return catalog;
        }

        var byId = new Dictionary<string, SpeakerPreset>(StringComparer.OrdinalIgnoreCase);
        var customs = new List<SpeakerPreset>();
        var leftovers = new List<SpeakerPreset>();
        foreach (var item in saved)
        {
            if (item is null)
            {
                continue;
            }

            var clone = item.Clone();
            if (clone.IsCustom || IsCustomId(clone.Id))
            {
                customs.Add(clone.Normalized());
                continue;
            }

            if (ChannelLayout.TryGet(clone.Id, out var layout))
            {
                clone.Id = layout.Id;
                clone.Channels = layout.Channels;
                clone.Labels = [];
                byId[clone.Id] = clone;
                continue;
            }

            leftovers.Add(clone.Normalized());
        }

        foreach (var item in catalog)
        {
            if (!byId.TryGetValue(item.Id, out var overlay))
            {
                continue;
            }

            item.AudioApi = overlay.AudioApi ?? "WaveOut";
            item.AudioDeviceId = overlay.AudioDeviceId ?? string.Empty;
            item.RecordInputMap = SnapshotMap(overlay.RecordInputMap, item.Channels);
            item.PlaybackOutputMap = SnapshotMap(overlay.PlaybackOutputMap, item.Channels);
            item.FileChannelMap = SnapshotMap(overlay.FileChannelMap, item.Channels);
        }

        foreach (var extra in leftovers)
        {
            var target = FindById(catalog, extra.Id) ?? FindById(catalog, ChannelLayout.PreferredForChannels(extra.Channels).Id);
            if (target is null)
            {
                continue;
            }

            if (target.PlaybackOutputMap.Length == 0 && extra.PlaybackOutputMap is { Length: > 0 })
            {
                target.PlaybackOutputMap = SnapshotMap(extra.PlaybackOutputMap, target.Channels);
            }

            if (target.RecordInputMap.Length == 0 && extra.RecordInputMap is { Length: > 0 })
            {
                target.RecordInputMap = SnapshotMap(extra.RecordInputMap, target.Channels);
            }

            if (target.FileChannelMap.Length == 0 && extra.FileChannelMap is { Length: > 0 })
            {
                target.FileChannelMap = SnapshotMap(extra.FileChannelMap, target.Channels);
            }

            if (string.IsNullOrWhiteSpace(target.AudioDeviceId) && !string.IsNullOrWhiteSpace(extra.AudioDeviceId))
            {
                target.ApplyAudioOutput(extra.ToAudioOutputSettings());
            }
        }

        if (customs.Count == 0)
        {
            return catalog;
        }

        var merged = new SpeakerPreset[catalog.Length + customs.Count];
        catalog.CopyTo(merged, 0);
        customs.CopyTo(merged, catalog.Length);
        return merged;
    }

    public static string ResolveActiveId(IEnumerable<SpeakerPreset> catalog, string? requested, SpeakerPreset[]? saved)
    {
        if (FindById(catalog, requested) is { } found)
        {
            return found.IsCustom ? found.Id : ChannelLayout.Parse(found.Id).Id;
        }

        if (saved is not null)
        {
            foreach (var item in saved)
            {
                if (item is null || !item.Id.Equals(requested, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (item.IsCustom || IsCustomId(item.Id))
                {
                    return item.Normalized().Id;
                }

                return ChannelLayout.PreferredForChannels(item.Channels).Id;
            }
        }

        return DefaultId;
    }

    private static bool SameId(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && a.Equals(b, StringComparison.OrdinalIgnoreCase);

    private static SpeakerPreset? FindById(IEnumerable<SpeakerPreset> presets, string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        foreach (var preset in presets)
        {
            if (preset.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                return preset;
            }
        }

        return null;
    }
}
