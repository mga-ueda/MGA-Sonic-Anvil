using System.Windows.Input;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class AppDialogKeysTests
{
    [Theory]
    [InlineData(Key.Escape, ModifierKeys.None, true)]
    [InlineData(Key.Escape, ModifierKeys.Shift, true)]
    [InlineData(Key.Escape, ModifierKeys.Control, false)]
    [InlineData(Key.Escape, ModifierKeys.Alt, false)]
    [InlineData(Key.Enter, ModifierKeys.None, false)]
    public void IsEscape_IgnoresCtrlAltWin(Key key, ModifierKeys modifiers, bool expected)
    {
        Assert.Equal(expected, AppDialogKeys.IsEscape(key, modifiers));
    }

    [Theory]
    [InlineData(Key.O, ModifierKeys.Alt, Key.O, true)]
    [InlineData(Key.C, ModifierKeys.Alt, Key.C, true)]
    [InlineData(Key.O, ModifierKeys.Alt | ModifierKeys.Shift, Key.O, true)]
    [InlineData(Key.O, ModifierKeys.Alt | ModifierKeys.Control, Key.O, false)]
    [InlineData(Key.O, ModifierKeys.None, Key.O, false)]
    [InlineData(Key.C, ModifierKeys.Alt, Key.O, false)]
    public void IsAltLetter_RequiresAltWithoutCtrlWin(
        Key key,
        ModifierKeys modifiers,
        Key letter,
        bool expected)
    {
        Assert.Equal(expected, AppDialogKeys.IsAltLetter(key, modifiers, letter));
    }

    [Fact]
    public void ShouldFocusDefaultButton_WhenNoInputs()
    {
        Assert.True(AppDialogKeys.ShouldFocusDefaultButton(hasInputTabStop: false));
        Assert.False(AppDialogKeys.ShouldFocusDefaultButton(hasInputTabStop: true));
    }
}
