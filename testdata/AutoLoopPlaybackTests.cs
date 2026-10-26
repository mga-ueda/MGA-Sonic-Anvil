using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class AutoLoopPlaybackTests
{
    [Fact]
    public void UsesSampleLoopWindow_RequiresAutoLoopAndSampleLoopWithoutSelectionOrAb()
    {
        Assert.True(AutoLoopPlayback.UsesSampleLoopWindow(
            autoLoopEnabled: true,
            sampleLoopEmpty: false,
            hasSelection: false,
            hasPlayerAbLoop: false));
        Assert.False(AutoLoopPlayback.UsesSampleLoopWindow(
            autoLoopEnabled: false,
            sampleLoopEmpty: false,
            hasSelection: false,
            hasPlayerAbLoop: false));
        Assert.False(AutoLoopPlayback.UsesSampleLoopWindow(
            autoLoopEnabled: true,
            sampleLoopEmpty: true,
            hasSelection: false,
            hasPlayerAbLoop: false));
        Assert.False(AutoLoopPlayback.UsesSampleLoopWindow(
            autoLoopEnabled: true,
            sampleLoopEmpty: false,
            hasSelection: true,
            hasPlayerAbLoop: false));
        Assert.False(AutoLoopPlayback.UsesSampleLoopWindow(
            autoLoopEnabled: true,
            sampleLoopEmpty: false,
            hasSelection: false,
            hasPlayerAbLoop: true));
        // プレイヤーではサンプルループを無視（割合ジャンプが S–E に吸われない）。
        Assert.False(AutoLoopPlayback.UsesSampleLoopWindow(
            autoLoopEnabled: true,
            sampleLoopEmpty: false,
            hasSelection: false,
            hasPlayerAbLoop: false,
            playerMode: true));
    }

    [Fact]
    public void ClampStartFrame_AllowBeforeStart_KeepsIntro_SnapsPastEnd()
    {
        var loop = new WaveSelection(100, 400);
        Assert.Equal(0, AutoLoopPlayback.ClampStartFrame(0, loop, allowBeforeStart: true));
        Assert.Equal(50, AutoLoopPlayback.ClampStartFrame(50, loop, allowBeforeStart: true));
        Assert.Equal(100, AutoLoopPlayback.ClampStartFrame(100, loop, allowBeforeStart: true));
        Assert.Equal(250, AutoLoopPlayback.ClampStartFrame(250, loop, allowBeforeStart: true));
        Assert.Equal(100, AutoLoopPlayback.ClampStartFrame(400, loop, allowBeforeStart: true));
        Assert.Equal(100, AutoLoopPlayback.ClampStartFrame(500, loop, allowBeforeStart: true));
    }

    [Fact]
    public void ClampStartFrame_DisallowBeforeStart_SnapsOutsideToStart()
    {
        var loop = new WaveSelection(100, 400);
        Assert.Equal(100, AutoLoopPlayback.ClampStartFrame(0, loop, allowBeforeStart: false));
        Assert.Equal(100, AutoLoopPlayback.ClampStartFrame(50, loop, allowBeforeStart: false));
        Assert.Equal(250, AutoLoopPlayback.ClampStartFrame(250, loop, allowBeforeStart: false));
        Assert.Equal(100, AutoLoopPlayback.ClampStartFrame(400, loop, allowBeforeStart: false));
    }
}
