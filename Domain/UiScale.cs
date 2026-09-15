using System.Globalization;

namespace MgaSonicAnvil.Domain;

/// <summary>アプリ表示の追加拡大。OS の DPI のうえに乗せる。100% 未満は使わない。</summary>
internal static class UiScale
{
    public const int DefaultPercent = 100;

    public const int MinPercent = 100;

    public const int MaxPercent = 200;

    public static readonly int[] Percents = [100, 110, 125, 150, 175, 200];

    public static int ClampPercent(int percent)
    {
        if (percent < MinPercent)
        {
            return MinPercent;
        }

        if (percent > MaxPercent)
        {
            return MaxPercent;
        }

        return percent;
    }

    public static double FactorFrom(int percent) => ClampPercent(percent) / 100d;

    public static string FormatPercent(int percent) =>
        ClampPercent(percent).ToString(CultureInfo.InvariantCulture) + "%";

    public static bool IsPreset(int percent)
    {
        foreach (var preset in Percents)
        {
            if (preset == percent)
            {
                return true;
            }
        }

        return false;
    }
}
