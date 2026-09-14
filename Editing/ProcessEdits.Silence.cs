using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal static partial class ProcessEdits
{
    public static IEditCommand? DeleteSilence(
        AudioDocument document,
        WaveSelection range,
        double thresholdDb,
        int channel = ChannelSolo.Off,
        int channelMask = 0,
        int fadeMs = ClickGuard.DefaultFadeMilliseconds)
    {
        range = range.Clamp(document.FrameCount);
        if (range.IsEmpty)
        {
            return null;
        }

        var mask = EditMask(document, channel, channelMask);
        fadeMs = ClickGuard.ClampFadeMs(fadeMs);
        var threshold = SilentSkip.LinearFromDb(thresholdDb);
        var holdFrames = SilentSkip.PeakWindowRadiusFrames(document.SampleRate);
        var after = SilentSkip.CopyAudibleRange(
            document.Interleaved,
            document.Channels,
            range.StartFrame,
            range.EndFrame,
            threshold,
            mask,
            holdFrames);
        ClickGuard.FadePackedRuns(
            after,
            document.Channels,
            SilentSkip.CollectAudibleSpans(
                document.Interleaved,
                document.Channels,
                range.StartFrame,
                range.EndFrame,
                threshold,
                mask,
                holdFrames),
            range.StartFrame,
            range.EndFrame,
            ClickGuard.FadeFrames(document.SampleRate, fadeMs));
        var oldFrames = range.Length;
        var newFrames = after.Length / Math.Max(1, document.Channels);
        if (newFrames == oldFrames)
        {
            return null;
        }

        if (newFrames == 0
            && range.StartFrame <= 0
            && range.EndFrame >= document.FrameCount)
        {
            return null;
        }

        var before = document.CopyRange(range.StartFrame, oldFrames);
        var silents = SilentSkip.CollectSilentSpans(
            document.Interleaved,
            document.Channels,
            range.StartFrame,
            range.EndFrame,
            threshold,
            mask,
            holdFrames);
        var markersAfter = MapMarkersThroughDeletes(document.SnapshotMarkers(), silents);
        var regionsAfter = MapRegionsThroughDeletes(document.SnapshotRegions(), silents, document.FrameCount - (oldFrames - newFrames));
        var loopAfter = MapSelectionThroughDeletes(document.SampleLoop, silents, inclusiveEnd: true)
            .Clamp(document.FrameCount - (oldFrames - newFrames));
        var cursorAfter = MapFrameThroughDeletes(document.CursorFrame, silents, inclusiveEnd: false);
        var command = new SpliceRangeCommand(
            "Delete Silence",
            range.StartFrame,
            before,
            after,
            document.Selection,
            WaveSelection.Empty,
            document.CursorFrame,
            cursorAfter,
            document.SnapshotMarkers(),
            markersAfter,
            document.SampleLoop,
            loopAfter,
            document.SnapshotRegions(),
            regionsAfter,
            UiStrings.EditHistoryRange(
                UiStrings.EditHistoryName("Delete Silence"),
                document.SampleRate,
                range.StartFrame,
                range.EndFrame));
        AttachRangeReplay(
            command,
            document.SampleRate,
            range,
            (target, mapped) => DeleteSilence(target, mapped, thresholdDb, channelMask: mask, fadeMs: fadeMs));
        command.Persist = HistoryRecipes.FromDeleteSilence(
            document.SampleRate,
            range,
            thresholdDb,
            channelMask: mask,
            fadeMs: fadeMs);
        return command;
    }

    private static MarkerSnapshot[] MapMarkersThroughDeletes(
        IReadOnlyList<MarkerSnapshot> markers,
        IReadOnlyList<WaveSelection> silents)
    {
        if (markers.Count == 0)
        {
            return [];
        }

        var next = new List<MarkerSnapshot>(markers.Count);
        foreach (var marker in markers)
        {
            if (IsInsideAny(marker.Frame, silents, inclusiveEnd: false))
            {
                continue;
            }

            next.Add(new MarkerSnapshot(MapFrameThroughDeletes(marker.Frame, silents, inclusiveEnd: false), marker.Comment));
        }

        return [.. next];
    }

    private static WaveRegion[] MapRegionsThroughDeletes(
        IReadOnlyList<WaveRegion> regions,
        IReadOnlyList<WaveSelection> silents,
        long frameCount)
    {
        if (regions.Count == 0)
        {
            return [];
        }

        var next = new List<WaveRegion>(regions.Count);
        foreach (var region in regions)
        {
            var mapped = MapSelectionThroughDeletes(region.Range, silents, inclusiveEnd: true).Clamp(frameCount);
            if (!mapped.IsEmpty)
            {
                next.Add(new WaveRegion(mapped, region.Name));
            }
        }

        return [.. next];
    }

    private static WaveSelection MapSelectionThroughDeletes(
        WaveSelection range,
        IReadOnlyList<WaveSelection> silents,
        bool inclusiveEnd)
    {
        if (range.IsEmpty)
        {
            return WaveSelection.Empty;
        }

        var start = MapFrameThroughDeletes(range.StartFrame, silents, inclusiveEnd: false);
        var end = MapFrameThroughDeletes(range.EndFrame, silents, inclusiveEnd);
        return new WaveSelection(start, end);
    }

    private static bool IsInsideAny(long frame, IReadOnlyList<WaveSelection> silents, bool inclusiveEnd)
    {
        foreach (var span in silents)
        {
            if (inclusiveEnd)
            {
                if (frame > span.StartFrame && frame <= span.EndFrame)
                {
                    return true;
                }

                continue;
            }

            if (frame >= span.StartFrame && frame < span.EndFrame)
            {
                return true;
            }
        }

        return false;
    }

    private static long MapFrameThroughDeletes(
        long frame,
        IReadOnlyList<WaveSelection> silents,
        bool inclusiveEnd)
    {
        var dest = frame;
        for (var i = silents.Count - 1; i >= 0; i--)
        {
            var span = silents[i];
            dest = ShiftFrameThroughDelete(dest, span.StartFrame, span.EndFrame, span.Length, inclusiveEnd);
        }

        return dest;
    }

    private static long ShiftFrameThroughDelete(
        long frame,
        long deleteStart,
        long deleteEnd,
        long deletedCount,
        bool inclusiveEnd)
    {
        if (inclusiveEnd)
        {
            if (frame <= deleteStart)
            {
                return frame;
            }

            return frame <= deleteEnd ? deleteStart : frame - deletedCount;
        }

        if (frame < deleteStart)
        {
            return frame;
        }

        return frame < deleteEnd ? deleteStart : frame - deletedCount;
    }
}
