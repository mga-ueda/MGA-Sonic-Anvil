using System.Linq;
using System.Text.Json;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class DefaultAudioFormatTests
{
    [Fact]
    public void Defaults_Are48kHz24BitStereo()
    {
        Assert.Equal(48000, DefaultAudioFormat.SampleRate);
        Assert.Equal(24, DefaultAudioFormat.BitsPerSample);
        Assert.Equal("Stereo", DefaultAudioFormat.ChannelLayoutId);
        Assert.Equal(new[] { 8, 16, 24 }, DefaultAudioFormat.BitDepths);
        Assert.DoesNotContain(4, DefaultAudioFormat.BitDepths);

        var settings = AppSettings.CreateDefault();
        var format = settings.ResolvedDefaultAudioFormat();
        Assert.Equal(48000, format.SampleRate);
        Assert.Equal(24, format.BitsPerSample);
        Assert.Equal("Stereo", format.Layout.Id);
        Assert.Equal(2, format.Channels);
    }

    [Theory]
    [InlineData(44100, 44100)]
    [InlineData(48000, 48000)]
    [InlineData(12345, 12345)]
    [InlineData(999, 48000)]
    [InlineData(400000, 48000)]
    [InlineData(0, 48000)]
    public void ClampSampleRate_KeepsValidOrFallsBack(int input, int expected)
    {
        Assert.Equal(expected, DefaultAudioFormat.ClampSampleRate(input));
    }

    [Theory]
    [InlineData(8, 8)]
    [InlineData(16, 16)]
    [InlineData(24, 24)]
    [InlineData(4, 24)]
    [InlineData(32, 24)]
    [InlineData(0, 24)]
    public void ClampBitDepth_Keeps8_16_24OrFallsBack(int input, int expected)
    {
        Assert.Equal(expected, DefaultAudioFormat.ClampBitDepth(input));
    }

    [Theory]
    [InlineData("Stereo", "Stereo")]
    [InlineData("stereo", "Stereo")]
    [InlineData("Mono", "Mono")]
    [InlineData("5.1", "5.1")]
    [InlineData(null, "Stereo")]
    [InlineData("", "Stereo")]
    [InlineData("Nope", "Stereo")]
    public void ClampLayout_KeepsKnownOrFallsBackToStereo(string? input, string expectedId)
    {
        Assert.Equal(expectedId, DefaultAudioFormat.ClampLayout(input).Id);
    }

    [Fact]
    public void Resolve_ClampsEachField()
    {
        var format = DefaultAudioFormat.Resolve(999, 4, "missing");
        Assert.Equal(48000, format.SampleRate);
        Assert.Equal(24, format.BitsPerSample);
        Assert.Equal("Stereo", format.Layout.Id);
        Assert.Equal(2, format.Channels);
    }

    [Fact]
    public void AppSettings_ResolvedHelpers_ClampStoredValues()
    {
        var settings = new AppSettings
        {
            DefaultSampleRate = 800,
            DefaultBitsPerSample = 32,
            DefaultChannelLayout = "bad",
        };

        Assert.Equal(48000, settings.ResolvedDefaultSampleRate());
        Assert.Equal(24, settings.ResolvedDefaultBitsPerSample());
        Assert.Equal("Stereo", settings.ResolvedDefaultChannelLayout().Id);
    }

    [Fact]
    public void Deserialize_MissingDefaultFormat_Uses48k24Stereo()
    {
        var settings = JsonSerializer.Deserialize(
            """{"SettingsGeneration":1,"AudioApi":"WaveOut"}""",
            AppSettingsJsonContext.Default.AppSettings);
        Assert.NotNull(settings);
        Assert.Equal(48000, settings!.DefaultSampleRate);
        Assert.Equal(24, settings.DefaultBitsPerSample);
        Assert.Equal("Stereo", settings.DefaultChannelLayout);
    }

    [Fact]
    public void PickerLayouts_AlwaysIncludesMonoAndVisibleSpeakers()
    {
        var stereoOnly = DefaultAudioFormat.PickerLayouts(["Stereo"]);
        Assert.Equal(["Mono", "Stereo"], stereoOnly.Select(item => item.Id));

        var surround = DefaultAudioFormat.PickerLayouts(["5.1"]);
        Assert.Equal(["Mono", "5.1"], surround.Select(item => item.Id));
        Assert.DoesNotContain(surround, item => item.Id == "Stereo");
    }

    [Fact]
    public void ClampPickerLayout_FallsBackWhenHidden()
    {
        Assert.Equal("5.1", DefaultAudioFormat.ClampPickerLayout("5.1", ["5.1"]).Id);
        Assert.Equal("Stereo", DefaultAudioFormat.ClampPickerLayout("5.1", ["Stereo"]).Id);
        Assert.Equal("Mono", DefaultAudioFormat.ClampPickerLayout("Stereo", ["5.1"]).Id);
    }

    [Fact]
    public void LastNewFormat_FallsBackToDefaultThenRemembers()
    {
        var settings = AppSettings.CreateDefault();
        var first = settings.ResolvedLastNewAudioFormat();
        Assert.Equal(48000, first.SampleRate);
        Assert.Equal(24, first.BitsPerSample);
        Assert.Equal("Stereo", first.Layout.Id);

        settings.RememberNewAudioFormat(DefaultAudioFormat.Resolve(44100, 16, "Mono", settings.ResolvedVisibleSpeakerIds()));
        var last = settings.ResolvedLastNewAudioFormat();
        Assert.Equal(44100, last.SampleRate);
        Assert.Equal(16, last.BitsPerSample);
        Assert.Equal("Mono", last.Layout.Id);
    }

    [Fact]
    public void AppSettings_RoundTripsDefaultFormat()
    {
        var settings = new AppSettings
        {
            DefaultSampleRate = 44100,
            DefaultBitsPerSample = 16,
            DefaultChannelLayout = "Mono",
        };
        var json = JsonSerializer.Serialize(settings, AppSettingsJsonContext.Default.AppSettings);
        var back = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
        Assert.NotNull(back);
        Assert.Equal(44100, back!.DefaultSampleRate);
        Assert.Equal(16, back.DefaultBitsPerSample);
        Assert.Equal("Mono", back.DefaultChannelLayout);
        Assert.Equal(1, back.ResolvedDefaultAudioFormat().Channels);
    }
}
