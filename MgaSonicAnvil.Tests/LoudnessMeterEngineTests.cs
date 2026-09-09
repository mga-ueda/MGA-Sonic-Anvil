using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LoudnessMeterEngineTests
{
    [Fact]
    public void Silence_IsNegativeInfinity()
    {
        var engine = new LoudnessMeterEngine();
        var left = new float[4800];
        var right = new float[4800];
        var snap = engine.Process(left, right, left.Length, 48000);
        Assert.True(float.IsNegativeInfinity(snap.ShortTermLufs));
        Assert.True(float.IsNegativeInfinity(snap.IntegratedLufs));
    }

    [Fact]
    public void StereoSineAtMinusTwenty_IsNearMinusTwentyPointSeven()
    {
        const int rate = 48000;
        var frames = rate * 4;
        var left = new float[frames];
        var right = new float[frames];
        var amp = (float)Math.Pow(10, -20d / 20d);
        for (var i = 0; i < frames; i++)
        {
            var s = amp * (float)Math.Sin(2 * Math.PI * 1000 * i / rate);
            left[i] = s;
            right[i] = s;
        }

        var engine = new LoudnessMeterEngine();
        var snap = engine.Process(left, right, frames, rate);
        Assert.InRange(snap.ShortTermLufs, -22.5f, -19.5f);
        Assert.InRange(snap.IntegratedLufs, -22.5f, -19.5f);
        Assert.True(snap.TruePeakDb < -19f);

        var interleaved = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            interleaved[i * 2] = left[i];
            interleaved[i * 2 + 1] = right[i];
        }

        var profile = LoudnessMeterEngine.BuildShortTermProfile(interleaved, 2, rate);
        Assert.True(profile.Values.Length >= 10);
        Assert.InRange(profile.AtFrame(frames - 1), -22.5f, -19.5f);
    }

    [Fact]
    public void Traffic_Lufs_SafeOnOrBelowTargetMinusOne()
    {
        Assert.Equal(LoudnessTraffic.Safe, LoudnessTrafficLight.ForLufs(-26f, -24));
        Assert.Equal(LoudnessTraffic.Safe, LoudnessTrafficLight.ForLufs(-25f, -24));
        Assert.Equal(LoudnessTraffic.Safe, LoudnessTrafficLight.ForLufs(-24f, -24));
        Assert.Equal(LoudnessTraffic.Caution, LoudnessTrafficLight.ForLufs(-24.5f, -24));
        Assert.Equal(LoudnessTraffic.Danger, LoudnessTrafficLight.ForLufs(-23.9f, -24));
        Assert.Equal(LoudnessTraffic.Idle, LoudnessTrafficLight.ForLufs(float.NegativeInfinity, -24));
    }

    [Fact]
    public void Traffic_TruePeak_DangerAtOrOverZero()
    {
        Assert.Equal(LoudnessTraffic.Safe, LoudnessTrafficLight.ForTruePeak(-2f));
        Assert.Equal(LoudnessTraffic.Caution, LoudnessTrafficLight.ForTruePeak(-0.5f));
        Assert.Equal(LoudnessTraffic.Danger, LoudnessTrafficLight.ForTruePeak(0f));
        Assert.Equal(LoudnessTraffic.Idle, LoudnessTrafficLight.ForTruePeak(float.NegativeInfinity));
    }

    [Fact]
    public void ParseTargetLufs_AcceptsRange()
    {
        Assert.True(LoudnessMeterEngine.TryParseTargetLufs("-24", out var v));
        Assert.Equal(-24, v);
        Assert.True(LoudnessMeterEngine.TryParseTargetLufs("-16.0", out v));
        Assert.Equal(-16, v);
        Assert.True(LoudnessMeterEngine.TryParseTargetLufs("0", out v));
        Assert.Equal(0, v);
        Assert.False(LoudnessMeterEngine.TryParseTargetLufs("", out _));
        Assert.False(LoudnessMeterEngine.TryParseTargetLufs("x", out _));
        Assert.False(LoudnessMeterEngine.TryParseTargetLufs("1", out _));
        Assert.False(LoudnessMeterEngine.TryParseTargetLufs("-80", out _));
    }

    [Fact]
    public void Traffic_Lra_WarnsWhenWide()
    {
        Assert.Equal(LoudnessTraffic.Safe, LoudnessTrafficLight.ForLra(12f));
        Assert.Equal(LoudnessTraffic.Caution, LoudnessTrafficLight.ForLra(22f));
        Assert.Equal(LoudnessTraffic.Danger, LoudnessTrafficLight.ForLra(26f));
        Assert.Equal(LoudnessTraffic.Idle, LoudnessTrafficLight.ForLra(float.NaN));
    }
}
