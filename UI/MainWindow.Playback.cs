using System.Windows;
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
        switch (command)
        {
            case TransportCommand.TogglePlayback:
                TogglePlayback();
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
            case TransportCommand.GoToStart:
                Waveform.ClearSelection();
                SeekFrame(0);
                Waveform.PanTimeToStart();
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
            case TransportCommand.Normalize:
                ApplyNormalize();
                break;
            case TransportCommand.Delete:
                ApplyDelete();
                break;
            case TransportCommand.Save:
                Save(saveAs: false);
                break;
            case TransportCommand.SaveMp3:
                SaveAsMp3();
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
            LoudnessMeter.Reset();
            _player.Prepare(_document, startFrame, playRange, loop: playRange is not null);
            _player.SetExitSpan(ComputeExitLayerSpan(playRange));
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
        PausePlaybackSoft();
        if (_document is not null)
        {
            SeekFrame(_lastPlaybackStart);
        }
    }

    private void SeekFrame(long frame)
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

        _player.Seek(frame);
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

    private void OnWaveformSelectionChanged()
    {
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
        if (_document is null)
        {
            return;
        }

        var frame = _player.SmoothCursorFrame;
        _document.CursorFrame = frame;
        if (!Waveform.IsInteracting)
        {
            Waveform.PlayheadFrame = frame;
            Waveform.ExitPlayheadFrame = _player.ExitCursorFrame;
            Waveform.FollowPlayhead();
            SyncOverviewPlayhead();
            SyncTransportPosition(frame);
        }
    }

    private void StartMeterRendering()
    {
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

        Span<float> peaks = stackalloc float[ChannelLayout.MaxChannels];
        Span<float> rms = stackalloc float[ChannelLayout.MaxChannels];
        var hasSamples = _player.TakeMeterInterval(peaks, rms, out var meterChannels);
        LevelMeter.Apply(_meter.Update(
            peaks[..Math.Max(1, meterChannels)],
            rms[..Math.Max(1, meterChannels)],
            _meterClock.Elapsed.TotalSeconds,
            hasSamples));
        // スペアナ・ゴニオも Background タイマー飢餓を避けてフレーム駆動で更新する。
        Spectrum.Tick();
        LoudnessMeter.Tick();
        VectorScope.Tick();
        if (AnyEffectPreviewing)
        {
            SyncPreviewPlayhead();
            return;
        }

        // 再生ヘッド・追従スクロールは vsync 同期でサンプリングしないとジッターが
        // 見えるため、ここ（毎フレーム）で行う。重い静的再描画は WaveformView 側の
        // 適応間引き（ペイント終了からコスト比例の休止を必ず挟む）が抑えるので入力飢餓にはならない。
        if (_playTimer.IsEnabled && !_player.IsScrubbing)
        {
            SyncPlaybackVisuals();
        }
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
        Waveform.ApplySpeakerLayout(speaker, fileMap);
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

    private void ApplyWaveformHeightScale()
    {
        var host = Waveform.Parent as System.Windows.FrameworkElement;
        if (host?.Parent is System.Windows.FrameworkElement border)
        {
            border.MinHeight = DesignMetrics.WaveformHostMinHeight * _waveformHeightScale;
        }
    }
}
