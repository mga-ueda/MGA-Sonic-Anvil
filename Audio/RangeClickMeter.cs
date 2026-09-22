using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Audio;

/// <summary>
/// 等分クリックの拍子。拍数が 4 で割れるなら 4/4（Hi Low Low Low）、
/// 3 で割れるなら 3/4（Hi Low Low）。両方割れるときは 4 優先。
/// </summary>
internal static class RangeClickMeter
{
    public static int BeatCount(ReadOnlySpan<long> frames, WaveSelection range)
    {
        if (frames.Length == 0 || range.IsEmpty)
        {
            return 0;
        }

        var n = frames.Length;
        if (frames[^1] == range.EndFrame)
        {
            n--;
        }

        return n;
    }

    public static int GroupSize(int beatCount)
    {
        if (beatCount > 0 && beatCount % 4 == 0)
        {
            return 4;
        }

        if (beatCount > 0 && beatCount % 3 == 0)
        {
            return 3;
        }

        return 0;
    }

    public static bool IsDownbeat(int beatIndex, int groupSize) =>
        groupSize > 0 && beatIndex >= 0 && beatIndex % groupSize == 0;
}
