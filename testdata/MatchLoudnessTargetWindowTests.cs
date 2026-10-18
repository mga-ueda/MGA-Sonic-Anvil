using System.Windows.Input;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class MatchLoudnessTargetWindowTests
{
    [Fact]
    public void NudgeStep_ShiftIsOne_ElseTenth()
    {
        Assert.Equal(0.1, MatchLoudnessTargetWindow.NudgeStep(ModifierKeys.None));
        Assert.Equal(1, MatchLoudnessTargetWindow.NudgeStep(ModifierKeys.Shift));
        Assert.Equal(1, MatchLoudnessTargetWindow.NudgeStep(ModifierKeys.Shift | ModifierKeys.Control));
    }

    [Fact]
    public void NudgeTarget_RespectsRangeAndSnap()
    {
        Assert.Equal(-23.9, MatchLoudnessTargetWindow.NudgeTarget(-24, 1, ModifierKeys.None), 5);
        Assert.Equal(-23, MatchLoudnessTargetWindow.NudgeTarget(-24, 1, ModifierKeys.Shift), 5);
        Assert.Equal(0, MatchLoudnessTargetWindow.NudgeTarget(0, 1, ModifierKeys.Shift), 5);
        Assert.Equal(
            LoudnessMeterEngine.MinTargetLufs,
            MatchLoudnessTargetWindow.NudgeTarget(LoudnessMeterEngine.MinTargetLufs, -1, ModifierKeys.Shift),
            5);
    }

    [Fact]
    public void SnapTargetLufs_RoundsToTenth()
    {
        Assert.Equal(-24.2, MatchLoudnessTargetWindow.SnapTargetLufs(-24.24), 5);
        Assert.Equal(-24.3, MatchLoudnessTargetWindow.SnapTargetLufs(-24.25), 5);
    }
}
