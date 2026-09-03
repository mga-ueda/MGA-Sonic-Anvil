namespace MgaSonicAnvil.Audio;

/// <summary>
/// WAVEFORMATEXTENSIBLE の一般的な並び（L, R, C, LFE, Ls, Rs …）に沿った表示名。
/// </summary>
internal static class ChannelLabels
{
    private static readonly string[] Named =
        ["L", "R", "C", "LFE", "Ls", "Rs", "Lsr", "Rsr"];

    public static string Name(int index, int channelCount)
    {
        if (channelCount <= 1)
        {
            return "M";
        }

        if ((uint)index >= (uint)channelCount)
        {
            return $"Ch{index + 1}";
        }

        return index < Named.Length ? Named[index] : $"Ch{index + 1}";
    }
}
