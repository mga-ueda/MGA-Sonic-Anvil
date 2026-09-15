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
        Assert.False(settings.AutoSpeakerSelect);
        Assert.False(new AppSettings().AutoSpeakerSelect);
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
    public void EmptySpeakerPresets_UseCatalogAndDefaultStereo()
    {
        var settings = new AppSettings { SpeakerPresets = [] };
        settings.EnsureSpeakerPresets();
        Assert.Equal(ChannelLayout.All.Length, settings.SpeakerPresets.Length);
        Assert.Equal(SpeakerPreset.DefaultId, settings.ActiveSpeakerPresetId);
        Assert.Equal("Stereo", settings.ResolvedSpeaker().Id);
        Assert.Equal(2, settings.ResolvedPlaybackLayout().Channels);
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
        settings.AutoSpeakerSelect = true;

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
        Assert.True(back.AutoSpeakerSelect);
    }

    [Fact]
    public void SettingsJson_MissingAutoSpeakerSelect_IsOff()
    {
        var json = """{"SettingsGeneration":1,"ActiveSpeakerPresetId":"Stereo"}""";
        var back = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
        Assert.NotNull(back);
        Assert.False(back!.AutoSpeakerSelect);
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

    [Fact]
    public void ResolveAutoSpeakerId_OneTwoChannelsPreferStereo()
    {
        var settings = Visible("Stereo", "Mono", "5.1");
        settings.ActiveSpeakerPresetId = "5.1";
        Assert.Equal("Stereo", AutoId(settings, 1));
        Assert.Equal("Stereo", AutoId(settings, 2));
        settings.ActiveSpeakerPresetId = "Mono";
        Assert.Equal("Stereo", AutoId(settings, 1));
        settings.ActiveSpeakerPresetId = "Stereo";
        Assert.Null(AutoId(settings, 1));
        Assert.Null(AutoId(settings, 2));
    }

    [Fact]
    public void ResolveAutoSpeakerId_MonoWhenStereoHidden()
    {
        var settings = Visible("Mono", "5.1");
        settings.ActiveSpeakerPresetId = "5.1";
        Assert.Equal("Mono", AutoId(settings, 1));
        Assert.Null(AutoId(settings, 2));
        settings.ActiveSpeakerPresetId = "Mono";
        Assert.Null(AutoId(settings, 1));
    }

    [Fact]
    public void ResolveAutoSpeakerId_SixChannelsPicksFiveOneWhenEnabled()
    {
        var settings = Visible("Stereo", "5.1", "5.1-Side", "6.0");
        settings.ActiveSpeakerPresetId = "Stereo";
        Assert.Equal("5.1", AutoId(settings, 6));
        settings.ActiveSpeakerPresetId = "5.1";
        Assert.Null(AutoId(settings, 6));
        settings.ActiveSpeakerPresetId = "5.1-Side";
        Assert.Null(AutoId(settings, 6));
    }

    [Fact]
    public void ResolveAutoSpeakerId_UniqueSixChannelMatch()
    {
        var settings = Visible("Stereo", "5.1-Side");
        settings.ActiveSpeakerPresetId = "Stereo";
        Assert.Equal("5.1-Side", AutoId(settings, 6));
    }

    [Fact]
    public void ResolveAutoSpeakerId_AmbiguousWithoutPreferredDoesNotSwitch()
    {
        var settings = Visible("Stereo", "5.1-Side", "6.0", "4.0.2");
        settings.ActiveSpeakerPresetId = "Stereo";
        Assert.Null(AutoId(settings, 6));
    }

    [Fact]
    public void ResolveAutoSpeakerId_NoMatchLeavesCurrent()
    {
        var settings = Visible("Stereo");
        settings.ActiveSpeakerPresetId = "Stereo";
        Assert.Null(AutoId(settings, 6));
        Assert.Null(AutoId(settings, 8));
    }

    [Fact]
    public void ResolveAutoSpeakerId_EightChannelsPicksSevenOne()
    {
        var settings = Visible("Stereo", "7.1", "7.1-SDDS", "8.0");
        settings.ActiveSpeakerPresetId = "Stereo";
        Assert.Equal("7.1", AutoId(settings, 8));
    }

    private static AppSettings Visible(params string[] ids)
    {
        var settings = new AppSettings();
        settings.EnsureSpeakerPresets();
        settings.ApplyVisibleSpeakerIds(ids);
        return settings;
    }

    private static string? AutoId(AppSettings settings, int fileChannels) =>
        SpeakerPreset.ResolveAutoSpeakerId(
            fileChannels,
            settings.SpeakerPresets,
            settings.ResolvedVisibleSpeakerIds(),
            settings.ActiveSpeakerPresetId);
}
