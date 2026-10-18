using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class WaveDeviceNamesTests
{
    [Fact]
    public void Resolve_KeepsShortCompleteName()
    {
        Assert.Equal(
            "Speakers (RME Fireface UCX II)",
            WaveDeviceNames.Resolve(
                "Speakers (RME Fireface UCX II)",
                ["Speakers (NVIDIA Broadcast)", "Speakers (RME Fireface UCX II)"]));
    }

    [Fact]
    public void Resolve_CompletesTruncatedWaveOutName()
    {
        var truncated = "Voicemeeter AUX Input (VB-Audio";
        Assert.True(truncated.Length >= WaveDeviceNames.CapsNameLimit);
        Assert.Equal(
            "Voicemeeter AUX Input (VB-Audio Voicemeeter VAIO)",
            WaveDeviceNames.Resolve(
                truncated,
                ["Voicemeeter AUX Input (VB-Audio Voicemeeter VAIO)"]));
    }

    [Fact]
    public void ResolveAll_DoesNotReuseTheSameFriendlyName()
    {
        var names = WaveDeviceNames.ResolveAll(
            [
                "Voicemeeter AUX Input (VB-Audio",
                "Voicemeeter Input (VB-Audio Voi",
            ],
            [
                "Voicemeeter AUX Input (VB-Audio Voicemeeter VAIO)",
                "Voicemeeter Input (VB-Audio Voicemeeter VAIO)",
            ]);
        Assert.Equal("Voicemeeter AUX Input (VB-Audio Voicemeeter VAIO)", names[0]);
        Assert.Equal("Voicemeeter Input (VB-Audio Voicemeeter VAIO)", names[1]);
    }

    [Fact]
    public void Resolve_LeavesUnknownTruncatedName()
    {
        var truncated = "Mystery Device Name That Is Cut";
        Assert.Equal(truncated, WaveDeviceNames.Resolve(truncated, ["Speakers (Realtek(R) Audio)"]));
    }

    [Fact]
    public void ResolveAll_CompletesWaveOutNameThatDroppedSpaces()
    {
        var amplifier = "AV Amplifier (NVIDIA HighDefin";
        var toshiba = "TOSHIBA-TV (NVIDIA HighDefinit";
        Assert.True(amplifier.Length < WaveDeviceNames.CapsNameLimit);
        Assert.DoesNotContain(')', amplifier);

        var names = WaveDeviceNames.ResolveAll(
            [amplifier, toshiba],
            [
                "AV Amplifier (NVIDIA High Definition Audio)",
                "TOSHIBA-TV (NVIDIA High Definition Audio)",
            ]);
        Assert.Equal("AV Amplifier (NVIDIA High Definition Audio)", names[0]);
        Assert.Equal("TOSHIBA-TV (NVIDIA High Definition Audio)", names[1]);
    }
}
