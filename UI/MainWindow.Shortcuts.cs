using System.Windows.Input;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        key = LibraryPlayerMode.ResolveDigitKey(key, e.ImeProcessedKey);
        var modifiers = Keyboard.Modifiers;
        if (MenuAccessKeys.IsContextMenuKey(key, modifiers))
        {
            if (!e.IsRepeat)
            {
                _pendingContextMenuKey = true;
            }

            e.Handled = true;
            return;
        }

        if (TryProcessShortcut(key, modifiers, e.IsRepeat))
        {
            e.Handled = true;
        }
    }

    private void MainWindow_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        key = LibraryPlayerMode.ResolveDigitKey(key, e.ImeProcessedKey);
        if (key is Key.Apps or Key.F10)
        {
            if (_pendingContextMenuKey)
            {
                _pendingContextMenuKey = false;
                e.Handled = true;
                OpenContextMenuFromKeyboard();
            }

            return;
        }

        if (key is Key.NumPad7 or Key.NumPad9 or Key.J or Key.K)
        {
            if (_seekNudgeDirection != 0
                && ((key is Key.NumPad7 or Key.J) && _seekNudgeDirection < 0
                    || (key is Key.NumPad9 or Key.K) && _seekNudgeDirection > 0))
            {
                StopSeekNudge();
            }

            return;
        }

        if (key is Key.Left or Key.Right or Key.NumPad1 or Key.NumPad3 or Key.OemComma or Key.OemPeriod)
        {
            if (_videoFrameStepDirection != 0
                && (key == Key.Left && _videoFrameStepDirection < 0
                    || key == Key.Right && _videoFrameStepDirection > 0))
            {
                StopVideoFrameStep();
            }

            if (_playbackShuttleDirection != 0
                && ((key is Key.Left or Key.NumPad1 or Key.OemComma) && _playbackShuttleDirection < 0
                    || (key is Key.Right or Key.NumPad3 or Key.OemPeriod) && _playbackShuttleDirection > 0))
            {
                StopPlaybackShuttle();
            }

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

        if (key is Key.Up or Key.Down)
        {
            if (_boostNudgeDirection != 0
                && (key == Key.Up && _boostNudgeDirection > 0
                    || key == Key.Down && _boostNudgeDirection < 0
                    || (Keyboard.Modifiers & ModifierKeys.Alt) == 0))
            {
                StopSpectrogramBoostNudge();
            }

            TryPlayLibraryAfterArrowRelease();
            return;
        }

        if (key is Key.NumPad4 or Key.NumPad6)
        {
            TryPlayLibraryAfterTrackKeyRelease();
        }

        if (key is Key.Home or Key.End or Key.PageUp or Key.PageDown)
        {
            if (!LibraryBrowser.PlaylistVisualIsPdf)
            {
                TryPlayLibraryAfterArrowRelease();
            }

            return;
        }

        if (key is Key.Add or Key.Subtract or Key.OemPlus or Key.OemMinus)
        {
            LibraryBrowser.ResetPlaylistPdfZoomLatch();
            ResetVideoLaunchScaleLatch();
            return;
        }

        if (key is Key.LeftAlt or Key.RightAlt)
        {
            if (_markerNudgeDirection != 0)
            {
                StopMarkerNudge();
            }

            if (_boostNudgeDirection != 0)
            {
                StopSpectrogramBoostNudge();
            }
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

        if (_pitchMenu is { IsOpen: true })
        {
            PitchShiftPicker.TryNudge(_pitchMenu, Math.Sign(e.Delta));
            e.Handled = true;
            return;
        }

        if (_timeStretchMenu is { IsOpen: true })
        {
            TimeStretchPicker.TryNudge(_timeStretchMenu, Math.Sign(e.Delta));
            e.Handled = true;
            return;
        }

        if (IsLibraryMaximized
            && e.OriginalSource is System.Windows.DependencyObject wheelOrigin
            && (IsDescendantOf(wheelOrigin, Overview)
                || FindWaveformFromOrigin(wheelOrigin) is not null))
        {
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

        var view = FindWaveformFromOrigin(origin);
        if (view is null)
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.Shift)
        {
            view.PanByVisibleFraction(e.Delta > 0 ? -0.1 : 0.1);
            e.Handled = true;
            return;
        }

        if (modifiers == ModifierKeys.Control)
        {
            view.WheelAmpZoom(e.Delta);
            e.Handled = true;
            return;
        }

        if (modifiers == ModifierKeys.None)
        {
            view.WheelTimeZoomAtPlayhead(e.Delta);
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

    private bool TryProcessShortcut(Key key, ModifierKeys modifiers, bool isRepeat = false)
    {
        if (IsUiBusy)
        {
            StopPlaybackShuttle();
            StopSeekNudge();
            StopVideoFrameStep();
            if (AppDialogKeys.IsEscape(key, modifiers))
            {
                TryRequestOpenCancel();
            }

            return true;
        }

        if (key == Key.F11 && modifiers == ModifierKeys.None)
        {
            ToggleWaveformMaximize();
            return true;
        }

        if (key == Key.F12 && modifiers == ModifierKeys.None)
        {
            ToggleAnalyzerMaximize();
            return true;
        }

        if (key == Key.F8 && modifiers == ModifierKeys.None)
        {
            ToggleVideoMiniPlayer();
            return true;
        }

        if (key == Key.F9 && modifiers == ModifierKeys.None)
        {
            ToggleLibraryMinimalChrome();
            return true;
        }

        if (key == Key.F10 && modifiers == ModifierKeys.None)
        {
            StopSeekNudge();
            StopVideoFrameStep();
            ToggleLibraryMaximize();
            return true;
        }

        // プレイヤーでも Silent Skip / Always on Top はステータスバー非表示の F8／F9 中も効かせる。
        if (key == Key.S && modifiers == ModifierKeys.Alt)
        {
            SilentSkipCheck.IsChecked = SilentSkipCheck.IsChecked != true;
            return true;
        }

        if (key == Key.A && modifiers == ModifierKeys.Alt)
        {
            AlwaysOnTopCheck.IsChecked = AlwaysOnTopCheck.IsChecked != true;
            return true;
        }

        if (TryConsumeRecordingShortcut(key, modifiers))
        {
            StopPlaybackShuttle();
            StopSeekNudge();
            StopVideoFrameStep();
            return true;
        }

        // タイル検索。Ctrl+F で開き、ボックス内では Enter / Alt+Enter / Esc を拾う。
        // プレイヤーではライブラリ／プレイリスト検索が Ctrl+F を使う。
        if (IsLibraryMaximized && TryProcessLibrarySearchKey(key, modifiers))
        {
            return true;
        }

        if (TryProcessTileSearchKey(key, modifiers))
        {
            return true;
        }

        if (IsTileSearchFocused)
        {
            // 検索ボックスへの文字入力はそのまま通す（1 文字ごとに判定される）。
            return false;
        }

        // ライブラリ／プレイリスト検索フォーカス中は文字入力を優先。
        // Alt+R／N／P（と Ctrl+Shift+P）だけは下のトグルへ通す（Alt は検索に文字を入れない）。
        if (IsLibraryMaximized
            && LibraryBrowser.IsSearchFocused
            && !LibraryPlayerMode.BypassesSearchFocus(key, modifiers))
        {
            return false;
        }

        // 動画ミニ: Q／Escape でプレイリストへ戻る（本再生中は暗いプレビューへ。F10 と同じ）。
        if (IsVideoMiniPlayerActive()
            && !isRepeat
            && LibraryPlayerMode.LeavesVideoMiniToPlaylist(key, modifiers))
        {
            LeaveVideoMiniToPlaylist(minimalChrome: false);
            return true;
        }

        // F8／引数起動の動画・PDF ミニ: +／- で窓サイズを拡縮（テンキー可）。
        // PDF 内容ズームより先に窓拡縮へ回す。
        if (IsVideoMiniPlayerActive()
            && !_playlistVideoFullscreen
            && TryHandleVideoLaunchScaleKey(key, modifiers, isRepeat))
        {
            return true;
        }

        // PDF 表示中はページ／内容拡縮を優先し、プレイリスト操作キーは奪う。
        if (IsLibraryMaximized
            && LibraryBrowser.PlaylistVisualIsPdf
            && TryHandlePlaylistPdfKey(key, modifiers, isRepeat))
        {
            return true;
        }

        // プレイヤー専用: Tab 循環 / F1 ツリー / F2 お気に入り / F3 プレイリスト
        if (IsLibraryMaximized && IsLibraryPaneCycleShortcut(key, modifiers))
        {
            if (key == Key.Tab)
            {
                LibraryBrowser.CyclePaneFocus(reverse: modifiers == ModifierKeys.Shift);
            }
            else
            {
                switch (key)
                {
                    case Key.F1:
                        LibraryBrowser.FocusExplorer();
                        break;
                    case Key.F2:
                        LibraryBrowser.FocusFavorites();
                        break;
                    default:
                        LibraryBrowser.FocusList();
                        break;
                }
            }

            return true;
        }

        // F5 はツリー再読込だけ。Ctrl+F5 は索引の再構築（すりガラスでロック）。
        if (IsLibraryMaximized && LibraryPlayerMode.IsExplorerRefresh(key, modifiers))
        {
            RefreshLibraryTree();
            return true;
        }

        if (IsLibraryMaximized && LibraryPlayerMode.IsLibraryCatalogRebuild(key, modifiers))
        {
            RebuildLibraryCatalog();
            return true;
        }

        // 空リスト／ツリー／検索フォーカスでも Alt+R はランダム切替。タイプアヘッドより先に拾う。
        if (IsLibraryMaximized
            && !IsLibraryGroupComboFocused
            && !ImeComposition.IsComposing
            && LibraryPlayerMode.IsShuffleToggle(key, modifiers))
        {
            LibraryBrowser.ToggleShuffle();
            return true;
        }

        // Alt+N は次の曲を自動再生。素の N（次の曲）より先、タイプアヘッド／検索入力より先。
        if (IsLibraryMaximized
            && !IsLibraryGroupComboFocused
            && !ImeComposition.IsComposing
            && LibraryPlayerMode.IsAutoPlayNextToggle(key, modifiers))
        {
            LibraryBrowser.ToggleAutoPlayNext();
            return true;
        }

        // Alt+P は Enter 追加時の自動再生。素の P（前の曲）より先、タイプアヘッド／検索入力より先。
        if (IsLibraryMaximized
            && !IsLibraryGroupComboFocused
            && !ImeComposition.IsComposing
            && LibraryPlayerMode.IsAutoPlayToggle(key, modifiers))
        {
            LibraryBrowser.ToggleAutoPlay();
            return true;
        }

        // Ctrl+Shift+P は動画プロキシの強制生成（設定の自動生成オフ／F8 抑止を無視）。
        if (IsLibraryMaximized
            && !IsLibraryGroupComboFocused
            && !ImeComposition.IsComposing
            && !isRepeat
            && LibraryPlayerMode.IsForceVideoProxyEncode(key, modifiers))
        {
            RequestForceVideoProxyEncode();
            return true;
        }

        // P／N は前後曲（テンキー 4／6）。ツリー／検索フォーカス中はタイプアヘッド（検索入力）を優先。
        if (IsLibraryMaximized
            && LibraryPlayerMode.AllowsPlayerLetterTransport(IsLibraryExplorerFocused)
            && !IsLibraryGroupComboFocused
            && !ImeComposition.IsComposing
            && !isRepeat
            && LibraryPlayerMode.IsPlayerTrackStepKey(key, modifiers))
        {
            TryStepLibraryPlaylist(key == Key.P ? -1 : 1);
            return true;
        }

        // R は再生し直し（テンキー 5）。Alt+R のランダムより後。ツリー／検索中はタイプアヘッド優先。
        if (IsLibraryMaximized
            && LibraryPlayerMode.AllowsPlayerLetterTransport(IsLibraryExplorerFocused)
            && !IsLibraryGroupComboFocused
            && !ImeComposition.IsComposing
            && !isRepeat
            && LibraryPlayerMode.IsPlayerRestartKey(key, modifiers))
        {
            RestartLibraryTrack();
            return true;
        }

        // 一時マーカーがあるとき Ctrl+←／→ をマーカージャンプに。
        if (IsLibraryMaximized
            && LibraryPlayerMode.PrefersMarkerArrowJump(
                IsPlayerScratchContext(),
                Waveform.HasPlayerScratchMarkers)
            && key is Key.Left or Key.Right
            && modifiers == ModifierKeys.Control)
        {
            Waveform.SeekToMarker(key == Key.Left ? -1 : 1);
            return true;
        }

        if (IsLibraryMaximized
            && LibraryPlayerMode.PrefersMarkerArrowJump(
                IsPlayerScratchContext(),
                Waveform.HasPlayerScratchMarkers)
            && key is Key.Left or Key.Right
            && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            Waveform.ExtendSelectionToMarker(key == Key.Left ? -1 : 1);
            return true;
        }

        // Ctrl+Delete／選択中 Delete は捨てマーカー削除。
        if (IsLibraryMaximized
            && IsPlayerScratchContext()
            && key == Key.Delete
            && modifiers == ModifierKeys.Control)
        {
            ApplyPlayerScratchMarkerDelete();
            return true;
        }

        if (IsLibraryMaximized
            && IsPlayerScratchContext()
            && key == Key.Delete
            && modifiers == ModifierKeys.None
            && LibraryPlayerMode.PrefersScratchMarkerDelete(
                playerMode: true,
                Waveform.HasSelectedMarkers))
        {
            ApplyPlayerScratchMarkerDelete();
            return true;
        }

        // I／O／M／Ins。ツリーフォーカス中は I／O／M をタイプアヘッドへ譲る（Ins はマーカーのまま）。
        if (IsLibraryMaximized
            && !IsLibraryGroupComboFocused
            && !ImeComposition.IsComposing
            && LibraryPlayerMode.AllowsPlayerScratchCueKey(
                key,
                modifiers,
                LibraryBrowser.IsExplorerTreeFocused)
            && TryHandlePlayerScratchCueKey(key, modifiers))
        {
            return true;
        }

        // Ctrl+←／↑ ライブラリ、Ctrl+→ プレイリスト、Ctrl+↓ お気に入り。
        // プレイヤー中の Ctrl+←／→（マーカー）は上で回したのでここには来ない。暗いプレビューではペインへ。
        if (IsLibraryMaximized
            && !IsLibraryGroupComboFocused
            && LibraryPlayerMode.IsPaneFocusArrow(key, modifiers))
        {
            switch (key)
            {
                case Key.Left:
                case Key.Up:
                    LibraryBrowser.FocusExplorer();
                    break;
                case Key.Right:
                    LibraryBrowser.FocusList();
                    break;
                default:
                    LibraryBrowser.FocusFavorites();
                    break;
            }

            return true;
        }

        // テンキー再生操作（プレイヤー／エディタ共通）。上段数字の割合／マーカーは別経路。
        if (!IsLibraryGroupComboFocused
            && !StatusTimes.IsTimeFocused
            && !IsEditingFileName
            && !Waveform.IsEditingMarkerComment
            && _volumeMenu is not { IsOpen: true }
            && _pitchMenu is not { IsOpen: true }
            && _timeStretchMenu is not { IsOpen: true }
            && _fadeMenu is not { IsOpen: true }
            && _formatMenu is not { IsOpen: true }
            && TryHandleTransportNumpad(key, modifiers, isRepeat))
        {
            return true;
        }

        if (IsLibraryMaximized && TryHandlePlaylistVideoFrameStep(key, modifiers, isRepeat))
        {
            return true;
        }

        if (IsLibraryMaximized
            && LibraryPlayerMode.IsVideoFullscreenToggle(key, modifiers)
            && TryTogglePlaylistVideoFullscreen())
        {
            return true;
        }

        if (IsLibraryMaximized
            && LibraryPlayerMode.IsVideoHudToggle(key, modifiers)
            && TryTogglePlaylistVideoHud())
        {
            return true;
        }

        if (IsLibraryMaximized
            && LibraryPlayerMode.IsVideoTimecodeToggle(key, modifiers)
            && TryTogglePlaylistVideoTimecode())
        {
            return true;
        }

        if (IsLibraryMaximized
            && !isRepeat
            && LibraryPlayerMode.IsPdfBackgroundPinToggle(key, modifiers)
            && TryTogglePdfBackgroundPin())
        {
            return true;
        }

        // F10: 本再生／イマーシブ一時停止／PDF 明るい表示から暗いプレビューへ（Escape と同じ。動画ミニの Q は上でプレイリストへ戻済み）。
        if (IsLibraryMaximized
            && key == Key.Q
            && modifiers == ModifierKeys.None
            && !isRepeat
            && TryExitPlaylistRealPlayback())
        {
            return true;
        }

        // 上段 0–9: 無修飾＝割合。Shift＋上段＝マーカー番号（二桁可。テンキーは上で再生操作済み）。
        // ツリーフォーカス中は使わない（Shift+8＝配下展開を優先。割合／マーカーは他ペインから）。
        if (IsLibraryMaximized
            && LibraryPlayerMode.AllowsPlayerDigitJump(IsLibraryExplorerFocused)
            && !IsLibraryGroupComboFocused
            && !StatusTimes.IsTimeFocused
            && !IsEditingFileName
            && _volumeMenu is not { IsOpen: true }
            && _pitchMenu is not { IsOpen: true }
            && _timeStretchMenu is not { IsOpen: true }
            && _fadeMenu is not { IsOpen: true }
            && _formatMenu is not { IsOpen: true }
            && TryHandleLibraryDigitJump(key, modifiers))
        {
            return true;
        }

        if (IsLibraryMaximized
            && key is Key.Left or Key.Right
            && modifiers == ModifierKeys.None)
        {
            StopPlaybackShuttle();
            if (IsLibraryExplorerFocused)
            {
                return false;
            }

            return true;
        }

        if (IsLibraryGroupComboFocused)
        {
            return false;
        }

        if (IsLibraryExplorerFocused)
        {
            if (!LibraryBrowser.IsExplorerSearchFocused)
            {
                if (LibraryBrowser.TryExplorerTypeahead(key, modifiers))
                {
                    return true;
                }
            }

            if (LibraryPlayerMode.IsPlaylistFolderAddKey(key, modifiers))
            {
                // Enter＝クリア直下／Ctrl+Enter＝クリア再帰／Shift＋＝追加（Ctrl で再帰）。
                LibraryBrowser.ClearExplorerTypeahead();
                var clear = LibraryPlayerMode.ClearsPlaylistOnFolderAdd(modifiers);
                var recursive = LibraryPlayerMode.RecursesPlaylistFolderAdd(modifiers);
                if (clear)
                {
                    LibraryBrowser.ReplacePlaylistFromSelection(recursive);
                }
                else
                {
                    LibraryBrowser.OpenSelectedFolder(recursive);
                }

                return true;
            }

            if (LibraryPlayerMode.IsExplorerExpandAll(key, modifiers))
            {
                LibraryBrowser.ClearExplorerTypeahead();
                LibraryBrowser.ExpandSelectedExplorerSubtree();
                return true;
            }

            if (LibraryPlayerMode.IsExplorerCollapseSubtree(key, modifiers))
            {
                LibraryBrowser.ClearExplorerTypeahead();
                LibraryBrowser.CollapseSelectedExplorerSubtree();
                return true;
            }

            // 設定のルート「上へ／下へ」と同じ並び替え。
            if (LibraryPlayerMode.IsExplorerRootReorder(key, modifiers))
            {
                LibraryBrowser.ClearExplorerTypeahead();
                LibraryBrowser.TryMoveSelectedExplorerRoots(key == Key.Up ? -1 : 1);
                return true;
            }

            if (key == Key.Space && modifiers == ModifierKeys.None)
            {
                LibraryBrowser.TryExplorerSpaceExpandOrMoveDown();
                return true;
            }

            if (key == Key.Space && modifiers == ModifierKeys.Shift)
            {
                LibraryBrowser.TryExplorerShiftSpaceCollapseOrMoveUp();
                return true;
            }

            if (LibraryPlayerMode.ExplorerOwnsHorizontal(key, modifiers))
            {
                LibraryBrowser.ClearExplorerTypeahead();
                StopPlaybackShuttle();
                return false;
            }

            if (LibraryPlayerMode.BlocksExplorerKey(key, modifiers))
            {
                return true;
            }

            // Ctrl+Q などアプリ共通ショートカットは続行。タイプアヘッドはツリーへ渡す。
            if (!LibraryPlayerMode.AllowsKey(key, modifiers))
            {
                return false;
            }
        }

        if (IsLibraryFavoritesFocused)
        {
            // Enter 系追加／Delete はお気に入りリスト側で処理。
            // ここで握るとプレイリストの Enter（再生）に吸われ、追加されない。
            if (LibraryPlayerMode.IsPlaylistFolderAddKey(key, modifiers))
            {
                return false;
            }

            if (key == Key.Delete && modifiers == ModifierKeys.None)
            {
                return false;
            }

            if (LibraryPlayerMode.BlocksExplorerKey(key, modifiers))
            {
                return true;
            }

            if (!LibraryPlayerMode.AllowsKey(key, modifiers))
            {
                return false;
            }
        }

        if (StatusTimes.IsTimeFocused)
        {
            StopPlaybackShuttle();
            return false;
        }

        if (!IsPlaybackShuttleKey(key, modifiers))
        {
            StopPlaybackShuttle();
        }

        if (key is not (Key.Left or Key.Right))
        {
            CommitTimelineNudgeSession();
        }

        if (_placeRepeatKind != PlaceRepeatKind.None && !IsContinuingPlaceKey(key, modifiers))
        {
            StopPlaceRepeat();
        }

        if (IsLibraryMaximized)
        {
            if ((key is Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown)
                && modifiers is ModifierKeys.None or ModifierKeys.Shift)
            {
                // 動画本再生・ミニ・PDF 本表示では Home／End／Page をエディタと同じタイムライン操作へ。
                if ((key is Key.Home or Key.End or Key.PageUp or Key.PageDown)
                    && LibraryPlayerMode.UsesEditorTimelineNavKeys(_playlistVideoChromeVisible))
                {
                    _libraryPlayOnArrowRelease = false;
                    return TryHandleWaveformTimelineNavKey(key, modifiers);
                }

                var extend = modifiers == ModifierKeys.Shift;
                // 単発は端から反対側へ回る。長押し連続は端で一旦止まる（Ctrl+Tab と同じ）。
                var wrap = !isRepeat;
                if (key is Key.Home or Key.End)
                {
                    LibraryBrowser.MoveSelectionToEdge(key == Key.Home ? -1 : 1, extend);
                }
                else if (key is Key.PageUp or Key.PageDown)
                {
                    LibraryBrowser.MoveSelectionPage(key == Key.PageUp ? -1 : 1, extend, wrap);
                }
                else
                {
                    LibraryBrowser.MoveSelection(key == Key.Up ? -1 : 1, extend, wrap);
                }

                _libraryPlayOnArrowRelease = !extend;
                return true;
            }

            if (key == Key.A && modifiers is ModifierKeys.Control or (ModifierKeys.Control | ModifierKeys.Shift))
            {
                // 動画本再生など前面 UI 隠れ中は全選択しない（続けて Delete で空にする事故を防ぐ）。
                if (LibraryPlayerMode.BlocksPlaylistMutationWhileChromeHidden(
                        _playlistVideoChromeVisible))
                {
                    return true;
                }

                LibraryBrowser.SelectAllRows();
                return true;
            }

            if (key == Key.C && modifiers == ModifierKeys.Control)
            {
                LibraryBrowser.CopySelected();
                return true;
            }

            // Enter はグローバル側（本再生中＝その場停止、停止中の動画／PDF＝Space と同じ）。

            // リストから外すだけ（ディスクは消さない）。選択中マーカーの Delete は上で処理済み。
            if (key == Key.Delete && modifiers == ModifierKeys.None)
            {
                if (LibraryPlayerMode.BlocksPlaylistMutationWhileChromeHidden(
                        _playlistVideoChromeVisible))
                {
                    return true;
                }

                RemoveLibrarySelectedFromList();
                return true;
            }

            // 捨てキュー操作は上の TryHandlePlayerScratchCueKey 済み。
            // 非コンテキスト時も I／O／M／Ins はここで消費し、エディタのフェード／AddMarker へ落とさない。
            if (modifiers == ModifierKeys.None
                && key is Key.I or Key.O or Key.M or Key.Insert)
            {
                return true;
            }

            if (IsPlayerScratchContext()
                && key is Key.Left or Key.Right
                && (modifiers & ModifierKeys.Alt) != 0
                && (modifiers & ~(ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Control)) == 0)
            {
                return BeginOrContinueMarkerNudge(key == Key.Left ? -1 : 1);
            }

            // 捨てキュー Undo／Redo（打てる状態のとき）。無ければプレイリスト除外の Undo。
            if (key == Key.Z && modifiers == (ModifierKeys.Control | ModifierKeys.Shift)
                || key == Key.Y && modifiers == ModifierKeys.Control)
            {
                if (IsPlayerScratchContext())
                {
                    UndoOrRedoPlayerScratch(redo: true);
                }

                return true;
            }

            if (key == Key.Z && modifiers == ModifierKeys.Control)
            {
                if (IsPlayerScratchContext() && Waveform.CanUndoPlayerScratch)
                {
                    UndoOrRedoPlayerScratch(redo: false);
                    return true;
                }

                TryUndoLibraryPlaylistRemove();
                return true;
            }

            // タブを閉じない。リストから外すのも Delete のみ。
            if (key == Key.W
                && modifiers is ModifierKeys.Control or (ModifierKeys.Control | ModifierKeys.Shift))
            {
                return true;
            }

            if (!LibraryPlayerMode.AllowsKey(key, modifiers))
            {
                return true;
            }
        }

        if (TryProcessHistoryShortcut(key, modifiers))
        {
            return true;
        }

        if (TryProcessEscapeShortcut(key, modifiers))
        {
            return true;
        }

        if (IsEditingFileName)
        {
            return false;
        }

        if (Waveform.IsEditingMarkerComment || StatusTimes.IsTimeFocused)
        {
            return false;
        }

        if (TryHandleFormatMenuShortcut(key, modifiers))
        {
            return true;
        }

        if (_fadeMenu is { IsOpen: true } fadeMenu)
        {
            return TryProcessFadeMenuShortcut(fadeMenu, key, modifiers);
        }

        if (modifiers == ModifierKeys.Shift && key is Key.A or Key.V)
        {
            CloseVolumeGainPicker();
            Waveform.ExitAnalysisView();
            return true;
        }

        if (TryHandleVolumeMenuShortcut(key, modifiers))
        {
            return true;
        }

        // 数字・小数点・Delete などをゲイン欄へ渡す。
        if (_volumeMenu is { IsOpen: true })
        {
            return false;
        }

        if (TryHandlePitchMenuShortcut(key, modifiers))
        {
            return true;
        }

        if (TryHandleTimeStretchMenuShortcut(key, modifiers))
        {
            return true;
        }

        if (TryProcessDocumentShortcut(key, modifiers, isRepeat))
        {
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

        if (key == Key.A && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            // タイル表示かどうかに関係なくタブを全選択。検索フィルター中はヒットしたタブのみ。
            SelectAllTabs();
            return true;
        }

        if (key == Key.A && modifiers == ModifierKeys.Control)
        {
            // タブバー、またはタイルの帯の上ではタブの全選択。
            if (DocumentTabHost.Visibility == System.Windows.Visibility.Visible && DocumentTabHost.IsMouseOver
                || IsPointerOverTileHeader())
            {
                SelectAllTabs();
                return true;
            }

            Waveform.SelectAll();
            return true;
        }

        if (key == Key.C && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ShowColorDevPanel();
            return true;
        }

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
            // 動画本再生中：その場停止（プレイリストは出さない）。
            if (IsPlaylistVideoPlaying())
            {
                return PausePlaybackHere(immersiveVideoHold: true);
            }

            // 停止中の動画／PDF：本再生／明るい表示へ（Enter は常に先頭から。Space は再開）。
            if (IsLibraryMaximized
                && LibraryPlayerMode.PlaysLibrarySelectionOnSpace(
                    IsLibraryExplorerFocused,
                    IsLibraryFavoritesFocused)
                && IsLibraryVisualTransportTarget()
                && !IsPlaybackActive())
            {
                PlayLibrarySelectionOnceOrToggle(fromStart: true);
                return true;
            }

            return PausePlaybackHere();
        }

        if (key == Key.Space && modifiers == ModifierKeys.None)
        {
            if (IsLibraryMaximized)
            {
                if (!LibraryPlayerMode.PlaysLibrarySelectionOnSpace(
                        IsLibraryExplorerFocused,
                        IsLibraryFavoritesFocused))
                {
                    return true;
                }

                PlayLibrarySelectionOnceOrToggle();
                return true;
            }

            TogglePlayback();
            return true;
        }

        // .（OemPeriod）は早送り。中央寄せは Z とテンキー .（Decimal）。
        if ((key is Key.Z or Key.Decimal) && modifiers == ModifierKeys.None)
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
            Waveform.ToggleSpectrogramView();
            return true;
        }

        if (key is Key.M or Key.Insert && modifiers == ModifierKeys.None)
        {
            // プレイヤーは上の IsLibraryMaximized 分岐で消費済み。ここへ来たらエディタのみ。
            if (IsLibraryMaximized)
            {
                return true;
            }

            return BeginOrContinuePlaceRepeat(PlaceRepeatKind.Marker);
        }

        if (key == Key.Q && modifiers == ModifierKeys.None)
        {
            ToggleRangeClick();
            return true;
        }

        if (key == Key.E && modifiers == ModifierKeys.Alt && _waapiPanelVisible)
        {
            TogglePlayPostExit();
            return true;
        }

        if (key == Key.W && modifiers == ModifierKeys.Alt)
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

        if (key == Key.R && modifiers == ModifierKeys.None)
        {
            ApplyReverse();
            return true;
        }

        if (key == Key.V && modifiers == ModifierKeys.None)
        {
            PromptVolume();
            return true;
        }

        if (key == Key.P && modifiers == ModifierKeys.None)
        {
            PromptPitch();
            return true;
        }

        if (key == Key.T && modifiers == ModifierKeys.None)
        {
            PromptTimeStretch();
            return true;
        }

        if (key == Key.Delete && modifiers == ModifierKeys.Control)
        {
            ApplyDeleteMarkers();
            return true;
        }

        if (key == Key.D && modifiers == ModifierKeys.Control)
        {
            // Ctrl+T はタイル巡回済み。無音検出は Ctrl+D。
            if (!IsLibraryMaximized && !IsRecording && _document is not null)
            {
                ApplyDetectSilence();
            }

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

        if (TryHandleWaveformTimelineNavKey(key, modifiers))
        {
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

        if (key is Key.Up or Key.Down && modifiers == ModifierKeys.Alt)
        {
            return BeginOrContinueSpectrogramBoostNudge(key == Key.Up ? 1 : -1);
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

        // エディタ: 上段 無修飾＝割合、Shift＝マーカー番号、Ctrl＝リージョン番号（テンキー再生操作は上で済み）。
        if (!IsLibraryMaximized && TryHandleEditorDigitJump(key, modifiers))
        {
            return true;
        }

        return false;
    }

    private bool TryConsumeRecordingShortcut(Key key, ModifierKeys modifiers)
    {
        if (!_recording)
        {
            return false;
        }

        if (key == Key.R && modifiers == ModifierKeys.Control)
        {
            StopRecording();
            return true;
        }

        if (key is Key.Space or Key.Enter or Key.Escape && modifiers == ModifierKeys.None)
        {
            StopRecording();
            return true;
        }

        if (key == Key.Q && modifiers == ModifierKeys.Control)
        {
            Close();
            return true;
        }

        return true;
    }

    private bool TryProcessEscapeShortcut(Key key, ModifierKeys modifiers)
    {
        if (key != Key.Escape)
        {
            return false;
        }

        // ツリーのタイプアヘッド打ち込みを先に捨てる（1秒待ちの代わりに即クリア）。
        if (IsLibraryMaximized
            && LibraryBrowser.IsExplorerTreeFocused
            && LibraryBrowser.ClearExplorerTypeahead())
        {
            return true;
        }

        if (_recording)
        {
            StopRecording();
            return true;
        }

        StopMarkerNudge();
        StopPlaceRepeat();
        StopSpectrogramBoostNudge();
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

        if (IsEditingFileName)
        {
            CancelFileNameEdit();
            return true;
        }

        if (CloseFadeCurvePicker() || CloseFormatConvertPicker() || CloseVolumeGainPicker() || ClosePitchShiftPicker() || CloseTimeStretchPicker())
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

        if (IsPlayerScratchContext() && Waveform.ScratchCues.HasAbPoint)
        {
            Waveform.ClearPlayerAbRepeat();
            SyncPlayWindowFromDocument();
            return true;
        }

        // 本再生／イマーシブ一時停止／PDF 明るい表示から、暗いプレビュー（前面 UI）へ戻す（Q も同じ）。
        if (IsLibraryMaximized && TryExitPlaylistRealPlayback())
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

        if (Waveform.TryExitAnalysisView())
        {
            return true;
        }

        return true;
    }

    private bool TryProcessFadeMenuShortcut(
        System.Windows.Controls.ContextMenu fadeMenu,
        Key key,
        ModifierKeys modifiers)
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
            PreviewFade(_fadePromptIsIn, FadeCurvePicker.HighlightedShape(fadeMenu));
            return true;
        }

        if (key == Key.Enter && modifiers == ModifierKeys.None)
        {
            ApplyFade(_fadePromptIsIn, FadeCurvePicker.HighlightedShape(fadeMenu));
            CloseFadeCurvePicker();
            return true;
        }

        if (modifiers == ModifierKeys.None
            && TryDigitPercent(key, out var fadeDigit)
            && fadeDigit > 0)
        {
            var index = (int)Math.Round(fadeDigit * 10d) - 1;
            if (FadeCurvePicker.HighlightByIndex(fadeMenu, index))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryProcessDocumentShortcut(Key key, ModifierKeys modifiers, bool isRepeat = false)
    {
        if (key == Key.F2 && modifiers == ModifierKeys.None)
        {
            if (_activeSession is not null)
            {
                BeginFileNameEdit(_activeSession);
                return true;
            }
        }

        if (key == Key.D && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (IsRecording)
            {
                return true;
            }

            if (HasTabSelection)
            {
                DuplicateSessions(SelectedTabsInOrder());
            }
            else if (_activeSession is not null)
            {
                DuplicateSession(_activeSession);
            }

            return true;
        }

        if (key == Key.B && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (!IsRecording && _selectedTabs.Count >= 2)
            {
                MergeSelectedTabs();
            }

            return true;
        }

        if (key == Key.Q && modifiers == ModifierKeys.Control)
        {
            Close();
            return true;
        }

        if (key == Key.R && modifiers == ModifierKeys.Control)
        {
            ToggleRecording();
            return true;
        }

        if (key == Key.W && modifiers == (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt))
        {
            LaunchWwiseProjectFromShortcut();
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
            if (!IsLibraryMaximized)
            {
                ReopenLastClosedTab();
            }

            return true;
        }

        if (key == Key.T && modifiers == ModifierKeys.Control)
        {
            if (!IsLibraryMaximized)
            {
                CycleWaveformTile();
            }

            return true;
        }

        if (key == Key.Tab && modifiers == ModifierKeys.Control)
        {
            if (!IsLibraryMaximized)
            {
                ActivateAdjacentTab(1, wrap: !isRepeat);
            }

            return true;
        }

        if (key == Key.Tab && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (!IsLibraryMaximized)
            {
                ActivateAdjacentTab(-1, wrap: !isRepeat);
            }

            return true;
        }

        if (key == Key.Tab && modifiers == ModifierKeys.None)
        {
            if (IsLibraryMaximized)
            {
                return true;
            }

            CycleChannelSolo(1);
            return true;
        }

        if (key == Key.Tab && modifiers == ModifierKeys.Shift)
        {
            if (IsLibraryMaximized)
            {
                return true;
            }

            CycleChannelSolo(-1);
            return true;
        }

        if (key == Key.PageDown && modifiers == ModifierKeys.Control)
        {
            if (!IsLibraryMaximized)
            {
                ActivateAdjacentTab(1, wrap: !isRepeat);
            }

            return true;
        }

        if (key == Key.PageUp && modifiers == ModifierKeys.Control)
        {
            if (!IsLibraryMaximized)
            {
                ActivateAdjacentTab(-1, wrap: !isRepeat);
            }

            return true;
        }

        if (key == Key.N && modifiers == ModifierKeys.Control)
        {
            NewDocument();
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
            if (IsLibraryMaximized)
            {
                ExportPlaylistSelectionMp3();
            }
            else
            {
                SaveAsMp3();
            }

            return true;
        }

        if (key == Key.S && modifiers == ModifierKeys.Control)
        {
            Save(saveAs: false);
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

    private bool TryHandlePitchMenuShortcut(Key key, ModifierKeys modifiers)
    {
        if (_pitchMenu is not { IsOpen: true })
        {
            return false;
        }

        if (key is Key.Up or Key.Down)
        {
            PitchShiftPicker.TryNudge(_pitchMenu, key == Key.Up ? 1 : -1);
            return true;
        }

        if (key == Key.Tab)
        {
            PitchShiftPicker.TryMoveFocus(
                _pitchMenu,
                reverse: (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift);
            return true;
        }

        if (key == Key.Space && modifiers == ModifierKeys.None)
        {
            if (PitchShiftPicker.IsTimeStretchFocused(_pitchMenu))
            {
                PitchShiftPicker.TryToggleTimeStretch(_pitchMenu);
                return true;
            }

            PreviewPitch(PitchShiftPicker.ReadSemitones(_pitchMenu), PitchShiftPicker.ReadTimeStretch(_pitchMenu));
            return true;
        }

        if (key == Key.Enter && modifiers == ModifierKeys.None)
        {
            ApplyPitch(PitchShiftPicker.ReadSemitones(_pitchMenu), PitchShiftPicker.ReadTimeStretch(_pitchMenu));
            return true;
        }

        if (key == Key.P && modifiers == ModifierKeys.None)
        {
            return true;
        }

        return false;
    }

    private bool TryHandleTimeStretchMenuShortcut(Key key, ModifierKeys modifiers)
    {
        if (_timeStretchMenu is not { IsOpen: true })
        {
            return false;
        }

        if (key is Key.Up or Key.Down)
        {
            TimeStretchPicker.TryNudge(_timeStretchMenu, key == Key.Up ? 1 : -1);
            return true;
        }

        if (key == Key.Tab)
        {
            TimeStretchPicker.TryMoveFocus(
                _timeStretchMenu,
                reverse: (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift);
            return true;
        }

        if (key == Key.Space && modifiers == ModifierKeys.None)
        {
            PreviewTimeStretch(TimeStretchPicker.ReadDestFrames(_timeStretchMenu));
            return true;
        }

        if (key == Key.Enter && modifiers == ModifierKeys.None)
        {
            ApplyTimeStretch(TimeStretchPicker.ReadDestFrames(_timeStretchMenu));
            return true;
        }

        if (key == Key.T && modifiers == ModifierKeys.None)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// エディタの上段数字ジャンプ。無修飾＝割合、Shift＝マーカー番号、Ctrl＝リージョン番号。
    /// </summary>
    private bool TryHandleEditorDigitJump(Key key, ModifierKeys modifiers)
    {
        if (!LibraryPlayerMode.IsDigitJumpChord(key, modifiers)
            || !TryDigitKey(key, out var digit))
        {
            return false;
        }

        if (LibraryPlayerMode.IsRegionDigitJump(key, modifiers))
        {
            if (_document is null || _document.Regions.Count == 0)
            {
                return true;
            }

            return ApplyResolvedRegionDigitJump(digit, _document.Regions.Count);
        }

        if (LibraryPlayerMode.IsMarkerDigitJump(key, modifiers))
        {
            if (_document is null || _document.Markers.Count == 0)
            {
                return true;
            }

            return ApplyResolvedMarkerDigitJump(
                digit,
                _document.Markers.Count,
                allowPercentFallback: false);
        }

        if (!TryDigitPercent(key, out var percent))
        {
            return false;
        }

        JumpByVisiblePercent(percent, extendSelection: false);
        return true;
    }

    private bool ApplyResolvedMarkerDigitJump(int digit, int markerCount, bool allowPercentFallback)
    {
        if (!LibraryPlayerMode.TryResolveMarkerDigitJump(
                _markerNumber,
                digit,
                markerCount,
                out var nextPending,
                out var seekId,
                out var percentFallback))
        {
            return false;
        }

        if (seekId is int id)
        {
            _markerNumber = nextPending;
            _regionNumber = 0;
            Waveform.SeekToMarkerId(id, extendSelection: false);
            RestartMarkerDigitTimer();
            return true;
        }

        ResetMarkerDigitEntry();
        if (allowPercentFallback && percentFallback)
        {
            JumpByVisiblePercent(digit / 10d, extendSelection: false);
        }

        return true;
    }

    private bool ApplyResolvedRegionDigitJump(int digit, int regionCount)
    {
        if (!LibraryPlayerMode.TryResolveRegionDigitJump(
                _regionNumber,
                digit,
                regionCount,
                out var nextPending,
                out var seekId))
        {
            return false;
        }

        _markerNumber = 0;
        if (seekId is int id)
        {
            _regionNumber = nextPending;
            Waveform.SeekToRegionNumber(id);
            RestartMarkerDigitTimer();
            return true;
        }

        if (nextPending > 0)
        {
            _regionNumber = nextPending;
            RestartMarkerDigitTimer();
            return true;
        }

        ResetMarkerDigitEntry();
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
        _regionNumber = 0;
        ResetPdfPageDigitEntry();
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
        // 割合ジャンプは上段のみ（テンキーは再生操作）。
        var digit = key switch
        {
            Key.D0 => 0,
            Key.D1 => 1,
            Key.D2 => 2,
            Key.D3 => 3,
            Key.D4 => 4,
            Key.D5 => 5,
            Key.D6 => 6,
            Key.D7 => 7,
            Key.D8 => 8,
            Key.D9 => 9,
            _ => -1,
        };
        if (digit < 0)
        {
            return false;
        }

        percent = digit / 10d;
        return true;
    }

    private static bool IsLibraryPaneCycleShortcut(Key key, ModifierKeys modifiers) =>
        LibraryPlayerMode.IsPaneCycleKey(key, modifiers)
        || (modifiers == ModifierKeys.None && key is Key.F1 or Key.F2 or Key.F3);

    /// <summary>
    /// プレイヤーのライブラリ／プレイリスト検索。Ctrl+F でフォーカス中のペインの検索へ。
    /// Enter で決定してツリーまたはリストへ戻る。Esc は未確定の入力を捨てる。
    /// </summary>
    private bool TryProcessLibrarySearchKey(Key key, ModifierKeys modifiers)
    {
        if (key == Key.F && modifiers == ModifierKeys.Control)
        {
            LibraryBrowser.FocusPaneSearch(LibraryBrowser.ActivePane);
            return true;
        }

        if (!LibraryBrowser.IsSearchFocused || ImeComposition.IsComposing)
        {
            return false;
        }

        if (key == Key.Enter && modifiers == ModifierKeys.None)
        {
            LibraryBrowser.CommitSearchFocus();
            return true;
        }

        if (key == Key.Escape && modifiers == ModifierKeys.None)
        {
            LibraryBrowser.CancelSearchFocus();
            return true;
        }

        return false;
    }

    private bool TryHandleTransportNumpad(Key key, ModifierKeys modifiers, bool isRepeat)
    {
        // テンキー＋ J／K／,／.（プレイヤー／エディタ共通。中央寄せは Z／テンキー .）。
        var command = LibraryPlayerMode.PlayerTransportCommand(key, modifiers);
        // ツリー／検索フォーカス中は J／K をタイプアヘッド（検索入力）に譲る。,／.／テンキーはそのまま。
        if (command is LibraryNumpadCommand.SeekBack or LibraryNumpadCommand.SeekForward
            && !LibraryPlayerMode.AllowsPlayerLetterTransport(IsLibraryExplorerFocused))
        {
            command = LibraryNumpadCommand.None;
        }

        var numpad = LibraryPlayerMode.UsesTransportNumpad()
            && LibraryPlayerMode.IsPlayerNumpadKey(key, modifiers);
        if (command == LibraryNumpadCommand.None && numpad)
        {
            command = LibraryPlayerMode.PlayerNumpadCommand(key, modifiers);
        }

        if (command == LibraryNumpadCommand.None)
        {
            // 2／8 など未割り当てテンキーは何もしない（割合ジャンプへ落とさない）。
            if (numpad)
            {
                StopPlaybackShuttle();
                StopSeekNudge();
                return true;
            }

            return false;
        }

        // キー種別ではなくコマンドで止める（テンキー 1／3／7／9 を押しっぱなしにするため）。
        if (command is not (LibraryNumpadCommand.Rewind or LibraryNumpadCommand.FastForward))
        {
            StopPlaybackShuttle();
        }

        if (command is not (LibraryNumpadCommand.SeekBack or LibraryNumpadCommand.SeekForward))
        {
            StopSeekNudge();
        }

        if (isRepeat
            && !LibraryPlayerMode.PlayerNumpadRepeatIsHold(command)
            && !LibraryPlayerMode.PlayerNumpadTrackStepRepeats(command))
        {
            return true;
        }

        switch (command)
        {
            case LibraryNumpadCommand.PlayPause:
                if (IsLibraryMaximized)
                {
                    ToggleLibraryPlayPause();
                }
                else if (IsPlaybackActive())
                {
                    // Space（停止で頭へ戻る）ではなく、プレイヤーの 0 と同じ一時停止／再開。
                    PausePlaybackHere();
                }
                else if (_document is not null)
                {
                    StartPlayback(_document.CursorFrame, prerollSeconds: 0);
                }

                break;
            case LibraryNumpadCommand.Restart:
                if (IsLibraryMaximized)
                {
                    RestartLibraryTrack();
                }
                else if (_document is not null)
                {
                    // プレイヤーの 5（頭から）に揃え、Alt+Enter（再生開始位置）とは別。
                    StartPlayback(0, prerollSeconds: 0);
                }

                break;
            case LibraryNumpadCommand.PreviousTrack:
                if (IsLibraryMaximized)
                {
                    // ↑↓ と同様、長押しは選択だけ送り、離したときに再生する。
                    StepLibraryTrackSelection(-1, wrap: !isRepeat);
                }
                else
                {
                    ActivateAdjacentTab(-1, wrap: !isRepeat);
                }

                break;
            case LibraryNumpadCommand.NextTrack:
                if (IsLibraryMaximized)
                {
                    StepLibraryTrackSelection(1, wrap: !isRepeat);
                }
                else
                {
                    ActivateAdjacentTab(1, wrap: !isRepeat);
                }

                break;
            case LibraryNumpadCommand.SeekBack:
                BeginOrContinueSeekNudge(-1);
                break;
            case LibraryNumpadCommand.SeekForward:
                BeginOrContinueSeekNudge(1);
                break;
            case LibraryNumpadCommand.Rewind:
                if (CanPlaybackShuttle(allowWhileStopped: true))
                {
                    BeginOrContinuePlaybackShuttle(-1);
                }

                break;
            case LibraryNumpadCommand.FastForward:
                if (CanPlaybackShuttle(allowWhileStopped: true))
                {
                    BeginOrContinuePlaybackShuttle(1);
                }

                break;
        }

        return true;
    }

    private bool TryHandleLibraryDigitJump(Key key, ModifierKeys modifiers)
    {
        if (!LibraryPlayerMode.IsDigitJumpChord(key, modifiers)
            || !TryDigitKey(key, out var digit))
        {
            return false;
        }

        // シークや行更新のあとでプレイリストへフォーカスが吸い戻されないように残す。
        var keepExplorer = IsLibraryExplorerFocused;
        var keepFavorites = IsLibraryFavoritesFocused;

        if (LibraryPlayerMode.IsMarkerDigitJump(key, modifiers))
        {
            if (LibraryPlayerMode.CanPlayerMarkerDigitJump(
                    playerMarkerContext: IsPlayerScratchContext(),
                    Waveform.HasPlayerScratchMarkers))
            {
                ApplyResolvedMarkerDigitJump(
                    digit,
                    Waveform.ScratchCues.Markers.Count,
                    allowPercentFallback: false);
            }
        }
        else if (TryDigitPercent(key, out var percent))
        {
            JumpByVisiblePercent(percent, extendSelection: false);
        }
        else
        {
            return false;
        }

        if (keepExplorer)
        {
            LibraryBrowser.RequestExplorerFocus();
        }
        else if (keepFavorites)
        {
            LibraryBrowser.FocusFavorites();
        }

        return true;
    }

    private static bool TryDigitKey(Key key, out int digit)
    {
        if (key is >= Key.D0 and <= Key.D9)
        {
            digit = key - Key.D0;
            return true;
        }

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            digit = key - Key.NumPad0;
            return true;
        }

        digit = -1;
        return false;
    }

    private bool TryHandleVideoLaunchScaleKey(Key key, ModifierKeys modifiers, bool isRepeat)
    {
        if (LibraryPlayerMode.IsZoomInKey(key, modifiers))
        {
            return TryScaleVideoLaunchWindow(1, isRepeat);
        }

        if (LibraryPlayerMode.IsZoomOutKey(key, modifiers))
        {
            return TryScaleVideoLaunchWindow(-1, isRepeat);
        }

        return false;
    }

    /// <summary>
    /// エディタと同じ Home／End／PageUp／PageDown（無修飾・Shift）の波形タイムライン操作。
    /// 動画本再生・ミニ・PDF 本表示でもプレイリスト移動の代わりに使う。
    /// </summary>
    private bool TryHandleWaveformTimelineNavKey(Key key, ModifierKeys modifiers)
    {
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

        return false;
    }

    private bool TryHandlePlaylistPdfKey(Key key, ModifierKeys modifiers, bool isRepeat)
    {
        // 選択プレビュー（暗表示）ではプレイリスト操作のまま。表示モード中だけ奪う。
        if (!LibraryBrowser.PlaylistVisualIsPdf || !LibraryBrowser.PlaylistVisualPlaying)
        {
            return false;
        }

        if (LibraryPlayerMode.IsZoomInKey(key, modifiers))
        {
            return LibraryBrowser.TryZoomPlaylistPdf(1, isRepeat);
        }

        if (LibraryPlayerMode.IsZoomOutKey(key, modifiers))
        {
            return LibraryBrowser.TryZoomPlaylistPdf(-1, isRepeat);
        }

        // 本再生（明るい表示）からの抜けは Escape／Q。Space／Enter は抜かない。
        if (modifiers == ModifierKeys.None && key is Key.Escape or Key.Q)
        {
            if (!isRepeat)
            {
                ExitPlaylistPdfView();
            }

            return true;
        }

        if (modifiers == ModifierKeys.None && key is Key.Enter or Key.Space)
        {
            return true;
        }

        // 上段数字だけページ番号へ（テンキーは再生操作＝4 前／6 次などへ通す。二桁可）。
        if (LibraryPlayerMode.IsPdfPageDigitKey(key, modifiers)
            && TryDigitKey(key, out var pageDigit))
        {
            if (!isRepeat)
            {
                ApplyResolvedPdfPageDigitJump(pageDigit);
            }

            return true;
        }

        if (LibraryPlayerMode.IsPdfPageKey(key, modifiers))
        {
            _libraryPlayOnArrowRelease = false;
            ResetPdfPageDigitEntry();
            switch (key)
            {
                case Key.Left:
                    if (LibraryBrowser.TryStepPlaylistPdfPage(-1))
                    {
                        SyncPlaylistPdfCursorFromVisual(flashPageNumber: true);
                    }

                    return true;
                case Key.Right:
                    if (LibraryBrowser.TryStepPlaylistPdfPage(1))
                    {
                        SyncPlaylistPdfCursorFromVisual(flashPageNumber: true);
                    }

                    return true;
            }
        }

        return LibraryPlayerMode.BlocksPlaylistWhilePdf(key, modifiers);
    }

    private bool ApplyResolvedPdfPageDigitJump(int digit)
    {
        if (!LibraryPlayerMode.TryResolvePdfPageDigitJump(
                _pdfPageDigit,
                digit,
                LibraryBrowser.PlaylistPdfPageCount,
                out var nextPending,
                out var seekPage))
        {
            return false;
        }

        if (seekPage is int page)
        {
            _pdfPageDigit = nextPending;
            if (LibraryBrowser.TryGoPlaylistPdfPageNumber(page))
            {
                SyncPlaylistPdfCursorFromVisual(flashPageNumber: true);
            }

            RestartMarkerDigitTimer();
            return true;
        }

        // 無効桁は消費のみ。桁送りも切る。
        _pdfPageDigit = 0;
        return true;
    }

    private void ResetPdfPageDigitEntry() => _pdfPageDigit = 0;

    private void SyncPlaylistPdfCursorFromVisual(bool flashPageNumber = false)
    {
        if (_document is null || !LibraryPlaylistDocuments.IsPdf(_document))
        {
            return;
        }

        var rate = Math.Max(1, _document.SampleRate);
        var frame = (long)Math.Round(LibraryBrowser.PlaylistVisualPosition.TotalSeconds * rate);
        frame = Math.Clamp(frame, 0, _document.FrameCount);
        _document.CursorFrame = frame;
        Waveform.PlayheadFrame = frame;
        SyncOverviewPlayhead();
        RefreshStatus();
        RefreshPlaylistPdfPageNumber();
        if (flashPageNumber)
        {
            FlashPlaylistPdfPageNumber();
        }
    }
}
