using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibraryVideoWindowScaleTests
{
    [Fact]
    public void StepKey_UsesTwentyFivePercent()
    {
        Assert.Equal(0.25, LibraryVideoWindowScale.KeyStepAmount, 6);
        var latched = false;
        var scale = LibraryVideoWindowScale.StepKey(1.0, direction: 1, isRepeat: false, ref latched);
        Assert.Equal(1.25, scale, 6);
        scale = LibraryVideoWindowScale.StepKey(scale, direction: 1, isRepeat: false, ref latched);
        Assert.Equal(1.5, scale, 6);
        scale = LibraryVideoWindowScale.StepKey(scale, direction: -1, isRepeat: false, ref latched);
        Assert.Equal(1.25, scale, 6);
    }

    [Fact]
    public void StepKey_FromMouseResizeMidpoint_SnapsToNearestKeyStep()
    {
        var latched = false;
        // 枠ドラッグで 113% 相当 → + は 125%、- は 100%
        Assert.Equal(
            1.25,
            LibraryVideoWindowScale.StepKey(1.13, direction: 1, isRepeat: false, ref latched),
            6);
        latched = false;
        Assert.Equal(
            1.0,
            LibraryVideoWindowScale.StepKey(1.13, direction: -1, isRepeat: false, ref latched),
            6);

        latched = false;
        Assert.Equal(
            1.0,
            LibraryVideoWindowScale.StepKey(0.87, direction: 1, isRepeat: false, ref latched),
            6);
        latched = false;
        Assert.Equal(
            0.75,
            LibraryVideoWindowScale.StepKey(0.87, direction: -1, isRepeat: false, ref latched),
            6);
    }

    [Fact]
    public void NextKeyStep_AlwaysLandsOnTwentyFivePercentGrid()
    {
        Assert.True(LibraryVideoWindowScale.IsKeyStep(
            LibraryVideoWindowScale.NextKeyStep(1.13, 1)));
        Assert.True(LibraryVideoWindowScale.IsKeyStep(
            LibraryVideoWindowScale.NextKeyStep(1.13, -1)));
        Assert.True(LibraryVideoWindowScale.IsKeyStep(
            LibraryVideoWindowScale.NextKeyStep(1.0, 1)));
        Assert.True(LibraryVideoWindowScale.IsKeyStep(
            LibraryVideoWindowScale.NextKeyStep(1.25, -1)));
    }

    [Fact]
    public void StepKey_StopsAtNativeOnCross_AndHoldsOnRepeat()
    {
        var latched = false;
        var scale = 0.8;

        scale = LibraryVideoWindowScale.StepKey(scale, direction: 1, isRepeat: false, ref latched);
        Assert.Equal(LibraryVideoWindowScale.Native, scale, 6);
        Assert.True(latched);

        var held = LibraryVideoWindowScale.StepKey(scale, direction: 1, isRepeat: true, ref latched);
        Assert.Equal(LibraryVideoWindowScale.Native, held, 6);

        latched = false;
        scale = LibraryVideoWindowScale.StepKey(scale, direction: 1, isRepeat: false, ref latched);
        Assert.Equal(1.25, scale, 6);
    }

    [Fact]
    public void StepKey_ClampsToMinAndMax()
    {
        var latched = false;
        Assert.Equal(
            LibraryVideoWindowScale.Min,
            LibraryVideoWindowScale.StepKey(LibraryVideoWindowScale.Min, -1, isRepeat: false, ref latched),
            6);
        Assert.Equal(
            LibraryVideoWindowScale.Max,
            LibraryVideoWindowScale.StepKey(LibraryVideoWindowScale.Max, 1, isRepeat: false, ref latched),
            6);
    }

    [Fact]
    public void FromClientSize_UsesWidthRatio()
    {
        Assert.Equal(
            LibraryVideoWindowScale.Native,
            LibraryVideoWindowScale.FromClientSize(1920, 1080, 1920, 1080),
            6);
        Assert.Equal(
            0.5,
            LibraryVideoWindowScale.FromClientSize(960, 540, 1920, 1080),
            6);
    }

    [Fact]
    public void RememberAfterApply_KeepsKeyStep_WhenSlightlyLarger()
    {
        // MinWidth などで 25% 指定が 47% 相当に膨らんでも、キー格子の意図を残す
        Assert.Equal(
            0.25,
            LibraryVideoWindowScale.RememberAfterApply(0.25, 0.47),
            6);
        Assert.Equal(
            0.5,
            LibraryVideoWindowScale.RememberAfterApply(0.5, 0.51),
            6);
    }

    [Fact]
    public void RememberAfterApply_UsesEffective_WhenWorkAreaShrinks()
    {
        Assert.Equal(
            0.4,
            LibraryVideoWindowScale.RememberAfterApply(0.75, 0.4),
            6);
    }

    [Fact]
    public void FormatPercent_KeyStepsAreExact()
    {
        Assert.Equal(25, LibraryVideoWindowScale.FormatPercent(0.25));
        Assert.Equal(50, LibraryVideoWindowScale.FormatPercent(0.5));
        Assert.Equal(100, LibraryVideoWindowScale.FormatPercent(1.0));
        Assert.Equal(125, LibraryVideoWindowScale.FormatPercent(1.25));
        Assert.Equal(48, LibraryVideoWindowScale.FormatPercent(0.48));
    }

    [Fact]
    public void Min_AllowsTwentyFivePercent()
    {
        Assert.Equal(0.25, LibraryVideoWindowScale.Min, 6);
        var latched = false;
        Assert.Equal(
            0.25,
            LibraryVideoWindowScale.StepKey(0.5, direction: -1, isRepeat: false, ref latched),
            6);
    }
}
