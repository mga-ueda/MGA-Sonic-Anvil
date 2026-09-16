using System.Windows;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class ConfirmChoiceWindowTests
{
    [Fact]
    public void UsesAppButtons_OnlyYesNoVariants()
    {
        Assert.True(ConfirmChoiceWindow.UsesAppButtons(MessageBoxButton.YesNo));
        Assert.True(ConfirmChoiceWindow.UsesAppButtons(MessageBoxButton.YesNoCancel));
        Assert.False(ConfirmChoiceWindow.UsesAppButtons(MessageBoxButton.OK));
        Assert.False(ConfirmChoiceWindow.UsesAppButtons(MessageBoxButton.OKCancel));
    }

    [Fact]
    public void DismissResult_MatchesButtonSet()
    {
        Assert.Equal(MessageBoxResult.Cancel, ConfirmChoiceWindow.DismissResult(MessageBoxButton.YesNoCancel));
        Assert.Equal(MessageBoxResult.No, ConfirmChoiceWindow.DismissResult(MessageBoxButton.YesNo));
    }

    [Theory]
    [InlineData(MessageBoxResult.None, MessageBoxButton.YesNo, true, false, false)]
    [InlineData(MessageBoxResult.Yes, MessageBoxButton.YesNoCancel, true, false, false)]
    [InlineData(MessageBoxResult.No, MessageBoxButton.YesNo, false, true, false)]
    [InlineData(MessageBoxResult.Cancel, MessageBoxButton.YesNoCancel, false, false, true)]
    [InlineData(MessageBoxResult.Cancel, MessageBoxButton.YesNo, true, false, false)]
    public void IsDefaultChoice_PrefersExplicitThenYes(
        MessageBoxResult defaultResult,
        MessageBoxButton buttons,
        bool yes,
        bool no,
        bool cancel)
    {
        Assert.Equal(yes, ConfirmChoiceWindow.IsDefaultChoice(MessageBoxResult.Yes, defaultResult, buttons));
        Assert.Equal(no, ConfirmChoiceWindow.IsDefaultChoice(MessageBoxResult.No, defaultResult, buttons));
        Assert.Equal(cancel, ConfirmChoiceWindow.IsDefaultChoice(MessageBoxResult.Cancel, defaultResult, buttons));
    }

    [Fact]
    public void FileActionConfirmStrings_IncludeNameInBothLanguages()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.Japanese);
            Assert.Contains("tone.wav", UiStrings.ConfirmDeleteFile("tone.wav"));
            Assert.Contains("ディスク", UiStrings.ConfirmDeleteFile("tone.wav"));
            Assert.Contains("untitled", UiStrings.ConfirmDeleteUntitled("untitled"));
            Assert.Contains("tone.wav", UiStrings.ConfirmDuplicateFile("tone.wav"));
            Assert.Contains("tone 2.wav", UiStrings.ConfirmDuplicateFileAs("tone.wav", "tone 2.wav"));
            Assert.Contains("隣", UiStrings.ConfirmDuplicateFileAs("tone.wav", "tone 2.wav"));
            UiStrings.SetLanguage(UiLanguage.English);
            Assert.Contains("tone.wav", UiStrings.ConfirmDeleteFile("tone.wav"));
            Assert.Contains("disk", UiStrings.ConfirmDeleteFile("tone.wav"), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("untitled", UiStrings.ConfirmDeleteUntitled("untitled"));
            Assert.Contains("tone.wav", UiStrings.ConfirmDuplicateFile("tone.wav"));
            Assert.Contains("tone 2.wav", UiStrings.ConfirmDuplicateFileAs("tone.wav", "tone 2.wav"));
            Assert.Contains("next tab", UiStrings.ConfirmDuplicateFileAs("tone.wav", "tone 2.wav"), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }

    [Fact]
    public void YesNoCancelLabels_StayLocalized()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.Japanese);
            Assert.Equal("はい", UiStrings.ButtonYes);
            Assert.Equal("いいえ", UiStrings.ButtonNo);
            Assert.Equal("キャンセル", UiStrings.ButtonYesNoCancel);
            UiStrings.SetLanguage(UiLanguage.English);
            Assert.Equal("Yes", UiStrings.ButtonYes);
            Assert.Equal("No", UiStrings.ButtonNo);
            Assert.Equal("Cancel", UiStrings.ButtonYesNoCancel);
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }
}
