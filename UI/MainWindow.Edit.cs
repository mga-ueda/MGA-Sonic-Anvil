using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using MgaSonicAnvil.Audio;
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

        if (_fadeMenu is { IsOpen: true })
        {
            _fadeMenu.IsOpen = false;
        }

        if (_player.IsPlaying)
        {
            PausePlaybackSoft();
        }

        _fadePromptIsIn = fadeIn;
        _fadePreviewResumeFrame = _document.CursorFrame;
        var target = placementTarget ?? Waveform;
        var placement = placementTarget is null ? PlacementMode.MousePoint : PlacementMode.Top;
        var menu = FadeCurvePicker.Show(
            target,
            placement,
            fadeIn,
            shape => ApplyFade(fadeIn, shape),
            shape => PreviewFade(fadeIn, shape),
            shape => ApplyFadeCurveVisualPreview(fadeIn, shape));
        _fadeMenu = menu;
        menu.Closed += (_, _) =>
        {
            StopFadePreview(restoreCursor: true);
            ClearFadeCurveVisualPreview();
            if (ReferenceEquals(_fadeMenu, menu))
            {
                _fadeMenu = null;
            }
        };
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

    private void PreviewFade(bool fadeIn, FadeShape shape)
    {
        if (_document is null || _fadePreviewToggling)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (now - _fadeSpaceTick < 120)
        {
            return;
        }

        _fadeSpaceTick = now;
        _fadePreviewToggling = true;
        try
        {
            if (_fadePreviewing && _player.IsPlaying)
            {
                StopFadePreview(restoreCursor: true);
                ApplyFadeCurveVisualPreview(fadeIn, shape);
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
            _fadePreviewToggling = false;
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
            PausePlaybackSoft();
            _meter.Reset();
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

            _player.Play();
            _fadePreviewing = true;
            _playbackGeneration = _player.Generation;
            _playTimer.Start();
            StartMeterRendering();
            Waveform.SetTrailRecording(true);
            Transport.SetPlaying(true);
            Waveform.PlayheadFrame = previewRange.StartFrame;
            ApplyFadeCurveVisualPreview(fadeIn, shape);
        }
        catch (Exception ex)
        {
            _fadePreviewing = false;
            ClearFadeCurveVisualPreview();
            PausePlaybackSoft();
            OwnerCenteredMessageBox.Show(this, ex.Message, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void StopFadePreview(bool restoreCursor)
    {
        if (!_fadePreviewing)
        {
            return;
        }

        var resume = _fadePreviewResumeFrame;
        _fadePreviewing = false;
        PausePlaybackSoft();
        if (restoreCursor && _document is not null)
        {
            SeekFrame(resume);
        }
    }

    private void PausePlaybackSoft()
    {
        if (_player.IsPlaying)
        {
            _player.Pause();
        }

        _playTimer.Stop();
        StopMeterRendering();
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

        _fadePreviewing = false;
        ClearFadeCurveVisualPreview();
        PausePlaybackSoft();
        var command = fadeIn
            ? ProcessEdits.FadeIn(_document, range, shape)
            : ProcessEdits.FadeOut(_document, range, shape);
        _history.Do(_document, command);
        AfterEdit();
    }

    private void ApplyFadeAroundPlayhead()
    {
        if (_document is null)
        {
            return;
        }

        var visible = new WaveSelection(Waveform.ViewLeftFrame, Waveform.ViewRightFrame);
        var command = ProcessEdits.FadeAroundPlayhead(_document, visible, Waveform.PlayheadFrame);
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
        _history.Do(_document, ProcessEdits.Normalize(_document, range));
        AfterEdit();
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
    }

    private void AddMarkerAtPlayhead()
    {
        if (_document is null)
        {
            return;
        }

        var frame = Waveform.PlayheadFrame;
        if (_document.HasMarkerAt(frame))
        {
            return;
        }

        _history.Do(_document, ProcessEdits.AddMarker(_document, frame));
        Waveform.SelectMarkerFrames([frame]);
        AfterMarkerEdit();
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

    private void ApplyDelete()
    {
        if (_document is null)
        {
            return;
        }

        if (Waveform.HasSelectedMarkers)
        {
            ApplyDeleteSelectedMarkers();
            return;
        }

        var range = _document.Selection;
        if (range.IsEmpty)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (range.StartFrame <= 0 && range.EndFrame >= _document.FrameCount)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorEmptyAfterDelete, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        StopPlaybackForEdit();
        _history.Do(_document, ProcessEdits.Delete(_document, range));
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

    private void CommitMarkerLayout(MarkerSnapshot[] before, MarkerSnapshot[] after)
    {
        if (_document is null)
        {
            return;
        }

        var command = ProcessEdits.ReplaceMarkers(before, after, before.Length == after.Length ? "Move Markers" : "Delete Markers");
        if (command is null)
        {
            return;
        }

        _history.Do(_document, command);
        AfterMarkerEdit();
    }

    private bool BeginOrContinueMarkerNudge(int direction)
    {
        if (direction == 0)
        {
            return false;
        }

        if (_markerNudgeDirection == direction && _markerNudgeTimer.IsEnabled)
        {
            return true;
        }

        if (!ApplyHeldMarkerNudge(direction))
        {
            return true;
        }

        _markerNudgeDirection = direction;
        _markerNudgeTimer.Start();
        return true;
    }

    private void OnMarkerNudgeTick()
    {
        if (_markerNudgeDirection == 0 || !IsMarkerNudgeHeld(_markerNudgeDirection))
        {
            StopMarkerNudge();
            return;
        }

        ApplyHeldMarkerNudge();
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

    private static bool IsMarkerNudgeHeld(int direction)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Alt) == 0)
        {
            return false;
        }

        return direction < 0 ? Keyboard.IsKeyDown(Key.Left) : Keyboard.IsKeyDown(Key.Right);
    }

    private void StopMarkerNudge()
    {
        _markerNudgeDirection = 0;
        _markerNudgeTimer.Stop();
    }

    private bool NudgeMarkersAtPlayhead(int direction, bool includePrevious, bool fast)
    {
        if (_document is null || direction == 0)
        {
            return false;
        }

        var origins = Waveform.MarkerNudgeFrames(includePrevious);
        if (origins.Count == 0)
        {
            return false;
        }

        var delta = Waveform.NudgeStepFrames * (fast ? 3 : 1) * direction;
        var command = ProcessEdits.MoveMarkers(_document, origins, delta, out var applied);
        if (command is null)
        {
            return false;
        }

        var playhead = Waveform.PlayheadFrame;
        _history.Do(_document, command);
        Waveform.SelectMarkerFrames(origins.Select(frame => frame + applied));
        Waveform.SeekKeepingSelection(playhead + applied);
        AfterMarkerEdit();
        return true;
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
    }

    private void UndoEdit()
    {
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

    private void AfterEdit()
    {
        if (_document is null)
        {
            return;
        }

        Waveform.PlayheadFrame = _document.CursorFrame;
        Waveform.PruneMarkerSelection();
        Waveform.Refresh();
        Overview.SetSelectedMarkerFrames(Waveform.SelectedMarkerFrames);
        Overview.Refresh();
        SyncViewChrome();
        RefreshStatus();
    }
}
