using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.UI;

internal readonly record struct HistoryPasteBusySnapshot(
    double Overall,
    int Finished,
    IReadOnlyList<ExportJobProgress> Jobs);

/// <summary>履歴貼り付けのすりガラス。タブの長さで全体を重み付けし、全タブを行として出す。</summary>
internal static class HistoryPasteBusyProgress
{
    public static HistoryPasteBusySnapshot Capture(
        IReadOnlyList<string> names,
        IReadOnlyList<double> progress,
        IReadOnlyList<long> frameCounts)
    {
        var count = names.Count;
        var jobs = new ExportJobProgress[count];
        var finished = 0;
        var weighted = 0d;
        var weightSum = 0d;
        for (var i = 0; i < count; i++)
        {
            var value = i < progress.Count ? Math.Clamp(progress[i], 0, 1) : 0;
            jobs[i] = new ExportJobProgress(names[i], value);
            if (value >= 1)
            {
                finished++;
            }

            var weight = Math.Max(1, i < frameCounts.Count ? frameCounts[i] : 1);
            weightSum += weight;
            weighted += value * weight;
        }

        return new HistoryPasteBusySnapshot(
            weightSum <= 0 ? 0 : weighted / weightSum,
            finished,
            jobs);
    }
}
