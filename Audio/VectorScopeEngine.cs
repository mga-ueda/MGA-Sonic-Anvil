namespace MgaSonicAnvil.Audio;

/// <summary>L/R を Mid/Side と位相相関へ落とす。表示は VectorScopeView。</summary>
internal static class VectorScopeEngine
{
    public static void MidSide(float left, float right, out float mid, out float side)
    {
        mid = (left + right) * 0.5f;
        side = (left - right) * 0.5f;
    }

    public static double Correlation(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        var count = Math.Min(left.Length, right.Length);
        if (count <= 0)
        {
            return 0;
        }

        double sumLr = 0;
        double sumL2 = 0;
        double sumR2 = 0;
        for (var i = 0; i < count; i++)
        {
            var l = left[i];
            var r = right[i];
            sumLr += l * (double)r;
            sumL2 += l * (double)l;
            sumR2 += r * (double)r;
        }

        var denom = Math.Sqrt(sumL2 * sumR2);
        return denom < 1e-12 ? 0 : Math.Clamp(sumLr / denom, -1d, 1d);
    }

    public static float PeakMidSide(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        var count = Math.Min(left.Length, right.Length);
        var peak = 0f;
        for (var i = 0; i < count; i++)
        {
            MidSide(left[i], right[i], out var mid, out var side);
            var local = Math.Max(Math.Abs(mid), Math.Abs(side));
            if (local > peak)
            {
                peak = local;
            }
        }

        return peak;
    }

    public static string FormatCorrelation(double value)
    {
        var clamped = Math.Clamp(value, -1d, 1d);
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{clamped:+0.00;-0.00;+0.00}");
    }
}
