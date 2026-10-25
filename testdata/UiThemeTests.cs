using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class UiThemeTests
{
    [Fact]
    public void ColorFor_ReturnsDarkPaletteValues()
    {
        Assert.Equal(
            System.Windows.Media.Color.FromRgb(0xEB, 0xEB, 0xEB),
            UiThemePalette.ColorFor(UiTheme.Dark, "PrimaryForeBrush"));
        Assert.Equal(
            System.Windows.Media.Color.FromRgb(0x96, 0x96, 0x96),
            UiThemePalette.ColorFor(UiTheme.Dark, "MutedForeBrush"));
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
    }

    [Fact]
    public void PlayerComboFill_KeepsSearchTextReadable()
    {
        var surface = System.Windows.Media.Color.FromRgb(0x1E, 0x1E, 0x1E);
        var fill = Composite(UiThemePalette.ColorFor(UiTheme.Dark, "PlayerComboFillBrush"), surface);
        var hover = Composite(UiThemePalette.ColorFor(UiTheme.Dark, "PlayerComboHoverFillBrush"), surface);
        var fore = UiThemePalette.ColorFor(UiTheme.Dark, "PrimaryForeBrush");
        var muted = UiThemePalette.ColorFor(UiTheme.Dark, "MutedForeBrush");
        Assert.True(Contrast(fore, fill) >= 4.5, "typed on fill");
        Assert.True(Contrast(fore, hover) >= 4.5, "typed on hover");
        Assert.True(Contrast(muted, fill) >= 3, "hint on fill");
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
