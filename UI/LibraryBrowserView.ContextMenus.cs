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
        var playlist = Submenu(UiStrings.LibraryMenuCatPlaylist, replace, append, replaceRecursive, appendRecursive);
        var edit = Submenu(UiStrings.LibraryMenuCatEdit, copy, cut, paste, rename, delete);
        var pitch = Submenu(UiStrings.LibraryMenuCatPitch, pitchUp, pitchDown, pitchReset);
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
        menu.Items.Add(pitch);
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
            SyncSubmenuEnabled(playlist, edit, pitch, tree, other);
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
        var pitch = Submenu(UiStrings.LibraryMenuCatPitch, pitchUp, pitchDown, pitchReset);
        var other = Submenu(UiStrings.LibraryMenuCatOther, copyNames, copyPaths, explorer);
        menu.Items.Add(playlist);
        menu.Items.Add(edit);
        menu.Items.Add(pitch);
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
            SyncSubmenuEnabled(playlist, edit, pitch, other);
        };
        ArmContextMenuDismissGuard(menu);
        _favoritesList.ContextMenu = menu;
    }

    private void RebuildListContextMenu()
    {
        var menu = new ContextMenu();
        var loudness = new MenuItem
        {
            Header = UiStrings.LibraryMenuMatchLoudness,
            // プレイヤーでは V は破壊編集キーとして握りつぶすため表記しない。
            InputGestureText = string.Empty,
        };
        ArmMenuItemClick(loudness, () => MatchLoudnessRequested?.Invoke(this, EventArgs.Empty));
        var previous = new MenuItem
        {
            Header = UiStrings.LibraryMenuPreviousTrack,
            InputGestureText = MenuGestures.PreviousTrack,
        };
        ArmMenuItemClick(previous, () => PlaylistPreviousRequested?.Invoke(this, EventArgs.Empty));
        var next = new MenuItem
        {
            Header = UiStrings.LibraryMenuNextTrack,
            InputGestureText = MenuGestures.NextTrack,
        };
        ArmMenuItemClick(next, () => PlaylistNextRequested?.Invoke(this, EventArgs.Empty));
        var restart = new MenuItem
        {
            Header = UiStrings.LibraryMenuRestartTrack,
            InputGestureText = MenuGestures.RestartTrack,
        };
        ArmMenuItemClick(restart, () => PlaylistRestartRequested?.Invoke(this, EventArgs.Empty));
        var autoPlay = new MenuItem
        {
            Header = UiStrings.LibraryMenuAutoPlay,
            InputGestureText = "Alt+P",
            IsCheckable = true,
        };
        ArmMenuItemClick(autoPlay, () => AutoPlayEnabled = autoPlay.IsChecked == true);
        var clear = new MenuItem
        {
            Header = UiStrings.LibraryMenuClearFromPlaylist,
            InputGestureText = MenuGestures.Delete,
        };
        ArmMenuItemClick(clear, () => ClearPlaylistRequested?.Invoke(this, EventArgs.Empty));
        AddCopyAndExplorerMenuItems(
            LibraryPane.List,
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
        var pitch = Submenu(
            UiStrings.LibraryMenuCatPitch,
            previous,
            next,
            restart,
            autoPlay,
            new Separator(),
            pitchUp,
            pitchDown,
            pitchReset);
        var edit = Submenu(
            UiStrings.LibraryMenuCatEdit,
            copy,
            cut,
            paste,
            rename,
            delete,
            new Separator(),
            clear);
        var audioItems = CreatePlaylistWaveMenuItems(
            (WaveMenuCommand.Undo, "Ctrl+Z"),
            (WaveMenuCommand.Redo, "Ctrl+Y"),
            default,
            (WaveMenuCommand.FadeIn, "I"),
            (WaveMenuCommand.FadeOut, "O"),
            default,
            (WaveMenuCommand.Normalize, "N"),
            (WaveMenuCommand.NormalizePerRegion, null),
            default,
            (WaveMenuCommand.Pitch, "P"),
            (WaveMenuCommand.TimeStretch, "T"),
            (WaveMenuCommand.Reverse, "R"),
            default,
            (WaveMenuCommand.InsertSilence, "Ctrl+Ins"),
            (WaveMenuCommand.DetectSilence, "Ctrl+D"),
            default,
            (WaveMenuCommand.DeleteAllMarkers, null),
            (WaveMenuCommand.DeleteAllRegions, null),
            (WaveMenuCommand.ClearLoopItem, null));
        var formatItems = CreatePlaylistWaveMenuItems(
            (WaveMenuCommand.SampleRate, "S"),
            (WaveMenuCommand.BitDepth, "B"),
            (WaveMenuCommand.Channels, "C"));
        var exportItems = CreatePlaylistWaveMenuItems(
            (WaveMenuCommand.ExportWave, null),
            (WaveMenuCommand.ExportMp3, "Ctrl+Shift+M"),
            (WaveMenuCommand.MergeTabs, "Ctrl+Shift+B"),
            default,
            (WaveMenuCommand.ExportByMarkers, null),
            (WaveMenuCommand.ExportByRegions, null),
            (WaveMenuCommand.ExportByChannels, null));
        var audio = Submenu(UiStrings.LibraryMenuCatAudio, audioItems);
        var format = Submenu(UiStrings.LibraryMenuCatFormat, formatItems);
        var exportChildren = new List<object> { loudness, new Separator() };
        exportChildren.AddRange(exportItems);
        var export = Submenu(UiStrings.LibraryMenuCatExport, exportChildren.ToArray());
        var other = Submenu(
            UiStrings.LibraryMenuCatOther,
            copyNames,
            copyPaths,
            explorer);
        var color = FileNameColorMarkerMenu.Create(
            SelectedColorPaths,
            chosen => SetManualFileNameColor(SelectedColorPaths(), chosen));
        menu.Items.Add(color);
        menu.Items.Add(pitch);
        menu.Items.Add(edit);
        menu.Items.Add(audio);
        menu.Items.Add(format);
        menu.Items.Add(export);
        menu.Items.Add(other);
        menu.Opened += (_, _) =>
        {
            // メニュー表示中に残った plain-press を捨て、閉じたあとの MouseUp で単一選択確定しない。
            AbandonPlaylistPendingClick();
            ReleasePlaylistMouseCapture();
            _suppressPlaylistPlainPressComplete = true;
            // Closed より前に Idle が回っても、解除時に ListContextMenuClosed を必ず飛ばす。
            _listContextMenuDismissPending = true;
            ListContextMenuOpened?.Invoke(this, EventArgs.Empty);
            var paths = SelectedPlaylistCopyPaths();
            var selected = SelectedSessions;
            var hasSelection = selected.Length > 0;
            var many = selected.Length >= 2;
            loudness.IsEnabled = hasSelection;
            var hasRows = _rows.Count > 0;
            previous.IsEnabled = hasRows;
            next.IsEnabled = hasRows;
            restart.IsEnabled = hasRows;
            autoPlay.IsChecked = AutoPlayEnabled;
            autoPlay.IsEnabled = true;
            clear.IsEnabled = hasSelection;
            copy.IsEnabled = paths.Length > 0;
            cut.IsEnabled = paths.Length > 0;
            paste.IsEnabled = true;
            rename.IsEnabled = hasSelection;
            delete.IsEnabled = hasSelection;
            pitchUp.IsEnabled = true;
            pitchDown.IsEnabled = true;
            pitchReset.IsEnabled = true;
            copyNames.IsEnabled = paths.Length > 0;
            copyPaths.IsEnabled = paths.Length > 0;
            explorer.IsEnabled = paths.Length > 0;
            color.IsEnabled = SelectedColorPaths().Length > 0;
            SyncPlaylistWaveMenuHeaders(audioItems, many);
            SyncPlaylistWaveMenuHeaders(formatItems, many);
            SyncPlaylistWaveMenuHeaders(exportItems, many);
            SyncPlaylistWaveMenuEnabled(audioItems, selected);
            SyncPlaylistWaveMenuEnabled(formatItems, selected);
            SyncPlaylistWaveMenuEnabled(exportItems, selected);
            SyncSubmenuEnabled(pitch, edit, audio, format, export, other);
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
        _grid.ContextMenu = menu;
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

    /// <summary>
    /// メニュー外クリックの dismiss と MenuItem.Click が競合しないよう、
    /// 項目上でのマウス／Enter／Space だけをコマンド実行とみなす。
    /// </summary>
    private object[] CreatePlaylistWaveMenuItems(params (WaveMenuCommand? Command, string? Gesture)[] entries)
    {
        var items = new List<object>(entries.Length);
        foreach (var (command, gesture) in entries)
        {
            if (command is null)
            {
                items.Add(new Separator());
                continue;
            }

            var cmd = command.Value;
            var item = new MenuItem
            {
                Tag = cmd,
                Header = PlaylistWaveMenuHeader(cmd, many: false),
                // プレイヤー専用メニュー。破壊編集ショートカットはキー無効のため表記しない。
                InputGestureText = LibraryPlayerMode.MenuGesture(
                    playerMode: true,
                    cmd,
                    gesture) ?? string.Empty,
            };
            ArmMenuItemClick(item, () => PlaylistWaveMenuRequested?.Invoke(this, cmd));
            items.Add(item);
        }

        return items.ToArray();
    }

    private static void SyncPlaylistWaveMenuHeaders(object[] items, bool many)
    {
        foreach (var entry in items)
        {
            if (entry is MenuItem { Tag: WaveMenuCommand command } item)
            {
                item.Header = PlaylistWaveMenuHeader(command, many);
            }
        }
    }

    private static void SyncPlaylistWaveMenuEnabled(object[] items, DocumentSession[] selected)
    {
        var hasSelection = selected.Length > 0;
        var many = selected.Length >= 2;
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

        foreach (var entry in items)
        {
            if (entry is not MenuItem { Tag: WaveMenuCommand command } item)
            {
                continue;
            }

            item.IsEnabled = command switch
            {
                WaveMenuCommand.ExportMp3 => LibraryPlayerMode.HasMp3ExportCandidate(selected),
                WaveMenuCommand.MergeTabs => many,
                WaveMenuCommand.ExportByMarkers => hasSelection && hasMarkers,
                WaveMenuCommand.ExportByRegions => hasSelection && hasRegions,
                WaveMenuCommand.NormalizePerRegion => hasSelection && hasRegions,
                WaveMenuCommand.DeleteAllRegions => hasSelection && hasRegions,
                WaveMenuCommand.ClearLoopItem => hasSelection && hasLoop,
                WaveMenuCommand.DeleteAllMarkers => hasSelection && hasMarkers,
                _ => hasSelection,
            };
        }
    }

    private static string PlaylistWaveMenuHeader(WaveMenuCommand command, bool many) => command switch
    {
        WaveMenuCommand.Undo => many ? UiStrings.WaveMenuUndoSelected : UiStrings.WaveMenuUndo,
        WaveMenuCommand.Redo => many ? UiStrings.WaveMenuRedoSelected : UiStrings.WaveMenuRedo,
        WaveMenuCommand.FadeIn => many ? UiStrings.WaveMenuFadeInSelected : UiStrings.WaveMenuFadeIn,
        WaveMenuCommand.FadeOut => many ? UiStrings.WaveMenuFadeOutSelected : UiStrings.WaveMenuFadeOut,
        WaveMenuCommand.Normalize => many ? UiStrings.WaveMenuNormalizeSelected : UiStrings.WaveMenuNormalize,
        WaveMenuCommand.NormalizePerRegion => many
            ? UiStrings.WaveMenuNormalizePerRegionSelected
            : UiStrings.WaveMenuNormalizePerRegion,
        WaveMenuCommand.Pitch => many ? UiStrings.WaveMenuPitchSelected : UiStrings.WaveMenuPitch,
        WaveMenuCommand.TimeStretch => many ? UiStrings.WaveMenuTimeStretchSelected : UiStrings.WaveMenuTimeStretch,
        WaveMenuCommand.Reverse => many ? UiStrings.WaveMenuReverseSelected : UiStrings.WaveMenuReverse,
        WaveMenuCommand.InsertSilence => many
            ? UiStrings.WaveMenuInsertSilenceSelected
            : UiStrings.WaveMenuInsertSilence,
        WaveMenuCommand.DetectSilence => many
            ? UiStrings.WaveMenuDetectSilenceSelected
            : UiStrings.WaveMenuDetectSilence,
        WaveMenuCommand.SampleRate => many ? UiStrings.WaveMenuSampleRateSelected : UiStrings.WaveMenuSampleRate,
        WaveMenuCommand.BitDepth => many ? UiStrings.WaveMenuBitDepthSelected : UiStrings.WaveMenuBitDepth,
        WaveMenuCommand.Channels => many ? UiStrings.WaveMenuChannelsSelected : UiStrings.WaveMenuChannels,
        WaveMenuCommand.ExportWave => many ? UiStrings.WaveMenuExportWaveSelected : UiStrings.WaveMenuExportWave,
        WaveMenuCommand.ExportMp3 => many ? UiStrings.WaveMenuExportMp3Selected : UiStrings.WaveMenuExportMp3,
        WaveMenuCommand.MergeTabs => UiStrings.WaveMenuMergeTabs,
        WaveMenuCommand.ExportByMarkers => many
            ? UiStrings.WaveMenuExportByMarkersSelected
            : UiStrings.WaveMenuExportByMarkers,
        WaveMenuCommand.ExportByRegions => many
            ? UiStrings.WaveMenuExportByRegionsSelected
            : UiStrings.WaveMenuExportByRegions,
        WaveMenuCommand.ExportByChannels => many
            ? UiStrings.WaveMenuExportByChannelsSelected
            : UiStrings.WaveMenuExportByChannels,
        WaveMenuCommand.DeleteAllMarkers => UiStrings.WaveMenuDeleteAllMarkers,
        WaveMenuCommand.DeleteAllRegions => UiStrings.WaveMenuDeleteAllRegions,
        WaveMenuCommand.ClearLoopItem => UiStrings.WaveMenuClearLoopItem,
        _ => command.ToString(),
    };

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
