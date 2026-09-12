using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal static partial class ProcessEdits
{
    public static IEditCommand Delete(
        AudioDocument document,
        WaveSelection range,
        int channel = ChannelSolo.Off,
        int channelMask = 0)
    {
        var mask = EditMask(document, channel, channelMask);
        if (ChannelSamples.IsScoped(mask, document.Channels))
        {
            var before = document.CopyRange(range.StartFrame, range.Length);
            var after = (float[])before.Clone();
            ChannelSamples.Silence(after, document.Channels, mask);
            var silence = new ReplaceRangeCommand(
                "Delete",
                range.StartFrame,
                before,
                after,
                document.Selection,
                document.Selection,
                document.CursorFrame,
                document.CursorFrame,
                UiStrings.EditHistoryRange(
                    UiStrings.EditHistoryName("Delete"),
                    document.SampleRate,
                    range.StartFrame,
                    range.EndFrame));
            AttachRangeReplay(silence, document.SampleRate, range, (target, mapped) => Delete(target, mapped, channelMask: mask));
            silence.Persist = HistoryRecipes.Range(HistoryRecipes.Delete, document.SampleRate, range, channelMask: mask);
            return silence;
        }

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
        AttachRangeReplay(command, document.SampleRate, range, (target, mapped) => Delete(target, mapped, channelMask: mask));
        command.Persist = HistoryRecipes.Range(HistoryRecipes.Delete, document.SampleRate, range, channelMask: mask);
        return command;
    }

    public static AudioClip? Copy(
        AudioDocument document,
        WaveSelection range,
        int channel = ChannelSolo.Off,
        int channelMask = 0)
    {
        range = range.Clamp(document.FrameCount);
        var mask = EditMask(document, channel, channelMask);
        if (range.IsEmpty)
        {
            return null;
        }

        var interleaved = document.CopyRange(range.StartFrame, range.Length);
        if (ChannelSamples.IsScoped(mask, document.Channels))
        {
            var extracted = ChannelSamples.ExtractScope(interleaved, document.Channels, mask);
            return new AudioClip(
                extracted.Samples,
                extracted.Channels,
                document.SampleRate,
                document.SnapshotMarkersInRange(range),
                document.SnapshotExactRegions(range));
        }

        return new AudioClip(
            interleaved,
            document.Channels,
            document.SampleRate,
            document.SnapshotMarkersInRange(range),
            document.SnapshotExactRegions(range));
    }

    public static IEditCommand? Paste(
        AudioDocument document,
        AudioClip clip,
        long insertFrame,
        int channel = ChannelSolo.Off,
        int channelMask = 0)
    {
        if (clip.IsEmpty)
        {
            return null;
        }

        var mask = EditMask(document, channel, channelMask);
        if (ChannelSamples.IsScoped(mask, document.Channels))
        {
            return PasteChannel(document, clip, insertFrame, mask);
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
        command.Replay = target => Paste(target, clip, EditReplay.MapFrame(resolvedFrame, sourceRate, target), channelMask: mask);
        command.Persist = HistoryRecipes.FromPaste(sourceRate, resolvedFrame, clip, channelMask: mask);
        return command;
    }

    private static IEditCommand? PasteChannel(
        AudioDocument document,
        AudioClip clip,
        long insertFrame,
        int mask)
    {
        var replace = document.Selection.Clamp(document.FrameCount);
        long destStart;
        long destFrames;
        if (!replace.IsEmpty)
        {
            destStart = replace.StartFrame;
            destFrames = replace.Length;
        }
        else
        {
            destStart = Math.Clamp(insertFrame, 0, document.FrameCount);
            destFrames = clip.FrameCount;
        }

        destFrames = Math.Min(destFrames, document.FrameCount - destStart);
        if (destFrames <= 0)
        {
            return null;
        }

        var before = document.CopyRange(destStart, destFrames);
        var after = (float[])before.Clone();
        ChannelSamples.WriteFitted(after, document.Channels, mask, clip.Interleaved, clip.Channels);
        var command = new ReplaceRangeCommand(
            "Paste",
            destStart,
            before,
            after,
            document.Selection,
            document.Selection,
            document.CursorFrame,
            document.CursorFrame,
            UiStrings.EditHistoryRange(
                UiStrings.EditHistoryName("Paste"),
                document.SampleRate,
                destStart,
                destStart + destFrames));
        var sourceRate = document.SampleRate;
        command.Replay = target => Paste(target, clip, EditReplay.MapFrame(destStart, sourceRate, target), channelMask: mask);
        command.Persist = HistoryRecipes.FromPaste(sourceRate, destStart, clip, channelMask: mask);
        return command;
    }

}
