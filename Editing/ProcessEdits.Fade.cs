using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal static partial class ProcessEdits
{
    public static IEditCommand FadeIn(AudioDocument document, WaveSelection range) =>
        FadeIn(document, range, FadeCurves.Default);

    public static IEditCommand FadeOut(AudioDocument document, WaveSelection range) =>
        FadeOut(document, range, FadeCurves.Default);

    public static IEditCommand FadeIn(
        AudioDocument document,
        WaveSelection range,
        FadeShape shape,
        int channel = ChannelSolo.Off,
        int channelMask = 0)
    {
        var mask = EditMask(document, channel, channelMask);
        var command = Fade(document, range, fadeIn: true, "Fade In", shape, mask);
        AttachRangeReplay(command, document.SampleRate, range, (target, mapped) => FadeIn(target, mapped, shape, channelMask: mask));
        command.Persist = HistoryRecipes.Range(HistoryRecipes.FadeIn, document.SampleRate, range, (int)shape, channelMask: mask);
        return command;
    }

    public static IEditCommand FadeOut(
        AudioDocument document,
        WaveSelection range,
        FadeShape shape,
        int channel = ChannelSolo.Off,
        int channelMask = 0)
    {
        var mask = EditMask(document, channel, channelMask);
        var command = Fade(document, range, fadeIn: false, "Fade Out", shape, mask);
        AttachRangeReplay(command, document.SampleRate, range, (target, mapped) => FadeOut(target, mapped, shape, channelMask: mask));
        command.Persist = HistoryRecipes.Range(HistoryRecipes.FadeOut, document.SampleRate, range, (int)shape, channelMask: mask);
        return command;
    }

    public static IEditCommand? FadeAroundPlayhead(
        AudioDocument document,
        WaveSelection visible,
        long playhead,
        int channel = ChannelSolo.Off,
        int channelMask = 0)
    {
        var mask = EditMask(document, channel, channelMask);
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
        ApplyFadeGains(after, document.Channels, union.StartFrame, fadeOut, fadeIn: false, mask);
        ApplyFadeGains(after, document.Channels, union.StartFrame, fadeIn, fadeIn: true, mask);
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
                : FadeAroundPlayhead(
                    target,
                    mappedVisible,
                    EditReplay.MapFrame(sourcePlayhead, sourceRate, target),
                    channelMask: mask);
        };
        command.Persist = new HistoryRecipe
        {
            Kind = HistoryRecipes.FadeAround,
            SourceRate = sourceRate,
            Start = sourceVisible.StartFrame,
            End = sourceVisible.EndFrame,
            Playhead = sourcePlayhead,
            ChannelMask = mask,
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

    private static IEditCommand Fade(
        AudioDocument document,
        WaveSelection range,
        bool fadeIn,
        string name,
        FadeShape shape,
        int mask)
    {
        var span = range;
        range = FadeCurves.InclusiveSampleRange(range, document.FrameCount);
        var before = document.CopyRange(range.StartFrame, range.Length);
        var after = (float[])before.Clone();
        var channels = document.Channels;
        var frames = (int)range.Length;
        if (frames <= 1)
        {
            ChannelSamples.Silence(after, channels, mask);
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
                    if (ChannelSolo.Contains(mask, ch))
                    {
                        after[offset + ch] *= gain;
                    }
                }
            }
        }

        return new ReplaceRangeCommand(
            name,
            range.StartFrame,
            before,
            after,
            document.Selection,
            WaveSelection.Empty,
            document.CursorFrame,
            document.CursorFrame,
            UiStrings.EditHistoryRange(
                UiStrings.EditHistoryName(name),
                document.SampleRate,
                span.StartFrame,
                span.EndFrame,
                UiStrings.LabelFadeCurveShort((int)shape)));
    }

    private static void ApplyFadeGains(
        float[] samples,
        int channels,
        long bufferStart,
        WaveSelection range,
        bool fadeIn,
        int mask)
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
                var count = Math.Min(channels, samples.Length - index);
                for (var ch = 0; ch < count; ch++)
                {
                    if (ChannelSolo.Contains(mask, ch))
                    {
                        samples[index + ch] = 0;
                    }
                }
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
                if (ChannelSolo.Contains(mask, ch))
                {
                    samples[offset + ch] *= gain;
                }
            }
        }
    }

}
