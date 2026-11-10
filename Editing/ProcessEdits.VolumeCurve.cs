using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal readonly record struct VolumeCurveRenderResult(float[] AfterRange, float PeakAbs);

internal static partial class ProcessEdits
{
    /// <summary>
    /// 音量カーブを範囲に適用。フレームごとの dB → リニアゲインを掛ける。
    /// </summary>
    public static IEditCommand? VolumeCurve(
        AudioDocument document,
        WaveSelection range,
        VolumeCurve curve,
        IProgress<double>? progress = null,
        int channel = ChannelSolo.Off,
        int channelMask = 0,
        float[]? precomputedAfterRange = null)
    {
        range = range.Clamp(document.FrameCount);
        var mask = EditMask(document, channel, channelMask);
        if (range.IsEmpty || curve.IsNoOp())
        {
            progress?.Report(1);
            return null;
        }

        progress?.Report(0);
        var beforeRange = document.CopyRange(range.StartFrame, range.Length);
        float[] afterRange;
        if (precomputedAfterRange is not null)
        {
            afterRange = (float[])precomputedAfterRange.Clone();
            progress?.Report(1);
        }
        else
        {
            afterRange = RenderVolumeCurveRange(document, range, curve, mask, progress).AfterRange;
        }

        var curveCopy = curve.Clone();
        var extra = UiStrings.FormatVolumeCurveExtra(curveCopy);
        var command = new ReplaceRangeCommand(
            "Volume",
            range.StartFrame,
            beforeRange,
            afterRange,
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
        AttachRangeReplay(
            command,
            document.SampleRate,
            range,
            (target, mapped) => VolumeCurve(target, mapped, curveCopy, channelMask: mask));
        command.Persist = HistoryRecipes.FromVolumeCurve(document.SampleRate, range, curveCopy, channelMask: mask);
        progress?.Report(1);
        return command;
    }

    public static VolumeCurveRenderResult RenderVolumeCurveRange(
        AudioDocument document,
        WaveSelection range,
        VolumeCurve curve,
        int channelMask,
        IProgress<double>? progress = null)
    {
        range = range.Clamp(document.FrameCount);
        var mask = EditMask(document, ChannelSolo.Off, channelMask);
        var before = document.CopyRange(range.StartFrame, range.Length);
        if (range.IsEmpty || before.Length == 0)
        {
            progress?.Report(1);
            return new VolumeCurveRenderResult([], 0f);
        }

        var channels = Math.Max(1, document.Channels);
        var frames = (int)Math.Min(int.MaxValue, range.Length);
        var table = curve.BuildGainDbTable(frames);
        var after = (float[])before.Clone();
        var peak = 0f;
        for (var frame = 0; frame < frames; frame++)
        {
            var linear = (float)WaveformGainAnalyzer.LinearFromDb(table[frame]);
            var index = frame * channels;
            for (var ch = 0; ch < channels; ch++)
            {
                if (!ChannelSolo.Contains(mask, ch))
                {
                    continue;
                }

                var sample = after[index + ch] * linear;
                after[index + ch] = sample;
                var abs = Math.Abs(sample);
                if (abs > peak)
                {
                    peak = abs;
                }
            }

            if ((frame & 0x3FFF) == 0)
            {
                progress?.Report(frame / (double)Math.Max(1, frames));
            }
        }

        progress?.Report(1);
        return new VolumeCurveRenderResult(after, peak);
    }
}
