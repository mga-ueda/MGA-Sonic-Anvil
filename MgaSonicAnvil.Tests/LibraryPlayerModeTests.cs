using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibraryPlayerModeTests
{
    [Fact]
    public void BlocksWaveMenu_AllowsPlaybackSeekSelectAndView()
    {
        Assert.False(LibraryPlayerMode.HidesWaveformContextMenu(playerMode: true));
        Assert.False(LibraryPlayerMode.HidesWaveformContextMenu(playerMode: false));
        Assert.False(LibraryPlayerMode.ShowsCueOverlays(playerMode: true));
        Assert.True(LibraryPlayerMode.ShowsCueOverlays(playerMode: false));
        Assert.True(LibraryPlayerMode.ShowsPlayerScratchOverlays(
            playerMode: true,
            videoMiniPlayer: true,
            videoRealPlayback: false));
        Assert.True(LibraryPlayerMode.ShowsPlayerScratchOverlays(
            playerMode: true,
            videoMiniPlayer: false,
            videoRealPlayback: true));
        Assert.False(LibraryPlayerMode.ShowsPlayerScratchOverlays(
            playerMode: true,
            videoMiniPlayer: false,
            videoRealPlayback: false));
        Assert.True(LibraryPlayerMode.ShowsPlayerScratchOverlays(
            playerMode: true,
            videoMiniPlayer: false,
            videoRealPlayback: false,
            audioWaveform: true));
        // 本再生で打ったキューがあっても、暗いプレビューでは出さない。
        Assert.False(LibraryPlayerMode.ShowsPlayerScratchOverlays(
            playerMode: true,
            videoMiniPlayer: false,
            videoRealPlayback: false,
            videoImmersivePause: false,
            audioWaveform: false));
        Assert.False(LibraryPlayerMode.ShowsPlayerScratchOverlays(
            playerMode: false,
            videoMiniPlayer: true,
            videoRealPlayback: true));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.TogglePlayback));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.SelectAll));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.SeekHere));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.Open));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.Save));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.SaveAs));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.SaveMp3));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.ExportMp3));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.ExportAllMp3));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.Volume));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.CenterPlayhead));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.CenterLock));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.MaximizeLibrary));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.SilentSkip));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.Manual));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.ViewWaveform));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.Shuffle));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.AutoPlayNext));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.AutoPlay));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.VideoMini));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.ForceVideoProxy));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.Undo));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.ViewSpectrogram));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.ViewOverlay));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.ViewLoudness));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.WaapiPanel));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.ColorPanel));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.CopyFileName));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.CopyFilePath));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.CopySelectionTime));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.CopySelectionTimePath));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.RangeClick));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.WwiseExport));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.PlayExit));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.Cut));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.AddMarker));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.AddMarkerHere));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.ClearMarkers));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.DeleteMarkers));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.DeleteAllMarkers));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.RenameMarker));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.PrevMarker));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.NextMarker));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.NextTab));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.PrevTab));
        Assert.True(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.ReopenTab));
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
    public void ShowsSeekCursors_HidesPlayheadAndMouseGuideForPlayerPdf()
    {
        Assert.False(LibraryPlayerMode.ShowsSeekCursors(playerMode: true, isPdf: true));
        Assert.True(LibraryPlayerMode.ShowsSeekCursors(playerMode: true, isPdf: false));
        Assert.True(LibraryPlayerMode.ShowsSeekCursors(playerMode: false, isPdf: true));
        Assert.True(LibraryPlayerMode.ShowsSeekCursors(playerMode: false, isPdf: false));
    }

    [Fact]
    public void WantsAmbientSpill_WhenUniformLeavesLetterboxOrPillarbox()
    {
        // 縦長 → 横長ステージ（ピラーボックス）
        Assert.True(LibraryVisualStage.WantsAmbientSpill(1080, 1920, 1920, 1080));
        // 4:3 → 16:9（ピラーボックス）
        Assert.True(LibraryVisualStage.WantsAmbientSpill(640, 480, 1920, 1080));
        // 16:9 → 縦長ウィンドウ（レターボックス）
        Assert.True(LibraryVisualStage.WantsAmbientSpill(1920, 1080, 900, 1600));
        // アスペクト一致 → 黒帯なし
        Assert.False(LibraryVisualStage.WantsAmbientSpill(1920, 1080, 1920, 1080));
        Assert.False(LibraryVisualStage.WantsAmbientSpill(640, 480, 800, 600));
        Assert.False(LibraryVisualStage.WantsAmbientSpill(0, 1920, 1920, 1080));
        Assert.False(LibraryVisualStage.WantsAmbientSpill(1080, 1920, 0, 1080));
        Assert.InRange(LibraryVisualStage.AmbientSpillBlurRadius, 48, 96);
        Assert.InRange(LibraryVisualStage.AmbientSpillScale, 1.08, 1.2);
        Assert.InRange(LibraryVisualStage.AmbientSpillOpacity, 0.2, 0.45);
        Assert.InRange(LibraryVisualStage.AmbientSpillAspectEpsilon, 0.005, 0.05);
        // 格子線はデバイス 1px（pixelsPerDip=2 → DIP 0.5）。
        Assert.Equal(0.5, WpfControlHelpers.DeviceHairline(2), 5);
        Assert.NotNull(LibraryVisualStage.CreateMeshBrush(2));

        var portrait = LibraryVisualStage.UniformContentRect(1080, 1920, 1920, 1080);
        Assert.False(portrait.IsEmpty);
        Assert.Equal(1080, portrait.Height, 3);
        Assert.Equal(1080 * (1080 / 1920d), portrait.Width, 3);
        Assert.Equal((1920 - portrait.Width) * 0.5, portrait.X, 3);
        Assert.Equal(0, portrait.Y, 3);

        var fourByThree = LibraryVisualStage.UniformContentRect(640, 480, 1920, 1080);
        Assert.True(fourByThree.Width < 1920);
        Assert.Equal(1080, fourByThree.Height, 3);
    }

    [Fact]
    public void WrapLoopFrame_RestartsAtSelectionStart()
    {
        Assert.True(LibraryPlayerMode.DrivesPlayheadWhileVideoPlays(
            playerMode: true, isVideoFile: true, videoClockRunning: true));
        Assert.False(LibraryPlayerMode.DrivesPlayheadWhileVideoPlays(
            playerMode: true, isVideoFile: true, videoClockRunning: false));
        Assert.False(LibraryPlayerMode.DrivesPlayheadWhileVideoPlays(
            playerMode: true, isVideoFile: false, videoClockRunning: true));
        Assert.False(LibraryPlayerMode.DrivesPlayheadWhileVideoPlays(
            playerMode: false, isVideoFile: true, videoClockRunning: true));
        Assert.True(LibraryPlayerMode.PreservesVisualPlayheadOnBind(playerMode: true));
        Assert.False(LibraryPlayerMode.PreservesVisualPlayheadOnBind(playerMode: false));
        Assert.Equal(48000, LibraryPlayerMode.FrameFromSeconds(1, 48000, 96000));
        Assert.Equal(0, LibraryPlayerMode.FrameFromSeconds(-1, 48000, 96000));
        Assert.Equal(96000, LibraryPlayerMode.FrameFromSeconds(4, 48000, 96000));
        Assert.Equal(0, LibraryPlayerMode.HoverResumeFrame(
            hoverVideoShown: false, visualPlaying: false, sameClip: true, 10, 48000, 96000));
        Assert.Equal(0, LibraryPlayerMode.HoverResumeFrame(
            hoverVideoShown: true, visualPlaying: true, sameClip: true, 10, 48000, 96000));
        Assert.Equal(0, LibraryPlayerMode.HoverResumeFrame(
            hoverVideoShown: true, visualPlaying: false, sameClip: false, 1, 48000, 96000));
        Assert.Equal(48000, LibraryPlayerMode.HoverResumeFrame(
            hoverVideoShown: true, visualPlaying: false, sameClip: true, 1, 48000, 96000));
        Assert.Equal(48000, LibraryPlayerMode.HoverResumeFrame(
            hoverVideoShown: true, visualPlaying: false, sameClip: true, 1, 48000, 0));
        Assert.Equal(TimeSpan.FromSeconds(2.5), LibraryPlayerMode.VisualPlayPosition(
            TimeSpan.FromSeconds(1),
            elapsedSeconds: 1.5,
            speed: 1,
            TimeSpan.FromSeconds(10)));
        Assert.Equal(TimeSpan.FromSeconds(10), LibraryPlayerMode.VisualPlayPosition(
            TimeSpan.Zero,
            elapsedSeconds: 20,
            speed: 1,
            TimeSpan.FromSeconds(10)));
        Assert.Equal(TimeSpan.FromSeconds(3), LibraryPlayerMode.VisualPlayPosition(
            TimeSpan.FromSeconds(3),
            elapsedSeconds: 2,
            speed: 0,
            TimeSpan.FromSeconds(10)));
        Assert.Equal(TimeSpan.FromSeconds(1), LibraryPlayerMode.VisualPlayPosition(
            TimeSpan.Zero,
            elapsedSeconds: 4,
            speed: 0.25,
            TimeSpan.FromSeconds(10)));
        Assert.Equal(1, LibraryPlayerMode.PreviewSpeedAtRampProgress(0), 5);
        Assert.Equal(LibraryVisualStage.PreviewLoopSpeed, LibraryPlayerMode.PreviewSpeedAtRampProgress(1), 5);
        Assert.Equal(1, LibraryVisualStage.PreviewSpeedRampSeconds);
        Assert.Equal(50, LibraryVisualStage.PreviewSpeedRampTickMs);
        // EaseOut: 中盤は線形より 1/3 に近い。
        Assert.True(
            LibraryPlayerMode.PreviewSpeedAtRampProgress(0.5)
            < (1 + LibraryVisualStage.PreviewLoopSpeed) * 0.5);
        Assert.Equal(TimeSpan.FromSeconds(10), LibraryPlayerMode.VisualDurationOrFallback(
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(3)));
        Assert.Equal(TimeSpan.FromSeconds(3), LibraryPlayerMode.VisualDurationOrFallback(
            TimeSpan.Zero,
            TimeSpan.FromSeconds(3)));
        Assert.Equal(TimeSpan.Zero, LibraryPlayerMode.VisualDurationOrFallback(TimeSpan.Zero, TimeSpan.Zero));
        Assert.Equal(100, LibraryPlayerMode.WrapLoopFrame(100, 100, 200));
        Assert.Equal(199, LibraryPlayerMode.WrapLoopFrame(199, 100, 200));
        Assert.Equal(100, LibraryPlayerMode.WrapLoopFrame(200, 100, 200));
        Assert.Equal(110, LibraryPlayerMode.WrapLoopFrame(210, 100, 200));
        Assert.Equal(100, LibraryPlayerMode.WrapLoopFrame(50, 100, 200));
        Assert.Equal(50, LibraryPlayerMode.WrapLoopFrame(50, 100, 100));
        Assert.True(LibraryPlayerMode.NeedsLoopSeek(200, 100, 200));
        Assert.True(LibraryPlayerMode.NeedsLoopSeek(50, 100, 200));
        Assert.False(LibraryPlayerMode.NeedsLoopSeek(150, 100, 200));
        Assert.False(LibraryPlayerMode.NeedsLoopSeek(150, 100, 100));
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
        Assert.False(LibraryPlayerMode.BlocksTransport(TransportCommand.SaveMp3));
        Assert.True(LibraryPlayerMode.BlocksTransport(TransportCommand.CenterPlayhead));
        Assert.True(LibraryPlayerMode.BlocksTransport(TransportCommand.ToggleSpectrogram));
        Assert.True(LibraryPlayerMode.BlocksTransport(TransportCommand.ToggleLoudnessView));
        Assert.True(LibraryPlayerMode.BlocksTransport(TransportCommand.ToggleAnalysis));
        Assert.True(LibraryPlayerMode.BlocksTransport(TransportCommand.Record));
        Assert.True(LibraryPlayerMode.BlocksTransport(TransportCommand.FadeIn));
        Assert.True(LibraryPlayerMode.BlocksTransport(TransportCommand.TimeZoomIn));
        Assert.True(LibraryPlayerMode.BlocksTransport(TransportCommand.NewDocument));
        Assert.True(LibraryPlayerMode.BlocksTransport(TransportCommand.ToggleWaapi));
    }

    [Fact]
    public void AllowsKey_KeepsJumpAndPlay_RejectsEdit()
    {
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Space, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Enter, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.F8, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.F9, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Left, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Left, ModifierKeys.Control));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Right, ModifierKeys.Control));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Down, ModifierKeys.Control));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Up, ModifierKeys.Control));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.Left, ModifierKeys.Shift));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Left, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Right, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.True(LibraryPlayerMode.IsPaneFocusArrow(Key.Left, ModifierKeys.Control));
        Assert.True(LibraryPlayerMode.IsPaneFocusArrow(Key.Right, ModifierKeys.Control));
        Assert.True(LibraryPlayerMode.IsPaneFocusArrow(Key.Down, ModifierKeys.Control));
        Assert.True(LibraryPlayerMode.IsPaneFocusArrow(Key.Up, ModifierKeys.Control));
        Assert.False(LibraryPlayerMode.IsPaneFocusArrow(Key.Left, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.UsesPlayerMarkerShortcuts(videoMiniPlayer: true, videoRealPlayback: false));
        Assert.True(LibraryPlayerMode.UsesPlayerMarkerShortcuts(videoMiniPlayer: false, videoRealPlayback: true));
        Assert.True(LibraryPlayerMode.UsesPlayerMarkerShortcuts(
            videoMiniPlayer: false,
            videoRealPlayback: false,
            videoImmersivePause: true));
        Assert.False(LibraryPlayerMode.UsesPlayerMarkerShortcuts(videoMiniPlayer: false, videoRealPlayback: false));
        Assert.True(LibraryPlayerMode.UsesPlayerMarkerShortcuts(
            videoMiniPlayer: false,
            videoRealPlayback: false,
            audioWaveform: true));
        Assert.True(LibraryPlayerMode.BlocksEditorMarkerPlacement(playerMode: true));
        Assert.False(LibraryPlayerMode.BlocksEditorMarkerPlacement(playerMode: false));
        Assert.True(LibraryPlayerMode.IsPlayerScratchCueKey(Key.M, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.IsPlayerScratchCueKey(Key.Insert, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.IsPlayerScratchCueKey(Key.I, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.IsPlayerScratchCueKey(Key.O, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.IsPlayerScratchCueKey(Key.M, ModifierKeys.Shift));
        Assert.False(LibraryPlayerMode.AllowsPlayerScratchCueKey(Key.M, ModifierKeys.None, explorerTreeFocused: true));
        Assert.False(LibraryPlayerMode.AllowsPlayerScratchCueKey(Key.I, ModifierKeys.None, explorerTreeFocused: true));
        Assert.False(LibraryPlayerMode.AllowsPlayerScratchCueKey(Key.O, ModifierKeys.None, explorerTreeFocused: true));
        Assert.True(LibraryPlayerMode.AllowsPlayerScratchCueKey(Key.Insert, ModifierKeys.None, explorerTreeFocused: true));
        Assert.True(LibraryPlayerMode.AllowsPlayerScratchCueKey(Key.M, ModifierKeys.None, explorerTreeFocused: false));
        Assert.True(LibraryPlayerMode.AllowsPlayerScratchCueKey(Key.I, ModifierKeys.None, explorerTreeFocused: false));
        Assert.True(LibraryPlayerMode.AllowsPlayerScratchCueKey(Key.O, ModifierKeys.None, explorerTreeFocused: false));
        Assert.False(LibraryPlayerMode.AllowsPlayerScratchCueKey(Key.T, ModifierKeys.None, explorerTreeFocused: false));
        Assert.True(LibraryPlayerMode.ShowsPlayerScratchOverlays(
            playerMode: true,
            videoMiniPlayer: false,
            videoRealPlayback: false,
            videoImmersivePause: true));
        Assert.True(LibraryPlayerMode.PrefersMarkerArrowJump(playerMarkerContext: true, hasScratchMarkers: true));
        Assert.False(LibraryPlayerMode.PrefersMarkerArrowJump(playerMarkerContext: true, hasScratchMarkers: false));
        Assert.False(LibraryPlayerMode.PrefersMarkerArrowJump(playerMarkerContext: false, hasScratchMarkers: true));
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
        Assert.True(LibraryPlayerMode.AllowsKey(Key.V, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.V, ModifierKeys.Shift));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.LeftShift, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.F4, ModifierKeys.Alt));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Space, ModifierKeys.Alt));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Q, ModifierKeys.Control));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.O, ModifierKeys.Control));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.O, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.C, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.S, ModifierKeys.Alt));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.A, ModifierKeys.Alt));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.R, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.R, ModifierKeys.Alt));
        Assert.True(LibraryPlayerMode.IsShuffleToggle(Key.R, ModifierKeys.Alt));
        Assert.False(LibraryPlayerMode.IsShuffleToggle(Key.R, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.IsShuffleToggle(Key.R, ModifierKeys.Control));
        Assert.True(LibraryPlayerMode.IsAutoPlayNextToggle(Key.N, ModifierKeys.Alt));
        Assert.False(LibraryPlayerMode.IsAutoPlayNextToggle(Key.N, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.IsAutoPlayNextToggle(Key.N, ModifierKeys.Control));
        Assert.False(LibraryPlayerMode.BlocksWaveMenu(WaveMenuCommand.AutoPlayNext));
        Assert.True(LibraryPlayerMode.IsAutoPlayToggle(Key.P, ModifierKeys.Alt));
        Assert.False(LibraryPlayerMode.IsAutoPlayToggle(Key.P, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.IsAutoPlayToggle(Key.P, ModifierKeys.Control));
        // 検索フォーカス中でも Alt+R／N／P と Ctrl+Shift+P は通す（素の文字キーは検索へ）。
        Assert.True(LibraryPlayerMode.BypassesSearchFocus(Key.R, ModifierKeys.Alt));
        Assert.True(LibraryPlayerMode.BypassesSearchFocus(Key.N, ModifierKeys.Alt));
        Assert.True(LibraryPlayerMode.BypassesSearchFocus(Key.P, ModifierKeys.Alt));
        Assert.True(LibraryPlayerMode.BypassesSearchFocus(
            Key.P, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.False(LibraryPlayerMode.BypassesSearchFocus(Key.N, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.BypassesSearchFocus(Key.R, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.BypassesSearchFocus(Key.A, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.IsPlaylistFolderAddKey(Key.Enter, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.IsPlaylistFolderAddKey(Key.Enter, ModifierKeys.Control));
        Assert.True(LibraryPlayerMode.IsPlaylistFolderAddKey(Key.Enter, ModifierKeys.Shift));
        Assert.True(LibraryPlayerMode.IsPlaylistFolderAddKey(Key.Enter, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.False(LibraryPlayerMode.IsPlaylistFolderAddKey(Key.Enter, ModifierKeys.Alt));
        Assert.True(LibraryPlayerMode.ClearsPlaylistOnFolderAdd(ModifierKeys.None));
        Assert.True(LibraryPlayerMode.ClearsPlaylistOnFolderAdd(ModifierKeys.Control));
        Assert.False(LibraryPlayerMode.ClearsPlaylistOnFolderAdd(ModifierKeys.Shift));
        Assert.False(LibraryPlayerMode.ClearsPlaylistOnFolderAdd(ModifierKeys.Control | ModifierKeys.Shift));
        Assert.False(LibraryPlayerMode.RecursesPlaylistFolderAdd(ModifierKeys.None));
        Assert.True(LibraryPlayerMode.RecursesPlaylistFolderAdd(ModifierKeys.Control));
        Assert.False(LibraryPlayerMode.RecursesPlaylistFolderAdd(ModifierKeys.Shift));
        Assert.True(LibraryPlayerMode.RecursesPlaylistFolderAdd(ModifierKeys.Control | ModifierKeys.Shift));
        LibraryPlayerMode.ResolvePlaylistFolderDrop(
            DragDropKeyStates.None, ModifierKeys.None, out var clearDrop, out var recursiveDrop);
        Assert.True(clearDrop);
        Assert.False(recursiveDrop);
        LibraryPlayerMode.ResolvePlaylistFolderDrop(
            DragDropKeyStates.ControlKey, ModifierKeys.None, out clearDrop, out recursiveDrop);
        Assert.True(clearDrop);
        Assert.True(recursiveDrop);
        LibraryPlayerMode.ResolvePlaylistFolderDrop(
            DragDropKeyStates.ShiftKey, ModifierKeys.None, out clearDrop, out recursiveDrop);
        Assert.False(clearDrop);
        Assert.False(recursiveDrop);
        LibraryPlayerMode.ResolvePlaylistFolderDrop(
            DragDropKeyStates.ShiftKey | DragDropKeyStates.ControlKey,
            ModifierKeys.None,
            out clearDrop,
            out recursiveDrop);
        Assert.False(clearDrop);
        Assert.True(recursiveDrop);
        Assert.False(LibraryPlayerMode.AllowsKey(Key.R, ModifierKeys.Control));
        Assert.True(LibraryPlayerMode.IsPlayerRestartKey(Key.R, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.IsPlayerRestartKey(Key.R, ModifierKeys.Alt));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.J, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.K, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.OemComma, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.OemPeriod, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.IsPlayerShuttleKey(Key.OemComma, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.IsPlayerShuttleKey(Key.OemPeriod, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.N, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.N, ModifierKeys.Alt));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.P, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.P, ModifierKeys.Alt));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.P, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Enter, ModifierKeys.Shift));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Enter, ModifierKeys.Control));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Enter, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.True(LibraryPlayerMode.IsForceVideoProxyEncode(Key.P, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.False(LibraryPlayerMode.IsForceVideoProxyEncode(Key.P, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.IsForceVideoProxyEncode(Key.P, ModifierKeys.Control));
        Assert.True(LibraryPlayerMode.IsPlayerTrackStepKey(Key.P, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.IsPlayerTrackStepKey(Key.N, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.IsPlayerTrackStepKey(Key.P, ModifierKeys.Control));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.I, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.O, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.M, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Insert, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Delete, ModifierKeys.Control));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.R, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Left, ModifierKeys.Alt));
        Assert.True(LibraryPlayerMode.PrefersScratchMarkerDelete(playerMode: true, hasSelectedScratchMarkers: true));
        Assert.False(LibraryPlayerMode.PrefersScratchMarkerDelete(playerMode: true, hasSelectedScratchMarkers: false));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.S, ModifierKeys.Control));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.S, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.M, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.M, ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.Z, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.W, ModifierKeys.Control));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.W, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.W, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.E, ModifierKeys.Alt));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.F4, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.Up, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.Delete, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.C, ModifierKeys.Control));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Z, ModifierKeys.Control));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Z, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Y, ModifierKeys.Control));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.Z, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Right, ModifierKeys.Alt | ModifierKeys.Control));
        Assert.False(LibraryPlayerMode.AllowsKey(Key.G, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Tab, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Tab, ModifierKeys.Shift));
    }

    [Fact]
    public void FilterMp3ExportCandidates_SkipsExistingMp3KeepsOthers()
    {
        var wave = new DocumentSession(AudioDocument.CreateDeferred(@"C:\tmp\a.wav"));
        var mp3 = new DocumentSession(AudioDocument.CreateDeferred(@"C:\tmp\b.mp3"));
        var aiff = new DocumentSession(AudioDocument.CreateDeferred(@"C:\tmp\c.aiff"));
        var filtered = LibraryPlayerMode.FilterMp3ExportCandidates([wave, mp3, aiff]);
        Assert.Equal([wave, aiff], filtered);
        Assert.False(LibraryPlayerMode.AllSessionsAreMp3([wave, mp3, aiff]));
        Assert.True(LibraryPlayerMode.AllSessionsAreMp3([mp3]));
        Assert.False(LibraryPlayerMode.IsMp3ExportCandidate(mp3.Document));
        Assert.True(LibraryPlayerMode.IsMp3ExportCandidate(wave.Document));
        Assert.True(LibraryPlayerMode.HasMp3ExportCandidate([wave, mp3]));
        Assert.False(LibraryPlayerMode.HasMp3ExportCandidate([mp3]));
        Assert.False(LibraryPlayerMode.HasMp3ExportCandidate([]));
    }

    [Fact]
    public void BlocksExplorerKey_StopsFileOps_KeepsNavigation()
    {
        Assert.True(LibraryPlayerMode.BlocksExplorerKey(Key.Delete, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.BlocksExplorerKey(Key.Back, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.Delete, ModifierKeys.Control));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.C, ModifierKeys.Control));
        Assert.True(LibraryPlayerMode.BlocksExplorerKey(Key.X, ModifierKeys.Control));
        Assert.True(LibraryPlayerMode.BlocksExplorerKey(Key.V, ModifierKeys.Control));
        Assert.True(LibraryPlayerMode.BlocksExplorerKey(Key.D, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.Up, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.Up, ModifierKeys.Shift));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.Down, ModifierKeys.Shift));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.Left, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.Enter, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.Enter, ModifierKeys.Shift));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.Enter, ModifierKeys.Control));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.Enter, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.True(LibraryPlayerMode.IsExplorerRootReorder(Key.Up, ModifierKeys.Alt));
        Assert.True(LibraryPlayerMode.IsExplorerRootReorder(Key.Down, ModifierKeys.Alt));
        Assert.False(LibraryPlayerMode.IsExplorerRootReorder(Key.Up, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.Up, ModifierKeys.Alt));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.Down, ModifierKeys.Alt));
        Assert.True(LibraryPlayerMode.BlocksExplorerKey(Key.Space, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.BlocksExplorerKey(Key.Space, ModifierKeys.Shift));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.Space, ModifierKeys.Shift));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.Space, ModifierKeys.Alt));
        Assert.False(LibraryPlayerMode.PlaysLibrarySelectionOnSpace(explorerFocused: true, favoritesFocused: false));
        Assert.False(LibraryPlayerMode.PlaysLibrarySelectionOnSpace(explorerFocused: false, favoritesFocused: true));
        Assert.True(LibraryPlayerMode.PlaysLibrarySelectionOnSpace(explorerFocused: false, favoritesFocused: false));
        Assert.True(LibraryPlayerMode.HaltsLibraryPlaybackOnSpace(playbackActive: false, immersiveVideoPause: true));
        Assert.True(LibraryPlayerMode.HaltsLibraryPlaybackOnSpace(playbackActive: true, immersiveVideoPause: false));
        Assert.False(LibraryPlayerMode.HaltsLibraryPlaybackOnSpace(playbackActive: false, immersiveVideoPause: false));
        // 前面 UI 隠れ中はプレイリストの全選択・行除外を受け付けない。
        Assert.True(LibraryPlayerMode.BlocksPlaylistMutationWhileChromeHidden(chromeVisible: false));
        Assert.False(LibraryPlayerMode.BlocksPlaylistMutationWhileChromeHidden(chromeVisible: true));
        // 本再生中／フレーム一時停止中の F10 はエディタへ落とさずプレイリスト（クローム）へ戻す。
        Assert.True(LibraryPlayerMode.ReturnsToPlaylistChromeOnF10(
            player: true, videoMini: false,
            visualShown: true, visualPlaying: true, isPdf: false, isVideo: true));
        Assert.True(LibraryPlayerMode.ReturnsToPlaylistChromeOnF10(
            player: true, videoMini: false,
            visualShown: true, visualPlaying: true, isPdf: true, isVideo: false));
        Assert.True(LibraryPlayerMode.ReturnsToPlaylistChromeOnF10(
            player: true, videoMini: false,
            visualShown: true, visualPlaying: false, isPdf: false, isVideo: true,
            immersivePauseHold: true));
        Assert.False(LibraryPlayerMode.ReturnsToPlaylistChromeOnF10(
            player: true, videoMini: true,
            visualShown: true, visualPlaying: true, isPdf: false, isVideo: true));
        Assert.False(LibraryPlayerMode.ReturnsToPlaylistChromeOnF10(
            player: true, videoMini: false,
            visualShown: true, visualPlaying: false, isPdf: false, isVideo: true));
        Assert.False(LibraryPlayerMode.ReturnsToPlaylistChromeOnF10(
            player: false, videoMini: false,
            visualShown: true, visualPlaying: true, isPdf: false, isVideo: true));
        Assert.True(LibraryPlayerMode.PromotesDimPreviewBeforePlayLoad(
            visualShown: true, visualPlaying: false, sameSource: true, isVideo: true));
        Assert.False(LibraryPlayerMode.PromotesDimPreviewBeforePlayLoad(
            visualShown: true, visualPlaying: true, sameSource: true, isVideo: true));
        Assert.False(LibraryPlayerMode.PromotesDimPreviewBeforePlayLoad(
            visualShown: true, visualPlaying: false, sameSource: false, isVideo: true));
        Assert.False(LibraryPlayerMode.PromotesDimPreviewBeforePlayLoad(
            visualShown: true, visualPlaying: false, sameSource: true, isVideo: false));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.F1, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.F2, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.F3, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.F5, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.BlocksExplorerKey(Key.F5, ModifierKeys.Control));
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
        Assert.False(LibraryPlayerMode.AllowsPlayerDigitJump(explorerFocused: true));
        Assert.True(LibraryPlayerMode.AllowsPlayerDigitJump(explorerFocused: false));
        Assert.False(LibraryPlayerMode.AllowsPlayerLetterTransport(explorerFocused: true));
        Assert.True(LibraryPlayerMode.AllowsPlayerLetterTransport(explorerFocused: false));
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
    public void IsExplorerRefresh_F5Only()
    {
        Assert.True(LibraryPlayerMode.IsExplorerRefresh(Key.F5, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.IsExplorerRefresh(Key.F5, ModifierKeys.Control));
        Assert.False(LibraryPlayerMode.IsExplorerRefresh(Key.F5, ModifierKeys.Shift));
        Assert.False(LibraryPlayerMode.IsExplorerRefresh(Key.F1, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.IsLibraryCatalogRebuild(Key.F5, ModifierKeys.Control));
        Assert.False(LibraryPlayerMode.IsLibraryCatalogRebuild(Key.F5, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.IsLibraryCatalogRebuild(Key.F5, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.F5, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.F5, ModifierKeys.Control));
    }

    [Fact]
    public void AllowsKey_PlayerPaneFocusKeys()
    {
        Assert.True(LibraryPlayerMode.AllowsKey(Key.F1, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.F2, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.F3, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.AllowsKey(Key.F5, ModifierKeys.None));
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
    public void RefreshesPrimaryForLibraryPeaks_SkipsClearedPrimaryDuringTileHandoff()
    {
        Assert.True(LibraryPlayerMode.RefreshesPrimaryForLibraryPeaks(
            tileMode: false,
            sessionHasTilePane: false));
        Assert.False(LibraryPlayerMode.RefreshesPrimaryForLibraryPeaks(
            tileMode: true,
            sessionHasTilePane: false));
        Assert.False(LibraryPlayerMode.RefreshesPrimaryForLibraryPeaks(
            tileMode: true,
            sessionHasTilePane: true));
        Assert.False(LibraryPlayerMode.RefreshesPrimaryForLibraryPeaks(
            tileMode: false,
            sessionHasTilePane: true));
    }

    [Fact]
    public void IsNewerLibraryPeaks_DoesNotReplaceCompleteWithInProgress()
    {
        const int buckets = 8;
        const int baseBucket = 100;
        var estimate = buckets * baseBucket;
        var actual = estimate - 6;
        var completeMins = new float[buckets];
        var completeMaxs = new float[buckets];
        Array.Fill(completeMins, -0.4f);
        Array.Fill(completeMaxs, 0.4f);
        var complete = PeakPyramid.CreateMonoForTests(completeMins, completeMaxs, actual, baseBucket, actual);
        Assert.False(complete.IsBuilding);

        var partialMins = new float[buckets];
        var partialMaxs = new float[buckets];
        Array.Fill(partialMins, -0.4f, 0, buckets / 2);
        Array.Fill(partialMaxs, 0.4f, 0, buckets / 2);
        var partial = PeakPyramid.CreateMonoForTests(
            partialMins,
            partialMaxs,
            estimate,
            baseBucket,
            filledFrames: estimate / 2);
        Assert.True(partial.IsBuilding);

        Assert.False(LibraryPlayerMode.IsNewerLibraryPeaks(complete, partial));
        Assert.True(LibraryPlayerMode.IsNewerLibraryPeaks(partial, complete));
        Assert.True(LibraryPlayerMode.IsNewerLibraryPeaks(PeakPyramid.Empty, complete));
        Assert.False(LibraryPlayerMode.IsNewerLibraryPeaks(complete, PeakPyramid.Empty));
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
        Assert.False(LibraryPlayerMode.ShowsPlayerMeters(playlistCount: 0));
        Assert.True(LibraryPlayerMode.ShowsPlayerMeters(playlistCount: 1));
        Assert.True(LibraryPlayerMode.ShowsPlayerMeters(playlistCount: 2));
        // 空リスト: HUD オンでも WAAPI／アナライザは隠す。波形ホストは HUD に従う。
        Assert.True(LibraryPlayerMode.HidesPlayerMeterHud(
            hideHud: false, showMeters: false, isMeterChrome: true));
        Assert.False(LibraryPlayerMode.HidesPlayerMeterHud(
            hideHud: false, showMeters: false, isMeterChrome: false));
        Assert.False(LibraryPlayerMode.HidesPlayerMeterHud(
            hideHud: false, showMeters: true, isMeterChrome: true));
        Assert.True(LibraryPlayerMode.HidesPlayerMeterHud(
            hideHud: true, showMeters: true, isMeterChrome: true));
        Assert.False(LibraryPlayerMode.InstantPlayerMeterReveal(show: false, signalMoving: true));
        Assert.False(LibraryPlayerMode.InstantPlayerMeterReveal(show: true, signalMoving: false));
        Assert.True(LibraryPlayerMode.InstantPlayerMeterReveal(show: true, signalMoving: true));
        // アウトは常に 1 秒フェード（instantReveal が残っていても即消ししない）。
        Assert.False(LibraryPlayerMode.InstantPlayerMeterFade(show: false, instantReveal: true));
        Assert.False(LibraryPlayerMode.InstantPlayerMeterFade(show: false, instantReveal: false));
        Assert.True(LibraryPlayerMode.InstantPlayerMeterFade(show: true, instantReveal: true));
        Assert.False(LibraryPlayerMode.InstantPlayerMeterFade(show: true, instantReveal: false));
        Assert.True(LibraryPlayerMode.SnapPlayerMetersHiddenOnEnter(enteringPlayer: true, instantReveal: false));
        Assert.False(LibraryPlayerMode.SnapPlayerMetersHiddenOnEnter(enteringPlayer: true, instantReveal: true));
        Assert.False(LibraryPlayerMode.SnapPlayerMetersHiddenOnEnter(enteringPlayer: false, instantReveal: false));
        Assert.Equal(1, LibraryPlayerMode.MeterFadeSeconds);
        Assert.Equal(0.2, LibraryPlayerMode.ChromeFadeSeconds);
        Assert.True(LibraryPlayerMode.HidesChromeForVideoFileLaunch(
            videoMiniPlayer: true, player: true));
        Assert.False(LibraryPlayerMode.HidesChromeForVideoFileLaunch(
            videoMiniPlayer: true, player: false));
        Assert.False(LibraryPlayerMode.HidesChromeForVideoFileLaunch(
            videoMiniPlayer: false, player: true));
        Assert.True(LibraryPlayerMode.CanToggleVideoMiniPlayer(
            alreadyActive: false, visualShown: true, hasPlayableSession: true));
        Assert.True(LibraryPlayerMode.CanToggleVideoMiniPlayer(
            alreadyActive: true, visualShown: false, hasPlayableSession: false));
        Assert.False(LibraryPlayerMode.CanToggleVideoMiniPlayer(
            alreadyActive: false, visualShown: true, hasPlayableSession: false));
        Assert.False(LibraryPlayerMode.CanToggleVideoMiniPlayer(
            alreadyActive: false, visualShown: false, hasPlayableSession: true));
        // 暗いバックグラウンドプレビュー（表示中・非本再生）からも F8 可。
        Assert.True(LibraryPlayerMode.CanToggleVideoMiniPlayer(
            alreadyActive: false, visualShown: true, hasPlayableSession: true));
        Assert.True(LibraryPlayerMode.HidesChromeForPlaylistVisual(
            player: true, visualShown: true, visualPlaying: true, isPdf: true, isVideo: false));
        Assert.False(LibraryPlayerMode.HidesChromeForPlaylistVisual(
            player: true, visualShown: true, visualPlaying: false, isPdf: true, isVideo: false));
        Assert.False(LibraryPlayerMode.HidesChromeForPlaylistVisual(
            player: true, visualShown: true, visualPlaying: false, isPdf: false, isVideo: true));
        Assert.True(LibraryPlayerMode.HidesChromeForPlaylistVisual(
            player: true, visualShown: true, visualPlaying: false, isPdf: false, isVideo: true, bridgeHold: true));
        Assert.True(LibraryPlayerMode.HidesChromeForPlaylistVisual(
            player: true, visualShown: true, visualPlaying: false, isPdf: false, isVideo: true,
            immersivePauseHold: true));
        Assert.False(LibraryPlayerMode.HidesChromeForPlaylistVisual(
            player: true, visualShown: true, visualPlaying: false, isPdf: false, isVideo: false,
            immersivePauseHold: true));
        Assert.True(LibraryPlayerMode.KeepsChromeHiddenForSettingsWhileVisualPlaying(
            player: true, visualPlaying: true, isVideo: true, isPdf: false));
        Assert.True(LibraryPlayerMode.KeepsChromeHiddenForSettingsWhileVisualPlaying(
            player: true, visualPlaying: true, isVideo: false, isPdf: true));
        Assert.False(LibraryPlayerMode.KeepsChromeHiddenForSettingsWhileVisualPlaying(
            player: true, visualPlaying: false, isVideo: true, isPdf: false));
        Assert.False(LibraryPlayerMode.KeepsChromeHiddenForSettingsWhileVisualPlaying(
            player: false, visualPlaying: true, isVideo: true, isPdf: false));
        Assert.False(LibraryPlayerMode.KeepsChromeHiddenForSettingsWhileVisualPlaying(
            player: true, visualPlaying: true, isVideo: false, isPdf: false));
        Assert.True(LibraryPlayerMode.HoldsPlaylistVisualChromeBridge(
            currentVisualPlaying: true, nextIsVisual: true));
        Assert.False(LibraryPlayerMode.HoldsPlaylistVisualChromeBridge(
            currentVisualPlaying: true, nextIsVisual: false));
        Assert.False(LibraryPlayerMode.HoldsPlaylistVisualChromeBridge(
            currentVisualPlaying: false, nextIsVisual: true));
        Assert.True(LibraryPlayerMode.HoldsPlaylistVisualChromeBridge(
            currentVisualPlaying: false, nextIsVisual: true, immersivePauseHold: true));
        Assert.False(LibraryPlayerMode.HoldsPlaylistVisualChromeBridge(
            currentVisualPlaying: false, nextIsVisual: false, immersivePauseHold: true));
        Assert.False(LibraryPlayerMode.BlocksPlaylistWhilePdf(Key.Up, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.BlocksPlaylistWhilePdf(Key.Down, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.BlocksPlaylistWhilePdf(Key.NumPad4, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.BlocksPlaylistWhilePdf(Key.NumPad5, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.BlocksPlaylistWhilePdf(Key.NumPad6, ModifierKeys.None));
        // Home／End／Page は波形タイムラインへ通す（ページめくりは ←／→ のみ）。
        Assert.False(LibraryPlayerMode.BlocksPlaylistWhilePdf(Key.PageUp, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.BlocksPlaylistWhilePdf(Key.Home, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.IsPdfPageKey(Key.Left, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.IsPdfPageKey(Key.Right, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.IsPdfPageKey(Key.PageUp, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.IsPdfPageKey(Key.Home, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.UsesEditorTimelineNavKeys(chromeVisible: false));
        Assert.False(LibraryPlayerMode.UsesEditorTimelineNavKeys(chromeVisible: true));
        Assert.True(LibraryPlayerMode.HaltsAtEndWithoutPlaylistAdvance(
            chromeVisible: false, visualShown: true, isVideo: true, isPdf: false));
        Assert.True(LibraryPlayerMode.HaltsAtEndWithoutPlaylistAdvance(
            chromeVisible: false, visualShown: true, isVideo: false, isPdf: true));
        Assert.False(LibraryPlayerMode.HaltsAtEndWithoutPlaylistAdvance(
            chromeVisible: true, visualShown: true, isVideo: true, isPdf: false));
        Assert.False(LibraryPlayerMode.HaltsAtEndWithoutPlaylistAdvance(
            chromeVisible: false, visualShown: false, isVideo: false, isPdf: false));
        Assert.True(LibraryPlayerMode.IsPdfPageDigitKey(Key.D3, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.IsPdfPageDigitKey(Key.NumPad3, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.IsPdfPageDigitKey(Key.D3, ModifierKeys.Shift));
        Assert.True(LibraryPlayerMode.ShowsPlaylistPdfPageNumber(chromeHidden: true, isPdf: true));
        Assert.False(LibraryPlayerMode.ShowsPlaylistPdfPageNumber(chromeHidden: false, isPdf: true));
        Assert.False(LibraryPlayerMode.ShowsPlaylistPdfPageNumber(chromeHidden: true, isPdf: false));
        Assert.Equal(3, LibraryPlayerMode.PdfPageNumberToastHoldSeconds);
        Assert.Equal("1 / 3", LibraryPlayerMode.FormatPdfPageLabel(0, 3));
        Assert.Equal("3 / 3", LibraryPlayerMode.FormatPdfPageLabel(2, 3));
        Assert.True(LibraryPlayerMode.ShowsPlaylistTrackIndex(
            chromeHidden: true, isVisual: true, trackCount: 2));
        Assert.False(LibraryPlayerMode.ShowsPlaylistTrackIndex(
            chromeHidden: false, isVisual: true, trackCount: 2));
        Assert.False(LibraryPlayerMode.ShowsPlaylistTrackIndex(
            chromeHidden: true, isVisual: false, trackCount: 2));
        Assert.False(LibraryPlayerMode.ShowsPlaylistTrackIndex(
            chromeHidden: true, isVisual: true, trackCount: 1));
        Assert.Equal("2 / 5", LibraryPlayerMode.FormatPlaylistTrackLabel(1, 5));
        Assert.Equal("1 / 2", LibraryPlayerMode.FormatCurrentOfTotalLabel(0, 2));
        Assert.Equal(
            LibraryPlayerMode.PdfPageNumberToastHoldSeconds,
            LibraryPlayerMode.PlaylistTrackIndexToastHoldSeconds);
        Assert.True(LibraryPlayerMode.TryResolvePdfPageDigitJump(0, 1, 12, out var pdfP1, out var pdfS1));
        Assert.Equal(1, pdfP1);
        Assert.Equal(1, pdfS1);
        Assert.True(LibraryPlayerMode.TryResolvePdfPageDigitJump(pdfP1, 2, 12, out var pdfP2, out var pdfS2));
        Assert.Equal(12, pdfP2);
        Assert.Equal(12, pdfS2);
        Assert.True(LibraryPlayerMode.TryResolvePdfPageDigitJump(1, 0, 12, out var pdfP10, out var pdfS10));
        Assert.Equal(10, pdfP10);
        Assert.Equal(10, pdfS10);
        // 0 単体は 1 と同じ。続けて 0 なら 10。
        Assert.True(LibraryPlayerMode.TryResolvePdfPageDigitJump(0, 0, 12, out var pdfZeroPend, out var pdfZeroSeek));
        Assert.Equal(1, pdfZeroPend);
        Assert.Equal(1, pdfZeroSeek);
        Assert.True(LibraryPlayerMode.TryResolvePdfPageDigitJump(pdfZeroPend, 0, 12, out var pdfP10FromZero, out var pdfS10FromZero));
        Assert.Equal(10, pdfP10FromZero);
        Assert.Equal(10, pdfS10FromZero);
        Assert.False(LibraryPlayerMode.HidesPlaylistVideoChromeOverlaysForPdf(
            isPdf: true, chromeHidden: false));
        Assert.True(LibraryPlayerMode.HidesPlaylistVideoChromeOverlaysForPdf(
            isPdf: true, chromeHidden: true));
        Assert.False(LibraryPlayerMode.HidesPlaylistVideoChromeOverlaysForPdf(
            isPdf: false, chromeHidden: true));
        Assert.True(LibraryPlayerMode.IsVideoFullscreenToggle(Key.F, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.IsVideoFullscreenToggle(Key.F, ModifierKeys.Control));
        Assert.True(LibraryPlayerMode.DefersVideoLaunchPlacementWhileFullscreen(fullscreen: true));
        Assert.False(LibraryPlayerMode.DefersVideoLaunchPlacementWhileFullscreen(fullscreen: false));
        Assert.True(LibraryPlayerMode.ExitsVideoFullscreenBeforeEnterMini(
            playlistVideoFullscreen: true, alreadyVideoMini: false));
        Assert.False(LibraryPlayerMode.ExitsVideoFullscreenBeforeEnterMini(
            playlistVideoFullscreen: true, alreadyVideoMini: true));
        Assert.False(LibraryPlayerMode.ExitsVideoFullscreenBeforeEnterMini(
            playlistVideoFullscreen: false, alreadyVideoMini: false));
        // F10（動画ミニではない）は常に 1。
        Assert.Equal(
            1,
            LibraryPlayerMode.VideoMiniOverlayScale(640, 360, videoMini: false, fullscreen: false));
        Assert.Equal(
            1,
            LibraryPlayerMode.VideoMiniOverlayScale(1920, 1080, videoMini: false, fullscreen: true));
        // 動画ミニでも F 全画面中は倍率 1（列幅・行高は呼び出し側で 1 倍に合わせ直す）。
        Assert.Equal(
            1,
            LibraryPlayerMode.VideoMiniOverlayScale(1920, 1080, videoMini: true, fullscreen: true));
        Assert.Equal(
            1,
            LibraryPlayerMode.VideoMiniOverlayScale(640, 360, videoMini: true, fullscreen: true));
        // 最大化相当（1920×1080）で倍率 1。1280×720 の F8 はそれより小さい（≈2/3）。
        Assert.Equal(
            1,
            LibraryPlayerMode.VideoMiniOverlayScale(1920, 1080, videoMini: true, fullscreen: false),
            3);
        Assert.Equal(
            1280 / 1920d,
            LibraryPlayerMode.VideoMiniOverlayScale(
                1280,
                720,
                videoMini: true,
                fullscreen: false,
                referenceWidth: 1920,
                referenceHeight: 1080),
            3);
        Assert.Equal(
            DesignMetrics.OverlayScaleMin,
            LibraryPlayerMode.VideoMiniOverlayScale(320, 180, videoMini: true, fullscreen: false),
            3);
        // 低い窓では見切れ防止で基準比例より小さく抑える。
        var shortWindow = LibraryPlayerMode.VideoMiniOverlayScale(
            1280, 400, videoMini: true, fullscreen: false);
        Assert.True(shortWindow < 1);
        Assert.True(shortWindow >= DesignMetrics.OverlayScaleMin);
        Assert.Equal(
            DesignMetrics.LibraryWaveformHeight * 0.5,
            LibraryPlayerMode.VideoMiniWaveformHeight(0.5),
            3);
        Assert.Equal(
            DesignMetrics.SpectrumHeight * 0.5,
            LibraryPlayerMode.VideoMiniTransportRowHeight(0.5),
            3);
        // 下段は TransportChromeHeight より小さくてよい（タブ／トランスポート非表示時）。
        Assert.True(
            LibraryPlayerMode.VideoMiniTransportRowHeight(0.5)
            < DesignMetrics.TransportChromeHeight);
        // ピーク LayoutTransform と同じ倍率で列幅も縮む／伸びる。
        var shrink = LibraryPlayerMode.VideoMiniMeterColumnWidths(
            DesignMetrics.LevelMeterWidth,
            DesignMetrics.LevelMeterWidthMax,
            scale: 0.5);
        Assert.Equal(DesignMetrics.LevelMeterWidth * 0.5, shrink.Min, 3);
        Assert.Equal(DesignMetrics.LevelMeterWidth * 0.5, shrink.Width, 3);
        var grow = LibraryPlayerMode.VideoMiniMeterColumnWidths(
            DesignMetrics.LevelMeterWidth,
            DesignMetrics.LevelMeterWidthMax,
            scale: 1.5);
        Assert.Equal(DesignMetrics.LevelMeterWidth * 1.5, grow.Width, 3);
        // 動画ミニ：列実高 − 上段/ゴニオ。16:9 より高い窓は 16:9 相当で打ち切り、低い窓は列高で縮む。
        var reserved = DesignMetrics.ProjectBarHeight + DesignMetrics.VectorScopeHeight;
        var col16x9 = 1080 - DesignMetrics.StatusBarHeight;
        Assert.Equal(
            col16x9 - reserved,
            LibraryPlayerMode.LevelMeterMaxLayoutHeight(col16x9, 1920, 1080, 1),
            3);
        Assert.Equal(
            col16x9 / 1.5 - reserved,
            LibraryPlayerMode.LevelMeterMaxLayoutHeight(col16x9, 1920, 1080, 1.5),
            3);
        var colShort = 600 - DesignMetrics.StatusBarHeight;
        Assert.Equal(
            colShort - reserved,
            LibraryPlayerMode.LevelMeterMaxLayoutHeight(colShort, 1920, 600, 1),
            3);
        var colTall = 1920 - DesignMetrics.StatusBarHeight;
        var aspectCol = colTall + 1080 * 9d / 16d - 1920;
        Assert.Equal(
            aspectCol - reserved,
            LibraryPlayerMode.LevelMeterMaxLayoutHeight(colTall, 1080, 1920, 1),
            3);
        Assert.True(LibraryPlayerMode.LeavesVideoMiniToPlaylist(Key.Q, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.LeavesVideoMiniToPlaylist(Key.Escape, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.LeavesVideoMiniToPlaylist(Key.Q, ModifierKeys.Control));
        Assert.False(LibraryPlayerMode.LeavesVideoMiniToPlaylist(Key.Space, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.IsVideoHudToggle(Key.A, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.IsVideoHudToggle(Key.A, ModifierKeys.Control));
        Assert.True(LibraryPlayerMode.IsVideoTimecodeToggle(Key.T, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.IsVideoTimecodeToggle(Key.T, ModifierKeys.Control));
        Assert.True(LibraryPlayerMode.IsVideoFrameStepKey(Key.Left, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.IsVideoFrameStepKey(Key.Right, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.IsVideoFrameStepKey(Key.Left, ModifierKeys.Shift));
        Assert.True(LibraryPlayerMode.CanStepPlaylistVideoFrame(
            playerMaximized: true, visualShown: true, isVideo: true, visualPlaying: true, immersivePause: false));
        Assert.True(LibraryPlayerMode.CanStepPlaylistVideoFrame(
            playerMaximized: true, visualShown: true, isVideo: true, visualPlaying: false, immersivePause: true));
        Assert.False(LibraryPlayerMode.CanStepPlaylistVideoFrame(
            playerMaximized: true, visualShown: true, isVideo: true, visualPlaying: false, immersivePause: false));
        Assert.Equal(1600, LibraryPlayerMode.VideoFrameStepSamples(48000, 30));
        Assert.Equal(LibraryPlayerMode.VideoFrameStepRepeatDelayMs, LibraryPlayerMode.VideoFrameStepTimerIntervalMs(false));
        Assert.Equal(LibraryPlayerMode.VideoFrameStepRepeatIntervalMs, LibraryPlayerMode.VideoFrameStepTimerIntervalMs(true));
        Assert.True(LibraryPlayerMode.UsesVideoOverlayLabelFore(
            playerMaximized: true, visualShown: true, isVideo: true,
            chromeHidden: true, videoMini: false));
        Assert.True(LibraryPlayerMode.UsesVideoOverlayLabelFore(
            playerMaximized: true, visualShown: true, isVideo: true,
            chromeHidden: false, videoMini: true));
        Assert.False(LibraryPlayerMode.UsesVideoOverlayLabelFore(
            playerMaximized: true, visualShown: true, isVideo: true,
            chromeHidden: false, videoMini: false));
        Assert.False(LibraryPlayerMode.UsesVideoOverlayLabelFore(
            playerMaximized: true, visualShown: true, isVideo: false,
            chromeHidden: true, videoMini: false));
        Assert.Equal(0xF5, LibraryPlayerMode.VideoOverlayLabelColor.R);
        Assert.True(LibraryPlayerMode.IsPlaylistVideoHudContext(
            playerMaximized: true, visualShown: true, isVideo: true,
            visualPlaying: true, immersivePause: false, framePaused: false));
        Assert.True(LibraryPlayerMode.IsPlaylistVideoHudContext(
            playerMaximized: true, visualShown: true, isVideo: true,
            visualPlaying: false, immersivePause: true, framePaused: false));
        Assert.True(LibraryPlayerMode.IsPlaylistVideoHudContext(
            playerMaximized: true, visualShown: true, isVideo: true,
            visualPlaying: false, immersivePause: false, framePaused: true));
        Assert.False(LibraryPlayerMode.IsPlaylistVideoHudContext(
            playerMaximized: true, visualShown: true, isVideo: true,
            visualPlaying: false, immersivePause: false, framePaused: false));
        Assert.True(LibraryPlayerMode.IsPlaylistVideoChromePlaySession(
            videoMiniPlacement: false, isVideo: true,
            visualPlaying: false, immersivePause: true, framePaused: false));
        Assert.False(LibraryPlayerMode.IsPlaylistVideoChromePlaySession(
            videoMiniPlacement: false, isVideo: true,
            visualPlaying: false, immersivePause: false, framePaused: false));
        Assert.True(LibraryPlayerMode.ResetsPlaylistVideoChromeOnPlay(
            videoMiniPlacement: false,
            isVideo: true,
            sessionActive: true,
            wasSessionActive: false,
            sourcePath: @"C:\a.mp4",
            previousSourcePath: null));
        Assert.True(LibraryPlayerMode.ResetsPlaylistVideoChromeOnPlay(
            videoMiniPlacement: false,
            isVideo: true,
            sessionActive: true,
            wasSessionActive: true,
            sourcePath: @"C:\b.mp4",
            previousSourcePath: @"C:\a.mp4"));
        // 一時停止→再開はセッション継続なのでリセットしない。
        Assert.False(LibraryPlayerMode.ResetsPlaylistVideoChromeOnPlay(
            videoMiniPlacement: false,
            isVideo: true,
            sessionActive: true,
            wasSessionActive: true,
            sourcePath: @"C:\a.mp4",
            previousSourcePath: @"C:\a.mp4"));
        Assert.False(LibraryPlayerMode.ResetsPlaylistVideoChromeOnPlay(
            videoMiniPlacement: true,
            isVideo: true,
            sessionActive: true,
            wasSessionActive: false,
            sourcePath: @"C:\a.mp4",
            previousSourcePath: null));
        Assert.False(LibraryPlayerMode.ResetsPlaylistVideoChromeOnPlay(
            videoMiniPlacement: false,
            isVideo: false,
            sessionActive: true,
            wasSessionActive: false,
            sourcePath: null,
            previousSourcePath: null));
        Assert.True(LibraryPlayerMode.ShowsPlaylistVideoTimecode(
            chromeHidden: true, timecodeOn: true, isVideo: true));
        Assert.False(LibraryPlayerMode.ShowsPlaylistVideoTimecode(
            chromeHidden: true, timecodeOn: false, isVideo: true));
        Assert.False(LibraryPlayerMode.ShowsPlaylistVideoTimecode(
            chromeHidden: false, timecodeOn: true, isVideo: true));
        Assert.False(LibraryPlayerMode.ShowsPlaylistVideoTimecode(
            chromeHidden: true, timecodeOn: true, isVideo: false));
        Assert.True(LibraryPlayerMode.ShowsPlaylistVideoFileName(
            chromeHidden: true, hudOn: true, isVideo: true));
        Assert.False(LibraryPlayerMode.ShowsPlaylistVideoFileName(
            chromeHidden: true, hudOn: false, isVideo: true));
        Assert.False(LibraryPlayerMode.ShowsPlaylistVideoFileName(
            chromeHidden: false, hudOn: true, isVideo: true));
        Assert.False(LibraryPlayerMode.ShowsPlaylistVideoFileName(
            chromeHidden: true, hudOn: true, isVideo: false));
        Assert.Equal("clip.mov", LibraryPlayerMode.FormatPlaylistVideoFileName("clip.mov", usingProxy: false));
        Assert.Equal(
            "clip.mov" + LibraryPlayerMode.PlaylistVideoProxySuffix,
            LibraryPlayerMode.FormatPlaylistVideoFileName("clip.mov", usingProxy: true));
        Assert.Equal(string.Empty, LibraryPlayerMode.FormatPlaylistVideoFileName(null, usingProxy: true));
        Assert.Equal(string.Empty, LibraryPlayerMode.FormatPlaylistVideoFileName("", usingProxy: true));
        // ラウドネスに被る分だけ左へ（左端ははみ出さない）。
        Assert.Equal(
            40,
            LibraryPlayerMode.PlaylistVideoFileNameLoudnessDodge(
                new Rect(100, 200, 200, 24),
                new Rect(260, 180, 100, 80),
                minNameLeft: 24,
                gap: 0),
            3);
        Assert.Equal(
            0,
            LibraryPlayerMode.PlaylistVideoFileNameLoudnessDodge(
                new Rect(100, 200, 100, 24),
                new Rect(260, 180, 100, 80),
                minNameLeft: 24,
                gap: 0),
            3);
        Assert.Equal(
            76,
            LibraryPlayerMode.PlaylistVideoFileNameLoudnessDodge(
                new Rect(100, 200, 200, 24),
                new Rect(200, 180, 100, 80),
                minNameLeft: 24,
                gap: 0),
            3);
        Assert.Equal(
            0,
            LibraryPlayerMode.PlaylistVideoFileNameLoudnessDodge(
                new Rect(100, 100, 200, 24),
                new Rect(260, 200, 100, 80),
                minNameLeft: 24,
                gap: 0),
            3);
        Assert.Equal(
            48,
            LibraryPlayerMode.PlaylistVideoFileNameLoudnessDodge(
                new Rect(100, 200, 200, 24),
                new Rect(260, 180, 100, 80),
                minNameLeft: 24,
                gap: 8),
            3);
        Assert.True(LibraryPlayerMode.IsPdfBackgroundPinToggle(Key.W, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.IsPdfBackgroundPinToggle(Key.W, ModifierKeys.Control));
        Assert.False(LibraryPlayerMode.IsPdfBackgroundPinToggle(Key.B, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.RestoresPdfDimPreviewOnExit(backgroundPinned: false));
        Assert.False(LibraryPlayerMode.RestoresPdfDimPreviewOnExit(backgroundPinned: true));
        Assert.True(LibraryPlayerMode.IgnoresSilentSkipForVideo(player: true, videoPlaying: true));
        Assert.False(LibraryPlayerMode.IgnoresSilentSkipForVideo(player: true, videoPlaying: false));
        Assert.False(LibraryPlayerMode.IgnoresSilentSkipForVideo(player: false, videoPlaying: true));
        Assert.Equal(1, LibraryPlayerMode.VideoClockSeekSeconds);
        Assert.True(LibraryPlayerMode.VideoShuttleSeekSeconds > 0);
        Assert.True(LibraryPlayerMode.VideoShuttleSeekSeconds < LibraryPlayerMode.VideoClockSeekSeconds);
        Assert.True(LibraryPlayerMode.VideoSeekIsShuttleScrub(playing: true, visualClockRunning: false));
        Assert.False(LibraryPlayerMode.VideoSeekIsShuttleScrub(playing: true, visualClockRunning: true));
        Assert.False(LibraryPlayerMode.VideoSeekIsShuttleScrub(playing: false, visualClockRunning: false));
        Assert.True(LibraryPlayerMode.DrivesPlayheadWhileVideoShuttles(
            playerMode: true, isVideoFile: true, visualShown: true, shuttling: true, audioPlaying: true));
        Assert.False(LibraryPlayerMode.DrivesPlayheadWhileVideoShuttles(
            playerMode: true, isVideoFile: true, visualShown: true, shuttling: true, audioPlaying: false));
        Assert.False(LibraryPlayerMode.DrivesPlayheadWhileVideoShuttles(
            playerMode: true, isVideoFile: true, visualShown: true, shuttling: false, audioPlaying: true));
        Assert.True(LibraryPlayerMode.BlocksPlaybackShuttleInDimPreview(
            playerMode: true, visualShown: true, isVideo: true, visualPlaying: false, audioPlaying: false));
        Assert.False(LibraryPlayerMode.BlocksPlaybackShuttleInDimPreview(
            playerMode: true, visualShown: true, isVideo: true, visualPlaying: true, audioPlaying: false));
        Assert.False(LibraryPlayerMode.BlocksPlaybackShuttleInDimPreview(
            playerMode: true, visualShown: true, isVideo: true, visualPlaying: false, audioPlaying: true));
        Assert.False(LibraryPlayerMode.BlocksPlaybackShuttleInDimPreview(
            playerMode: false, visualShown: true, isVideo: true, visualPlaying: false, audioPlaying: false));
        Assert.False(LibraryPlayerMode.BlocksPlaybackShuttleInDimPreview(
            playerMode: true, visualShown: true, isVideo: false, visualPlaying: false, audioPlaying: false));
        Assert.False(LibraryPlayerMode.BlocksPlaybackShuttleInDimPreview(
            playerMode: true,
            visualShown: true,
            isVideo: true,
            visualPlaying: false,
            audioPlaying: false,
            frameOrImmersivePaused: true));
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
            var chrome = LibraryPlayerMode.CreateOpacityFade(1, 0, LibraryPlayerMode.ChromeFadeSeconds);
            Assert.Equal(TimeSpan.FromSeconds(0.2), chrome.Duration.TimeSpan);
        });
    }

    [Fact]
    public void SetVideoUiShadow_AddsAndClears()
    {
        RunSta(() =>
        {
            var box = new Rectangle();
            LibraryPlayerMode.SetVideoUiShadow(box, on: true);
            Assert.IsType<DropShadowEffect>(box.Effect);
            LibraryPlayerMode.SetVideoUiShadow(box, on: false);
            Assert.Null(box.Effect);
        });
    }

    [Fact]
    public void SetVideoTextShadow_AddsAndClears()
    {
        RunSta(() =>
        {
            var box = new TextBlock();
            LibraryPlayerMode.SetVideoTextShadow(box, on: true);
            Assert.IsType<DropShadowEffect>(box.Effect);
            LibraryPlayerMode.SetVideoTextShadow(box, on: false);
            Assert.Null(box.Effect);
        });
    }

    [Fact]
    public void FadeElementOpacity_InstantRestoreAfterHeldFadeOut()
    {
        RunSta(() =>
        {
            var box = new Rectangle { Opacity = 1 };
            LibraryPlayerMode.FadeElementOpacity(box, 0, instant: false);
            Assert.True(box.HasAnimatedProperties);
            LibraryPlayerMode.FadeElementOpacity(box, 1, instant: true);
            Assert.Equal(1, box.Opacity);
            Assert.False(box.HasAnimatedProperties);
        });
    }

    [Fact]
    public void FadeElementOpacity_FadeOutThenIn_CrossfadesFromCurrent()
    {
        RunSta(() =>
        {
            var box = new Rectangle { Opacity = 0.4 };
            LibraryPlayerMode.FadeElementOpacity(box, 1, instant: false);
            Assert.True(box.HasAnimatedProperties);
            LibraryPlayerMode.FadeElementOpacity(box, 0, instant: false);
            Assert.True(box.HasAnimatedProperties);
        });
    }

    [Fact]
    public void NeedsForcedOpaque_SkipsInFlightFadeIn()
    {
        RunSta(() =>
        {
            var box = new Rectangle { Opacity = 0 };
            Assert.True(LibraryPlayerMode.NeedsForcedOpaque(box));
            LibraryPlayerMode.FadeElementOpacity(box, 1, instant: false);
            Assert.True(box.HasAnimatedProperties);
            Assert.False(LibraryPlayerMode.NeedsForcedOpaque(box));
            box.BeginAnimation(UIElement.OpacityProperty, null);
            box.Opacity = 0;
            box.IsHitTestVisible = false;
            Assert.True(LibraryPlayerMode.NeedsForcedOpaque(box));
            box.Opacity = 1;
            box.IsHitTestVisible = true;
            Assert.False(LibraryPlayerMode.NeedsForcedOpaque(box));
        });
    }

    [Fact]
    public void HighlightsFormatValue_OnlyKnownNonDefaults()
    {
        Assert.Equal("PlayerFormatMismatchForeBrush", LibraryPlayerMode.FormatMismatchForeBrushKey);
        Assert.Equal(ColorDevGroup.Player, ColorDevCatalog.GroupOf(LibraryPlayerMode.FormatMismatchForeBrushKey));
        Assert.True(UiThemePalette.IsThemeable(LibraryPlayerMode.FormatMismatchForeBrushKey));
        Assert.Equal(
            Color.FromRgb(0xFF, 0x8A, 0x1A),
            UiThemePalette.ColorFor(UiTheme.Dark, LibraryPlayerMode.FormatMismatchForeBrushKey));
        Assert.False(LibraryPlayerMode.HighlightsFormatValue(0, 48000));
        Assert.False(LibraryPlayerMode.HighlightsFormatValue(48000, 48000));
        Assert.True(LibraryPlayerMode.HighlightsFormatValue(44100, 48000));
        Assert.True(LibraryPlayerMode.HighlightsFormatValue(16, 24));
        Assert.True(LibraryPlayerMode.HighlightsFormatValue(1, 2));
        Assert.False(LibraryPlayerMode.HighlightsFormatValue(2, 2));
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
        Assert.Equal(1, LibraryPlayerMode.StepLoopIndex(3, 0, 1));
        Assert.Equal(0, LibraryPlayerMode.StepLoopIndex(3, 2, 1));
        Assert.Equal(2, LibraryPlayerMode.StepLoopIndex(3, 0, -1));
        Assert.Equal(1, LibraryPlayerMode.StepLoopIndex(3, 2, -1));
        Assert.Equal(0, LibraryPlayerMode.StepLoopIndex(3, -1, 1));
        Assert.Equal(2, LibraryPlayerMode.StepLoopIndex(3, -1, -1));
        Assert.Equal(0, LibraryPlayerMode.StepLoopIndex(1, 0, 1));
        Assert.Equal(-1, LibraryPlayerMode.StepLoopIndex(0, 0, 1));
        Assert.Equal(2, LibraryPlayerMode.StepLoopIndex(3, 2, 1, wrap: false));
        Assert.Equal(0, LibraryPlayerMode.StepLoopIndex(3, 0, -1, wrap: false));
        Assert.Equal(1, LibraryPlayerMode.StepLoopIndex(3, 0, 1, wrap: false));
        Assert.Equal(0, LibraryPlayerMode.StepLoopIndex(3, 2, -10, wrap: false));
        Assert.Equal(2, LibraryPlayerMode.StepLoopIndex(3, 0, 10, wrap: false));
    }

    [Fact]
    public void PlayerDigitJump_PercentPlain_MarkerWithShift()
    {
        // プレイヤー／エディタ共通でテンキー再生操作。上段は割合／Shift＋マーカー。
        Assert.True(LibraryPlayerMode.UsesTransportNumpad());

        Assert.True(LibraryPlayerMode.IsPlayerNumpadKey(Key.NumPad7, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.IsPlayerNumpadKey(Key.D7, ModifierKeys.None));
        Assert.Equal(LibraryNumpadCommand.PlayPause, LibraryPlayerMode.PlayerNumpadCommand(Key.NumPad0, ModifierKeys.None));
        Assert.Equal(LibraryNumpadCommand.Rewind, LibraryPlayerMode.PlayerNumpadCommand(Key.NumPad1, ModifierKeys.None));
        Assert.Equal(LibraryNumpadCommand.FastForward, LibraryPlayerMode.PlayerNumpadCommand(Key.NumPad3, ModifierKeys.None));
        Assert.Equal(LibraryNumpadCommand.PreviousTrack, LibraryPlayerMode.PlayerNumpadCommand(Key.NumPad4, ModifierKeys.None));
        Assert.Equal(LibraryNumpadCommand.Restart, LibraryPlayerMode.PlayerNumpadCommand(Key.NumPad5, ModifierKeys.None));
        Assert.Equal(LibraryNumpadCommand.NextTrack, LibraryPlayerMode.PlayerNumpadCommand(Key.NumPad6, ModifierKeys.None));
        Assert.Equal(LibraryNumpadCommand.SeekBack, LibraryPlayerMode.PlayerNumpadCommand(Key.NumPad7, ModifierKeys.None));
        Assert.Equal(LibraryNumpadCommand.SeekForward, LibraryPlayerMode.PlayerNumpadCommand(Key.NumPad9, ModifierKeys.None));
        Assert.Equal(LibraryNumpadCommand.None, LibraryPlayerMode.PlayerNumpadCommand(Key.NumPad2, ModifierKeys.None));
        Assert.Equal(LibraryNumpadCommand.None, LibraryPlayerMode.PlayerNumpadCommand(Key.NumPad8, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.IsPlayerNumpadKey(Key.NumPad2, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.IsPlayerNumpadKey(Key.NumPad8, ModifierKeys.None));
        Assert.Equal(LibraryNumpadCommand.None, LibraryPlayerMode.PlayerNumpadCommand(Key.NumPad7, ModifierKeys.Shift));
        Assert.Equal(LibraryNumpadCommand.None, LibraryPlayerMode.PlayerNumpadCommand(Key.D5, ModifierKeys.None));

        Assert.True(LibraryPlayerMode.IsTopRowDigitKey(Key.D5));
        Assert.False(LibraryPlayerMode.IsTopRowDigitKey(Key.NumPad5));
        Assert.True(LibraryPlayerMode.IsPercentDigitJump(Key.D5, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.IsPercentDigitJump(Key.NumPad5, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.IsDigitJumpChord(Key.NumPad5, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.IsMarkerDigitJump(Key.D5, ModifierKeys.Shift));
        Assert.False(LibraryPlayerMode.IsMarkerDigitJump(Key.NumPad5, ModifierKeys.Shift));
        Assert.False(LibraryPlayerMode.IsDigitJumpChord(Key.NumPad5, ModifierKeys.Shift));
        Assert.True(LibraryPlayerMode.IsRegionDigitJump(Key.D5, ModifierKeys.Control));
        Assert.False(LibraryPlayerMode.IsRegionDigitJump(Key.NumPad5, ModifierKeys.Control));
        Assert.False(LibraryPlayerMode.IsRegionDigitJump(Key.D5, ModifierKeys.Shift));
        Assert.True(LibraryPlayerMode.IsDigitJumpChord(Key.D5, ModifierKeys.Control));
        Assert.True(LibraryPlayerMode.CanPlayerMarkerDigitJump(playerMarkerContext: true, hasScratchMarkers: true));
        Assert.False(LibraryPlayerMode.CanPlayerMarkerDigitJump(playerMarkerContext: true, hasScratchMarkers: false));
        Assert.False(LibraryPlayerMode.CanPlayerMarkerDigitJump(playerMarkerContext: false, hasScratchMarkers: true));

        // 上段の二桁: 1→2 で 12、1→0 で 10。
        Assert.True(LibraryPlayerMode.TryResolveMarkerDigitJump(0, 1, 12, out var p1, out var s1, out var pct1));
        Assert.Equal(1, p1);
        Assert.Equal(1, s1);
        Assert.False(pct1);
        Assert.True(LibraryPlayerMode.TryResolveMarkerDigitJump(p1, 2, 12, out var p2, out var s2, out var pct2));
        Assert.Equal(12, p2);
        Assert.Equal(12, s2);
        Assert.False(pct2);
        Assert.True(LibraryPlayerMode.TryResolveMarkerDigitJump(1, 0, 12, out var p10, out var s10, out var pct10));
        Assert.Equal(10, p10);
        Assert.Equal(10, s10);
        Assert.False(pct10);
        Assert.True(LibraryPlayerMode.TryResolveMarkerDigitJump(1, 2, 3, out var pRestart, out var sRestart, out var pctRestart));
        Assert.Equal(2, pRestart);
        Assert.Equal(2, sRestart);
        Assert.False(pctRestart);
        Assert.True(LibraryPlayerMode.TryResolveMarkerDigitJump(1, 5, 3, out _, out var over, out var pctOver));
        Assert.Null(over);
        Assert.True(pctOver);

        Assert.False(LibraryPlayerMode.IsPlayerShuttleKey(Key.NumPad1, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.IsPlayerShuttleKey(Key.NumPad3, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.IsPlayerShuttleKey(Key.OemComma, ModifierKeys.None));
        Assert.True(LibraryPlayerMode.IsPlayerShuttleKey(Key.OemPeriod, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.IsPlayerShuttleKey(Key.Left, ModifierKeys.None));
        Assert.Equal(LibraryNumpadCommand.Rewind, LibraryPlayerMode.PlayerTransportCommand(Key.OemComma, ModifierKeys.None));
        Assert.Equal(LibraryNumpadCommand.FastForward, LibraryPlayerMode.PlayerTransportCommand(Key.OemPeriod, ModifierKeys.None));
        Assert.Equal(LibraryNumpadCommand.SeekBack, LibraryPlayerMode.PlayerTransportCommand(Key.J, ModifierKeys.None));
        Assert.Equal(LibraryNumpadCommand.SeekForward, LibraryPlayerMode.PlayerTransportCommand(Key.K, ModifierKeys.None));
        Assert.Equal(LibraryNumpadCommand.None, LibraryPlayerMode.PlayerTransportCommand(Key.NumPad1, ModifierKeys.None));
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
        Assert.Equal(16, LibraryPlayerMode.PlaylistLoadUiFlushCount);
        Assert.Equal(33, LibraryPlayerMode.PlaylistLoadUiFlushMs);
        Assert.Equal(50, LibraryPlayerMode.PlayheadIdleInvalidateMs);
        Assert.True(LibraryPlayerMode.ShouldRefreshPlayheadPaint(double.NaN, 0, 10, 0));
        Assert.False(LibraryPlayerMode.ShouldRefreshPlayheadPaint(10, 100, 10.2, 120));
        Assert.True(LibraryPlayerMode.ShouldRefreshPlayheadPaint(10, 100, 10.2, 160));
        Assert.True(LibraryPlayerMode.ShouldRefreshPlayheadPaint(10, 100, 11, 110));
        Assert.True(LibraryPlayerMode.PlayerNumpadRepeatIsHold(LibraryNumpadCommand.SeekForward));
        Assert.True(LibraryPlayerMode.PlayerNumpadRepeatIsHold(LibraryNumpadCommand.SeekBack));
        Assert.True(LibraryPlayerMode.PlayerNumpadRepeatIsHold(LibraryNumpadCommand.Rewind));
        Assert.False(LibraryPlayerMode.PlayerNumpadRepeatIsHold(LibraryNumpadCommand.NextTrack));
        Assert.True(LibraryPlayerMode.PlayerNumpadTrackStepRepeats(LibraryNumpadCommand.PreviousTrack));
        Assert.True(LibraryPlayerMode.PlayerNumpadTrackStepRepeats(LibraryNumpadCommand.NextTrack));
        Assert.False(LibraryPlayerMode.PlayerNumpadTrackStepRepeats(LibraryNumpadCommand.PlayPause));
        Assert.False(LibraryPlayerMode.PlayerNumpadTrackStepRepeats(LibraryNumpadCommand.Restart));
    }

    [Fact]
    public void RegionDigitJump_CtrlTopRow_TwoDigitsFromOne()
    {
        Assert.True(LibraryPlayerMode.TryResolveRegionDigitJump(0, 1, 12, out var p1, out var s1));
        Assert.Equal(1, p1);
        Assert.Equal(1, s1);
        Assert.True(LibraryPlayerMode.TryResolveRegionDigitJump(p1, 2, 12, out var p12, out var s12));
        Assert.Equal(12, p12);
        Assert.Equal(12, s12);
        Assert.True(LibraryPlayerMode.TryResolveRegionDigitJump(1, 0, 12, out var p10, out var s10));
        Assert.Equal(10, p10);
        Assert.Equal(10, s10);

        Assert.True(LibraryPlayerMode.TryResolveRegionDigitJump(1, 6, 15, out var restart, out var seekRestart));
        Assert.Equal(6, restart);
        Assert.Equal(6, seekRestart);

        Assert.True(LibraryPlayerMode.TryResolveRegionDigitJump(1, 5, 3, out var dropped, out var noSeek));
        Assert.Equal(0, dropped);
        Assert.Null(noSeek);
        Assert.False(LibraryPlayerMode.TryResolveRegionDigitJump(0, 1, 0, out _, out _));
        Assert.False(LibraryPlayerMode.IsRegionDigitJump(Key.D1, ModifierKeys.Control | ModifierKeys.Shift));
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
