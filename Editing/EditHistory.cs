using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal interface IEditCommand
{
    string Name { get; }

    void Apply(AudioDocument document);

    void Revert(AudioDocument document);
}

internal sealed class EditHistory
{
    private readonly Stack<IEditCommand> _undo = new();
    private readonly Stack<IEditCommand> _redo = new();

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public int UndoCount => _undo.Count;

    public int RedoCount => _redo.Count;

    public string? UndoName => _undo.Count > 0 ? _undo.Peek().Name : null;

    public string? RedoName => _redo.Count > 0 ? _redo.Peek().Name : null;

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    public void Do(AudioDocument document, IEditCommand command)
    {
        command.Apply(document);
        _undo.Push(command);
        _redo.Clear();
        document.ClampCursor();
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
        document.ClampCursor();
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
        document.ClampCursor();
        return true;
    }
}

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
        long cursorAfter)
    {
        Name = name;
        _startFrame = startFrame;
        _before = before;
        _after = after;
        _selectionBefore = selectionBefore;
        _selectionAfter = selectionAfter;
        _cursorBefore = cursorBefore;
        _cursorAfter = cursorAfter;
    }

    public string Name { get; }

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

    public DeleteRangeCommand(
        long startFrame,
        float[] removed,
        WaveSelection selectionBefore,
        long cursorBefore,
        MarkerSnapshot[] markersBefore,
        WaveSelection sampleLoopBefore)
    {
        _startFrame = startFrame;
        _removed = removed;
        _selectionBefore = selectionBefore;
        _cursorBefore = cursorBefore;
        _markersBefore = markersBefore;
        _sampleLoopBefore = sampleLoopBefore;
    }

    public string Name => "Delete";

    public void Apply(AudioDocument document)
    {
        var frames = _removed.Length / document.Channels;
        document.DeleteRange(_startFrame, frames);
        document.ApplyDeleteToMarkers(_startFrame, frames);
        document.ApplyDeleteToSampleLoop(_startFrame, frames);
        document.Selection = WaveSelection.Empty;
        document.CursorFrame = _startFrame;
    }

    public void Revert(AudioDocument document)
    {
        document.InsertRange(_startFrame, _removed);
        document.ReplaceMarkers(_markersBefore);
        document.SetSampleLoop(_sampleLoopBefore);
        document.Selection = _selectionBefore;
        document.CursorFrame = _cursorBefore;
    }
}

internal sealed class AddMarkerCommand : IEditCommand
{
    private readonly long _frame;
    private readonly MarkerSnapshot[] _markersBefore;

    public AddMarkerCommand(long frame, MarkerSnapshot[] markersBefore)
    {
        _frame = frame;
        _markersBefore = markersBefore;
    }

    public string Name => "Add Marker";

    public void Apply(AudioDocument document) => document.TryAddMarker(_frame);

    public void Revert(AudioDocument document) => document.ReplaceMarkers(_markersBefore);
}

internal sealed class SetMarkerCommentCommand : IEditCommand
{
    private readonly long _frame;
    private readonly string _before;
    private readonly string _after;

    public SetMarkerCommentCommand(long frame, string before, string after)
    {
        _frame = frame;
        _before = before;
        _after = after;
    }

    public string Name => "Marker Comment";

    public void Apply(AudioDocument document) => document.TrySetMarkerComment(_frame, _after);

    public void Revert(AudioDocument document) => document.TrySetMarkerComment(_frame, _before);
}

internal sealed class ReplaceMarkersCommand : IEditCommand
{
    private readonly MarkerSnapshot[] _before;
    private readonly MarkerSnapshot[] _after;

    public ReplaceMarkersCommand(string name, MarkerSnapshot[] before, MarkerSnapshot[] after)
    {
        Name = name;
        _before = before;
        _after = after;
    }

    public string Name { get; }

    public void Apply(AudioDocument document) => document.ReplaceMarkers(_after);

    public void Revert(AudioDocument document) => document.ReplaceMarkers(_before);
}

internal sealed class SetSampleLoopCommand : IEditCommand
{
    private readonly WaveSelection _before;
    private readonly WaveSelection _after;

    public SetSampleLoopCommand(WaveSelection before, WaveSelection after)
    {
        _before = before;
        _after = after;
    }

    public string Name => "Set Sample Loop";

    public void Apply(AudioDocument document) => document.SetSampleLoop(_after);

    public void Revert(AudioDocument document) => document.SetSampleLoop(_before);
}

internal static class ProcessEdits
{
    public static IEditCommand FadeIn(AudioDocument document, WaveSelection range) =>
        FadeIn(document, range, FadeCurves.Default);

    public static IEditCommand FadeOut(AudioDocument document, WaveSelection range) =>
        FadeOut(document, range, FadeCurves.Default);

    public static IEditCommand FadeIn(AudioDocument document, WaveSelection range, FadeShape shape) =>
        Fade(document, range, fadeIn: true, "Fade In", shape);

    public static IEditCommand FadeOut(AudioDocument document, WaveSelection range, FadeShape shape) =>
        Fade(document, range, fadeIn: false, "Fade Out", shape);

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
        return new ReplaceRangeCommand(
            "Fade Around Playhead",
            union.StartFrame,
            before,
            after,
            document.Selection,
            document.Selection,
            document.CursorFrame,
            cursor);
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

        return new ReplaceRangeCommand(
            "Normalize",
            range.StartFrame,
            before,
            after,
            document.Selection,
            range,
            document.CursorFrame,
            range.StartFrame);
    }

    public static IEditCommand Delete(AudioDocument document, WaveSelection range)
    {
        var removed = document.CopyRange(range.StartFrame, range.Length);
        return new DeleteRangeCommand(
            range.StartFrame,
            removed,
            document.Selection,
            document.CursorFrame,
            document.SnapshotMarkers(),
            document.SampleLoop);
    }

    public static IEditCommand? SetSampleLoop(AudioDocument document, WaveSelection range)
    {
        var before = document.SampleLoop;
        var after = range.Clamp(document.FrameCount);
        if (before == after)
        {
            return null;
        }

        return new SetSampleLoopCommand(before, after);
    }

    public static IEditCommand AddMarker(AudioDocument document, long frame)
    {
        return new AddMarkerCommand(frame, document.SnapshotMarkers());
    }

    public static IEditCommand? SetMarkerComment(AudioDocument document, long frame, string comment)
    {
        var before = document.MarkerCommentAt(frame);
        var after = MarkerRoles.Normalize(comment);
        if (before == after || !document.HasMarkerAt(frame))
        {
            return null;
        }

        return new SetMarkerCommentCommand(frame, before, after);
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

        return new ReplaceMarkersCommand("Delete Markers", before, after);
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
        return new ReplaceMarkersCommand(name, before, after);
    }

    public static IEditCommand? ReplaceMarkers(MarkerSnapshot[] before, MarkerSnapshot[] after, string name)
    {
        if (before.AsSpan().SequenceEqual(after))
        {
            return null;
        }

        return new ReplaceMarkersCommand(name, before, after);
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
            selectionAfter.StartFrame);
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
