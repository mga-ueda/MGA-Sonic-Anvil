using System.Windows;
using System.Windows.Media;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

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
                StopPlayback();
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
            case TransportCommand.Open:
                OpenFromDialog();
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
        }
    }

    private void TogglePlayback()
    {
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
            _player.Prepare(_document, startFrame, playRange, loop: playRange is not null);
            _player.Play();
            _playbackGeneration = _player.Generation;
            _playTimer.Start();
            StartMeterRendering();
            Waveform.SetTrailRecording(true);
            Transport.SetPlaying(true);
            Waveform.PlayheadFrame = startFrame;
        }
        catch (Exception ex)
        {
            PausePlaybackSoft();
            OwnerCenteredMessageBox.Show(this, ex.Message, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void StopPlayback() => HaltPlaybackToStart();

    private bool PausePlaybackHere()
    {
        if (_document is null || !IsPlaybackActive())
        {
            return false;
        }

        var frame = _player.CursorFrame;
        _fadePreviewing = false;
        PausePlaybackSoft();
        SeekFrame(frame);
        RestoreFadeVisualIfMenuOpen();
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
        RefreshStatus();
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
            return;
        }

        _player.SetPlayWindow(_document.Selection, loop: true);
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

        if (_player.ProviderEnded)
        {
            OnPlaybackEnded(_playbackGeneration);
            return;
        }

        if (_formatPreviewing)
        {
            return;
        }

        var frame = _player.CursorFrame;
        _document.CursorFrame = frame;
        if (!Waveform.IsInteracting)
        {
            Waveform.PlayheadFrame = frame;
            Waveform.FollowPlayhead();
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

        var hasSamples = _player.TakeMeterInterval(out var peakL, out var rmsL, out var peakR, out var rmsR);
        LevelMeter.Apply(_meter.Update(peakL, rmsL, peakR, rmsR, _meterClock.Elapsed.TotalSeconds, hasSamples));
    }

    private void ExtinguishMeter()
    {
        _meter.Extinguish();
        LevelMeter.Extinguish();
    }

    private void OnScrubStarted(long frame)
    {
        if (_document is null)
        {
            return;
        }

        _resumeAfterScrub = _player.IsPlaying && !_player.IsScrubbing;
        if (_fadePreviewing)
        {
            _fadePreviewing = false;
            Waveform.SetPreviewGain(null);
        }

        if (_formatPreviewing)
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

        if (_fadePreviewing)
        {
            if (Environment.TickCount64 - _fadePreviewStartedAt < 250)
            {
                return;
            }

            _fadePreviewing = false;
            PausePlaybackSoft();
            if (_document is not null)
            {
                SeekFrame(_fadePreviewResumeFrame);
            }

            RestoreFadeVisualIfMenuOpen();
            return;
        }

        if (_formatPreviewing)
        {
            if (Environment.TickCount64 - _formatPreviewStartedAt < 250)
            {
                return;
            }

            StopFormatPreview();
            return;
        }

        if (_document is { Selection.IsEmpty: false })
        {
            PausePlaybackSoft();
            return;
        }

        HaltPlaybackToStart();
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

    private void SetRegionFromSelection()
    {
        if (_document is null)
        {
            return;
        }

        var range = _document.Selection;
        if (range.IsEmpty)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
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
