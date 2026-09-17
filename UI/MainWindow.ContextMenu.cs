using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
    private WaveformContextHit _waveMenuHit = new();
    private bool _pendingContextMenuKey;

    private void OpenWaveformContextMenu(WaveformContextHit hit)
    {
        if (IsUiBusy)
        {
            return;
        }

        _waveMenuHit = hit;
        if (_waveMenu is { IsOpen: true })
        {
            _waveMenu.IsOpen = false;
        }

        var menu = WaveformContextMenuBuilder.Create(
            WaveformContextMenuBuilder.Build(CaptureWaveMenuModel(hit)),
            ExecuteWaveMenu,
            Waveform);
        _waveMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_waveMenu, menu))
            {
                _waveMenu = null;
            }
        };
        menu.IsOpen = true;
    }

    private void Overview_MouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (IsUiBusy)
        {
            return;
        }

        e.Handled = true;
        OpenOverviewContextMenu(e.GetPosition(Overview).X);
    }

    private bool OpenContextMenuFromKeyboard()
    {
        if (IsUiBusy)
        {
            return true;
        }

        if (TryOpenTabContextMenuUnderPointer())
        {
            return true;
        }

        if (Overview.IsMouseOver)
        {
            OpenOverviewContextMenu(System.Windows.Input.Mouse.GetPosition(Overview).X);
            return true;
        }

        return Waveform.TryRequestContextMenuFromKeyboard();
    }

    private void OpenOverviewContextMenu(double x)
    {
        OpenWaveformContextMenu(new WaveformContextHit
        {
            Frame = (long)Math.Round(Overview.FrameAt(x)),
        });
    }

    private bool TryOpenTabContextMenuUnderPointer()
    {
        foreach (var border in DocumentTabs.Children.OfType<System.Windows.Controls.Border>())
        {
            if (border.IsMouseOver && border.Tag is DocumentSession session)
            {
                OpenTabContextMenu(border, session);
                return true;
            }
        }

        if (_tileMode)
        {
            foreach (var pane in _tilePanes)
            {
                if (pane.Header.IsMouseOver)
                {
                    OpenTabContextMenu(pane.Header, pane.Session);
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsPointerOverTileHeader()
    {
        if (!_tileMode)
        {
            return false;
        }

        foreach (var pane in _tilePanes)
        {
            if (pane.Header.IsMouseOver)
            {
                return true;
            }
        }

        return false;
    }

    private WaveformContextMenuModel CaptureWaveMenuModel(WaveformContextHit hit)
    {
        var document = _document;
        var hasDoc = document is not null;
        var recording = IsRecording;
        var busy = IsUiBusy;
        var selectedTabs = SelectedTabsInOrder();
        var canEdit = hasDoc && !busy && !recording;
        var canNavigate = hasDoc && !busy && !recording;
        var hasSelection = hasDoc && !document!.Selection.IsEmpty;
        return new WaveformContextMenuModel
        {
            HasDocument = hasDoc,
            CanEdit = canEdit,
            CanNavigate = canNavigate,
            IsBusy = busy,
            IsPlaying = IsPlaybackActive(),
            IsRecording = recording,
            HasSelection = hasSelection,
            HasAnySelection = hasSelection
                || Waveform.HasSelectedMarkers
                || Waveform.HasSelectedRegions
                || Waveform.HasSelectedTimelineItems,
            CanUndo = hasDoc && _history.CanUndo,
            CanRedo = hasDoc && _history.CanRedo,
            CanPasteAudio = _clipboard is { IsEmpty: false },
            CanPasteHistory = _historyRecipeClipboard.Count > 0,
            HasMarkers = hasDoc && document!.Markers.Count > 0,
            HasRegions = hasDoc && document!.Regions.Count > 0,
            HasSelectedMarkers = Waveform.HasSelectedMarkers,
            HasSelectedRegions = Waveform.HasSelectedRegions,
            HasSampleLoop = hasDoc && !document!.SampleLoop.IsEmpty,
            AllowsRegionsAndLoops = hasDoc && document!.AllowsRegionsAndLoops,
            CanRenameMarker = hasDoc && (Waveform.HasSelectedMarkers || document!.HasMarkerAt(Waveform.PlayheadFrame)),
            CanRenameRegion = hasDoc
                && document!.AllowsRegionsAndLoops
                && (Waveform.HasSelectedRegions || document.Regions.Count > 0),
            HasSolo = (_activeSession?.SoloMask ?? 0) != 0,
            CenterLocked = Waveform.CenterLocked,
            SilentSkip = SilentSkipCheck.IsChecked == true,
            AlwaysOnTop = AlwaysOnTopCheck.IsChecked == true,
            TipsVisible = AppStorage.Settings.ShowTips,
            WaapiVisible = _waapiPanelVisible,
            PlayExit = WaapiBar.PlayPostExitChecked,
            WaapiExportEnabled = WaapiBar.ExportEnabled,
            CanReopenTab = _workspace.ClosedTabs.Count > 0,
            HasMultipleTabs = _sessions.Count > 1,
            CanCloseOtherTabs = hasDoc && !busy && !recording && _sessions.Count > 1,
            CanCloseTabsToRight = CanCloseTabsFromActive(rightSide: true, hasDoc, busy, recording),
            CanCloseTabsToLeft = CanCloseTabsFromActive(rightSide: false, hasDoc, busy, recording),
            CanMergeTabs = !busy && !recording && _selectedTabs.Count >= 2,
            HasSelectedTabs = selectedTabs.Length > 0,
            HasMultipleSelectedTabs = selectedTabs.Length >= 2,
            AllTabsSelected = AllTabsSelected,
            SelectedTabsHaveMarkers = AnyTabHasMarkers(selectedTabs),
            SelectedTabsHaveRegions = AnyTabHasRegions(selectedTabs),
            WaveTileArrange = _tileArrange,
            CanTileHorizontal = CanOfferTileArrange(WaveformTileArrange.Horizontal),
            CanTileVertical = CanOfferTileArrange(WaveformTileArrange.Vertical),
            CanTileGrid = CanOfferTileArrange(WaveformTileArrange.Grid),
            WaveformMaximized = _waveformMaximized,
            CanLoopPlay = canNavigate
                && (hasSelection
                    || !document!.SampleLoop.IsEmpty
                    || document.TryGetRoleSpan(MarkerRole.Loop, Waveform.PlayheadFrame, out _)),
            CanAddMarkerHere = canEdit && !document!.HasMarkerAt(hit.Frame),
            AnalysisView = Waveform.AnalysisView,
            Hit = hit,
        };
    }

    private void ExecuteWaveMenu(WaveMenuCommand command)
    {
        switch (command)
        {
            case WaveMenuCommand.ClearMarkers:
                ClearMarkers(_waveMenuHit.MarkerFrames);
                break;
            case WaveMenuCommand.ClearRegion:
                ClearRegion(_waveMenuHit.Region);
                break;
            case WaveMenuCommand.ClearLoop:
            case WaveMenuCommand.ClearLoopItem:
                ClearSampleLoop();
                break;
            case WaveMenuCommand.PlayFromHere:
                PlayFromFrame(_waveMenuHit.Frame);
                break;
            case WaveMenuCommand.SeekHere:
                SeekFrame(_waveMenuHit.Frame);
                break;
            case WaveMenuCommand.AddMarkerHere:
                AddMarkerAtFrame(_waveMenuHit.Frame);
                break;
            case WaveMenuCommand.SelectSpanHere:
                Waveform.SelectSpanAt(_waveMenuHit.Frame);
                break;
            case WaveMenuCommand.Undo:
                UndoEdit();
                break;
            case WaveMenuCommand.Redo:
                RedoEdit();
                break;
            case WaveMenuCommand.Cut:
                ApplyCut();
                break;
            case WaveMenuCommand.Copy:
                ApplyCopy();
                break;
            case WaveMenuCommand.Paste:
                ApplyPaste();
                break;
            case WaveMenuCommand.PasteHistory:
                // タブ選択中は選択タブへ（Ctrl+V・タブメニューと同じ挙動）。
                if (HasTabSelection)
                {
                    PasteHistoryRecipesToTabs(SelectedTabsInOrder());
                }
                else if (_activeSession is not null)
                {
                    PasteHistoryRecipesToTabs([_activeSession]);
                }

                break;
            case WaveMenuCommand.Delete:
                ApplyDelete();
                break;
            case WaveMenuCommand.DeleteSilence:
                ApplyDeleteSilence();
                break;
            case WaveMenuCommand.SelectAll:
                Waveform.SelectAll();
                break;
            case WaveMenuCommand.ClearSelection:
                Waveform.ClearSelection();
                break;
            case WaveMenuCommand.SelectToStart:
                Waveform.SelectToDocumentEdge(-1);
                break;
            case WaveMenuCommand.SelectToEnd:
                Waveform.SelectToDocumentEdge(1);
                break;
            case WaveMenuCommand.History:
                OpenEditHistory();
                break;
            case WaveMenuCommand.FadeIn:
                PromptFade(fadeIn: true, Waveform);
                break;
            case WaveMenuCommand.FadeOut:
                PromptFade(fadeIn: false, Waveform);
                break;
            case WaveMenuCommand.FadeAround:
                ApplyFadeAroundPlayhead();
                break;
            case WaveMenuCommand.Normalize:
                ApplyNormalize();
                break;
            case WaveMenuCommand.NormalizePerRegion:
                ApplyNormalizePerRegion();
                break;
            case WaveMenuCommand.Volume:
                PromptVolume();
                break;
            case WaveMenuCommand.Pitch:
                PromptPitch();
                break;
            case WaveMenuCommand.TimeStretch:
                PromptTimeStretch();
                break;
            case WaveMenuCommand.Reverse:
                ApplyReverse();
                break;
            case WaveMenuCommand.SampleRate:
                PromptFormatConvert(FormatConvertKind.SampleRate);
                break;
            case WaveMenuCommand.BitDepth:
                PromptFormatConvert(FormatConvertKind.BitDepth);
                break;
            case WaveMenuCommand.Channels:
                PromptFormatConvert(FormatConvertKind.Channels);
                break;
            case WaveMenuCommand.AddMarker:
                TryAddMarkerAtPlayhead();
                break;
            case WaveMenuCommand.RenameMarker:
            case WaveMenuCommand.RenameRegion:
                RenameMarkerAtPosition();
                break;
            case WaveMenuCommand.DeleteMarkers:
                ApplyDeleteMarkers();
                break;
            case WaveMenuCommand.DeleteAllMarkers:
                ApplyDeleteAllMarkers();
                break;
            case WaveMenuCommand.SetRegion:
                TrySetRegionFromSelection();
                break;
            case WaveMenuCommand.DeleteRegions:
                ApplyDeleteSelectedRegions();
                break;
            case WaveMenuCommand.DeleteAllRegions:
                ApplyDeleteAllRegions();
                break;
            case WaveMenuCommand.SetLoop:
                SetSampleLoopFromSelection();
                break;
            case WaveMenuCommand.PrevMarker:
                Waveform.SeekToMarker(-1);
                break;
            case WaveMenuCommand.NextMarker:
                Waveform.SeekToMarker(1);
                break;
            case WaveMenuCommand.TogglePlayback:
                TogglePlayback();
                break;
            case WaveMenuCommand.PauseHere:
                PausePlaybackHere();
                break;
            case WaveMenuCommand.Preroll:
                StartPrerollPlayback();
                break;
            case WaveMenuCommand.Restart:
                RestartFromLastPlaybackStart();
                break;
            case WaveMenuCommand.LoopPlay:
                JumpToLoopPrerollAndPlay();
                break;
            case WaveMenuCommand.PlayExit:
                TogglePlayPostExit();
                break;
            case WaveMenuCommand.Record:
                ToggleRecording();
                break;
            case WaveMenuCommand.GoStart:
                ExecuteTransport(TransportCommand.GoToStart);
                break;
            case WaveMenuCommand.GoEnd:
                ExecuteTransport(TransportCommand.GoToEnd);
                break;
            case WaveMenuCommand.ViewLeft:
                Waveform.SeekToViewEdge(-1);
                break;
            case WaveMenuCommand.ViewRight:
                Waveform.SeekToViewEdge(1);
                break;
            case WaveMenuCommand.TimeZoomIn:
                Waveform.ZoomTimeIn();
                break;
            case WaveMenuCommand.TimeZoomOut:
                Waveform.ZoomTimeOut();
                break;
            case WaveMenuCommand.TimeZoomMax:
                Waveform.ZoomTimeToMax();
                break;
            case WaveMenuCommand.TimeZoomFit:
                Waveform.ResetTimeZoom();
                break;
            case WaveMenuCommand.AmpZoomIn:
                Waveform.ZoomAmpIn();
                break;
            case WaveMenuCommand.AmpZoomOut:
                Waveform.ZoomAmpOut();
                break;
            case WaveMenuCommand.AmpZoomMax:
                Waveform.ZoomAmpToMax();
                break;
            case WaveMenuCommand.AmpZoomReset:
                Waveform.ResetAmpZoom();
                break;
            case WaveMenuCommand.CenterPlayhead:
                Waveform.CenterViewOnPlayhead();
                break;
            case WaveMenuCommand.CenterLock:
                if (Waveform.CenterLocked)
                {
                    Waveform.UnlockCenter();
                }
                else
                {
                    Waveform.LockCenterToPlayhead();
                }

                break;
            case WaveMenuCommand.ViewWaveform:
                Waveform.SetAnalysisView(WaveformAnalysisView.Waveform);
                break;
            case WaveMenuCommand.ViewSpectrogram:
                Waveform.SetAnalysisView(WaveformAnalysisView.Spectrogram);
                break;
            case WaveMenuCommand.ViewOverlay:
                Waveform.SetAnalysisView(WaveformAnalysisView.Overlay);
                break;
            case WaveMenuCommand.ViewLoudness:
                Waveform.SetAnalysisView(WaveformAnalysisView.Loudness);
                break;
            case WaveMenuCommand.TileOff:
                SetWaveformTileArrange(WaveformTileArrange.Off);
                break;
            case WaveMenuCommand.TileHorizontal:
                SetWaveformTileArrange(WaveformTileArrange.Horizontal);
                break;
            case WaveMenuCommand.TileVertical:
                SetWaveformTileArrange(WaveformTileArrange.Vertical);
                break;
            case WaveMenuCommand.TileGrid:
                SetWaveformTileArrange(WaveformTileArrange.Grid);
                break;
            case WaveMenuCommand.MaximizeWaveform:
                ToggleWaveformMaximize();
                break;
            case WaveMenuCommand.SoloNext:
                CycleChannelSolo(1);
                break;
            case WaveMenuCommand.SoloPrev:
                CycleChannelSolo(-1);
                break;
            case WaveMenuCommand.SoloClear:
                ClearChannelSolo();
                break;
            case WaveMenuCommand.FocusTime:
                StatusTimes.FocusCurrentTime();
                break;
            case WaveMenuCommand.SilentSkip:
                SilentSkipCheck.IsChecked = SilentSkipCheck.IsChecked != true;
                break;
            case WaveMenuCommand.AlwaysOnTop:
                AlwaysOnTopCheck.IsChecked = AlwaysOnTopCheck.IsChecked != true;
                break;
            case WaveMenuCommand.NewDocument:
                NewDocument();
                break;
            case WaveMenuCommand.Open:
                OpenFromDialog();
                break;
            case WaveMenuCommand.Save:
                Save(saveAs: false);
                break;
            case WaveMenuCommand.SaveAs:
                Save(saveAs: true);
                break;
            case WaveMenuCommand.SaveMp3:
                SaveAsMp3();
                break;
            case WaveMenuCommand.RenameFile:
                if (_activeSession is not null)
                {
                    BeginFileNameEdit(_activeSession);
                }

                break;
            case WaveMenuCommand.DuplicateFile:
                if (HasTabSelection)
                {
                    DuplicateSessions(SelectedTabsInOrder());
                }
                else if (_activeSession is not null)
                {
                    DuplicateSession(_activeSession);
                }

                break;
            case WaveMenuCommand.MergeTabs:
                MergeSelectedTabs();
                break;
            case WaveMenuCommand.DeleteFile:
                if (HasTabSelection)
                {
                    DeleteSessionFiles(SelectedTabsInOrder());
                }
                else if (_activeSession is not null)
                {
                    DeleteSessionFile(_activeSession);
                }

                break;
            case WaveMenuCommand.CloseTab:
                CloseDocument();
                break;
            case WaveMenuCommand.CloseOthers:
                if (_activeSession is not null)
                {
                    CloseOtherTabs(_activeSession);
                }

                break;
            case WaveMenuCommand.CloseTabsRight:
                if (_activeSession is not null)
                {
                    CloseTabsFrom(_activeSession, rightSide: true);
                }

                break;
            case WaveMenuCommand.CloseTabsLeft:
                if (_activeSession is not null)
                {
                    CloseTabsFrom(_activeSession, rightSide: false);
                }

                break;
            case WaveMenuCommand.CloseAll:
                CloseAllTabs();
                break;
            case WaveMenuCommand.CopyAllTabTimes:
                CopyAllTabTimes();
                break;
            case WaveMenuCommand.ReopenTab:
                ReopenLastClosedTab();
                break;
            case WaveMenuCommand.SelectAllTabs:
                SelectAllTabs();
                break;
            case WaveMenuCommand.NextTab:
                ActivateAdjacentTab(1);
                break;
            case WaveMenuCommand.PrevTab:
                ActivateAdjacentTab(-1);
                break;
            case WaveMenuCommand.Settings:
                OpenSettings();
                break;
            case WaveMenuCommand.WaapiPanel:
                ToggleWaapiPanel();
                break;
            case WaveMenuCommand.WwiseExport:
                if (WaapiBar.ExportEnabled)
                {
                    _ = ExportToWwiseAsync();
                }

                break;
            case WaveMenuCommand.Quit:
                Close();
                break;
            case WaveMenuCommand.ExportWave:
                // タブ選択中は選択タブを一括書き出し（タブメニューと同じ挙動）。
                if (HasTabSelection)
                {
                    ExportTabs(SelectedTabsInOrder(), AudioFileKind.Wave);
                }
                else
                {
                    ExportActiveTab(AudioFileKind.Wave);
                }

                break;
            case WaveMenuCommand.ExportMp3:
                if (HasTabSelection)
                {
                    ExportTabs(SelectedTabsInOrder(), AudioFileKind.Mp3);
                }
                else
                {
                    ExportActiveTab(AudioFileKind.Mp3);
                }

                break;
            case WaveMenuCommand.ExportByMarkers:
                if (HasTabSelection)
                {
                    ExportTabsSeparated(SelectedTabsInOrder(), TabExportSplit.Markers);
                }
                else
                {
                    ExportActiveTabSeparated(TabExportSplit.Markers);
                }

                break;
            case WaveMenuCommand.ExportByRegions:
                if (HasTabSelection)
                {
                    ExportTabsSeparated(SelectedTabsInOrder(), TabExportSplit.Regions);
                }
                else
                {
                    ExportActiveTabSeparated(TabExportSplit.Regions);
                }

                break;
            case WaveMenuCommand.ExportByChannels:
                if (HasTabSelection)
                {
                    ExportTabsSeparated(SelectedTabsInOrder(), TabExportSplit.Channels);
                }
                else
                {
                    ExportActiveTabSeparated(TabExportSplit.Channels);
                }

                break;
            case WaveMenuCommand.ExportAllWave:
                ExportAllTabsWave();
                break;
            case WaveMenuCommand.ExportAllMp3:
                ExportAllTabsMp3();
                break;
            case WaveMenuCommand.Tips:
                ToggleTips();
                break;
            case WaveMenuCommand.Manual:
                ManualViewer.Open(this);
                break;
            case WaveMenuCommand.GitHub:
                TryOpenUrl(AppVersion.RepositoryUrl);
                break;
        }
    }

    private void PlayFromFrame(long frame)
    {
        if (_document is null)
        {
            return;
        }

        SeekFrame(frame);
        if (!IsPlaybackActive())
        {
            StartPlayback(frame, prerollSeconds: 0);
        }
    }

    private void AddMarkerAtFrame(long frame)
    {
        if (_document is null)
        {
            return;
        }

        frame = Math.Clamp(frame, 0, _document.FrameCount);
        SeekFrame(frame);
        if (_document.HasMarkerAt(frame))
        {
            return;
        }

        ApplyPlaceLive(ProcessEdits.AddMarker(_document, frame));
        AfterMarkerEdit();
    }

    private void ClearChannelSolo()
    {
        if (_activeSession is null)
        {
            return;
        }

        _activeSession.SoloMask = 0;
        ApplyChannelSolo();
    }

    private void ExportActiveTab(AudioFileKind kind)
    {
        if (_activeSession is null)
        {
            return;
        }

        ExportTabs([_activeSession], kind);
    }

    private void ExportActiveTabSeparated(TabExportSplit split)
    {
        if (_activeSession is null)
        {
            return;
        }

        ExportTabsSeparated([_activeSession], split);
    }

    private bool CanCloseTabsFromActive(bool rightSide, bool hasDoc, bool busy, bool recording)
    {
        if (!hasDoc || busy || recording || _activeSession is null)
        {
            return false;
        }

        var index = _sessions.IndexOf(_activeSession);
        if (index < 0)
        {
            return false;
        }

        return rightSide ? index < _sessions.Count - 1 : index > 0;
    }
}
