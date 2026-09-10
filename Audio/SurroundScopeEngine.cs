namespace MgaSonicAnvil.Audio;

/// <summary>サラウンドの方位角とエネルギー包絡。表示は VectorScopeView。</summary>
internal static class SurroundScopeEngine
{
    public const int EnvelopeSteps = 180;
    /// <summary>LFE 円の、外円に対する半径比（フルスケール）。</summary>
    public const double LfeRadius = 0.30;

    public static bool IsSurround(int channels) => channels > 2;

    public static ChannelLayout LayoutOf(int channels) => ChannelLayout.Guess(Math.Max(1, channels));

    public static bool IsLfeLabel(string label) =>
        label.Equals("LFE", StringComparison.OrdinalIgnoreCase);

    /// <summary>正面を 0°、右回りを正。LFE と未知ラベルは false。</summary>
    public static bool TryAzimuthDegrees(string label, out double degrees)
    {
        degrees = label switch
        {
            "M" or "C" => 0,
            "L" or "Tfl" => -30,
            "R" or "Tfr" => 30,
            "Lw" => -60,
            "Rw" => 60,
            "Tsl" => -90,
            "Tsr" => 90,
            "Ls" => -110,
            "Rs" => 110,
            "Lsr" or "Lb" or "Tbl" => -135,
            "Rsr" or "Rb" or "Tbr" => 135,
            _ => double.NaN,
        };
        return double.IsFinite(degrees);
    }

    /// <summary>リニア振幅をメーターと同じ dB 正規化半径（0..1）にする。</summary>
    public static float RadiusFromLinear(float linear)
    {
        var db = LevelMeterEngine.ToDb(Math.Max(0, linear));
        return (float)LevelMeterEngine.DbToNorm(db);
    }

    public static float LfeLevel(ReadOnlySpan<float> levels, ChannelLayout layout)
    {
        var n = Math.Min(levels.Length, layout.Channels);
        for (var i = 0; i < n; i++)
        {
            if (IsLfeLabel(layout.LabelAt(i)))
            {
                return Math.Clamp(levels[i], 0, 1);
            }
        }

        return 0;
    }

    public static void FillEnvelope(ReadOnlySpan<float> levels, ChannelLayout layout, Span<float> envelope)
    {
        envelope.Clear();
        var n = Math.Min(levels.Length, layout.Channels);
        for (var ch = 0; ch < n; ch++)
        {
            var label = layout.LabelAt(ch);
            if (IsLfeLabel(label) || !TryAzimuthDegrees(label, out var azimuth))
            {
                continue;
            }

            var level = Math.Clamp(levels[ch], 0, 1);
            if (level <= 1e-6f)
            {
                continue;
            }

            AddLobe(envelope, azimuth, level);
        }

        for (var i = 0; i < envelope.Length; i++)
        {
            if (envelope[i] > 1f)
            {
                envelope[i] = 1f;
            }
        }
    }

    public static void PolarToXy(
        double azimuthDegrees,
        double radius,
        double cx,
        double cy,
        double plotRadius,
        out double x,
        out double y)
    {
        var rad = azimuthDegrees * Math.PI / 180d;
        x = cx + plotRadius * radius * Math.Sin(rad);
        y = cy - plotRadius * radius * Math.Cos(rad);
    }

    public static double StepAzimuth(int step, int steps)
    {
        var n = Math.Max(1, steps);
        return step * 360d / n;
    }

    private static void AddLobe(Span<float> envelope, double azimuthDegrees, float level)
    {
        var steps = envelope.Length;
        if (steps <= 0)
        {
            return;
        }

        for (var i = 0; i < steps; i++)
        {
            var delta = WrapDelta(StepAzimuth(i, steps) - azimuthDegrees);
            var weight = Math.Max(0, Math.Cos(delta * Math.PI / 180d));
            envelope[i] += level * (float)(weight * weight);
        }
    }

    private static double WrapDelta(double degrees)
    {
        var d = degrees;
        while (d > 180)
        {
            d -= 360;
        }

        while (d < -180)
        {
            d += 360;
        }

        return d;
    }
}
