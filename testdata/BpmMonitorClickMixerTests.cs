using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class BpmMonitorClickMixerTests
{
    [Fact]
    public void MixBuffer_Onset_AddsLowWavAtMinusTwentyDb()
    {
        var click = new BpmMonitorClickMixer();
        click.EnsureSample();
        click.SetDeviceRate(48000);
        const int frames = 2048;
        var buffer = new float[frames * 2];
        click.MixBuffer(buffer, 0, channels: 2, frames, [100], dimLinear: 1f);
        float peak = 0;
        for (var i = 100 * 2; i < Math.Min(buffer.Length, (100 + 512) * 2); i++)
        {
            peak = Math.Max(peak, Math.Abs(buffer[i]));
        }

        Assert.True(peak > 0.001f);
        Assert.True(peak <= RangeClickMixer.OutputGainLinear + 0.05f);
        Assert.Equal(0f, buffer[0]);
    }

    [Fact]
    public void CollectOnsetsFromSynthDiff_FindsRisingEdge()
    {
        const int frames = 200;
        var original = new float[frames];
        var scratch = new float[frames];
        for (var i = 40; i < 80; i++)
        {
            scratch[i] = 0.25f;
        }

        var into = new List<int>();
        BpmMonitorClickMixer.CollectOnsetsFromSynthDiff(original, 0, scratch, frames, channels: 1, into);
        Assert.Contains(40, into);
        Assert.True(into.Count >= 1);
    }

    [Fact]
    public void CollectOnsetsFromSynthDiff_DampedSineClick_OneOnsetOnly()
    {
        // EffeTune MixClickSample 相当（2 kHz 減衰サイン）。ゼロクロスで再アームしないこと。
        const int sampleRate = 48000;
        const int frames = 4800;
        var original = new float[frames];
        var scratch = new float[frames];
        var omega = 2.0 * Math.PI * 2000.0 / sampleRate;
        var r = Math.Exp(-1.0 / (0.006 * sampleRate));
        var amplitude = 0.31622776601683794 * Math.Exp(0.0005 / 0.006);
        var a = 2.0 * r * Math.Cos(omega);
        var b = r * r;
        var y1 = 0.0;
        var y2 = -amplitude * Math.Sin(omega) / r;
        var attack = (int)Math.Ceiling(0.0005 * sampleRate);
        var length = (int)Math.Ceiling(0.06 * sampleRate);
        for (var age = 0; age < length && age < frames; age++)
        {
            var y = a * y1 - b * y2;
            y2 = y1;
            y1 = y;
            var gain = age < attack ? age / (double)attack : 1.0;
            scratch[age] = (float)(gain * y);
        }

        var into = new List<int>();
        BpmMonitorClickMixer.CollectOnsetsFromSynthDiff(
            original, 0, scratch, frames, channels: 1, into, sampleRate);
        Assert.Single(into);
        Assert.InRange(into[0], 1, attack + 8);
    }

    [Fact]
    public void Reset_StopsCarryOverVoice()
    {
        var click = new BpmMonitorClickMixer();
        click.EnsureSample();
        click.SetDeviceRate(48000);
        var buffer = new float[256];
        click.MixBuffer(buffer, 0, 1, 128, [0], 1f);
        click.Reset();
        Array.Clear(buffer);
        click.MixBuffer(buffer, 0, 1, 128, [], 1f);
        Assert.All(buffer, s => Assert.Equal(0f, s));
    }
}
