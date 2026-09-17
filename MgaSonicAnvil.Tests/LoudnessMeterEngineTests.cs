using System.Windows.Media;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
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

        var measured = LoudnessMeterEngine.MeasureFile(interleaved, 2, rate);
        Assert.InRange(measured.IntegratedLufs, -22.5f, -19.5f);
        Assert.InRange(measured.ShortTermLufs, -22.5f, -19.5f);
        Assert.InRange(measured.TruePeakDb, -22.5f, -19f);
    }

    [Fact]
    public void MeasureFile_ShortTermIsFileMaximum()
    {
        const int rate = 48000;
        var loudFrames = rate * 4;
        var quietFrames = rate * 4;
        var frames = loudFrames + quietFrames;
        var interleaved = new float[frames * 2];
        var amp = (float)Math.Pow(10, -20d / 20d);
        for (var i = 0; i < loudFrames; i++)
        {
            var s = amp * (float)Math.Sin(2 * Math.PI * 1000 * i / rate);
            interleaved[i * 2] = s;
            interleaved[i * 2 + 1] = s;
        }

        var engine = new LoudnessMeterEngine();
        var left = new float[frames];
        var right = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            left[i] = interleaved[i * 2];
            right[i] = interleaved[i * 2 + 1];
        }

        var live = engine.Process(left, right, frames, rate);
        Assert.True(live.ShortTermLufs < -40f);
        Assert.InRange(engine.MaxShortTermLufs, -22.5f, -19.5f);

        var measured = LoudnessMeterEngine.MeasureFile(interleaved, 2, rate);
        Assert.InRange(measured.ShortTermLufs, -22.5f, -19.5f);
        Assert.InRange(measured.IntegratedLufs, live.IntegratedLufs - 0.2f, live.IntegratedLufs + 0.2f);
        Assert.InRange(measured.MomentaryMaxLufs, -22.5f, -19.5f);
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
    public void ApplyLinearGainToLufs_AddsTwentyLog()
    {
        Assert.InRange(LoudnessMeterEngine.ApplyLinearGainToLufs(-24f, 2f), -18.05f, -17.95f);
        Assert.Equal(-24f, LoudnessMeterEngine.ApplyLinearGainToLufs(-24f, 1f), 5);
        Assert.True(float.IsNegativeInfinity(LoudnessMeterEngine.ApplyLinearGainToLufs(-24f, 0f)));
        Assert.True(float.IsNegativeInfinity(
            LoudnessMeterEngine.ApplyLinearGainToLufs(float.NegativeInfinity, 2f)));
    }

    [Fact]
    public void ApplyLinearGain_ShiftsLufsAndTruePeakButNotLra()
    {
        var snap = new LoudnessSnapshot(-20f, -18f, -19f, -16f, 7.5f, -3f, -24);
        var gained = LoudnessMeterEngine.ApplyLinearGain(snap, 0.5f);
        Assert.InRange(gained.ShortTermLufs - snap.ShortTermLufs, -6.1f, -5.9f);
        Assert.InRange(gained.IntegratedLufs - snap.IntegratedLufs, -6.1f, -5.9f);
        Assert.InRange(gained.MomentaryMaxLufs - snap.MomentaryMaxLufs, -6.1f, -5.9f);
        Assert.InRange(gained.TruePeakDb - snap.TruePeakDb, -6.1f, -5.9f);
        Assert.Equal(snap.LoudnessRangeLu, gained.LoudnessRangeLu, 5);
        Assert.Equal(snap, LoudnessMeterEngine.ApplyLinearGain(snap, 1f));
    }

    [Fact]
    public void MeasureFile_IgnoresSpareCapacityPastSampleCount()
    {
        const int rate = 48000;
        var frames = rate * 4;
        var used = frames * 2;
        var interleaved = new float[used * 2];
        var amp = (float)Math.Pow(10, -20d / 20d);
        for (var i = 0; i < frames; i++)
        {
            var s = amp * (float)Math.Sin(2 * Math.PI * 1000 * i / rate);
            interleaved[i * 2] = s;
            interleaved[i * 2 + 1] = s;
        }

        for (var i = used; i < interleaved.Length; i++)
        {
            interleaved[i] = 0.9f;
        }

        var exact = LoudnessMeterEngine.MeasureFile(interleaved[..used], 2, rate);
        var padded = LoudnessMeterEngine.MeasureFile(interleaved, 2, rate, sampleCount: used);
        Assert.InRange(padded.IntegratedLufs, exact.IntegratedLufs - 0.15f, exact.IntegratedLufs + 0.15f);
        Assert.InRange(padded.TruePeakDb, exact.TruePeakDb - 0.15f, exact.TruePeakDb + 0.15f);
        Assert.True(padded.TruePeakDb < -6f);
    }

    [Fact]
    public void MeasureFile_InPlaceGain_ShiftsIntegrated()
    {
        const int rate = 48000;
        var frames = rate * 4;
        var interleaved = new float[frames * 2];
        var amp = (float)Math.Pow(10, -20d / 20d);
        for (var i = 0; i < frames; i++)
        {
            var s = amp * (float)Math.Sin(2 * Math.PI * 1000 * i / rate);
            interleaved[i * 2] = s;
            interleaved[i * 2 + 1] = s;
        }

        var before = LoudnessMeterEngine.MeasureFile(interleaved, 2, rate);
        for (var i = 0; i < interleaved.Length; i++)
        {
            interleaved[i] *= 0.5f;
        }

        var after = LoudnessMeterEngine.MeasureFile(interleaved, 2, rate);
        Assert.InRange(before.IntegratedLufs - after.IntegratedLufs, 5.8f, 6.2f);
        Assert.InRange(before.ShortTermLufs - after.ShortTermLufs, 5.8f, 6.2f);
        Assert.InRange(before.TruePeakDb - after.TruePeakDb, 5.8f, 6.2f);
    }

    [Fact]
    public void OfflineCache_InvalidatesWhenRevisionChangesOnSameBuffer()
    {
        var samples = new float[8];
        Assert.True(LoudnessMeterView.CanReuseOffline(samples, 1, 8, samples, 1, 8));
        Assert.False(LoudnessMeterView.CanReuseOffline(samples, 1, 8, samples, 2, 8));
        Assert.False(LoudnessMeterView.CanReuseOffline(samples, 1, 8, samples, 1, 4));
        Assert.False(LoudnessMeterView.CanReuseOffline(samples, 1, 8, new float[8], 1, 8));
    }

    [Fact]
    public void Profile_AtFrame_AppliesPreviewGain()
    {
        var values = new float[] { -20f, -18f };
        var profile = new LoudnessProfile(48000, 4800, values);
        Assert.Equal(-20f, profile.AtFrame(0), 4);
        Assert.InRange(profile.AtFrame(0, _ => 2f), -14.05f, -13.95f);
        Assert.Equal(-20f, profile.AtFrame(0, _ => 1f), 4);
    }

    [Fact]
    public void Traffic_Lra_WarnsWhenWide()
    {
        Assert.Equal(LoudnessTraffic.Safe, LoudnessTrafficLight.ForLra(12f));
        Assert.Equal(LoudnessTraffic.Caution, LoudnessTrafficLight.ForLra(22f));
        Assert.Equal(LoudnessTraffic.Danger, LoudnessTrafficLight.ForLra(26f));
        Assert.Equal(LoudnessTraffic.Idle, LoudnessTrafficLight.ForLra(float.NaN));
    }

    [Fact]
    public void FillChip_OnlyWhenOfflineAndActive()
    {
        Assert.True(LoudnessMeterView.UsesFillChip(offline: true, LoudnessTraffic.Safe));
        Assert.True(LoudnessMeterView.UsesFillChip(offline: true, LoudnessTraffic.Caution));
        Assert.False(LoudnessMeterView.UsesFillChip(offline: true, LoudnessTraffic.Idle));
        Assert.False(LoudnessMeterView.UsesFillChip(offline: false, LoudnessTraffic.Safe));
    }

    [Fact]
    public void ChipText_IsBlackInDarkAndWhiteInLight()
    {
        Assert.Equal(Colors.Black, LoudnessMeterView.ChipTextColor(UiTheme.Dark));
        Assert.Equal(Colors.White, LoudnessMeterView.ChipTextColor(UiTheme.Light));
        Assert.Equal("MutedForeBrush", LoudnessMeterView.ChromeForeKey(UiTheme.Dark));
        Assert.Equal("MutedForeBrush", LoudnessMeterView.ChromeForeKey(UiTheme.Light));
        var cyan = Color.FromRgb(0x3A, 0xB8, 0xE8);
        var darkChip = LoudnessMeterView.ShadeChipFill(cyan, UiTheme.Dark);
        var lightChip = LoudnessMeterView.ShadeChipFill(cyan, UiTheme.Light);
        Assert.True(Luma(darkChip) < Luma(cyan));
        Assert.True(Luma(lightChip) > Luma(cyan));
    }

    private static double Luma(Color color) =>
        (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255d;
}
