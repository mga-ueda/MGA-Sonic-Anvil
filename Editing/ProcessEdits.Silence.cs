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
        int fadeMs = ClickGuard.DefaultFadeMilliseconds,
        bool edgesOnly = false) =>
        DeleteSilence(
            document,
            range,
            SilenceDetectOptions.FromThreshold(thresholdDb, edgesOnly),
            channel,
            channelMask,
            fadeMs);

    public static IEditCommand? DeleteSilence(
        AudioDocument document,
        WaveSelection range,
        SilenceDetectOptions options,
        int channel = ChannelSolo.Off,
        int channelMask = 0,
        int fadeMs = -1)
    {
        range = range.Clamp(document.FrameCount);
        if (range.IsEmpty)
        {
            return null;
        }

        options = options.Normalized();
        if (fadeMs >= 0)
        {
            var clamped = SilenceDetectOptions.ClampFadeMs(fadeMs);
            options = options with { FadeInMs = clamped, FadeOutMs = clamped };
        }

        var mask = EditMask(document, channel, channelMask);
        var keeps = CollectDeleteSilenceKeepSpans(document, range, mask, options);
        var silents = CollectDeleteSilenceSpans(document, range, mask, options);
        if (silents.Count == 0)
        {
            return null;
        }

        var after = SilenceDetect.CopyKeepRanges(document.Interleaved, document.Channels, keeps);
        if (keeps.Count > 0)
        {
            ClickGuard.FadePackedRuns(
                after,
                document.Channels,
                keeps,
                range.StartFrame,
                range.EndFrame,
                SilenceDetectOptions.FadeFrames(document.SampleRate, options.FadeInMs),
                SilenceDetectOptions.FadeFrames(document.SampleRate, options.FadeOutMs));
        }

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
        var markersAfter = MapMarkersThroughDeletes(document.SnapshotMarkers(), silents);
        var regionsAfter = MapRegionsThroughDeletes(document.SnapshotRegions(), silents, document.FrameCount - (oldFrames - newFrames));
        var loopAfter = MapSelectionThroughDeletes(document.SampleLoop, silents, inclusiveEnd: true)
            .Clamp(document.FrameCount - (oldFrames - newFrames));
        var command = new SpliceRangeCommand(
            "Delete Silence",
            range.StartFrame,
            before,
            after,
            document.Selection,
            WaveSelection.Empty,
            document.CursorFrame,
            range.StartFrame,
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
            (target, mapped) => DeleteSilence(
                target,
                mapped,
                options,
                channelMask: mask));
        command.Persist = HistoryRecipes.FromDeleteSilence(
            document.SampleRate,
            range,
            options,
            channelMask: mask);
        return command;
    }

    /// <summary>削除対象の無音区間。</summary>
    private static List<WaveSelection> CollectDeleteSilenceSpans(
        AudioDocument document,
        WaveSelection range,
        int soloMask,
        SilenceDetectOptions options)
    {
        range = range.Clamp(document.FrameCount);
        if (range.IsEmpty)
        {
            return [];
        }

        return SilenceDetect.CollectSilentSpans(
            document.Interleaved,
            document.Channels,
            range.StartFrame,
            range.EndFrame,
            document.SampleRate,
            soloMask,
            options);
    }

    /// <summary>残す区間（プレビュー／削除／リージョン／マーカー用）。</summary>
    public static List<WaveSelection> CollectDeleteSilenceKeepSpans(
        AudioDocument document,
        WaveSelection range,
        int soloMask,
        SilenceDetectOptions options)
    {
        range = range.Clamp(document.FrameCount);
        if (range.IsEmpty)
        {
            return [];
        }

        return SilenceDetect.CollectKeepSpans(
            document.Interleaved,
            document.Channels,
            range.StartFrame,
            range.EndFrame,
            document.SampleRate,
            soloMask,
            options);
    }

    /// <summary>
    /// しきい値で検出した可聴区間をリージョンにする。
    /// 解析範囲と重なる既存リージョンは外し、検出結果で置き換える。
    /// </summary>
    public static IEditCommand? CreateRegionsFromSilenceDetection(
        AudioDocument document,
        WaveSelection range,
        double thresholdDb,
        int channel = ChannelSolo.Off,
        int channelMask = 0,
        bool edgesOnly = false) =>
        CreateRegionsFromSilenceDetection(
            document,
            range,
            SilenceDetectOptions.FromThreshold(thresholdDb, edgesOnly),
            channel,
            channelMask);

    public static IEditCommand? CreateRegionsFromSilenceDetection(
        AudioDocument document,
        WaveSelection range,
        SilenceDetectOptions options,
        int channel = ChannelSolo.Off,
        int channelMask = 0)
    {
        if (!document.AllowsRegionsAndLoops)
        {
            return null;
        }

        range = range.Clamp(document.FrameCount);
        if (range.IsEmpty)
        {
            return null;
        }

        options = options.Normalized();
        var mask = EditMask(document, channel, channelMask);
        var keeps = CollectDeleteSilenceKeepSpans(document, range, mask, options);
        if (keeps.Count == 0)
        {
            return null;
        }

        var before = document.SnapshotRegions();
        var after = new List<WaveRegion>(before.Length + keeps.Count);
        foreach (var region in before)
        {
            if (RangesOverlap(region.Range, range))
            {
                continue;
            }

            after.Add(region);
        }

        foreach (var keep in keeps)
        {
            after.Add(new WaveRegion(keep, string.Empty));
        }

        after.Sort((left, right) => left.StartFrame.CompareTo(right.StartFrame));
        var next = after.ToArray();
        if (RegionsEqual(before, next))
        {
            return null;
        }

        var summary = UiStrings.EditHistoryRange(
            UiStrings.EditHistoryName("Detect Silence"),
            document.SampleRate,
            range.StartFrame,
            range.EndFrame);
        var command = new SetRegionCommand(before, next, summary);
        var sourceRate = document.SampleRate;
        command.Replay = target => CreateRegionsFromSilenceDetection(
            target,
            EditReplay.MapRange(range, sourceRate, target),
            options,
            channelMask: mask);
        command.Persist = HistoryRecipes.FromRegionSnapshots(HistoryRecipes.ReplaceRegions, sourceRate, next);
        return command;
    }

    /// <summary>
    /// しきい値で検出した可聴区間の先頭にマーカーを付ける。
    /// 解析範囲内の既存マーカーは外し、検出結果で置き換える。
    /// </summary>
    public static IEditCommand? CreateMarkersFromSilenceDetection(
        AudioDocument document,
        WaveSelection range,
        double thresholdDb,
        int channel = ChannelSolo.Off,
        int channelMask = 0,
        bool edgesOnly = false) =>
        CreateMarkersFromSilenceDetection(
            document,
            range,
            SilenceDetectOptions.FromThreshold(thresholdDb, edgesOnly),
            channel,
            channelMask);

    public static IEditCommand? CreateMarkersFromSilenceDetection(
        AudioDocument document,
        WaveSelection range,
        SilenceDetectOptions options,
        int channel = ChannelSolo.Off,
        int channelMask = 0)
    {
        range = range.Clamp(document.FrameCount);
        if (range.IsEmpty)
        {
            return null;
        }

        options = options.Normalized();
        var mask = EditMask(document, channel, channelMask);
        var keeps = CollectDeleteSilenceKeepSpans(document, range, mask, options);
        if (keeps.Count == 0)
        {
            return null;
        }

        var before = document.SnapshotMarkers();
        var after = new List<MarkerSnapshot>(before.Length + keeps.Count);
        foreach (var marker in before)
        {
            if (marker.Frame >= range.StartFrame && marker.Frame < range.EndFrame)
            {
                continue;
            }

            after.Add(marker);
        }

        foreach (var keep in keeps)
        {
            var frame = keep.StartFrame;
            if (after.Exists(marker => marker.Frame == frame))
            {
                continue;
            }

            after.Add(new MarkerSnapshot(frame, string.Empty));
        }

        after.Sort((left, right) => left.Frame.CompareTo(right.Frame));
        var next = after.ToArray();
        var command = ApplyMarkers(
            document,
            before,
            next,
            "Detect Silence Markers");
        if (command is null)
        {
            return null;
        }

        var sourceRate = document.SampleRate;
        command.Replay = target => CreateMarkersFromSilenceDetection(
            target,
            EditReplay.MapRange(range, sourceRate, target),
            options,
            channelMask: mask);
        return command;
    }

    private static bool RangesOverlap(WaveSelection a, WaveSelection b) =>
        !a.IsEmpty && !b.IsEmpty && a.StartFrame < b.EndFrame && b.StartFrame < a.EndFrame;

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
