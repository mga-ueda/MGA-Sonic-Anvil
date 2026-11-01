namespace MgaSonicAnvil.Audio;

/// <summary>文書ごとの VST インサート 1 スロット。永続化はしない（セッション寿命）。</summary>
internal sealed class VstInsertSlot
{
    public string? PluginPath { get; set; }

    /// <summary>VST3 クラス ID（16 バイトを Base64）。空ならパスから既定クラス。</summary>
    public string? ClassId { get; set; }

    public string? DisplayName { get; set; }

    public bool Bypassed { get; set; }

    /// <summary>プラグイン固有 state（IComponent getState）。</summary>
    public byte[]? StateBlob { get; set; }

    public bool Faulted { get; set; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(PluginPath);

    public VstInsertSlot Clone() => new()
    {
        PluginPath = PluginPath,
        ClassId = ClassId,
        DisplayName = DisplayName,
        Bypassed = Bypassed,
        StateBlob = StateBlob is null ? null : (byte[])StateBlob.Clone(),
        Faulted = Faulted,
    };

    public bool StructuralEquals(VstInsertSlot? other)
    {
        if (other is null)
        {
            return false;
        }

        return string.Equals(PluginPath, other.PluginPath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(ClassId ?? string.Empty, other.ClassId ?? string.Empty, StringComparison.Ordinal);
    }

    public string StructuralKey =>
        IsEmpty
            ? "-"
            : (PluginPath ?? string.Empty).ToLowerInvariant() + "\0" + (ClassId ?? string.Empty);
}

/// <summary>最大 8 スロットのインサートチェーン。</summary>
internal sealed class VstInsertChain
{
    public const int MaxSlots = 8;

    public VstInsertChain()
    {
        Slots = new VstInsertSlot[MaxSlots];
        for (var i = 0; i < MaxSlots; i++)
        {
            Slots[i] = new VstInsertSlot();
        }
    }

    public VstInsertSlot[] Slots { get; }

    public bool IsEmpty
    {
        get
        {
            for (var i = 0; i < Slots.Length; i++)
            {
                if (!Slots[i].IsEmpty)
                {
                    return false;
                }
            }

            return true;
        }
    }

    public string StructuralKey
    {
        get
        {
            var parts = new string[Slots.Length];
            for (var i = 0; i < Slots.Length; i++)
            {
                parts[i] = i + ":" + Slots[i].StructuralKey;
            }

            return string.Join("|", parts);
        }
    }

    public bool StructuralEquals(VstInsertChain? other)
    {
        if (other is null || other.Slots.Length != Slots.Length)
        {
            return false;
        }

        for (var i = 0; i < Slots.Length; i++)
        {
            if (!Slots[i].StructuralEquals(other.Slots[i]))
            {
                return false;
            }
        }

        return true;
    }

    public VstInsertChain Clone()
    {
        var copy = new VstInsertChain();
        for (var i = 0; i < Slots.Length; i++)
        {
            copy.Slots[i] = Slots[i].Clone();
        }

        return copy;
    }

    public void Clear()
    {
        for (var i = 0; i < Slots.Length; i++)
        {
            Slots[i] = new VstInsertSlot();
        }
    }

    public void ClearSlot(int index)
    {
        if ((uint)index >= (uint)Slots.Length)
        {
            return;
        }

        // 同一インスタンスを空にする。new 差し替えだと外部が掴んでいる参照への
        // 後続代入がチェーンに反映されず、差し替え後に UI が開けなくなる。
        var slot = Slots[index];
        slot.PluginPath = null;
        slot.ClassId = null;
        slot.DisplayName = null;
        slot.StateBlob = null;
        slot.Bypassed = false;
        slot.Faulted = false;
    }
}
