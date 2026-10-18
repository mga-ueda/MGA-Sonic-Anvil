using System.Runtime.InteropServices;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class ImeCompositionTests
{
    [Theory]
    [InlineData("at MS.Win32.UnsafeNativeMethods+ITfContextOwnerCompositionServices.TerminateComposition")]
    [InlineData("at System.Windows.Documents.FrameworkTextComposition.CompleteCurrentComposition")]
    [InlineData("at System.Windows.Documents.TextEditor.CompleteComposition()")]
    public void HasCompositionFrame_DetectsWpfImePath(string stack) =>
        Assert.True(ImeComposition.HasCompositionFrame(stack));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("at MgaSonicAnvil.UI.WaveformView.OnMouseLeftButtonDown")]
    public void HasCompositionFrame_IgnoresUnrelatedStacks(string? stack) =>
        Assert.False(ImeComposition.HasCompositionFrame(stack));

    [Fact]
    public void IsTerminateFailure_RequiresBothTypeAndFrame()
    {
        Assert.False(ImeComposition.IsTerminateFailure(new InvalidOperationException("CompleteComposition")));
        Assert.False(ImeComposition.IsTerminateFailure(new COMException("fail")));
        Assert.False(ImeComposition.IsTerminateFailure(null));
    }

    [Fact]
    public void ShouldBlockTextEditor_OnlyWhileComposing()
    {
        Assert.False(ImeComposition.ShouldBlockTextEditor(composing: false));
        Assert.True(ImeComposition.ShouldBlockTextEditor(composing: true));
    }

    [Fact]
    public void ShouldInterceptImeMouse_OnlyWhenImeEnabled()
    {
        Assert.False(ImeComposition.ShouldInterceptImeMouse(imeEnabled: false));
        Assert.True(ImeComposition.ShouldInterceptImeMouse(imeEnabled: true));
    }
}
