using System.Windows;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class ComboBoxFitTests
{
    [Fact]
    public void WidthFor_AddsChromeAndMargin()
    {
        var padding = new Thickness(6, 1, 6, 1);
        var border = new Thickness(1);
        Assert.Equal(
            100 + 6 + 6 + 1 + 1 + ComboBoxFit.ArrowColumnWidth + ComboBoxFit.ExtraMargin,
            ComboBoxFit.WidthFor(100, padding, border));
        Assert.Equal(
            100 + 6 + 6 + 1 + 1 + ComboBoxFit.ArrowColumnWidth + ComboBoxFit.ExtraMargin + 12,
            ComboBoxFit.WidthFor(100, padding, border, scrollBar: 12));
    }

    [Fact]
    public void ItemText_UsesToString()
    {
        Assert.Equal("Stereo (2ch)", ComboBoxFit.ItemText(new Named("Stereo (2ch)")));
        Assert.Equal(string.Empty, ComboBoxFit.ItemText(null));
        Assert.Equal("WASAPI", ComboBoxFit.ItemText("WASAPI"));
    }

    [Fact]
    public void StepTabIndex_WrapsUnlessPausedAtEnds()
    {
        Assert.Equal(1, AudioSettingsWindow.StepTabIndex(0, 4, 1));
        Assert.Equal(0, AudioSettingsWindow.StepTabIndex(3, 4, 1));
        Assert.Equal(3, AudioSettingsWindow.StepTabIndex(0, 4, -1));
        Assert.Equal(3, AudioSettingsWindow.StepTabIndex(3, 4, 1, wrap: false));
        Assert.Equal(0, AudioSettingsWindow.StepTabIndex(0, 4, -1, wrap: false));
        Assert.Equal(2, AudioSettingsWindow.StepTabIndex(3, 4, -1, wrap: false));
        Assert.Equal(0, AudioSettingsWindow.StepTabIndex(0, 1, 1));
    }

    [Fact]
    public void SettingsWindowChromeWidth_IsStableFrameOnly()
    {
        var chrome = AudioSettingsWindow.SettingsWindowChromeWidth();
        Assert.True(chrome > 0);
        Assert.Equal(chrome, AudioSettingsWindow.SettingsWindowChromeWidth());
    }

    [Fact]
    public void AdjacentIndex_WrapsUnlessPausedAtEnds()
    {
        Assert.Equal(1, DocumentTabLayout.AdjacentIndex(0, 4, 1));
        Assert.Equal(0, DocumentTabLayout.AdjacentIndex(3, 4, 1));
        Assert.Equal(3, DocumentTabLayout.AdjacentIndex(0, 4, -1));
        Assert.Equal(3, DocumentTabLayout.AdjacentIndex(3, 4, 1, wrap: false));
        Assert.Equal(0, DocumentTabLayout.AdjacentIndex(0, 4, -1, wrap: false));
        Assert.Equal(2, DocumentTabLayout.AdjacentIndex(0, 4, 1, i => i == 1));
        Assert.Equal(0, DocumentTabLayout.AdjacentIndex(0, 4, 1, _ => true));
        Assert.Equal(1, DocumentTabLayout.AdjacentIndex(3, 4, -1, i => i == 2));
        Assert.Equal(2, DocumentTabLayout.AdjacentIndex(0, 4, 1, i => i == 1, wrap: false));
        Assert.Equal(0, DocumentTabLayout.AdjacentIndex(0, 4, 1, i => i is 1 or 2 or 3, wrap: false));
    }

    [Fact]
    public void FirstVisibleIndex_SkipsVeiledTiles()
    {
        Assert.Equal(-1, DocumentTabLayout.FirstVisibleIndex(0));
        Assert.Equal(0, DocumentTabLayout.FirstVisibleIndex(4));
        Assert.Equal(2, DocumentTabLayout.FirstVisibleIndex(4, i => i < 2));
        Assert.Equal(-1, DocumentTabLayout.FirstVisibleIndex(4, _ => true));
    }

    [Theory]
    [InlineData(false, "Stereo", "5.1", false)]
    [InlineData(true, "Stereo", "Stereo", false)]
    [InlineData(true, "Stereo", "5.1", true)]
    [InlineData(true, null, "5.1", true)]
    public void ShouldConfirmSpeakerSave_OnlyWhenDirtyAndSwitching(
        bool dirty,
        string? currentId,
        string nextId,
        bool expected)
    {
        Assert.Equal(expected, AudioSettingsWindow.ShouldConfirmSpeakerSave(dirty, currentId, nextId));
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void ShouldCaptureEditorMaps_SkipsWhenLoadingOrSyncing(
        bool loadingSpeaker,
        bool syncingSpeaker,
        bool expected)
    {
        Assert.Equal(expected, AudioSettingsWindow.ShouldCaptureEditorMaps(loadingSpeaker, syncingSpeaker));
    }

    [Theory]
    [InlineData(48000, 1000, true, 48000, 1000, true, false)]
    [InlineData(48000, 1000, true, 44100, 1000, true, true)]
    [InlineData(48000, 1000, true, 48000, 2000, true, true)]
    [InlineData(48000, 1000, true, 48000, 1000, false, true)]
    public void ShouldAbandonTimeEdit_WhenDocumentIdentityChanges(
        int previousRate,
        long previousTotal,
        bool previousHasDocument,
        int sampleRate,
        long totalFrames,
        bool hasDocument,
        bool expected)
    {
        Assert.Equal(
            expected,
            StatusTimeStrip.ShouldAbandonTimeEdit(
                previousRate,
                previousTotal,
                previousHasDocument,
                sampleRate,
                totalFrames,
                hasDocument));
    }

    private sealed record Named(string Label)
    {
        public override string ToString() => Label;
    }
}
