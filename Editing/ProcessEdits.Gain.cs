using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal static partial class ProcessEdits
{
    public static IEditCommand Normalize(
        AudioDocument document,
        WaveSelection range,
        int channel = ChannelSolo.Off,
        int channelMask = 0)
    {
        var mask = EditMask(document, channel, channelMask);
        var before = document.CopyRange(range.StartFrame, range.Length);
        var after = (float[])before.Clone();
        var peak = ChannelSamples.Peak(after, document.Channels, mask);

        if (peak > 1e-8f)
        {
            // Sound Forge 既定に合わせ -0.1 dB ヘッドルーム。
            var target = (float)Math.Pow(10d, -0.1d / 20d);
            ChannelSamples.ApplyGain(after, document.Channels, mask, target / peak);
        }

        var command = new ReplaceRangeCommand(
            "Normalize",
            range.StartFrame,
            before,
            after,
            document.Selection,
            WaveSelection.Empty,
            document.CursorFrame,
            range.StartFrame,
            UiStrings.EditHistoryRange(
                UiStrings.EditHistoryName("Normalize"),
                document.SampleRate,
                range.StartFrame,
                range.EndFrame));
        // 再適用時はゲインを適用先のピークから再計算する。
        AttachRangeReplay(command, document.SampleRate, range, (target, mapped) => Normalize(target, mapped, channelMask: mask));
        command.Persist = HistoryRecipes.Range(HistoryRecipes.Normalize, document.SampleRate, range, channelMask: mask);
        return command;
    }

    public static IEditCommand? Gain(
        AudioDocument document,
        WaveSelection range,
        double gainDb,
        int channel = ChannelSolo.Off,
        int channelMask = 0)
    {
        range = range.Clamp(document.FrameCount);
        var mask = EditMask(document, channel, channelMask);
        gainDb = WaveformGainAnalyzer.SnapGainDb(gainDb);
        if (range.IsEmpty || WaveformGainAnalyzer.IsNoOp(gainDb))
        {
            return null;
        }

        var linear = (float)WaveformGainAnalyzer.LinearFromDb(gainDb);
        var before = document.CopyRange(range.StartFrame, range.Length);
        var after = (float[])before.Clone();
        ChannelSamples.ApplyGain(after, document.Channels, mask, linear);

        var extra = UiStrings.FormatSignedDb(gainDb);
        var command = new ReplaceRangeCommand(
            "Volume",
            range.StartFrame,
            before,
            after,
            document.Selection,
            WaveSelection.Empty,
            document.CursorFrame,
            range.StartFrame,
            UiStrings.EditHistoryRange(
                UiStrings.EditHistoryName("Volume"),
                document.SampleRate,
                range.StartFrame,
                range.EndFrame,
                extra));
        var snapped = gainDb;
        AttachRangeReplay(command, document.SampleRate, range, (target, mapped) => Gain(target, mapped, snapped, channelMask: mask));
        command.Persist = HistoryRecipes.FromGain(document.SampleRate, range, snapped, channelMask: mask);
        return command;
    }

    public static IEditCommand? PenGain(
        AudioDocument document,
        long startFrame,
        float[] gains,
        int channelMask = 0)
    {
        if (gains is not { Length: > 0 } || document.FrameCount <= 0)
        {
            return null;
        }

        startFrame = Math.Clamp(startFrame, 0, document.FrameCount - 1);
        var length = Math.Min(gains.Length, (int)(document.FrameCount - startFrame));
        if (length <= 0)
        {
            return null;
        }

        var mask = EditMask(document, ChannelSolo.Off, channelMask);
        var before = document.CopyRange(startFrame, length);
        var after = (float[])before.Clone();
        var channels = Math.Max(1, document.Channels);
        var changed = false;
        for (var frame = 0; frame < length; frame++)
        {
            var gain = gains[frame];
            if (Math.Abs(gain - 1f) < 1e-6f)
            {
                continue;
            }

            changed = true;
            var index = frame * channels;
            for (var ch = 0; ch < channels; ch++)
            {
                if (ChannelSolo.Contains(mask, ch))
                {
                    after[index + ch] *= gain;
                }
            }
        }

        if (!changed)
        {
            return null;
        }

        var stored = gains.Length == length ? gains : gains.AsSpan(0, length).ToArray();
        var end = startFrame + length;
        var command = new ReplaceRangeCommand(
            "Pen",
            startFrame,
            before,
            after,
            document.Selection,
            document.Selection,
            document.CursorFrame,
            document.CursorFrame,
            UiStrings.EditHistoryRange(
                UiStrings.EditHistoryName("Pen"),
                document.SampleRate,
                startFrame,
                end));
        command.Replay = target => PenGain(target, startFrame, stored, mask);
        command.Persist = HistoryRecipes.FromPenGain(document.SampleRate, startFrame, end, stored, mask);
        return command;
    }

}
