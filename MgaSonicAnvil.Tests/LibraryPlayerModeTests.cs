using System.Windows.Input;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibraryPlayerModeTests
{
    [Fact]
    public void BlocksWaveMenu_AllowsPlaybackSeekSelectAndView()
    {
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
        Assert.False(LibraryPlayerMode.AllowsKey(Key.V, ModifierKeys.Shift));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.LeftShift, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.F4, ModifierKeys.Alt));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Space, ModifierKeys.Alt));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Q, ModifierKeys.Control));
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
        Assert.False(LibraryPlayerMode.AllowsKey(Key.Tab, ModifierKeys.None));
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
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.F10, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.Q, ModifierKeys.Control));
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
    }

    private static DocumentSession Session(string name) =>
        new(new AudioDocument([], 48000, 1, 16, AudioFileKind.Wave, name));
}
