using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.Config;

/// <summary>カタログ配置に対する、デバイスと入出力ポートの記憶。</summary>
internal sealed class SpeakerPreset
{
    public const string DefaultId = "Stereo";

    public string Id { get; set; } = DefaultId;

    /// <summary>保存互換。表示はカタログ名を使う。</summary>
    public string Name { get; set; } = string.Empty;

    public int Channels { get; set; } = 2;

    public string AudioApi { get; set; } = "WaveOut";

    public string AudioDeviceId { get; set; } = string.Empty;

    public int[] RecordInputMap { get; set; } = [];

    public int[] PlaybackOutputMap { get; set; } = [];

    /// <summary>スピーカー → ファイルの波形番号。録音と再生で共通。空は 1 から順。</summary>
    public int[] FileChannelMap { get; set; } = [];

    public string DisplayName() => ChannelLayout.Parse(Id).MenuLabel;

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
        };

    public SpeakerPreset Normalized()
    {
        AudioApi ??= "WaveOut";
        AudioDeviceId ??= string.Empty;
        Name ??= string.Empty;
        RecordInputMap ??= [];
        PlaybackOutputMap ??= [];
        FileChannelMap ??= [];
        if (ChannelLayout.TryGet(Id, out var layout))
        {
            Id = layout.Id;
            Channels = layout.Channels;
            return this;
        }

        var preferred = ChannelLayout.PreferredForChannels(Channels < 1 ? 2 : Channels);
        Id = preferred.Id;
        Channels = preferred.Channels;
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

    /// <summary>空や不明は Stereo だけ。並びはカタログ順。</summary>
    public static string[] NormalizeVisibleIds(IEnumerable<string>? ids)
    {
        var want = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (ids is not null)
        {
            foreach (var id in ids)
            {
                if (ChannelLayout.TryGet(id, out var layout))
                {
                    want.Add(layout.Id);
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

        return ordered.Count == 0 ? [.. DefaultVisibleIds] : [.. ordered];
    }

    /// <summary>チェックした配置。今使っている配置は、外していても残す。</summary>
    public static SpeakerPreset[] FilterMenu(
        IEnumerable<SpeakerPreset> presets,
        IEnumerable<string>? visibleIds,
        string? activeId)
    {
        var allowed = new HashSet<string>(NormalizeVisibleIds(visibleIds), StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(activeId) && ChannelLayout.TryGet(activeId, out var active))
        {
            allowed.Add(active.Id);
        }

        var menu = new List<SpeakerPreset>();
        foreach (var preset in presets)
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

    /// <summary>カタログを正本にし、保存済みのデバイスとマップだけ載せる。</summary>
    public static SpeakerPreset[] MergeCatalog(IEnumerable<SpeakerPreset>? saved)
    {
        var catalog = CreateCatalog();
        if (saved is null)
        {
            return catalog;
        }

        var byId = new Dictionary<string, SpeakerPreset>(StringComparer.OrdinalIgnoreCase);
        var leftovers = new List<SpeakerPreset>();
        foreach (var item in saved)
        {
            if (item is null)
            {
                continue;
            }

            var clone = item.Clone();
            if (ChannelLayout.TryGet(clone.Id, out var layout))
            {
                clone.Id = layout.Id;
                clone.Channels = layout.Channels;
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

        return catalog;
    }

    public static string ResolveActiveId(IEnumerable<SpeakerPreset> catalog, string? requested, SpeakerPreset[]? saved)
    {
        if (FindById(catalog, requested) is not null)
        {
            return ChannelLayout.Parse(requested).Id;
        }

        if (saved is not null)
        {
            foreach (var item in saved)
            {
                if (item is null || !item.Id.Equals(requested, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return ChannelLayout.PreferredForChannels(item.Channels).Id;
            }
        }

        return DefaultId;
    }

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
