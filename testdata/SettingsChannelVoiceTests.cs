using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class SettingsChannelVoiceTests
{
    [Theory]
    [InlineData("L", "Left")]
    [InlineData("R", "Right")]
    [InlineData("C", "Center")]
    [InlineData("Ls", "Left surround")]
    [InlineData("Tfl", "Top front left")]
    public void SpokenText_IsEnglish(string label, string expected)
    {
        Assert.Equal(expected, SettingsChannelVoice.SpokenText(label));
    }

    [Fact]
    public void SpokenText_OmitsLfe()
    {
        Assert.Equal(string.Empty, SettingsChannelVoice.SpokenText("LFE"));
        Assert.Equal(string.Empty, SettingsChannelVoice.SpokenText("lfe"));
    }

    [Fact]
    public void SpokenText_CoversNonLfeCatalogLabels()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var layout in ChannelLayout.All)
        {
            foreach (var label in layout.Labels)
            {
                if (!seen.Add(label) || SettingsTone.IsLfe(label))
                {
                    continue;
                }

                Assert.False(string.IsNullOrWhiteSpace(SettingsChannelVoice.SpokenText(label)));
            }
        }
    }

    [Fact]
    public void NormalizeLoop_PeaksAtMinusThree()
    {
        Assert.Equal(-3, SettingsChannelVoice.LevelDb);
        var loop = SettingsChannelVoice.NormalizeLoop([0.5f, -1f, 0.25f], 48000, gapSeconds: 0);
        Assert.Equal(3, loop.Length);
        Assert.Equal(SettingsChannelVoice.Amplitude, loop.Max(static sample => Math.Abs(sample)), 5);
    }

    [Fact]
    public void Resample_DoublesLengthWhenRateDoubles()
    {
        var source = new float[] { 0f, 1f, 0f };
        var dest = SettingsChannelVoice.Resample(source, 22050, 44100);
        Assert.Equal(6, dest.Length);
        Assert.Equal(0, dest[0], 5);
        Assert.Equal(1, dest[2], 5);
    }

    [Fact]
    public void TryRender_ReturnsSamplesOrAClearError()
    {
        var ok = SettingsChannelVoice.TryRender("L", 48000, out var samples, out var error);
        if (ok)
        {
            Assert.NotEmpty(samples);
            Assert.True(samples.Max(static sample => Math.Abs(sample)) <= SettingsChannelVoice.Amplitude + 1e-4f);
            return;
        }

        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryRender_RejectsLfe()
    {
        Assert.False(SettingsChannelVoice.TryRender("LFE", 48000, out var samples, out var error));
        Assert.Empty(samples);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
