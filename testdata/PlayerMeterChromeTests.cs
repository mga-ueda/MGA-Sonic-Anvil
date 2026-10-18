using System.Windows.Media;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class PlayerMeterChromeTests
{
    [Fact]
    public void Grid_LeavesEditorColorUnchanged()
    {
        var dark = Color.FromRgb(0x3A, 0x3A, 0x3E);
        Assert.Equal(dark, PlayerMeterChrome.Grid(dark, player: false));
    }

    [Fact]
    public void Grid_LiftsDarkPlayerLines()
    {
        var grid = Color.FromRgb(0x3A, 0x3A, 0x3E);
        var border = Color.FromRgb(0x1A, 0x22, 0x1A);
        var liftedGrid = PlayerMeterChrome.Grid(grid, player: true);
        var liftedBorder = PlayerMeterChrome.Grid(border, player: true);
        Assert.True(Luma(liftedGrid) > Luma(grid));
        Assert.True(Luma(liftedBorder) > Luma(border));
        Assert.True(Luma(liftedGrid) < 0.55);
        Assert.True(Luma(liftedBorder) < 0.55);
    }

    [Fact]
    public void Grid_RaisesLowTickAlphaInPlayer()
    {
        var tick = Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF);
        var lifted = PlayerMeterChrome.Grid(tick, player: true);
        Assert.True(lifted.A > tick.A);
        Assert.Equal(tick.A, PlayerMeterChrome.Grid(tick, player: false).A);
    }

    [Fact]
    public void Grid_DarkensLightPlayerLines()
    {
        var light = Color.FromRgb(0xC8, 0xC8, 0xCC);
        var darkened = PlayerMeterChrome.Grid(light, player: true);
        Assert.True(Luma(darkened) < Luma(light));
        Assert.True(Luma(darkened) > 0.4);
        Assert.Equal(light.A, darkened.A);
        Assert.Equal(light, PlayerMeterChrome.Grid(light, player: false));
    }

    [Fact]
    public void Grid_PlayerKeysMapEditorBrushes()
    {
        Assert.Equal(PlayerMeterChrome.TrackBorderKey, PlayerMeterChrome.PlayerKey("LevelMeterTrackBorderBrush"));
        Assert.Equal(PlayerMeterChrome.TickKey, PlayerMeterChrome.PlayerKey("LevelMeterTickBrush"));
        Assert.Equal(PlayerMeterChrome.ScopeGridKey, PlayerMeterChrome.PlayerKey("VectorScopeGridBrush"));
        Assert.Equal(PlayerMeterChrome.HullKey, PlayerMeterChrome.PlayerKey("SurroundHullStrokeBrush"));
    }

    [Fact]
    public void PaletteDefaults_MatchBakedEditorColors()
    {
        Assert.Equal(
            PlayerMeterChrome.Grid(Color.FromRgb(0x1A, 0x22, 0x1A), player: true),
            Color.FromRgb(0x5A, 0x60, 0x5A));
        Assert.Equal(
            PlayerMeterChrome.Grid(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF), player: true),
            Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF));
        Assert.Equal(
            PlayerMeterChrome.Grid(Color.FromRgb(0x3A, 0x3A, 0x3E), player: true),
            Color.FromRgb(0x71, 0x71, 0x74));
        Assert.Equal(
            PlayerMeterChrome.Grid(Color.FromRgb(0x5A, 0x5A, 0x5F), player: true),
            Color.FromRgb(0x88, 0x88, 0x8C));

        Assert.Equal(
            UiThemePalette.ColorFor(UiTheme.Dark, PlayerMeterChrome.TrackBorderKey),
            UiThemePalette.ColorFor(UiTheme.Light, PlayerMeterChrome.TrackBorderKey));
        Assert.Equal(
            UiThemePalette.ColorFor(UiTheme.Dark, PlayerMeterChrome.TickKey),
            UiThemePalette.ColorFor(UiTheme.Light, PlayerMeterChrome.TickKey));
        Assert.Equal(
            UiThemePalette.ColorFor(UiTheme.Dark, PlayerMeterChrome.ScopeGridKey),
            UiThemePalette.ColorFor(UiTheme.Light, PlayerMeterChrome.ScopeGridKey));
        Assert.Equal(
            UiThemePalette.ColorFor(UiTheme.Dark, PlayerMeterChrome.HullKey),
            UiThemePalette.ColorFor(UiTheme.Light, PlayerMeterChrome.HullKey));
    }

    private static double Luma(Color color) =>
        ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255d;
}
