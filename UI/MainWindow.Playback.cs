using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
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
                    ToggleLibraryTransportPlayback();
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
            case TransportCommand.CycleWaveformHeight:
                CycleWaveformHeight();
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
                SaveAsMp3();
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
            case TransportCommand.ToggleUiTheme:
                UiThemeService.ToggleDarkLight();
                break;
            case TransportCommand.OpenColorPanel:
                ShowColorDevPanel();
                break;
            case TransportCommand.ToggleTips:
                ToggleTips();
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
        _player.IsPlaying || _player.IsScrubbing || _playTimer.IsEnabled;

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
        IsLibraryMaximized
            ? LibraryPlayerMode.IsPlayerShuttleKey(key, modifiers)
            : modifiers == ModifierKeys.None && key is Key.Left or Key.Right;

    private bool CanPlaybackShuttle() =>
        _player.IsPlaying && !_player.IsScrubbing && !AnyEffectPreviewing;

    private bool BeginOrContinuePlaybackShuttle(int direction)
    {
        if (direction == 0)
        {
            return false;
        }

        if (_playbackShuttleDirection == direction)
        {
            return true;
        }

        StopSeekNudge();
        _playbackShuttleDirection = direction;
        _player.SetPlaybackSpeed(
            direction < 0 ? -PlaybackSampleProvider.FastSpeed : PlaybackSampleProvider.FastSpeed);
        return true;
    }

    private void StopPlaybackShuttle()
    {
        if (_playbackShuttleDirection == 0)
        {
            return;
        }

        _playbackShuttleDirection = 0;
        _player.SetPlaybackSpeed(1);
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

        // プレイヤーから戻った MP3 などは、昇格前だとストリーム早戻し（ピッチ変化）のままになる。
        if (!IsLibraryMaximized
            && _activeSession is { } session
            && LibraryPlayerMode.NeedsEditorPcmUpgrade(session.Document))
        {
            _ = StartPlaybackAfterEditorPcmAsync(session, startFrame, prerollSeconds);
            return;
        }

        WaveSelection? playRange = _document.Selection.IsEmpty ? null : _document.Selection;
        if (playRange is { } range && (startFrame < range.StartFrame || startFrame >= range.EndFrame))
        {
            startFrame = range.StartFrame;
        }

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
            _playTimer.Start();
            StartMeterRendering();
            Waveform.SetTrailRecording(true);
            Transport.SetPlaying(true);
            Waveform.PlayheadFrame = startFrame;
            SyncOverviewPlayhead();
        }
        catch (Exception ex)
        {
            PausePlaybackSoft();
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

    /// <summary>
    /// エディタで PCM のまま再生している曲を、プレイヤーへ戻したときに可変速へ戻す。
    /// 波形用の PCM とピークは捨てない。
    /// </summary>
    private void ResumePlayerVariableRate()
    {
        if (_document is null || !IsPlaybackActive() || _player.IsStreamBound)
        {
            return;
        }

        if (_document.SourcePath is not { Length: > 0 } path
            || !File.Exists(path)
            || !AudioCodec.CanStreamPlay(path))
        {
            return;
        }

        WaveSelection? playRange = _document.Selection.IsEmpty ? null : _document.Selection;
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

    private bool PausePlaybackHere()
    {
        if (_document is null || !IsPlaybackActive())
        {
            return false;
        }

        var frame = _player.CursorFrame;
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
        PausePlaybackSoft();
        SeekFrame(frame);
        RestoreFadeVisualIfMenuOpen();
        RestoreVolumeVisualIfMenuOpen();
        return true;
    }

    private void HaltPlaybackToStart()
    {
        _editorPlayAfterPcmTicket++;
        PausePlaybackSoft();
        if (_document is not null)
        {
            SeekFrame(_lastPlaybackStart);
        }
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

        _document.CursorFrame = frame;
        Waveform.PlayheadFrame = frame;
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

        if (_document.Selection.IsEmpty)
        {
            _player.SetPlayWindow(null, loop: false);
            _player.SetExitSpan(null);
            return;
        }

        _player.SetPlayWindow(_document.Selection, loop: true);
        _player.SetExitSpan(ComputeExitLayerSpan(_document.Selection));
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
    /// 範囲選択中のマーカー／リージョン端を再生時にクリックで鳴らす。
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
        if (!_rangeClickEnabled || IsLibraryMaximized || _document is null || _document.Selection.IsEmpty)
        {
            _player.SetRangeClickFrames([]);
            return;
        }

        var frames = DocumentRangeDivide.ClickFrames(_document);
        var group = RangeClickMeter.GroupSize(
            RangeClickMeter.BeatCount(frames, _document.Selection));
        _player.SetRangeClickFrames(frames, group);
    }

    private void OnWaveformSelectionChanged()
    {
        SyncRangeClicks();

        Overview.InvalidateVisual();
        RefreshStatus();
        if (_document is null || !_player.IsPlaying)
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

        var cursor = _player.CursorFrame;
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

        _player.ReadPlayheadVisuals(out var frame, out var exitFrame);
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

    private void StartMeterRendering()
    {
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
        if (!_player.IsPlaying)
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
        var minTicks = LibraryPlayerMode.PlaybackVisualMinIntervalMs / 1000d * Stopwatch.Frequency;
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
        _meter.Extinguish();
        LevelMeter.Apply(_meter.Snapshot);
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

        if (IsLibraryMaximized && _libraryStopAfterTrack)
        {
            _libraryStopAfterTrack = false;
            HaltPlaybackToStart();
            return;
        }

        if (IsLibraryMaximized && TryAdvanceLibraryPlaylist())
        {
            return;
        }

        HaltPlaybackToStart();
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

    private void CycleWaveformHeight()
    {
        _waveformHeightScale = _waveformHeightScale >= 3 ? 1 : _waveformHeightScale + 1;
        ApplyWaveformHeightScale();
        AppStorage.Settings.WaveformHeightScale = _waveformHeightScale;
        AppStorage.Save();
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

    private void ApplyWaveformHeightScale()
    {
        WaveformHostBorder.MinHeight = DesignMetrics.WaveformHostMinHeight * _waveformHeightScale;
    }

    private void ToggleAnalyzerMaximize() => SetWaveformMaximizeMode(
        _waveformMaximizeMode == WaveformMaximizeMode.Analyzers
            ? WaveformMaximizeMode.Off
            : WaveformMaximizeMode.Analyzers);

    private void ToggleWaveformMaximize() => SetWaveformMaximizeMode(
        _waveformMaximizeMode == WaveformMaximizeMode.Waveform
            ? WaveformMaximizeMode.Off
            : WaveformMaximizeMode.Waveform);

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

    private void SetWaveformMaximizeMode(WaveformMaximizeMode mode, bool? playFirstOnLibrary = null)
    {
        if (_waveformMaximizeMode == mode)
        {
            return;
        }

        if (_waveformMaximizeMode == WaveformMaximizeMode.Library
            && mode != WaveformMaximizeMode.Library)
        {
            if (!KeepOnlyLibrarySelectedSessions())
            {
                return;
            }

            LibraryBrowser.ShuffleEnabled = false;

            if (IsPlaybackActive())
            {
                StopPlayback();
            }
        }

        if (mode == WaveformMaximizeMode.Library)
        {
            _libraryPlayFirstPending = playFirstOnLibrary ?? true;
            SyncRangeClicks();
        }

        var wasLibrary = _waveformMaximizeMode == WaveformMaximizeMode.Library;
        var wantLibrary = mode == WaveformMaximizeMode.Library;
        var wasFullscreen = IsFullscreenMaximizeMode(_waveformMaximizeMode);
        var wantFullscreen = IsFullscreenMaximizeMode(mode);
        if (!wasFullscreen)
        {
            PersistCurrentWindowPlacement();
        }

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
            if (wantLibrary && wasFullscreen)
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
            if (wantLibrary || wasLibrary)
            {
                TryApplyCurrentModePlacement();
            }
        }

        if (wantLibrary)
        {
            ScheduleLibraryWaveformPaint();
        }

        if (!wasFullscreen || wasLibrary || wantLibrary)
        {
            AppStorage.Save();
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
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        if (WindowState != WindowState.Normal)
        {
            WindowState = WindowState.Normal;
        }

        if (!WindowPlacement.TryGetContainingMonitorDip(this, out var monitor))
        {
            return;
        }

        Left = monitor.X;
        Top = monitor.Y;
        Width = monitor.Width;
        Height = monitor.Height;
    }

    private void PersistCurrentWindowPlacement()
    {
        if (IsFullscreenMaximizeMode(_waveformMaximizeMode))
        {
            return;
        }

        WindowPlacement.Capture(this, AppStorage.Settings, CurrentPlacementKind());
    }

    private bool TryApplyCurrentModePlacement() =>
        WindowPlacement.TryApply(this, AppStorage.Settings, CurrentPlacementKind());

    private MainWindowPlacementKind CurrentPlacementKind()
    {
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

    private void RestoreWaveformWindowFrame()
    {
        RestoreWaveformWindowChrome();
        ApplyRememberedWindowFrameBounds();
    }

    private double AnalyzerMaximizeLayoutScale =>
        _waveformMaximizeMode == WaveformMaximizeMode.Analyzers
            ? DesignMetrics.AnalyzerMaximizeScale
            : 1d;

    private void ApplyAnalyzerMaximizeScale()
    {
        var transform = AnalyzerMaximizeLayoutScale > 1.0001
            ? UiScaleService.CreatePublishedTransform(AnalyzerMaximizeLayoutScale)
            : Transform.Identity;
        LevelMeter.LayoutTransform = transform;
        VectorScope.LayoutTransform = transform;
        LoudnessMeter.LayoutTransform = transform;
        Spectrum.LayoutTransform = transform;
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
        TipService.SetHostSuppressed(_waveformMaximizeMode == WaveformMaximizeMode.Waveform);
        ApplyWaapiPanelVisible();
        ApplyLibraryChrome();
        Transport.SetMaximizeMode(_waveformMaximizeMode);
        PrimaryWaveform.ShowScaleLane = ShowWaveformScaleLane;
        ForEachWaveform(view => view.ShowScaleLane = ShowWaveformScaleLane);
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
    /// F9。プレイヤー中だけサイド列・ステータス・トランスポート／メーター列を畳む。
    /// 波形は残す。再生と Alt+S（Silent Skip）は続ける。
    /// ApplyWaveformMaximizeChrome のあとに呼ぶ。
    /// </summary>
    private void ApplyLibraryMinimalChrome()
    {
        if (!IsLibraryMaximized)
        {
            LibraryBrowser.SetSidePanesVisible(true);
            SyncWindowMinSizeForMode(minimal: false);
            return;
        }

        LibraryBrowser.SetSidePanesVisible(!_libraryMinimalChrome);
        if (!_libraryMinimalChrome || _waveformMaximizeMode == WaveformMaximizeMode.Waveform)
        {
            SyncWindowMinSizeForMode(minimal: false);
            return;
        }

        TransportChromeHost.Visibility = Visibility.Collapsed;
        MeterColumn.Visibility = Visibility.Collapsed;
        MeterColumnSplitter.Visibility = Visibility.Collapsed;
        StatusBarHost.Visibility = Visibility.Collapsed;
        WorkGrid.RowDefinitions[1].Height = new GridLength(0);
        MeterColumnDef.MinWidth = 0;
        MeterColumnDef.MaxWidth = 0;
        MeterColumnDef.Width = new GridLength(0);
        SyncWindowMinSizeForMode(minimal: true);
    }

    private void SyncWindowMinSizeForMode(bool minimal)
    {
        var unscaledWidth = minimal
            ? DesignMetrics.MinimalPlayerWindowMinWidth
            : DesignMetrics.WindowMinWidth;
        var unscaledHeight = minimal
            ? DesignMetrics.MinimalPlayerWindowMinHeight
            : DesignMetrics.WindowMinHeight;
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
