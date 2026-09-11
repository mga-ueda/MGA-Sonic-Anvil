namespace MgaSonicAnvil.Audio;

/// <summary>サラウンドの方位角とエネルギー包絡。表示は VectorScopeView。</summary>
internal static class SurroundScopeEngine
{
    public const int EnvelopeSteps = 180;
    /// <summary>LFE 円の、外円に対する半径比（フルスケール）。</summary>
    public const double LfeRadius = 0.30;
    /// <summary>ローブ輪郭に載せる直近サンプル数。先端が今、裾が少し前。</summary>
    public const int WaveformFrames = 256;
    public const double WaveSpreadDeg = 66;
    public const float WaveFloor = 0.56f;
    public const float WaveSpan = 0.44f;
    public const int WrapMaxPoints = 256;
    public const double WrapOuterRatio = 0.22;
    public const double WrapMinRadius = 0.08;
    public const int WrapSmoothSteps = 7;
    public const float WrapInward = 0.88f;
    /// <summary>33ms ティックで約 0.45s 保持。</summary>
    public const int WrapHoldFrames = 14;
    public const float WrapRelease = 0.045f;

    public static bool IsSurround(int channels) => channels > 2;

    public static ChannelLayout LayoutOf(int fileChannels, ChannelLayout speaker, int[]? fileChannelMap = null) =>
        ChannelLayout.ForFile(fileChannels, speaker, fileChannelMap);

    public static bool IsLfeLabel(string label) =>
        label.Equals("LFE", StringComparison.OrdinalIgnoreCase);

    /// <summary>ファイル順のチャンネルを円周へ等間隔。1 本目が正面（0°）。レイアウトは当てない。</summary>
    public static double AzimuthDegrees(int index, int channels)
    {
        var n = Math.Max(1, channels);
        var i = index % n;
        if (i < 0)
        {
            i += n;
        }

        return i * 360d / n;
    }

    /// <summary>正面を 0°、右回りを正。LFE と未知ラベルは false。</summary>
    public static bool TryAzimuthDegrees(string label, out double degrees)
    {
        degrees = label switch
        {
            "M" or "C" => 0,
            "S" or "Cs" or "Bc" => 180,
            "L" or "Tfl" => -30,
            "R" or "Tfr" => 30,
            "Lc" or "Flc" => -15,
            "Rc" or "Frc" => 15,
            "Lw" => -60,
            "Rw" => 60,
            "Sl" or "Tsl" => -90,
            "Sr" or "Tsr" => 90,
            "Ls" => -110,
            "Rs" => 110,
            "Lsr" or "Lb" or "Tbl" => -135,
            "Rsr" or "Rb" or "Tbr" => 135,
            _ => double.NaN,
        };
        return double.IsFinite(degrees);
    }

    /// <summary>リニア振幅とメーター dB 半径の中間（0..1）。</summary>
    public static float RadiusFromLinear(float linear)
    {
        var raw = Math.Clamp(linear, 0, 1);
        var db = (float)LevelMeterEngine.DbToNorm(LevelMeterEngine.ToDb(raw));
        return raw + (db - raw) * 0.5f;
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
            var level = Math.Clamp(levels[ch], 0, 1);
            if (level <= 1e-6f)
            {
                continue;
            }

            var label = layout.LabelAt(ch);
            if (IsLfeLabel(label))
            {
                continue;
            }

            var azimuth = TryAzimuthDegrees(label, out var named)
                ? named
                : AzimuthDegrees(ch, layout.Channels);
            AddLobe(envelope, azimuth, level, default, 0, 0);
        }

        ClampEnvelope(envelope);
    }

    /// <summary>各方位葉の点ごと最大。ピーク全体を囲む枠用。LFE は含めない。</summary>
    public static void FillUnion(ReadOnlySpan<float> levels, ChannelLayout layout, Span<float> envelope) =>
        FillUnion(levels, layout, envelope, default, 0);

    public static void FillUnion(
        ReadOnlySpan<float> levels,
        ChannelLayout layout,
        Span<float> envelope,
        ReadOnlySpan<float> planar,
        int stride)
    {
        envelope.Clear();
        var n = Math.Min(levels.Length, layout.Channels);
        for (var ch = 0; ch < n; ch++)
        {
            var level = Math.Clamp(levels[ch], 0, 1);
            if (level <= 1e-6f)
            {
                continue;
            }

            var label = layout.LabelAt(ch);
            if (IsLfeLabel(label))
            {
                continue;
            }

            var azimuth = TryAzimuthDegrees(label, out var named)
                ? named
                : AzimuthDegrees(ch, layout.Channels);
            MaxLobe(envelope, azimuth, level, planar, stride, ch);
        }

        ClampEnvelope(envelope);
    }

    /// <summary>
    /// 全チャンネルをひとつの輪郭に。本体は凸包、凹凸は波形の残差。
    /// </summary>
    public static void FillCombinedWrap(
        ReadOnlySpan<float> levels,
        ChannelLayout layout,
        Span<float> envelope,
        ReadOnlySpan<float> planar = default,
        int stride = 0)
    {
        envelope.Clear();
        Span<float> union = stackalloc float[EnvelopeSteps];
        FillUnion(levels, layout, union, planar, stride);
        Span<double> hx = stackalloc double[WrapMaxPoints];
        Span<double> hy = stackalloc double[WrapMaxPoints];
        var hull = HullFromUnion(union, hx, hy);
        if (hull < 3)
        {
            return;
        }

        Span<float> smooth = stackalloc float[EnvelopeSteps];
        BoxSmooth(union, smooth, WrapSmoothSteps);
        for (var i = 0; i < envelope.Length; i++)
        {
            var hullR = HullRadiusAt(hx, hy, hull, StepAzimuth(i, envelope.Length));
            if (hullR < WrapMinRadius)
            {
                continue;
            }

            var residual = union[i] - smooth[i];
            var lo = hullR * WrapInward;
            var value = hullR + residual;
            if (value < lo)
            {
                value = lo;
            }

            envelope[i] = (float)Math.Clamp(value, 0, 1);
        }
    }

    /// <summary>枠用。伸びたら保持し、切れたらゆっくり戻す。</summary>
    public static void HoldWrap(Span<float> held, ReadOnlySpan<float> instant, Span<int> holdFrames)
    {
        var n = Math.Min(held.Length, Math.Min(instant.Length, holdFrames.Length));
        for (var i = 0; i < n; i++)
        {
            var now = instant[i];
            if (now >= held[i])
            {
                held[i] = now;
                holdFrames[i] = now >= 0.04f ? WrapHoldFrames : 0;
                continue;
            }

            if (holdFrames[i] > 0)
            {
                holdFrames[i]--;
                continue;
            }

            held[i] += (now - held[i]) * WrapRelease;
            if (held[i] < 0.002f)
            {
                held[i] = 0;
            }
        }
    }

    /// <summary>全チャンネルの外側だけを凸包でひとつに囲む。単位円。</summary>
    public static int CombinedWrapPoints(
        ReadOnlySpan<float> levels,
        ChannelLayout layout,
        ReadOnlySpan<float> planar,
        int stride,
        Span<double> xs,
        Span<double> ys)
    {
        if (xs.Length < 3 || ys.Length < 3)
        {
            return 0;
        }

        Span<float> union = stackalloc float[EnvelopeSteps];
        FillUnion(levels, layout, union, planar, stride);
        return HullFromUnion(union, xs, ys);
    }

    private static int HullFromUnion(ReadOnlySpan<float> union, Span<double> xs, Span<double> ys)
    {
        if (xs.Length < 3 || ys.Length < 3)
        {
            return 0;
        }

        var peak = 0f;
        for (var i = 0; i < union.Length; i++)
        {
            if (union[i] > peak)
            {
                peak = union[i];
            }
        }

        if (peak < 0.02f)
        {
            return 0;
        }

        var cut = Math.Max(WrapMinRadius, peak * WrapOuterRatio);
        Span<double> px = stackalloc double[EnvelopeSteps];
        Span<double> py = stackalloc double[EnvelopeSteps];
        var n = 0;
        for (var i = 0; i < union.Length; i++)
        {
            if (union[i] < cut)
            {
                continue;
            }

            PolarToXy(StepAzimuth(i, union.Length), union[i], 0, 0, 1, out px[n], out py[n]);
            n++;
        }

        if (n < 3)
        {
            return 0;
        }

        n = ConvexHull(px, py, n);
        if (n < 3)
        {
            return 0;
        }

        ClampUnitDisk(px, py, n);
        n = Math.Min(n, Math.Min(xs.Length, ys.Length));
        px[..n].CopyTo(xs);
        py[..n].CopyTo(ys);
        return n;
    }

    private static void BoxSmooth(ReadOnlySpan<float> source, Span<float> dest, int radius)
    {
        var n = source.Length;
        var w = Math.Max(0, radius);
        var span = (2 * w) + 1;
        for (var i = 0; i < n; i++)
        {
            var sum = 0f;
            for (var k = -w; k <= w; k++)
            {
                var j = i + k;
                while (j < 0)
                {
                    j += n;
                }

                while (j >= n)
                {
                    j -= n;
                }

                sum += source[j];
            }

            dest[i] = sum / span;
        }
    }

    private static double HullRadiusAt(ReadOnlySpan<double> xs, ReadOnlySpan<double> ys, int count, double azimuthDegrees)
    {
        PolarToXy(azimuthDegrees, 1, 0, 0, 1, out var dx, out var dy);
        var best = 0d;
        for (var i = 0; i < count; i++)
        {
            var j = (i + 1) % count;
            if (TryRaySegment(dx, dy, xs[i], ys[i], xs[j], ys[j], out var t) && t > best)
            {
                best = t;
            }
        }

        return best;
    }

    private static bool TryRaySegment(
        double dx,
        double dy,
        double ax,
        double ay,
        double bx,
        double by,
        out double t)
    {
        var sx = bx - ax;
        var sy = by - ay;
        var rxs = (dx * sy) - (dy * sx);
        t = 0;
        if (Math.Abs(rxs) < 1e-12)
        {
            return false;
        }

        var qxs = (ax * sy) - (ay * sx);
        var qxr = (ax * dy) - (ay * dx);
        t = qxs / rxs;
        var u = qxr / rxs;
        return t > 1e-6 && u >= 0 && u <= 1;
    }

    /// <summary>1 本の方位葉。サラウンド表示のチャンネル色用。</summary>
    public static void FillLobe(Span<float> envelope, double azimuthDegrees, float level) =>
        FillLobe(envelope, azimuthDegrees, level, default, 0, 0);

    public static void FillLobe(
        Span<float> envelope,
        double azimuthDegrees,
        float level,
        ReadOnlySpan<float> samples)
        => FillLobe(envelope, azimuthDegrees, level, samples, 1, 0);

    public static void FillLobe(
        Span<float> envelope,
        double azimuthDegrees,
        float level,
        ReadOnlySpan<float> planar,
        int stride,
        int channel)
    {
        envelope.Clear();
        AddLobe(envelope, azimuthDegrees, Math.Clamp(level, 0, 1), planar, stride, channel);
        ClampEnvelope(envelope);
    }

    private static void ClampEnvelope(Span<float> envelope)
    {
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

    private static void AddLobe(
        Span<float> envelope,
        double azimuthDegrees,
        float level,
        ReadOnlySpan<float> planar,
        int stride,
        int channel)
    {
        BlendLobe(envelope, azimuthDegrees, level, planar, stride, channel, max: false);
    }

    private static void MaxLobe(
        Span<float> envelope,
        double azimuthDegrees,
        float level,
        ReadOnlySpan<float> planar,
        int stride,
        int channel)
    {
        BlendLobe(envelope, azimuthDegrees, level, planar, stride, channel, max: true);
    }

    private static void BlendLobe(
        Span<float> envelope,
        double azimuthDegrees,
        float level,
        ReadOnlySpan<float> planar,
        int stride,
        int channel,
        bool max)
    {
        var steps = envelope.Length;
        if (steps <= 0)
        {
            return;
        }

        ResolveWaveWindow(planar, stride, channel, out var start, out var take, out var peak);
        for (var i = 0; i < steps; i++)
        {
            var delta = WrapDelta(StepAzimuth(i, steps) - azimuthDegrees);
            var weight = Math.Max(0, Math.Cos(delta * Math.PI / 180d));
            var value = level * (float)(weight * weight) * WaveTexture(planar, stride, channel, start, take, peak, delta);
            if (max)
            {
                if (value > envelope[i])
                {
                    envelope[i] = value;
                }
            }
            else
            {
                envelope[i] += value;
            }
        }
    }

    private static float WaveTexture(
        ReadOnlySpan<float> planar,
        int stride,
        int channel,
        int start,
        int take,
        float peak,
        double deltaDegrees)
    {
        if (take <= 0 || peak <= 1e-6f)
        {
            return 1f;
        }

        var age = Math.Clamp(Math.Abs(deltaDegrees) / WaveSpreadDeg, 0, 1);
        var mag = AgePeak(planar, stride, channel, start, take, age);
        return WaveFloor + (WaveSpan * (mag / peak));
    }

    private static void ResolveWaveWindow(
        ReadOnlySpan<float> planar,
        int stride,
        int channel,
        out int start,
        out int take,
        out float peak)
    {
        start = 0;
        take = 0;
        peak = 0;
        var step = Math.Max(1, stride);
        if (planar.IsEmpty || channel < 0 || channel >= step)
        {
            return;
        }

        var frames = planar.Length / step;
        if (frames <= 0)
        {
            return;
        }

        take = Math.Min(WaveformFrames, frames);
        start = frames - take;
        for (var i = 0; i < take; i++)
        {
            var a = Math.Abs(planar[(start + i) * step + channel]);
            if (a > peak)
            {
                peak = a;
            }
        }
    }

    private static float AgePeak(
        ReadOnlySpan<float> planar,
        int stride,
        int channel,
        int start,
        int take,
        double age01)
    {
        if (take <= 0)
        {
            return 0;
        }

        var newest = start + take - 1;
        var center = newest - (Math.Clamp(age01, 0, 1) * (take - 1));
        var width = Math.Max(1, take / 36);
        var lo = (int)Math.Floor(center - (width * 0.5));
        var hi = lo + width;
        lo = Math.Clamp(lo, start, newest);
        hi = Math.Clamp(hi, lo + 1, newest + 1);
        var peak = 0f;
        for (var i = lo; i < hi; i++)
        {
            var a = Math.Abs(planar[(i * stride) + channel]);
            if (a > peak)
            {
                peak = a;
            }
        }

        return peak;
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

    private static int ConvexHull(Span<double> xs, Span<double> ys, int count)
    {
        SortPoints(xs, ys, count);
        Span<double> hx = stackalloc double[count * 2];
        Span<double> hy = stackalloc double[count * 2];
        var m = 0;
        for (var i = 0; i < count; i++)
        {
            while (m >= 2 && Cross(hx[m - 2], hy[m - 2], hx[m - 1], hy[m - 1], xs[i], ys[i]) <= 0)
            {
                m--;
            }

            hx[m] = xs[i];
            hy[m] = ys[i];
            m++;
        }

        var lower = m;
        for (var i = count - 2; i >= 0; i--)
        {
            while (m > lower && Cross(hx[m - 2], hy[m - 2], hx[m - 1], hy[m - 1], xs[i], ys[i]) <= 0)
            {
                m--;
            }

            hx[m] = xs[i];
            hy[m] = ys[i];
            m++;
        }

        m = Math.Max(0, m - 1);
        hx[..m].CopyTo(xs);
        hy[..m].CopyTo(ys);
        return m;
    }

    private static void SortPoints(Span<double> xs, Span<double> ys, int count)
    {
        for (var i = 1; i < count; i++)
        {
            var x = xs[i];
            var y = ys[i];
            var j = i - 1;
            while (j >= 0 && (xs[j] > x || (xs[j] == x && ys[j] > y)))
            {
                xs[j + 1] = xs[j];
                ys[j + 1] = ys[j];
                j--;
            }

            xs[j + 1] = x;
            ys[j + 1] = y;
        }
    }

    private static double Cross(double ax, double ay, double bx, double by, double cx, double cy) =>
        ((bx - ax) * (cy - ay)) - ((by - ay) * (cx - ax));

    private static void ClampUnitDisk(Span<double> xs, Span<double> ys, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var r = Math.Sqrt((xs[i] * xs[i]) + (ys[i] * ys[i]));
            if (r > 1)
            {
                xs[i] /= r;
                ys[i] /= r;
            }
        }
    }
}
