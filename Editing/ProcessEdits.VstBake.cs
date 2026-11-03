using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal static partial class ProcessEdits
{
    /// <summary>
    /// VST インサート通過後のサンプルで文書全体を置き換え、バイパス副作用も履歴に載せる。
    /// <paramref name="after"/> は interleaved。元と同じ長さ、または Include Tail で長くなったもの。
    /// </summary>
    public static IEditCommand? BakeVstInserts(
        AudioDocument document,
        float[] after,
        Action applyBypass,
        Action revertBypass)
    {
        ArgumentNullException.ThrowIfNull(applyBypass);
        ArgumentNullException.ThrowIfNull(revertBypass);

        if (document.FrameCount <= 0 || after.Length == 0)
        {
            return null;
        }

        var channels = Math.Max(1, document.Channels);
        if (after.Length % channels != 0)
        {
            return null;
        }

        var afterFrames = after.Length / channels;
        if (afterFrames < document.FrameCount)
        {
            return null;
        }

        var before = document.CopyRange(0, document.FrameCount);
        var afterCopy = (float[])after.Clone();
        var endFrame = afterFrames;
        var command = new BakeVstInsertsCommand(
            before,
            afterCopy,
            document.Selection,
            document.Selection,
            document.CursorFrame,
            document.CursorFrame,
            UiStrings.EditHistoryRange(
                UiStrings.EditHistoryName("Bounce Inserts"),
                document.SampleRate,
                0,
                endFrame),
            applyBypass,
            revertBypass);
        // 再適用はサンプル直書き（VST／バイパスは再現しない）。セッション復元も Record 相当。
        command.Replay = target =>
        {
            var targetChannels = Math.Max(1, target.Channels);
            if (afterCopy.Length == 0 || afterCopy.Length % targetChannels != 0)
            {
                return null;
            }

            if (afterCopy.Length / targetChannels < target.FrameCount)
            {
                return null;
            }

            return BakeVstInserts(target, afterCopy, static () => { }, static () => { });
        };
        command.Persist = HistoryRecipes.FromRecord(document.SampleRate, 0, afterCopy, channels);
        return command;
    }
}
