using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal static partial class ProcessEdits
{
    public static IEditCommand? ApplyLoopMix(AudioDocument document, CrossfadeEditState draft)
    {
        if (document.FrameCount <= 0 || document.Channels < 1 || draft.LoopLength <= 0)
        {
            return null;
        }

        var state = draft.Clone();
        state.Clamp(document.FrameCount);
        if (state.LoopLength <= 0)
        {
            return null;
        }

        var channels = document.Channels;
        var frameCount = document.FrameCount;
        var before = document.CopyRange(0, frameCount);
        var work = (float[])before.Clone();

        // プリ＝S 手前をエンドへ、ポスト＝E 以降をスタートへ。折り返しは常に S。
        if (state.PreCrossfadeFrames > 0)
        {
            LoopCrossfade.BakePreCrossfade(
                before,
                work,
                channels,
                frameCount,
                state.StartFrame,
                state.EndFrame,
                state.PreCrossfadeFrames,
                state.PreOutShape,
                state.PreInShape);
        }

        if (state.PostCrossfadeFrames > 0)
        {
            LoopCrossfade.BakePostCrossfade(
                before,
                work,
                channels,
                frameCount,
                state.StartFrame,
                state.EndFrame,
                state.PostCrossfadeFrames,
                state.PostOutShape,
                state.PostInShape);
        }

        float[] after;
        WaveSelection loopAfter;
        MarkerSnapshot[] markersAfter;
        WaveRegion[] regionsAfter;
        WaveSelection selectionAfter;
        long cursorAfter;

        if (state.TruncateOutside)
        {
            // 焼き込みは [S,E) 内。E 以降を切り落す。
            var keepStart = state.StartFrame;
            var keepEnd = Math.Min(frameCount, state.EndFrame);
            if (keepEnd <= keepStart)
            {
                return null;
            }

            var keepLen = keepEnd - keepStart;
            after = new float[checked((int)keepLen * channels)];
            Array.Copy(work, checked((int)keepStart * channels), after, 0, after.Length);

            loopAfter = new WaveSelection(0, state.LoopLength).Clamp(keepLen);
            markersAfter = ShiftMarkers(document.SnapshotMarkers(), keepStart, keepEnd);
            regionsAfter = ShiftRegions(document.SnapshotRegions(), keepStart, keepEnd);
            selectionAfter = ShiftSelection(document.Selection, keepStart, keepEnd);
            cursorAfter = Math.Clamp(document.CursorFrame - keepStart, 0, keepLen);
        }
        else
        {
            after = work;
            loopAfter = state.LoopRange.Clamp(frameCount);
            markersAfter = document.SnapshotMarkers();
            regionsAfter = document.SnapshotRegions();
            selectionAfter = document.Selection.Clamp(frameCount);
            cursorAfter = Math.Clamp(document.CursorFrame, 0, frameCount);
        }

        var summary = UiStrings.EditHistoryName("Loop Mix");
        var command = new SpliceRangeCommand(
            "Loop Mix",
            startFrame: 0,
            before,
            after,
            document.Selection,
            selectionAfter,
            document.CursorFrame,
            cursorAfter,
            document.SnapshotMarkers(),
            markersAfter,
            document.SampleLoop,
            loopAfter,
            document.SnapshotRegions(),
            regionsAfter,
            summary);

        var source = state.Clone();
        var sourceRate = document.SampleRate;
        command.Replay = target => ApplyLoopMix(target, RemapState(source, sourceRate, target));
        command.Persist = new HistoryRecipe
        {
            Kind = HistoryRecipes.LoopMix,
            SourceRate = sourceRate,
            Start = source.StartFrame,
            End = source.EndFrame,
            Frame = source.PreCrossfadeFrames,
            Playhead = source.PostCrossfadeFrames,
            Value = (int)source.PreOutShape,
            Channel = (int)source.PostOutShape,
            // +1: 0 は旧レシピ（イン脚未保存）→ アウトと同型にフォールバック
            Timing0 = (int)source.PreInShape + 1,
            Timing1 = (int)source.PostInShape + 1,
            Flag = source.TruncateOutside,
        };
        return command;
    }

    private static CrossfadeEditState RemapState(
        CrossfadeEditState source,
        int sourceRate,
        AudioDocument target)
    {
        var mapped = source.Clone();
        mapped.StartFrame = EditReplay.MapFrame(source.StartFrame, sourceRate, target);
        mapped.EndFrame = EditReplay.MapFrame(source.EndFrame, sourceRate, target);
        mapped.PreCrossfadeFrames = EditReplay.MapFrame(source.PreCrossfadeFrames, sourceRate, target);
        mapped.PostCrossfadeFrames = EditReplay.MapFrame(source.PostCrossfadeFrames, sourceRate, target);
        mapped.Clamp(target.FrameCount);
        return mapped;
    }

    private static MarkerSnapshot[] ShiftMarkers(
        MarkerSnapshot[] markers,
        long keepStart,
        long keepEnd)
    {
        var list = new List<MarkerSnapshot>(markers.Length);
        foreach (var marker in markers)
        {
            if (marker.Frame < keepStart || marker.Frame >= keepEnd)
            {
                continue;
            }

            list.Add(new MarkerSnapshot(marker.Frame - keepStart, marker.Comment));
        }

        return list.ToArray();
    }

    private static WaveRegion[] ShiftRegions(WaveRegion[] regions, long keepStart, long keepEnd)
    {
        var list = new List<WaveRegion>(regions.Length);
        foreach (var region in regions)
        {
            var start = Math.Max(region.StartFrame, keepStart) - keepStart;
            var end = Math.Min(region.EndFrame, keepEnd) - keepStart;
            if (end <= start)
            {
                continue;
            }

            list.Add(new WaveRegion(start, end, region.Name));
        }

        return list.ToArray();
    }

    private static WaveSelection ShiftSelection(WaveSelection selection, long keepStart, long keepEnd)
    {
        if (selection.IsEmpty)
        {
            return WaveSelection.Empty;
        }

        var start = Math.Max(selection.StartFrame, keepStart) - keepStart;
        var end = Math.Min(selection.EndFrame, keepEnd) - keepStart;
        return end <= start ? WaveSelection.Empty : new WaveSelection(start, end);
    }
}
