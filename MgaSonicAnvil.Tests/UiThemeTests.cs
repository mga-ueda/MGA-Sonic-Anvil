using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class UiThemeTests
{
    [Theory]
    [InlineData(null, "Auto")]
    [InlineData("", "Auto")]
    [InlineData("auto", "Auto")]
    [InlineData("dark", "Dark")]
    [InlineData("Dark", "Dark")]
    [InlineData("light", "Light")]
    [InlineData("Light", "Light")]
    [InlineData("unknown", "Auto")]
    public void ParseChoice_ReadsStoredValues(string? stored, string expected)
    {
        Assert.Equal(expected, UiThemes.ParseChoice(stored).ToString());
    }

    [Fact]
    public void Resolve_Auto_UsesOsTheme()
    {
        Assert.Equal(UiTheme.Light, UiThemes.Resolve(UiThemeChoice.Auto, osLight: true));
        Assert.Equal(UiTheme.Dark, UiThemes.Resolve(UiThemeChoice.Auto, osLight: false));
    }

    [Fact]
    public void Resolve_Explicit_IgnoresOs()
    {
        Assert.Equal(UiTheme.Dark, UiThemes.Resolve(UiThemeChoice.Dark, osLight: true));
        Assert.Equal(UiTheme.Light, UiThemes.Resolve(UiThemeChoice.Light, osLight: false));
    }

    [Fact]
    public void ToggledChoice_FlipsResolvedTheme()
    {
        Assert.Equal(UiThemeChoice.Light, UiThemes.ToggledChoice(UiTheme.Dark));
        Assert.Equal(UiThemeChoice.Dark, UiThemes.ToggledChoice(UiTheme.Light));
    }

    [Fact]
    public void ToStoredValue_RoundTripsChoices()
    {
        Assert.Equal("auto", UiThemes.ToStoredValue(UiThemeChoice.Auto));
        Assert.Equal("dark", UiThemes.ToStoredValue(UiThemeChoice.Dark));
        Assert.Equal("light", UiThemes.ToStoredValue(UiThemeChoice.Light));
    }

    [Fact]
    public void DefaultFor_LightUsesPaletteNotDarkXaml()
    {
        var lightWave = UiColors.DefaultFor(UiTheme.Light, "WaveformBackBrush");
        var lightFill = UiColors.DefaultFor(UiTheme.Light, "WaveFillBrush");
        Assert.Equal(System.Windows.Media.Color.FromRgb(0xFA, 0xFA, 0xFA), lightWave);
        Assert.Equal(System.Windows.Media.Color.FromRgb(0x40, 0x52, 0x73), lightFill);
        Assert.NotEqual(System.Windows.Media.Color.FromRgb(0x26, 0x26, 0x26), lightWave);
        Assert.NotEqual(System.Windows.Media.Color.FromRgb(0xC6, 0xD9, 0xFF), lightFill);
        Assert.Equal(
            UiThemePalette.ColorFor(UiTheme.Light, "MouseGuideBrush").A,
            UiColors.DefaultFor(UiTheme.Light, "MouseGuideBrush").A);
    }

    [Fact]
    public void Palette_CoversThemeableKeysAndSkipsAccents()
    {
        Assert.True(UiThemePalette.IsThemeable("SurfaceBackBrush"));
        Assert.True(UiThemePalette.IsThemeable("PrimaryForeBrush"));
        Assert.True(UiThemePalette.IsThemeable("WaveFillBrush"));
        Assert.True(UiThemePalette.IsThemeable("AccentCyanBrush"));
        Assert.False(UiThemePalette.IsThemeable("PlayheadBrush"));
        Assert.False(UiThemePalette.IsThemeable("MarkerBrush"));
        Assert.False(UiThemePalette.IsThemeable("DirtyAccentBrush"));
        Assert.False(UiThemePalette.IsThemeable("SeekExitBrush"));
        Assert.True(UiThemePalette.IsThemeable("WaapiToggleOnBackBrush"));
        Assert.True(UiThemePalette.IsThemeable("ExportButtonBackBrush"));
        Assert.True(UiThemePalette.IsThemeable("ExportButtonFillBrush"));
        Assert.True(UiThemePalette.IsThemeable("LevelMeterTrackBackBrush"));
        Assert.True(UiThemePalette.IsThemeable("PlayerLevelMeterTickBrush"));
        Assert.True(UiThemePalette.IsThemeable("KeepTargetLockForeBrush"));
        Assert.True(UiThemePalette.IsThemeable("ActionLinkHoverForeBrush"));
        Assert.True(UiThemePalette.IsThemeable("SampleLoopTimeLabelForeBrush"));
        Assert.True(UiThemePalette.IsThemeable("RegionTimeLabelForeBrush"));
        Assert.True(UiThemePalette.IsThemeable("MarkerLabelForeBrush"));
        Assert.True(UiThemePalette.IsThemeable("MenuHighlightBackBrush"));
        Assert.True(UiThemePalette.IsThemeable("MenuDisabledForeBrush"));

        foreach (var key in UiThemePalette.ThemeableKeys)
        {
            var light = UiThemePalette.ColorFor(UiTheme.Light, key);
            Assert.True(light.A > 0, key);
        }
    }

    [Fact]
    public void LightPalette_UsesWhiteRegionMarkerLoopLabels()
    {
        var white = System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF);
        Assert.Equal(white, UiThemePalette.ColorFor(UiTheme.Light, "SampleLoopTimeLabelForeBrush"));
        Assert.Equal(white, UiThemePalette.ColorFor(UiTheme.Light, "RegionTimeLabelForeBrush"));
        Assert.Equal(white, UiThemePalette.ColorFor(UiTheme.Light, "MarkerLabelForeBrush"));
    }

    [Fact]
    public void LightPalette_KeepsTextDarkerThanSurface()
    {
        var back = UiThemePalette.ColorFor(UiTheme.Light, "SurfaceBackBrush");
        var fore = UiThemePalette.ColorFor(UiTheme.Light, "PrimaryForeBrush");
        Assert.True(RelativeLuma(fore) < RelativeLuma(back));
    }

    [Fact]
    public void LightPalette_MeetsContrastFloors()
    {
        var surface = UiThemePalette.ColorFor(UiTheme.Light, "SurfaceBackBrush");
        var chrome = UiThemePalette.ColorFor(UiTheme.Light, "TransportBackBrush");
        var wave = UiThemePalette.ColorFor(UiTheme.Light, "WaveformBackBrush");
        var track = UiThemePalette.ColorFor(UiTheme.Light, "LevelMeterTrackBackBrush");
        Assert.True(Contrast(UiThemePalette.ColorFor(UiTheme.Light, "AccentCyanBrush"), surface) >= 3);
        Assert.Equal(
            UiThemePalette.ColorFor(UiTheme.Light, "AccentCyanBrush"),
            UiThemePalette.ColorFor(UiTheme.Light, "ActionLinkForeBrush"));
        Assert.NotEqual(
            UiThemePalette.ColorFor(UiTheme.Light, "ActionLinkForeBrush"),
            UiThemePalette.ColorFor(UiTheme.Light, "ActionLinkHoverForeBrush"));
        Assert.True(Contrast(UiThemePalette.ColorFor(UiTheme.Light, "PrimaryForeBrush"), surface) >= 7);
        Assert.True(Contrast(UiThemePalette.ColorFor(UiTheme.Light, "MutedForeBrush"), surface) >= 7);
        Assert.True(Contrast(UiThemePalette.ColorFor(UiTheme.Light, "StatusBarDetailForeBrush"), chrome) >= 7);
        Assert.True(Contrast(UiThemePalette.ColorFor(UiTheme.Light, "TransportForeBrush"), chrome) >= 4.5);
        Assert.True(Contrast(UiThemePalette.ColorFor(UiTheme.Light, "ExportButtonForeBrush"), UiThemePalette.ColorFor(UiTheme.Light, "ExportButtonFillBrush")) >= 4.5);
        var lockFore = UiThemePalette.ColorFor(UiTheme.Light, "KeepTargetLockForeBrush");
        var lockHover = UiThemePalette.ColorFor(UiTheme.Light, "KeepTargetLockHoverForeBrush");
        Assert.True(lockFore.G < lockFore.R);
        Assert.True(lockFore.B < 0x40);
        Assert.True(Contrast(lockFore, chrome) >= 2.5);
        Assert.True(RelativeLuma(lockHover) > RelativeLuma(lockFore));
        Assert.True(Contrast(Composite(UiThemePalette.ColorFor(UiTheme.Light, "WaveZeroLineBrush"), wave), wave) >= 3);
        Assert.True(UiThemePalette.ColorFor(UiTheme.Light, "LoopRangeFillBrush").A < 0x1A);
        Assert.True(
            Contrast(Composite(UiThemePalette.ColorFor(UiTheme.Light, "LoopRangeFillBrush"), wave), wave) < 1.15);
        Assert.True(
            RelativeLuma(Composite(UiThemePalette.ColorFor(UiTheme.Light, "LevelMeterTickBrush"), track))
            >= RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "VectorScopeGridBrush")) - 0.02);
        Assert.Equal(
            UiThemePalette.ColorFor(UiTheme.Light, "MutedForeBrush"),
            UiThemePalette.ColorFor(UiTheme.Light, "DbScaleForeBrush"));
        Assert.True(
            RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "SpectrogramScaleForeBrush"))
            > RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "MutedForeBrush")));
        Assert.True(
            RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "SurroundHullStrokeBrush"))
            > RelativeLuma(System.Windows.Media.Color.FromRgb(0x5A, 0x5A, 0x5F)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlayerComboFill_KeepsSearchTextReadable(bool light)
    {
        var theme = light ? UiTheme.Light : UiTheme.Dark;
        var surface = theme == UiTheme.Light
            ? UiThemePalette.ColorFor(theme, "SurfaceBackBrush")
            : System.Windows.Media.Color.FromRgb(0x1E, 0x1E, 0x1E);
        var fill = Composite(UiThemePalette.ColorFor(theme, "PlayerComboFillBrush"), surface);
        var hover = Composite(UiThemePalette.ColorFor(theme, "PlayerComboHoverFillBrush"), surface);
        var fore = UiThemePalette.ColorFor(theme, "PrimaryForeBrush");
        var muted = UiThemePalette.ColorFor(theme, "MutedForeBrush");
        Assert.True(Contrast(fore, fill) >= 4.5, $"{theme} typed on fill");
        Assert.True(Contrast(fore, hover) >= 4.5, $"{theme} typed on hover");
        Assert.True(Contrast(muted, fill) >= 3, $"{theme} hint on fill");
    }

    [Fact]
    public void LightPalette_KeepsBrighterAsEnabled()
    {
        Assert.True(RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "TransportForeBrush"))
            > RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "TransportDisabledForeBrush")));
        Assert.True(RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "TransportHoverBackBrush"))
            > RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "TransportBackBrush")));
        var surface = UiThemePalette.ColorFor(UiTheme.Light, "SurfaceBackBrush");
        var menuHighlight = UiThemePalette.ColorFor(UiTheme.Light, "MenuHighlightBackBrush");
        Assert.True(RelativeLuma(surface) - RelativeLuma(menuHighlight) >= 0.08);
        Assert.True(Contrast(UiThemePalette.ColorFor(UiTheme.Light, "PrimaryForeBrush"), menuHighlight) >= 7);
        Assert.True(RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "TransportPressedBackBrush"))
            >= RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "TransportHoverBackBrush")));
        Assert.True(RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "HistoryStripHoverBackBrush"))
            > RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "HistoryStripBackBrush")));
        var stripBack = UiThemePalette.ColorFor(UiTheme.Light, "HistoryStripBackBrush");
        var chrome = UiThemePalette.ColorFor(UiTheme.Light, "TransportBackBrush");
        Assert.True(RelativeLuma(stripBack) < RelativeLuma(chrome));
        Assert.True(RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "WaapiToggleOffHoverBackBrush"))
            > RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "WaapiToggleOffBackBrush")));
        Assert.True(RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "ChromeDimBrush"))
            > RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "ChromeMidBrush")));
        Assert.True(RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "ExportButtonHoverFillBrush"))
            > RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "ExportButtonFillBrush")));
        Assert.True(RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "ExportButtonHoverBackBrush"))
            > RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "ExportButtonBackBrush")));
        Assert.True(RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "ClearButtonHoverBackBrush"))
            > RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "ClearButtonBackBrush")));
        Assert.True(RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "WaapiToggleOnHoverBackBrush"))
            > RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "WaapiToggleOnBackBrush")));
        Assert.True(RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "ScrollThumbHoverBackBrush"))
            > RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "ScrollThumbBackBrush")));
        Assert.True(RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "ScrollThumbPressedBackBrush"))
            >= RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "ScrollThumbHoverBackBrush")));
    }

    [Fact]
    public void LightPalette_AvoidsPureBlack()
    {
        foreach (var key in UiThemePalette.ThemeableKeys)
        {
            var color = UiThemePalette.ColorFor(UiTheme.Light, key);
            Assert.False(color.R == 0 && color.G == 0 && color.B == 0, key);
        }

        var wave = UiThemePalette.ColorFor(UiTheme.Light, "WaveformBackBrush");
        var guide = Composite(UiThemePalette.ColorFor(UiTheme.Light, "MouseGuideBrush"), wave);
        Assert.True(RelativeLuma(guide) > 0.28);
    }

    [Fact]
    public void LightPalette_LiftsMeterWellsAndButtonFills()
    {
        Assert.True(RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "ScrollThumbBackBrush")) > 0.5);
        Assert.True(RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "ScrollThumbGripBrush")) > 0.25);
        Assert.True(RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "LevelMeterTrackBackBrush")) > 0.8);
        var waveBack = UiThemePalette.ColorFor(UiTheme.Light, "WaveformBackBrush");
        Assert.True(
            RelativeLuma(Composite(UiThemePalette.ColorFor(UiTheme.Light, "OverviewOutsideFillBrush"), waveBack))
            > RelativeLuma(Composite(System.Windows.Media.Color.FromArgb(150, 0, 0, 0), waveBack)));
        Assert.True(RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "HistoryStripBackBrush")) > 0.6);
        Assert.True(RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "LevelMeterTrackBorderBrush")) > 0.5);
        Assert.Equal(
            UiThemePalette.ColorFor(UiTheme.Dark, "PlayerLevelMeterTickBrush"),
            UiThemePalette.ColorFor(UiTheme.Light, "PlayerLevelMeterTickBrush"));
        Assert.Equal(
            System.Windows.Media.Color.FromRgb(0xC8, 0xC8, 0xC8),
            UiThemePalette.ColorFor(UiTheme.Light, "LevelMeterHoldBorderBrush"));
        Assert.True(RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "LevelMeterClipOffBrush")) > 0.8);
        Assert.True(RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "ExportButtonFillBrush")) > 0.7);
        Assert.True(RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "ClearButtonFillBrush")) > 0.7);
        Assert.True(RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "StatusExportButtonFillBrush")) > 0.7);
        Assert.True(RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "ExportButtonBackBrush")) > 0.25);
        Assert.True(RelativeLuma(UiThemePalette.ColorFor(UiTheme.Light, "ClearButtonBackBrush")) > 0.2);
        Assert.True(UiThemePalette.ColorFor(UiTheme.Light, "RegionWaveFillLoopBrush").B
            > UiThemePalette.ColorFor(UiTheme.Light, "RegionWaveFillLoopBrush").R);
        Assert.True(UiThemePalette.ColorFor(UiTheme.Light, "RegionWaveFillExitBrush").R
            > UiThemePalette.ColorFor(UiTheme.Light, "RegionWaveFillExitBrush").B);
        var anacrusis = UiThemePalette.ColorFor(UiTheme.Light, "RegionWaveFillAnacrusisBrush");
        Assert.True(anacrusis.G > anacrusis.R);
        Assert.True(anacrusis.G > anacrusis.B);
        Assert.True(anacrusis.A < 0x80);
        Assert.True(UiThemePalette.ColorFor(UiTheme.Light, "RegionWaveFillLoopBrush").A < 0x80);
        Assert.True(UiThemePalette.ColorFor(UiTheme.Light, "RegionWaveFillExitBrush").A < 0x80);
    }

    [Fact]
    public void LightSetting_PlayerPaintsDarkTextAndSurface()
    {
        void Body()
        {
            var app = Application.Current!;
            var previous = UiThemeService.Current;
            var fore = app.Resources.Contains("PrimaryForeBrush") ? app.Resources["PrimaryForeBrush"] : null;
            var back = app.Resources.Contains("SurfaceBackBrush") ? app.Resources["SurfaceBackBrush"] : null;
            var muted = app.Resources.Contains("MutedForeBrush") ? app.Resources["MutedForeBrush"] : null;
            try
            {
                app.Resources["PrimaryForeBrush"] = new SolidColorBrush(Color.FromRgb(0xEB, 0xEB, 0xEB));
                app.Resources["SurfaceBackBrush"] = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
                app.Resources["MutedForeBrush"] = new SolidColorBrush(Color.FromRgb(0x96, 0x96, 0x96));
                UiColors.Load();
                UiThemeService.Apply(UiTheme.Light, force: true);

                Assert.Equal(Color.FromRgb(0x1A, 0x1A, 0x1A), Brush("PrimaryForeBrush"));
                Assert.Equal(UiTheme.Light, UiThemeService.Painted);

                UiThemeService.SetPlayerForcesDark(true);
                Assert.Equal(UiTheme.Light, UiThemeService.Current);
                Assert.Equal(UiTheme.Dark, UiThemeService.Painted);
                Assert.Equal(Color.FromRgb(0xEB, 0xEB, 0xEB), Brush("PrimaryForeBrush"));
                Assert.Equal(Color.FromRgb(0x96, 0x96, 0x96), Brush("MutedForeBrush"));
                Assert.Equal(Color.FromRgb(0x1E, 0x1E, 0x1E), Brush("SurfaceBackBrush"));

                UiThemeService.SetPlayerForcesDark(false);
                Assert.Equal(UiTheme.Light, UiThemeService.Painted);
                Assert.Equal(Color.FromRgb(0x1A, 0x1A, 0x1A), Brush("PrimaryForeBrush"));

                Color Brush(string key) => ((SolidColorBrush)app.Resources[key]).Color;
            }
            finally
            {
                UiThemeService.SetPlayerForcesDark(false);
                UiThemeService.Apply(previous, force: true);
                Restore(app, "PrimaryForeBrush", fore);
                Restore(app, "SurfaceBackBrush", back);
                Restore(app, "MutedForeBrush", muted);
            }
        }

        var app = Application.Current;
        var dispatcher = app?.Dispatcher;
        if (dispatcher is not null
            && dispatcher.Thread.IsAlive
            && !dispatcher.HasShutdownStarted
            && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(Body);
            return;
        }

        RunSta(() =>
        {
            if (Application.Current is null)
            {
                _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            }

            Body();
        });
    }

    private static void Restore(Application app, string key, object? saved)
    {
        if (saved is null)
        {
            app.Resources.Remove(key);
            return;
        }

        app.Resources[key] = saved;
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    private static double Contrast(System.Windows.Media.Color a, System.Windows.Media.Color b)
    {
        var l1 = RelativeLuma(a);
        var l2 = RelativeLuma(b);
        var hi = Math.Max(l1, l2);
        var lo = Math.Min(l1, l2);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static double RelativeLuma(System.Windows.Media.Color color)
    {
        static double Lin(byte channel)
        {
            var s = channel / 255d;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Lin(color.R)) + (0.7152 * Lin(color.G)) + (0.0722 * Lin(color.B));
    }

    private static System.Windows.Media.Color Composite(System.Windows.Media.Color over, System.Windows.Media.Color under)
    {
        var a = over.A / 255d;
        return System.Windows.Media.Color.FromRgb(
            (byte)Math.Round((over.R * a) + (under.R * (1 - a))),
            (byte)Math.Round((over.G * a) + (under.G * (1 - a))),
            (byte)Math.Round((over.B * a) + (under.B * (1 - a))));
    }
}
