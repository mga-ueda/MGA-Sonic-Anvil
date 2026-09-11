using System.Text.Json;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class SpeakerPresetTests
{
    [Fact]
    public void NewSettings_HasFullCatalogAndStereoActive()
    {
        var settings = new AppSettings();
        settings.EnsureSpeakerPresets();
        Assert.Equal(ChannelLayout.All.Length, settings.SpeakerPresets.Length);
        Assert.Equal(SpeakerPreset.DefaultId, settings.ActiveSpeakerPresetId);
        Assert.Equal("Stereo", settings.ResolvedSpeaker().Id);
        Assert.Equal(2, settings.ResolvedSpeaker().Channels);
        Assert.Equal("Stereo", settings.ResolvedPlaybackLayout().Id);
        Assert.Equal(["L", "R"], settings.ResolvedPlaybackLayout().Labels);
        Assert.Contains(settings.SpeakerPresets, preset => preset.Id == "9.1.6");
        Assert.Contains(settings.SpeakerPresets, preset => preset.Id == "Quad");
        Assert.Contains(settings.SpeakerPresets, preset => preset.Id == "2.1");
        Assert.Contains(settings.SpeakerPresets, preset => preset.Id == "5.1.2-Side");
        Assert.Equal(["Stereo"], settings.ResolvedVisibleSpeakerIds());
        Assert.Equal(["Stereo"], settings.MenuSpeakers().Select(preset => preset.Id));
    }

    [Fact]
    public void NormalizeVisibleIds_EmptyIsStereoOnly()
    {
        Assert.Equal(["Stereo"], SpeakerPreset.NormalizeVisibleIds(null));
        Assert.Equal(["Stereo"], SpeakerPreset.NormalizeVisibleIds([]));
        Assert.Equal(["Stereo"], SpeakerPreset.NormalizeVisibleIds(["nope"]));
    }

    [Fact]
    public void NormalizeVisibleIds_KeepsCatalogOrder()
    {
        Assert.Equal(["Stereo", "5.1"], SpeakerPreset.NormalizeVisibleIds(["5.1", "Stereo", "5.1"]));
    }

    [Fact]
    public void FilterMenu_KeepsActiveWhenHidden()
    {
        var settings = new AppSettings();
        settings.EnsureSpeakerPresets();
        settings.ActiveSpeakerPresetId = "5.1";
        settings.ApplyVisibleSpeakerIds(["Stereo"]);
        Assert.Equal(["Stereo"], settings.ResolvedVisibleSpeakerIds());
        Assert.Equal(["Stereo", "5.1"], settings.MenuSpeakers().Select(preset => preset.Id));
    }

    [Fact]
    public void LegacySurroundSettings_MapOntoCatalogFiveOne()
    {
        var settings = new AppSettings
        {
            RecordLayout = "5.1",
            PlaybackLayout = "5.1",
            AudioApi = "Asio",
            AudioDeviceId = "Fireface",
            RecordInputMap = [0, 1, 2, 3, 4, 5],
            PlaybackOutputMap = [0, 1, 2, 3, 4, 5],
        };
        settings.EnsureSpeakerPresets();
        Assert.Equal(ChannelLayout.All.Length, settings.SpeakerPresets.Length);
        Assert.Equal("5.1", settings.ActiveSpeakerPresetId);
        var fiveOne = settings.FindSpeaker("5.1");
        Assert.NotNull(fiveOne);
        Assert.Equal(6, fiveOne!.Channels);
        Assert.Equal([0, 1, 2, 3, 4, 5], fiveOne.PlaybackOutputMap);
        Assert.Equal("Asio", fiveOne.AudioApi);
        Assert.Equal("Fireface", fiveOne.AudioDeviceId);
        Assert.Equal(6, settings.ResolvedPlaybackLayout().Channels);
        Assert.Equal(["L", "R", "C", "LFE", "Ls", "Rs"], settings.ResolvedPlaybackLayout().Labels);
    }

    [Fact]
    public void CustomSixChannelPreset_MigratesToFiveOne()
    {
        var settings = new AppSettings();
        settings.EnsureSpeakerPresets();
        var extra = settings.FindSpeaker("Stereo")!.Clone();
        extra.Id = "studio";
        extra.Name = "Studio";
        extra.Channels = 6;
        extra.PlaybackOutputMap = [2, 3, 4, 5, 6, 7];
        settings.ReplaceSpeakerPresets([settings.FindSpeaker("Stereo")!, extra], extra.Id);

        Assert.Equal(ChannelLayout.All.Length, settings.SpeakerPresets.Length);
        Assert.Equal("5.1", settings.ActiveSpeakerPresetId);
        Assert.Equal([2, 3, 4, 5, 6, 7], settings.ResolvedPlaybackOutputMap());
    }

    [Fact]
    public void SettingsJson_RoundTripsCatalogMaps()
    {
        var settings = new AppSettings();
        settings.EnsureSpeakerPresets();
        var fiveOne = settings.FindSpeaker("5.1")!;
        fiveOne.PlaybackOutputMap = [2, 3, 4, 5, 6, 7];
        fiveOne.FileChannelMap = [2, 0, 1, 3, 4, 5];
        settings.ReplaceSpeakerPresets(settings.SpeakerPresets, "5.1");
        settings.ApplyVisibleSpeakerIds(["5.1", "Stereo"]);

        var json = JsonSerializer.Serialize(settings, AppSettingsJsonContext.Default.AppSettings);
        var back = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
        Assert.NotNull(back);
        back!.EnsureSpeakerPresets();
        Assert.Equal(ChannelLayout.All.Length, back.SpeakerPresets.Length);
        Assert.Equal("5.1", back.ActiveSpeakerPresetId);
        Assert.Equal(6, back.ResolvedSpeaker().Channels);
        Assert.Equal([2, 3, 4, 5, 6, 7], back.ResolvedPlaybackOutputMap());
        Assert.Equal([2, 0, 1, 3, 4, 5], back.ResolvedFileChannelMap());
        Assert.Equal(["Stereo", "5.1"], back.ResolvedVisibleSpeakerIds());
    }

    [Fact]
    public void MergeCatalog_KeepsStereoAndFiveOneFileMapsApart()
    {
        var settings = new AppSettings();
        settings.EnsureSpeakerPresets();
        settings.FindSpeaker("Stereo")!.FileChannelMap = [1, 0, 3, 4, 5, 2];
        settings.FindSpeaker("5.1")!.FileChannelMap = [2, 0, 1, 3, 4, 5];
        settings.ReplaceSpeakerPresets(settings.SpeakerPresets, "Stereo");
        Assert.Equal([1, 0], settings.FindSpeaker("Stereo")!.FileChannelMap);
        Assert.Equal([2, 0, 1, 3, 4, 5], settings.FindSpeaker("5.1")!.FileChannelMap);
        Assert.False(ReferenceEquals(
            settings.FindSpeaker("Stereo")!.FileChannelMap,
            settings.FindSpeaker("5.1")!.FileChannelMap));
    }

    [Fact]
    public void SnapshotMap_KeepsOnlyThatLayoutLength()
    {
        Assert.Empty(SpeakerPreset.SnapshotMap(null, 2));
        Assert.Empty(SpeakerPreset.SnapshotMap([], 6));
        Assert.Equal([1, 0], SpeakerPreset.SnapshotMap([1, 0, 3, 4, 5, 2], 2));
        Assert.Equal([2, 0, 1, 3, 4, 5], SpeakerPreset.SnapshotMap([2, 0, 1, 3, 4, 5], 6));
    }

    [Fact]
    public void SwitchingActiveSpeaker_KeepsEachFileChannelMap()
    {
        var settings = new AppSettings();
        settings.EnsureSpeakerPresets();
        settings.FindSpeaker("Stereo")!.FileChannelMap = [1, 0];
        settings.FindSpeaker("5.1")!.FileChannelMap = [2, 0, 1, 3, 4, 5];
        settings.ActiveSpeakerPresetId = "Stereo";
        settings.EnsureSpeakerPresets();
        Assert.Equal([1, 0], settings.FindSpeaker("Stereo")!.FileChannelMap);
        Assert.Equal([2, 0, 1, 3, 4, 5], settings.FindSpeaker("5.1")!.FileChannelMap);

        settings.ActiveSpeakerPresetId = "5.1";
        settings.EnsureSpeakerPresets();
        Assert.Equal([1, 0], settings.FindSpeaker("Stereo")!.FileChannelMap);
        Assert.Equal([2, 0, 1, 3, 4, 5], settings.ResolvedFileChannelMap());
        Assert.Equal([2, 0, 1, 3, 4, 5], settings.FileChannelMap);
    }
}
