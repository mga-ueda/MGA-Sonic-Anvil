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

    private void OnFadeCurveHighlighted(bool fadeIn, FadeShape shape)
    {
        ApplyFadeCurveVisualPreview(fadeIn, shape);
        if (!_fadeReplayOnHighlight || _document is null || _fadePreviewToggling)
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
            _fadePreviewing = true;
            _fadePreviewStartedAt = Environment.TickCount64;
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

        RestoreFadeVisualIfMenuOpen();
    }

    private void ReleaseStuckScrub()
    {
        Overview.CancelDrag();
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
        Waveform.ClearSelection();
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

    private void ApplyCopy()
    {
        if (_document is null)
        {
            return;
        }

        var clip = ProcessEdits.Copy(_document, _document.Selection);
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

        if (range.StartFrame <= 0 && range.EndFrame >= _document.FrameCount)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorEmptyAfterDelete, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var clip = ProcessEdits.Copy(_document, range);
        if (clip is null)
        {
            return;
        }

        _clipboard = clip;
        _historyClipboardIsLatest = false;
        StopPlaybackForEdit();
        _history.Do(_document, ProcessEdits.Delete(_document, range));
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

        var command = ProcessEdits.Paste(_document, _clipboard, Waveform.PlayheadFrame);
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
    }

    private void UndoEdit()
    {
        CommitTimelineNudgeSession();
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
        Waveform.InvalidateSpectrogramCache();
        Waveform.Refresh();
        Overview.SetSelectedMarkerFrames(Waveform.SelectedMarkerFrames);
        Overview.Refresh();
        SyncViewChrome();
        RefreshStatus();
    }
}
