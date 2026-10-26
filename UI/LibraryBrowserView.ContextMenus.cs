using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

internal sealed partial class LibraryBrowserView
{
    private void RebuildExplorerContextMenu()
    {
        var menu = new ContextMenu();
        var replace = new MenuItem
        {
            Header = UiStrings.LibraryMenuReplacePlaylist,
            InputGestureText = "Enter",
        };
        replace.Click += (_, _) => ReplacePlaylistFromSelection();
        var append = new MenuItem
        {
            Header = UiStrings.LibraryMenuAppendPlaylist,
            InputGestureText = "Shift+Enter",
        };
        append.Click += (_, _) => OpenSelectedFolder(recursive: false);
        var replaceRecursive = new MenuItem
        {
            Header = UiStrings.LibraryMenuReplacePlaylistRecursive,
            InputGestureText = "Ctrl+Enter",
        };
        replaceRecursive.Click += (_, _) => ReplacePlaylistFromSelection(recursive: true);
        var appendRecursive = new MenuItem
        {
            Header = UiStrings.LibraryMenuAppendPlaylistRecursive,
            InputGestureText = "Ctrl+Shift+Enter",
        };
        appendRecursive.Click += (_, _) => OpenSelectedFolder(recursive: true);
        var add = new MenuItem { Header = UiStrings.LibraryMenuAddToFavorites };
        add.Click += (_, _) => AddSelectedExplorerToFavorites();
        var addRoot = new MenuItem { Header = UiStrings.LibraryMenuAddAsExplorerRoot };
        addRoot.Click += (_, _) => AddSelectedExplorerToRoots();
        var removeRoot = new MenuItem { Header = UiStrings.LibraryMenuRemoveExplorerRoot };
        removeRoot.Click += (_, _) => RemoveSelectedExplorerRoots();
        var expand = new MenuItem
        {
            Header = UiStrings.LibraryMenuExpandSubtree,
            InputGestureText = MenuGestures.ExplorerExpand,
        };
        expand.Click += (_, _) => ExpandSelectedExplorerSubtree();
        var collapse = new MenuItem
        {
            Header = UiStrings.LibraryMenuCollapseSubtree,
            InputGestureText = MenuGestures.ExplorerCollapse,
        };
        collapse.Click += (_, _) => CollapseSelectedExplorerSubtree();
        var moveUp = new MenuItem
        {
            Header = UiStrings.LibraryMenuMoveRootUp,
            InputGestureText = "Alt+Up",
        };
        moveUp.Click += (_, _) => TryMoveSelectedExplorerRoots(-1);
        var moveDown = new MenuItem
        {
            Header = UiStrings.LibraryMenuMoveRootDown,
            InputGestureText = "Alt+Down",
        };
        moveDown.Click += (_, _) => TryMoveSelectedExplorerRoots(1);
        var refresh = new MenuItem
        {
            Header = UiStrings.LibraryMenuRefreshExplorer,
            InputGestureText = "F5",
        };
        refresh.Click += (_, _) =>
        {
            RefreshExplorer();
            FocusExplorer();
        };
        var rebuild = new MenuItem
        {
            Header = UiStrings.LibraryMenuRebuildCatalog,
            InputGestureText = "Ctrl+F5",
        };
        rebuild.Click += (_, _) => LibraryCatalogRebuildRequested?.Invoke(this, EventArgs.Empty);
        AddCopyAndExplorerMenuItems(
            LibraryPane.Explorer,
            out var copy,
            out var cut,
            out var paste,
            out var rename,
            out var delete,
            out var pitchUp,
            out var pitchDown,
            out var pitchReset,
            out var copyNames,
            out var copyPaths,
            out var explorer);
        // 並びはウィンドウメニューに寄せる（編集→再生）。プレイリスト／ツリーはライブラリ固有。
        var playlist = Submenu(UiStrings.LibraryMenuCatPlaylist, replace, append, replaceRecursive, appendRecursive);
        var edit = Submenu(UiStrings.LibraryMenuCatEdit, copy, cut, paste, rename, delete);
        var playback = Submenu(UiStrings.LibraryMenuCatPitch, pitchUp, pitchDown, pitchReset);
        var tree = Submenu(
            UiStrings.LibraryMenuCatTree,
            expand,
            collapse,
            add,
            new Separator(),
            moveUp,
            moveDown,
            addRoot,
            removeRoot);
        var other = Submenu(
            UiStrings.LibraryMenuCatOther,
            refresh,
            rebuild,
            new Separator(),
            copyNames,
            copyPaths,
            explorer);
        menu.Items.Add(playlist);
        menu.Items.Add(edit);
        menu.Items.Add(playback);
        menu.Items.Add(tree);
        menu.Items.Add(other);
        menu.Opened += (_, _) =>
        {
            var selected = SelectedExplorerFolders.Length > 0;
            var transfer = SelectedExplorerTransferPaths();
            var existing = LibraryShellFiles.ExistingPaths(SelectedExplorerFolders);
            // 欠損だけのときは追加／クリアして追加を無効（Enter と同様スルー）。
            replace.IsEnabled = existing.Length > 0;
            append.IsEnabled = existing.Length > 0;
            replaceRecursive.IsEnabled = existing.Length > 0;
            appendRecursive.IsEnabled = existing.Length > 0;
            add.IsEnabled = selected;
            expand.IsEnabled = selected;
            collapse.IsEnabled = selected;
            var canMoveRoot = selected && SelectionContainsExplorerRoot() && _explorerRoots.Length >= 2;
            moveUp.IsEnabled = canMoveRoot;
            moveDown.IsEnabled = canMoveRoot;
            addRoot.IsEnabled = selected && SelectionCanRegisterExplorerRoot();
            removeRoot.IsEnabled = selected && SelectionContainsExplorerRoot();
            copy.IsEnabled = transfer.Length > 0;
            cut.IsEnabled = transfer.Length > 0;
            paste.IsEnabled = true;
            rename.IsEnabled = selected;
            delete.IsEnabled = SelectionCanRegisterExplorerRoot();
            pitchUp.IsEnabled = true;
            pitchDown.IsEnabled = true;
            pitchReset.IsEnabled = true;
            copyNames.IsEnabled = transfer.Length > 0;
            copyPaths.IsEnabled = transfer.Length > 0;
            explorer.IsEnabled = existing.Length > 0;
            SyncSubmenuEnabled(playlist, edit, playback, tree, other);
        };
        ArmContextMenuDismissGuard(menu);
        _folderTree.ContextMenu = menu;
    }

    private void RebuildFavoritesContextMenu()
    {
        var menu = new ContextMenu();
        var replace = new MenuItem
        {
            Header = UiStrings.LibraryMenuReplacePlaylist,
            InputGestureText = "Enter",
        };
        replace.Click += (_, _) => ActivateSelectedFavorites(clearPlaylist: true, recursive: false);
        var append = new MenuItem
        {
            Header = UiStrings.LibraryMenuAppendPlaylist,
            InputGestureText = "Shift+Enter",
        };
        append.Click += (_, _) => ActivateSelectedFavorites(clearPlaylist: false, recursive: false);
        var replaceRecursive = new MenuItem
        {
            Header = UiStrings.LibraryMenuReplacePlaylistRecursive,
            InputGestureText = "Ctrl+Enter",
        };
        replaceRecursive.Click += (_, _) => ActivateSelectedFavorites(clearPlaylist: true, recursive: true);
        var appendRecursive = new MenuItem
        {
            Header = UiStrings.LibraryMenuAppendPlaylistRecursive,
            InputGestureText = "Ctrl+Shift+Enter",
        };
        appendRecursive.Click += (_, _) => ActivateSelectedFavorites(clearPlaylist: false, recursive: true);
        var remove = new MenuItem { Header = UiStrings.LibraryMenuRemoveFromFavorites };
        remove.Click += (_, _) => RemoveSelectedFavorites();
        AddCopyAndExplorerMenuItems(
            LibraryPane.Favorites,
            out var copy,
            out var cut,
            out var paste,
            out var rename,
            out var delete,
            out var pitchUp,
            out var pitchDown,
            out var pitchReset,
            out var copyNames,
            out var copyPaths,
            out var explorer);
        var playlist = Submenu(UiStrings.LibraryMenuCatPlaylist, replace, append, replaceRecursive, appendRecursive);
        var edit = Submenu(
            UiStrings.LibraryMenuCatEdit,
            rename,
            remove,
            new Separator(),
            copy,
            cut,
            paste,
            delete);
        var playback = Submenu(UiStrings.LibraryMenuCatPitch, pitchUp, pitchDown, pitchReset);
        var other = Submenu(UiStrings.LibraryMenuCatOther, copyNames, copyPaths, explorer);
        menu.Items.Add(playlist);
        menu.Items.Add(edit);
        menu.Items.Add(playback);
        menu.Items.Add(other);
        menu.Opened += (_, _) =>
        {
            var paths = LibraryShellFiles.ExistingPaths(SelectedFavoritePaths);
            var selected = SelectedFavoritePaths.Length > 0;
            replace.IsEnabled = paths.Length > 0;
            append.IsEnabled = paths.Length > 0;
            replaceRecursive.IsEnabled = paths.Length > 0;
            appendRecursive.IsEnabled = paths.Length > 0;
            remove.IsEnabled = selected;
            copy.IsEnabled = false;
            cut.IsEnabled = false;
            paste.IsEnabled = false;
            rename.IsEnabled = selected;
            delete.IsEnabled = false;
            pitchUp.IsEnabled = true;
            pitchDown.IsEnabled = true;
            pitchReset.IsEnabled = true;
            copyNames.IsEnabled = paths.Length > 0;
            copyPaths.IsEnabled = paths.Length > 0;
            explorer.IsEnabled = paths.Length > 0;
            SyncSubmenuEnabled(playlist, edit, playback, other);
        };
        ArmContextMenuDismissGuard(menu);
        _favoritesList.ContextMenu = menu;
    }

    private void RebuildListContextMenu()
    {
        var menu = new ContextMenu();
        menu.Opened += (_, _) =>
        {
            // メニュー表示中に残った plain-press を捨て、閉じたあとの MouseUp で単一選択確定しない。
            AbandonPlaylistPendingClick();
            ReleasePlaylistMouseCapture();
            _suppressPlaylistPlainPressComplete = true;
            // Closed より前に Idle が回っても、解除時に ListContextMenuClosed を必ず飛ばす。
            _listContextMenuDismissPending = true;
            ListContextMenuOpened?.Invoke(this, EventArgs.Empty);
            RebuildListContextMenuItems(menu);
        };
        menu.Closed += (_, _) =>
        {
            AbandonPlaylistPendingClick();
            _suppressPlaylistPlainPressComplete = true;
            // ListContextMenuClosed（＝_listContextMenuOpen 解除）と plain-press 抑制解除は
            // dismiss ガード解除と同じタイミング（Idle かつ左ボタン解放）まで遅らせる。
            _listContextMenuDismissPending = true;
            // Closed 同期パスでのキャプチャ解放は Dispatcher 再入の原因になるので遅延する。
            Dispatcher.BeginInvoke(ReleasePlaylistMouseCapture, DispatcherPriority.Input);
        };
        ArmContextMenuDismissGuard(menu);
        // 初回表示前にも中身を入れておく（キーボードで開くテスト／最初の Opened 前の検査用）。
        RebuildListContextMenuItems(menu);
        _grid.ContextMenu = menu;
    }

    /// <summary>
    /// ウィンドウメニューと同じ <see cref="WaveformContextMenuBuilder.Build"/> ツリー。
    /// 先頭にカラー、編集／その他へプレイリスト固有項目を足す。実行不可カテゴリは非表示にせずグレーアウト。
    /// </summary>
    private void RebuildListContextMenuItems(ContextMenu menu)
    {
        var color = FileNameColorMarkerMenu.Create(
            SelectedColorPaths,
            chosen => SetManualFileNameColor(SelectedColorPaths(), chosen));
        color.IsEnabled = SelectedColorPaths().Length > 0;

        var paths = SelectedPlaylistCopyPaths();
        var hasSelection = SelectedSessions.Length > 0;
        AddCopyAndExplorerMenuItems(
            LibraryPane.List,
            out var copy,
            out var cut,
            out var paste,
            out var rename,
            out var delete,
            out _,
            out _,
            out _,
            out var copyNames,
            out var copyPaths,
            out var explorer);
        copy.IsEnabled = paths.Length > 0;
        cut.IsEnabled = paths.Length > 0;
        paste.IsEnabled = true;
        rename.IsEnabled = hasSelection;
        delete.IsEnabled = hasSelection;
        copyNames.IsEnabled = paths.Length > 0;
        copyPaths.IsEnabled = paths.Length > 0;
        explorer.IsEnabled = paths.Length > 0;
        var clear = new MenuItem
        {
            Header = UiStrings.LibraryMenuClearFromPlaylist,
            InputGestureText = MenuGestures.Delete,
            IsEnabled = hasSelection,
        };
        ArmMenuItemClick(clear, () => ClearPlaylistRequested?.Invoke(this, EventArgs.Empty));

        var entries = WaveformContextMenuBuilder.Build(ResolveListWaveMenuModel(), includeHere: false);
        menu.Items.Clear();
        menu.Items.Add(color);
        WaveformContextMenuBuilder.Append(
            menu.Items,
            entries,
            command => PlaylistWaveMenuRequested?.Invoke(this, command));

        // ライブラリ固有のシェル操作・クリアはウィンドウ編集／その他の末尾へ（同名の無効項目の下）。
        if (FindMenuCategory(menu, UiStrings.WaveMenuCatEdit) is { } edit)
        {
            edit.Items.Add(new Separator());
            edit.Items.Add(copy);
            edit.Items.Add(cut);
            edit.Items.Add(paste);
            edit.Items.Add(rename);
            edit.Items.Add(delete);
            edit.Items.Add(new Separator());
            edit.Items.Add(clear);
            SyncSubmenuEnabled(edit);
        }

        if (FindMenuCategory(menu, UiStrings.WaveMenuCatOther) is { } other)
        {
            other.Items.Add(new Separator());
            other.Items.Add(copyNames);
            other.Items.Add(copyPaths);
            other.Items.Add(explorer);
            SyncSubmenuEnabled(other);
        }
    }

    private WaveformContextMenuModel ResolveListWaveMenuModel()
    {
        if (ResolvePlaylistWaveMenuModel is { } resolve)
        {
            return resolve();
        }

        // 単体テスト等、ホスト未接続時のプレイヤー相当。
        var selected = SelectedSessions;
        var hasRows = _rows.Count > 0;
        var hasSelection = selected.Length > 0;
        var hasMarkers = false;
        var hasRegions = false;
        var hasLoop = false;
        foreach (var session in selected)
        {
            var document = session.Document;
            if (document.Markers.Count > 0)
            {
                hasMarkers = true;
            }

            if (document.AllowsRegionsAndLoops && document.Regions.Count > 0)
            {
                hasRegions = true;
            }

            if (!document.SampleLoop.IsEmpty)
            {
                hasLoop = true;
            }
        }

        return new WaveformContextMenuModel
        {
            LibraryMaximized = true,
            HasDocument = hasSelection || hasRows,
            HasSelectedTabs = hasSelection,
            HasMultipleSelectedTabs = selected.Length >= 2,
            SelectedTabsHaveMarkers = hasMarkers,
            SelectedTabsHaveRegions = hasRegions,
            SelectedTabsHaveSampleLoop = hasLoop,
            CanStepPlaylist = hasRows,
            CanRestartTrack = hasRows,
            AutoPlay = AutoPlayEnabled,
            AutoPlayNext = AutoPlayNextEnabled,
            Shuffle = ShuffleEnabled,
            CanExportMp3 = LibraryPlayerMode.HasMp3ExportCandidate(selected),
            CanMergeTabs = selected.Length >= 2,
            CanNavigate = hasSelection,
            CanUndo = hasSelection,
            CanRedo = hasSelection,
        };
    }

    private static MenuItem? FindMenuCategory(ContextMenu menu, string header)
    {
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (item.Header as string == header)
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>
    /// メニューの外側クリック dismiss が下のコントロールへ貫通するぶんのガードを張る。
    /// 開いてから、閉じた同じ左クリックが上がるまで（少なくとも一度 ApplicationIdle を経てから）
    /// true を維持する。波形メニューなど、このビューの外で作ったメニューも同じ貫通を止める。
    /// </summary>
    internal void ArmContextMenuDismissGuard(ContextMenu menu)
    {
        menu.Opened += (_, _) =>
        {
            _contextMenuDismissGuard = true;
        };
        menu.Closed += (_, _) =>
        {
            _contextMenuDismissGuard = true;
            var epoch = ++_contextMenuDismissReleaseEpoch;
            Dispatcher.BeginInvoke(
                () => ContinueContextMenuDismissGuardRelease(epoch),
                DispatcherPriority.ApplicationIdle);
        };
    }

    /// <summary>
    /// Closed 後のガード解除。ほかのメニューが開いているあいだ、および同じ dismiss の
    /// 左ボタンが押されているあいだは残す（Idle だけでは外さない）。
    /// </summary>
    private void ContinueContextMenuDismissGuardRelease(int epoch)
    {
        if (epoch != _contextMenuDismissReleaseEpoch)
        {
            return;
        }

        if (HasOpenContextMenu || IsDismissGuardLeftHeld())
        {
            EnsureContextMenuDismissLeftUpWatcher(epoch);
            return;
        }

        StopContextMenuDismissLeftUpWatcher();
        _contextMenuDismissGuard = false;
        // plain-press 抑制はガードと同じ寿命。pending の有無に関わらずここで下ろす。
        _suppressPlaylistPlainPressComplete = false;

        if (!_listContextMenuDismissPending)
        {
            return;
        }

        _listContextMenuDismissPending = false;
        ListContextMenuClosed?.Invoke(this, EventArgs.Empty);
    }

    private bool IsDismissGuardLeftHeld() =>
        DismissGuardLeftHeldForTests?.Invoke() ?? Mouse.LeftButton == MouseButtonState.Pressed;

    private void EnsureContextMenuDismissLeftUpWatcher(int epoch)
    {
        _contextMenuDismissWatchEpoch = epoch;
        if (_contextMenuDismissLeftUpWatching)
        {
            return;
        }

        _contextMenuDismissLeftUpWatching = true;
        _contextMenuDismissLeftUpTimer.Start();
    }

    private void StopContextMenuDismissLeftUpWatcher()
    {
        if (!_contextMenuDismissLeftUpWatching)
        {
            return;
        }

        _contextMenuDismissLeftUpWatching = false;
        _contextMenuDismissLeftUpTimer.Stop();
    }

    /// <summary>テスト専用。保留中の dismiss ガード解除をいまの左ボタン状態で再評価する。</summary>
    internal void FlushContextMenuDismissGuardForTests() =>
        ContinueContextMenuDismissGuardRelease(_contextMenuDismissReleaseEpoch);

    private static void ArmMenuItemClick(MenuItem item, Action action)
    {
        var armed = false;
        item.PreviewMouseLeftButtonDown += (_, _) => armed = true;
        item.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space)
            {
                armed = true;
            }
        };
        item.Click += (_, _) =>
        {
            if (!armed)
            {
                return;
            }

            armed = false;
            action();
        };
    }

    private void RebuildColumnHeaderContextMenu()
    {
        _columnHeaderMenu = CreateColumnHeaderContextMenu();
        ApplyGridStyles();
    }

    private ContextMenu CreateColumnHeaderContextMenu()
    {
        var menu = new ContextMenu();
        var enabled = new HashSet<LibraryFileColumn>(_visibleColumns);
        foreach (var column in LibraryColumnFilter.All)
        {
            if (LibraryColumnFilter.IsLocked(column))
            {
                continue;
            }

            var item = new MenuItem
            {
                // 列見出しの改行（ビット\nレート等）はメニューでは使わない。
                Header = UiStrings.LibraryColumnLabel(column),
                IsCheckable = true,
                IsChecked = enabled.Contains(column),
                StaysOpenOnClick = true,
                Tag = column,
            };
            item.Click += ColumnHeaderMenu_Toggle;
            menu.Items.Add(item);
        }

        menu.Opened += (_, _) => SyncColumnHeaderMenuChecks();
        ArmContextMenuDismissGuard(menu);
        return menu;
    }

    private void SyncColumnHeaderMenuChecks()
    {
        if (_columnHeaderMenu is null)
        {
            return;
        }

        var enabled = new HashSet<LibraryFileColumn>(_visibleColumns);
        foreach (var item in _columnHeaderMenu.Items.OfType<MenuItem>())
        {
            if (item.Tag is not LibraryFileColumn column)
            {
                continue;
            }

            item.IsChecked = enabled.Contains(column);
        }
    }

    private void ColumnHeaderMenu_Toggle(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: LibraryFileColumn column } item
            || LibraryColumnFilter.IsLocked(column))
        {
            return;
        }

        var selected = new HashSet<LibraryFileColumn>(_visibleColumns) { LibraryFileColumn.Name };
        if (item.IsChecked == true)
        {
            selected.Add(column);
        }
        else
        {
            selected.Remove(column);
        }

        ApplyColumnVisibility(LibraryColumnFilter.Merge(_visibleColumns, selected), notify: true);
        SyncColumnHeaderMenuChecks();
    }

    private static MenuItem Submenu(string header, params object[] children)
    {
        var item = new MenuItem { Header = header };
        foreach (var child in children)
        {
            item.Items.Add(child);
        }

        return item;
    }

    /// <summary>実行できる子が一つもないカテゴリはグレーアウトする。</summary>
    private static void SyncSubmenuEnabled(params MenuItem[] submenus)
    {
        foreach (var submenu in submenus)
        {
            var any = false;
            foreach (var child in submenu.Items)
            {
                if (child is MenuItem { IsEnabled: true })
                {
                    any = true;
                    break;
                }
            }

            submenu.IsEnabled = any;
        }
    }

    private void AddCopyAndExplorerMenuItems(
        LibraryPane pane,
        out MenuItem copy,
        out MenuItem cut,
        out MenuItem paste,
        out MenuItem rename,
        out MenuItem delete,
        out MenuItem pitchUp,
        out MenuItem pitchDown,
        out MenuItem pitchReset,
        out MenuItem copyNames,
        out MenuItem copyPaths,
        out MenuItem explorer)
    {
        copy = new MenuItem
        {
            Header = UiStrings.LibraryMenuCopy,
            InputGestureText = "Ctrl+C",
        };
        copy.Click += (_, _) => CopyPaths(CopyPathsFor(pane));
        cut = new MenuItem
        {
            Header = UiStrings.LibraryMenuCut,
            InputGestureText = "Ctrl+X",
        };
        cut.Click += (_, _) => CutPaths(pane);
        paste = new MenuItem
        {
            Header = UiStrings.LibraryMenuPaste,
            InputGestureText = "Ctrl+V",
        };
        paste.Click += (_, _) => LibraryPasteRequested?.Invoke(this, pane);
        rename = new MenuItem
        {
            Header = UiStrings.LibraryMenuRename,
            InputGestureText = "F2",
        };
        rename.Click += (_, _) => LibraryRenameRequested?.Invoke(this, pane);
        delete = new MenuItem
        {
            Header = UiStrings.LibraryMenuDelete,
            InputGestureText = "Ctrl+Delete",
        };
        delete.Click += (_, _) => LibraryDeleteRequested?.Invoke(this, pane);
        pitchUp = new MenuItem
        {
            Header = UiStrings.LibraryMenuPitchUp,
            InputGestureText = MenuGestures.PitchUp,
        };
        pitchUp.Click += (_, _) => PlaybackPitchUpRequested?.Invoke(this, EventArgs.Empty);
        pitchDown = new MenuItem
        {
            Header = UiStrings.LibraryMenuPitchDown,
            InputGestureText = MenuGestures.PitchDown,
        };
        pitchDown.Click += (_, _) => PlaybackPitchDownRequested?.Invoke(this, EventArgs.Empty);
        pitchReset = new MenuItem
        {
            Header = UiStrings.LibraryMenuPitchReset,
            InputGestureText = MenuGestures.PitchReset,
        };
        pitchReset.Click += (_, _) => PlaybackPitchResetRequested?.Invoke(this, EventArgs.Empty);
        copyNames = new MenuItem { Header = UiStrings.LibraryMenuCopyFileName };
        copyNames.Click += (_, _) => CopyPathTexts(CopyPathsFor(pane), fullPath: false);
        copyPaths = new MenuItem { Header = UiStrings.LibraryMenuCopyFilePath };
        copyPaths.Click += (_, _) => CopyPathTexts(CopyPathsFor(pane), fullPath: true);
        explorer = new MenuItem { Header = UiStrings.LibraryMenuOpenInExplorer };
        explorer.Click += (_, _) => LibraryShellFiles.TryOpenInExplorer(RevealPathsFor(pane));
    }

}
