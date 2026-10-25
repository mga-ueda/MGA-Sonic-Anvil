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
        // プレイリスト側と同じ。外側クリックでメニューを閉じた貫通が、空領域の選択解除や
        // プレイヤー解除へ落ちてリストが消えるのを、閉じた直後のアイドルまで握りつぶす。
        LibraryBrowser.ArmContextMenuDismissGuard(menu);
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
        // フェード調整中は破壊編集メニューを無効化（再生・シーク系の CanNavigate は残す）。
        var canEdit = hasDoc
            && !busy
            && !recording
            && !IsLibraryMaximized
            && !BlocksOutsideFadeToolActions;
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
            CanUndo = ResolveMenuCanUndo(canScratch),
            CanRedo = ResolveMenuCanRedo(canScratch),
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
            CanMergeTabs = !busy && !recording && ResolveBatchEditTargets().Length >= 2,
            HasSelectedTabs = IsLibraryMaximized
                ? LibraryBrowser.SelectedSessions.Length > 0
                : selectedTabs.Length > 0,
            HasMultipleSelectedTabs = IsLibraryMaximized
                ? LibraryBrowser.SelectedSessions.Length >= 2
                : selectedTabs.Length >= 2,
            AllTabsSelected = AllTabsSelected,
            SelectedTabsHaveMarkers = AnyTabHasMarkers(
                IsLibraryMaximized ? ResolvePlaylistLoudnessMatchTargets() : selectedTabs),
            SelectedTabsHaveRegions = AnyTabHasRegions(
                IsLibraryMaximized ? ResolvePlaylistLoudnessMatchTargets() : selectedTabs),
            SelectedTabsHaveSampleLoop = AnyTabHasSampleLoop(
                IsLibraryMaximized ? ResolvePlaylistLoudnessMatchTargets() : selectedTabs),
            WaveTileArrange = _tileArrange,
            CanTileHorizontal = CanOfferTileArrange(WaveformTileArrange.Horizontal),
            CanTileVertical = CanOfferTileArrange(WaveformTileArrange.Vertical),
            CanTileGrid = CanOfferTileArrange(WaveformTileArrange.Grid),
            WaveformMaximized = _waveformMaximizeMode == WaveformMaximizeMode.Waveform,
            AnalyzersMaximized = _waveformMaximizeMode == WaveformMaximizeMode.Analyzers,
            LibraryMaximized = _waveformMaximizeMode == WaveformMaximizeMode.Library,
            CanExportMp3 = !IsLibraryMaximized
                || LibraryPlayerMode.HasMp3ExportCandidate(ResolvePlaylistLoudnessMatchTargets()),
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

    /// <summary>メニュー／ショートカット／トランスポート共通。選択があれば選択、なければアクティブ（プレイヤーはプレイリスト選択）。</summary>
    private void ExportMp3FromUi()
    {
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
