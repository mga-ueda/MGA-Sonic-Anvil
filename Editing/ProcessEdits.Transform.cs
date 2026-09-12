using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal static partial class ProcessEdits
{
    public static IEditCommand? PitchShift(
        AudioDocument document,
        WaveSelection range,
        int semitones,
        bool timeStretch = true,
        IProgress<double>? progress = null,
        int channel = ChannelSolo.Off,
        int channelMask = 0)
    {
        range = range.Clamp(document.FrameCount);
        var mask = EditMask(document, channel, channelMask);
        semitones = Audio.PitchShift.Snap(semitones);
        if (range.IsEmpty || Audio.PitchShift.IsNoOp(semitones))
        {
            return null;
        }

        progress?.Report(0);
        var before = document.CopyRange(range.StartFrame, range.Length);
        float[] after;
        if (ChannelSamples.IsScoped(mask, document.Channels))
        {
            var extracted = ChannelSamples.ExtractScope(before, document.Channels, mask);
            var processed = Audio.PitchShift.Apply(
                extracted.Samples,
                extracted.Channels,
                document.SampleRate,
                semitones,
                timeStretch,
                progress);
            after = (float[])before.Clone();
            ChannelSamples.WriteFitted(after, document.Channels, mask, processed, extracted.Channels);
        }
        else
        {
            after = Audio.PitchShift.Apply(
                before,
                document.Channels,
                document.SampleRate,
                semitones,
                timeStretch,
                progress);
        }

        progress?.Report(1);
        var extra = UiStrings.FormatPitchShiftExtra(semitones, timeStretch);
        var snapped = semitones;
        var stretch = timeStretch;
        if (after.Length == before.Length)
        {
            var command = new ReplaceRangeCommand(
                "Pitch Shift",
                range.StartFrame,
                before,
                after,
                document.Selection,
                WaveSelection.Empty,
                document.CursorFrame,
                document.CursorFrame,
                UiStrings.EditHistoryRange(
                    UiStrings.EditHistoryName("Pitch Shift"),
                    document.SampleRate,
                    range.StartFrame,
                    range.EndFrame,
                    extra));
            AttachRangeReplay(
                command,
                document.SampleRate,
                range,
                (target, mapped) => PitchShift(target, mapped, snapped, stretch, channelMask: mask));
            command.Persist = HistoryRecipes.FromPitchShift(document.SampleRate, range, snapped, stretch, channelMask: mask);
            return command;
        }

        var oldFrames = range.Length;
        var newFrames = after.Length / Math.Max(1, document.Channels);
        var markersAfter = MapMarkersThroughRange(document.SnapshotMarkers(), range.StartFrame, oldFrames, newFrames);
        var regionsAfter = MapRegionsThroughRange(document.SnapshotRegions(), range.StartFrame, oldFrames, newFrames);
        var loopAfter = MapSelectionThroughRange(document.SampleLoop, range.StartFrame, oldFrames, newFrames);
        var cursorAfter = AudioDocument.MapFrameThroughRangeStretch(
            document.CursorFrame,
            range.StartFrame,
            oldFrames,
            newFrames);
        var splice = new SpliceRangeCommand(
            "Pitch Shift",
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
                UiStrings.EditHistoryName("Pitch Shift"),
                document.SampleRate,
                range.StartFrame,
                range.EndFrame,
                extra));
        AttachRangeReplay(
            splice,
            document.SampleRate,
            range,
            (target, mapped) => PitchShift(target, mapped, snapped, stretch, channelMask: mask));
        splice.Persist = HistoryRecipes.FromPitchShift(document.SampleRate, range, snapped, stretch, channelMask: mask);
        return splice;
    }

    public static IEditCommand? TimeStretch(
        AudioDocument document,
        WaveSelection range,
        int destFrames,
        IProgress<double>? progress = null,
        int channel = ChannelSolo.Off,
        int channelMask = 0)
    {
        range = range.Clamp(document.FrameCount);
        var mask = EditMask(document, channel, channelMask);
        destFrames = Audio.TimeStretch.ClampDestFrames((int)range.Length, destFrames);
        if (range.IsEmpty || Audio.TimeStretch.IsNoOp((int)range.Length, destFrames))
        {
            return null;
        }

        progress?.Report(0);
        var before = document.CopyRange(range.StartFrame, range.Length);
        float[] after;
        if (ChannelSamples.IsScoped(mask, document.Channels))
        {
            var extracted = ChannelSamples.ExtractScope(before, document.Channels, mask);
            var processed = Audio.TimeStretch.Apply(
                extracted.Samples,
                extracted.Channels,
                document.SampleRate,
                destFrames,
                progress);
            after = (float[])before.Clone();
            ChannelSamples.WriteFitted(after, document.Channels, mask, processed, extracted.Channels);
        }
        else
        {
            after = Audio.TimeStretch.Apply(
                before,
                document.Channels,
                document.SampleRate,
                destFrames,
                progress);
        }

        progress?.Report(1);
        var extra = UiStrings.FormatTimeStretchExtra(document.SampleRate, (int)range.Length, destFrames);
        var oldFrames = range.Length;
        var ratio = destFrames / (double)oldFrames;
        if (after.Length == before.Length)
        {
            var replace = new ReplaceRangeCommand(
                "Time Stretch",
                range.StartFrame,
                before,
                after,
                document.Selection,
                WaveSelection.Empty,
                document.CursorFrame,
                document.CursorFrame,
                UiStrings.EditHistoryRange(
                    UiStrings.EditHistoryName("Time Stretch"),
                    document.SampleRate,
                    range.StartFrame,
                    range.EndFrame,
                    extra));
            AttachRangeReplay(replace, document.SampleRate, range, (target, mapped) =>
                TimeStretch(
                    target,
                    mapped,
                    Audio.TimeStretch.DestFrameCountFromRatio((int)mapped.Length, ratio),
                    channelMask: mask));
            replace.Persist = HistoryRecipes.FromTimeStretch(document.SampleRate, range, ratio, channelMask: mask);
            return replace;
        }

        var newFrames = after.Length / Math.Max(1, document.Channels);
        var markersAfter = MapMarkersThroughRange(document.SnapshotMarkers(), range.StartFrame, oldFrames, newFrames);
        var regionsAfter = MapRegionsThroughRange(document.SnapshotRegions(), range.StartFrame, oldFrames, newFrames);
        var loopAfter = MapSelectionThroughRange(document.SampleLoop, range.StartFrame, oldFrames, newFrames);
        var cursorAfter = AudioDocument.MapFrameThroughRangeStretch(
            document.CursorFrame,
            range.StartFrame,
            oldFrames,
            newFrames);
        var splice = new SpliceRangeCommand(
            "Time Stretch",
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
                UiStrings.EditHistoryName("Time Stretch"),
                document.SampleRate,
                range.StartFrame,
                range.EndFrame,
                extra));
        AttachRangeReplay(splice, document.SampleRate, range, (target, mapped) =>
            TimeStretch(
                target,
                mapped,
                Audio.TimeStretch.DestFrameCountFromRatio((int)mapped.Length, ratio),
                channelMask: mask));
        splice.Persist = HistoryRecipes.FromTimeStretch(document.SampleRate, range, ratio, channelMask: mask);
        return splice;
    }

    public static IEditCommand? Reverse(
        AudioDocument document,
        WaveSelection range,
        int channel = ChannelSolo.Off,
        int channelMask = 0)
    {
        range = range.Clamp(document.FrameCount);
        var mask = EditMask(document, channel, channelMask);
        if (range.Length < 2)
        {
            return null;
        }

        var before = document.CopyRange(range.StartFrame, range.Length);
        var after = Audio.Reverse.Apply(before, document.Channels);
        ChannelSamples.RestoreOthers(before, after, document.Channels, mask);
        var command = new ReplaceRangeCommand(
            "Reverse",
            range.StartFrame,
            before,
            after,
            document.Selection,
            WaveSelection.Empty,
            document.CursorFrame,
            range.StartFrame,
            UiStrings.EditHistoryRange(
                UiStrings.EditHistoryName("Reverse"),
                document.SampleRate,
                range.StartFrame,
                range.EndFrame));
        AttachRangeReplay(command, document.SampleRate, range, (target, mapped) => Reverse(target, mapped, channelMask: mask));
        command.Persist = HistoryRecipes.Range(HistoryRecipes.Reverse, document.SampleRate, range, channelMask: mask);
        return command;
    }

    private static MarkerSnapshot[] MapMarkersThroughRange(
        MarkerSnapshot[] markers,
        long rangeStart,
        long oldLength,
        long newLength)
    {
        if (markers.Length == 0)
        {
            return markers;
        }

        var mapped = new MarkerSnapshot[markers.Length];
        for (var i = 0; i < markers.Length; i++)
        {
            mapped[i] = markers[i] with
            {
                Frame = AudioDocument.MapFrameThroughRangeStretch(
                    markers[i].Frame,
                    rangeStart,
                    oldLength,
                    newLength),
            };
        }

        return mapped;
    }

    private static WaveRegion[] MapRegionsThroughRange(
        WaveRegion[] regions,
        long rangeStart,
        long oldLength,
        long newLength)
    {
        if (regions.Length == 0)
        {
            return regions;
        }

        var mapped = new WaveRegion[regions.Length];
        for (var i = 0; i < regions.Length; i++)
        {
            var start = AudioDocument.MapFrameThroughRangeStretch(
                regions[i].StartFrame,
                rangeStart,
                oldLength,
                newLength);
            var end = AudioDocument.MapFrameThroughRangeStretch(
                regions[i].EndFrame,
                rangeStart,
                oldLength,
                newLength);
            mapped[i] = new WaveRegion(start, end, regions[i].Name);
        }

        return mapped;
    }

    private static WaveSelection MapSelectionThroughRange(
        WaveSelection range,
        long rangeStart,
        long oldLength,
        long newLength)
    {
        if (range.IsEmpty)
        {
            return range;
        }

        var start = AudioDocument.MapFrameThroughRangeStretch(range.StartFrame, rangeStart, oldLength, newLength);
        var end = AudioDocument.MapFrameThroughRangeStretch(range.EndFrame, rangeStart, oldLength, newLength);
        return new WaveSelection(start, end);
    }


}
