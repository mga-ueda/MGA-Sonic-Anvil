using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LoudnessPenGainTests
{
    [Fact]
    public void MissingBaselineOrPoints_DoesNotThrow()
    {
        var points = new[]
        {
            new LoudnessPenGain.Point(0, 0.5),
            new LoudnessPenGain.Point(10, 0.5),
        };

        Assert.False(LoudnessPenGain.TryRender(null, 1, 0, points, -14, 4, out _));
        Assert.False(LoudnessPenGain.TryRender(new float[8], 1, 0, null, -14, 4, out _));
        Assert.False(LoudnessPenGain.TryRender(Array.Empty<float>(), 1, 0, points, -14, 4, out _));
    }

    [Fact]
    public void ReversingTime_ErasesTheLaterStroke()
    {
        var points = new List<LoudnessPenGain.Point>
        {
            new(0, 0.2),
            new(100, 0.2),
            new(200, 0.8),
        };

        Assert.True(LoudnessPenGain.Place(points, new LoudnessPenGain.Point(50, 0.4)));
        Assert.Equal(new[] { 0L, 50L }, points.Select(point => point.Frame).ToArray());
        Assert.Equal(0.4, points[^1].YNorm);
        Assert.True(LoudnessPenGain.Place(points, new LoudnessPenGain.Point(180, 0.3)));
        Assert.Equal(new[] { 0L, 50L, 180L }, points.Select(point => point.Frame).ToArray());

        var backward = new List<LoudnessPenGain.Point> { new(300, 0.5), new(200, 0.5) };
        Assert.True(LoudnessPenGain.Place(backward, new LoudnessPenGain.Point(240, 0.1)));
        Assert.Equal(new[] { 300L, 240L }, backward.Select(point => point.Frame).ToArray());
    }

    [Fact]
    public void TopOfScale_IsZeroDb()
    {
        Assert.Equal(0, LoudnessPenGain.DbFromYNorm(0, -14), 3);
        Assert.Equal(-35, LoudnessPenGain.DbFromYNorm(1, -14), 3);
    }

    [Fact]
    public void StrokeEdges_MatchTheSurroundingSamples()
    {
        const int rate = 48000;
        const float amp = 0.5f;
        const int fade = 480;
        var baseline = Sine(rate, 2, amp);
        var level = LoudnessMeterEngine.BuildShortTermProfile(baseline, 1, rate);
        var y = YForLufs(level.AtFrame(baseline.Length / 2) - 6f);
        var points = new[]
        {
            new LoudnessPenGain.Point(rate / 2, y),
            new LoudnessPenGain.Point(baseline.Length - rate / 2, y),
        };

        Assert.True(LoudnessPenGain.TryRender(baseline, 1, 0, points, -14, fade, out var rendered, rate));
        var start = (int)rendered.StartFrame;
        Assert.Equal(baseline[start] - baseline[start - 1], rendered.Samples[0] - baseline[start - 1], 4);
        var end = start + rendered.Samples.Length - 1;
        Assert.Equal(baseline[end] - baseline[end + 1], rendered.Samples[^1] - baseline[end + 1], 4);
        var earlyAt = PeakIndex(baseline, start, start + 24);
        var laterAt = PeakIndex(baseline, start + fade / 2 - 24, start + fade / 2 + 24);
        var early = Math.Abs(rendered.Samples[earlyAt - start] / baseline[earlyAt] - 1f);
        var later = Math.Abs(rendered.Samples[laterAt - start] / baseline[laterAt] - 1f);
        Assert.True(later > 0.05f);
        Assert.True(early < later * 0.2);
    }

    [Fact]
    public void ShallowDip_FollowsTheCurve_NotThePeakScale()
    {
        const int rate = 48000;
        const float amp = 0.5f;
        var baseline = Sine(rate, 4, amp);
        var level = LoudnessMeterEngine.BuildShortTermProfile(baseline, 1, rate);
        var y = YForLufs(level.AtFrame(baseline.Length / 2) - 1f);
        var points = new[]
        {
            new LoudnessPenGain.Point(rate, y),
            new LoudnessPenGain.Point(baseline.Length - rate, y),
        };

        Assert.True(LoudnessPenGain.TryRender(baseline, 1, 0, points, -14, 40, out var rendered, rate));
        var peakAt = PeakIndex(baseline, baseline.Length / 2 - 100, baseline.Length / 2 + 100);
        var peak = rendered.Samples[peakAt - (int)rendered.StartFrame];
        Assert.InRange(peak, amp * 0.85f, amp * 0.94f);
        var head = (int)rendered.StartFrame;
        Assert.Equal(baseline[head], rendered.Samples[0], 3);
        Assert.Equal(baseline[head + rendered.Samples.Length - 1], rendered.Samples[^1], 3);
    }

    [Fact]
    public void Sine_KeepsItsShape_WhenTheCurveMoves()
    {
        const int rate = 48000;
        const float amp = 0.2f;
        var baseline = Sine(rate, 4, amp);
        var level = LoudnessMeterEngine.BuildShortTermProfile(baseline, 1, rate);
        var y = YForLufs(level.AtFrame(baseline.Length / 2) - 6f);
        var points = new[]
        {
            new LoudnessPenGain.Point(rate, y),
            new LoudnessPenGain.Point(baseline.Length - rate, y),
        };

        Assert.True(LoudnessPenGain.TryRender(baseline, 1, 0, points, -14, 40, out var rendered, rate));
        var peakAt = PeakIndex(baseline, baseline.Length / 2 - 200, baseline.Length / 2 + 200);
        var local = peakAt - (int)rendered.StartFrame;
        var peak = rendered.Samples[local];
        Assert.InRange(peak, amp * 0.48f, amp * 0.55f);
        var quarter = rendered.Samples[local - rate / 8000];
        Assert.InRange(quarter / peak, 0.65f, 0.78f);
    }

    [Fact]
    public void QuietEdge_StaysOnItsOwnCurve()
    {
        const int rate = 48000;
        var loud = Sine(rate, 2, 0.4f);
        var quiet = Sine(rate, 4, 0.02f, 880f);
        var baseline = new float[loud.Length + quiet.Length];
        loud.CopyTo(baseline, 0);
        quiet.CopyTo(baseline, loud.Length);
        var level = LoudnessMeterEngine.BuildShortTermProfile(baseline, 1, rate);
        var tail = baseline.Length - rate / 2;
        var y = YForLufs(level.AtFrame(tail));
        var points = new[]
        {
            new LoudnessPenGain.Point(baseline.Length - rate, y),
            new LoudnessPenGain.Point(baseline.Length - 1, y),
        };

        Assert.True(LoudnessPenGain.TryRender(baseline, 1, 0, points, -14, 48, out var rendered, rate));
        var at = PeakIndex(baseline, tail - 100, tail + 100) - (int)rendered.StartFrame;
        Assert.InRange(Math.Abs(rendered.Samples[at]), 0.015f, 0.028f);
        var endPeak = 0f;
        for (var i = rendered.Samples.Length - 80; i < rendered.Samples.Length; i++)
        {
            endPeak = Math.Max(endPeak, Math.Abs(rendered.Samples[i]));
        }

        Assert.InRange(endPeak, 0.015f, 0.028f);
    }

    [Fact]
    public void OutOfPhaseStereo_DoesNotExplode()
    {
        const int rate = 48000;
        var frames = rate * 4;
        var baseline = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var s = 0.5f * MathF.Sin(2f * MathF.PI * 440f * i / rate);
            baseline[i * 2] = s;
            baseline[i * 2 + 1] = -s;
        }

        var level = LoudnessMeterEngine.BuildShortTermProfile(baseline, 2, rate);
        var y = YForLufs(level.AtFrame(frames / 2));
        var points = new[]
        {
            new LoudnessPenGain.Point(rate, y),
            new LoudnessPenGain.Point(frames - rate, y),
        };

        Assert.True(LoudnessPenGain.TryRender(baseline, 2, 0, points, -14, 20, out var rendered, rate));
        var mid = 0f;
        var from = rendered.Samples.Length / 3;
        var to = rendered.Samples.Length * 2 / 3;
        for (var i = from; i < to; i++)
        {
            mid = Math.Max(mid, Math.Abs(rendered.Samples[i]));
        }

        Assert.InRange(mid, 0.45f, 0.55f);
        Assert.False(LoudnessPenGain.WouldClip(mid));
    }

    private static int PeakIndex(float[] samples, int from, int to)
    {
        var best = from;
        for (var i = from; i <= to; i++)
        {
            if (samples[i] > samples[best])
            {
                best = i;
            }
        }

        return best;
    }

    private static double YForLufs(double lufs) => -lufs / 35d;

    private static float[] Sine(int rate, double seconds, float amp, float hz = 1000f)
    {
        var frames = (int)Math.Round(rate * seconds);
        var samples = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            samples[i] = amp * MathF.Sin(2f * MathF.PI * hz * i / rate);
        }

        return samples;
    }

    [Fact]
    public void Undo_RestoresTheStroke()
    {
        const int rate = 48000;
        var samples = Sine(rate, 1, 0.5f);
        var document = new AudioDocument(samples, rate, 1, 24, AudioFileKind.Wave, null);
        var baseline = (float[])document.Interleaved.Clone();
        var level = LoudnessMeterEngine.BuildShortTermProfile(baseline, 1, rate);
        var y = YForLufs(level.AtFrame(samples.Length / 2) - 6f);
        var points = new[]
        {
            new LoudnessPenGain.Point(rate / 10, y),
            new LoudnessPenGain.Point(samples.Length - rate / 10, y),
        };
        Assert.True(LoudnessPenGain.TryRender(baseline, 1, 0, points, -14, 10, out var rendered, rate));
        document.ReplaceRange(rendered.StartFrame, rendered.Samples);
        var before = new float[rendered.Samples.Length];
        Array.Copy(baseline, (int)rendered.StartFrame, before, 0, before.Length);
        var command = new ReplaceRangeCommand(
            "Pen",
            rendered.StartFrame,
            before,
            rendered.Samples,
            WaveSelection.Empty,
            WaveSelection.Empty,
            0,
            0);
        var history = new EditHistory();
        history.AcceptApplied(document, command);

        var center = PeakIndex(baseline, samples.Length / 2 - 100, samples.Length / 2 + 100);
        Assert.NotEqual(baseline[center], document.Interleaved[center]);
        Assert.True(history.Undo(document));
        Assert.Equal(baseline[center], document.Interleaved[center], 3);
        Assert.True(history.Redo(document));
        Assert.NotEqual(baseline[center], document.Interleaved[center]);
    }
}
