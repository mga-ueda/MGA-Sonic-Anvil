using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal static partial class ProcessEdits
{
    /// <summary>再生ヘッド以降を take で上書き。take より後ろの元波形は残す。take が長ければ伸ばす。</summary>
    public static IEditCommand? RecordOverwrite(AudioDocument document, long startFrame, float[] take)
    {
        startFrame = Math.Clamp(startFrame, 0, document.FrameCount);
        var beforeFrames = document.FrameCount - startFrame;
        var before = beforeFrames > 0 ? document.CopyRange(startFrame, beforeFrames) : [];
        return RecordOverwrite(document, startFrame, before, take);
    }

    public static IEditCommand? RecordOverwrite(
        AudioDocument document,
        long startFrame,
        float[] before,
        float[] take)
    {
        var channels = Math.Max(1, document.Channels);
        if (take.Length < channels)
        {
            return null;
        }

        take = AlignTake(take, channels);
        startFrame = Math.Clamp(startFrame, 0, document.FrameCount);
        var takeFrames = take.Length / channels;
        var after = CombineTakeAndLeftover(take, before, channels);
        if (after.Length == before.Length && after.AsSpan().SequenceEqual(before))
        {
            return null;
        }

        var takeEnd = startFrame + takeFrames;
        var command = new SpliceRangeCommand(
            "Record",
            startFrame,
            before,
            after,
            document.Selection,
            WaveSelection.Empty,
            document.CursorFrame,
            takeEnd,
            document.SnapshotMarkers(),
            document.SnapshotMarkers(),
            document.SampleLoop,
            document.SampleLoop,
            document.SnapshotRegions(),
            document.SnapshotRegions(),
            UiStrings.EditHistoryRange(
                UiStrings.EditHistoryName("Record"),
                document.SampleRate,
                startFrame,
                takeEnd));
        var sourceRate = document.SampleRate;
        command.Replay = target => RecordOverwrite(
            target,
            EditReplay.MapFrame(startFrame, sourceRate, target),
            take);
        command.Persist = HistoryRecipes.FromRecord(sourceRate, startFrame, take, channels);
        return command;
    }

    public static float[] CombineTakeAndLeftover(float[] take, float[] before, int channels)
    {
        channels = Math.Max(1, channels);
        take = AlignTake(take, channels);
        var leftover = Math.Max(0, before.Length - take.Length);
        if (leftover == 0)
        {
            return take;
        }

        var after = new float[take.Length + leftover];
        Array.Copy(take, after, take.Length);
        Array.Copy(before, take.Length, after, take.Length, leftover);
        return after;
    }

    private static float[] AlignTake(float[] take, int channels)
    {
        var aligned = take.Length - (take.Length % channels);
        if (aligned == take.Length)
        {
            return take;
        }

        if (aligned <= 0)
        {
            return [];
        }

        var copy = new float[aligned];
        Array.Copy(take, copy, aligned);
        return copy;
    }
}
