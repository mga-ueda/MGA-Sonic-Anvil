using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibraryPlayerModeTests
{
    [Fact]
    public void BlocksWaveMenu_AllowsPlaybackSeekSelectAndView()
    {
        Assert.True(LibraryPlayerMode.HidesWaveformContextMenu(playerMode: true));
        Assert.False(LibraryPlayerMode.HidesWaveformContextMenu(playerMode: false));
        Assert.False(LibraryPlayerMode.ShowsCueOverlays(playerMode: true));
        Assert.True(LibraryPlayerMode.ShowsCueOverlays(playerMode: false));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.TogglePlayback));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.SelectAll));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.SeekHere));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.Open));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.Save));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.SaveAs));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.SaveMp3));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.CenterPlayhead));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.CenterLock));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.MaximizeLibrary));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.SilentSkip));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.Manual));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.ViewWaveform));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.ViewSpectrogram));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.ViewOverlay));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.ViewLoudness));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.WaapiPanel));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.WwiseExport));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.PlayExit));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.Cut));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.AddMarker));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.Record));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.NewDocument));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.TimeZoomIn));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.ExportWave));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.FocusTime));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.CloseTab));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.CloseOthers));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.CloseTabsRight));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.CloseTabsLeft));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.CloseAll));
    }

    [Fact]
    public void BlocksTransport_AllowsPlaySeekAndView()
    {
        Assert.False(LibraryPlayerMode.BlocksTransport(TransportCommand.TogglePlayback));
        Assert.False(LibraryPlayerMode.BlocksTransport(TransportCommand.GoToStart));
        Assert.False(LibraryPlayerMode.BlocksTransport(TransportCommand.Open));
        Assert.False(LibraryPlayerMode.BlocksTransport(TransportCommand.ToggleLibraryMaximize));
        Assert.False(LibraryPlayerMode.BlocksTransport(TransportCommand.ToggleAnalyzerMaximize));
        Assert.True(LibraryPlayerMode.BlocksTransport(TransportCommand.Save));
        Assert.True(LibraryPlayerMode.BlocksTransport(TransportCommand.SaveAs));
        Assert.True(LibraryPlayerMode.BlocksTransport(TransportCommand.SaveMp3));
        Assert.True(LibraryPlayerMode.BlocksTransport(TransportCommand.CenterPlayhead));
        Assert.True(LibraryPlayerMode.BlocksTransport(TransportCommand.ToggleSpectrogram));
        Assert.True(LibraryPlayerMode.BlocksTransport(TransportCommand.ToggleLoudnessView));
        Assert.True(LibraryPlayerMode.BlocksTransport(TransportCommand.ToggleAnalysis));
        Assert.True(LibraryPlayerMode.BlocksTransport(TransportCommand.Record));
        Assert.True(LibraryPlayerMode.BlocksTransport(TransportCommand.FadeIn));
        Assert.True(LibraryPlayerMode.BlocksTransport(TransportCommand.TimeZoomIn));
        Assert.True(LibraryPlayerMode.BlocksTransport(TransportCommand.NewDocument));
        Assert.True(LibraryPlayerMode.BlocksTransport(TransportCommand.CycleWaveformHeight));
        Assert.True(LibraryPlayerMode.BlocksTransport(TransportCommand.ToggleWaapi));
    }

    [Fact]
    public void AllowsKey_KeepsJumpAndPlay_RejectsEdit()
    {
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Space, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Enter, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Left, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Home, ModifierKeys.Control));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.PageDown, ModifierKeys.Control));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.Home, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.End, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.PageUp, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.PageDown, ModifierKeys.Shift));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.D3, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.NumPad7, ModifierKeys.Shift));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.A, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.A, ModifierKeys.Shift));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.A, ModifierKeys.Control));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.F, ModifierKeys.Control));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.F, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.V, ModifierKeys.Shift));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.LeftShift, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.F4, ModifierKeys.Alt));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Space, ModifierKeys.Alt));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Q, ModifierKeys.Control));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.O, ModifierKeys.Control));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.O, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.C, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.S, ModifierKeys.Alt));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.S, ModifierKeys.Control));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.S, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.M, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.Z, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.W, ModifierKeys.Control));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.W, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.W, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.E, ModifierKeys.Alt));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.F4, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.Up, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.M, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.Delete, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.C, ModifierKeys.Control));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.Left, ModifierKeys.Alt));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.G, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Tab, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Tab, ModifierKeys.Shift));
    }

    [Fact]
    public void BlocksExplorerKey_StopsFileOps_KeepsNavigation()
    {
        Assert.True(LibraryPlayerMode.BlocksExplorerKey(Key.Delete, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.BlocksExplorerKey(Key.Back, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.BlocksExplorerKey(Key.C, ModifierKeys.Control));
        Assert.True(LibraryPlayerMode.BlocksExplorerKey(Key.X, ModifierKeys.Control));
        Assert.True(LibraryPlayerMode.BlocksExplorerKey(Key.V, ModifierKeys.Control));
        Assert.True(LibraryPlayerMode.BlocksExplorerKey(Key.D, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.Up, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.Left, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.Enter, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.Enter, ModifierKeys.Shift));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.F1, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.F2, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.F3, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.Tab, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.Tab, ModifierKeys.Shift));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.F10, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.Q, ModifierKeys.Control));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.O, ModifierKeys.Control));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.O, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.C, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.Multiply, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.D8, ModifierKeys.Shift));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.Divide, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.Oem2, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.IsExplorerExpandAll(Key.Multiply, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.IsExplorerExpandAll(Key.D8, ModifierKeys.Shift));
        Assert.False(LibraryPlayerMode.IsExplorerExpandAll(Key.D8, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.IsExplorerCollapseSubtree(Key.Divide, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.IsExplorerCollapseSubtree(Key.Oem2, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.IsExplorerCollapseSubtree(Key.Oem2, ModifierKeys.Shift));
        Assert.True(LibraryPlayerMode.ExplorerOwnsHorizontal(Key.Left, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.ExplorerOwnsHorizontal(Key.Right, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.ExplorerOwnsHorizontal(Key.Left, ModifierKeys.Shift));
        Assert.False(LibraryPlayerMode.ExplorerOwnsHorizontal(Key.Left, ModifierKeys.Control));
        Assert.False(LibraryPlayerMode.ExplorerOwnsHorizontal(Key.Up, ModifierKeys.None));
    }

    [Fact]
    public void AllowsKey_PlayerPaneFocusKeys()
    {
        Assert.True(LibraryPlayerMode.AllowsKey(Key.F1, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.F2, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.F3, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.F2, ModifierKeys.Control));
        Assert.Equal(LibraryPane.Favorites, LibraryPlayerMode.NextPane(LibraryPane.Explorer, reverse: false));
        Assert.Equal(LibraryPane.List, LibraryPlayerMode.NextPane(LibraryPane.Favorites, reverse: false));
        Assert.Equal(LibraryPane.Explorer, LibraryPlayerMode.NextPane(LibraryPane.List, reverse: false));
        Assert.Equal(LibraryPane.List, LibraryPlayerMode.NextPane(LibraryPane.Explorer, reverse: true));
        Assert.Equal(LibraryPane.Explorer, LibraryPlayerMode.NextPane(LibraryPane.Favorites, reverse: true));
        Assert.Equal(LibraryPane.Favorites, LibraryPlayerMode.NextPane(LibraryPane.List, reverse: true));
        Assert.True(LibraryPlayerMode.IsPaneCycleKey(Key.Tab, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.IsPaneCycleKey(Key.Tab, ModifierKeys.Shift));
        Assert.False(LibraryPlayerMode.IsPaneCycleKey(Key.Tab, ModifierKeys.Control));
        Assert.Equal(LibraryPane.Explorer, LibraryPlayerMode.SearchPane(LibraryPane.Explorer));
        Assert.Equal(LibraryPane.Explorer, LibraryPlayerMode.SearchPane(LibraryPane.Favorites));
        Assert.Equal(LibraryPane.List, LibraryPlayerMode.SearchPane(LibraryPane.List));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.F, ModifierKeys.Control));
    }

    [Fact]
    public void SessionsToKeep_PrefersListSelection()
    {
        var first = Session("a.wav");
        var second = Session("b.wav");
        var kept = LibraryPlayerMode.SessionsToKeep([first, second], first);
        Assert.Equal(new[] { first, second }, kept);
        Assert.Equal(new[] { first }, LibraryPlayerMode.SessionsToKeep([], first));
        Assert.Empty(LibraryPlayerMode.SessionsToKeep([], null));
    }

    [Fact]
    public void SessionsToDrop_LeavesOnlyKept()
    {
        var first = Session("a.wav");
        var second = Session("b.wav");
        var third = Session("c.wav");
        var drop = LibraryPlayerMode.SessionsToDrop([first, second, third], [first, third]);
        Assert.Equal(new[] { second }, drop);
        Assert.Empty(LibraryPlayerMode.SessionsToDrop([first, second], [first, second]));
    }

    [Fact]
    public void NeedsEditorPcmUpgrade_IsStreamOrDeferred()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".mp3");
        var deferred = AudioDocument.CreateDeferred(path);
        Assert.True(LibraryPlayerMode.NeedsEditorPcmUpgrade(deferred));
        Assert.False(LibraryPlayerMode.NeedsEditorPeakUpgrade(deferred));

        deferred.ActivateStreamPlayback(44100, 2, 16, 88200);
        Assert.True(LibraryPlayerMode.NeedsEditorPcmUpgrade(deferred));
        Assert.False(LibraryPlayerMode.NeedsEditorPeakUpgrade(deferred));

        var pcm = new AudioDocument([], 48000, 1, 16, AudioFileKind.Wave, "a.wav");
        Assert.False(LibraryPlayerMode.NeedsEditorPcmUpgrade(pcm));
        Assert.True(LibraryPlayerMode.NeedsEditorPeakUpgrade(pcm));
        Assert.False(LibraryPlayerMode.CanReusePeaks(playerMode: false, pcm));
        Assert.False(LibraryPlayerMode.CanReusePeaks(playerMode: true, pcm));
    }

    [Fact]
    public void NeedsEditorPeakUpgrade_RebuildsPlayerMonoEnvelope()
    {
        var samples = new float[400];
        samples[2] = 0.9f;
        samples[3] = -0.4f;
        var pcm = new AudioDocument(samples, 48000, 2, 16, AudioFileKind.Wave, "a.wav");
        Assert.False(LibraryPlayerMode.NeedsEditorPcmUpgrade(pcm));
        Assert.False(pcm.Peaks.NeedsEditorRebuild(pcm.Channels));
        Assert.False(LibraryPlayerMode.NeedsEditorPeakUpgrade(pcm));
        Assert.True(LibraryPlayerMode.CanReusePeaks(playerMode: false, pcm));

        pcm.ReplacePeaks(PeakPyramid.BuildPlayerDisplay(samples, 2, samples.Length));
        Assert.Equal(1, pcm.Peaks.Channels);
        Assert.False(pcm.Peaks.NeedsEditorDetail);
        Assert.True(pcm.Peaks.NeedsEditorRebuild(pcm.Channels));
        Assert.True(LibraryPlayerMode.NeedsEditorPeakUpgrade(pcm));
        Assert.True(LibraryPlayerMode.CanReusePeaks(playerMode: true, pcm));
        Assert.False(LibraryPlayerMode.CanReusePeaks(playerMode: false, pcm));
    }

    [Fact]
    public void FirstSession_IsListHead()
    {
        var first = Session("a.wav");
        var second = Session("b.wav");
        Assert.Same(first, LibraryPlayerMode.FirstSession([first, second]));
        Assert.Null(LibraryPlayerMode.FirstSession([]));
    }

    [Fact]
    public void ShowsPlayerMeters_HidesWhenPlaylistEmpty()
    {
        Assert.True(LibraryPlayerMode.ShowsPlayerMeters(playerMode: false, playlistCount: 0));
        Assert.True(LibraryPlayerMode.ShowsPlayerMeters(playerMode: false, playlistCount: 2));
        Assert.False(LibraryPlayerMode.ShowsPlayerMeters(playerMode: true, playlistCount: 0));
        Assert.True(LibraryPlayerMode.ShowsPlayerMeters(playerMode: true, playlistCount: 1));
        Assert.False(LibraryPlayerMode.InstantPlayerMeterReveal(show: false, signalMoving: true));
        Assert.False(LibraryPlayerMode.InstantPlayerMeterReveal(show: true, signalMoving: false));
        Assert.True(LibraryPlayerMode.InstantPlayerMeterReveal(show: true, signalMoving: true));
        Assert.True(LibraryPlayerMode.SnapPlayerMetersHiddenOnEnter(enteringPlayer: true, instantReveal: false));
        Assert.False(LibraryPlayerMode.SnapPlayerMetersHiddenOnEnter(enteringPlayer: true, instantReveal: true));
        Assert.False(LibraryPlayerMode.SnapPlayerMetersHiddenOnEnter(enteringPlayer: false, instantReveal: false));
        Assert.Equal(1, LibraryPlayerMode.MeterFadeSeconds);
        Assert.True(LibraryPlayerMode.MeterFadeFrameRate >= 24);
        Assert.True(LibraryPlayerMode.MeterFadeFrameRate <= 60);
    }

    [Fact]
    public void CreateMeterFade_IsOneSecondSine()
    {
        RunSta(() =>
        {
            var fadeIn = LibraryPlayerMode.CreateMeterFade(0, 1);
            var fadeOut = LibraryPlayerMode.CreateMeterFade(1, 0);
            Assert.Equal(TimeSpan.FromSeconds(1), fadeIn.Duration.TimeSpan);
            Assert.Equal(0, fadeIn.From);
            Assert.Equal(1, fadeIn.To);
            Assert.Equal(1, fadeOut.From);
            Assert.Equal(0, fadeOut.To);
            Assert.Equal(FillBehavior.HoldEnd, fadeIn.FillBehavior);
            var fadeInEase = Assert.IsType<SineEase>(fadeIn.EasingFunction);
            var fadeOutEase = Assert.IsType<SineEase>(fadeOut.EasingFunction);
            Assert.Equal(EasingMode.EaseOut, fadeInEase.EasingMode);
            Assert.Equal(EasingMode.EaseIn, fadeOutEase.EasingMode);
            Assert.False(fadeIn.AutoReverse);
            Assert.Equal(LibraryPlayerMode.MeterFadeFrameRate, Timeline.GetDesiredFrameRate(fadeIn));
            Assert.Equal(LibraryPlayerMode.MeterFadeFrameRate, Timeline.GetDesiredFrameRate(fadeOut));
        });
    }

    [Fact]
    public void ListNav_HomeEndPageAndVisibleStep()
    {
        Assert.Equal(0, LibraryPlayerMode.EdgeIndex(4, -1));
        Assert.Equal(3, LibraryPlayerMode.EdgeIndex(4, 1));
        Assert.Equal(-1, LibraryPlayerMode.EdgeIndex(0, 1));
        Assert.Equal(1, LibraryPlayerMode.PageStep(1));
        Assert.Equal(7, LibraryPlayerMode.PageStep(8));
        Assert.Equal(1, LibraryPlayerMode.NextLoopIndex(3, 0));
        Assert.Equal(2, LibraryPlayerMode.NextLoopIndex(3, 1));
        Assert.Equal(0, LibraryPlayerMode.NextLoopIndex(3, 2));
        Assert.Equal(0, LibraryPlayerMode.NextLoopIndex(3, -1));
        Assert.Equal(0, LibraryPlayerMode.NextLoopIndex(1, 0));
        Assert.Equal(-1, LibraryPlayerMode.NextLoopIndex(0, 0));
        Assert.Equal(2, LibraryPlayerMode.PreviousLoopIndex(3, 0));
        Assert.Equal(0, LibraryPlayerMode.PreviousLoopIndex(3, 1));
        Assert.Equal(1, LibraryPlayerMode.PreviousLoopIndex(3, 2));
        Assert.Equal(2, LibraryPlayerMode.PreviousLoopIndex(3, -1));
        Assert.Equal(0, LibraryPlayerMode.PreviousLoopIndex(1, 0));
        Assert.Equal(-1, LibraryPlayerMode.PreviousLoopIndex(0, 0));
    }

    [Fact]
    public void PlayerNumpad_MapsTransport_NotPercent()
    {
        Assert.Equal(LibraryNumpadCommand.PlayPause, LibraryPlayerMode.PlayerNumpadCommand(Key.NumPad0, ModifierKeys.None));
        Assert.Equal(LibraryNumpadCommand.Rewind, LibraryPlayerMode.PlayerNumpadCommand(Key.NumPad1, ModifierKeys.None));
        Assert.Equal(LibraryNumpadCommand.None, LibraryPlayerMode.PlayerNumpadCommand(Key.NumPad2, ModifierKeys.None));
        Assert.Equal(LibraryNumpadCommand.FastForward, LibraryPlayerMode.PlayerNumpadCommand(Key.NumPad3, ModifierKeys.None));
        Assert.Equal(LibraryNumpadCommand.PreviousTrack, LibraryPlayerMode.PlayerNumpadCommand(Key.NumPad4, ModifierKeys.None));
        Assert.Equal(LibraryNumpadCommand.Restart, LibraryPlayerMode.PlayerNumpadCommand(Key.NumPad5, ModifierKeys.None));
        Assert.Equal(LibraryNumpadCommand.NextTrack, LibraryPlayerMode.PlayerNumpadCommand(Key.NumPad6, ModifierKeys.None));
        Assert.Equal(LibraryNumpadCommand.SeekBack, LibraryPlayerMode.PlayerNumpadCommand(Key.NumPad7, ModifierKeys.None));
        Assert.Equal(LibraryNumpadCommand.None, LibraryPlayerMode.PlayerNumpadCommand(Key.NumPad8, ModifierKeys.None));
        Assert.Equal(LibraryNumpadCommand.SeekForward, LibraryPlayerMode.PlayerNumpadCommand(Key.NumPad9, ModifierKeys.None));
        Assert.Equal(LibraryNumpadCommand.None, LibraryPlayerMode.PlayerNumpadCommand(Key.NumPad5, ModifierKeys.Shift));
        Assert.Equal(LibraryNumpadCommand.None, LibraryPlayerMode.PlayerNumpadCommand(Key.D5, ModifierKeys.None));
        Assert.Equal(LibraryNumpadCommand.None, LibraryPlayerMode.PlayerNumpadCommand(Key.Left, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.IsPlayerNumpadKey(Key.NumPad7, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.IsPlayerNumpadKey(Key.D7, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.IsPlayerShuttleKey(Key.NumPad1, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.IsPlayerShuttleKey(Key.NumPad3, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.IsPlayerShuttleKey(Key.Left, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.IsPlayerShuttleKey(Key.Right, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.IsPlayerSeekNudgeKey(Key.NumPad7, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.IsPlayerSeekNudgeKey(Key.NumPad9, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.IsPlayerSeekNudgeKey(Key.NumPad1, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.IsPlayerSeekNudgeKey(Key.D7, ModifierKeys.None));
        Assert.Equal(Key.D5, LibraryPlayerMode.ResolveDigitKey(Key.ImeProcessed, Key.D5));
        Assert.Equal(Key.D0, LibraryPlayerMode.ResolveDigitKey(Key.ImeProcessed, Key.D0));
        Assert.Equal(Key.NumPad7, LibraryPlayerMode.ResolveDigitKey(Key.ImeProcessed, Key.NumPad7));
        Assert.Equal(Key.ImeProcessed, LibraryPlayerMode.ResolveDigitKey(Key.ImeProcessed, Key.A));
        Assert.Equal(Key.D3, LibraryPlayerMode.ResolveDigitKey(Key.D3, Key.D5));
        Assert.Equal(5, LibraryPlayerMode.SeekNudgeSeconds);
        Assert.Equal(750, LibraryPlayerMode.SeekNudgeFadeMilliseconds);
        Assert.Equal(250, LibraryPlayerMode.SeekNudgeRepeatDelayMs);
        Assert.Equal(200, LibraryPlayerMode.SeekNudgeRepeatIntervalMs);
        Assert.True(LibraryPlayerMode.SeekNudgeRepeatDelayMs > LibraryPlayerMode.SeekNudgeRepeatIntervalMs);
        Assert.Equal(250, LibraryPlayerMode.SeekNudgeTimerIntervalMs(repeatStarted: false));
        Assert.Equal(200, LibraryPlayerMode.SeekNudgeTimerIntervalMs(repeatStarted: true));
        Assert.Equal(DispatcherPriority.Send, LibraryPlayerMode.SeekNudgeTimerPriority);
        Assert.Equal(1, LibraryPlayerMode.SeekNudgeCatchUpSteps(50, repeatStarted: true));
        Assert.Equal(1, LibraryPlayerMode.SeekNudgeCatchUpSteps(199, repeatStarted: true));
        Assert.Equal(2, LibraryPlayerMode.SeekNudgeCatchUpSteps(400, repeatStarted: true));
        Assert.Equal(3, LibraryPlayerMode.SeekNudgeCatchUpSteps(2000, repeatStarted: true));
        Assert.Equal(5, LibraryPlayerMode.SeekNudgeCatchUpSeconds(50, repeatStarted: true));
        Assert.Equal(10, LibraryPlayerMode.SeekNudgeCatchUpSeconds(400, repeatStarted: true));
        Assert.Equal(15, LibraryPlayerMode.SeekNudgeCatchUpSeconds(2000, repeatStarted: true));
        Assert.Equal(16, LibraryPlayerMode.PlaybackVisualMinIntervalMs);
        Assert.True(LibraryPlayerMode.PlaybackVisualMinIntervalMs >= 14);
        Assert.True(LibraryPlayerMode.PlaybackVisualMinIntervalMs <= 20);
        Assert.Equal(DispatcherPriority.Background, LibraryPlayerMode.AnalyzerTickPriority);
        Assert.True(LibraryPlayerMode.AnalyzerTickPriority < DispatcherPriority.Input);
        Assert.Equal(50, LibraryPlayerMode.PlayheadIdleInvalidateMs);
        Assert.True(LibraryPlayerMode.ShouldRefreshPlayheadPaint(double.NaN, 0, 10, 0));
        Assert.False(LibraryPlayerMode.ShouldRefreshPlayheadPaint(10, 100, 10.2, 120));
        Assert.True(LibraryPlayerMode.ShouldRefreshPlayheadPaint(10, 100, 10.2, 160));
        Assert.True(LibraryPlayerMode.ShouldRefreshPlayheadPaint(10, 100, 11, 110));
        Assert.True(LibraryPlayerMode.PlayerNumpadRepeatIsHold(LibraryNumpadCommand.SeekForward));
        Assert.True(LibraryPlayerMode.PlayerNumpadRepeatIsHold(LibraryNumpadCommand.SeekBack));
        Assert.True(LibraryPlayerMode.PlayerNumpadRepeatIsHold(LibraryNumpadCommand.Rewind));
        Assert.False(LibraryPlayerMode.PlayerNumpadRepeatIsHold(LibraryNumpadCommand.NextTrack));
    }

    [Fact]
    public void KeepPeakJob_KeepsPlayingAndNextOnly()
    {
        var playing = Session("now.mp3").Document;
        var next = Session("next.mp3").Document;
        var other = Session("other.mp3").Document;
        Assert.True(LibraryPlayerMode.KeepPeakJob(playing, playing, next));
        Assert.True(LibraryPlayerMode.KeepPeakJob(next, playing, next));
        Assert.False(LibraryPlayerMode.KeepPeakJob(other, playing, next));
        Assert.True(LibraryPlayerMode.KeepPeakJob(playing, playing, null));
        Assert.False(LibraryPlayerMode.KeepPeakJob(other, playing, null));
        Assert.False(LibraryPlayerMode.KeepPeakJob(other, null, null));
    }

    private static DocumentSession Session(string name) =>
        new(new AudioDocument([], 48000, 1, 16, AudioFileKind.Wave, name));

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
