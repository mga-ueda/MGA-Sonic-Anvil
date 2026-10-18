using MgaSonicAnvil.Audio;
using MgaSonicAnvil.UI;

namespace MgaSonicAnvil.Editing;

/// <summary>ラウドネス曲線上のペン一筆。縦位置は表示中の曲線と同じ LUFS で、その差だけゲインを変える。</summary>
internal static class LoudnessPenGain
{
    public readonly record struct Point(long Frame, double YNorm);

    public readonly record struct Rendered(
        long StartFrame,
        float[] Samples,
        float[] Gains,
        float Peak);

    /// <summary>
    /// 一筆は時刻の関数にする。進む方向へ足し、時刻が戻ったら先端を消し戻す。
    /// 同じ時刻に音量を二つ置かない。
    /// </summary>
    public static bool Place(List<Point> points, Point next)
    {
        if (points.Count == 0)
        {
            points.Add(next);
            return true;
        }

        if (points.Count == 1 || Continues(points, next))
        {
            var last = points[^1];
            if (next.Frame == last.Frame)
            {
                if (next.YNorm == last.YNorm)
                {
                    return false;
                }

                points[^1] = next;
                return true;
            }

            points.Add(next);
            return true;
        }

        var changed = false;
        while (points.Count >= 2 && IsBehindTip(points, next.Frame))
        {
            points.RemoveAt(points.Count - 1);
            changed = true;
        }

        var tip = points[^1];
        if (next.Frame == tip.Frame)
        {
            if (!changed && next.YNorm == tip.YNorm)
            {
                return false;
            }

            points[^1] = next;
            return true;
        }

        points.Add(next);
        return true;
    }

    public static bool MovesBackward(IReadOnlyList<Point> points, Point next)
    {
        if (points.Count < 2)
        {
            return false;
        }

        var dir = Direction(points);
        if (dir == 0)
        {
            return false;
        }

        return dir > 0
            ? next.Frame < points[^1].Frame
            : next.Frame > points[^1].Frame;
    }

    private static bool Continues(List<Point> points, Point next)
    {
        var dir = Direction(points);
        if (dir == 0 || next.Frame == points[^1].Frame)
        {
            return true;
        }

        return dir > 0 ? next.Frame > points[^1].Frame : next.Frame < points[^1].Frame;
    }

    private static bool IsBehindTip(List<Point> points, long frame)
    {
        var tip = points[^1].Frame;
        var prev = points[^2].Frame;
        if (tip > prev)
        {
            return frame < tip;
        }

        if (tip < prev)
        {
            return frame > tip;
        }

        return false;
    }

    private static int Direction(IReadOnlyList<Point> points)
    {
        for (var i = 1; i < points.Count; i++)
        {
            var delta = points[i].Frame.CompareTo(points[i - 1].Frame);
            if (delta != 0)
            {
                return delta;
            }
        }

        return 0;
    }

    public static double DbFromYNorm(double yNorm, double targetLufs)
    {
        var top = LoudnessOverlayRenderer.TopLufs;
        var bottom = LoudnessMeterEngine.ClampTargetLufs(targetLufs) - LoudnessOverlayRenderer.FloorLu;
        if (bottom >= top)
        {
            bottom = top - LoudnessOverlayRenderer.FloorLu;
        }

        return top - Math.Clamp(yNorm, 0, 1) * (top - bottom);
    }

    public static bool TryRender(
        float[]? baseline,
        int channels,
        int channelMask,
        IReadOnlyList<Point>? points,
        double targetLufs,
        int fadeFrames,
        out Rendered rendered,
        int sampleRate = 48000)
    {
        rendered = default;
        channels = Math.Max(1, channels);
        if (baseline is null || points is null || baseline.Length < channels || points.Count < 2)
        {
            return false;
        }

        var frameCount = baseline.Length / channels;
        if (frameCount < 1)
        {
            return false;
        }
        long min = long.MaxValue;
        long max = long.MinValue;
        foreach (var point in points)
        {
            var frame = Math.Clamp(point.Frame, 0, frameCount - 1);
            min = Math.Min(min, frame);
            max = Math.Max(max, frame);
        }

        if (max <= min)
        {
            return false;
        }

        var length = (int)(max - min + 1);
        var gains = new float[length];
        Array.Fill(gains, 1f);
        // ペンの高さはピーク振幅ではなく、画面の Short Term 曲線と同じ LUFS。
        // ピークに合わせると、曲線の少し下でも十数 dB 下がり、端の小さい音は逆に跳ね上がる。
        var level = LevelOf(baseline, channels, sampleRate);
        long previousFrame = points[0].Frame;
        var previousY = points[0].YNorm;
        for (var i = 1; i < points.Count; i++)
        {
            PaintSegment(level, targetLufs, gains, min, previousFrame, previousY, points[i]);
            previousFrame = points[i].Frame;
            previousY = points[i].YNorm;
        }

        // ファイルの外には繋ぐ音がない。端まで描いた側を 1 倍へ戻すと、画面端で線が跳ね上がる。
        ApplyEdgeFade(gains, fadeFrames, fadeHead: min > 0, fadeTail: max < frameCount - 1);
        var samples = new float[length * channels];
        var start = (int)min * channels;
        Array.Copy(baseline, start, samples, 0, samples.Length);
        var peak = 0f;
        for (var frame = 0; frame < length; frame++)
        {
            var gain = gains[frame];
            var index = frame * channels;
            for (var ch = 0; ch < channels; ch++)
            {
                if (!ChannelSolo.Contains(channelMask, ch))
                {
                    continue;
                }

                var sample = samples[index + ch] * gain;
                samples[index + ch] = sample;
                peak = Math.Max(peak, Math.Abs(sample));
            }
        }

        rendered = new Rendered(min, samples, gains, peak);
        return true;
    }

    public static float HeadroomLinear => (float)Math.Pow(10d, -0.1d / 20d);

    public static bool WouldClip(float peak) => peak > 1f;

    public static void LowerToHeadroom(Rendered source, int channels, int channelMask, out Rendered lowered)
    {
        channels = Math.Max(1, channels);
        var factor = source.Peak <= 1e-8f ? 1f : HeadroomLinear / source.Peak;
        var samples = (float[])source.Samples.Clone();
        var gains = (float[])source.Gains.Clone();
        for (var i = 0; i < samples.Length; i++)
        {
            if (ChannelSolo.Contains(channelMask, i % channels))
            {
                samples[i] *= factor;
            }
        }

        for (var i = 0; i < gains.Length; i++)
        {
            gains[i] *= factor;
        }

        lowered = new Rendered(source.StartFrame, samples, gains, source.Peak * factor);
    }

    private static float[]? _levelSource;
    private static int _levelChannels;
    private static int _levelRate;
    private static LoudnessProfile? _level;

    private static LoudnessProfile LevelOf(float[] baseline, int channels, int sampleRate)
    {
        sampleRate = Math.Max(1000, sampleRate);
        if (!ReferenceEquals(_levelSource, baseline)
            || _level is null
            || _levelChannels != channels
            || _levelRate != sampleRate)
        {
            _levelSource = baseline;
            _levelChannels = channels;
            _levelRate = sampleRate;
            _level = LoudnessMeterEngine.BuildShortTermProfile(baseline, channels, sampleRate);
        }

        return _level;
    }

    private static void PaintSegment(
        LoudnessProfile level,
        double targetLufs,
        float[] gains,
        long origin,
        long fromFrame,
        double fromY,
        Point to)
    {
        var start = Math.Clamp(fromFrame, origin, origin + gains.Length - 1);
        var end = Math.Clamp(to.Frame, origin, origin + gains.Length - 1);
        var span = Math.Abs(end - start);
        var step = end >= start ? 1 : -1;
        for (var i = 0; i <= span; i++)
        {
            var frame = start + step * i;
            var t = span == 0 ? 1d : i / (double)span;
            var y = fromY + (to.YNorm - fromY) * t;
            var index = (int)(frame - origin);
            if ((uint)index >= (uint)gains.Length)
            {
                continue;
            }

            gains[index] = GainFor(level, frame, y, targetLufs);
        }
    }

    private static float GainFor(LoudnessProfile level, long frame, double yNorm, double targetLufs)
    {
        var current = level.AtFrame(frame);
        if (!float.IsFinite(current))
        {
            return 1f;
        }

        var desired = DbFromYNorm(yNorm, targetLufs);
        return (float)Math.Pow(10d, (desired - current) / 20d);
    }

    private static void ApplyEdgeFade(float[] gains, int fadeFrames, bool fadeHead, bool fadeTail)
    {
        if (fadeFrames <= 0 || gains.Length < 2 || (!fadeHead && !fadeTail))
        {
            return;
        }

        var edge = Math.Min(fadeFrames, gains.Length / 2);
        if (edge <= 0)
        {
            return;
        }

        for (var i = 0; i < gains.Length; i++)
        {
            var weight = 1d;
            if (fadeHead && i < edge)
            {
                weight = FadeCurves.Apply01(FadeShape.SCurve, i / (double)edge);
            }

            var fromEnd = gains.Length - 1 - i;
            if (fadeTail && fromEnd < edge)
            {
                weight = Math.Min(weight, FadeCurves.Apply01(FadeShape.SCurve, fromEnd / (double)edge));
            }

            gains[i] = (float)(1d + (gains[i] - 1d) * weight);
        }
    }
}
