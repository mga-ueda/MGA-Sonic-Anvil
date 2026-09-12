using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal sealed class EditHistory
{
    private readonly Stack<IEditCommand> _undo = new();
    private readonly Stack<IEditCommand> _redo = new();
    private readonly Stack<OriginSnapshot> _undoOrigins = new();
    private readonly Stack<OriginSnapshot> _redoOrigins = new();
    private int _cleanIndex;
    private bool _cleanValid = true;

    /// <summary>
    /// コマンド実行前のフォーマット変換起点。内容編集の Revert は起点を「今のレートのデータ」で
    /// 取り直してしまうため、Undo 後もレート／ビット変換がオリジナルから再変換できるように戻す。
    /// </summary>
    private readonly record struct OriginSnapshot(float[] Samples, int SampleRate, int Channels, int Bits)
    {
        public static OriginSnapshot Capture(AudioDocument document) =>
            new(
                document.FormatOriginSamples,
                document.FormatOriginSampleRate,
                document.FormatOriginChannels,
                document.FormatOriginBitsPerSample);

        public void Restore(AudioDocument document) =>
            document.SetFormatOrigin(Samples, SampleRate, Channels, Bits);
    }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public int UndoCount => _undo.Count;

    public int RedoCount => _redo.Count;

    public string? UndoName => _undo.Count > 0 ? _undo.Peek().Name : null;

    public string? RedoName => _redo.Count > 0 ? _redo.Peek().Name : null;

    /// <summary>初期状態を 0、最後に実行した編集を UndoCount。</summary>
    public int CurrentIndex => _undo.Count;

    public int TotalCount => _undo.Count + _redo.Count;

    public bool IsClean => _cleanValid && _undo.Count == _cleanIndex;

    public void MarkClean()
    {
        _cleanIndex = _undo.Count;
        _cleanValid = true;
    }

    public IReadOnlyList<EditHistoryEntry> Snapshot()
    {
        var items = new List<EditHistoryEntry>(TotalCount + 1)
        {
            new(0, UiStrings.EditHistoryOrigin, UiStrings.EditHistoryOrigin),
        };
        var index = 1;
        foreach (var command in _undo.Reverse())
        {
            items.Add(new(index++, command.Name, command.Summary, command.Replay is not null));
        }

        foreach (var command in _redo)
        {
            items.Add(new(index++, command.Name, command.Summary, command.Replay is not null));
        }

        return items;
    }

    /// <summary>Snapshot と同じ並び（1 始まり）で再実行レシピを返す。無ければ null。</summary>
    public Func<AudioDocument, IEditCommand?>? ReplayAt(int index)
    {
        if (index < 1 || index > TotalCount)
        {
            return null;
        }

        var i = 1;
        foreach (var command in _undo.Reverse())
        {
            if (i++ == index)
            {
                return command.Replay;
            }
        }

        foreach (var command in _redo)
        {
            if (i++ == index)
            {
                return command.Replay;
            }
        }

        return null;
    }

    public bool JumpTo(AudioDocument document, int index)
    {
        index = Math.Clamp(index, 0, TotalCount);
        var changed = false;
        while (_undo.Count > index)
        {
            Undo(document);
            changed = true;
        }

        while (_undo.Count < index)
        {
            Redo(document);
            changed = true;
        }

        return changed;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _undoOrigins.Clear();
        _redoOrigins.Clear();
        _cleanIndex = 0;
        _cleanValid = true;
    }

    public void RestoreClean(int cleanIndex, bool valid)
    {
        _cleanIndex = Math.Clamp(cleanIndex, 0, TotalCount);
        _cleanValid = valid;
    }

    public HistorySessionSnapshot? TryExport()
    {
        if (TotalCount <= 0)
        {
            return null;
        }

        var recipes = new List<HistoryRecipe>(TotalCount);
        foreach (var command in _undo.Reverse())
        {
            if (command.Persist is null)
            {
                return null;
            }

            recipes.Add(command.Persist);
        }

        foreach (var command in _redo)
        {
            if (command.Persist is null)
            {
                return null;
            }

            recipes.Add(command.Persist);
        }

        return new HistorySessionSnapshot
        {
            CurrentIndex = CurrentIndex,
            CleanIndex = _cleanIndex,
            CleanValid = _cleanValid,
            Recipes = [.. recipes],
        };
    }

    public static bool TryImport(AudioDocument document, HistorySessionSnapshot snapshot, out EditHistory history)
    {
        history = new EditHistory();
        if (snapshot.Recipes is not { Length: > 0 } recipes)
        {
            return false;
        }

        foreach (var recipe in recipes)
        {
            var command = HistoryRecipes.TryCreate(document, recipe);
            if (command is null)
            {
                history = new EditHistory();
                return false;
            }

            history.Do(document, command);
        }

        history.JumpTo(document, snapshot.CurrentIndex);
        history.RestoreClean(snapshot.CleanIndex, snapshot.CleanValid);
        document.SetDirty(!history.IsClean);
        return true;
    }

    public void Do(AudioDocument document, IEditCommand command)
    {
        var origin = OriginSnapshot.Capture(document);
        command.Apply(document);
        if (_redo.Count > 0 && _cleanValid && _cleanIndex > _undo.Count)
        {
            _cleanValid = false;
        }

        _undo.Push(command);
        _undoOrigins.Push(origin);
        _redo.Clear();
        _redoOrigins.Clear();
        Finish(document);
    }

    public bool Undo(AudioDocument document)
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        var command = _undo.Pop();
        var origin = _undoOrigins.Pop();
        command.Revert(document);
        origin.Restore(document);
        _redo.Push(command);
        _redoOrigins.Push(origin);
        Finish(document);
        return true;
    }

    public bool Redo(AudioDocument document)
    {
        if (_redo.Count == 0)
        {
            return false;
        }

        var command = _redo.Pop();
        var origin = _redoOrigins.Pop();
        command.Apply(document);
        _undo.Push(command);
        _undoOrigins.Push(origin);
        Finish(document);
        return true;
    }

    private void Finish(AudioDocument document)
    {
        document.ClampCursor();
        document.SetDirty(!IsClean);
    }
}
