using System.Windows.Input;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class StatusTimeEditTests
{
    [Fact]
    public void ApplyStart_WithoutSelection_SelectsToEnd()
    {
        var next = StatusTimeEdit.ApplyStart(WaveSelection.Empty, 100, 500);
        Assert.Equal(new WaveSelection(100, 500), next);
    }

    [Fact]
    public void ApplyStart_WithSelection_KeepsEnd()
    {
        var next = StatusTimeEdit.ApplyStart(new WaveSelection(10, 40), 20, 500);
        Assert.Equal(new WaveSelection(20, 40), next);
    }

    [Fact]
    public void ApplyEnd_WithoutSelection_SelectsFromStart()
    {
        var next = StatusTimeEdit.ApplyEnd(WaveSelection.Empty, 80, 500);
        Assert.Equal(new WaveSelection(0, 80), next);
    }

    [Fact]
    public void ApplyEnd_BeforeStart_Clears()
    {
        var next = StatusTimeEdit.ApplyEnd(new WaveSelection(50, 90), 20, 500);
        Assert.True(next.IsEmpty);
    }

    [Fact]
    public void ApplyLength_WithoutSelection_UsesPlayhead()
    {
        var next = StatusTimeEdit.ApplyLength(WaveSelection.Empty, 40, playhead: 10, frameCount: 500);
        Assert.Equal(new WaveSelection(10, 50), next);
    }

    [Fact]
    public void ApplyLength_Zero_Clears()
    {
        var next = StatusTimeEdit.ApplyLength(new WaveSelection(10, 40), 0, playhead: 10, frameCount: 500);
        Assert.True(next.IsEmpty);
    }

    [Theory]
    [InlineData(false, 48000, ModifierKeys.None, 48000)]
    [InlineData(false, 48000, ModifierKeys.Shift, 480000)]
    [InlineData(false, 48000, ModifierKeys.Control, 2880000)]
    [InlineData(false, 48000, ModifierKeys.Control | ModifierKeys.Shift, 28800000)]
    [InlineData(true, 48000, ModifierKeys.None, 1)]
    [InlineData(true, 48000, ModifierKeys.Shift, 10)]
    [InlineData(true, 48000, ModifierKeys.Control, 100)]
    [InlineData(true, 48000, ModifierKeys.Control | ModifierKeys.Shift, 1000)]
    public void NudgeStep_UsesDisplayModeAndModifiers(
        bool showSamples,
        int sampleRate,
        ModifierKeys modifiers,
        long expected)
    {
        Assert.Equal(expected, StatusTimeEdit.NudgeStep(showSamples, sampleRate, modifiers));
    }

    [Fact]
    public void ApplyLength_ClampsToFileEnd()
    {
        var next = StatusTimeEdit.ApplyLength(new WaveSelection(480, 490), 100, playhead: 0, frameCount: 500);
        Assert.Equal(new WaveSelection(480, 500), next);
    }
}
