using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class SettingsToneTests
{
    [Fact]
    public void Amplitude_IsMinusTwentyDbfs()
    {
        Assert.Equal(0.1, SettingsTone.Amplitude, 6);
        Assert.Equal(-20, SettingsTone.LevelDb);
        Assert.Equal(1000, SettingsTone.Hertz);
    }

    [Fact]
    public void FillInterleaved_ReachesMinusTwentyOnOneKAt48k()
    {
        var samples = new float[48 * 2];
        SettingsTone.FillInterleaved(samples, channels: 2, sampleRate: 48000, phase: 0, out _);
        var peak = 0f;
        for (var i = 0; i < samples.Length; i++)
        {
            peak = Math.Max(peak, Math.Abs(samples[i]));
        }

        Assert.Equal(SettingsTone.Amplitude, peak, 3);
        Assert.Equal(samples[0], samples[1]);
    }

    [Fact]
    public void Provider_ScattersSineOntoMappedPorts()
    {
        var provider = new SettingsToneProvider(48000, logicalChannels: 2, outputPorts: 4, [3, 1])
        {
            Enabled = true,
        };
        var buffer = new float[48 * 4];
        Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));

        var peak0 = 0f;
        var peak1 = 0f;
        var peak2 = 0f;
        var peak3 = 0f;
        for (var i = 0; i < 48; i++)
        {
            var at = i * 4;
            peak0 = Math.Max(peak0, Math.Abs(buffer[at]));
            peak1 = Math.Max(peak1, Math.Abs(buffer[at + 1]));
            peak2 = Math.Max(peak2, Math.Abs(buffer[at + 2]));
            peak3 = Math.Max(peak3, Math.Abs(buffer[at + 3]));
        }

        Assert.Equal(0, peak0, 5);
        Assert.Equal(SettingsTone.Amplitude, peak1, 3);
        Assert.Equal(0, peak2, 5);
        Assert.Equal(SettingsTone.Amplitude, peak3, 3);
    }

    [Fact]
    public void Provider_SilenceWhenDisabled()
    {
        var provider = new SettingsToneProvider(48000, logicalChannels: 2, outputPorts: 2, [0, 1]);
        var buffer = new float[32];
        Array.Fill(buffer, 1f);
        provider.Read(buffer, 0, buffer.Length);
        Assert.All(buffer, sample => Assert.Equal(0, sample));
    }
}
