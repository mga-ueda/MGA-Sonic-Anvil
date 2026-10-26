using System.Text.Json;
using MgaSonicAnvil.Config;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class WindowLayoutPresetTests
{
    [Fact]
    public void Defaults_MatchPlanTable()
    {
        var f9 = WindowLayoutPreset.CreateDefault(WindowLayoutSlot.F9);
        Assert.Equal(LayoutFullscreenMode.Off, f9.ResolvedFullscreenMode());
        Assert.False(f9.ShowLibraryAndFavorites);
        Assert.False(f9.ResolvedShowLibraryAndFavorites(WindowLayoutSlot.F9));
        Assert.True(f9.ShowWaveform);
        Assert.False(f9.ShowAnalyzerArea);
        Assert.Equal(LayoutAnalyzerDock.Bottom, f9.ResolvedAnalyzerDock());
        Assert.False(f9.ShowLevelMeter);
        Assert.False(f9.ResolvedShowStatusBar(WindowLayoutSlot.F9));

        var f10 = WindowLayoutPreset.CreateDefault(WindowLayoutSlot.F10);
        Assert.Equal(LayoutFullscreenMode.Off, f10.ResolvedFullscreenMode());
        Assert.True(f10.ShowLibraryAndFavorites);
        Assert.True(f10.ShowWaveform);
        Assert.True(f10.ShowAnalyzerArea);
        Assert.True(f10.ShowLevelMeter);
        Assert.True(f10.ResolvedShowStatusBar(WindowLayoutSlot.F10));

        var f11 = WindowLayoutPreset.CreateDefault(WindowLayoutSlot.F11);
        Assert.Equal(LayoutFullscreenMode.Toggle, f11.ResolvedFullscreenMode());
        Assert.True(f11.FullscreenToggleStartsOn);
        Assert.False(f11.ShowLibraryAndFavorites);
        Assert.True(f11.ShowWaveform);
        Assert.False(f11.ShowAnalyzerArea);
        Assert.True(f11.ShowLevelMeter);
        Assert.True(f11.ResolvedShowStatusBar(WindowLayoutSlot.F11));

        var f12 = WindowLayoutPreset.CreateDefault(WindowLayoutSlot.F12);
        Assert.Equal(LayoutFullscreenMode.Toggle, f12.ResolvedFullscreenMode());
        Assert.False(f12.FullscreenToggleStartsOn);
        Assert.False(f12.ShowLibraryAndFavorites);
        Assert.True(f12.ShowWaveform);
        Assert.True(f12.ShowAnalyzerArea);
        Assert.True(f12.ShowLevelMeter);
        Assert.True(f12.ResolvedShowStatusBar(WindowLayoutSlot.F12));
    }

    [Fact]
    public void Settings_EnsureAndRoundTrip()
    {
        var settings = AppSettings.CreateDefault();
        Assert.NotNull(settings.LayoutPresetF9);
        var f10Default = WindowLayoutPreset.CreateDefault(WindowLayoutSlot.F10);
        var f10Resolved = settings.ResolvedLayoutPreset(WindowLayoutSlot.F10);
        Assert.Equal(f10Default.ResolvedFullscreenMode(), f10Resolved.ResolvedFullscreenMode());
        Assert.Equal(f10Default.ShowLibraryAndFavorites, f10Resolved.ShowLibraryAndFavorites);
        Assert.Equal(f10Default.ShowWaveform, f10Resolved.ShowWaveform);
        Assert.Equal(f10Default.ShowAnalyzerArea, f10Resolved.ShowAnalyzerArea);
        Assert.Equal(f10Default.ResolvedAnalyzerDock(), f10Resolved.ResolvedAnalyzerDock());
        Assert.Equal(f10Default.ShowLevelMeter, f10Resolved.ShowLevelMeter);
        Assert.Equal(f10Default.ShowStatusBar, f10Resolved.ShowStatusBar);

        settings.ApplyLayoutPreset(
            WindowLayoutSlot.F9,
            new WindowLayoutPreset
            {
                FullscreenMode = "Toggle",
                FullscreenToggleStartsOn = true,
                ShowLibraryAndFavorites = true,
                ShowWaveform = false,
                ShowAnalyzerArea = true,
                AnalyzerDock = "Top",
                ShowLevelMeter = false,
                ShowStatusBar = false,
            });

        var json = JsonSerializer.Serialize(settings, AppSettingsJsonContext.Default.AppSettings);
        var back = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
        Assert.NotNull(back);
        var f9 = back!.ResolvedLayoutPreset(WindowLayoutSlot.F9);
        Assert.Equal(LayoutFullscreenMode.Toggle, f9.ResolvedFullscreenMode());
        Assert.True(f9.FullscreenToggleStartsOn);
        // プレイヤー F9 はライブラリ帯オフ固定（保存で true を渡しても既定へ）。
        Assert.False(f9.ShowLibraryAndFavorites);
        Assert.False(f9.ShowWaveform);
        Assert.True(f9.ShowAnalyzerArea);
        Assert.Equal(LayoutAnalyzerDock.Top, f9.ResolvedAnalyzerDock());
        Assert.False(f9.ShowLevelMeter);
        Assert.False(f9.ResolvedShowStatusBar(WindowLayoutSlot.F9));

        var missing = JsonSerializer.Deserialize(
            """{"SettingsGeneration":1}""",
            AppSettingsJsonContext.Default.AppSettings);
        Assert.NotNull(missing);
        missing!.EnsureLayoutPresets();
        Assert.False(missing.ResolvedLayoutPreset(WindowLayoutSlot.F9).ShowLibraryAndFavorites);
        Assert.True(missing.ResolvedLayoutPreset(WindowLayoutSlot.F10).ShowLibraryAndFavorites);
        Assert.True(WindowLayoutPreset.IsPlayerLayoutSlot(WindowLayoutSlot.F9));
        Assert.True(WindowLayoutPreset.IsPlayerLayoutSlot(WindowLayoutSlot.F10));
        Assert.False(WindowLayoutPreset.IsPlayerLayoutSlot(WindowLayoutSlot.F11));

        // 旧プリセット JSON に ShowStatusBar が無くてもスロット既定へ。
        var legacy = JsonSerializer.Deserialize(
            """{"SettingsGeneration":1,"LayoutPresetF10":{"FullscreenMode":"Off","ShowLibraryAndFavorites":true,"ShowWaveform":true,"ShowAnalyzerArea":true,"AnalyzerDock":"Bottom","ShowLevelMeter":true}}""",
            AppSettingsJsonContext.Default.AppSettings);
        Assert.True(legacy!.ResolvedLayoutPreset(WindowLayoutSlot.F10).ResolvedShowStatusBar(WindowLayoutSlot.F10));
        Assert.False(
            WindowLayoutPreset.CreateDefault(WindowLayoutSlot.F9).ResolvedShowStatusBar(WindowLayoutSlot.F9));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ResolveFullscreenOn_ToggleFlipsWhenSameSlot(bool currentlyOn, bool expectedWhenToggle)
    {
        var toggle = new WindowLayoutPreset
        {
            FullscreenMode = "Toggle",
            FullscreenToggleStartsOn = false,
        };
        Assert.Equal(
            expectedWhenToggle,
            WindowLayoutPresetLogic.ResolveFullscreenOn(toggle, sameSlot: true, currentlyOn));
    }

    [Fact]
    public void ResolveFullscreenOn_ToggleShortcutFirstVisitFlipsFromStart()
    {
        var startOff = new WindowLayoutPreset
        {
            FullscreenMode = "Toggle",
            FullscreenToggleStartsOn = false,
        };
        // 起動など非ショートカットは開始オフを載せる。
        Assert.False(WindowLayoutPresetLogic.ResolveFullscreenOn(
            startOff, sameSlot: false, currentlyFullscreenOn: false, userShortcut: false));
        // 初回ショートカットは開始オフからトグル → オン（無変化にしない）。
        Assert.True(WindowLayoutPresetLogic.ResolveFullscreenOn(
            startOff, sameSlot: false, currentlyFullscreenOn: false, userShortcut: true));

        var startOn = new WindowLayoutPreset
        {
            FullscreenMode = "Toggle",
            FullscreenToggleStartsOn = true,
        };
        Assert.True(WindowLayoutPresetLogic.ResolveFullscreenOn(
            startOn, sameSlot: false, currentlyFullscreenOn: false, userShortcut: false));
        Assert.False(WindowLayoutPresetLogic.ResolveFullscreenOn(
            startOn, sameSlot: false, currentlyFullscreenOn: true, userShortcut: true));
    }

    [Fact]
    public void ResolveFullscreenOn_ToggleRemembersSessionStateAcrossSlots()
    {
        var toggle = new WindowLayoutPreset
        {
            FullscreenMode = "Toggle",
            FullscreenToggleStartsOn = true,
        };
        // 往復では記憶を優先（開始設定・ショートカット反転は見ない）。
        Assert.False(WindowLayoutPresetLogic.ResolveFullscreenOn(
            toggle,
            sameSlot: false,
            currentlyFullscreenOn: true,
            rememberedToggleFullscreenOn: false,
            userShortcut: true));
        Assert.True(WindowLayoutPresetLogic.ResolveFullscreenOn(
            toggle,
            sameSlot: false,
            currentlyFullscreenOn: false,
            rememberedToggleFullscreenOn: true,
            userShortcut: true));
        // 同一スロットでは従来どおり反転。
        Assert.False(WindowLayoutPresetLogic.ResolveFullscreenOn(
            toggle, sameSlot: true, currentlyFullscreenOn: true, rememberedToggleFullscreenOn: true));
    }

    [Fact]
    public void ResolveFullscreenOn_FixedModesKeepOrInitial()
    {
        var on = new WindowLayoutPreset { FullscreenMode = "On" };
        Assert.True(WindowLayoutPresetLogic.ResolveFullscreenOn(on, sameSlot: false, currentlyFullscreenOn: false));
        Assert.True(WindowLayoutPresetLogic.ResolveFullscreenOn(on, sameSlot: true, currentlyFullscreenOn: true));
        Assert.False(WindowLayoutPresetLogic.ResolveFullscreenOn(on, sameSlot: true, currentlyFullscreenOn: false));

        var off = new WindowLayoutPreset { FullscreenMode = "Off" };
        Assert.False(WindowLayoutPresetLogic.ResolveFullscreenOn(off, sameSlot: false, currentlyFullscreenOn: true));
        Assert.True(WindowLayoutPresetLogic.ResolveFullscreenOn(off, sameSlot: true, currentlyFullscreenOn: true));

        // 固定モードは記憶があっても Initial を使う。
        Assert.True(WindowLayoutPresetLogic.ResolveFullscreenOn(
            on, sameSlot: false, currentlyFullscreenOn: false, rememberedToggleFullscreenOn: false));
    }

    [Fact]
    public void PlayerSlots_KeepPlaylistWorkspace_SidePanesFixedPerSlot()
    {
        Assert.True(WindowLayoutPreset.IsPlayerLayoutSlot(WindowLayoutSlot.F9));
        Assert.False(WindowLayoutPreset.CreateDefault(WindowLayoutSlot.F9)
            .ResolvedShowLibraryAndFavorites(WindowLayoutSlot.F9));

        Assert.True(WindowLayoutPreset.IsPlayerLayoutSlot(WindowLayoutSlot.F10));
        Assert.True(WindowLayoutPreset.CreateDefault(WindowLayoutSlot.F10)
            .ResolvedShowLibraryAndFavorites(WindowLayoutSlot.F10));

        Assert.False(WindowLayoutPreset.IsPlayerLayoutSlot(WindowLayoutSlot.F11));
        Assert.False(WindowLayoutPreset.IsPlayerLayoutSlot(WindowLayoutSlot.F12));
    }

    [Fact]
    public void PlacementKind_ToggleOffUsesWindowedSlot()
    {
        Assert.Equal("Analyzer", WindowLayoutPresetLogic.PlacementKindName(WindowLayoutSlot.F12, false));
        Assert.Equal("AnalyzerFullscreen", WindowLayoutPresetLogic.PlacementKindName(WindowLayoutSlot.F12, true));
        Assert.Equal("Waveform", WindowLayoutPresetLogic.PlacementKindName(WindowLayoutSlot.F11, false));
        Assert.Equal("WaveformFullscreen", WindowLayoutPresetLogic.PlacementKindName(WindowLayoutSlot.F11, true));
        Assert.Equal("Player", WindowLayoutPresetLogic.PlacementKindName(WindowLayoutSlot.F10, false));
        Assert.Equal("MinimalPlayer", WindowLayoutPresetLogic.PlacementKindName(WindowLayoutSlot.F9, false));
    }
}
