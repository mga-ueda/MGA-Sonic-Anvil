namespace MgaSonicAnvil.Domain;

/// <summary>
/// プレイヤー捨てキューの Undo／Redo。別ファイルへ移ると捨てる。
/// </summary>
internal sealed class PlayerScratchHistory
{
    private readonly Stack<PlayerScratchSnapshot> _undo = new();
    private readonly Stack<PlayerScratchSnapshot> _redo = new();

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    /// <summary>変更前の状態を積む。同じ内容なら積まない。</summary>
    public void PushBefore(PlayerScratchCues cues)
    {
        var snap = PlayerScratchSnapshot.Capture(cues);
        if (_undo.Count > 0 && _undo.Peek().Equals(snap))
        {
            return;
        }

        _undo.Push(snap);
        _redo.Clear();
    }

    /// <summary>既に撮った変更前スナップショットを積む（ドラッグ終了など）。</summary>
    public void Push(PlayerScratchSnapshot before, PlayerScratchCues current)
    {
        var after = PlayerScratchSnapshot.Capture(current);
        if (before.Equals(after))
        {
            return;
        }

        _undo.Push(before);
        _redo.Clear();
    }

    public bool TryUndo(PlayerScratchCues cues)
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        _redo.Push(PlayerScratchSnapshot.Capture(cues));
        _undo.Pop().Restore(cues);
        return true;
    }

    public bool TryRedo(PlayerScratchCues cues)
    {
        if (_redo.Count == 0)
        {
            return false;
        }

        _undo.Push(PlayerScratchSnapshot.Capture(cues));
        _redo.Pop().Restore(cues);
        return true;
    }
}

internal readonly struct PlayerScratchSnapshot : IEquatable<PlayerScratchSnapshot>
{
    public PlayerScratchSnapshot(MarkerSnapshot[] markers, long? abIn, long? abOut)
    {
        Markers = markers;
        AbIn = abIn;
        AbOut = abOut;
    }

    public MarkerSnapshot[] Markers { get; }

    public long? AbIn { get; }

    public long? AbOut { get; }

    public static PlayerScratchSnapshot Capture(PlayerScratchCues cues) =>
        new(cues.SnapshotMarkers(), cues.AbIn, cues.AbOut);

    public void Restore(PlayerScratchCues cues)
    {
        cues.ReplaceMarkers(Markers);
        cues.ReplaceAb(AbIn, AbOut);
    }

    public bool Equals(PlayerScratchSnapshot other)
    {
        if (AbIn != other.AbIn || AbOut != other.AbOut)
        {
            return false;
        }

        if (Markers.Length != other.Markers.Length)
        {
            return false;
        }

        for (var i = 0; i < Markers.Length; i++)
        {
            if (Markers[i].Frame != other.Markers[i].Frame
                || !string.Equals(Markers[i].Comment, other.Markers[i].Comment, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) =>
        obj is PlayerScratchSnapshot other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(AbIn, AbOut, Markers.Length);
}
