using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal interface IEditCommand
{
    string Name { get; }

    string Summary { get; }

    /// <summary>
    /// 同じ操作を別ドキュメントへ再実行するファクトリ（履歴のコピー＆ペースト用）。
    /// パラメータ（範囲・カーブ等）から作り直すので、ノーマライズ等は適用先で再計算される。
    /// null は再適用不可（ドラッグ由来のタイムライン一括移動など）。
    /// 適用できない場合（範囲が空になる等）はファクトリが null を返す。
    /// </summary>
    Func<AudioDocument, IEditCommand?>? Replay { get; set; }

    /// <summary>起動復元用。無い操作はセッションに残さない。</summary>
    HistoryRecipe? Persist { get; set; }

    void Apply(AudioDocument document);

    void Revert(AudioDocument document);
}

/// <summary>
/// 履歴レシピを別ドキュメントへ写像する座標変換。レートが違えば時間基準で
/// スケールし、長さはクランプする。
/// </summary>
internal static class EditReplay
{
    public static long MapFrame(long frame, int sourceRate, AudioDocument target)
    {
        var mapped = sourceRate > 0 && sourceRate != target.SampleRate
            ? (long)Math.Round(frame * (double)target.SampleRate / sourceRate)
            : frame;
        return Math.Clamp(mapped, 0, Math.Max(0, target.FrameCount));
    }

    public static WaveSelection MapRange(WaveSelection range, int sourceRate, AudioDocument target)
    {
        if (range.IsEmpty)
        {
            return WaveSelection.Empty;
        }

        return new WaveSelection(
            MapFrame(range.StartFrame, sourceRate, target),
            MapFrame(range.EndFrame, sourceRate, target));
    }

    public static long ScaleDelta(long delta, int sourceRate, int targetRate) =>
        sourceRate > 0 && targetRate > 0 && sourceRate != targetRate
            ? (long)Math.Round(delta * (double)targetRate / sourceRate)
            : delta;
}

internal sealed class EditHistory
{
    private readonly Stack<IEditCommand> _undo = new();
    private readonly Stack<IEditCommand> _redo = new();
    private int _cleanIndex;
    private bool _cleanValid = true;

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
        command.Apply(document);
        if (_redo.Count > 0 && _cleanValid && _cleanIndex > _undo.Count)
        {
            _cleanValid = false;
        }

        _undo.Push(command);
        _redo.Clear();
        Finish(document);
    }

    public bool Undo(AudioDocument document)
    {
        if (_undo.Count == 0)
        {
            return false;
        }

        var command = _undo.Pop();
        command.Revert(document);
        _redo.Push(command);
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
        command.Apply(document);
        _undo.Push(command);
        Finish(document);
        return true;
    }

    private void Finish(AudioDocument document)
    {
        document.ClampCursor();
        document.SetDirty(!IsClean);
    }
}

internal readonly record struct EditHistoryEntry(int Index, string Name, string Title, bool CanReplay = false);

internal sealed class ReplaceRangeCommand : IEditCommand
{
    private readonly long _startFrame;
    private readonly float[] _before;
    private readonly float[] _after;
    private readonly WaveSelection _selectionBefore;
    private readonly WaveSelection _selectionAfter;
    private readonly long _cursorBefore;
    private readonly long _cursorAfter;

    public ReplaceRangeCommand(
        string name,
        long startFrame,
        float[] before,
        float[] after,
        WaveSelection selectionBefore,
        WaveSelection selectionAfter,
        long cursorBefore,
        long cursorAfter,
        string? summary = null)
    {
        Name = name;
        Summary = string.IsNullOrWhiteSpace(summary) ? UiStrings.EditHistoryName(name) : summary;
        _startFrame = startFrame;
        _before = before;
        _after = after;
        _selectionBefore = selectionBefore;
        _selectionAfter = selectionAfter;
        _cursorBefore = cursorBefore;
        _cursorAfter = cursorAfter;
    }

    public string Name { get; }

    public string Summary { get; }

    public Func<AudioDocument, IEditCommand?>? Replay { get; set; }

    public HistoryRecipe? Persist { get; set; }

    public void Apply(AudioDocument document)
    {
        document.ReplaceRange(_startFrame, _after);
        document.Selection = _selectionAfter;
        document.CursorFrame = _cursorAfter;
    }

    public void Revert(AudioDocument document)
    {
        document.ReplaceRange(_startFrame, _before);
        document.Selection = _selectionBefore;
        document.CursorFrame = _cursorBefore;
    }
}

internal sealed class DeleteRangeCommand : IEditCommand
{
    private readonly long _startFrame;
    private readonly float[] _removed;
    private readonly WaveSelection _selectionBefore;
    private readonly long _cursorBefore;
    private readonly MarkerSnapshot[] _markersBefore;
    private readonly WaveSelection _sampleLoopBefore;
    private readonly WaveRegion[] _regionsBefore;

    public DeleteRangeCommand(
        long startFrame,
        float[] removed,
        WaveSelection selectionBefore,
        long cursorBefore,
        MarkerSnapshot[] markersBefore,
        WaveSelection sampleLoopBefore,
        WaveRegion[] regionsBefore,
        string summary)
    {
        _startFrame = startFrame;
        _removed = removed;
        _selectionBefore = selectionBefore;
        _cursorBefore = cursorBefore;
        _markersBefore = markersBefore;
        _sampleLoopBefore = sampleLoopBefore;
        _regionsBefore = regionsBefore;
        Summary = summary;
    }

    public string Name => "Delete";

    public string Summary { get; }

    public Func<AudioDocument, IEditCommand?>? Replay { get; set; }

    public HistoryRecipe? Persist { get; set; }

    public void Apply(AudioDocument document)
    {
        var frames = _removed.Length / document.Channels;
        document.DeleteRange(_startFrame, frames);
        document.ApplyDeleteToMarkers(_startFrame, frames);
        document.ApplyDeleteToSampleLoop(_startFrame, frames);
        document.ApplyDeleteToRegion(_startFrame, frames);
        document.Selection = WaveSelection.Empty;
        document.CursorFrame = _startFrame;
    }

    public void Revert(AudioDocument document)
    {
        document.InsertRange(_startFrame, _removed);
        document.ReplaceMarkers(_markersBefore);
        document.SetSampleLoop(_sampleLoopBefore);
        document.SetRegions(_regionsBefore);
        document.Selection = _selectionBefore;
        document.CursorFrame = _cursorBefore;
    }
}

internal sealed class PasteCommand : IEditCommand
{
    private readonly long _insertFrame;
    private readonly float[] _inserted;
    private readonly float[]? _replaced;
    private readonly WaveSelection _selectionBefore;
    private readonly long _cursorBefore;
    private readonly MarkerSnapshot[] _markersBefore;
    private readonly MarkerSnapshot[] _pastedMarkers;
    private readonly WaveRegion[] _pastedRegions;
    private readonly WaveSelection _sampleLoopBefore;
    private readonly WaveRegion[] _regionsBefore;

    public PasteCommand(
        long insertFrame,
        float[] inserted,
        float[]? replaced,
        WaveSelection selectionBefore,
        long cursorBefore,
        MarkerSnapshot[] markersBefore,
        MarkerSnapshot[] pastedMarkers,
        WaveRegion[] pastedRegions,
        WaveSelection sampleLoopBefore,
        WaveRegion[] regionsBefore,
        string summary)
    {
        _insertFrame = insertFrame;
        _inserted = inserted;
        _replaced = replaced;
        _selectionBefore = selectionBefore;
        _cursorBefore = cursorBefore;
        _markersBefore = markersBefore;
        _pastedMarkers = pastedMarkers;
        _pastedRegions = pastedRegions;
        _sampleLoopBefore = sampleLoopBefore;
        _regionsBefore = regionsBefore;
        Summary = summary;
    }

    public string Name => "Paste";

    public string Summary { get; }

    public Func<AudioDocument, IEditCommand?>? Replay { get; set; }

    public HistoryRecipe? Persist { get; set; }

    public void Apply(AudioDocument document)
    {
        if (_replaced is { Length: > 0 })
        {
            var removedFrames = _replaced.Length / document.Channels;
            document.DeleteRange(_insertFrame, removedFrames);
            document.ApplyDeleteToMarkers(_insertFrame, removedFrames);
            document.ApplyDeleteToSampleLoop(_insertFrame, removedFrames);
            document.ApplyDeleteToRegion(_insertFrame, removedFrames);
        }

        document.InsertRange(_insertFrame, _inserted);
        var insertedFrames = _inserted.Length / document.Channels;
        document.ApplyInsertToMarkers(_insertFrame, insertedFrames);
        document.ApplyInsertToSampleLoop(_insertFrame, insertedFrames);
        document.ApplyInsertToRegion(_insertFrame, insertedFrames);
        document.ApplyPastedMarkers(_insertFrame, _pastedMarkers);
        document.ApplyPastedRegions(_insertFrame, _pastedRegions);
        document.Selection = new WaveSelection(_insertFrame, _insertFrame + insertedFrames);
        document.CursorFrame = _insertFrame + insertedFrames;
    }

    public void Revert(AudioDocument document)
    {
        var insertedFrames = _inserted.Length / document.Channels;
        document.DeleteRange(_insertFrame, insertedFrames);
        if (_replaced is { Length: > 0 })
        {
            document.InsertRange(_insertFrame, _replaced);
        }

        document.ReplaceMarkers(_markersBefore);
        document.SetSampleLoop(_sampleLoopBefore);
        document.SetRegions(_regionsBefore);
        document.Selection = _selectionBefore;
        document.CursorFrame = _cursorBefore;
    }
}

internal sealed class AddMarkerCommand : IEditCommand
{
    private readonly long _frame;
    private readonly MarkerSnapshot[] _markersBefore;

    public AddMarkerCommand(long frame, MarkerSnapshot[] markersBefore, string summary)
    {
        _frame = frame;
        _markersBefore = markersBefore;
        Summary = summary;
    }

    public string Name => "Add Marker";

    public string Summary { get; }

    public Func<AudioDocument, IEditCommand?>? Replay { get; set; }

    public HistoryRecipe? Persist { get; set; }

    public void Apply(AudioDocument document) => document.TryAddMarker(_frame);

    public void Revert(AudioDocument document) => document.ReplaceMarkers(_markersBefore);
}

internal sealed class SetRegionNameCommand : IEditCommand
{
    private readonly WaveSelection _range;
    private readonly string _before;
    private readonly string _after;

    public SetRegionNameCommand(WaveSelection range, string before, string after, string summary)
    {
        _range = range;
        _before = before;
        _after = after;
        Summary = summary;
    }

    public string Name => "Region Name";

    public string Summary { get; }

    public Func<AudioDocument, IEditCommand?>? Replay { get; set; }

    public HistoryRecipe? Persist { get; set; }

    public void Apply(AudioDocument document) => document.TrySetRegionName(_range, _after);

    public void Revert(AudioDocument document) => document.TrySetRegionName(_range, _before);
}

internal sealed class SetMarkerCommentCommand : IEditCommand
{
    private readonly long _frame;
    private readonly string _before;
    private readonly string _after;

    public SetMarkerCommentCommand(long frame, string before, string after, string summary)
    {
        _frame = frame;
        _before = before;
        _after = after;
        Summary = summary;
    }

    public string Name => "Marker Comment";

    public string Summary { get; }

    public Func<AudioDocument, IEditCommand?>? Replay { get; set; }

    public HistoryRecipe? Persist { get; set; }

    public void Apply(AudioDocument document) => document.TrySetMarkerComment(_frame, _after);

    public void Revert(AudioDocument document) => document.TrySetMarkerComment(_frame, _before);
}

internal sealed class ReplaceMarkersCommand : IEditCommand
{
    private readonly MarkerSnapshot[] _before;
    private readonly MarkerSnapshot[] _after;

    public ReplaceMarkersCommand(string name, MarkerSnapshot[] before, MarkerSnapshot[] after, string summary)
    {
        Name = name;
        Summary = summary;
        _before = before;
        _after = after;
    }

    public string Name { get; }

    public string Summary { get; }

    public Func<AudioDocument, IEditCommand?>? Replay { get; set; }

    public HistoryRecipe? Persist { get; set; }

    public void Apply(AudioDocument document) => document.ReplaceMarkers(_after);

    public void Revert(AudioDocument document) => document.ReplaceMarkers(_before);
}

internal sealed class SetSampleLoopCommand : IEditCommand
{
    private readonly WaveSelection _before;
    private readonly WaveSelection _after;

    public SetSampleLoopCommand(WaveSelection before, WaveSelection after, string summary)
    {
        _before = before;
        _after = after;
        Summary = summary;
    }

    public string Name => "Set Sample Loop";

    public string Summary { get; }

    public Func<AudioDocument, IEditCommand?>? Replay { get; set; }

    public HistoryRecipe? Persist { get; set; }

    public void Apply(AudioDocument document) => document.SetSampleLoop(_after);

    public void Revert(AudioDocument document) => document.SetSampleLoop(_before);
}

internal sealed class SetRegionCommand : IEditCommand
{
    private readonly WaveRegion[] _before;
    private readonly WaveRegion[] _after;

    public SetRegionCommand(WaveRegion[] before, WaveRegion[] after, string summary)
    {
        _before = before;
        _after = after;
        Summary = summary;
    }

    public string Name => "Set Region";

    public string Summary { get; }

    public Func<AudioDocument, IEditCommand?>? Replay { get; set; }

    public HistoryRecipe? Persist { get; set; }

    public void Apply(AudioDocument document) => document.SetRegions(_after);

    public void Revert(AudioDocument document) => document.SetRegions(_before);
}

internal sealed class MoveTimelineItemsCommand : IEditCommand
{
    private readonly MarkerSnapshot[] _markersBefore;
    private readonly MarkerSnapshot[] _markersAfter;
    private readonly WaveRegion[] _regionsBefore;
    private readonly WaveRegion[] _regionsAfter;
    private readonly WaveSelection _loopBefore;
    private readonly WaveSelection _loopAfter;

    public MoveTimelineItemsCommand(
        MarkerSnapshot[] markersBefore,
        MarkerSnapshot[] markersAfter,
        WaveRegion[] regionsBefore,
        WaveRegion[] regionsAfter,
        WaveSelection loopBefore,
        WaveSelection loopAfter,
        string summary)
    {
        _markersBefore = markersBefore;
        _markersAfter = markersAfter;
        _regionsBefore = regionsBefore;
        _regionsAfter = regionsAfter;
        _loopBefore = loopBefore;
        _loopAfter = loopAfter;
        Summary = summary;
    }

    public string Name => "Move Timeline";

    public string Summary { get; }

    public Func<AudioDocument, IEditCommand?>? Replay { get; set; }

    public HistoryRecipe? Persist { get; set; }

    public void Apply(AudioDocument document)
    {
        document.ReplaceMarkers(_markersAfter);
        document.SetRegions(_regionsAfter);
        document.SetSampleLoop(_loopAfter);
    }

    public void Revert(AudioDocument document)
    {
        document.ReplaceMarkers(_markersBefore);
        document.SetRegions(_regionsBefore);
        document.SetSampleLoop(_loopBefore);
    }
}

internal sealed class ConvertFormatCommand : IEditCommand
{
    private readonly FormatSnapshot _before;
    private readonly FormatSnapshot _after;

    public ConvertFormatCommand(string name, string summary, FormatSnapshot before, FormatSnapshot after)
    {
        Name = name;
        Summary = summary;
        _before = before;
        _after = after;
    }

    public string Name { get; }

    public string Summary { get; }

    public Func<AudioDocument, IEditCommand?>? Replay { get; set; }

    public HistoryRecipe? Persist { get; set; }

    public void Apply(AudioDocument document) => _after.Restore(document);

    public void Revert(AudioDocument document) => _before.Restore(document);
}

internal readonly record struct FormatSnapshot(
    float[] Samples,
    int SampleRate,
    int Channels,
    int BitsPerSample,
    float[] OriginSamples,
    int OriginSampleRate,
    int OriginChannels,
    int OriginBitsPerSample,
    WaveSelection Selection,
    WaveSelection SampleLoop,
    WaveRegion[] Regions,
    long Cursor,
    MarkerSnapshot[] Markers)
{
    public static FormatSnapshot Capture(AudioDocument document) =>
        new(
            document.Interleaved,
            document.SampleRate,
            document.Channels,
            document.BitsPerSample,
            document.FormatOriginSamples,
            document.FormatOriginSampleRate,
            document.FormatOriginChannels,
            document.FormatOriginBitsPerSample,
            document.Selection,
            document.SampleLoop,
            document.SnapshotRegions(),
            document.CursorFrame,
            document.SnapshotMarkers());

    public void Restore(AudioDocument document)
    {
        document.ReplaceAudio(Samples, SampleRate, Channels, BitsPerSample);
        document.SetFormatOrigin(OriginSamples, OriginSampleRate, OriginChannels, OriginBitsPerSample);
        document.ReplaceMarkers(Markers, markDirty: false);
        document.Selection = Selection;
        document.SetSampleLoop(SampleLoop, markDirty: false);
        document.SetRegions(Regions, markDirty: false);
        document.CursorFrame = Cursor;
    }
}

internal static class ProcessEdits
{
    public static IEditCommand FadeIn(AudioDocument document, WaveSelection range) =>
        FadeIn(document, range, FadeCurves.Default);

    public static IEditCommand FadeOut(AudioDocument document, WaveSelection range) =>
        FadeOut(document, range, FadeCurves.Default);

    public static IEditCommand FadeIn(AudioDocument document, WaveSelection range, FadeShape shape)
    {
        var command = Fade(document, range, fadeIn: true, "Fade In", shape);
        AttachRangeReplay(command, document.SampleRate, range, (target, mapped) => FadeIn(target, mapped, shape));
        command.Persist = HistoryRecipes.Range(HistoryRecipes.FadeIn, document.SampleRate, range, (int)shape);
        return command;
    }

    public static IEditCommand FadeOut(AudioDocument document, WaveSelection range, FadeShape shape)
    {
        var command = Fade(document, range, fadeIn: false, "Fade Out", shape);
        AttachRangeReplay(command, document.SampleRate, range, (target, mapped) => FadeOut(target, mapped, shape));
        command.Persist = HistoryRecipes.Range(HistoryRecipes.FadeOut, document.SampleRate, range, (int)shape);
        return command;
    }

    /// <summary>範囲を写像して同じ操作を作り直すレシピを付ける。写像後に空なら適用しない。</summary>
    private static void AttachRangeReplay(
        IEditCommand command,
        int sourceRate,
        WaveSelection range,
        Func<AudioDocument, WaveSelection, IEditCommand?> factory)
    {
        command.Replay = target =>
        {
            var mapped = EditReplay.MapRange(range, sourceRate, target);
            return mapped.IsEmpty ? null : factory(target, mapped);
        };
    }

    public static IEditCommand? FadeAroundPlayhead(
        AudioDocument document,
        WaveSelection visible,
        long playhead)
    {
        SplitVisibleAroundPlayhead(visible, playhead, document.FrameCount, out var fadeOut, out var fadeIn);
        if (fadeOut.IsEmpty && fadeIn.IsEmpty)
        {
            return null;
        }

        var start = fadeOut.IsEmpty ? fadeIn.StartFrame : fadeOut.StartFrame;
        var end = fadeIn.IsEmpty ? fadeOut.EndFrame : fadeIn.EndFrame;
        var union = new WaveSelection(start, end);
        if (union.IsEmpty)
        {
            return null;
        }

        var before = document.CopyRange(union.StartFrame, union.Length);
        var after = (float[])before.Clone();
        ApplyFadeGains(after, document.Channels, union.StartFrame, fadeOut, fadeIn: false);
        ApplyFadeGains(after, document.Channels, union.StartFrame, fadeIn, fadeIn: true);
        var cursor = Math.Clamp(playhead, 0, Math.Max(0, document.FrameCount));
        var command = new ReplaceRangeCommand(
            "Fade Around Playhead",
            union.StartFrame,
            before,
            after,
            document.Selection,
            document.Selection,
            document.CursorFrame,
            cursor,
            UiStrings.EditHistoryRange(
                UiStrings.EditHistoryName("Fade Around Playhead"),
                document.SampleRate,
                union.StartFrame,
                union.EndFrame));
        var sourceRate = document.SampleRate;
        var sourceVisible = visible;
        var sourcePlayhead = playhead;
        command.Replay = target =>
        {
            var mappedVisible = EditReplay.MapRange(sourceVisible, sourceRate, target);
            return mappedVisible.IsEmpty
                ? null
                : FadeAroundPlayhead(target, mappedVisible, EditReplay.MapFrame(sourcePlayhead, sourceRate, target));
        };
        command.Persist = new HistoryRecipe
        {
            Kind = HistoryRecipes.FadeAround,
            SourceRate = sourceRate,
            Start = sourceVisible.StartFrame,
            End = sourceVisible.EndFrame,
            Playhead = sourcePlayhead,
        };
        return command;
    }

    public static void SplitVisibleAroundPlayhead(
        WaveSelection visible,
        long playhead,
        long frameCount,
        out WaveSelection fadeOut,
        out WaveSelection fadeIn)
    {
        visible = visible.Clamp(frameCount);
        playhead = Math.Clamp(playhead, 0, Math.Max(0, frameCount));
        fadeOut = new WaveSelection(visible.StartFrame, Math.Min(playhead, visible.EndFrame)).Clamp(frameCount);
        fadeIn = new WaveSelection(Math.Max(playhead, visible.StartFrame), visible.EndFrame).Clamp(frameCount);
    }

    public static IEditCommand Normalize(AudioDocument document, WaveSelection range)
    {
        var before = document.CopyRange(range.StartFrame, range.Length);
        var after = (float[])before.Clone();
        var peak = 0f;
        foreach (var sample in after)
        {
            var abs = Math.Abs(sample);
            if (abs > peak)
            {
                peak = abs;
            }
        }

        if (peak > 1e-8f)
        {
            // Sound Forge 既定に合わせ -0.1 dB ヘッドルーム。
            var target = (float)Math.Pow(10d, -0.1d / 20d);
            var gain = target / peak;
            for (var i = 0; i < after.Length; i++)
            {
                after[i] *= gain;
            }
        }

        var command = new ReplaceRangeCommand(
            "Normalize",
            range.StartFrame,
            before,
            after,
            document.Selection,
            range,
            document.CursorFrame,
            range.StartFrame,
            UiStrings.EditHistoryRange(
                UiStrings.EditHistoryName("Normalize"),
                document.SampleRate,
                range.StartFrame,
                range.EndFrame));
        // 再適用時はゲインを適用先のピークから再計算する。
        AttachRangeReplay(command, document.SampleRate, range, Normalize);
        command.Persist = HistoryRecipes.Range(HistoryRecipes.Normalize, document.SampleRate, range);
        return command;
    }

    public static IEditCommand? Gain(AudioDocument document, WaveSelection range, double gainDb)
    {
        range = range.Clamp(document.FrameCount);
        gainDb = WaveformGainAnalyzer.SnapGainDb(gainDb);
        if (range.IsEmpty || WaveformGainAnalyzer.IsNoOp(gainDb))
        {
            return null;
        }

        var linear = (float)WaveformGainAnalyzer.LinearFromDb(gainDb);
        var before = document.CopyRange(range.StartFrame, range.Length);
        var after = (float[])before.Clone();
        for (var i = 0; i < after.Length; i++)
        {
            after[i] *= linear;
        }

        var extra = UiStrings.FormatSignedDb(gainDb);
        var command = new ReplaceRangeCommand(
            "Volume",
            range.StartFrame,
            before,
            after,
            document.Selection,
            document.Selection,
            document.CursorFrame,
            document.CursorFrame,
            UiStrings.EditHistoryRange(
                UiStrings.EditHistoryName("Volume"),
                document.SampleRate,
                range.StartFrame,
                range.EndFrame,
                extra));
        var snapped = gainDb;
        AttachRangeReplay(command, document.SampleRate, range, (target, mapped) => Gain(target, mapped, snapped));
        command.Persist = HistoryRecipes.FromGain(document.SampleRate, range, snapped);
        return command;
    }

    public static IEditCommand Delete(AudioDocument document, WaveSelection range)
    {
        var removed = document.CopyRange(range.StartFrame, range.Length);
        var command = new DeleteRangeCommand(
            range.StartFrame,
            removed,
            document.Selection,
            document.CursorFrame,
            document.SnapshotMarkers(),
            document.SampleLoop,
            document.SnapshotRegions(),
            UiStrings.EditHistoryRange(
                UiStrings.EditHistoryName("Delete"),
                document.SampleRate,
                range.StartFrame,
                range.EndFrame));
        AttachRangeReplay(command, document.SampleRate, range, Delete);
        command.Persist = HistoryRecipes.Range(HistoryRecipes.Delete, document.SampleRate, range);
        return command;
    }

    public static AudioClip? Copy(AudioDocument document, WaveSelection range)
    {
        range = range.Clamp(document.FrameCount);
        if (range.IsEmpty)
        {
            return null;
        }

        return new AudioClip(
            document.CopyRange(range.StartFrame, range.Length),
            document.Channels,
            document.SampleRate,
            document.SnapshotMarkersInRange(range),
            document.SnapshotExactRegions(range));
    }

    public static IEditCommand? Paste(AudioDocument document, AudioClip clip, long insertFrame)
    {
        if (clip.IsEmpty)
        {
            return null;
        }

        var samples = clip.AdaptTo(document.Channels);
        if (samples.Length < document.Channels)
        {
            return null;
        }

        var replace = document.Selection.Clamp(document.FrameCount);
        float[]? replaced = null;
        if (!replace.IsEmpty)
        {
            insertFrame = replace.StartFrame;
            replaced = document.CopyRange(replace.StartFrame, replace.Length);
        }
        else
        {
            insertFrame = Math.Clamp(insertFrame, 0, document.FrameCount);
        }

        var insertedFrames = samples.Length / Math.Max(1, document.Channels);
        var pasteVerb = replaced is { Length: > 0 }
            ? UiStrings.EditHistoryName("Paste") + "（置換）"
            : UiStrings.EditHistoryName("Paste");
        var command = new PasteCommand(
            insertFrame,
            samples,
            replaced,
            document.Selection,
            document.CursorFrame,
            document.SnapshotMarkers(),
            clip.Markers.ToArray(),
            clip.Regions.ToArray(),
            document.SampleLoop,
            document.SnapshotRegions(),
            UiStrings.EditHistoryRange(
                pasteVerb,
                document.SampleRate,
                insertFrame,
                insertFrame + insertedFrames));
        var sourceRate = document.SampleRate;
        var resolvedFrame = insertFrame;
        command.Replay = target => Paste(target, clip, EditReplay.MapFrame(resolvedFrame, sourceRate, target));
        command.Persist = HistoryRecipes.FromPaste(sourceRate, resolvedFrame, clip);
        return command;
    }

    public static IEditCommand? SetSampleLoop(AudioDocument document, WaveSelection range)
    {
        if (!document.AllowsRegionsAndLoops && !range.IsEmpty)
        {
            return null;
        }

        var before = document.SampleLoop;
        var after = range.IsEmpty ? WaveSelection.Empty : range.Clamp(document.FrameCount);
        if (before == after)
        {
            if (after.IsEmpty)
            {
                return null;
            }

            after = WaveSelection.Empty;
        }

        var command = new SetSampleLoopCommand(before, after, SampleLoopSummary(document.SampleRate, after));
        var sourceRate = document.SampleRate;
        var sourceAfter = after;
        // 再適用は「after の状態にする」。トグルではないので同じ範囲でも解除にならない。
        command.Replay = target =>
        {
            var mapped = sourceAfter.IsEmpty
                ? WaveSelection.Empty
                : EditReplay.MapRange(sourceAfter, sourceRate, target);
            if (target.SampleLoop == mapped)
            {
                return null;
            }

            return new SetSampleLoopCommand(
                target.SampleLoop,
                mapped,
                SampleLoopSummary(target.SampleRate, mapped));
        };
        command.Persist = HistoryRecipes.Range(HistoryRecipes.SetSampleLoop, sourceRate, sourceAfter);
        return command;
    }

    private static string SampleLoopSummary(int sampleRate, WaveSelection after) =>
        after.IsEmpty
            ? UiStrings.EditHistoryName("Set Sample Loop") + "  解除"
            : UiStrings.EditHistoryRange(
                UiStrings.EditHistoryName("Set Sample Loop"),
                sampleRate,
                after.StartFrame,
                after.EndFrame);

    public static IEditCommand? SetRegion(AudioDocument document, WaveSelection range)
    {
        if (!document.AllowsRegionsAndLoops && !range.IsEmpty)
        {
            return null;
        }

        var before = document.SnapshotRegions();
        var afterList = new List<WaveRegion>(before);
        if (range.IsEmpty)
        {
            if (afterList.Count == 0)
            {
                return null;
            }

            afterList.Clear();
        }
        else
        {
            var next = range.Clamp(document.FrameCount);
            if (next.IsEmpty)
            {
                return null;
            }

            var index = afterList.FindIndex(item => item.Range == next);
            if (index >= 0)
            {
                afterList.RemoveAt(index);
            }
            else
            {
                afterList.Add(new WaveRegion(next, string.Empty));
            }
        }

        var after = afterList.ToArray();
        var summary = after.Length < before.Length
            ? UiStrings.EditHistoryName("Set Region") + "  解除"
            : UiStrings.EditHistoryRange(
                UiStrings.EditHistoryName("Set Region"),
                document.SampleRate,
                range.StartFrame,
                range.EndFrame);
        var command = new SetRegionCommand(before, after, summary);
        var sourceRate = document.SampleRate;
        var sourceRange = range;
        var wasRemoval = after.Length < before.Length;
        command.Replay = target =>
        {
            // 全解除は適用先も全解除。個別は追加／解除の向きを保つ（トグルの反転を防ぐ）。
            if (sourceRange.IsEmpty)
            {
                var targetBefore = target.SnapshotRegions();
                return targetBefore.Length == 0
                    ? null
                    : new SetRegionCommand(targetBefore, [], UiStrings.EditHistoryName("Set Region") + "  解除");
            }

            var mapped = EditReplay.MapRange(sourceRange, sourceRate, target);
            if (mapped.IsEmpty)
            {
                return null;
            }

            var exists = target.RegionNumber(mapped) > 0;
            if (wasRemoval)
            {
                return exists ? RemoveRegions(target, [mapped]) : null;
            }

            return exists ? null : SetRegion(target, mapped);
        };
        command.Persist = new HistoryRecipe
        {
            Kind = HistoryRecipes.SetRegion,
            SourceRate = sourceRate,
            Start = sourceRange.StartFrame,
            End = sourceRange.EndFrame,
            Flag = wasRemoval,
        };
        return command;
    }

    public static IEditCommand? RemoveRegions(AudioDocument document, IReadOnlyList<WaveSelection> ranges)
    {
        if (ranges is null || ranges.Count == 0)
        {
            return null;
        }

        var before = document.SnapshotRegions();
        var remove = new HashSet<WaveSelection>();
        foreach (var range in ranges)
        {
            if (!range.IsEmpty)
            {
                remove.Add(range);
            }
        }

        var after = before.Where(region => !remove.Contains(region.Range)).ToArray();
        if (after.Length == before.Length)
        {
            return null;
        }

        var command = new SetRegionCommand(
            before,
            after,
            UiStrings.EditHistoryName("Set Region") + "  解除");
        var sourceRate = document.SampleRate;
        var removedRanges = remove.ToArray();
        command.Replay = target => RemoveRegions(
            target,
            removedRanges
                .Select(item => EditReplay.MapRange(item, sourceRate, target))
                .Where(item => !item.IsEmpty)
                .ToArray());
        command.Persist = HistoryRecipes.FromRanges(HistoryRecipes.RemoveRegions, sourceRate, removedRanges);
        return command;
    }

    public static IEditCommand AddMarker(AudioDocument document, long frame)
    {
        var command = new AddMarkerCommand(
            frame,
            document.SnapshotMarkers(),
            UiStrings.EditHistoryPoint(UiStrings.EditHistoryName("Add Marker"), document.SampleRate, frame));
        var sourceRate = document.SampleRate;
        command.Replay = target =>
        {
            var mapped = EditReplay.MapFrame(frame, sourceRate, target);
            return target.HasMarkerAt(mapped) ? null : AddMarker(target, mapped);
        };
        command.Persist = new HistoryRecipe
        {
            Kind = HistoryRecipes.AddMarker,
            SourceRate = sourceRate,
            Frame = frame,
        };
        return command;
    }

    public static IEditCommand? SetMarkerComment(AudioDocument document, long frame, string comment)
    {
        var before = document.MarkerCommentAt(frame);
        var after = MarkerRoles.Normalize(comment);
        if (before == after || !document.HasMarkerAt(frame))
        {
            return null;
        }

        var command = new SetMarkerCommentCommand(
            frame,
            before,
            after,
            UiStrings.EditHistoryPoint(
                UiStrings.EditHistoryName("Marker Comment"),
                document.SampleRate,
                frame,
                UiStrings.EditHistoryQuote(after)));
        var sourceRate = document.SampleRate;
        command.Replay = target =>
            SetMarkerComment(target, EditReplay.MapFrame(frame, sourceRate, target), after);
        command.Persist = new HistoryRecipe
        {
            Kind = HistoryRecipes.MarkerComment,
            SourceRate = sourceRate,
            Frame = frame,
            Text = after,
        };
        return command;
    }

    public static IEditCommand? SetRegionName(AudioDocument document, WaveSelection range, string name)
    {
        var before = document.RegionName(range);
        var after = MarkerRoles.Normalize(name);
        if (before == after || document.RegionNumber(range) <= 0)
        {
            return null;
        }

        var command = new SetRegionNameCommand(
            range,
            before,
            after,
            UiStrings.EditHistoryRange(
                UiStrings.EditHistoryName("Region Name"),
                document.SampleRate,
                range.StartFrame,
                range.EndFrame,
                UiStrings.EditHistoryQuote(after)));
        var sourceRate = document.SampleRate;
        command.Replay = target =>
            SetRegionName(target, EditReplay.MapRange(range, sourceRate, target), after);
        command.Persist = new HistoryRecipe
        {
            Kind = HistoryRecipes.RegionName,
            SourceRate = sourceRate,
            Start = range.StartFrame,
            End = range.EndFrame,
            Text = after,
        };
        return command;
    }

    public static IEditCommand? RemoveMarkers(AudioDocument document, IReadOnlyList<long> frames)
    {
        var before = document.SnapshotMarkers();
        var remove = new HashSet<long>(frames);
        var after = before.Where(marker => !remove.Contains(marker.Frame)).ToArray();
        if (after.Length == before.Length)
        {
            return null;
        }

        var command = new ReplaceMarkersCommand(
            "Delete Markers",
            before,
            after,
            UiStrings.EditHistoryMarkers("Delete Markers", before, after, document.SampleRate));
        var sourceRate = document.SampleRate;
        var removedFrames = remove.ToArray();
        command.Replay = target => RemoveMarkers(
            target,
            removedFrames.Select(item => EditReplay.MapFrame(item, sourceRate, target)).ToArray());
        command.Persist = HistoryRecipes.FromMarkers(HistoryRecipes.RemoveMarkers, sourceRate, removedFrames);
        return command;
    }

    public static IEditCommand? MoveMarkers(
        AudioDocument document,
        IReadOnlyList<long> frames,
        long delta,
        out long appliedDelta)
    {
        appliedDelta = 0;
        var before = document.SnapshotMarkers();
        var moving = new List<long>();
        foreach (var frame in frames)
        {
            if (document.HasMarkerAt(frame) && !moving.Contains(frame))
            {
                moving.Add(frame);
            }
        }

        if (moving.Count == 0 || delta == 0)
        {
            return null;
        }

        moving.Sort();
        var occupied = new HashSet<long>();
        foreach (var marker in before)
        {
            if (moving.BinarySearch(marker.Frame) < 0)
            {
                occupied.Add(marker.Frame);
            }
        }

        appliedDelta = MarkerMoves.ResolveGroupDelta(moving, delta, occupied, Math.Max(0, document.FrameCount));
        if (appliedDelta == 0)
        {
            return null;
        }

        var moved = moving.ToHashSet();
        var after = new MarkerSnapshot[before.Length];
        for (var i = 0; i < before.Length; i++)
        {
            var frame = moved.Contains(before[i].Frame)
                ? before[i].Frame + appliedDelta
                : before[i].Frame;
            after[i] = new MarkerSnapshot(frame, before[i].Comment);
        }

        Array.Sort(after, (left, right) => left.Frame.CompareTo(right.Frame));
        var name = moving.Count == 1 ? "Move Marker" : "Move Markers";
        var command = new ReplaceMarkersCommand(
            name,
            before,
            after,
            UiStrings.EditHistoryMarkers(name, before, after, document.SampleRate));
        var sourceRate = document.SampleRate;
        var movingFrames = moving.ToArray();
        var deltaApplied = appliedDelta;
        command.Replay = target => MoveMarkers(
            target,
            movingFrames.Select(item => EditReplay.MapFrame(item, sourceRate, target)).ToArray(),
            EditReplay.ScaleDelta(deltaApplied, sourceRate, target.SampleRate),
            out _);
        command.Persist = HistoryRecipes.FromMarkers(
            HistoryRecipes.MoveMarkers,
            sourceRate,
            movingFrames,
            deltaApplied);
        return command;
    }

    public static IEditCommand? MoveTimelineItems(
        AudioDocument document,
        MarkerSnapshot[] markersBefore,
        WaveRegion[] regionsBefore,
        WaveSelection loopBefore)
    {
        var markersAfter = document.SnapshotMarkers();
        var regionsAfter = document.SnapshotRegions();
        var loopAfter = document.SampleLoop;
        if (markersBefore.AsSpan().SequenceEqual(markersAfter)
            && RegionsEqual(regionsBefore, regionsAfter)
            && loopBefore == loopAfter)
        {
            return null;
        }

        var command = new MoveTimelineItemsCommand(
            markersBefore,
            markersAfter,
            regionsBefore,
            regionsAfter,
            loopBefore,
            loopAfter,
            UiStrings.EditHistoryTimelineMove(
                markersBefore,
                markersAfter,
                regionsBefore,
                regionsAfter,
                loopBefore,
                loopAfter,
                document.SampleRate));
        command.Persist = HistoryRecipes.FromTimeline(
            document.SampleRate,
            markersAfter,
            regionsAfter,
            loopAfter);
        return command;
    }

    private static bool RegionsEqual(WaveRegion[] left, WaveRegion[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    public static IEditCommand? ReplaceMarkers(
        MarkerSnapshot[] before,
        MarkerSnapshot[] after,
        string name,
        int sampleRate)
    {
        if (before.AsSpan().SequenceEqual(after))
        {
            return null;
        }

        return new ReplaceMarkersCommand(
            name,
            before,
            after,
            UiStrings.EditHistoryMarkers(name, before, after, sampleRate));
    }

    public static IEditCommand? ConvertSampleRate(
        AudioDocument document,
        int destRate,
        IProgress<double>? progress = null)
    {
        if (!FormatConvert.IsValidSampleRate(destRate) || destRate == document.SampleRate)
        {
            return null;
        }

        var before = FormatSnapshot.Capture(document);
        var samples = document.MaterializeFormat(destRate, document.BitsPerSample, document.Channels, progress);
        var destFrames = samples.Length / document.Channels;
        var after = new FormatSnapshot(
            samples,
            destRate,
            document.Channels,
            document.BitsPerSample,
            before.OriginSamples,
            before.OriginSampleRate,
            before.OriginChannels,
            before.OriginBitsPerSample,
            FormatConvert.ScaleSelection(document.Selection, document.SampleRate, destRate, destFrames),
            FormatConvert.ScaleSelection(document.SampleLoop, document.SampleRate, destRate, destFrames),
            ScaleRegions(document, destRate, destFrames),
            FormatConvert.ScaleFrame(document.CursorFrame, document.SampleRate, destRate, destFrames),
            FormatConvert.ScaleMarkers(document.SnapshotMarkers(), document.SampleRate, destRate, destFrames));
        var command = new ConvertFormatCommand(
            "Convert Sample Rate",
            $"{UiStrings.EditHistoryName("Convert Sample Rate")}  {document.SampleRate}→{destRate} Hz",
            before,
            after);
        command.Replay = target => ConvertSampleRate(target, destRate);
        command.Persist = new HistoryRecipe
        {
            Kind = HistoryRecipes.ConvertRate,
            SourceRate = document.SampleRate,
            Value = destRate,
        };
        return command;
    }

    private static WaveRegion[] ScaleRegions(AudioDocument document, int destRate, long destFrames)
    {
        var source = document.SnapshotRegions();
        if (source.Length == 0)
        {
            return source;
        }

        var scaled = new WaveRegion[source.Length];
        for (var i = 0; i < source.Length; i++)
        {
            scaled[i] = new WaveRegion(
                FormatConvert.ScaleSelection(source[i].Range, document.SampleRate, destRate, destFrames),
                source[i].Name);
        }

        return scaled;
    }

    public static IEditCommand? ConvertBitDepth(AudioDocument document, int bits)
    {
        if (!FormatConvert.IsValidBitDepth(bits) || bits == document.BitsPerSample)
        {
            return null;
        }

        var before = FormatSnapshot.Capture(document);
        var samples = document.MaterializeFormat(document.SampleRate, bits, document.Channels);
        var after = before with { Samples = samples, BitsPerSample = bits };
        var command = new ConvertFormatCommand(
            "Convert Bit Depth",
            $"{UiStrings.EditHistoryName("Convert Bit Depth")}  {document.BitsPerSample}→{bits} bit",
            before,
            after);
        command.Replay = target => ConvertBitDepth(target, bits);
        command.Persist = new HistoryRecipe
        {
            Kind = HistoryRecipes.ConvertBits,
            SourceRate = document.SampleRate,
            Value = bits,
        };
        return command;
    }

    public static IEditCommand? ConvertChannels(AudioDocument document, int destChannels)
    {
        if (destChannels < 1 || destChannels == document.Channels)
        {
            return null;
        }

        var before = FormatSnapshot.Capture(document);
        var samples = document.MaterializeFormat(document.SampleRate, document.BitsPerSample, destChannels);
        var after = before with { Samples = samples, Channels = destChannels };
        var command = new ConvertFormatCommand(
            "Convert Channels",
            $"{UiStrings.EditHistoryName("Convert Channels")}  {document.Channels}→{destChannels} ch",
            before,
            after);
        command.Replay = target => ConvertChannels(target, destChannels);
        command.Persist = new HistoryRecipe
        {
            Kind = HistoryRecipes.ConvertChannels,
            SourceRate = document.SampleRate,
            Value = destChannels,
        };
        return command;
    }

    private static IEditCommand Fade(
        AudioDocument document,
        WaveSelection range,
        bool fadeIn,
        string name,
        FadeShape shape)
    {
        var selectionAfter = range;
        range = FadeCurves.InclusiveSampleRange(range, document.FrameCount);
        var before = document.CopyRange(range.StartFrame, range.Length);
        var after = (float[])before.Clone();
        var channels = document.Channels;
        var frames = (int)range.Length;
        if (frames <= 1)
        {
            Array.Clear(after);
        }
        else
        {
            for (var frame = 0; frame < frames; frame++)
            {
                var t = frame / (double)(frames - 1);
                var gain = FadeCurves.Gain(shape, fadeIn, t);
                var offset = frame * channels;
                for (var ch = 0; ch < channels; ch++)
                {
                    after[offset + ch] *= gain;
                }
            }
        }

        return new ReplaceRangeCommand(
            name,
            range.StartFrame,
            before,
            after,
            document.Selection,
            selectionAfter,
            document.CursorFrame,
            selectionAfter.StartFrame,
            UiStrings.EditHistoryRange(
                UiStrings.EditHistoryName(name),
                document.SampleRate,
                selectionAfter.StartFrame,
                selectionAfter.EndFrame,
                UiStrings.LabelFadeCurveShort((int)shape)));
    }

    private static void ApplyFadeGains(
        float[] samples,
        int channels,
        long bufferStart,
        WaveSelection range,
        bool fadeIn)
    {
        if (range.IsEmpty || samples.Length == 0 || channels < 1)
        {
            return;
        }

        var frames = (int)range.Length;
        var offsetFrames = (int)(range.StartFrame - bufferStart);
        if (frames <= 1)
        {
            var index = offsetFrames * channels;
            if ((uint)index < (uint)samples.Length)
            {
                Array.Clear(samples, index, Math.Min(channels, samples.Length - index));
            }

            return;
        }

        for (var frame = 0; frame < frames; frame++)
        {
            var t = frame / (double)(frames - 1);
            var gain = FadeCurves.Gain(FadeShape.Linear, fadeIn, t);
            var offset = (offsetFrames + frame) * channels;
            if ((uint)offset >= (uint)samples.Length)
            {
                break;
            }

            var count = Math.Min(channels, samples.Length - offset);
            for (var ch = 0; ch < count; ch++)
            {
                samples[offset + ch] *= gain;
            }
        }
    }
}
