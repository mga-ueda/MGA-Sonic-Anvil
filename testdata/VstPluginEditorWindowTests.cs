using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class VstPluginEditorWindowTests
{
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int VkW = 0x57;
    private const int VkA = 0x41;

    [Fact]
    public void IsPluginWindowCloseChord_TrueOnlyForPlainCtrlW()
    {
        Assert.True(VstPluginEditorWindow.IsPluginWindowCloseChord(
            WmKeyDown, VkW, control: true, shift: false, alt: false, win: false));

        Assert.False(VstPluginEditorWindow.IsPluginWindowCloseChord(
            WmKeyUp, VkW, control: true, shift: false, alt: false, win: false));
        Assert.False(VstPluginEditorWindow.IsPluginWindowCloseChord(
            WmKeyDown, VkA, control: true, shift: false, alt: false, win: false));
        Assert.False(VstPluginEditorWindow.IsPluginWindowCloseChord(
            WmKeyDown, VkW, control: false, shift: false, alt: false, win: false));
        Assert.False(VstPluginEditorWindow.IsPluginWindowCloseChord(
            WmKeyDown, VkW, control: true, shift: true, alt: false, win: false));
        Assert.False(VstPluginEditorWindow.IsPluginWindowCloseChord(
            WmKeyDown, VkW, control: true, shift: false, alt: true, win: false));
        Assert.False(VstPluginEditorWindow.IsPluginWindowCloseChord(
            WmKeyDown, VkW, control: true, shift: false, alt: false, win: true));
    }

    [Fact]
    public void IsOwnedByWindow_FalseForNullHandles()
    {
        Assert.False(VstPluginEditorWindow.IsOwnedByWindow(IntPtr.Zero, new IntPtr(1)));
        Assert.False(VstPluginEditorWindow.IsOwnedByWindow(new IntPtr(1), IntPtr.Zero));
    }
}
