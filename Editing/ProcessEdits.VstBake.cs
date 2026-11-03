using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal static partial class ProcessEdits
{
    /// <summary>
    /// VST インサート通過後のサンプルで文書全体を置き換え、バイパス副作用も履歴に載せる。
    /// <paramref name="after"/> は interleaved・文書と同じ長さであること。
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
        var expected = checked((int)(document.FrameCount * channels));
        if (after.Length != expected)
        {
            return null;
        }

        var before = document.CopyRange(0, document.FrameCount);
        var afterCopy = (float[])after.Clone();
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
                document.FrameCount),
            applyBypass,
            revertBypass);
        // 再適用はサンプル直書き（VST／バイパスは再現しない）。セッション復元も Record 相当。
        command.Replay = target =>
        {
            if (afterCopy.Length != checked((int)(target.FrameCount * Math.Max(1, target.Channels))))
            {
                return null;
            }

            return BakeVstInserts(target, afterCopy, static () => { }, static () => { });
        };
        command.Persist = HistoryRecipes.FromRecord(document.SampleRate, 0, afterCopy, channels);
        return command;
    }
}
