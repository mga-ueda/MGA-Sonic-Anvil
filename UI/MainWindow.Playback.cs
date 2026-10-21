using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;
using MgaSonicAnvil.Wwise;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
    private void ExecuteTransport(TransportCommand command)
    {
        if (IsLibraryMaximized && LibraryPlayerMode.BlocksTransport(command))
        {
            return;
        }

        switch (command)
        {
            case TransportCommand.TogglePlayback:
                if (IsLibraryMaximized)
                {
                    // エディタの Space と同じ（単独再生／停止で開始位置へ）。曲末の次送りは自動再生チェック。
                    PlayLibrarySelectionOnceOrToggle();
                }
                else
                {
                    TogglePlayback();
                }

                break;
            case TransportCommand.Stop:
                if (IsRecording)
                {
                    StopRecording();
                    break;
                }

                StopPlayback();
                break;
            case TransportCommand.Record:
                ToggleRecording();
                break;
            case TransportCommand.JumpToTime:
                StatusTimes.FocusCurrentTime();
                break;
            case TransportCommand.GoToStart:
                Waveform.ClearSelection();
                SeekFrame(0);
                Waveform.PanTimeToStart();
                break;
            case TransportCommand.PreviousPage:
                Waveform.SeekByVisibleFraction(-0.05);
                break;
            case TransportCommand.NextPage:
                Waveform.SeekByVisibleFraction(0.05);
                break;
            case TransportCommand.PreviousMarker:
                Waveform.SeekToMarker(-1);
                break;
            case TransportCommand.NextMarker:
                Waveform.SeekToMarker(1);
                break;
            case TransportCommand.GoToEnd:
                if (_document is not null)
                {
                    Waveform.ClearSelection();
                    SeekFrame(_document.FrameCount);
                    Waveform.PanTimeToEnd();
                }

                break;
            case TransportCommand.TimeZoomIn:
                Waveform.ZoomTimeIn();
                break;
            case TransportCommand.TimeZoomOut:
                Waveform.ZoomTimeOut();
                break;
            case TransportCommand.TimeZoomMax:
                Waveform.ZoomTimeToMax();
                break;
            case TransportCommand.TimeZoomReset:
                Waveform.ResetTimeZoom();
                break;
            case TransportCommand.AmpZoomIn:
                Waveform.ZoomAmpIn();
                break;
            case TransportCommand.AmpZoomOut:
                Waveform.ZoomAmpOut();
                break;
            case TransportCommand.AmpZoomMax:
                Waveform.ZoomAmpToMax();
                break;
            case TransportCommand.AmpZoomReset:
                Waveform.ResetAmpZoom();
                break;
            case TransportCommand.FadeIn:
                PromptFade(fadeIn: true, Transport.ButtonFor(TransportCommand.FadeIn));
                break;
            case TransportCommand.FadeOut:
                PromptFade(fadeIn: false, Transport.ButtonFor(TransportCommand.FadeOut));
                break;
            case TransportCommand.FadeAround:
                ApplyFadeAroundPlayhead();
                break;
            case TransportCommand.Normalize:
                ApplyNormalize();
                break;
            case TransportCommand.Volume:
                PromptVolume();
                break;
            case TransportCommand.Pitch:
                PromptPitch();
                break;
            case TransportCommand.TimeStretch:
                PromptTimeStretch();
                break;
            case TransportCommand.Reverse:
                ApplyReverse();
                break;
            case TransportCommand.Delete:
                ApplyDelete();
                break;
            case TransportCommand.Undo:
                UndoEdit();
                break;
            case TransportCommand.Redo:
                RedoEdit();
                break;
            case TransportCommand.AddMarker:
                TryAddMarkerAtPlayhead();
                break;
            case TransportCommand.SetLoop:
                SetSampleLoopFromSelection();
                break;
            case TransportCommand.SetRegion:
                TrySetRegionFromSelection();
                break;
            case TransportCommand.ToggleRangeClick:
                ToggleRangeClick();
                break;
            case TransportCommand.NewDocument:
                NewDocument();
                break;
            case TransportCommand.Open:
                OpenFromDialog();
                break;
            case TransportCommand.Save:
                Save(saveAs: false);
                break;
            case TransportCommand.SaveAs:
                Save(saveAs: true);
                break;
            case TransportCommand.SaveMp3:
                ExportMp3FromUi();
                break;
            case TransportCommand.ToggleAnalysis:
            case TransportCommand.ToggleSpectrogram:
                Waveform.CycleSpectrogramView();
                break;
            case TransportCommand.ToggleLoudnessView:
                Waveform.ToggleLoudnessView();
                break;
            case TransportCommand.CenterPlayhead:
                CenterPlayheadOrToggleLock();
                break;
            case TransportCommand.History:
                OpenEditHistory();
                break;
            case TransportCommand.OpenColorPanel:
                ShowColorDevPanel();
                break;
            case TransportCommand.OpenSettings:
                OpenSettings();
                break;
            case TransportCommand.OpenManual:
                ManualViewer.Open(this);
                break;
            case TransportCommand.ToggleLibraryMaximize:
                ToggleLibraryMaximize();
                break;
            case TransportCommand.ToggleAnalyzerMaximize:
                ToggleAnalyzerMaximize();
                break;
            case TransportCommand.ToggleWaapi:
                ToggleWaapiPanel();
                break;
        }
    }

    private void TogglePlayback()
    {
        if (IsRecording)
        {
            StopRecording();
            return;
        }

        if (_document is null)
        {
            return;
        }

        if (IsPlaybackActive())
        {
            HaltPlaybackToStart();
            return;
        }

        StartPlayback(_document.CursorFrame, prerollSeconds: 0);
    }

    private bool IsPlaybackActive() =>
        _player.IsPlaying
        || _player.IsScrubbing
        || _playTimer.IsEnabled
        || LibraryBrowser.PlaylistVisualPlaying;

    private void AttachPlaybackToActiveWaveform()
    {
        if (!_player.IsPlaying)
        {
            return;
        }

        Waveform.SetTrailRecording(true);
        Transport.SetPlaying(true);
        if (!_playTimer.IsEnabled)
        {
            _playTimer.Start();
        }

        StartMeterRendering();
        SyncOverviewPlayhead();
    }

    private bool IsPlaybackShuttleKey(Key key, ModifierKeys modifiers) =>
        LibraryPlayerMode.IsPlayerShuttleKey(key, modifiers)
        || (modifiers == ModifierKeys.None && key is Key.NumPad1 or Key.NumPad3)
        || (!IsLibraryMaximized
            && modifiers == ModifierKeys.None
            && key is Key.Left or Key.Right);

    /// <param name="allowWhileStopped">
    /// true なら停止中の ,／.／テンキー 1／3 を許可（離すとその位置で停止のまま）。
    /// ←→ の再生中シャトルは false のまま（停止中は 1 ステップシーク）。
    /// 暗いバックグラウンド動画プレビュー中は allowWhileStopped でも不可。
    /// </param>
    private bool CanPlaybackShuttle(bool allowWhileStopped = false)
    {
        if (_document is null || _player.IsScrubbing || AnyEffectPreviewing)
        {
            return false;
        }

        if (LibraryPlayerMode.BlocksPlaybackShuttleInDimPreview(
                IsLibraryMaximized,
                LibraryBrowser.PlaylistVisualShown,
                LibraryBrowser.PlaylistVisualIsVideo,
                LibraryBrowser.PlaylistVisualPlaying,
                _player.IsPlaying,
                LibraryBrowser.PlaylistVisualFramePaused || _playlistVideoImmersivePause))
        {
            return false;
        }

        if ((_player.IsPlaying)
            || (LibraryBrowser.PlaylistVisualPlaying && LibraryBrowser.PlaylistVisualShown))
        {
            return true;
        }

        return allowWhileStopped;
    }

    private bool BeginOrContinuePlaybackShuttle(int direction)
    {
        if (direction == 0 || _document is null)
        {
            return false;
        }

        if (_playbackShuttleDirection == direction)
        {
            return true;
        }

        StopSeekNudge();
        StopVideoFrameStep();

        // 停止中は一時的に再生を始め、キーを離したらその位置で止める。
        if (!_player.IsPlaying && !LibraryBrowser.PlaylistVisualPlaying)
        {
            _shuttleStartedWhileStopped = true;
            _shuttleRestoreImmersivePause =
                _playlistVideoImmersivePause || LibraryBrowser.PlaylistVisualFramePaused;
            StartPlayback(_document.CursorFrame, prerollSeconds: 0);
            if (!_player.IsPlaying && !LibraryBrowser.PlaylistVisualPlaying)
            {
                _shuttleStartedWhileStopped = false;
                _shuttleRestoreImmersivePause = false;
                return false;
            }
        }

        if (LibraryBrowser.PlaylistVisualShown && LibraryBrowser.PlaylistVisualIsVideo)
        {
            // 映像は SpeedRatio±3 ではなく、一時停止＋音声ヘッドへの Position 追従。
            _playbackShuttleDirection = direction;
            if (LibraryBrowser.PlaylistVisualPlaying)
            {
                LibraryBrowser.SetPlaylistVisualSpeed(0);
            }

            if (_player.IsPlaying)
            {
                // 本再生中の正は映像クロック。音声ヘッドが開始位置のままだと
                // 追従シークで冒頭へ飛ぶので、速度切替の前に映像位置へ合わせる。
                SyncPlayerCursorToPlaylistVisual();
                _player.SetPlaybackSpeed(
                    direction < 0 ? -PlaybackSampleProvider.FastSpeed : PlaybackSampleProvider.FastSpeed);
                if (!_playTimer.IsEnabled)
                {
                    _playTimer.Start();
                }

                EnsureLibraryVisualPlayheadTicker();
                StartMeterRendering();
            }

            return true;
        }

        _playbackShuttleDirection = direction;
        _player.SetPlaybackSpeed(
            direction < 0 ? -PlaybackSampleProvider.FastSpeed : PlaybackSampleProvider.FastSpeed);
        return true;
    }

    private void StopPlaybackShuttle()
    {
        if (_playbackShuttleDirection == 0 && !_shuttleStartedWhileStopped)
        {
            return;
        }

        _playbackShuttleDirection = 0;
        // 本再生中だけ 1 倍へ戻す。暗い 1/3 速プレビュー表示中に触ると通常速に化ける。
        if (LibraryBrowser.PlaylistVisualShown && LibraryBrowser.PlaylistVisualPlaying)
        {
            LibraryBrowser.SetPlaylistVisualSpeed(1);
        }

        _player.SetPlaybackSpeed(1);

        if (!_shuttleStartedWhileStopped)
        {
            return;
        }

        // 停止中から始めたシャトル：離した位置で停止のまま（開始位置へは戻さない）。
        var immersive = _shuttleRestoreImmersivePause;
        _shuttleStartedWhileStopped = false;
        _shuttleRestoreImmersivePause = false;
        if (_player.IsPlaying || LibraryBrowser.PlaylistVisualPlaying)
        {
            PausePlaybackHere(immersiveVideoHold: immersive);
        }
    }

    /// <summary>映像クロックの位置へ音声ヘッドを合わせる（動画シャトル開始時）。</summary>
    private void SyncPlayerCursorToPlaylistVisual()
    {
        if (_document is null
            || !LibraryBrowser.PlaylistVisualShown
            || !LibraryBrowser.PlaylistVisualIsVideo)
        {
            return;
        }

        var frame = LibraryPlayerMode.FrameFromSeconds(
            LibraryBrowser.PlaylistVisualPosition.TotalSeconds,
            _document.SampleRate,
            _document.FrameCount);
        _document.CursorFrame = frame;
        Waveform.PlayheadFrame = frame;
        _player.Seek(frame);
    }

    private void DetachOverviewScrubKeepPlayback()
    {
        Overview.CancelDrag();
        ApplyOverviewViewToWaveform();
        if (Waveform.IsScrubbing)
        {
            Waveform.AbandonScrub();
        }

        if (_player.IsScrubbing)
        {
            _player.EndScrub();
        }

        _resumeAfterScrub = false;
        if (_player.IsPlaying)
        {
            _playTimer.Start();
        }
    }

    private void StartPrerollPlayback()
    {
        if (_document is null)
        {
            return;
        }

        var frame = Math.Max(0, _document.CursorFrame - _document.SampleRate * 3L);
        StartPlayback(frame, prerollSeconds: 3);
    }

    private void RestartFromLastPlaybackStart()
    {
        if (_document is null)
        {
            return;
        }

        StartPlayback(_lastPlaybackStart, prerollSeconds: 0);
    }

    private void StartPlayback(long startFrame, double prerollSeconds)
    {
        if (_document is null)
        {
            return;
        }

        // 再開後に Enter で止められるよう、一時停止ホールドはここで外す（再生開始でクロームは隠れる）。
        ClearPlaylistVideoImmersivePause(sync: false);

        if (IsLibraryMaximized && LibraryPlaylistDocuments.IsVisual(_document))
        {
            _lastPlaybackStart = startFrame;
            var rate = Math.Max(1, _document.SampleRate);
            var target = TimeSpan.FromSeconds(startFrame / (double)rate);
            var video = LibraryPlaylistDocuments.IsVideo(_document);
            var sameVideo = video
                && LibraryBrowser.PlaylistVisualShown
                && LibraryBrowser.PlaylistVisualIsVideo
                && _activeSession is not null
                && LibraryBrowser.IsPlaylistVisualSameSource(_activeSession.Document.SourcePath);
            if (!(sameVideo && LibraryBrowser.PlaylistVisualPlaying))
            {
                LibraryBrowser.ShowPlaylistVisual(_activeSession!, play: true);
            }

            // プレビューから本再生へ戻した直後の再シークは、時間軸を一度戻してから進める。
            var drifted = !sameVideo
                || Math.Abs((LibraryBrowser.PlaylistVisualPosition - target).TotalMilliseconds) > 200;
            if (drifted)
            {
                LibraryBrowser.SeekPlaylistVisual(target);
            }

            if (!video)
            {
                Transport.SetPlaying(true);
                return;
            }
        }
        if (!IsLibraryMaximized
            && _activeSession is { } session
            && LibraryPlayerMode.NeedsEditorPcmUpgrade(session.Document))
        {
            _ = StartPlaybackAfterEditorPcmAsync(session, startFrame, prerollSeconds);
            return;
        }

        // プレイヤーの WAVE / AIFF はメモリ早戻しのため、ストリームのまま再生しない。
        if (IsLibraryMaximized
            && _activeSession is { } playerSession
            && AudioCodec.IsUncompressedPcm(playerSession.Document.SourceKind)
            && (playerSession.Document.IsDeferredLoad || playerSession.Document.IsStreamPlayback))
        {
            _ = StartPlaybackAfterPlayerPcmAsync(playerSession, startFrame, prerollSeconds);
            return;
        }

        WaveSelection? playRange = ResolvePlayLoopRange();
        if (playRange is { } range && (startFrame < range.StartFrame || startFrame >= range.EndFrame))
        {
            startFrame = range.StartFrame;
        }

        var gapless = Mp3Gapless.ResolveWindow(_document, _document.FrameCount, playRange);
        startFrame = Mp3Gapless.ClampStart(startFrame, gapless, _document.FrameCount);

        _lastPlaybackStart = startFrame;

        _ = prerollSeconds;
        ReleaseStuckScrub();
        try
        {
            _meter.Reset();
            LoudnessMeter.ResetLive();
            _player.Prepare(
                _document,
                startFrame,
                playRange,
                loop: playRange is not null,
                preferStream: IsLibraryMaximized);
            _player.SetExitSpan(ComputeExitLayerSpan(playRange));
            SyncRangeClicks();
            _player.Play();
            _playbackGeneration = _player.Generation;
            EnsureLibraryVisualPlaybackClock();
            Waveform.PlayheadFrame = startFrame;
            SyncOverviewPlayhead();
        }
        catch (Exception ex)
        {
            PausePlaybackSoft();
            if (IsLibraryMaximized && LibraryPlaylistDocuments.IsVideo(_document))
            {
                LibraryBrowser.ShowPlaylistVisual(_activeSession!, play: true);
                LibraryBrowser.SeekPlaylistVisual(
                    TimeSpan.FromSeconds(startFrame / (double)Math.Max(1, _document.SampleRate)));
                EnsureLibraryVisualPlaybackClock();
                Waveform.PlayheadFrame = startFrame;
                return;
            }

            OwnerCenteredMessageBox.Show(this, ex.Message, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task StartPlaybackAfterEditorPcmAsync(
        DocumentSession session,
        long startFrame,
        double prerollSeconds)
    {
        var ticket = ++_editorPlayAfterPcmTicket;
        await EnsureLibrarySessionLoadedAsync(session).ConfigureAwait(true);
        if (ticket != _editorPlayAfterPcmTicket
            || IsLibraryMaximized
            || !ReferenceEquals(_activeSession, session)
            || _document is null
            || LibraryPlayerMode.NeedsEditorPcmUpgrade(session.Document))
        {
            return;
        }

        StartPlayback(startFrame, prerollSeconds);
    }

    private async Task StartPlaybackAfterPlayerPcmAsync(
        DocumentSession session,
        long startFrame,
        double prerollSeconds)
    {
        var ticket = ++_editorPlayAfterPcmTicket;
        await EnsureLibrarySessionLoadedAsync(session).ConfigureAwait(true);
        if (ticket != _editorPlayAfterPcmTicket
            || !IsLibraryMaximized
            || !ReferenceEquals(_activeSession, session)
            || _document is null
            || session.Document.IsDeferredLoad
            || session.Document.IsStreamPlayback)
        {
            return;
        }

        StartPlayback(startFrame, prerollSeconds);
    }

    /// <summary>
    /// エディタで PCM のまま再生している圧縮曲を、プレイヤーへ戻したときにストリーム可変速へ戻す。
    /// WAVE / AIFF はメモリ早戻しのまま。波形用の PCM とピークは捨てない。
    /// </summary>
    private void ResumePlayerVariableRate()
    {
        if (_document is null || !IsPlaybackActive() || _player.IsStreamBound)
        {
            return;
        }

        if (!AudioPlayer.ShouldBindPlaybackStream(_document, preferStream: true))
        {
            return;
        }

        WaveSelection? playRange = ResolvePlayLoopRange();
        var frame = _player.SmoothCursorFrame;
        try
        {
            _player.Rebind(
                _document,
                frame,
                playRange,
                loop: playRange is not null,
                preferStream: true);
            _player.SetExitSpan(ComputeExitLayerSpan(playRange));
            _player.Play();
            _playbackGeneration = _player.Generation;
            _playTimer.Start();
            StartMeterRendering();
        }
        catch (Exception ex)
        {
            PausePlaybackSoft();
            OwnerCenteredMessageBox.Show(this, ex.Message, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void StopPlayback() => HaltPlaybackToStart();

    private void StopPlaybackForExport()
    {
        if (!IsPlaybackActive())
        {
            return;
        }

        PausePlaybackSoft();
    }

    private bool PausePlaybackHere(bool immersiveVideoHold = false)
    {
        if (_document is null || !IsPlaybackActive())
        {
            return false;
        }

        var frame = LibraryPlaylistDocuments.IsPdf(_document)
            ? _document.CursorFrame
            : _player.CursorFrame;
        if (_pitchPreview.Previewing)
        {
            frame += _pitchPreview.Origin;
        }

        if (_timeStretchPreview.Previewing)
        {
            frame += _timeStretchPreview.Origin;
        }

        _fadePreview.Previewing = false;
        _volumePreview.Previewing = false;
        _pitchPreview.Previewing = false;
        _timeStretchPreview.Previewing = false;
        // テンキー 0 はフレーム一時停止＋クローム維持。Enter はその場停止でクロームを戻す。
        // 動画はどちらも静止（暗い 1/3 速へは落とさない。バックグラウンドへ戻すのは Escape）。
        if (immersiveVideoHold)
        {
            _playlistVideoImmersivePause = true;
            PausePlaybackSoft(freezePlaylistVisual: true);
        }
        else
        {
            ClearPlaylistVideoImmersivePause(sync: false);
            var freezeVideo = IsLibraryMaximized
                && LibraryBrowser.PlaylistVisualShown
                && LibraryBrowser.PlaylistVisualIsVideo;
            PausePlaybackSoft(freezePlaylistVisual: freezeVideo);
        }

        SeekFrame(frame);
        RestoreFadeVisualIfMenuOpen();
        RestoreVolumeVisualIfMenuOpen();
        return true;
    }

    private void HaltPlaybackToStart(bool keepVideoChromeHidden = false)
    {
        _editorPlayAfterPcmTicket++;
        // Space の停止は開始位置へ戻す（即再開しない）。動画／PDF 本再生中はプレイリストを出さない。
        // 暗いプレビューへ落とすのは Escape。
        var videoShown = IsLibraryMaximized
            && LibraryBrowser.PlaylistVisualShown
            && LibraryBrowser.PlaylistVisualIsVideo;
        var visualShown = videoShown
            || (IsLibraryMaximized
                && LibraryBrowser.PlaylistVisualShown
                && LibraryBrowser.PlaylistVisualIsPdf);
        if (keepVideoChromeHidden && visualShown)
        {
            _playlistVideoImmersivePause = true;
        }
        else
        {
            ClearPlaylistVideoImmersivePause(sync: false);
        }

        // 動画本再生の Space／Enter 停止は常に静止（暗いプレビューへ落とさない）。
        PausePlaybackSoft(freezePlaylistVisual: videoShown);

        if (_document is null)
        {
            return;
        }

        SeekFrame(_lastPlaybackStart);
        SyncPlaylistVideoChromeFade();
    }

    /// <summary>
    /// 動画本再生／フレーム一時停止から、止めた位置の暗い 1/3 速プレビューへ。
    /// Escape／F10／F8 解除用。Space／Enter では呼ばない。
    /// </summary>
    private void DropPlaylistVideoToDimPreview()
    {
        _editorPlayAfterPcmTicket++;
        ClearPlaylistVideoImmersivePause(sync: false);
        var videoShown = IsLibraryMaximized
            && LibraryBrowser.PlaylistVisualShown
            && LibraryBrowser.PlaylistVisualIsVideo;
        if (!videoShown)
        {
            HaltPlaybackToStart();
            return;
        }

        var pos = LibraryBrowser.PlaylistVisualPosition;
        PausePlaybackSoft(keepPlaylistVisual: true);
        if (_document is not null)
        {
            var rate = Math.Max(1, _document.SampleRate);
            var frame = Math.Clamp(
                (long)Math.Round(pos.TotalSeconds * rate),
                0,
                _document.FrameCount);
            _document.CursorFrame = frame;
            Waveform.PlayheadFrame = frame;
        }

        LibraryBrowser.EnterPlaylistVisualDimPreview(pos);
        SyncOverviewPlayhead();
        SyncPlaylistVideoChromeFade();
        RefreshStatus();
    }

    private void SeekFrame(long frame, int crossfadeMilliseconds = 0)
    {
        if (_document is null)
        {
            return;
        }

        frame = Math.Clamp(frame, 0, _document.FrameCount);
        _document.CursorFrame = frame;
        Waveform.PlayheadFrame = frame;
        if (IsLibraryMaximized && LibraryPlaylistDocuments.IsPdf(_document))
        {
            LibraryBrowser.SeekPlaylistVisual(
                TimeSpan.FromSeconds(frame / (double)Math.Max(1, _document.SampleRate)));
            SyncOverviewPlayhead();
            RefreshStatus();
            return;
        }

        if (IsLibraryMaximized && LibraryPlaylistDocuments.IsVideo(_document))
        {
            LibraryBrowser.SeekPlaylistVisual(
                TimeSpan.FromSeconds(frame / (double)Math.Max(1, _document.SampleRate)));
        }

        if (_player.IsPlaying)
        {
            SyncPlayWindowFromDocument();
        }

        if (crossfadeMilliseconds > 0 && _player.IsPlaying)
        {
            _player.SeekWithCrossfade(frame, crossfadeMilliseconds);
        }
        else
        {
            _player.Seek(frame);
        }

        SyncOverviewPlayhead();
        RefreshStatus();
    }

    private void SyncOverviewPlayhead()
    {
        Overview.SyncPlayhead(_document is null ? -1 : Waveform.ExitPlayheadFrame);
    }

    private void OnCursorCommitted(long frame)
    {
        if (_document is null)
        {
            return;
        }

        NotePlaylistVideoSimpleSeekKeyboard();
        _document.CursorFrame = frame;
        Waveform.PlayheadFrame = frame;
        if (IsLibraryMaximized && LibraryPlaylistDocuments.IsPdf(_document))
        {
            LibraryBrowser.SeekPlaylistVisual(
                TimeSpan.FromSeconds(frame / (double)Math.Max(1, _document.SampleRate)));
            SyncOverviewPlayhead();
            RefreshStatus();
            return;
        }

        if (IsLibraryMaximized && LibraryPlaylistDocuments.IsVideo(_document))
        {
            LibraryBrowser.SeekPlaylistVisual(
                TimeSpan.FromSeconds(frame / (double)Math.Max(1, _document.SampleRate)));
        }

        if (_player.IsPlaying && !Waveform.IsInteracting)
        {
            SyncPlayWindowFromDocument();
            _player.Seek(frame);
        }

        SyncOverviewPlayhead();
        RefreshStatus();
    }

    private void SyncPlayWindowFromDocument()
    {
        if (_document is null)
        {
            return;
        }

        var playRange = ResolvePlayLoopRange();
        if (playRange is null)
        {
            _player.SetPlayWindow(null, loop: false);
            _player.SetExitSpan(null);
            return;
        }

        _player.SetPlayWindow(playRange, loop: true);
        _player.SetExitSpan(ComputeExitLayerSpan(playRange));
    }

    /// <summary>
    /// プレイヤー A-B が揃っていればそれを優先。なければ選択範囲ループ。
    /// どちらも無く Auto Loop オンなら全長ループ（Auto Next より優先）。
    /// </summary>
    private WaveSelection? ResolvePlayLoopRange()
    {
        if (_document is null)
        {
            return null;
        }

        if (IsLibraryMaximized && Waveform.TryGetPlayerAbLoop(out var ab))
        {
            return ab;
        }

        if (!_document.Selection.IsEmpty)
        {
            return _document.Selection;
        }

        if (IsLibraryMaximized
            && LibraryBrowser.AutoLoopEnabled
            && _document.FrameCount > 0)
        {
            return new WaveSelection(0, _document.FrameCount);
        }

        return null;
    }

    /// <summary>
    /// 再生ウィンドウ終端に接する -E（Exit）区間。Play -E のループ折り返し二重再生対象。
    /// 連続する Exit リージョンは 1 区間へつなげる。該当がなければ null。
    /// </summary>
    private WaveSelection? ComputeExitLayerSpan(WaveSelection? window)
    {
        if (_document is null || window is not { IsEmpty: false } range)
        {
            return null;
        }

        long start = -1;
        long end = -1;
        foreach (var region in WaveOnlyPlanBuilder.BuildRegions(_document))
        {
            if (region.Kind != WaveOnlyRegionKind.Exit || region.FrameCount <= 0)
            {
                continue;
            }

            if (start < 0)
            {
                if (region.StartFrame == range.EndFrame)
                {
                    start = region.StartFrame;
                    end = region.EndFrame;
                }
            }
            else if (region.StartFrame == end)
            {
                end = region.EndFrame;
            }
            else
            {
                break;
            }
        }

        return start >= 0 ? new WaveSelection(start, end) : null;
    }

    /// <summary>
    /// マーカー／リージョン端を再生時にクリックで鳴らす（選択があればその範囲、なければファイル全体）。
    /// 拍数が 4 で割れるなら Hi Low Low Low、3 なら Hi Low Low。
    /// オン／オフはユーザー操作のみ（既定オフ。自動ではオンにもオフにもしない）。
    /// </summary>
    private void ToggleRangeClick()
    {
        _rangeClickEnabled = !_rangeClickEnabled;
        Transport.SetRangeClickEnabled(_rangeClickEnabled);
        SyncRangeClicks();
        Waveform.Focus();
    }

    private void SyncRangeClicks()
    {
        if (!_rangeClickEnabled || IsLibraryMaximized || _document is null)
        {
            _player.SetRangeClickFrames([]);
            return;
        }

        var frames = DocumentRangeDivide.ClickFrames(_document);
        var range = DocumentRangeDivide.ClickRange(_document);
        var group = RangeClickMeter.GroupSize(
            RangeClickMeter.BeatCount(frames, range));
        _player.SetRangeClickFrames(frames, group);
    }

    private void OnWaveformSelectionChanged()
    {
        SyncRangeClicks();

        Overview.InvalidateVisual();
        RefreshStatus();
        if (_document is null || !IsPlaybackActive())
        {
            _ = Waveform.TakePendingSelectionPrerollJump();
            return;
        }

        var selection = _document.Selection;
        if (selection.IsEmpty)
        {
            if (!Waveform.IsInteracting)
            {
                SyncPlayWindowFromDocument();
            }

            _ = Waveform.TakePendingSelectionPrerollJump();
            return;
        }

        if (Waveform.TakePendingSelectionPrerollJump())
        {
            JumpPlaybackToSelectionPreroll(selection);
            return;
        }

        var cursor = _player.IsPlaying ? _player.CursorFrame : Waveform.PlayheadFrame;
        var inside = cursor >= selection.StartFrame && cursor < selection.EndFrame;
        if (inside)
        {
            SyncPlayWindowFromDocument();
            return;
        }

        if (Waveform.IsInteracting)
        {
            return;
        }

        _lastPlaybackStart = selection.StartFrame;
        Waveform.SeekKeepingSelection(selection.StartFrame);
    }

    private void JumpPlaybackToSelectionPreroll(WaveSelection selection)
    {
        if (_document is null || selection.IsEmpty)
        {
            return;
        }

        var preroll = _document.SampleRate * 3L;
        var start = selection.Length < preroll ? selection.StartFrame : selection.EndFrame - preroll;
        _lastPlaybackStart = start;
        Waveform.SeekKeepingSelection(start);
    }

    private void OnPlayTick()
    {
        if (_document is null)
        {
            return;
        }

        if (_player.IsScrubbing)
        {
            return;
        }

        if (Waveform.IsScrubbing && !Overview.IsDragging)
        {
            Waveform.AbandonScrub();
        }

        if (AnyEffectPreviewing)
        {
            SyncPreviewPlayhead();
            if (_player.ProviderEnded && Environment.TickCount64 - ActivePreviewStartedAt >= 250)
            {
                OnPlaybackEnded(_playbackGeneration);
            }

            return;
        }

        if (_player.ProviderEnded)
        {
            if (IsLibraryVideoPlayheadClock() && !_player.IsPlaying)
            {
                if (!_meterRendering)
                {
                    SyncPlaybackVisuals();
                }

                return;
            }

            OnPlaybackEnded(_playbackGeneration);
            return;
        }

        // 通常はフレーム駆動（OnMeterRendering）が描画を同期する。ここは代行のみ。
        if (!_meterRendering)
        {
            SyncPlaybackVisuals();
        }
    }

    private void SyncPlaybackVisuals()
    {
        AdoptLibraryGaplessAdvance();
        if (_document is null)
        {
            return;
        }

        if (IsLibraryMaximized && LibraryPlaylistDocuments.IsPdf(_document))
        {
            var visualFrame = LibraryPlayerMode.FrameFromSeconds(
                LibraryBrowser.PlaylistVisualPosition.TotalSeconds,
                _document.SampleRate,
                _document.FrameCount);
            _document.CursorFrame = visualFrame;
            if (!Waveform.IsInteracting)
            {
                Waveform.PlayheadFrame = visualFrame;
                Waveform.FollowPlayhead();
                SyncTransportPosition(visualFrame);
            }

            return;
        }

        var videoPlaying = IsLibraryVideoPlayheadClock();
        var videoShuttle = LibraryPlayerMode.DrivesPlayheadWhileVideoShuttles(
            IsLibraryMaximized,
            LibraryPlaylistDocuments.IsVideo(_document),
            LibraryBrowser.PlaylistVisualShown && LibraryBrowser.PlaylistVisualIsVideo,
            _playbackShuttleDirection != 0,
            _player.IsPlaying);
        long frame;
        long exitFrame = -1;
        if (videoShuttle)
        {
            // 早送り中は映像クロックを止めるので、音声ヘッドで映像 Position を追従させる。
            EnsurePlaylistVideoTimelineLength();
            _player.ReadPlayheadVisuals(out frame, out exitFrame);
            if (!Waveform.IsInteracting)
            {
                ApplyPlaylistVideoLoopAndClock(ref frame);
            }
        }
        else if (videoPlaying)
        {
            EnsurePlaylistVideoTimelineLength();
            frame = LibraryPlayerMode.FrameFromSeconds(
                LibraryBrowser.PlaylistVisualPosition.TotalSeconds,
                _document.SampleRate,
                _document.FrameCount);
            if (!Waveform.IsInteracting)
            {
                ApplyPlaylistVideoLoopAndClock(ref frame);
            }
        }
        else
        {
            _player.ReadPlayheadVisuals(out frame, out exitFrame);
        }

        _document.CursorFrame = frame;
        if (!Waveform.IsInteracting)
        {
            Waveform.PlayheadFrame = frame;
            Waveform.ExitPlayheadFrame = exitFrame;
            Waveform.FollowPlayhead();
            if (OverviewHost.Visibility == Visibility.Visible)
            {
                SyncOverviewPlayhead();
            }

            SyncTransportPosition(frame);
        }
    }

    private bool IsLibraryVideoPlayheadClock() =>
        _document is not null
        && LibraryPlayerMode.DrivesPlayheadWhileVideoPlays(
            IsLibraryMaximized,
            LibraryPlaylistDocuments.IsVideo(_document),
            LibraryBrowser.PlaylistVisualClockRunning && LibraryBrowser.PlaylistVisualIsVideo);

    private void EnsureLibraryVisualPlaybackClock()
    {
        // 動画は映像クロック、wave / mp3 は _playTimer＋音声ヘッド。音声が出ている本再生だけ _playTimer を回す。
        if (_player.IsPlaying && !_playTimer.IsEnabled)
        {
            _playTimer.Start();
        }

        EnsureLibraryVisualPlayheadTicker();
        Waveform.SetTrailRecording(true);
        Transport.SetPlaying(true);
    }

    /// <summary>ホバー／プレビュー用。本再生の _playTimer とは別にし、Space の再生判定を汚さない。</summary>
    private void OnVisualPlayheadTick()
    {
        if (IsLibraryVideoPlayheadClock() && !_player.IsScrubbing && !Waveform.IsScrubbing)
        {
            SyncPlaybackVisuals();
        }
    }

    /// <summary>音声が出ていなくても、映像クロックでシークバーを動かす（本再生判定には使わない）。</summary>
    private void EnsureLibraryVisualPlayheadTicker()
    {
        StartMeterRendering();
        if (!_visualPlayheadTimer.IsEnabled)
        {
            _visualPlayheadTimer.Start();
        }
    }

    /// <summary>
    /// 動画の本再生：シークバーは映像の再生位置。音声出力の有無には依存しない。
    /// 選択範囲があればその範囲でループする。
    /// </summary>
    private void ApplyPlaylistVideoLoopAndClock(ref long frame)
    {
        if (_document is null)
        {
            return;
        }

        var loop = ResolvePlayLoopRange() ?? WaveSelection.Empty;
        var rate = Math.Max(1, _document.SampleRate);
        if (!loop.IsEmpty && LibraryPlayerMode.NeedsLoopSeek(frame, loop.StartFrame, loop.EndFrame))
        {
            frame = LibraryPlayerMode.WrapLoopFrame(frame, loop.StartFrame, loop.EndFrame);
            LibraryBrowser.SeekPlaylistVisual(TimeSpan.FromSeconds(frame / (double)rate));
        }

        if (_playbackShuttleDirection != 0 && _player.IsPlaying)
        {
            _player.ReadPlayheadVisuals(out var audioFrame, out _);
            LibraryBrowser.SeekPlaylistVisual(TimeSpan.FromSeconds(audioFrame / (double)rate));
            frame = audioFrame;
            NotePlaylistVideoSimpleSeekKeyboard();
        }
    }

    private void EnsurePlaylistVideoTimelineLength()
    {
        if (_document is null)
        {
            return;
        }

        var duration = LibraryBrowser.PlaylistVisualDuration;
        if (duration <= TimeSpan.Zero || _document.SampleRate < 1)
        {
            return;
        }

        var frames = Math.Max(1, (long)Math.Round(duration.TotalSeconds * _document.SampleRate));
        if (frames == _document.FrameCount)
        {
            return;
        }

        _document.SyncStreamPlaybackMeta(
            _document.SampleRate,
            _document.Channels,
            _document.BitsPerSample,
            frames);
        if (_document.Tags.DurationSeconds <= 0
            || Math.Abs(_document.Tags.DurationSeconds - duration.TotalSeconds) > 0.05)
        {
            _document.ApplyTags(_document.Tags.WithDurationSeconds(duration.TotalSeconds));
        }
        Waveform.ResetTimeZoom();
        ScheduleLibraryWaveformPaint();
    }

    private void StartMeterRendering()
    {
        // 再生中だけ release を打ち切る。停止中に playhead 用で再入場すると
        // BeginMeterRelease が止まり、レベルメーターが最後のバーで固まる。
        if (_player.IsPlaying)
        {
            _meterReleaseTimer?.Stop();
        }

        if (IsLibraryMaximized)
        {
            SyncPlayerMeterFade();
        }

        if (_meterRendering)
        {
            return;
        }

        _meterRendering = true;
        CompositionTarget.Rendering += OnMeterRendering;
    }

    private void StopMeterRendering()
    {
        if (!_meterRendering)
        {
            return;
        }

        _meterRendering = false;
        CompositionTarget.Rendering -= OnMeterRendering;
    }

    private void OnMeterRendering(object? sender, EventArgs e)
    {
        var videoShuttle = _playbackShuttleDirection != 0 && _player.IsPlaying;
        if (!_player.IsPlaying && !IsLibraryVideoPlayheadClock() && !videoShuttle)
        {
            return;
        }

        // 同一フレームで複数回発火することがあるため RenderingTime で間引く。
        if (e is RenderingEventArgs rendering)
        {
            if (rendering.RenderingTime == _lastRenderingTime)
            {
                return;
            }

            _lastRenderingTime = rendering.RenderingTime;
        }

        var nowStamp = Stopwatch.GetTimestamp();
        // 早送り中は映像を音声へ細かく合わせる（通常の間引きより短く）。
        var minIntervalMs = videoShuttle
            ? LibraryPlayerMode.VideoShuttleSeekSeconds * 1000d
            : LibraryPlayerMode.PlaybackVisualMinIntervalMs;
        var minTicks = minIntervalMs / 1000d * Stopwatch.Frequency;
        if (nowStamp - _lastPlaybackVisualStamp < minTicks)
        {
            return;
        }

        _lastPlaybackVisualStamp = nowStamp;

        if (AnyEffectPreviewing)
        {
            SyncPreviewPlayhead();
            QueueAnalyzerTick();
            return;
        }

        // 再生ヘッドだけ vsync 側。スペアナ／ゴニオは Render より低い優先度へ回し、
        // マウスとキー（Input）が描画の後ろに並ばないようにする。
        if (_playTimer.IsEnabled && !_player.IsScrubbing)
        {
            SyncPlaybackVisuals();
        }
        else if (IsLibraryVideoPlayheadClock() && !_player.IsScrubbing)
        {
            SyncPlaybackVisuals();
        }
        else if (videoShuttle && !_player.IsScrubbing)
        {
            SyncPlaybackVisuals();
        }

        QueueAnalyzerTick();
    }

    private void QueueAnalyzerTick()
    {
        if (_analyzerTickQueued)
        {
            return;
        }

        _analyzerTickQueued = true;
        _ = Dispatcher.BeginInvoke(LibraryPlayerMode.AnalyzerTickPriority, FlushAnalyzerTick);
    }

    private void FlushAnalyzerTick()
    {
        _analyzerTickQueued = false;
        if (!_player.IsPlaying)
        {
            // スペアナ／ゴニオは独自タイマーで無音へ落ちる。レベルメーターも同様に減衰させる。
            // 動画本再生→暗いプレビューで StartMeterRendering が再開したあともここで落とす。
            if (_meter.DecayAfterStop(_meterClock.Elapsed.TotalSeconds))
            {
                LevelMeter.Apply(_meter.Snapshot);
            }
            else if (_meterReleaseTimer is { IsEnabled: true })
            {
                ExtinguishMeter();
            }

            return;
        }

        Span<float> peaks = stackalloc float[ChannelLayout.MaxChannels];
        Span<float> rms = stackalloc float[ChannelLayout.MaxChannels];
        var hasSamples = _player.TakeMeterInterval(peaks, rms, out var meterChannels);
        LevelMeter.Apply(_meter.Update(
            peaks[..Math.Max(1, meterChannels)],
            rms[..Math.Max(1, meterChannels)],
            _meterClock.Elapsed.TotalSeconds,
            hasSamples));
        Spectrum.Tick();
        LoudnessMeter.Tick();
        VectorScope.Tick();
    }

    private void SyncPreviewPlayhead()
    {
        if (_document is null)
        {
            return;
        }

        var frame = _player.SmoothCursorFrame;
        if (_pitchPreview.Previewing)
        {
            frame += _pitchPreview.Origin;
        }

        if (_timeStretchPreview.Previewing)
        {
            frame += _timeStretchPreview.Origin;
        }

        _document.CursorFrame = frame;
        Waveform.SetPlayheadFromPlayback(frame);
        SyncOverviewPlayhead();
        SyncTransportPosition(frame);
    }

    private void ExtinguishMeter()
    {
        _meterReleaseTimer?.Stop();
        _meter.Extinguish();
        LevelMeter.Apply(_meter.Snapshot);
    }

    private void BeginMeterRelease()
    {
        if (!_meter.DecayAfterStop(_meterClock.Elapsed.TotalSeconds))
        {
            ExtinguishMeter();
            return;
        }

        LevelMeter.Apply(_meter.Snapshot);
        _meterReleaseTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _meterReleaseTimer.Tick -= OnMeterReleaseTick;
        _meterReleaseTimer.Tick += OnMeterReleaseTick;
        _meterReleaseTimer.Start();
    }

    private void OnMeterReleaseTick(object? sender, EventArgs e)
    {
        if (_player.IsPlaying)
        {
            _meterReleaseTimer?.Stop();
            return;
        }

        if (_meter.DecayAfterStop(_meterClock.Elapsed.TotalSeconds))
        {
            LevelMeter.Apply(_meter.Snapshot);
            return;
        }

        ExtinguishMeter();
    }

    private void ApplyPlayerRoute()
    {
        var speaker = AppStorage.Settings.ResolvedSpeaker();
        _player.SetOutputMap(speaker.PlaybackOutputMap);
        _player.SetFileChannelMap(speaker.FileChannelMap, speaker.Channels);
        ApplyChannelSolo();
    }

    private void CycleChannelSolo(int direction = 1)
    {
        if (_document is null || _activeSession is null)
        {
            return;
        }

        _activeSession.SoloMask = ChannelSolo.StepMask(
            _activeSession.SoloMask,
            _document.Channels,
            direction < 0 ? -1 : 1);
        ApplyChannelSolo();
    }

    private void ToggleChannelSolo(int channel, bool add, bool mute = false)
    {
        if (_document is null || _activeSession is null)
        {
            return;
        }

        _activeSession.SoloMask = mute
            ? ChannelSolo.Mute(_activeSession.SoloMask, channel, _document.Channels)
            : ChannelSolo.Toggle(_activeSession.SoloMask, channel, _document.Channels, add);
        ApplyChannelSolo();
    }

    private int EditMask() =>
        ChannelSolo.ClampMask(_activeSession?.SoloMask ?? 0, _document?.Channels ?? 1);

    private void ApplyChannelSolo()
    {
        var mask = ChannelSolo.ClampMask(_activeSession?.SoloMask ?? 0, _document?.Channels ?? 1);
        if (_activeSession is not null)
        {
            _activeSession.SoloMask = mask;
        }

        Waveform.SetSoloMask(mask);
        _player.SetSoloMask(mask);
    }

    private void SyncMonitorLayout()
    {
        var channels = _document?.Channels ?? 2;
        var speaker = AppStorage.Settings.ResolvedPlaybackLayout();
        var fileMap = AppStorage.Settings.ResolvedFileChannelMap();
        ForEachWaveform(view => view.ApplySpeakerLayout(speaker, fileMap));
        VectorScope.ApplyLayout(channels, speaker, fileMap);
        _meter.EnsureLayout(channels);
        LevelMeter.Apply(_meter.Snapshot);
        ApplyMeterColumnWidth(_meterColumnPreferred);
        VectorScope.InvalidateVisual();
    }

    private void OnScrubStarted(long frame)
    {
        if (_document is null)
        {
            return;
        }

        StopPlaybackShuttle();
        StopSeekNudge();
        StopVideoFrameStep();
        _resumeAfterScrub = _player.IsPlaying && !_player.IsScrubbing;
        if (_fadePreview.Previewing)
        {
            _fadePreview.Previewing = false;
            Waveform.SetPreviewGain(null);
        }

        if (_volumePreview.Previewing)
        {
            _volumePreview.Previewing = false;
            Waveform.SetPreviewGain(null);
        }

        if (_pitchPreview.Previewing)
        {
            _pitchPreview.Previewing = false;
        }

        if (_timeStretchPreview.Previewing)
        {
            _timeStretchPreview.Previewing = false;
        }

        if (_formatPreview.Previewing)
        {
            StopFormatPreview();
        }

        _playTimer.Stop();
        Waveform.SetTrailRecording(false);
        try
        {
            _player.BeginScrub(_document, frame);
            _playbackGeneration = _player.Generation;
            StartMeterRendering();
            Transport.SetPlaying(true);
            SyncTransportPosition(frame);
        }
        catch (Exception ex)
        {
            _resumeAfterScrub = false;
            PausePlaybackSoft();
            if (IsLibraryMaximized && LibraryPlaylistDocuments.IsVideo(_document))
            {
                return;
            }

            OwnerCenteredMessageBox.Show(this, ex.Message, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnScrubPreviewed(long frame)
    {
        if (_document is null || !_player.IsScrubbing)
        {
            return;
        }

        _document.CursorFrame = frame;
        _player.CaptureScrub(_document, frame);
        SyncOverviewPlayhead();
        SyncTransportPosition(frame);
        RefreshStatus();
    }

    private void OnScrubEnded(long frame, bool commit)
    {
        if (_document is null)
        {
            return;
        }

        var resume = commit && _resumeAfterScrub;
        _resumeAfterScrub = false;
        if (_player.IsScrubbing)
        {
            _player.EndScrub();
        }

        if (resume)
        {
            StartPlayback(frame, prerollSeconds: 0);
            return;
        }

        PausePlaybackSoft();
        SeekFrame(frame);
    }

    private void OnPlaybackEnded(int generation)
    {
        if (generation != _playbackGeneration)
        {
            return;
        }

        if (_player.IsScrubbing)
        {
            return;
        }

        if (Waveform.IsScrubbing)
        {
            Waveform.AbandonScrub();
        }

        if (TryHandleEffectPreviewEnded(_fadePreview, () =>
            {
                StopPreviewToResume(_fadePreview);
                RestoreFadeVisualIfMenuOpen();
            })
            || TryHandleEffectPreviewEnded(_volumePreview, () =>
            {
                StopPreviewToResume(_volumePreview);
                RestoreVolumeVisualIfMenuOpen();
            })
            || TryHandleEffectPreviewEnded(_pitchPreview, () => StopPreviewToResume(_pitchPreview))
            || TryHandleEffectPreviewEnded(_timeStretchPreview, () => StopPreviewToResume(_timeStretchPreview))
            || TryHandleEffectPreviewEnded(_formatPreview, StopFormatPreview))
        {
            return;
        }

        if (IsLibraryMaximized)
        {
            var haltVisual = LibraryPlayerMode.HaltsAtEndWithoutPlaylistAdvance(
                _playlistVideoChromeVisible,
                LibraryBrowser.PlaylistVisualShown,
                LibraryBrowser.PlaylistVisualIsVideo,
                LibraryBrowser.PlaylistVisualIsPdf);
            // 一曲再生、または映像本再生／ミニ／PDF 本表示（プレイリスト非表示）では次へ進まない。
            if (_libraryStopAfterTrack || haltVisual)
            {
                _libraryStopAfterTrack = false;
                HaltPlaybackToStart(keepVideoChromeHidden: haltVisual);
                return;
            }

            // 再生ウィンドウのループが効かなかったときの保険。Auto Next より優先。
            if (TryRestartLibraryAutoLoop())
            {
                return;
            }

            if (TryAdvanceLibraryPlaylist())
            {
                return;
            }
        }

        HaltPlaybackToStart();
    }

    /// <summary>Auto Loop オンかつ範囲なしのとき、先頭から再生し直す。</summary>
    private bool TryRestartLibraryAutoLoop()
    {
        if (!LibraryBrowser.AutoLoopEnabled || _document is null || _document.FrameCount <= 0)
        {
            return false;
        }

        // A-B／選択があるときは ResolvePlayLoopRange 側のループに任せる。
        if (Waveform.TryGetPlayerAbLoop(out _) || !_document.Selection.IsEmpty)
        {
            return false;
        }

        StartPlayback(0, prerollSeconds: 0);
        return true;
    }

    private bool AnyEffectPreviewing =>
        _fadePreview.Previewing
        || _formatPreview.Previewing
        || _volumePreview.Previewing
        || _pitchPreview.Previewing
        || _timeStretchPreview.Previewing;

    private long ActivePreviewStartedAt =>
        _fadePreview.Previewing
            ? _fadePreview.StartedAt
            : _formatPreview.Previewing
                ? _formatPreview.StartedAt
                : _pitchPreview.Previewing
                    ? _pitchPreview.StartedAt
                    : _timeStretchPreview.Previewing
                        ? _timeStretchPreview.StartedAt
                        : _volumePreview.StartedAt;

    private bool TryHandleEffectPreviewEnded(EffectPreviewState preview, Action finish)
    {
        if (!preview.Previewing)
        {
            return false;
        }

        if (preview.IsTooSoon)
        {
            return true;
        }

        finish();
        return true;
    }

    private void StopPreviewToResume(EffectPreviewState preview)
    {
        preview.Previewing = false;
        PausePlaybackSoft();
        if (_document is not null)
        {
            SeekFrame(preview.ResumeFrame);
        }
    }

    private void JumpToLoopPrerollAndPlay()
    {
        if (_document is null)
        {
            return;
        }

        var range = _document.Selection;
        if (range.IsEmpty)
        {
            if (!_document.SampleLoop.IsEmpty)
            {
                range = _document.SampleLoop;
            }
            else if (!_document.TryGetRoleSpan(MarkerRole.Loop, Waveform.PlayheadFrame, out range))
            {
                return;
            }

            Waveform.SetSelection(range);
        }

        var preroll = _document.SampleRate * 3L;
        var start = range.Length < preroll ? range.StartFrame : range.EndFrame - preroll;
        Waveform.SeekKeepingSelection(start);
        StartPlayback(start, prerollSeconds: 0);
    }

    private void SetSampleLoopFromSelection()
    {
        if (_document is null)
        {
            return;
        }

        if (!_document.AllowsRegionsAndLoops)
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.ErrorMp3NoRegionLoop,
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var range = _document.Selection;
        if (range.IsEmpty)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var command = ProcessEdits.SetSampleLoop(_document, range);
        if (command is null)
        {
            return;
        }

        _history.Do(_document, command);
        AfterMarkerEdit();
    }

    private void ClearSampleLoop()
    {
        if (TryRunBatchEdit(
                UiStrings.OverlayApplySelected,
                (session, _) =>
                {
                    var document = session.Document;
                    if (!document.AllowsRegionsAndLoops || document.SampleLoop.IsEmpty)
                    {
                        return null;
                    }

                    return ProcessEdits.SetSampleLoop(document, WaveSelection.Empty);
                }))
        {
            return;
        }

        if (_document is null || _document.SampleLoop.IsEmpty)
        {
            return;
        }

        var command = ProcessEdits.SetSampleLoop(_document, WaveSelection.Empty);
        if (command is null)
        {
            return;
        }

        _history.Do(_document, command);
        AfterMarkerEdit();
    }

    private bool TrySetRegionFromSelection(bool quiet = false)
    {
        if (_document is null)
        {
            return false;
        }

        if (!_document.AllowsRegionsAndLoops)
        {
            if (!quiet)
            {
                OwnerCenteredMessageBox.Show(
                    this,
                    UiStrings.ErrorMp3NoRegionLoop,
                    UiStrings.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            return false;
        }

        var range = _document.Selection;
        if (range.IsEmpty)
        {
            if (!quiet)
            {
                OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            }

            return false;
        }

        var previous = DocumentRangeDivide.ResolvePreviousParts(_regionDivide, _document, range, regions: true);
        var next = RangeDivide.NextParts(previous);
        var command = ProcessEdits.DivideRegions(_document, range, previous, next);
        if (command is not null)
        {
            ApplyPlaceLive(command);
        }

        _regionDivide = new RangeDivideState(_document, range.StartFrame, range.EndFrame, next);
        AfterMarkerEdit();
        SyncRangeClicks();
        return true;
    }

    private void ClearRegion(WaveSelection range)
    {
        if (_document is null || range.IsEmpty)
        {
            return;
        }

        var command = ProcessEdits.SetRegion(_document, range);
        if (command is null)
        {
            return;
        }

        _history.Do(_document, command);
        AfterMarkerEdit();
    }

    private void CenterPlayheadOrToggleLock()
    {
        if (IsPlaybackActive())
        {
            DetachOverviewScrubKeepPlayback();
            if (Waveform.CenterLocked)
            {
                Waveform.UnlockCenter();
            }
            else
            {
                Waveform.LockCenterToPlayhead();
            }

            return;
        }

        Waveform.CenterViewOnPlayhead();
    }

    private void ToggleAnalyzerMaximize()
    {
        SetWaveformMaximizeMode(
            _waveformMaximizeMode == WaveformMaximizeMode.Analyzers
                ? WaveformMaximizeMode.Off
                : WaveformMaximizeMode.Analyzers);
    }

    private void ToggleWaveformMaximize()
    {
        SetWaveformMaximizeMode(
            _waveformMaximizeMode == WaveformMaximizeMode.Waveform
                ? WaveformMaximizeMode.Off
                : WaveformMaximizeMode.Waveform);
    }

    internal bool IsWaveformMaximized =>
        IsFullscreenMaximizeMode(_waveformMaximizeMode);

    private static bool IsFullscreenMaximizeMode(WaveformMaximizeMode mode) =>
        mode is WaveformMaximizeMode.Waveform or WaveformMaximizeMode.Analyzers;

    internal void RefreshWaveformFullscreenFrame()
    {
        if (IsWaveformMaximized)
        {
            ApplyWaveformFullscreenFrame();
        }
    }

    private void SetWaveformMaximizeMode(
        WaveformMaximizeMode mode,
        bool? playFirstOnLibrary = null,
        bool retainMinimalChrome = false)
    {
        if (_waveformMaximizeMode == mode)
        {
            return;
        }

        if (_waveformMaximizeMode == WaveformMaximizeMode.Library
            && mode != WaveformMaximizeMode.Library)
        {
            // メニュー表示中、および外側クリック dismiss が下のコントロールへ貫通したときの解除は捨てる。
            if (_listContextMenuOpen
                || LibraryBrowser.HasOpenContextMenu
                || LibraryBrowser.IsContextMenuDismissGuardActive)
            {
                return;
            }

            SetPlaylistVideoFullscreen(false);
            // 動画ミニ枠のまま F11／F12 寸法を覚えさせない。
            LeaveVideoMiniBeforeWaveformFullscreen();
            // セッション切替で捨てキューが消える前にスナップショットする。
            // 同じ音声ドキュメントが残るときだけエディタへマージ。動画／PDF は持ち込まないので捨てる。
            var scratchDocument = _document;
            var scratchMarkers = Waveform.ScratchCues.HasMarkers
                ? Waveform.ScratchCues.SnapshotMarkers()
                : [];
            var scratchAbIn = Waveform.ScratchCues.AbIn;
            var scratchAbOut = Waveform.ScratchCues.AbOut;
            var scratchAb = Waveform.ScratchCues.LoopRange();
            Waveform.ClearPlayerAbRepeat();
            // 選択の音声ファイルだけエディタのタブに残す。プレイリスト自体は戻りで消さない。
            var parked = _sessions.ToArray();
            if (!KeepOnlyLibrarySelectedSessions())
            {
                RestorePlayerScratchIfCleared(scratchMarkers, scratchAbIn, scratchAbOut);
                return;
            }

            _playlistParked = parked;

            if (ReferenceEquals(_document, scratchDocument))
            {
                CommitPlayerScratchMarkersToEditor(scratchMarkers);
                CommitPlayerScratchAbToEditor(scratchAb);
            }

            Waveform.ClearPlayerScratchAll();

            if (IsPlaybackActive())
            {
                StopPlayback();
            }

            // エディタへ戻す前にストリームを閉じる。Pause だけでは元ファイルが掴みっぱなしになる。
            CancelLibraryGapless();
            _player.ReleaseStreamSource();
        }

        if (mode == WaveformMaximizeMode.Library)
        {
            if (_waveformMaximizeMode != WaveformMaximizeMode.Library
                && !ConfirmDirtySessionsBeforePlayer())
            {
                return;
            }

            _libraryPlayFirstPending = playFirstOnLibrary ?? true;
            SyncRangeClicks();
        }

        var wasLibrary = _waveformMaximizeMode == WaveformMaximizeMode.Library;
        var wantLibrary = mode == WaveformMaximizeMode.Library;
        var wasFullscreen = IsFullscreenMaximizeMode(_waveformMaximizeMode);
        var wantFullscreen = IsFullscreenMaximizeMode(mode);
        PersistCurrentWindowPlacement();

        if (wantFullscreen && !wasFullscreen)
        {
            RememberWindowFrameBeforeFullscreen(fromLibrary: wasLibrary);
            _waveformMaximizeMode = mode;
            ApplyWaveformMaximizeChrome();
            ApplyWaveformFullscreenFrame();
        }
        else if (!wantFullscreen && wasFullscreen)
        {
            _waveformMaximizeMode = mode;
            if (wantLibrary && wasFullscreen && !retainMinimalChrome)
            {
                _libraryMinimalChrome = _minimalBeforeWaveformMax;
            }

            RestoreWaveformWindowChrome();
            if (wantLibrary)
            {
                if (!TryApplyCurrentModePlacement())
                {
                    ApplyRememberedWindowFrameBounds();
                }
            }
            else
            {
                ApplyRememberedWindowFrameBounds();
            }

            ApplyWaveformMaximizeChrome();
        }
        else
        {
            _waveformMaximizeMode = mode;
            ApplyWaveformMaximizeChrome();
            if (wantFullscreen)
            {
                ApplyWaveformFullscreenFrame();
            }
            else if (wantLibrary || wasLibrary)
            {
                TryApplyCurrentModePlacement();
            }
        }

        if (wantLibrary)
        {
            // 波形の再描画予約は ApplyLibraryChrome 側。ここでは再生開始だけ。
            ScheduleLibraryEnterPlayback();
        }
        else
        {
            // プレイヤー離脱の Bind は、まだライブラリ中なのでメーターの文書を外す。
            // エディタの停止中表示は、モードが戻ってから付け直す。
            LoudnessMeter.Document = _document;
        }

        SyncPlaylistSelectionLoudness();

        if (!wasFullscreen || wasLibrary || wantLibrary)
        {
            Dispatcher.BeginInvoke(AppStorage.Save, DispatcherPriority.Background);
        }

        if (mode == WaveformMaximizeMode.Library)
        {
            ResumePlayerVariableRate();
            FocusLibraryPaneForPlaylist();
            return;
        }

        Waveform.Focus();
    }

    private void ApplyWaveformFullscreenFrame()
    {
        // 最大化のまま枠だけ消すと作業領域で止まり、タスクバーが残る。
        if (WindowState != WindowState.Normal)
        {
            WindowState = WindowState.Normal;
        }

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        if (TryGetSavedFullscreenBounds(out var saved))
        {
            Left = saved.X;
            Top = saved.Y;
            Width = saved.Width;
            Height = saved.Height;
        }

        if (WindowPlacement.TryCoverContainingMonitor(this) || saved.Width > 1)
        {
            PersistCurrentWindowPlacement();
        }

        SyncShellFullscreen();
    }

    private void SyncShellFullscreen()
    {
        var fullscreen = IsWaveformMaximized || _playlistVideoFullscreen;
        WindowPlacement.SetShellFullscreen(this, fullscreen);
        ApplyWindowTopmost();
        if (fullscreen)
        {
            WindowPlacement.TryCoverContainingMonitor(this);
        }
    }

    /// <summary>フルスクリーン中はタスクバーより前に出す。抜けたら「常に最前面」の設定に戻す。</summary>
    private void ApplyWindowTopmost()
    {
        var want = AlwaysOnTopCheck.IsChecked == true
            || IsWaveformMaximized
            || _playlistVideoFullscreen;
        if (Topmost != want)
        {
            Topmost = want;
        }
    }

    private bool TryGetSavedFullscreenBounds(out Rect saved)
    {
        saved = default;
        var kind = CurrentPlacementKind();
        if (kind is not (MainWindowPlacementKind.WaveformFullscreen or MainWindowPlacementKind.AnalyzerFullscreen))
        {
            return false;
        }

        return WindowPlacement.TryGetDipBounds(
            AppStorage.Settings,
            1,
            1,
            kind,
            out saved,
            out _);
    }

    private void PersistCurrentWindowPlacement()
    {
        if (_playlistVideoFullscreen)
        {
            return;
        }

        if (IsFullscreenMaximizeMode(_waveformMaximizeMode))
        {
            // 最大化解除の途中寸法を F11／F12 として残すと、次回は作業領域のままタスクバーが出る。
            if (WindowPlacement.TryGetContainingMonitorDip(this, out var monitor)
                && (Width < monitor.Width - 4 || Height < monitor.Height - 4))
            {
                return;
            }

            WindowPlacement.Capture(this, AppStorage.Settings, CurrentPlacementKind());
            return;
        }

        if (WindowStyle == WindowStyle.None)
        {
            return;
        }

        // 関連付け／引数の動画ミニプレイヤーは別枠（位置のみ）。
        // 突入直後はまだ Library 前なので、ここで通常／F10／F9 を書くと既定サイズで上書きしてしまう。
        if (WindowPlacement.UsesVideoLaunchPlacementSlot(_videoLaunchPlacement))
        {
            if (IsLibraryMaximized)
            {
                WindowPlacement.CaptureVideoLaunchPosition(this, AppStorage.Settings);
            }

            return;
        }

        WindowPlacement.Capture(this, AppStorage.Settings, CurrentPlacementKind());
    }

    private bool TryApplyCurrentModePlacement()
    {
        if (_videoLaunchPlacement && IsLibraryMaximized)
        {
            return TryApplyVideoLaunchPlacement();
        }

        return WindowPlacement.TryApply(this, AppStorage.Settings, CurrentPlacementKind());
    }

    private bool TryApplyVideoLaunchPlacement()
    {
        // F 全画面中はモニター一面のままにし、抜けたときにまとめて反映する。
        if (LibraryPlayerMode.DefersVideoLaunchPlacementWhileFullscreen(_playlistVideoFullscreen))
        {
            _videoLaunchPlacementDeferred = true;
            return false;
        }

        if (!LibraryBrowser.TryGetPlaylistVideoNaturalPixels(out var width, out var height))
        {
            return false;
        }

        // 位置の更新は動画ミニ中の LocationChanged / Persist に任せる。
        // ここで Capture すると、F10 プレイリスト寸法のまま F8 へ入った瞬間に
        // プレイリスト側の位置で動画ミニ枠を上書きしてしまう。
        _videoLaunchPlacementDeferred = false;
        var scale = LibraryVideoWindowScale.Clamp(_videoLaunchScale);
        // 幅・高さとも同じ倍率。Fit も縦横比を保つのでアスペクトは変わらない。
        var scaledWidth = Math.Max(1, (int)Math.Round(width * scale));
        var scaledHeight = Math.Max(1, (int)Math.Round(height * scale));
        _videoLaunchScaleApplying = true;
        try
        {
            WindowPlacement.ApplyVideoLaunch(this, AppStorage.Settings, scaledWidth, scaledHeight);
            // 作業領域で縮められたときだけ実効倍率へ合わせる（丸め差で 25% 格子を壊さない）。
            SyncVideoLaunchScaleFromWindow(width, height, preferScale: scale);
        }
        finally
        {
            _videoLaunchScaleApplying = false;
        }

        return true;
    }

    /// <summary>
    /// 動画ミニ窓のキー拡縮（+／-・テンキー）。常に 25% 刻みへ着地。原寸で一旦止まる。
    /// 枠ドラッグ後も、今の大きさに近い 25% 倍率へ。倍率はモードを抜けるまで覚える。
    /// </summary>
    private bool TryScaleVideoLaunchWindow(int direction, bool isRepeat)
    {
        if (!CanScaleVideoLaunchWindow() || direction == 0)
        {
            return false;
        }

        // 枠ドラッグ後の実寸を拾ってから 25% 格子へ進む。
        if (LibraryBrowser.TryGetPlaylistVideoNaturalPixels(out var naturalW, out var naturalH))
        {
            SyncVideoLaunchScaleFromWindow(naturalW, naturalH);
        }

        var next = LibraryVideoWindowScale.StepKey(
            _videoLaunchScale,
            direction,
            isRepeat,
            ref _videoLaunchScaleLatched);
        return ApplyVideoLaunchScale(next, showKeyToast: true);
    }

    private bool CanScaleVideoLaunchWindow() =>
        IsVideoMiniPlayerActive()
        && !_playlistVideoFullscreen
        && LibraryBrowser.TryGetPlaylistVideoNaturalPixels(out _, out _);

    private bool ApplyVideoLaunchScale(double next, bool showKeyToast)
    {
        next = LibraryVideoWindowScale.Clamp(next);
        if (Math.Abs(next - _videoLaunchScale) >= 1e-9)
        {
            // 拡縮しても中心を保つ。
            WindowPlacement.CaptureVideoLaunchPosition(this, AppStorage.Settings);
            _videoLaunchScale = next;
            TryApplyVideoLaunchPlacement();
        }

        if (showKeyToast)
        {
            ShowVideoLaunchScaleToast();
        }

        return true;
    }

    /// <summary>枠ドラッグ後の実寸を倍率へ反映（+／- の 25% 着地の基準）。</summary>
    private void SyncVideoLaunchScaleAfterUserResize()
    {
        if (_videoLaunchScaleApplying
            || !IsVideoMiniPlayerActive()
            || _playlistVideoFullscreen
            || !LibraryBrowser.TryGetPlaylistVideoNaturalPixels(out var naturalW, out var naturalH))
        {
            return;
        }

        SyncVideoLaunchScaleFromWindow(naturalW, naturalH);
    }

    private VideoLaunchAspectResize.Constraints? TryGetVideoLaunchAspectConstraints()
    {
        if (!IsVideoMiniPlayerActive()
            || _playlistVideoFullscreen
            || !LibraryBrowser.TryGetPlaylistVideoNaturalPixels(out var naturalW, out var naturalH)
            || naturalW < 1
            || naturalH < 1)
        {
            return null;
        }

        var dip = WindowPlacement.DipScale(this);
        if (dip <= 0)
        {
            return null;
        }

        WindowPlacement.TryGetNonClientDipSize(this, out var nonClientDipW, out var nonClientDipH);
        var nonClientW = (int)Math.Round(nonClientDipW * dip);
        var nonClientH = (int)Math.Round(nonClientDipH * dip);
        var minClientW = Math.Max(1, (int)Math.Round(naturalW * LibraryVideoWindowScale.Min));
        var minClientH = Math.Max(1, (int)Math.Round(naturalH * LibraryVideoWindowScale.Min));
        var maxClientW = Math.Max(minClientW, (int)Math.Round(naturalW * LibraryVideoWindowScale.Max));
        var maxClientH = Math.Max(minClientH, (int)Math.Round(naturalH * LibraryVideoWindowScale.Max));
        // 作業領域より大きくしない。
        var work = SystemParameters.WorkArea;
        var maxOuterW = Math.Max(1, work.Width * dip);
        var maxOuterH = Math.Max(1, work.Height * dip);
        maxClientW = Math.Min(maxClientW, Math.Max(1, (int)Math.Round(maxOuterW) - nonClientW));
        maxClientH = Math.Min(maxClientH, Math.Max(1, (int)Math.Round(maxOuterH) - nonClientH));

        return new VideoLaunchAspectResize.Constraints(
            clientAspect: naturalW / (double)naturalH,
            nonClientWidthPx: nonClientW,
            nonClientHeightPx: nonClientH,
            minClientWidthPx: minClientW,
            minClientHeightPx: minClientH,
            maxClientWidthPx: Math.Max(minClientW, maxClientW),
            maxClientHeightPx: Math.Max(minClientH, maxClientH));
    }

    /// <summary>+／- キー拡縮時だけ中央に拡大率を 1 秒出してフェードアウト。</summary>
    private void ShowVideoLaunchScaleToast()
    {
        PlaylistVideoScaleToastText.Text =
            LibraryVideoWindowScale.FormatPercent(_videoLaunchScale)
                .ToString(CultureInfo.InvariantCulture) + "%";
        LibraryPlayerMode.FadeElementOpacity(
            PlaylistVideoScaleToast,
            to: 1,
            instant: true,
            hitTestVisible: false);
        _videoLaunchScaleToastTimer ??= new DispatcherTimer
        {
            Interval = LibraryExplorerTypeahead.ToastIdleClear,
        };
        _videoLaunchScaleToastTimer.Tick -= OnVideoLaunchScaleToastTick;
        _videoLaunchScaleToastTimer.Tick += OnVideoLaunchScaleToastTick;
        _videoLaunchScaleToastTimer.Stop();
        _videoLaunchScaleToastTimer.Start();
    }

    private void OnVideoLaunchScaleToastTick(object? sender, EventArgs e)
    {
        _videoLaunchScaleToastTimer?.Stop();
        LibraryPlayerMode.FadeElementOpacity(
            PlaylistVideoScaleToast,
            to: 0,
            instant: false,
            hitTestVisible: false,
            seconds: LibraryPlayerMode.ChromeFadeSeconds);
    }

    private void HideVideoLaunchScaleToast()
    {
        _videoLaunchScaleToastTimer?.Stop();
        LibraryPlayerMode.FadeElementOpacity(
            PlaylistVideoScaleToast,
            to: 0,
            instant: true,
            hitTestVisible: false);
    }

    private void SyncVideoLaunchScaleFromWindow(
        int naturalPixelsWidth,
        int naturalPixelsHeight,
        double? preferScale = null)
    {
        if (naturalPixelsWidth < 1
            || naturalPixelsHeight < 1
            || !WindowPlacement.TryGetClientDipSize(this, out var clientW, out var clientH))
        {
            return;
        }

        var dip = WindowPlacement.DipScale(this);
        if (dip <= 0)
        {
            return;
        }

        var effective = LibraryVideoWindowScale.FromClientSize(
            clientW,
            clientH,
            naturalPixelsWidth / dip,
            naturalPixelsHeight / dip);
        _videoLaunchScale = preferScale is double requested
            ? LibraryVideoWindowScale.RememberAfterApply(requested, effective)
            : effective;
    }

    private void ResetVideoLaunchScaleLatch() => _videoLaunchScaleLatched = false;

    /// <summary>動画ミニ中に動かした位置をすぐ覚える（次の本再生／再起動用）。</summary>
    private void RememberVideoLaunchPlacementIfNeeded()
    {
        if (!_videoLaunchPlacement
            || !IsLibraryMaximized
            || _playlistVideoFullscreen
            || WindowStyle == WindowStyle.None)
        {
            return;
        }

        WindowPlacement.CaptureVideoLaunchPosition(this, AppStorage.Settings);
        _videoLaunchPlacementSaveTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(400),
        };
        _videoLaunchPlacementSaveTimer.Tick -= OnVideoLaunchPlacementSaveTick;
        _videoLaunchPlacementSaveTimer.Tick += OnVideoLaunchPlacementSaveTick;
        _videoLaunchPlacementSaveTimer.Stop();
        _videoLaunchPlacementSaveTimer.Start();
    }

    private void OnVideoLaunchPlacementSaveTick(object? sender, EventArgs e)
    {
        _videoLaunchPlacementSaveTimer?.Stop();
        AppStorage.Save();
    }

    private MainWindowPlacementKind CurrentPlacementKind()
    {
        if (_waveformMaximizeMode == WaveformMaximizeMode.Waveform)
        {
            return MainWindowPlacementKind.WaveformFullscreen;
        }

        if (_waveformMaximizeMode == WaveformMaximizeMode.Analyzers)
        {
            return MainWindowPlacementKind.AnalyzerFullscreen;
        }

        if (IsLibraryMaximized && _libraryMinimalChrome)
        {
            return MainWindowPlacementKind.MinimalPlayer;
        }

        if (IsLibraryMaximized)
        {
            return MainWindowPlacementKind.Player;
        }

        return MainWindowPlacementKind.Editor;
    }

    private void RememberWindowFrameBeforeFullscreen(bool fromLibrary)
    {
        _windowStyleBeforeWaveformMax = WindowStyle;
        _resizeModeBeforeWaveformMax = ResizeMode;
        _minimalBeforeWaveformMax = fromLibrary && _libraryMinimalChrome;
        if (fromLibrary
            && WindowPlacement.TryGetDipBounds(
                AppStorage.Settings,
                MinWidth,
                MinHeight,
                MainWindowPlacementKind.Editor,
                out var editorBounds,
                out var editorMaximized))
        {
            _boundsBeforeWaveformMax = editorBounds;
            _windowStateBeforeWaveformMax = editorMaximized ? WindowState.Maximized : WindowState.Normal;
            return;
        }

        _windowStateBeforeWaveformMax = WindowState;
        _boundsBeforeWaveformMax = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;
    }

    private void RestoreWaveformWindowChrome()
    {
        WindowStyle = _windowStyleBeforeWaveformMax;
        ResizeMode = _resizeModeBeforeWaveformMax;
        DarkWindowChrome.ApplyImmersiveDarkTitleBar(this);
        SyncShellFullscreen();
    }

    private void ApplyRememberedWindowFrameBounds()
    {
        WindowState = WindowState.Normal;
        Left = _boundsBeforeWaveformMax.X;
        Top = _boundsBeforeWaveformMax.Y;
        Width = Math.Max(MinWidth, _boundsBeforeWaveformMax.Width);
        Height = Math.Max(MinHeight, _boundsBeforeWaveformMax.Height);
        if (_windowStateBeforeWaveformMax == WindowState.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    /// <summary>F12 アナライザ最大化の倍率。F10 通常／F 全画面は 1（ウィンドウ比例なし）。</summary>
    private double AnalyzerMaximizeLayoutScale =>
        _waveformMaximizeMode == WaveformMaximizeMode.Analyzers
            ? DesignMetrics.AnalyzerMaximizeScale
            : 1d;

    /// <summary>動画ミニ専用の窓比例倍率。F10 では常に 1。基準は作業領域（最大化相当＝1）。</summary>
    private double VideoMiniOverlayLayoutScale
    {
        get
        {
            var work = SystemParameters.WorkArea;
            return LibraryPlayerMode.VideoMiniOverlayScale(
                ActualWidth,
                ActualHeight,
                videoMini: IsVideoMiniPlayerActive(),
                fullscreen: _playlistVideoFullscreen,
                referenceWidth: work.Width > 1 ? work.Width : DesignMetrics.OverlayScaleReferenceWidth,
                referenceHeight: work.Height > 1 ? work.Height : DesignMetrics.OverlayScaleReferenceHeight);
        }
    }

    /// <summary>メーター類に当てる倍率（動画ミニは窓比例、それ以外は F12 倍率のみ）。</summary>
    private double AnalyzerLayoutScale =>
        IsVideoMiniPlayerActive() ? VideoMiniOverlayLayoutScale : AnalyzerMaximizeLayoutScale;

    private void ApplyAnalyzerMaximizeScale()
    {
        var meterScale = AnalyzerLayoutScale;
        var meterTransform = Math.Abs(meterScale - 1d) > 0.0001
            ? UiScaleService.CreatePublishedTransform(meterScale)
            : Transform.Identity;
        // ピークも列幅と同じ倍率で LayoutTransform（レイアウトが逆変換するので Stretch でも枠内に収まる）。
        LevelMeter.LayoutTransform = meterTransform;
        LoudnessMeter.LayoutTransform = meterTransform;
        VectorScope.LayoutTransform = meterTransform;
        Spectrum.LayoutTransform = meterTransform;
        // WAAPI トグルも同じ窓比例／F12 倍率。スロット高は変換後の見た目に合わせる。
        if (_waapiToggle is not null)
        {
            _waapiToggle.LayoutTransform = meterTransform;
        }

        if (IsLibraryMaximized && MeterTopSlot.Visibility == Visibility.Visible)
        {
            MeterTopSlot.Height = DesignMetrics.ProjectBarHeight * meterScale;
        }

        ApplyLevelMeterHeightCap(meterScale);

        // ファイル名／拡縮トーストは LayoutTransform。タイムコードは Viewbox 高さで比例させる。
        var overlayScale = VideoMiniOverlayLayoutScale;
        var overlayTransform = Math.Abs(overlayScale - 1d) > 0.0001
            ? UiScaleService.CreatePublishedTransform(overlayScale)
            : Transform.Identity;
        PlaylistVideoFileName.LayoutTransform = overlayTransform;
        PlaylistVideoSimpleSeek.LayoutTransform = Transform.Identity;
        PlaylistVideoSimpleSeek.Height = DesignMetrics.PlaylistVideoSimpleSeekHitHeight * overlayScale;
        PlaylistVideoSimpleSeek.Margin = DesignMetrics.PlaylistVideoSimpleSeekMargin(overlayScale);
        PlaylistVideoScaleToast.LayoutTransform = overlayTransform;
        PlaybackPitchToast.LayoutTransform = overlayTransform;
        ApplyPlaylistVideoTimecodeOverlayScale(overlayScale);
        ApplyPlaylistPdfPageNumberOverlayScale(overlayScale);
        ApplyPlaylistTrackIndexOverlayScale(overlayScale);
        SchedulePlaylistVideoFileNamePlacement();
    }

    /// <summary>動画ミニのタイムコードを窓比例で拡縮（高さ・余白）。F10／全画面は等倍。</summary>
    private void ApplyPlaylistVideoTimecodeOverlayScale(double scale)
    {
        var designHeight = DesignMetrics.PlaylistVideoTimecodeHostHeight;
        var designMargin = DesignMetrics.PlaylistVideoTimecodeMargin;
        if (!IsVideoMiniPlayerActive() || _playlistVideoFullscreen)
        {
            PlaylistVideoTimecode.LayoutTransform = Transform.Identity;
            PlaylistVideoTimecode.Height = designHeight;
            PlaylistVideoTimecode.Margin = designMargin;
            return;
        }

        var s = Math.Clamp(
            scale,
            DesignMetrics.OverlayScaleMin,
            DesignMetrics.OverlayScaleMax);
        if (double.IsNaN(s) || double.IsInfinity(s) || s <= 0)
        {
            s = 1;
        }

        // Viewbox の外寸だけ変える（中のデザイン寸法はそのまま → 確実に拡縮される）。
        PlaylistVideoTimecode.LayoutTransform = Transform.Identity;
        PlaylistVideoTimecode.Height = designHeight * s;
        PlaylistVideoTimecode.Margin = new Thickness(
            designMargin.Left * s,
            designMargin.Top * s,
            designMargin.Right * s,
            designMargin.Bottom * s);
    }

    /// <summary>動画ミニのリサイズ／F 全画面に合わせてアナライザ倍率と列幅・行高を更新する。</summary>
    private void SyncWindowProportionalOverlayScale()
    {
        ApplyAnalyzerMaximizeScale();
        if (!IsVideoMiniPlayerActive() || _waveformMaximizeMode == WaveformMaximizeMode.Waveform)
        {
            return;
        }

        ApplyVideoMiniOverlayLayout(VideoMiniOverlayLayoutScale);
    }

    /// <summary>
    /// 動画ミニの波形帯・下段行・スペアナ／ゴニオの揃え。
    /// 下段は SpectrumHeight×倍率だけ（TransportChromeHeight を下限にすると小さい窓で見切れる）。
    /// </summary>
    private void ApplyVideoMiniOverlayLayout(double scale)
    {
        PrimaryWaveform.OverlayChromeScale = scale;
        var waveH = LibraryPlayerMode.VideoMiniWaveformHeight(scale);
        LibraryRowDef.MinHeight = 0;
        LibraryRowDef.Height = new GridLength(1, GridUnitType.Star);
        LibraryWaveformRowDef.MinHeight = waveH;
        LibraryWaveformRowDef.MaxHeight = waveH;
        LibraryWaveformRowDef.Height = new GridLength(waveH);
        WaveformHostBorder.MinHeight = waveH;

        WorkGrid.RowDefinitions[1].Height = new GridLength(
            LibraryPlayerMode.VideoMiniTransportRowHeight(scale));
        // ゴニオ（右列下端）と同じ高さ帯にスペアナ／ラウドネスを揃える。
        Spectrum.VerticalAlignment = VerticalAlignment.Bottom;
        LoudnessMeter.VerticalAlignment = VerticalAlignment.Bottom;
        ApplyMeterColumnWidth(_meterColumnPreferred);
        ApplyLevelMeterHeightCap(scale);
        ScheduleLibraryWaveformPaint();
    }

    /// <summary>
    /// 動画ミニ専用：レベルメーターの縦伸び上限（列実高・16:9・ゴニオ／上段を考慮）。
    /// F10 など通常プレイヤーは Stretch のまま。
    /// 縦長ミニでは MeterPeakHost（WAAPI＋ピーク）を Bottom 揃えし、WAAPI がピーク直上に付く。
    /// </summary>
    private void ApplyLevelMeterHeightCap(double layoutScale)
    {
        if (!IsVideoMiniPlayerActive())
        {
            ClearLevelMeterHeightCap();
            return;
        }

        var columnH = MeterColumn.ActualHeight;
        if (columnH <= 1)
        {
            // 未レイアウト時は窓高からステータス分を引いた概算。
            var windowH = ActualHeight > 1 ? ActualHeight : SystemParameters.WorkArea.Height;
            columnH = Math.Max(0, windowH - DesignMetrics.StatusBarHeight);
        }

        var width = ActualWidth > 1 ? ActualWidth : SystemParameters.WorkArea.Width;
        var height = ActualHeight > 1 ? ActualHeight : SystemParameters.WorkArea.Height;
        var cap = LibraryPlayerMode.LevelMeterMaxLayoutHeight(columnH, width, height, layoutScale);
        if (double.IsInfinity(cap) || double.IsNaN(cap) || cap <= 0)
        {
            ClearLevelMeterHeightCap();
            return;
        }

        LevelMeter.MaxHeight = cap;
        LevelMeter.Height = cap;
        LevelMeter.VerticalAlignment = VerticalAlignment.Stretch;
        // WAAPI とピークを一塊にしてゴニオ直上へ。
        MeterPeakHost.VerticalAlignment = VerticalAlignment.Bottom;
    }

    private void ClearLevelMeterHeightCap()
    {
        LevelMeter.ClearValue(FrameworkElement.HeightProperty);
        LevelMeter.ClearValue(FrameworkElement.MaxHeightProperty);
        LevelMeter.VerticalAlignment = VerticalAlignment.Stretch;
        MeterPeakHost.VerticalAlignment = VerticalAlignment.Stretch;
    }

    private void ApplyWaveformMaximizeChrome()
    {
        var show = _waveformMaximizeMode != WaveformMaximizeMode.Waveform;
        var visibility = show ? Visibility.Visible : Visibility.Collapsed;
        var showOverview = _waveformMaximizeMode != WaveformMaximizeMode.Library;
        StatusBarHost.Visibility = Visibility.Visible;
        OverviewHost.Visibility = showOverview ? Visibility.Visible : Visibility.Collapsed;
        if (!showOverview)
        {
            Overview.CancelDrag();
        }
        TransportChromeHost.Visibility = visibility;
        MeterColumn.Visibility = visibility;
        MeterColumnSplitter.Visibility = visibility;
        ApplyAnalyzerMaximizeScale();
        ApplyWaapiPanelVisible();
        ApplyLibraryChrome();
        Transport.SetMaximizeMode(_waveformMaximizeMode);
        ApplyWaveformScaleLanes();
        RefreshTileDividers();
        OverviewScaleColumn.Width = show
            ? DesignMetrics.DbScaleWidthGrid
            : new GridLength(0);
        if (!show)
        {
            WorkGrid.RowDefinitions[1].Height = new GridLength(0);
            MeterColumnDef.MinWidth = 0;
            MeterColumnDef.MaxWidth = 0;
            MeterColumnDef.Width = new GridLength(0);
            return;
        }

        WorkGrid.RowDefinitions[1].Height = new GridLength(Math.Max(
            DesignMetrics.TransportChromeHeight,
            DesignMetrics.SpectrumHeight * AnalyzerMaximizeLayoutScale));
        ApplyMeterColumnWidth(_meterColumnPreferred);
        ApplyLibraryMinimalChrome();
    }

    /// <summary>
    /// F9／F8。プレイヤー中だけサイド列・ステータス・トランスポートを畳む。
    /// F8 動画ミニはピーク／ゴニオ／スペアナ／ラウドネスを残す。F9 はメーター列も畳む。
    /// 波形は残す。再生と Alt+S（Silent Skip）／Alt+A（Always on Top）は続ける。
    /// ApplyWaveformMaximizeChrome のあとに呼ぶ。
    /// </summary>
    private void ApplyLibraryMinimalChrome()
    {
        if (!IsLibraryMaximized)
        {
            LibraryBrowser.SetSidePanesVisible(true);
            RestoreMinimalChromeMeterHosts();
            SyncWindowMinSizeForMode(minimal: false);
            return;
        }

        LibraryBrowser.SetSidePanesVisible(!_libraryMinimalChrome);
        if (!_libraryMinimalChrome || _waveformMaximizeMode == WaveformMaximizeMode.Waveform)
        {
            RestoreMinimalChromeMeterHosts();
            SyncWindowMinSizeForMode(minimal: false);
            return;
        }

        StatusBarHost.Visibility = Visibility.Collapsed;
        if (_videoLaunchPlacement)
        {
            // 動画ミニ: アナライザ類＋WAAPI トグルを残す（リスト／トランスポート／タブは隠す）。
            // MeterTopSlot は F10 本再生と同じく出し、WAAPI ボタンだけ消えないようにする。
            TransportChromeHost.Visibility = Visibility.Visible;
            MeterColumn.Visibility = Visibility.Visible;
            MeterColumnSplitter.Visibility = Visibility.Collapsed;
            DocumentTabHost.Visibility = Visibility.Collapsed;
            HistoryStrip.Visibility = Visibility.Collapsed;
            TransportBarHost.Visibility = Visibility.Collapsed;
            MeterColumn.ClipToBounds = false;
            TransportChromeHost.ClipToBounds = false;
            WorkGrid.ClipToBounds = false;
            MeterTopSlot.Height = double.NaN;
            MeterTopSlot.Visibility = Visibility.Visible;
            // 行高・LayoutTransform は ApplyAnalyzerMaximizeScale（AnalyzerLayoutScale）に任せる。
            ApplyAnalyzerMaximizeScale();
            ApplyVideoMiniOverlayLayout(VideoMiniOverlayLayoutScale);
            SyncWindowMinSizeForMode(videoMini: true);
            return;
        }

        TransportChromeHost.Visibility = Visibility.Collapsed;
        MeterColumn.Visibility = Visibility.Collapsed;
        MeterColumnSplitter.Visibility = Visibility.Collapsed;
        WorkGrid.RowDefinitions[1].Height = new GridLength(0);
        MeterColumnDef.MinWidth = 0;
        MeterColumnDef.MaxWidth = 0;
        MeterColumnDef.Width = new GridLength(0);
        SyncWindowMinSizeForMode(minimal: true);
    }

    private void RestoreMinimalChromeMeterHosts()
    {
        // プレイヤー中はタブ／履歴ストリップを出さない（ApplyLibraryChrome の Collapsed を上書きしない）。
        if (!IsLibraryMaximized)
        {
            if (DocumentTabHost.Visibility != Visibility.Visible)
            {
                DocumentTabHost.Visibility = Visibility.Visible;
            }

            if (HistoryStrip.Visibility != Visibility.Visible)
            {
                HistoryStrip.Visibility = Visibility.Visible;
            }
        }

        if (TransportBarHost.Visibility != Visibility.Visible)
        {
            TransportBarHost.Visibility = Visibility.Visible;
        }

        if (MeterTopSlot.Visibility != Visibility.Visible)
        {
            MeterTopSlot.Visibility = Visibility.Visible;
        }

        MeterTopSlot.Height = DesignMetrics.ProjectBarHeight;
        MeterColumn.ClearValue(UIElement.ClipToBoundsProperty);
        TransportChromeHost.ClearValue(UIElement.ClipToBoundsProperty);
        WorkGrid.ClearValue(UIElement.ClipToBoundsProperty);
        Spectrum.VerticalAlignment = VerticalAlignment.Top;
        LoudnessMeter.VerticalAlignment = VerticalAlignment.Stretch;
        ClearLevelMeterHeightCap();
    }

    private void SyncWindowMinSizeForMode(bool minimal = false, bool videoMini = false)
    {
        double unscaledWidth;
        double unscaledHeight;
        if (videoMini)
        {
            // F8: 原寸 25% まで縮める（F9 の 280 下限だと 47% 前後で止まる）。
            unscaledWidth = DesignMetrics.VideoMiniWindowMinWidth;
            unscaledHeight = DesignMetrics.VideoMiniWindowMinHeight;
        }
        else if (minimal)
        {
            unscaledWidth = DesignMetrics.MinimalPlayerWindowMinWidth;
            unscaledHeight = DesignMetrics.MinimalPlayerWindowMinHeight;
        }
        else
        {
            unscaledWidth = DesignMetrics.WindowMinWidth;
            unscaledHeight = DesignMetrics.WindowMinHeight;
        }

        UiScaleService.SetUnscaledMinWidth(this, unscaledWidth);
        UiScaleService.SetUnscaledMinHeight(this, unscaledHeight);
        if (WindowState == WindowState.Normal)
        {
            if (Width < MinWidth)
            {
                Width = MinWidth;
            }

            if (Height < MinHeight)
            {
                Height = MinHeight;
            }
        }
    }
}

internal enum WaveformMaximizeMode
{
    Off,
    Waveform,
    Analyzers,
    Library,
}
