using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

/// <summary>
/// 履歴レシピを別ドキュメントへ写像する座標変換。レートが違えば時間基準で
/// スケールし、長さはクランプする。
/// </summary>
internal static class EditReplay
{
    public static long MapFrame(long frame, int sourceRate, AudioDocument target)
    {
        var mapped = sourceRate > 0 && sourceRate != target.SampleRate
            ? (long)Math.Round(frame * (double)target.SampleRate / sourceRate)
            : frame;
        return Math.Clamp(mapped, 0, Math.Max(0, target.FrameCount));
    }

    public static WaveSelection MapRange(WaveSelection range, int sourceRate, AudioDocument target)
    {
        if (range.IsEmpty)
        {
            return WaveSelection.Empty;
        }

        return new WaveSelection(
            MapFrame(range.StartFrame, sourceRate, target),
            MapFrame(range.EndFrame, sourceRate, target));
    }

    public static long ScaleDelta(long delta, int sourceRate, int targetRate) =>
        sourceRate > 0 && targetRate > 0 && sourceRate != targetRate
            ? (long)Math.Round(delta * (double)targetRate / sourceRate)
            : delta;
}
