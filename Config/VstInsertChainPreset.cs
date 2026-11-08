using System.Text.Json.Serialization;
using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.Config;

/// <summary>インサート 1–8 の組を名前付きで覚えるプリセット。</summary>
internal sealed class VstInsertChainPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public VstInsertSlotSnapshot[] Slots { get; set; } = [];

    public VstInsertChainPreset Clone()
    {
        var slots = DocumentSessionStore.NormalizeDefaultInserts(Slots);
        var copy = new VstInsertSlotSnapshot[slots.Length];
        for (var i = 0; i < slots.Length; i++)
        {
            var src = slots[i];
            copy[i] = new VstInsertSlotSnapshot
            {
                PluginPath = src.PluginPath,
                ClassId = src.ClassId,
                DisplayName = src.DisplayName,
                Bypassed = src.Bypassed,
                StateBase64 = src.StateBase64,
            };
        }

        return new VstInsertChainPreset
        {
            Id = Id,
            Name = Name ?? string.Empty,
            Slots = copy,
        };
    }

    public static VstInsertChainPreset Create(string name, VstInsertSlotSnapshot[] slots) =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = (name ?? string.Empty).Trim(),
            Slots = DocumentSessionStore.NormalizeDefaultInserts(slots),
        };

    public static VstInsertChainPreset[] NormalizeList(IEnumerable<VstInsertChainPreset>? presets)
    {
        if (presets is null)
        {
            return [];
        }

        var list = new List<VstInsertChainPreset>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var preset in presets)
        {
            if (preset is null || string.IsNullOrWhiteSpace(preset.Name))
            {
                continue;
            }

            var clone = preset.Clone();
            if (string.IsNullOrWhiteSpace(clone.Id) || !seen.Add(clone.Id))
            {
                clone.Id = Guid.NewGuid().ToString("N");
                seen.Add(clone.Id);
            }

            clone.Name = clone.Name.Trim();
            list.Add(clone);
        }

        return [.. list];
    }

    public static VstInsertChainPreset? FindById(IEnumerable<VstInsertChainPreset>? presets, string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || presets is null)
        {
            return null;
        }

        foreach (var preset in presets)
        {
            if (preset is not null
                && string.Equals(preset.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return preset;
            }
        }

        return null;
    }

    public static VstInsertChainPreset? FindByName(IEnumerable<VstInsertChainPreset>? presets, string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || presets is null)
        {
            return null;
        }

        var trimmed = name.Trim();
        foreach (var preset in presets)
        {
            if (preset is not null
                && string.Equals(preset.Name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return preset;
            }
        }

        return null;
    }
}

[JsonSerializable(typeof(VstInsertChainPreset))]
[JsonSerializable(typeof(VstInsertChainPreset[]))]
[JsonSourceGenerationOptions(WriteIndented = false)]
internal partial class VstInsertChainPresetJsonContext : JsonSerializerContext;
