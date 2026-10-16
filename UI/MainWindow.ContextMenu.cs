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
        if (IsUiBusy || LibraryPlayerMode.HidesWaveformContextMenu(IsLibraryMaximized))
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

        // 前面 UI 隠れ中でもツリー／お気に入りのメニューは出す。
        // リストの「プレイリストからクリア」だけは、見えない選択を消す事故になるので出さない。
        if (LibraryPlayerMode.BlocksPlaylistMutationWhileChromeHidden(_playlistVideoChromeVisible)
            && LibraryBrowser.IsListKeyboardFocused)
        {
            return true;
        }

        if (LibraryBrowser.TryOpenKeyboardContextMenu())
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

        if (LibraryPlayerMode.HidesWaveformContextMenu(IsLibraryMaximized))
        {
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
        var canEdit = hasDoc && !busy && !recording && !IsLibraryMaximized;
        var canScratch = hasDoc && !busy && !recording && IsPlayerScratchContext();
        var canNavigate = hasDoc && !busy && !recording;
        var hasSelection = hasDoc && !document!.Selection.IsEmpty;
        var hasScratchMarkers = canScratch && Waveform.HasPlayerScratchMarkers;
        var scratchHasAtHit = canScratch && Waveform.PlayerScratchHasMarkerAt(hit.Frame);
        var scratchHasAtPlayhead = canScratch && Waveform.PlayerScratchHasMarkerAt(Waveform.PlayheadFrame);
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
            CanCopySelectionTimes = SelectionClipboardText.CanCopy(CaptureSelectionClipboardItems()),
            CanUndo = IsLibraryMaximized
                ? ((canScratch && Waveform.CanUndoPlayerScratch) || _libraryPlaylistUndo.CanUndo)
                : hasDoc && _history.CanUndo,
            CanRedo = IsLibraryMaximized
                ? canScratch && Waveform.CanRedoPlayerScratch
                : hasDoc && _history.CanRedo,
            CanPasteAudio = _clipboard is { IsEmpty: false },
            CanPasteHistory = _historyRecipeClipboard.Count > 0,
            HasMarkers = IsLibraryMaximized
                ? hasScratchMarkers
                : hasDoc && document!.Markers.Count > 0,
            HasRegions = hasDoc && document!.Regions.Count > 0 && !IsLibraryMaximized,
            HasSelectedMarkers = Waveform.HasSelectedMarkers,
            HasSelectedRegions = Waveform.HasSelectedRegions && !IsLibraryMaximized,
            HasSampleLoop = hasDoc && !document!.SampleLoop.IsEmpty && !IsLibraryMaximized,
            AllowsRegionsAndLoops = hasDoc && document!.AllowsRegionsAndLoops && !IsLibraryMaximized,
            // プレイヤー捨てマーカーはコメント改名しない。
            CanRenameMarker = !IsLibraryMaximized
                && hasDoc
                && (Waveform.HasSelectedMarkers || document!.HasMarkerAt(Waveform.PlayheadFrame)),
            CanRenameRegion = hasDoc
                && document!.AllowsRegionsAndLoops
                && !IsLibraryMaximized
                && (Waveform.HasSelectedRegions || document.Regions.Count > 0),
            HasSolo = (_activeSession?.SoloMask ?? 0) != 0,
            CenterLocked = Waveform.CenterLocked,
            SilentSkip = SilentSkipCheck.IsChecked == true,
            AlwaysOnTop = AlwaysOnTopCheck.IsChecked == true,
            WaapiVisible = _waapiPanelVisible,
            PlayExit = WaapiBar.PlayPostExitChecked,
            RangeClick = _rangeClickEnabled,
            Shuffle = LibraryBrowser.ShuffleEnabled,
            AutoPlayNext = LibraryBrowser.AutoPlayNextEnabled,
            AutoPlay = LibraryBrowser.AutoPlayEnabled,
            CanStepPlaylist = _sessions.Count > 0,
            CanRestartTrack = _sessions.Count > 0,
            VideoMini = IsVideoMiniPlayerActive(),
            MinimalChrome = IsLibraryMaximized && _libraryMinimalChrome && !IsVideoMiniPlayerActive(),
            CanToggleVideoMini = LibraryPlayerMode.CanToggleVideoMiniPlayer(
                IsVideoMiniPlayerActive(),
                IsVideoMiniEnterEligible(),
                hasPlayableSession: _sessions.Count > 0),
            VideoFullscreen = _playlistVideoFullscreen,
            CanToggleVideoFullscreen = IsLibraryMaximized
                && (_playlistVideoFullscreen
                    || (LibraryBrowser.PlaylistVisualShown
                        && (LibraryBrowser.PlaylistVisualIsVideo || LibraryBrowser.PlaylistVisualIsPdf))),
            CanScaleVideoMini = CanScaleVideoLaunchWindow(),
            VideoHud = _playlistVideoHud,
            CanToggleVideoHud = IsPlaylistVisualHudContext(),
            VideoTimecode = _playlistVideoTimecode,
            CanToggleVideoTimecode = IsPlaylistVisualHudContext(),
            PdfBackgroundPinned = LibraryBrowser.PdfBackgroundPinned,
            CanTogglePdfBackground = IsLibraryMaximized
                && (LibraryBrowser.PlaylistVisualIsPdf || LibraryBrowser.PdfBackgroundPinned),
            CanExitRealPlayback = CanOfferExitRealPlayback(),
            CanForceVideoProxy = CanOfferForceVideoProxy(),
            TileMode = _tileMode,
            TileSearchActive = TileSearchBox.Text.Length > 0,
            KeepTarget = WaapiBar.KeepTargetChecked,
            AutoActive = WaapiBar.AutoActiveChecked,
            CanKeepTarget = WaapiBar.KeepTargetToggleEnabled,
            WaapiExportEnabled = WaapiBar.ExportEnabled,
            CanReopenTab = _workspace.ClosedTabs.Count > 0,
            HasMultipleTabs = _sessions.Count > 1,
            CanCloseOtherTabs = hasDoc && !busy && !recording && _sessions.Count > 1,
            CanCloseTabsToRight = CanCloseTabsFromActive(rightSide: true, hasDoc, busy, recording),
            CanCloseTabsToLeft = CanCloseTabsFromActive(rightSide: false, hasDoc, busy, recording),
            CanMergeTabs = !busy && !recording && _selectedTabs.Count >= 2 && !IsLibraryMaximized,
            HasSelectedTabs = selectedTabs.Length > 0,
            HasMultipleSelectedTabs = selectedTabs.Length >= 2,
            AllTabsSelected = AllTabsSelected,
            SelectedTabsHaveMarkers = AnyTabHasMarkers(selectedTabs),
            SelectedTabsHaveRegions = AnyTabHasRegions(selectedTabs),
            WaveTileArrange = _tileArrange,
            CanTileHorizontal = CanOfferTileArrange(WaveformTileArrange.Horizontal),
            CanTileVertical = CanOfferTileArrange(WaveformTileArrange.Vertical),
            CanTileGrid = CanOfferTileArrange(WaveformTileArrange.Grid),
            WaveformMaximized = _waveformMaximizeMode == WaveformMaximizeMode.Waveform,
            AnalyzersMaximized = _waveformMaximizeMode == WaveformMaximizeMode.Analyzers,
            LibraryMaximized = _waveformMaximizeMode == WaveformMaximizeMode.Library,
            CanExportMp3 = !IsLibraryMaximized
                || LibraryPlayerMode.HasMp3ExportCandidate(ResolvePlaylistLoudnessMatchTargets()),
            CanExportAllMp3 = !IsLibraryMaximized
                || LibraryPlayerMode.HasMp3ExportCandidate(_sessions),
            CanLoopPlay = canNavigate
                && (hasSelection
                    || !document!.SampleLoop.IsEmpty
                    || document.TryGetRoleSpan(MarkerRole.Loop, Waveform.PlayheadFrame, out _)),
            CanAddMarkerHere = IsLibraryMaximized
                ? canScratch && !scratchHasAtHit
                : canEdit && !document!.HasMarkerAt(hit.Frame),
            AnalysisView = Waveform.AnalysisView,
            Hit = hit,
        };
    }

    private void ExecuteWaveMenu(WaveMenuCommand command)
    {
        if (IsLibraryMaximized && LibraryPlayerMode.BlocksWaveMenu(command))
        {
            return;
        }

        switch (command)
        {
            case WaveMenuCommand.ClearMarkers:
                if (IsLibraryMaximized)
                {
                    Waveform.TryRemovePlayerScratchMarkers(_waveMenuHit.MarkerFrames);
                }
                else
                {
                    ClearMarkers(_waveMenuHit.MarkerFrames);
                }

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
                if (IsLibraryMaximized)
                {
                    SyncPlayerScratchEnabled();
                    Waveform.TryAddPlayerScratchMarker(_waveMenuHit.Frame);
                }
                else
                {
                    AddMarkerAtFrame(_waveMenuHit.Frame);
                }

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
            case WaveMenuCommand.SilenceSelection:
                PromptSilenceSelection();
                break;
            case WaveMenuCommand.InsertSilence:
                PromptInsertSilence();
                break;
            case WaveMenuCommand.DetectSilence:
                ApplyDetectSilence();
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
            case WaveMenuCommand.CopySelectionTime:
                CopySelectionTimes(SelectionClipboardPathKind.FileName);
                break;
            case WaveMenuCommand.CopySelectionTimePath:
                CopySelectionTimes(SelectionClipboardPathKind.FullPath);
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
                if (IsLibraryMaximized)
                {
                    TryAddPlayerScratchMarkerAtPlayhead();
                }
                else
                {
                    TryAddMarkerAtPlayhead();
                }

                break;
            case WaveMenuCommand.RenameMarker:
            case WaveMenuCommand.RenameRegion:
                RenameMarkerAtPosition();
                break;
            case WaveMenuCommand.DeleteMarkers:
                if (IsLibraryMaximized)
                {
                    ApplyPlayerScratchMarkerDelete();
                }
                else
                {
                    ApplyDeleteMarkers();
                }

                break;
            case WaveMenuCommand.DeleteAllMarkers:
                if (IsLibraryMaximized)
                {
                    Waveform.TryClearPlayerScratchMarkers();
                }
                else
                {
                    ApplyDeleteAllMarkers();
                }

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
                if (IsLibraryMaximized)
                {
                    PlayLibrarySelectionOnceOrToggle();
                }
                else
                {
                    TogglePlayback();
                }

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
            case WaveMenuCommand.Shuffle:
                LibraryBrowser.ToggleShuffle();
                break;
            case WaveMenuCommand.AutoPlay:
                LibraryBrowser.ToggleAutoPlay();
                break;
            case WaveMenuCommand.PreviousTrack:
                TryStepLibraryPlaylist(-1);
                break;
            case WaveMenuCommand.NextTrack:
                TryStepLibraryPlaylist(1);
                break;
            case WaveMenuCommand.RestartTrack:
                RestartLibraryTrack();
                break;
            case WaveMenuCommand.AutoPlayNext:
                LibraryBrowser.ToggleAutoPlayNext();
                break;
            case WaveMenuCommand.RangeClick:
                ToggleRangeClick();
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
            case WaveMenuCommand.MaximizeAnalyzers:
                ToggleAnalyzerMaximize();
                break;
            case WaveMenuCommand.MaximizeLibrary:
                ToggleLibraryMaximize();
                break;
            case WaveMenuCommand.VideoMini:
                ToggleVideoMiniPlayer();
                break;
            case WaveMenuCommand.MinimalChrome:
                ToggleLibraryMinimalChrome();
                break;
            case WaveMenuCommand.VideoFullscreen:
                TryTogglePlaylistVideoFullscreen();
                break;
            case WaveMenuCommand.VideoMiniZoomIn:
                TryScaleVideoLaunchWindow(1, isRepeat: false);
                break;
            case WaveMenuCommand.VideoMiniZoomOut:
                TryScaleVideoLaunchWindow(-1, isRepeat: false);
                break;
            case WaveMenuCommand.VideoHud:
                TryTogglePlaylistVideoHud();
                break;
            case WaveMenuCommand.VideoTimecode:
                TryTogglePlaylistVideoTimecode();
                break;
            case WaveMenuCommand.PdfBackgroundPin:
                TryTogglePdfBackgroundPin();
                break;
            case WaveMenuCommand.ExitRealPlayback:
                if (IsVideoMiniPlayerActive())
                {
                    LeaveVideoMiniToPlaylist(minimalChrome: false);
                }
                else
                {
                    TryExitPlaylistRealPlayback();
                }

                break;
            case WaveMenuCommand.ForceVideoProxy:
                RequestForceVideoProxyEncode();
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
                if (IsLibraryMaximized)
                {
                    ExportPlaylistSelectionMp3();
                }
                else
                {
                    SaveAsMp3();
                }

                break;
            case WaveMenuCommand.RenameFile:
                if (_activeSession is not null)
                {
                    BeginFileNameEdit(_activeSession);
                }

                break;
            case WaveMenuCommand.CopyFileName:
                CopySessionPathTexts(fullPath: false);
                break;
            case WaveMenuCommand.CopyFilePath:
                CopySessionPathTexts(fullPath: true);
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
            case WaveMenuCommand.FindTabs:
                OpenTileSearch();
                break;
            case WaveMenuCommand.ClearTileSearch:
                CloseTileSearch();
                break;
            case WaveMenuCommand.Settings:
                OpenSettings();
                break;
            case WaveMenuCommand.ColorPanel:
                ShowColorDevPanel();
                break;
            case WaveMenuCommand.WaapiPanel:
                ToggleWaapiPanel();
                break;
            case WaveMenuCommand.KeepTarget:
                WaapiBar.ToggleKeepTarget();
                break;
            case WaveMenuCommand.AutoActive:
                WaapiBar.AutoActiveChecked = !WaapiBar.AutoActiveChecked;
                PersistWaapiSettings();
                break;
            case WaveMenuCommand.OutputFolder:
                BrowseExportOutputFolder();
                break;
            case WaveMenuCommand.LaunchWwise:
                LaunchWwiseProjectFromShortcut();
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
                if (IsLibraryMaximized)
                {
                    ExportPlaylistSelectionMp3();
                }
                else if (HasTabSelection)
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
            case WaveMenuCommand.Manual:
                ManualViewer.Open(this);
                break;
            case WaveMenuCommand.GitHub:
                TryOpenUrl(AppVersion.RepositoryUrl);
                break;
        }
    }

    private void CopySelectionTimes(
        SelectionClipboardPathKind pathKind,
        IReadOnlyList<DocumentSession>? sessions = null)
    {
        var text = SelectionClipboardText.Format(
            CaptureSelectionClipboardItems(sessions),
            pathKind,
            AppStorage.Settings.StatusShowSamples);
        if (text.Length == 0)
        {
            return;
        }

        SystemClipboard.TrySetText(text, this);
    }

    private (string? SourcePath, string DisplayName, WaveSelection Selection, int SampleRate)[] CaptureSelectionClipboardItems(
        IReadOnlyList<DocumentSession>? sessions = null)
    {
        sessions ??= HasTabSelection
            ? SelectedTabsInOrder()
            : _activeSession is null ? [] : [_activeSession];
        if (sessions.Count == 0)
        {
            return [];
        }

        var items = new (string? SourcePath, string DisplayName, WaveSelection Selection, int SampleRate)[sessions.Count];
        for (var i = 0; i < sessions.Count; i++)
        {
            var document = sessions[i].Document;
            items[i] = (document.SourcePath, sessions[i].DisplayName, document.Selection, document.SampleRate);
        }

        return items;
    }

    private void CopySessionPathTexts(bool fullPath)
    {
        var sessions = HasTabSelection
            ? SelectedTabsInOrder()
            : _activeSession is null ? [] : [_activeSession];
        CopySessionPathTexts(sessions, fullPath);
    }

    private void CopySessionPathTexts(IReadOnlyList<DocumentSession> sessions, bool fullPath)
    {
        if (sessions.Count == 0)
        {
            return;
        }

        string text;
        if (fullPath)
        {
            var paths = new string?[sessions.Count];
            for (var i = 0; i < sessions.Count; i++)
            {
                paths[i] = sessions[i].Document.SourcePath;
            }

            text = DocumentFileNames.ClipboardFullPaths(paths);
        }
        else
        {
            var items = new (string? SourcePath, string DisplayName)[sessions.Count];
            for (var i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                items[i] = (session.Document.SourcePath, session.DisplayName);
            }

            text = DocumentFileNames.ClipboardFileNames(items);
        }

        if (text.Length == 0)
        {
            return;
        }

        SystemClipboard.TrySetText(text, this);
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
