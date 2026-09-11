using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class SurroundScopeEngineTests
{
    [Fact]
    public void IsSurround_StartsAtThreeChannels()
    {
        Assert.False(SurroundScopeEngine.IsSurround(2));
        Assert.True(SurroundScopeEngine.IsSurround(6));
    }

    [Theory]
    [InlineData("C", 0)]
    [InlineData("L", -30)]
    [InlineData("R", 30)]
    [InlineData("Ls", -110)]
    [InlineData("Rs", 110)]
    public void TryAzimuthDegrees_UsesIuFrontZero(string label, double expected)
    {
        Assert.True(SurroundScopeEngine.TryAzimuthDegrees(label, out var degrees));
        Assert.Equal(expected, degrees, 6);
    }

    [Fact]
    public void TryAzimuthDegrees_RejectsLfe()
    {
        Assert.False(SurroundScopeEngine.TryAzimuthDegrees("LFE", out _));
        Assert.True(SurroundScopeEngine.IsLfeLabel("LFE"));
    }

    [Theory]
    [InlineData(6, 0, 0)]
    [InlineData(6, 1, 60)]
    [InlineData(6, 5, 300)]
    [InlineData(3, 1, 120)]
    public void AzimuthDegrees_SpacesChannelsEvenly(int channels, int index, double expected)
    {
        Assert.Equal(expected, SurroundScopeEngine.AzimuthDegrees(index, channels), 6);
    }

    [Fact]
    public void FiveOneLayout_PlacesCenterInFrontAndSkipsLfe()
    {
        var layout = ChannelLayout.Parse("5.1");
        var levels = new float[layout.Channels];
        levels[2] = 1f;
        levels[3] = 0.9f;
        var envelope = new float[SurroundScopeEngine.EnvelopeSteps];
        SurroundScopeEngine.FillEnvelope(levels, layout, envelope);
        var peak = 0;
        for (var i = 1; i < envelope.Length; i++)
        {
            if (envelope[i] > envelope[peak])
            {
                peak = i;
            }
        }

        Assert.True(envelope[peak] > 0.5f);
        Assert.Equal(0, SurroundScopeEngine.StepAzimuth(peak, envelope.Length), 6);
        Assert.Equal(0.9f, SurroundScopeEngine.LfeLevel(levels, layout));
    }

    [Fact]
    public void NumberedLayout_PlacesEnergyOnFileOrderCircle()
    {
        var layout = ChannelLayout.FromChannels(6);
        var levels = new float[layout.Channels];
        levels[2] = 1f;
        var envelope = new float[SurroundScopeEngine.EnvelopeSteps];
        SurroundScopeEngine.FillEnvelope(levels, layout, envelope);
        var peak = 0;
        for (var i = 1; i < envelope.Length; i++)
        {
            if (envelope[i] > envelope[peak])
            {
                peak = i;
            }
        }

        Assert.True(envelope[peak] > 0.5f);
        Assert.Equal(120, SurroundScopeEngine.StepAzimuth(peak, envelope.Length), 6);
        Assert.Equal(0f, SurroundScopeEngine.LfeLevel(levels, layout));
    }

    [Fact]
    public void RadiusFromLinear_BlendsLinearAndMeterDb()
    {
        Assert.Equal(0, SurroundScopeEngine.RadiusFromLinear(0), 4);
        Assert.Equal(1, SurroundScopeEngine.RadiusFromLinear(1), 4);
        var linear = 0.1f;
        var db = (float)LevelMeterEngine.DbToNorm(LevelMeterEngine.ToDb(linear));
        var mid = linear + (db - linear) * 0.5f;
        Assert.Equal(mid, SurroundScopeEngine.RadiusFromLinear(linear), 4);
        Assert.True(SurroundScopeEngine.RadiusFromLinear(linear) > linear);
        Assert.True(SurroundScopeEngine.RadiusFromLinear(linear) < db);
    }

    [Fact]
    public void FillUnion_TakesPointwiseMaxNotSum()
    {
        var layout = ChannelLayout.Parse("5.1");
        var levels = new float[layout.Channels];
        levels[0] = 1f;
        levels[1] = 1f;
        var union = new float[SurroundScopeEngine.EnvelopeSteps];
        var added = new float[SurroundScopeEngine.EnvelopeSteps];
        var left = new float[SurroundScopeEngine.EnvelopeSteps];
        var right = new float[SurroundScopeEngine.EnvelopeSteps];
        SurroundScopeEngine.FillUnion(levels, layout, union);
        SurroundScopeEngine.FillEnvelope(levels, layout, added);
        SurroundScopeEngine.FillLobe(left, -30, 1f);
        SurroundScopeEngine.FillLobe(right, 30, 1f);

        var front = 0;
        Assert.Equal(0, SurroundScopeEngine.StepAzimuth(front, union.Length), 6);
        Assert.Equal(Math.Max(left[front], right[front]), union[front], 4);
        Assert.True(added[front] > union[front] + 0.1f);
        Assert.True(union[front] > 0.5f);
    }

    [Fact]
    public void FillUnion_SkipsLfe()
    {
        var layout = ChannelLayout.Parse("5.1");
        var levels = new float[layout.Channels];
        levels[3] = 1f;
        var union = new float[SurroundScopeEngine.EnvelopeSteps];
        SurroundScopeEngine.FillUnion(levels, layout, union);
        Assert.All(union, value => Assert.Equal(0f, value));
    }

    [Fact]
    public void CombinedWrapPoints_SkipsLfe()
    {
        var layout = ChannelLayout.Parse("5.1");
        var levels = new float[layout.Channels];
        levels[3] = 1f;
        var xs = new double[SurroundScopeEngine.WrapMaxPoints];
        var ys = new double[SurroundScopeEngine.WrapMaxPoints];
        Assert.Equal(0, SurroundScopeEngine.CombinedWrapPoints(levels, layout, default, 0, xs, ys));
    }

    [Fact]
    public void CombinedWrapPoints_WrapsFrontPairAwayFromOrigin()
    {
        var layout = ChannelLayout.Parse("5.1");
        var levels = new float[layout.Channels];
        levels[0] = 1f;
        levels[1] = 1f;
        var xs = new double[SurroundScopeEngine.WrapMaxPoints];
        var ys = new double[SurroundScopeEngine.WrapMaxPoints];
        var n = SurroundScopeEngine.CombinedWrapPoints(levels, layout, default, 0, xs, ys);
        Assert.True(n >= 3);

        var minR = 1d;
        var maxR = 0d;
        var hasLeft = false;
        var hasRight = false;
        for (var i = 0; i < n; i++)
        {
            var r = Math.Sqrt((xs[i] * xs[i]) + (ys[i] * ys[i]));
            minR = Math.Min(minR, r);
            maxR = Math.Max(maxR, r);
            hasLeft |= xs[i] < -0.05;
            hasRight |= xs[i] > 0.05;
        }

        Assert.True(minR > 0.12);
        Assert.True(maxR > 0.7);
        Assert.True(hasLeft);
        Assert.True(hasRight);
    }

    [Fact]
    public void FillCombinedWrap_SkipsLfe()
    {
        var layout = ChannelLayout.Parse("5.1");
        var levels = new float[layout.Channels];
        levels[3] = 1f;
        var wrap = new float[SurroundScopeEngine.EnvelopeSteps];
        SurroundScopeEngine.FillCombinedWrap(levels, layout, wrap);
        Assert.All(wrap, value => Assert.Equal(0f, value));
    }

    [Fact]
    public void FillCombinedWrap_StaysAwayFromOrigin()
    {
        var layout = ChannelLayout.Parse("5.1");
        var levels = new float[layout.Channels];
        levels[0] = 1f;
        levels[1] = 1f;
        var wrap = new float[SurroundScopeEngine.EnvelopeSteps];
        SurroundScopeEngine.FillCombinedWrap(levels, layout, wrap);
        var minR = 1f;
        var maxR = 0f;
        for (var i = 0; i < wrap.Length; i++)
        {
            if (wrap[i] < 0.04f)
            {
                continue;
            }

            minR = Math.Min(minR, wrap[i]);
            maxR = Math.Max(maxR, wrap[i]);
        }

        Assert.True(minR > 0.06f);
        Assert.True(maxR > 0.7f);
    }

    [Fact]
    public void FillCombinedWrap_KeepsWaveformDetail()
    {
        var layout = ChannelLayout.Parse("5.1");
        var levels = new float[layout.Channels];
        levels[0] = 1f;
        var smooth = new float[SurroundScopeEngine.EnvelopeSteps];
        var jagged = new float[SurroundScopeEngine.EnvelopeSteps];
        var samples = new float[SurroundScopeEngine.WaveformFrames];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)Math.Sin(i * 0.41);
        }

        SurroundScopeEngine.FillCombinedWrap(levels, layout, smooth);
        SurroundScopeEngine.FillCombinedWrap(levels, layout, jagged, samples, 1);
        var maxDev = 0f;
        for (var i = 0; i < smooth.Length; i++)
        {
            maxDev = Math.Max(maxDev, Math.Abs(jagged[i] - smooth[i]));
        }

        Assert.True(maxDev > 0.02f);
    }

    [Fact]
    public void HoldWrap_SnapsUpAndHolds()
    {
        var held = new float[4];
        var hold = new int[4];
        SurroundScopeEngine.HoldWrap(held, new float[] { 0.2f, 0.8f, 0, 0 }, hold);
        Assert.Equal(0.8f, held[1], 4);
        Assert.Equal(SurroundScopeEngine.WrapHoldFrames, hold[1]);

        for (var i = 0; i < SurroundScopeEngine.WrapHoldFrames; i++)
        {
            SurroundScopeEngine.HoldWrap(held, new float[] { 0, 0.1f, 0, 0 }, hold);
        }

        Assert.Equal(0.8f, held[1], 4);
    }

    [Fact]
    public void HoldWrap_FallsAfterHold()
    {
        var held = new float[] { 0, 0.8f, 0, 0 };
        var hold = new int[4];
        SurroundScopeEngine.HoldWrap(held, new float[] { 0, 0.1f, 0, 0 }, hold);
        Assert.True(held[1] < 0.8f);
        Assert.True(held[1] > 0.1f);
    }

    [Fact]
    public void FillLobe_PeaksAtAzimuth()
    {
        var envelope = new float[SurroundScopeEngine.EnvelopeSteps];
        SurroundScopeEngine.FillLobe(envelope, 120, 1f);
        var peak = 0;
        for (var i = 1; i < envelope.Length; i++)
        {
            if (envelope[i] > envelope[peak])
            {
                peak = i;
            }
        }

        Assert.True(envelope[peak] > 0.5f);
        Assert.Equal(120, SurroundScopeEngine.StepAzimuth(peak, envelope.Length), 6);
    }

    [Fact]
    public void FillLobe_WithoutSamplesStaysSmooth()
    {
        var envelope = new float[SurroundScopeEngine.EnvelopeSteps];
        SurroundScopeEngine.FillLobe(envelope, 0, 1f);
        for (var i = 0; i < envelope.Length; i++)
        {
            var delta = SurroundScopeEngine.StepAzimuth(i, envelope.Length);
            if (delta > 180)
            {
                delta -= 360;
            }

            var weight = Math.Max(0, Math.Cos(delta * Math.PI / 180d));
            Assert.Equal(weight * weight, envelope[i], 4);
        }
    }

    [Fact]
    public void FillLobe_NewestSamplesSitAtTheTip()
    {
        var samples = new float[SurroundScopeEngine.WaveformFrames];
        samples[^1] = 1f;
        var envelope = new float[SurroundScopeEngine.EnvelopeSteps];
        SurroundScopeEngine.FillLobe(envelope, 0, 1f, samples);
        var tip = TextureAt(envelope, 0, 0);
        var flank = TextureAt(envelope, 0, 50);
        Assert.True(tip > 0.95f);
        Assert.True(flank < tip - 0.15f);
    }

    [Fact]
    public void FillLobe_OldestSamplesSitOnTheFlanks()
    {
        var samples = new float[SurroundScopeEngine.WaveformFrames];
        samples[0] = 1f;
        var envelope = new float[SurroundScopeEngine.EnvelopeSteps];
        SurroundScopeEngine.FillLobe(envelope, 0, 1f, samples);
        var tip = TextureAt(envelope, 0, 0);
        var flank = TextureAt(envelope, 0, SurroundScopeEngine.WaveSpreadDeg);
        Assert.True(flank > tip + 0.15f);
    }

    [Fact]
    public void FillLobe_WaveformIsDeterministic()
    {
        var samples = new float[SurroundScopeEngine.WaveformFrames];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)Math.Sin(i * 0.37);
        }

        var first = new float[SurroundScopeEngine.EnvelopeSteps];
        var second = new float[SurroundScopeEngine.EnvelopeSteps];
        SurroundScopeEngine.FillLobe(first, -30, 0.8f, samples);
        SurroundScopeEngine.FillLobe(second, -30, 0.8f, samples);
        Assert.Equal(first, second);
    }

    [Fact]
    public void PolarToXy_FrontIsUp()
    {
        SurroundScopeEngine.PolarToXy(0, 1, 0, 0, 10, out var x, out var y);
        Assert.Equal(0, x, 6);
        Assert.Equal(-10, y, 6);
    }

    private static float TextureAt(float[] envelope, double azimuth, double delta)
    {
        var steps = envelope.Length;
        var az = azimuth + delta;
        while (az < 0)
        {
            az += 360;
        }

        var step = (int)Math.Round(az / (360d / steps)) % steps;
        var weight = Math.Max(0, Math.Cos(delta * Math.PI / 180d));
        var smooth = weight * weight;
        return smooth < 1e-4 ? 0 : envelope[step] / (float)smooth;
    }
}
