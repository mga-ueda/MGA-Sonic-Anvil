using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
    private void PromptFade(bool fadeIn, FrameworkElement? placementTarget = null)
    {
        if (_document is null)
        {
            return;
        }

        var range = ActiveRange();
        if (range.IsEmpty)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        CloseFormatConvertPicker();
        CloseVolumeGainPicker();
        ClosePitchShiftPicker();
        CloseTimeStretchPicker();
        if (_fadeMenu is { IsOpen: true })
        {
            _fadeMenu.IsOpen = false;
        }

        if (_player.IsPlaying)
        {
            PausePlaybackSoft();
        }

        _fadePromptIsIn = fadeIn;
        _fadePreview.ResumeFrame = _document.CursorFrame;
        _fadeReplayOnHighlight = false;
        var initial = fadeIn
            ? AppStorage.Settings.ResolvedFadeInCurve()
            : AppStorage.Settings.ResolvedFadeOutCurve();
        var menu = FadeCurvePicker.Show(
            this,
            PlacementMode.Center,
            fadeIn,
            shape => ApplyFade(fadeIn, shape),
            shape => PreviewFade(fadeIn, shape),
            shape => OnFadeCurveHighlighted(fadeIn, shape),
            initial);
        _fadeMenu = menu;
        menu.Closed += (_, _) =>
        {
            _fadeReplayOnHighlight = false;
            StopFadePreview(restoreCursor: true);
            ClearFadeCurveVisualPreview();
            if (ReferenceEquals(_fadeMenu, menu))
            {
                _fadeMenu = null;
            }
        };
        Dispatcher.BeginInvoke(() => _fadeReplayOnHighlight = true, DispatcherPriority.ApplicationIdle);
    }

    private bool CloseFadeCurvePicker()
    {
        if (_fadeMenu is not { IsOpen: true })
        {
            return false;
        }

        _fadeMenu.IsOpen = false;
        return true;
    }

    private void PromptVolume()
    {
        if (_document is null)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoDocument, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var range = ActiveRange();
        if (range.IsEmpty)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_volumeMenu is { IsOpen: true })
        {
            return;
        }

        if (Waveform.AnalysisView != WaveformAnalysisView.Loudness)
        {
            Waveform.SetAnalysisView(WaveformAnalysisView.Loudness);
        }

        CloseFadeCurvePicker();
        CloseFormatConvertPicker();
        ClosePitchShiftPicker();
        CloseTimeStretchPicker();
        if (_player.IsPlaying)
        {
            PausePlaybackSoft();
        }

        _volumePreview.ResumeFrame = _document.CursorFrame;
        var menu = VolumeGainPicker.Show(
            this,
            AppStorage.Settings.ResolvedLoudnessTargetLufs(),
            ApplyVolume,
            PreviewVolume,
            OnVolumeGainChanged);
        _volumeMenu = menu;
        menu.Closed += (_, _) =>
        {
            StopVolumePreview(restoreCursor: true);
            ClearVolumeVisualPreview();
            if (ReferenceEquals(_volumeMenu, menu))
            {
                _volumeMenu = null;
            }
        };

        var interleaved = _document.Interleaved;
        var channels = _document.Channels;
        var sampleRate = _document.SampleRate;
        _ = Task.Run(() => WaveformGainAnalyzer.Build(interleaved, channels, sampleRate, range))
            .ContinueWith(
                task =>
                {
                    if (!task.IsCompletedSuccessfully)
                    {
                        return;
                    }

                    menu.Dispatcher.BeginInvoke(() =>
                    {
                        if (!ReferenceEquals(_volumeMenu, menu) || menu is not { IsOpen: true })
                        {
                            return;
                        }

                        VolumeGainPicker.SetAnalyzer(menu, task.Result);
                    });
                },
                TaskScheduler.Default);
    }

    private bool CloseVolumeGainPicker()
    {
        if (_volumeMenu is not { IsOpen: true })
        {
            return false;
        }

        _volumeMenu.IsOpen = false;
        return true;
    }

    private void PromptPitch()
    {
        if (IsUiBusy)
        {
            return;
        }

        if (_document is null)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoDocument, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var range = ActiveRange();
        if (range.IsEmpty)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_pitchMenu is { IsOpen: true })
        {
            return;
        }

        CloseFadeCurvePicker();
        CloseFormatConvertPicker();
        CloseVolumeGainPicker();
        CloseTimeStretchPicker();
        if (_player.IsPlaying)
        {
            PausePlaybackSoft();
        }

        _pitchPreview.ResumeFrame = _document.CursorFrame;
        var menu = PitchShiftPicker.Show(this, ApplyPitch, PreviewPitch, OnPitchChanged);
        _pitchMenu = menu;
        menu.Closed += (_, _) =>
        {
            StopPitchPreview(restoreCursor: true);
            if (ReferenceEquals(_pitchMenu, menu))
            {
                _pitchMenu = null;
            }
        };
    }

    private bool ClosePitchShiftPicker()
    {
        if (_pitchMenu is not { IsOpen: true })
        {
            return false;
        }

        _pitchMenu.IsOpen = false;
        return true;
    }

    private void OnPitchChanged(int semitones, bool timeStretch)
    {
        if (!_pitchPreview.Previewing || _document is null || _pitchPreview.Toggling)
        {
            return;
        }

        var range = ActiveRange();
        if (range.IsEmpty)
        {
            return;
        }

        StartPitchPreviewPlayback(range, semitones, timeStretch);
    }

    private void PreviewPitch(int semitones, bool timeStretch)
    {
        if (_document is null || _pitchPreview.Toggling)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (now - _pitchPreview.SpaceTick < 120)
        {
            return;
        }

        _pitchPreview.SpaceTick = now;
        _pitchPreview.Toggling = true;
        try
        {
            if (_pitchPreview.Previewing && _player.IsPlaying)
            {
                StopPitchPreview(restoreCursor: true);
                return;
            }

            var range = ActiveRange();
            if (range.IsEmpty)
            {
                return;
            }

            StartPitchPreviewPlayback(range, semitones, timeStretch);
        }
        finally
        {
            _pitchPreview.Toggling = false;
        }
    }

    private void StartPitchPreviewPlayback(WaveSelection range, int semitones, bool timeStretch)
    {
        if (_document is null)
        {
            return;
        }

        try
        {
            if (_player.IsPlaying)
            {
                _player.Pause();
            }

            _playTimer.Stop();
            _meter.Reset();
            LoudnessMeter.Reset();
            var source = _document.CopyRange(range.StartFrame, range.Length);
            var pitched = Audio.PitchShift.IsNoOp(semitones)
                ? source
                : Audio.PitchShift.Apply(
                    source,
                    _document.Channels,
                    _document.SampleRate,
                    semitones,
                    timeStretch);
            var preview = new AudioDocument(
                pitched,
                _document.SampleRate,
                _document.Channels,
                _document.BitsPerSample,
                _document.SourceKind,
                null);
            var previewRange = new WaveSelection(0, preview.FrameCount);
            if (_player.HasOutputDevice)
            {
                _player.Rebind(preview, 0, previewRange, loop: false);
            }
            else
            {
                _player.Prepare(preview, 0, previewRange, loop: false);
            }

            _pitchPreview.Origin = range.StartFrame;
            _pitchPreview.Previewing = true;
            _pitchPreview.StartedAt = Environment.TickCount64;
            _player.Play();
            _playbackGeneration = _player.Generation;
            _playTimer.Start();
            StartMeterRendering();
            Waveform.SetTrailRecording(true);
            Transport.SetPlaying(true);
            Waveform.UnlockCenter();
            Waveform.SetPlayheadFromPlayback(range.StartFrame);
        }
        catch (Exception ex)
        {
            _pitchPreview.Previewing = false;
            PausePlaybackSoft();
            OwnerCenteredMessageBox.Show(this, ex.Message, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void StopPitchPreview(bool restoreCursor)
    {
        if (!_pitchPreview.Previewing)
        {
            return;
        }

        var resume = _pitchPreview.ResumeFrame;
        _pitchPreview.Previewing = false;
        PausePlaybackSoft();
        if (restoreCursor && _document is not null)
        {
            SeekFrame(resume);
        }
    }

    private void ApplyPitch(int semitones, bool timeStretch) =>
        _ = ApplyPitchAsync(semitones, timeStretch);

    private async Task ApplyPitchAsync(int semitones, bool timeStretch)
    {
        if (_document is null || IsUiBusy)
        {
            return;
        }

        var range = ActiveRange();
        if (range.IsEmpty)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _pitchPreview.Previewing = false;
        ClosePitchShiftPicker();
        if (Audio.PitchShift.IsNoOp(semitones))
        {
            return;
        }

        PausePlaybackSoft();
        var document = _document;
        _pitchShiftBusy = true;
        Exception? error = null;
        IEditCommand? command = null;
        try
        {
            ShowPitchShiftBusyGlass();
            var progress = new Progress<double>(p => _busyGlass.SetProgress(p));
            command = await Task.Run(() => ProcessEdits.PitchShift(document, range, semitones, timeStretch, progress, channelMask: EditMask()))
                .ConfigureAwait(true);
            if (!IsLoaded || !ReferenceEquals(_document, document) || command is null)
            {
                return;
            }

            _history.Do(document, command);
            AfterTransform();
            PausePlaybackSoft();
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            _pitchShiftBusy = false;
            if (error is not null)
            {
                _busyGlass.HideOverlay();
            }
            else
            {
                _busyGlass.BeginFadeOut();
            }
        }

        if (error is not null && IsLoaded)
        {
            OwnerCenteredMessageBox.Show(
                this,
                error.Message,
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void PromptTimeStretch()
    {
        if (IsUiBusy)
        {
            return;
        }

        if (_document is null)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoDocument, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var range = ActiveRange();
        if (range.IsEmpty)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_timeStretchMenu is { IsOpen: true })
        {
            return;
        }

        CloseFadeCurvePicker();
        CloseFormatConvertPicker();
        CloseVolumeGainPicker();
        ClosePitchShiftPicker();
        if (_player.IsPlaying)
        {
            PausePlaybackSoft();
        }

        _timeStretchPreview.ResumeFrame = _document.CursorFrame;
        var menu = TimeStretchPicker.Show(
            this,
            (int)Math.Min(int.MaxValue, range.Length),
            _document.SampleRate,
            ApplyTimeStretch,
            PreviewTimeStretch,
            OnTimeStretchChanged);
        _timeStretchMenu = menu;
        menu.Closed += (_, _) =>
        {
            StopTimeStretchPreview(restoreCursor: true);
            if (ReferenceEquals(_timeStretchMenu, menu))
            {
                _timeStretchMenu = null;
            }
        };
    }

    private bool CloseTimeStretchPicker()
    {
        if (_timeStretchMenu is not { IsOpen: true })
        {
            return false;
        }

        _timeStretchMenu.IsOpen = false;
        return true;
    }

    private void OnTimeStretchChanged(int destFrames)
    {
        if (!_timeStretchPreview.Previewing || _document is null || _timeStretchPreview.Toggling)
        {
            return;
        }

        var range = ActiveRange();
        if (range.IsEmpty)
        {
            return;
        }

        StartTimeStretchPreviewPlayback(range, destFrames);
    }

    private void PreviewTimeStretch(int destFrames)
    {
        if (_document is null || _timeStretchPreview.Toggling)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (now - _timeStretchPreview.SpaceTick < 120)
        {
            return;
        }

        _timeStretchPreview.SpaceTick = now;
        _timeStretchPreview.Toggling = true;
        try
        {
            if (_timeStretchPreview.Previewing && _player.IsPlaying)
            {
                StopTimeStretchPreview(restoreCursor: true);
                return;
            }

            var range = ActiveRange();
            if (range.IsEmpty)
            {
                return;
            }

            StartTimeStretchPreviewPlayback(range, destFrames);
        }
        finally
        {
            _timeStretchPreview.Toggling = false;
        }
    }

    private void StartTimeStretchPreviewPlayback(WaveSelection range, int destFrames)
    {
        if (_document is null)
        {
            return;
        }

        try
        {
            if (_player.IsPlaying)
            {
                _player.Pause();
            }

            _playTimer.Stop();
            _meter.Reset();
            LoudnessMeter.Reset();
            var source = _document.CopyRange(range.StartFrame, range.Length);
            var stretched = Audio.TimeStretch.IsNoOp((int)range.Length, destFrames)
                ? source
                : Audio.TimeStretch.Apply(
                    source,
                    _document.Channels,
                    _document.SampleRate,
                    destFrames);
            var preview = new AudioDocument(
                stretched,
                _document.SampleRate,
                _document.Channels,
                _document.BitsPerSample,
                _document.SourceKind,
                null);
            var previewRange = new WaveSelection(0, preview.FrameCount);
            if (_player.HasOutputDevice)
            {
                _player.Rebind(preview, 0, previewRange, loop: false);
            }
            else
            {
                _player.Prepare(preview, 0, previewRange, loop: false);
            }

            _timeStretchPreview.Origin = range.StartFrame;
            _timeStretchPreview.Previewing = true;
            _timeStretchPreview.StartedAt = Environment.TickCount64;
            _player.Play();
            _playbackGeneration = _player.Generation;
            _playTimer.Start();
            StartMeterRendering();
            Waveform.SetTrailRecording(true);
            Transport.SetPlaying(true);
            Waveform.UnlockCenter();
            Waveform.SetPlayheadFromPlayback(range.StartFrame);
        }
        catch (Exception ex)
        {
            _timeStretchPreview.Previewing = false;
            PausePlaybackSoft();
            OwnerCenteredMessageBox.Show(this, ex.Message, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void StopTimeStretchPreview(bool restoreCursor)
    {
        if (!_timeStretchPreview.Previewing)
        {
            return;
        }

        var resume = _timeStretchPreview.ResumeFrame;
        _timeStretchPreview.Previewing = false;
        PausePlaybackSoft();
        if (restoreCursor && _document is not null)
        {
            SeekFrame(resume);
        }
    }

    private void ApplyTimeStretch(int destFrames) =>
        _ = ApplyTimeStretchAsync(destFrames);

    private async Task ApplyTimeStretchAsync(int destFrames)
    {
        if (_document is null || IsUiBusy)
        {
            return;
        }

        var range = ActiveRange();
        if (range.IsEmpty)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _timeStretchPreview.Previewing = false;
        CloseTimeStretchPicker();
        destFrames = Audio.TimeStretch.ClampDestFrames((int)range.Length, destFrames);
        if (Audio.TimeStretch.IsNoOp((int)range.Length, destFrames))
        {
            return;
        }

        PausePlaybackSoft();
        var document = _document;
        var viewStart = Waveform.ViewStart;
        _timeStretchBusy = true;
        Exception? error = null;
        IEditCommand? command = null;
        try
        {
            ShowTimeStretchBusyGlass();
            var progress = new Progress<double>(p => _busyGlass.SetProgress(p));
            command = await Task.Run(() => ProcessEdits.TimeStretch(document, range, destFrames, progress, channelMask: EditMask()))
                .ConfigureAwait(true);
            if (!IsLoaded || !ReferenceEquals(_document, document) || command is null)
            {
                return;
            }

            _history.Do(document, command);
            var mappedDest = ChannelSamples.IsScoped(EditMask(), document.Channels)
                ? range.Length
                : destFrames;
            Waveform.SetViewStartExternal(
                AudioDocument.MapFrameThroughRangeStretch(
                    (long)Math.Round(viewStart),
                    range.StartFrame,
                    range.Length,
                    mappedDest));
            AfterTransform();
            PausePlaybackSoft();
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            _timeStretchBusy = false;
            if (error is not null)
            {
                _busyGlass.HideOverlay();
            }
            else
            {
                _busyGlass.BeginFadeOut();
            }
        }

        if (error is not null && IsLoaded)
        {
            OwnerCenteredMessageBox.Show(
                this,
                error.Message,
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnVolumeGainChanged(double gainDb)
    {
        ApplyVolumeVisualPreview(gainDb);
        if (!_volumePreview.Previewing || _document is null || _volumePreview.Toggling)
        {
            return;
        }

        var range = ActiveRange();
        if (range.IsEmpty)
        {
            return;
        }

        StartVolumePreviewPlayback(range, gainDb);
    }

    private void ApplyVolumeVisualPreview(double gainDb)
    {
        if (_document is null)
        {
            ClearVolumeVisualPreview();
            return;
        }

        var range = ActiveRange();
        if (range.IsEmpty)
        {
            ClearVolumeVisualPreview();
            return;
        }

        var linear = (float)WaveformGainAnalyzer.LinearFromDb(gainDb);
        Waveform.SetPreviewGain(frame =>
            frame < range.StartFrame || frame >= range.EndFrame
                ? 1f
                : linear);
    }

    private void ClearVolumeVisualPreview() => Waveform.SetPreviewGain(null);

    private void RestoreVolumeVisualIfMenuOpen()
    {
        if (_document is null || _volumeMenu is not { IsOpen: true })
        {
            return;
        }

        ApplyVolumeVisualPreview(VolumeGainPicker.ReadGain(_volumeMenu));
    }

    private void PreviewVolume(double gainDb)
    {
        if (_document is null || _volumePreview.Toggling)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (now - _volumePreview.SpaceTick < 120)
        {
            return;
        }

        _volumePreview.SpaceTick = now;
        _volumePreview.Toggling = true;
        try
        {
            if (_volumePreview.Previewing && _player.IsPlaying)
            {
                StopVolumePreview(restoreCursor: true);
                RestoreVolumeVisualIfMenuOpen();
                return;
            }

            var range = ActiveRange();
            if (range.IsEmpty)
            {
                return;
            }

            StartVolumePreviewPlayback(range, gainDb);
        }
        finally
        {
            _volumePreview.Toggling = false;
        }
    }

    private void StartVolumePreviewPlayback(WaveSelection range, double gainDb)
    {
        if (_document is null)
        {
            return;
        }

        try
        {
            if (_player.IsPlaying)
            {
                _player.Pause();
            }

            _playTimer.Stop();
            _meter.Reset();
            LoudnessMeter.Reset();
            var linear = (float)WaveformGainAnalyzer.LinearFromDb(gainDb);
            float GainAt(long frame) =>
                frame < range.StartFrame || frame >= range.EndFrame
                    ? 1f
                    : linear;
            if (_player.HasOutputDevice)
            {
                _player.Rebind(_document, range.StartFrame, range, loop: false, GainAt);
            }
            else
            {
                _player.Prepare(_document, range.StartFrame, range, loop: false, GainAt);
            }

            ApplyVolumeVisualPreview(gainDb);
            _volumePreview.Previewing = true;
            _volumePreview.StartedAt = Environment.TickCount64;
            _player.Play();
            _playbackGeneration = _player.Generation;
            _playTimer.Start();
            StartMeterRendering();
            Waveform.SetTrailRecording(true);
            Transport.SetPlaying(true);
            Waveform.UnlockCenter();
            Waveform.SetPlayheadFromPlayback(range.StartFrame);
        }
        catch (Exception ex)
        {
            _volumePreview.Previewing = false;
            ClearVolumeVisualPreview();
            PausePlaybackSoft();
            OwnerCenteredMessageBox.Show(this, ex.Message, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void StopVolumePreview(bool restoreCursor)
    {
        if (!_volumePreview.Previewing)
        {
            return;
        }

        var resume = _volumePreview.ResumeFrame;
        _volumePreview.Previewing = false;
        PausePlaybackSoft();
        if (restoreCursor && _document is not null)
        {
            SeekFrame(resume);
        }

        RestoreVolumeVisualIfMenuOpen();
    }

    private void OnFadeCurveHighlighted(bool fadeIn, FadeShape shape)
    {
        ApplyFadeCurveVisualPreview(fadeIn, shape);
        if (!_fadeReplayOnHighlight || _document is null || _fadePreview.Toggling)
        {
            return;
        }

        var range = ActiveRange();
        if (range.IsEmpty)
        {
            return;
        }

        StartFadePreviewPlayback(range, fadeIn, shape);
    }

    private void ApplyFadeCurveVisualPreview(bool fadeIn, FadeShape shape)
    {
        if (_document is null)
        {
            ClearFadeCurveVisualPreview();
            return;
        }

        var range = FadeCurves.InclusiveSampleRange(ActiveRange(), _document.FrameCount);
        if (range.IsEmpty)
        {
            ClearFadeCurveVisualPreview();
            return;
        }

        Waveform.SetPreviewGain(frame =>
            frame < range.StartFrame || frame >= range.EndFrame
                ? 1f
                : FadeCurves.GainAtFrame(shape, fadeIn, frame, range.StartFrame, range.Length));
    }

    private void ClearFadeCurveVisualPreview() => Waveform.SetPreviewGain(null);

    private void RestoreFadeVisualIfMenuOpen()
    {
        if (_document is null || _fadeMenu is not { IsOpen: true })
        {
            return;
        }

        ApplyFadeCurveVisualPreview(_fadePromptIsIn, FadeCurvePicker.HighlightedShape(_fadeMenu));
    }

    private void PreviewFade(bool fadeIn, FadeShape shape)
    {
        if (_document is null || _fadePreview.Toggling)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (now - _fadePreview.SpaceTick < 120)
        {
            return;
        }

        _fadePreview.SpaceTick = now;
        _fadePreview.Toggling = true;
        try
        {
            if (_fadePreview.Previewing && _player.IsPlaying)
            {
                StopFadePreview(restoreCursor: true);
                RestoreFadeVisualIfMenuOpen();
                return;
            }

            var range = ActiveRange();
            if (range.IsEmpty)
            {
                return;
            }

            StartFadePreviewPlayback(range, fadeIn, shape);
        }
        finally
        {
            _fadePreview.Toggling = false;
        }
    }

    private void StartFadePreviewPlayback(WaveSelection range, bool fadeIn, FadeShape shape)
    {
        if (_document is null)
        {
            return;
        }

        try
        {
            if (_player.IsPlaying)
            {
                _player.Pause();
            }

            _playTimer.Stop();
            _meter.Reset();
            LoudnessMeter.Reset();
            var previewRange = FadeCurves.InclusiveSampleRange(range, _document.FrameCount);
            var gain = (long frame) =>
                FadeCurves.GainAtFrame(shape, fadeIn, frame, previewRange.StartFrame, previewRange.Length);
            if (_player.HasOutputDevice)
            {
                _player.Rebind(_document, previewRange.StartFrame, previewRange, loop: false, gain);
            }
            else
            {
                _player.Prepare(_document, previewRange.StartFrame, previewRange, loop: false, gain);
            }

            ApplyFadeCurveVisualPreview(fadeIn, shape);
            _fadePreview.Previewing = true;
            _fadePreview.StartedAt = Environment.TickCount64;
            _player.Play();
            _playbackGeneration = _player.Generation;
            _playTimer.Start();
            StartMeterRendering();
            Waveform.SetTrailRecording(true);
            Transport.SetPlaying(true);
            Waveform.UnlockCenter();
            Waveform.SetPlayheadFromPlayback(previewRange.StartFrame);
        }
        catch (Exception ex)
        {
            _fadePreview.Previewing = false;
            ClearFadeCurveVisualPreview();
            PausePlaybackSoft();
            OwnerCenteredMessageBox.Show(this, ex.Message, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void StopFadePreview(bool restoreCursor)
    {
        if (!_fadePreview.Previewing)
        {
            return;
        }

        var resume = _fadePreview.ResumeFrame;
        _fadePreview.Previewing = false;
        PausePlaybackSoft();
        if (restoreCursor && _document is not null)
        {
            SeekFrame(resume);
        }

        RestoreFadeVisualIfMenuOpen();
    }

    private void ReleaseStuckScrub()
    {
        Overview.CancelDrag();
        ApplyOverviewViewToWaveform();
        // グローバル Capture 解除はフェード / 変換メニューを閉じ、Closed で試聴まで止める。
        Waveform.AbandonScrub();
        if (_player.IsScrubbing)
        {
            _player.EndScrub();
        }

        _resumeAfterScrub = false;
    }

    private void PausePlaybackSoft()
    {
        ReleaseStuckScrub();
        _player.Pause();

        _playTimer.Stop();
        StopMeterRendering();
        Waveform.ExitPlayheadFrame = -1;
        Waveform.SetTrailRecording(false);
        Transport.SetPlaying(false);
        ExtinguishMeter();
        Waveform.UnlockCenter();
    }

    private void ApplyFade(bool fadeIn, FadeShape shape)
    {
        if (_document is null)
        {
            return;
        }

        var range = ActiveRange();
        if (range.IsEmpty)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _fadePreview.Previewing = false;
        ClearFadeCurveVisualPreview();
        PausePlaybackSoft();
        var command = fadeIn
            ? ProcessEdits.FadeIn(_document, range, shape, channelMask: EditMask())
            : ProcessEdits.FadeOut(_document, range, shape, channelMask: EditMask());
        _history.Do(_document, command);
        AfterTransform();
    }

    private void ApplyFadeAroundPlayhead()
    {
        if (_document is null)
        {
            return;
        }

        var visible = new WaveSelection(Waveform.ViewLeftFrame, Waveform.ViewRightFrame);
        var command = ProcessEdits.FadeAroundPlayhead(_document, visible, Waveform.PlayheadFrame, channelMask: EditMask());
        if (command is null)
        {
            return;
        }

        StopPlaybackForEdit();
        _history.Do(_document, command);
        AfterEdit();
    }

    private void ApplyNormalize()
    {
        if (_document is null)
        {
            return;
        }

        var range = ActiveRange();
        if (range.IsEmpty)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        StopPlaybackForEdit();
        _history.Do(_document, ProcessEdits.Normalize(_document, range, channelMask: EditMask()));
        AfterTransform();
    }

    private void ApplyReverse()
    {
        if (_document is null)
        {
            return;
        }

        var range = ActiveRange();
        if (range.IsEmpty)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var command = ProcessEdits.Reverse(_document, range, channelMask: EditMask());
        if (command is null)
        {
            return;
        }

        StopPlaybackForEdit();
        _history.Do(_document, command);
        AfterTransform();
    }

    private void ApplyVolume(double gainDb)
    {
        if (_document is null)
        {
            return;
        }

        var range = ActiveRange();
        if (range.IsEmpty)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _volumePreview.Previewing = false;
        ClearVolumeVisualPreview();
        CloseVolumeGainPicker();
        if (WaveformGainAnalyzer.IsNoOp(WaveformGainAnalyzer.SnapGainDb(gainDb)))
        {
            return;
        }

        PausePlaybackSoft();
        var command = ProcessEdits.Gain(_document, range, gainDb, channelMask: EditMask());
        if (command is null)
        {
            return;
        }

        _history.Do(_document, command);
        AfterTransform();
    }

    private void CommitMarkerComment(long frame, string comment)
    {
        if (_document is null)
        {
            return;
        }

        var command = ProcessEdits.SetMarkerComment(_document, frame, comment);
        if (command is null)
        {
            return;
        }

        _history.Do(_document, command);
        Waveform.Refresh();
        Overview.Refresh();
        RefreshStatus();
        RefreshHistoryStrip();
    }

    private void CommitRegionName(WaveSelection region, string name)
    {
        if (_document is null)
        {
            return;
        }

        var command = ProcessEdits.SetRegionName(_document, region, name);
        if (command is null)
        {
            return;
        }

        _history.Do(_document, command);
        Waveform.Refresh();
        Overview.Refresh();
        RefreshStatus();
        RefreshHistoryStrip();
    }

    private bool TryAddMarkerAtPlayhead()
    {
        if (_document is null)
        {
            return false;
        }

        var range = _document.Selection;
        if (!range.IsEmpty)
        {
            var previous = DocumentRangeDivide.ResolvePreviousParts(_markerDivide, _document, range, regions: false);
            var next = RangeDivide.NextParts(previous);
            var command = ProcessEdits.DivideMarkers(_document, range, previous, next);
            if (command is not null)
            {
                ApplyPlaceLive(command);
            }

            _markerDivide = new RangeDivideState(_document, range.StartFrame, range.EndFrame, next);
            AfterMarkerEdit();
            return true;
        }

        var frame = Waveform.PlayheadFrame;
        if (_document.HasMarkerAt(frame))
        {
            return true;
        }

        ApplyPlaceLive(ProcessEdits.AddMarker(_document, frame));
        AfterMarkerEdit();
        return true;
    }

    private void RenameMarkerAtPosition()
    {
        if (_document is null)
        {
            return;
        }

        Waveform.TryBeginRenameMarker();
    }

    private void ApplyDeleteMarkers()
    {
        if (_document is null)
        {
            return;
        }

        if (!Waveform.HasSelectedMarkers && _document.HasMarkerAt(Waveform.PlayheadFrame))
        {
            Waveform.SelectMarkerFrames([Waveform.PlayheadFrame]);
        }

        ApplyDeleteSelectedMarkers();
    }

    private void ApplyCopy()
    {
        if (_document is null)
        {
            return;
        }

        var clip = ProcessEdits.Copy(_document, _document.Selection, channelMask: EditMask());
        if (clip is null)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _clipboard = clip;
        _historyClipboardIsLatest = false;
    }

    private void ApplyCut()
    {
        if (_document is null)
        {
            return;
        }

        var range = _document.Selection.Clamp(_document.FrameCount);
        if (range.IsEmpty)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (DeletesWholeFile(range))
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorEmptyAfterDelete, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var clip = ProcessEdits.Copy(_document, range, channelMask: EditMask());
        if (clip is null)
        {
            return;
        }

        _clipboard = clip;
        _historyClipboardIsLatest = false;
        StopPlaybackForEdit();
        _history.Do(_document, ProcessEdits.Delete(_document, range, channelMask: EditMask()));
        AfterEdit();
    }

    private void ApplyPaste()
    {
        if (_document is null)
        {
            return;
        }

        // タブ選択中の Ctrl+V は選択タブへの履歴レシピ適用。
        if (HasTabSelection)
        {
            PasteHistoryRecipesToTabs(SelectedTabsInOrder());
            return;
        }

        // 直近のコピーが履歴レシピなら、履歴ウィンドウを開かなくても
        // Ctrl+V で同じ処理を適用する（複数ファイルへの反映用）。
        if (TryPasteHistoryRecipesDirect())
        {
            return;
        }

        if (_clipboard is null || _clipboard.IsEmpty)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorClipboardEmpty, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var command = ProcessEdits.Paste(_document, _clipboard, Waveform.PlayheadFrame, channelMask: EditMask());
        if (command is null)
        {
            return;
        }

        StopPlaybackForEdit();
        _history.Do(_document, command);
        AfterEdit();
    }

    private void ApplyDelete()
    {
        if (_document is null)
        {
            return;
        }

        if (Waveform.HasSelectedMarkers || Waveform.HasSelectedRegions)
        {
            ApplyDeleteSelectedMarkers();
            ApplyDeleteSelectedRegions();
            return;
        }

        var range = _document.Selection;
        if (range.IsEmpty)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (DeletesWholeFile(range))
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorEmptyAfterDelete, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        StopPlaybackForEdit();
        _history.Do(_document, ProcessEdits.Delete(_document, range, channelMask: EditMask()));
        AfterEdit();
    }

    private void ApplyDeleteSelectedMarkers()
    {
        if (_document is null || !Waveform.HasSelectedMarkers)
        {
            return;
        }

        var command = ProcessEdits.RemoveMarkers(_document, Waveform.SelectedMarkerFrames.ToArray());
        if (command is null)
        {
            return;
        }

        _history.Do(_document, command);
        Waveform.ClearMarkerSelection();
        AfterMarkerEdit();
    }

    private void ApplyDeleteSelectedRegions()
    {
        if (_document is null || !Waveform.HasSelectedRegions)
        {
            return;
        }

        var command = ProcessEdits.RemoveRegions(_document, Waveform.SelectedRegions);
        if (command is null)
        {
            return;
        }

        _history.Do(_document, command);
        Waveform.ClearMarkerSelection();
        AfterMarkerEdit();
    }

    private void ClearMarkers(IReadOnlyList<long> frames)
    {
        if (_document is null || frames is null || frames.Count == 0)
        {
            return;
        }

        var command = ProcessEdits.RemoveMarkers(_document, frames);
        if (command is null)
        {
            return;
        }

        _history.Do(_document, command);
        Waveform.ClearMarkerSelection();
        AfterMarkerEdit();
    }

    private void BeginTimelineNudgeSession()
    {
        if (_timelineNudgeOpen || _document is null)
        {
            return;
        }

        _timelineNudgeOpen = true;
        _timelineNudgeMarkersBefore = _document.SnapshotMarkers();
        _timelineNudgeRegionsBefore = _document.SnapshotRegions();
        _timelineNudgeLoopBefore = _document.SampleLoop;
    }

    private void CommitTimelineNudgeSession()
    {
        if (!_timelineNudgeOpen)
        {
            return;
        }

        _timelineNudgeOpen = false;
        if (_document is null)
        {
            return;
        }

        CommitTimelineLayout(_timelineNudgeMarkersBefore, _timelineNudgeRegionsBefore, _timelineNudgeLoopBefore);
    }

    private void CommitTimelineLayout(
        MarkerSnapshot[] markersBefore,
        WaveRegion[] regionsBefore,
        WaveSelection loopBefore)
    {
        if (_document is null)
        {
            return;
        }

        if (_timelineNudgeOpen)
        {
            CommitTimelineNudgeSession();
        }

        var command = ProcessEdits.MoveTimelineItems(_document, markersBefore, regionsBefore, loopBefore);
        if (command is null)
        {
            return;
        }

        _history.Do(_document, command);
        AfterMarkerEdit();
    }

    private const int TimelineNudgeRepeatDelayMs = 400;
    private const int TimelineNudgeRepeatIntervalMs = 50;

    private bool BeginOrContinueMarkerNudge(int direction) =>
        BeginOrContinueTimelineNudge(direction, atPlayhead: true);

    private bool BeginOrContinueTimelineNudge(int direction, bool atPlayhead)
    {
        if (direction == 0)
        {
            return false;
        }

        if (_markerNudgeDirection == direction && _markerNudgeTimer.IsEnabled && _nudgeAtPlayhead == atPlayhead)
        {
            return true;
        }

        _nudgeAtPlayhead = atPlayhead;
        if (!ApplyNudgeStep(direction))
        {
            return true;
        }

        _markerNudgeDirection = direction;
        _nudgeRepeatStarted = false;
        _markerNudgeTimer.Stop();
        _markerNudgeTimer.Interval = TimeSpan.FromMilliseconds(TimelineNudgeRepeatDelayMs);
        _markerNudgeTimer.Start();
        return true;
    }

    private void OnMarkerNudgeTick()
    {
        if (_markerNudgeDirection == 0 || !IsNudgeHeld(_markerNudgeDirection))
        {
            StopMarkerNudge();
            return;
        }

        ApplyNudgeStep(_markerNudgeDirection);
        if (_nudgeRepeatStarted)
        {
            return;
        }

        _nudgeRepeatStarted = true;
        _markerNudgeTimer.Stop();
        _markerNudgeTimer.Interval = TimeSpan.FromMilliseconds(TimelineNudgeRepeatIntervalMs);
        _markerNudgeTimer.Start();
    }

    private bool ApplyNudgeStep(int direction)
    {
        if (_nudgeAtPlayhead)
        {
            return ApplyHeldMarkerNudge(direction);
        }

        NudgeSelectedTimeline(direction);
        return true;
    }

    private bool ApplyHeldMarkerNudge(int? direction = null)
    {
        var step = direction ?? _markerNudgeDirection;
        if (step == 0)
        {
            return false;
        }

        var includePrevious = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var fast = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        return NudgeMarkersAtPlayhead(step, includePrevious, fast);
    }

    private bool IsNudgeHeld(int direction)
    {
        var keyDown = direction < 0 ? Keyboard.IsKeyDown(Key.Left) : Keyboard.IsKeyDown(Key.Right);
        if (!keyDown)
        {
            return false;
        }

        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        return _nudgeAtPlayhead ? alt : !alt;
    }

    private void StopMarkerNudge()
    {
        _markerNudgeDirection = 0;
        _nudgeRepeatStarted = false;
        _markerNudgeTimer.Stop();
        _markerNudgeTimer.Interval = TimeSpan.FromMilliseconds(TimelineNudgeRepeatDelayMs);
        CommitTimelineNudgeSession();
    }

    private enum PlaceRepeatKind
    {
        None,
        Marker,
        Region,
    }

    private bool BeginOrContinuePlaceRepeat(PlaceRepeatKind kind)
    {
        if (kind == PlaceRepeatKind.None)
        {
            return false;
        }

        if (_placeRepeatKind == kind && _placeRepeatTimer.IsEnabled)
        {
            return true;
        }

        BeginPlaceSession();
        if (!ApplyPlace(kind, quiet: false))
        {
            StopPlaceRepeat();
            return true;
        }

        _placeRepeatKind = kind;
        _placeRepeatStarted = false;
        _placeRepeatTimer.Stop();
        _placeRepeatTimer.Interval = TimeSpan.FromMilliseconds(TimelineNudgeRepeatDelayMs);
        _placeRepeatTimer.Start();
        return true;
    }

    private void OnPlaceRepeatTick()
    {
        if (_placeRepeatKind == PlaceRepeatKind.None || !IsPlaceHeld(_placeRepeatKind))
        {
            StopPlaceRepeat();
            return;
        }

        ApplyPlace(_placeRepeatKind, quiet: true);
        if (_placeRepeatStarted)
        {
            return;
        }

        _placeRepeatStarted = true;
        _placeRepeatTimer.Stop();
        _placeRepeatTimer.Interval = TimeSpan.FromMilliseconds(TimelineNudgeRepeatIntervalMs);
        _placeRepeatTimer.Start();
    }

    private bool ApplyPlace(PlaceRepeatKind kind, bool quiet) =>
        kind switch
        {
            PlaceRepeatKind.Marker => TryAddMarkerAtPlayhead(),
            PlaceRepeatKind.Region => TrySetRegionFromSelection(quiet),
            _ => false,
        };

    private bool IsPlaceHeld(PlaceRepeatKind kind) =>
        kind switch
        {
            PlaceRepeatKind.Marker =>
                (Keyboard.IsKeyDown(Key.M) || Keyboard.IsKeyDown(Key.Insert))
                && Keyboard.Modifiers == ModifierKeys.None,
            PlaceRepeatKind.Region =>
                Keyboard.IsKeyDown(Key.R) && Keyboard.Modifiers == ModifierKeys.Shift,
            _ => false,
        };

    private void StopPlaceRepeat()
    {
        CommitPlaceSession();
        _placeRepeatKind = PlaceRepeatKind.None;
        _placeRepeatStarted = false;
        _placeRepeatTimer.Stop();
        _placeRepeatTimer.Interval = TimeSpan.FromMilliseconds(TimelineNudgeRepeatDelayMs);
    }

    private bool BeginOrContinueSpectrogramBoostNudge(int direction)
    {
        if (!Waveform.SpectrogramVisible || direction == 0)
        {
            return false;
        }

        if (_boostNudgeDirection == direction && _boostNudgeTimer.IsEnabled)
        {
            return true;
        }

        if (!Waveform.NudgeSpectrogramBoost(direction))
        {
            return false;
        }

        _boostNudgeDirection = direction;
        _boostRepeatStarted = false;
        _boostNudgeTimer.Stop();
        _boostNudgeTimer.Interval = TimeSpan.FromMilliseconds(TimelineNudgeRepeatDelayMs);
        _boostNudgeTimer.Start();
        return true;
    }

    private void OnSpectrogramBoostNudgeTick()
    {
        if (_boostNudgeDirection == 0 || !IsSpectrogramBoostHeld(_boostNudgeDirection))
        {
            StopSpectrogramBoostNudge();
            return;
        }

        Waveform.NudgeSpectrogramBoost(_boostNudgeDirection);
        if (_boostRepeatStarted)
        {
            return;
        }

        _boostRepeatStarted = true;
        _boostNudgeTimer.Stop();
        _boostNudgeTimer.Interval = TimeSpan.FromMilliseconds(TimelineNudgeRepeatIntervalMs);
        _boostNudgeTimer.Start();
    }

    private bool IsSpectrogramBoostHeld(int direction)
    {
        if (IsUiBusy || !Waveform.SpectrogramVisible)
        {
            return false;
        }

        var keyDown = direction > 0 ? Keyboard.IsKeyDown(Key.Up) : Keyboard.IsKeyDown(Key.Down);
        return keyDown && Keyboard.Modifiers == ModifierKeys.Alt;
    }

    private void StopSpectrogramBoostNudge()
    {
        _boostNudgeDirection = 0;
        _boostRepeatStarted = false;
        _boostNudgeTimer.Stop();
        _boostNudgeTimer.Interval = TimeSpan.FromMilliseconds(TimelineNudgeRepeatDelayMs);
    }

    private void BeginPlaceSession()
    {
        if (_placeSessionOpen || _document is null)
        {
            return;
        }

        _placeSessionOpen = true;
        _placeMarkersBefore = _document.SnapshotMarkers();
        _placeRegionsBefore = _document.SnapshotRegions();
    }

    private void ApplyPlaceLive(IEditCommand command)
    {
        if (_document is null)
        {
            return;
        }

        if (_placeSessionOpen)
        {
            command.Apply(_document);
            return;
        }

        _history.Do(_document, command);
    }

    private void CommitPlaceSession()
    {
        if (!_placeSessionOpen)
        {
            return;
        }

        _placeSessionOpen = false;
        if (_document is null)
        {
            return;
        }

        var command = _placeRepeatKind switch
        {
            PlaceRepeatKind.Marker => ProcessEdits.ApplyMarkers(
                _document,
                _placeMarkersBefore,
                _document.SnapshotMarkers()),
            PlaceRepeatKind.Region => ProcessEdits.ApplyRegions(
                _document,
                _placeRegionsBefore,
                _document.SnapshotRegions(),
                _document.Selection),
            _ => null,
        };
        if (command is null)
        {
            return;
        }

        _history.Do(_document, command);
        AfterMarkerEdit();
    }

    private bool IsContinuingPlaceKey(Key key, ModifierKeys modifiers) =>
        _placeRepeatKind switch
        {
            PlaceRepeatKind.Marker => key is Key.M or Key.Insert && modifiers == ModifierKeys.None,
            PlaceRepeatKind.Region => key == Key.R && modifiers == ModifierKeys.Shift,
            _ => false,
        };

    private bool NudgeMarkersAtPlayhead(int direction, bool includePrevious, bool fast)
    {
        if (_document is null || direction == 0)
        {
            return false;
        }

        if (!Waveform.TrySelectTimelineAtPlayhead(includePrevious))
        {
            return false;
        }

        var delta = Waveform.NudgeStepFrames * (fast ? 3 : 1) * direction;
        BeginTimelineNudgeSession();
        if (!Waveform.TryNudgeSelectedTimeline(delta, out _))
        {
            return false;
        }

        Waveform.SeekKeepingSelection(Waveform.PlayheadFrame);
        AfterMarkerEdit();
        return true;
    }

    private bool NudgePlayheadOrSelection(int direction)
    {
        if (direction == 0)
        {
            return false;
        }

        if (Waveform.HasSelectedTimelineItems)
        {
            return BeginOrContinueTimelineNudge(direction, atPlayhead: false);
        }

        Waveform.NudgePlayhead(direction);
        return true;
    }

    private void NudgeSelectedTimeline(int direction)
    {
        if (_document is null || direction == 0)
        {
            return;
        }

        var fast = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        BeginTimelineNudgeSession();
        if (!Waveform.TryNudgeSelectedTimeline(Waveform.NudgeStepFrames * (fast ? 3 : 1) * direction, out _))
        {
            return;
        }

        AfterMarkerEdit();
    }

    private void AfterMarkerEdit()
    {
        if (_document is null)
        {
            return;
        }

        Waveform.PruneMarkerSelection();
        Waveform.Refresh();
        Overview.SetSelectedMarkerFrames(Waveform.SelectedMarkerFrames);
        Overview.Refresh();
        SyncViewChrome();
        RefreshStatus();
        RefreshHistoryStrip();
    }

    private void UndoEdit()
    {
        CommitTimelineNudgeSession();
        StopPlaceRepeat();
        if (_document is null || !_history.CanUndo)
        {
            return;
        }

        StopPlaybackForEdit();
        _history.Undo(_document);
        AfterEdit();
    }

    private void RedoEdit()
    {
        CommitTimelineNudgeSession();
        StopPlaceRepeat();
        if (_document is null || !_history.CanRedo)
        {
            return;
        }

        StopPlaybackForEdit();
        _history.Redo(_document);
        AfterEdit();
    }

    private void StopPlaybackForEdit()
    {
        PausePlaybackSoft();
    }

    private void AfterTransform()
    {
        Waveform.ClearSelection();
        AfterEdit();
    }

    private void AfterEdit()
    {
        _markerDivide = null;
        _regionDivide = null;
        if (_document is null)
        {
            return;
        }

        Waveform.PlayheadFrame = _document.CursorFrame;
        Waveform.PruneMarkerSelection();
        Waveform.InvalidateSpectrogramCache();
        Waveform.Refresh();
        Overview.SetSelectedMarkerFrames(Waveform.SelectedMarkerFrames);
        Overview.Refresh();
        SyncViewChrome();
        SyncMonitorLayout();
        ApplyChannelSolo();
        RefreshStatus();
        RefreshHistoryStrip();
    }
}
