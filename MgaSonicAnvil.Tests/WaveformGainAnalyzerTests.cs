using MgaSonicAnvil.Audio;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class WaveformGainAnalyzerTests
{
    [Fact]
    public void WholeFilePlusSixDb_ShiftsRmsAndPeakBySix()
    {
        var samples = Constant(frames: 48000, value: 0.25f);
        var analyzer = WaveformGainAnalyzer.Build(samples, 2, 48000, new WaveSelection(0, 48000));
        Assert.InRange(analyzer.Current.RmsDb, -12.2f, -11.9f);
        Assert.InRange(analyzer.Current.PeakDb, -12.2f, -11.9f);

        var after = analyzer.Predict(6);
        Assert.InRange(after.RmsDb - analyzer.Current.RmsDb, 5.9f, 6.1f);
        Assert.InRange(after.PeakDb - analyzer.Current.PeakDb, 5.9f, 6.1f);
    }

    [Fact]
    public void WholeFilePlusThreeDb_ShiftsIntegratedLufsByAboutThree()
    {
        const int rate = 48000;
        var frames = rate * 4;
        var samples = Sine(frames, rate, amp: (float)Math.Pow(10, -20d / 20d));
        var analyzer = WaveformGainAnalyzer.Build(samples, 2, rate, new WaveSelection(0, frames));
        Assert.True(float.IsFinite(analyzer.Current.IntegratedLufs));

        var after = analyzer.Predict(3);
        Assert.InRange(after.IntegratedLufs - analyzer.Current.IntegratedLufs, 2.8f, 3.2f);
    }

    [Fact]
    public void PartialRange_RmsUsesOutsidePlusGainedInside()
    {
        var samples = Constant(frames: 100, value: 0.5f);
        var analyzer = WaveformGainAnalyzer.Build(samples, 2, 48000, new WaveSelection(0, 50));
        var after = analyzer.Predict(6);

        // 半分だけ +6 dB（振幅×2 → エネルギー×4）。
        var expected = 20d * Math.Log10(Math.Sqrt((0.25 * 100 + 0.25 * 4 * 100) / 200d));
        Assert.InRange(after.RmsDb, (float)(expected - 0.05), (float)(expected + 0.05));
        Assert.InRange(after.PeakDb, -0.05f, 0.05f);
    }

    [Fact]
    public void ZeroGain_ReturnsCurrentReadings()
    {
        var samples = Constant(frames: 64, value: 0.4f);
        var analyzer = WaveformGainAnalyzer.Build(samples, 2, 48000, new WaveSelection(0, 64));
        var after = analyzer.Predict(0);
        Assert.Equal(analyzer.Current, after);
    }

    [Fact]
    public void SnapGainDb_RoundsToTenthsAndClamps()
    {
        Assert.Equal(1.3, WaveformGainAnalyzer.SnapGainDb(1.25));
        Assert.Equal(60, WaveformGainAnalyzer.SnapGainDb(99));
        Assert.Equal(-60, WaveformGainAnalyzer.SnapGainDb(-99));
        Assert.Equal(0, WaveformGainAnalyzer.SnapGainDb(double.NaN));
    }

    [Fact]
    public void NudgeStep_UsesModifiers()
    {
        Assert.Equal(0.1, VolumeGainPicker.NudgeStep(System.Windows.Input.ModifierKeys.None));
        Assert.Equal(1, VolumeGainPicker.NudgeStep(System.Windows.Input.ModifierKeys.Shift));
        Assert.Equal(3, VolumeGainPicker.NudgeStep(System.Windows.Input.ModifierKeys.Control));
        Assert.Equal(6, VolumeGainPicker.NudgeStep(System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift));
    }

    private static float[] Constant(int frames, float value)
    {
        var samples = new float[frames * 2];
        Array.Fill(samples, value);
        return samples;
    }

    private static float[] Sine(int frames, int rate, float amp)
    {
        var samples = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var s = amp * (float)Math.Sin(2 * Math.PI * 1000 * i / rate);
            samples[i * 2] = s;
            samples[i * 2 + 1] = s;
        }

        return samples;
    }
}
