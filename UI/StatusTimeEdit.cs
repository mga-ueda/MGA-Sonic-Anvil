using System.Windows.Input;
using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.UI;

internal static class StatusTimeEdit
{
    public static long NudgeStep(bool showSamples, int sampleRate, ModifierKeys modifiers)
    {
        var shift = (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        var ctrl = (modifiers & ModifierKeys.Control) == ModifierKeys.Control
            && (modifiers & ModifierKeys.Alt) == ModifierKeys.None;
        if (!showSamples)
        {
            var seconds = ctrl && shift ? 600 : ctrl ? 60 : shift ? 10 : 1;
            return Math.Max(1, (long)sampleRate * seconds);
        }

        if (ctrl && shift)
        {
            return 1000;
        }

        if (ctrl)
        {
            return 100;
        }

        return shift ? 10 : 1;
    }

    public static long ClampFrame(long frame, long frameCount) =>
        Math.Clamp(frame, 0, Math.Max(0, frameCount));

    public static WaveSelection ApplyStart(WaveSelection current, long start, long frameCount)
    {
        start = ClampFrame(start, frameCount);
        if (current.IsEmpty)
        {
            return start >= frameCount ? WaveSelection.Empty : new WaveSelection(start, frameCount);
        }

        var end = Math.Min(frameCount, current.EndFrame);
        return end <= start ? WaveSelection.Empty : new WaveSelection(start, end);
    }

    public static WaveSelection ApplyEnd(WaveSelection current, long end, long frameCount)
    {
        end = ClampFrame(end, frameCount);
        if (current.IsEmpty)
        {
            return end <= 0 ? WaveSelection.Empty : new WaveSelection(0, end);
        }

        return end <= current.StartFrame
            ? WaveSelection.Empty
            : new WaveSelection(current.StartFrame, end);
    }

    public static WaveSelection ApplyLength(WaveSelection current, long length, long playhead, long frameCount)
    {
        if (length <= 0 || frameCount <= 0)
        {
            return WaveSelection.Empty;
        }

        long start;
        if (current.IsEmpty)
        {
            start = ClampFrame(playhead, frameCount);
            if (start >= frameCount)
            {
                start = Math.Max(0, frameCount - length);
            }
        }
        else
        {
            start = current.StartFrame;
        }

        var end = Math.Min(frameCount, start + length);
        return end <= start ? WaveSelection.Empty : new WaveSelection(start, end);
    }
}
