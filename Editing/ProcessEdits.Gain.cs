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
            document.CursorFrame,
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

}
