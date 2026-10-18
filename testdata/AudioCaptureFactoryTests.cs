using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class AudioCaptureFactoryTests
{
    [Fact]
    public void HardwareKey_UsesOuterParenthesesAndStripsDefault()
    {
        Assert.Equal(
            "Realtek(R) Audio",
            AudioCaptureFactory.HardwareKey("Speakers (Realtek(R) Audio) (Default)"));
        Assert.Equal(
            "Realtek(R) Audio",
            AudioCaptureFactory.HardwareKey("Microphone (Realtek(R) Audio)"));
        Assert.Equal("USB Mic", AudioCaptureFactory.HardwareKey("USB Mic"));
    }

    [Fact]
    public void MatchRecordDeviceId_PairsByHardwareName()
    {
        AudioOutputDeviceInfo[] captures =
        [
            new(string.Empty, "Default"),
            new("cap-1", "Microphone (Realtek(R) Audio)"),
            new("cap-2", "USB Mic"),
        ];

        Assert.Equal(
            "cap-1",
            AudioCaptureFactory.MatchRecordDeviceId("Speakers (Realtek(R) Audio) (Default)", captures));
        Assert.Equal("cap-2", AudioCaptureFactory.MatchRecordDeviceId("USB Mic", captures));
        Assert.Equal(string.Empty, AudioCaptureFactory.MatchRecordDeviceId("Headphones (Other)", captures));
    }

    [Fact]
    public void ResolveRecordDeviceId_AsioKeepsPlaybackId()
    {
        Assert.Equal(
            "Focusrite USB ASIO",
            AudioCaptureFactory.ResolveRecordDeviceId(AudioOutputApi.Asio, "Focusrite USB ASIO"));
    }
}
