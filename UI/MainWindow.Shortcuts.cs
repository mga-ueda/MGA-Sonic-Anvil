using System.Windows.Input;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        if (TryProcessShortcut(key, modifiers))
        {
            e.Handled = true;
        }
    }

    private void MainWindow_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Left or Key.Right)
        {
            if (_markerNudgeDirection != 0
                && (key == Key.Left && _markerNudgeDirection < 0
                    || key == Key.Right && _markerNudgeDirection > 0
                    || (Keyboard.Modifiers & ModifierKeys.Alt) == 0))
            {
                StopMarkerNudge();
            }

            CommitTimelineNudgeSession();
            return;
        }

        if (_markerNudgeDirection != 0 && key is Key.LeftAlt or Key.RightAlt)
        {
            StopMarkerNudge();
        }

        if (_placeRepeatKind != PlaceRepeatKind.None && !IsPlaceHeld(_placeRepeatKind))
        {
            StopPlaceRepeat();
        }
    }

    private void MainWindow_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (IsUiBusy)
        {
            e.Handled = true;
            return;
        }

        if (_volumeMenu is { IsOpen: true })
        {
            VolumeGainPicker.TryNudge(_volumeMenu, Math.Sign(e.Delta));
            e.Handled = true;
            return;
        }

        if (e.OriginalSource is not System.Windows.DependencyObject origin)
        {
            return;
        }

        if (IsDescendantOf(origin, Overview))
        {
            if (Keyboard.Modifiers == ModifierKeys.None)
            {
                Waveform.WheelTimeZoomAtPlayhead(e.Delta);
                e.Handled = true;
            }

            return;
        }

        if (!IsDescendantOf(origin, Waveform))
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.Shift)
        {
            Waveform.PanByVisibleFraction(e.Delta > 0 ? -0.1 : 0.1);
            e.Handled = true;
            return;
        }

        if (modifiers == ModifierKeys.Control)
        {
            Waveform.WheelAmpZoom(e.Delta);
            e.Handled = true;
            return;
        }

        if (modifiers == ModifierKeys.None)
        {
            Waveform.WheelTimeZoomAtPlayhead(e.Delta);
            e.Handled = true;
        }
    }

    private static bool IsDescendantOf(
        System.Windows.DependencyObject? origin,
        System.Windows.DependencyObject target)
    {
        while (origin is not null)
        {
            if (ReferenceEquals(origin, target))
            {
                return true;
            }

            origin = System.Windows.Media.VisualTreeHelper.GetParent(origin);
        }

        return false;
    }

    private bool TryProcessShortcut(Key key, ModifierKeys modifiers)
    {
        if (IsUiBusy)
        {
            return true;
        }

        if (StatusTimes.IsTimeFocused)
        {
            return false;
        }

        if (key is not (Key.Left or Key.Right))
        {
            CommitTimelineNudgeSession();
        }

        if (_placeRepeatKind != PlaceRepeatKind.None && !IsContinuingPlaceKey(key, modifiers))
        {
            StopPlaceRepeat();
        }

        if (TryProcessHistoryShortcut(key, modifiers))
        {
            return true;
        }

        if (key == Key.Escape)
        {
            StopMarkerNudge();
            StopPlaceRepeat();
            if (StatusTimes.IsEditing)
            {
                StatusTimes.CancelEdit();
                Waveform.Focus();
                return true;
            }

            if (StatusTimes.IsTimeFocused)
            {
                Waveform.Focus();
                return true;
            }

            if (Waveform.CancelMarkerCommentEdit())
            {
                return true;
            }

            if (CloseFadeCurvePicker() || CloseFormatConvertPicker() || CloseVolumeGainPicker())
            {
                return true;
            }

            if (Waveform.IsScrubbing)
            {
                Overview.CancelDrag();
                Waveform.CancelScrub();
                return true;
            }

            if (Waveform.CancelMarkerInteraction())
            {
                return true;
            }

            if (ClearTabSelection())
            {
                return true;
            }

            if (Waveform.ClearSelection())
            {
                return true;
            }

            ConfirmAndExit();
            return true;
        }

        if (Waveform.IsEditingMarkerComment || StatusTimes.IsTimeFocused)
        {
            return false;
        }

        if (TryHandleFormatMenuShortcut(key, modifiers))
        {
            return true;
        }

        if (_fadeMenu is { IsOpen: true })
        {
            if (key == Key.I && modifiers == ModifierKeys.None)
            {
                PromptFade(fadeIn: true);
                return true;
            }

            if (key == Key.O && modifiers == ModifierKeys.None)
            {
                PromptFade(fadeIn: false);
                return true;
            }

            if (key == Key.Space && modifiers == ModifierKeys.None)
            {
                PreviewFade(_fadePromptIsIn, FadeCurvePicker.HighlightedShape(_fadeMenu));
                return true;
            }

            if (key == Key.Enter && modifiers == ModifierKeys.None)
            {
                ApplyFade(_fadePromptIsIn, FadeCurvePicker.HighlightedShape(_fadeMenu));
                CloseFadeCurvePicker();
                return true;
            }

            if (modifiers == ModifierKeys.None
                && TryDigitPercent(key, out var fadeDigit)
                && fadeDigit > 0)
            {
                var index = (int)Math.Round(fadeDigit * 10d) - 1;
                if (FadeCurvePicker.HighlightByIndex(_fadeMenu, index))
                {
                    return true;
                }
            }

            return false;
        }

        if (TryHandleVolumeMenuShortcut(key, modifiers))
        {
            return true;
        }

        if (key == Key.W && modifiers == ModifierKeys.Control)
        {
            CloseDocument();
            return true;
        }

        if (key == Key.W && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            CloseAllTabs();
            return true;
        }

        if (key == Key.T && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ReopenLastClosedTab();
            return true;
        }

        if (key == Key.Tab && modifiers == ModifierKeys.Control)
        {
            ActivateAdjacentTab(1);
            return true;
        }

        if (key == Key.Tab && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ActivateAdjacentTab(-1);
            return true;
        }

        if (key == Key.PageDown && modifiers == ModifierKeys.Control)
        {
            ActivateAdjacentTab(1);
            return true;
        }

        if (key == Key.PageUp && modifiers == ModifierKeys.Control)
        {
            ActivateAdjacentTab(-1);
            return true;
        }

        if (key == Key.O && modifiers == ModifierKeys.Control)
        {
            OpenFromDialog();
            return true;
        }

        if (key == Key.O && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            OpenSettings();
            return true;
        }

        if (key == Key.E && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (WaapiBar.ExportEnabled)
            {
                _ = ExportToWwiseAsync();
            }

            return true;
        }

        if (key == Key.S && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            Save(saveAs: true);
            return true;
        }

        if (key == Key.M && modifiers == (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt))
        {
            ExportAllTabsMp3();
            return true;
        }

        if (key == Key.M && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            SaveAsMp3();
            return true;
        }

        if (key == Key.S && modifiers == ModifierKeys.Control)
        {
            Save(saveAs: false);
            return true;
        }

        if (key == Key.Z && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            RedoEdit();
            return true;
        }

        if (key == Key.Y && modifiers == ModifierKeys.Control)
        {
            RedoEdit();
            return true;
        }

        if (key == Key.Z && modifiers == ModifierKeys.Control)
        {
            UndoEdit();
            return true;
        }

        if (key == Key.A && modifiers == ModifierKeys.Control)
        {
            // タブバー上にポインタがあるときはタブの全選択。
            if (DocumentTabHost.Visibility == System.Windows.Visibility.Visible && DocumentTabHost.IsMouseOver)
            {
                SelectAllTabs();
                return true;
            }

            Waveform.SelectAll();
            return true;
        }

#if DEBUG
        if (key == Key.C && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ShowColorDevPanel();
            return true;
        }
#endif

        if (key == Key.C && modifiers == ModifierKeys.Control)
        {
            ApplyCopy();
            return true;
        }

        if (key == Key.X && modifiers == ModifierKeys.Control)
        {
            ApplyCut();
            return true;
        }

        if (key == Key.V && modifiers == ModifierKeys.Control)
        {
            ApplyPaste();
            return true;
        }

        if (key == Key.Space && modifiers == ModifierKeys.Control)
        {
            StartPrerollPlayback();
            return true;
        }

        if (key == Key.Enter && modifiers == ModifierKeys.Alt)
        {
            RestartFromLastPlaybackStart();
            return true;
        }

        if (key == Key.Enter && modifiers == ModifierKeys.None)
        {
            return PausePlaybackHere();
        }

        if (key == Key.Space && modifiers == ModifierKeys.None)
        {
            TogglePlayback();
            return true;
        }

        if ((key is Key.Z or Key.OemPeriod or Key.Decimal) && modifiers == ModifierKeys.None)
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
            }
            else
            {
                Waveform.CenterViewOnPlayhead();
            }

            return true;
        }

        if (key == Key.L && modifiers == ModifierKeys.Shift)
        {
            SetSampleLoopFromSelection();
            return true;
        }

        if (key == Key.R && modifiers == ModifierKeys.Shift)
        {
            return BeginOrContinuePlaceRepeat(PlaceRepeatKind.Region);
        }

        if (key == Key.L && modifiers == ModifierKeys.None)
        {
            JumpToLoopPrerollAndPlay();
            return true;
        }

        if (key == Key.A && modifiers == ModifierKeys.None)
        {
            Waveform.ToggleAnalysisView();
            return true;
        }

        if (key is Key.M or Key.Insert && modifiers == ModifierKeys.None)
        {
            return BeginOrContinuePlaceRepeat(PlaceRepeatKind.Marker);
        }

        if (key == Key.E && modifiers == ModifierKeys.None)
        {
            TogglePlayPostExit();
            return true;
        }

        if (key == Key.W && modifiers == ModifierKeys.None)
        {
            ToggleWaapiPanel();
            return true;
        }

        if (key == Key.R && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            RenameMarkerAtPosition();
            return true;
        }

        if (key == Key.U && modifiers == ModifierKeys.None)
        {
            OpenEditHistory();
            return true;
        }

        if (key == Key.G && modifiers == ModifierKeys.None)
        {
            JumpToLoopPrerollAndPlay();
            return true;
        }

        if (key == Key.T && modifiers == ModifierKeys.None)
        {
            return StatusTimes.FocusCurrentTime();
        }

        if (key == Key.S && modifiers == ModifierKeys.None)
        {
            PromptFormatConvert(FormatConvertKind.SampleRate);
            return true;
        }

        if (key == Key.B && modifiers == ModifierKeys.None)
        {
            PromptFormatConvert(FormatConvertKind.BitDepth);
            return true;
        }

        if (key == Key.C && modifiers == ModifierKeys.None)
        {
            PromptFormatConvert(FormatConvertKind.Channels);
            return true;
        }

        if (key == Key.I && modifiers == ModifierKeys.None)
        {
            PromptFade(fadeIn: true);
            return true;
        }

        if (key == Key.O && modifiers == ModifierKeys.None)
        {
            PromptFade(fadeIn: false);
            return true;
        }

        if (key == Key.X && modifiers == ModifierKeys.None)
        {
            ApplyFadeAroundPlayhead();
            return true;
        }

        if (key == Key.N && modifiers == ModifierKeys.None)
        {
            ApplyNormalize();
            return true;
        }

        if (key == Key.V && modifiers == ModifierKeys.None)
        {
            PromptVolume();
            return true;
        }

        if (key == Key.Delete && modifiers == ModifierKeys.Control)
        {
            ApplyDeleteMarkers();
            return true;
        }

        if (key is Key.Delete or Key.Back && modifiers == ModifierKeys.None)
        {
            ApplyDelete();
            return true;
        }

        if (key == Key.Up && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            Waveform.ZoomAmpToMax();
            return true;
        }

        if (key == Key.Down && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            Waveform.ResetAmpZoom();
            return true;
        }

        if (key == Key.Up && modifiers == ModifierKeys.Control)
        {
            Waveform.ZoomTimeToMax();
            Waveform.CenterViewOnPlayhead();
            return true;
        }

        if (key == Key.Down && modifiers == ModifierKeys.Control)
        {
            Waveform.ResetTimeZoom();
            Waveform.CenterViewOnPlayhead();
            return true;
        }

        if (key == Key.Home && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            Waveform.SelectToDocumentEdge(-1);
            return true;
        }

        if (key == Key.End && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            Waveform.SelectToDocumentEdge(1);
            return true;
        }

        if (key == Key.Home && modifiers == ModifierKeys.Control)
        {
            ExecuteTransport(TransportCommand.GoToStart);
            return true;
        }

        if (key == Key.End && modifiers == ModifierKeys.Control)
        {
            ExecuteTransport(TransportCommand.GoToEnd);
            return true;
        }

        if (key == Key.Home && modifiers == ModifierKeys.Shift)
        {
            Waveform.ExtendSelectionToViewEdge(-1);
            return true;
        }

        if (key == Key.End && modifiers == ModifierKeys.Shift)
        {
            Waveform.ExtendSelectionToViewEdge(1);
            return true;
        }

        if (key == Key.Home && modifiers == ModifierKeys.None)
        {
            Waveform.SeekToViewEdge(-1);
            return true;
        }

        if (key == Key.End && modifiers == ModifierKeys.None)
        {
            Waveform.SeekToViewEdge(1);
            return true;
        }

        if (key == Key.PageUp && modifiers == ModifierKeys.Shift)
        {
            Waveform.ExtendSelectionByVisibleFraction(-0.05);
            return true;
        }

        if (key == Key.PageDown && modifiers == ModifierKeys.Shift)
        {
            Waveform.ExtendSelectionByVisibleFraction(0.05);
            return true;
        }

        if (key == Key.PageUp && modifiers == ModifierKeys.None)
        {
            Waveform.SeekByVisibleFraction(-0.05);
            return true;
        }

        if (key == Key.PageDown && modifiers == ModifierKeys.None)
        {
            Waveform.SeekByVisibleFraction(0.05);
            return true;
        }

        if (_markerNudgeDirection != 0
            && key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl)
        {
            if (_nudgeAtPlayhead)
            {
                ApplyHeldMarkerNudge();
                return true;
            }

            if (key is Key.LeftShift or Key.RightShift)
            {
                ApplyNudgeStep(_markerNudgeDirection);
                return true;
            }
        }

        if (key is Key.Left or Key.Right && (modifiers & ModifierKeys.Alt) != 0)
        {
            var extra = modifiers & ~ModifierKeys.Alt & ~ModifierKeys.Shift & ~ModifierKeys.Control;
            if (extra == ModifierKeys.None)
            {
                return BeginOrContinueMarkerNudge(key == Key.Left ? -1 : 1);
            }
        }

        if (key == Key.Left && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            Waveform.ExtendSelectionToMarker(-1);
            return true;
        }

        if (key == Key.Right && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            Waveform.ExtendSelectionToMarker(1);
            return true;
        }

        if (key == Key.Left && modifiers == ModifierKeys.Control)
        {
            Waveform.SeekToMarker(-1);
            return true;
        }

        if (key == Key.Right && modifiers == ModifierKeys.Control)
        {
            Waveform.SeekToMarker(1);
            return true;
        }

        if (key == Key.Left && modifiers == ModifierKeys.Shift)
        {
            if (Waveform.HasSelectedTimelineItems)
            {
                return NudgePlayheadOrSelection(-1);
            }

            Waveform.NudgeSelection(-1);
            return true;
        }

        if (key == Key.Right && modifiers == ModifierKeys.Shift)
        {
            if (Waveform.HasSelectedTimelineItems)
            {
                return NudgePlayheadOrSelection(1);
            }

            Waveform.NudgeSelection(1);
            return true;
        }

        if (key == Key.Left && modifiers == ModifierKeys.None)
        {
            return NudgePlayheadOrSelection(-1);
        }

        if (key == Key.Right && modifiers == ModifierKeys.None)
        {
            return NudgePlayheadOrSelection(1);
        }

        if (key == Key.Up && modifiers == ModifierKeys.Shift)
        {
            Waveform.ZoomAmpIn();
            return true;
        }

        if (key == Key.Down && modifiers == ModifierKeys.Shift)
        {
            Waveform.ZoomAmpOut();
            return true;
        }

        if (key == Key.Up && modifiers == ModifierKeys.None)
        {
            Waveform.ZoomTimeIn();
            Waveform.CenterViewOnPlayhead();
            return true;
        }

        if (key == Key.Down && modifiers == ModifierKeys.None)
        {
            Waveform.ZoomTimeOut();
            Waveform.CenterViewOnPlayhead();
            return true;
        }

        if (TryJumpToMarkerByNumpad(key, modifiers))
        {
            return true;
        }

        if (TryDigitPercent(key, out var percent)
            && (modifiers == ModifierKeys.None || modifiers == ModifierKeys.Shift))
        {
            JumpByVisiblePercent(percent, extendSelection: modifiers == ModifierKeys.Shift);
            return true;
        }

        return false;
    }

    private bool TryHandleVolumeMenuShortcut(Key key, ModifierKeys modifiers)
    {
        if (_volumeMenu is not { IsOpen: true })
        {
            return false;
        }

        if (key is Key.Up or Key.Down)
        {
            VolumeGainPicker.TryNudge(_volumeMenu, key == Key.Up ? 1 : -1);
            return true;
        }

        if (key == Key.Space && modifiers == ModifierKeys.None)
        {
            PreviewVolume(VolumeGainPicker.ReadGain(_volumeMenu));
            return true;
        }

        if (key == Key.Enter && modifiers == ModifierKeys.None)
        {
            ApplyVolume(VolumeGainPicker.ReadGain(_volumeMenu));
            return true;
        }

        if (key == Key.V && modifiers == ModifierKeys.None)
        {
            return true;
        }

        return false;
    }

    private bool TryJumpToMarkerByNumpad(Key key, ModifierKeys modifiers)
    {
        if (_document is null || _document.Markers.Count == 0)
        {
            return false;
        }

        if (!TryNumpadDigit(key, out var digit)
            || (modifiers != ModifierKeys.None && modifiers != ModifierKeys.Shift))
        {
            return false;
        }

        var extend = modifiers == ModifierKeys.Shift;
        var maxId = _document.Markers.Count;
        var next = _markerNumber * 10 + digit;
        if (next > 0 && next <= maxId)
        {
            _markerNumber = next;
            Waveform.SeekToMarkerId(next, extendSelection: extend);
            RestartMarkerDigitTimer();
            return true;
        }

        if (digit > 0 && digit <= maxId)
        {
            _markerNumber = digit;
            Waveform.SeekToMarkerId(digit, extendSelection: extend);
            RestartMarkerDigitTimer();
            return true;
        }

        ResetMarkerDigitEntry();
        JumpByVisiblePercent(digit / 10d, extend);
        return true;
    }

    private void JumpByVisiblePercent(double percent, bool extendSelection)
    {
        if (extendSelection)
        {
            Waveform.ExtendSelectionToVisiblePercent(percent);
            return;
        }

        Waveform.JumpVisiblePercent(percent);
    }

    private void RestartMarkerDigitTimer()
    {
        _markerDigitTimer.Stop();
        _markerDigitTimer.Start();
    }

    private void ResetMarkerDigitEntry()
    {
        _markerDigitTimer.Stop();
        _markerNumber = 0;
    }

    private static bool TryNumpadDigit(Key key, out int digit)
    {
        digit = key switch
        {
            Key.NumPad0 => 0,
            Key.NumPad1 => 1,
            Key.NumPad2 => 2,
            Key.NumPad3 => 3,
            Key.NumPad4 => 4,
            Key.NumPad5 => 5,
            Key.NumPad6 => 6,
            Key.NumPad7 => 7,
            Key.NumPad8 => 8,
            Key.NumPad9 => 9,
            _ => -1,
        };
        return digit >= 0;
    }

    private static bool TryDigitPercent(Key key, out double percent)
    {
        percent = 0;
        var digit = key switch
        {
            Key.D0 or Key.NumPad0 => 0,
            Key.D1 or Key.NumPad1 => 1,
            Key.D2 or Key.NumPad2 => 2,
            Key.D3 or Key.NumPad3 => 3,
            Key.D4 or Key.NumPad4 => 4,
            Key.D5 or Key.NumPad5 => 5,
            Key.D6 or Key.NumPad6 => 6,
            Key.D7 or Key.NumPad7 => 7,
            Key.D8 or Key.NumPad8 => 8,
            Key.D9 or Key.NumPad9 => 9,
            _ => -1,
        };
        if (digit < 0)
        {
            return false;
        }

        percent = digit / 10d;
        return true;
    }
}
