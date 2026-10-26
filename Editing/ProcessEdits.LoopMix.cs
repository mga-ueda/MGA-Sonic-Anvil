using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal static partial class ProcessEdits
{
    public static IEditCommand? ApplyLoopMix(AudioDocument document, CrossfadeEditState draft) =>
        ApplyLoopMix(document, draft, lowerClippedCrossfade: false, out _);

    /// <param name="lowerClippedCrossfade">
    /// true なら、焼き込み区間のピークが 0 dBFS を超えるとき波形全体をフルスケールへ下げる。
    /// </param>
    public static IEditCommand? ApplyLoopMix(
        AudioDocument document,
        CrossfadeEditState draft,
        bool lowerClippedCrossfade,
        out TabMerge.BounceClipInfo clip)
    {
        clip = default;
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

        // プリ＝S 手前をエンド手前へ、ポスト＝ループスタートを E 以降へ。
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

        clip = MeasureCrossfadeBakeClip(work, channels, state);
        if (lowerClippedCrossfade && clip.ExceedsFullScale)
        {
            LowerEntireWaveform(work, clip.Peak);
            clip = MeasureCrossfadeBakeClip(work, channels, state);
        }

        float[] after;
        WaveSelection loopAfter;
        MarkerSnapshot[] markersAfter;
        WaveRegion[] regionsAfter;
        WaveSelection selectionAfter;
        long cursorAfter;

        if (state.TruncateOutside)
        {
            // ポストは [E, E＋Post) へ焼くが、前後削除では終点以降ごと切り落す。
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
        else if (state.DeleteAfterLoop)
        {
            // 終点より後ろだけ削除。始点より前は残す。
            var keepStart = 0L;
            var keepEnd = Math.Min(frameCount, state.EndFrame);
            if (keepEnd <= keepStart)
            {
                return null;
            }

            var keepLen = keepEnd - keepStart;
            after = new float[checked((int)keepLen * channels)];
            Array.Copy(work, 0, after, 0, after.Length);

            loopAfter = state.LoopRange.Clamp(keepLen);
            markersAfter = ShiftMarkers(document.SnapshotMarkers(), keepStart, keepEnd);
            regionsAfter = ShiftRegions(document.SnapshotRegions(), keepStart, keepEnd);
            selectionAfter = ShiftSelection(document.Selection, keepStart, keepEnd);
            cursorAfter = Math.Clamp(document.CursorFrame, 0, keepLen);
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
        var lower = lowerClippedCrossfade;
        command.Replay = target => ApplyLoopMix(
            target,
            RemapState(source, sourceRate, target),
            lower,
            out _);
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
            Flag2 = source.DeleteAfterLoop,
            // 1 = 波形全体をフルスケールへ下げた。旧レシピは 0（下げない）。
            Amount = lower ? 1 : 0,
        };
        return command;
    }

    private static TabMerge.BounceClipInfo MeasureCrossfadeBakeClip(
        float[] samples,
        int channels,
        CrossfadeEditState state)
    {
        var peak = 0f;
        AccumulatePeak(samples, channels, state.EndFrame - state.PreCrossfadeFrames, state.PreCrossfadeFrames, ref peak);
        AccumulatePeak(samples, channels, state.EndFrame, state.PostCrossfadeFrames, ref peak);
        if (!float.IsFinite(peak))
        {
            return new TabMerge.BounceClipInfo { Peak = float.PositiveInfinity, OverDb = double.PositiveInfinity };
        }

        if (!(peak > 1f))
        {
            return new TabMerge.BounceClipInfo { Peak = peak, OverDb = 0 };
        }

        return new TabMerge.BounceClipInfo { Peak = peak, OverDb = LevelMeterEngine.ToDb(peak) };
    }

    private static void LowerEntireWaveform(float[] samples, float peak)
    {
        LowerSampleRange(samples, 0, samples.Length, peak);
    }

    private static void AccumulatePeak(
        float[] samples,
        int channels,
        long startFrame,
        long frameCount,
        ref float peak)
    {
        if (frameCount <= 0 || channels < 1)
        {
            return;
        }

        var start = checked((int)startFrame * channels);
        var end = checked(start + (int)frameCount * channels);
        start = Math.Clamp(start, 0, samples.Length);
        end = Math.Clamp(end, 0, samples.Length);
        for (var i = start; i < end; i++)
        {
            if (!float.IsFinite(samples[i]))
            {
                peak = float.PositiveInfinity;
                return;
            }

            var magnitude = Math.Abs(samples[i]);
            if (magnitude > peak)
            {
                peak = magnitude;
            }
        }
    }

    private static void LowerSampleRange(float[] samples, int offset, int count, float peak)
    {
        if (count <= 0 || offset < 0 || offset >= samples.Length || !(peak > 1f))
        {
            return;
        }

        count = Math.Min(count, samples.Length - offset);
        for (var pass = 0; pass < 3 && peak > 1f; pass++)
        {
            var scale = 1d / peak;
            var next = 0f;
            for (var i = 0; i < count; i++)
            {
                var scaled = (float)(samples[offset + i] * scale);
                samples[offset + i] = scaled;
                var magnitude = Math.Abs(scaled);
                if (magnitude > next)
                {
                    next = magnitude;
                }
            }

            peak = next;
        }
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
