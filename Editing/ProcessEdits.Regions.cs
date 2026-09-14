using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal static partial class ProcessEdits
{
    public static IEditCommand? NormalizePerRegion(
        AudioDocument document,
        int channel = ChannelSolo.Off,
        int channelMask = 0,
        int fadeMs = ClickGuard.DefaultFadeMilliseconds) =>
        NormalizePerRegion(document, RegionRanges(document), channel, channelMask, fadeMs);

    public static IEditCommand? NormalizePerRegion(
        AudioDocument document,
        IReadOnlyList<WaveSelection> ranges,
        int channel = ChannelSolo.Off,
        int channelMask = 0,
        int fadeMs = ClickGuard.DefaultFadeMilliseconds) =>
        ApplyPerRegion(
            document,
            ranges,
            channel,
            channelMask,
            fadeMs,
            "Normalize Per Region",
            HistoryRecipes.NormalizePerRegion,
            NormalizeRange);

    private static WaveSelection[] RegionRanges(AudioDocument document)
    {
        if (!document.AllowsRegionsAndLoops)
        {
            return [];
        }

        return [.. document.SnapshotRegions()
            .Select(region => region.Range.Clamp(document.FrameCount))
            .Where(range => !range.IsEmpty)];
    }

    private static IEditCommand? ApplyPerRegion(
        AudioDocument document,
        IReadOnlyList<WaveSelection> ranges,
        int channel,
        int channelMask,
        int fadeMs,
        string name,
        string kind,
        Action<float[], int, WaveSelection, long, int, int> apply)
    {
        var mask = EditMask(document, channel, channelMask);
        var clipped = ClipRanges(ranges, document.FrameCount);
        if (clipped.Length == 0)
        {
            return null;
        }

        fadeMs = ClickGuard.ClampFadeMs(fadeMs);
        var union = clipped[0];
        for (var i = 1; i < clipped.Length; i++)
        {
            union = union.Union(clipped[i]);
        }

        var before = document.CopyRange(union.StartFrame, union.Length);
        var after = (float[])before.Clone();
        var fadeFrames = ClickGuard.FadeFrames(document.SampleRate, fadeMs);
        foreach (var range in clipped)
        {
            apply(after, document.Channels, range, union.StartFrame, mask, fadeFrames);
        }

        if (after.AsSpan().SequenceEqual(before))
        {
            return null;
        }

        var command = new ReplaceRangeCommand(
            name,
            union.StartFrame,
            before,
            after,
            document.Selection,
            WaveSelection.Empty,
            document.CursorFrame,
            document.CursorFrame,
            UiStrings.EditHistoryRange(
                UiStrings.EditHistoryName(name),
                document.SampleRate,
                union.StartFrame,
                union.EndFrame));
        var stored = clipped;
        command.Replay = target =>
            ApplyPerRegion(
                target,
                MapRanges(stored, document.SampleRate, target),
                ChannelSolo.Off,
                mask,
                fadeMs,
                name,
                kind,
                apply);
        command.Persist = HistoryRecipes.FromRegionEdits(
            kind,
            document.SampleRate,
            stored,
            channelMask: mask,
            fadeMs: fadeMs);
        return command;
    }

    private static WaveSelection[] ClipRanges(IReadOnlyList<WaveSelection> ranges, long frameCount)
    {
        var clipped = new List<WaveSelection>(ranges.Count);
        foreach (var range in ranges)
        {
            var next = range.Clamp(frameCount);
            if (!next.IsEmpty)
            {
                clipped.Add(next);
            }
        }

        return [.. clipped];
    }

    private static WaveSelection[] MapRanges(
        IReadOnlyList<WaveSelection> ranges,
        int sourceRate,
        AudioDocument target)
    {
        var mapped = new WaveSelection[ranges.Count];
        for (var i = 0; i < ranges.Count; i++)
        {
            mapped[i] = EditReplay.MapRange(ranges[i], sourceRate, target);
        }

        return mapped;
    }

    private static void NormalizeRange(
        float[] samples,
        int channels,
        WaveSelection range,
        long bufferStart,
        int mask,
        int fadeFrames)
    {
        var peak = ChannelSamples.Peak(samples, channels, mask, bufferStart, range);
        if (peak > 1e-8f)
        {
            var target = (float)Math.Pow(10d, -0.1d / 20d);
            ChannelSamples.ApplyGain(
                samples,
                channels,
                mask,
                target / peak,
                bufferStart,
                range);
        }

        ClickGuard.FadeRangeEdges(
            samples,
            channels,
            bufferStart,
            range,
            mask,
            fadeIn: true,
            fadeOut: true,
            fadeFrames);
    }
}
