namespace MgaSonicAnvil.UI;

/// <summary>
/// プレイヤーの Delete で外した曲を Ctrl+Z で戻すための記録。
/// プレイヤー退出で捨てる。
/// </summary>
internal sealed class LibraryPlaylistRemoveUndo
{
    private readonly Stack<Entry> _stack = new();

    public bool CanUndo => _stack.Count > 0;

    public void Clear() => _stack.Clear();

    public void Push(IReadOnlyList<DocumentSession> sessions, IReadOnlyList<int> indices)
    {
        if (sessions.Count == 0 || sessions.Count != indices.Count)
        {
            return;
        }

        var ordered = new (DocumentSession Session, int Index)[sessions.Count];
        for (var i = 0; i < sessions.Count; i++)
        {
            ordered[i] = (sessions[i], indices[i]);
        }

        Array.Sort(ordered, static (a, b) => a.Index.CompareTo(b.Index));
        var restoredSessions = new DocumentSession[ordered.Length];
        var restoredIndices = new int[ordered.Length];
        for (var i = 0; i < ordered.Length; i++)
        {
            restoredSessions[i] = ordered[i].Session;
            restoredIndices[i] = ordered[i].Index;
        }

        _stack.Push(new Entry(restoredSessions, restoredIndices));
    }

    public bool TryPop(out DocumentSession[] sessions, out int[] indices)
    {
        if (_stack.Count == 0)
        {
            sessions = [];
            indices = [];
            return false;
        }

        var entry = _stack.Pop();
        sessions = entry.Sessions;
        indices = entry.Indices;
        return true;
    }

    /// <summary>昇順の元インデックスへ Insert する（低い側から入れる）。</summary>
    public static void RestoreInto(IList<DocumentSession> list, DocumentSession[] sessions, int[] indices)
    {
        for (var i = 0; i < sessions.Length; i++)
        {
            var index = Math.Clamp(indices[i], 0, list.Count);
            list.Insert(index, sessions[i]);
        }
    }

    private sealed record Entry(DocumentSession[] Sessions, int[] Indices);
}
