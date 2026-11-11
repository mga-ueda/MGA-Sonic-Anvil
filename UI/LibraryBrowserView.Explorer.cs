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
    internal string ExplorerTypeaheadQuery => _explorerTypeahead;

    /// <summary>黄背景／↑↓が有効なセッション中か（トースト用バッファが空でも true）。</summary>
    internal bool IsExplorerTypeaheadActive => _explorerTypeaheadWalk.Length > 0;

    /// <summary>
    /// 打ち込みセッションを終わらせる（Esc・プレイリスト登録／再生・他 UI へのフォーカス）。
    /// 黄背景・渡り歩き・トーストをすべて消す。入力アイドルでのトースト消しとは別。
    /// </summary>
    public bool ClearExplorerTypeahead()
    {
        StopExplorerTypeaheadToastIdleTimer();
        ClearExplorerTypeaheadHighlights();
        _explorerTypeaheadIgnoreText = "";
        var had = _explorerTypeahead.Length > 0 || _explorerTypeaheadWalk.Length > 0;
        _explorerTypeahead = "";
        _explorerTypeaheadWalk = "";
        HideExplorerTypeaheadToast();
        return had;
    }

    public bool TryExplorerTypeahead(Key key, ModifierKeys modifiers)
    {
        if (!LibraryExplorerTypeahead.TryMapChar(key, modifiers, out var character))
        {
            return false;
        }

        _explorerTypeaheadIgnoreText = character;
        return TryAppendExplorerTypeahead(character);
    }

    /// <summary>黄背景セッション中の ↑↓。ヒット行だけを渡り歩く（トースト非表示中も可）。</summary>
    public bool TryStepExplorerTypeahead(int direction)
    {
        if (!IsExplorerTypeaheadActive || direction == 0)
        {
            return false;
        }

        return ActivateExplorerTypeaheadStep(direction);
    }

    private bool TryAppendExplorerTypeahead(string next)
    {
        if (!LibraryExplorerTypeahead.IsTypeaheadText(next))
        {
            return false;
        }

        _explorerTypeahead = LibraryExplorerTypeahead.Append(_explorerTypeahead, next);
        _explorerTypeaheadWalk = _explorerTypeahead;
        ActivateExplorerTypeaheadMatch();
        ShowExplorerTypeaheadToast();
        SyncExplorerTypeaheadHighlights();
        return true;
    }

    private void ShowExplorerTypeaheadToast()
    {
        if (_explorerTypeahead.Length == 0)
        {
            HideExplorerTypeaheadToast();
            return;
        }

        _explorerTypeaheadToast.ShowSticky(_explorerTypeahead);
        _explorerTypeaheadToastIdleTimer ??= new DispatcherTimer
        {
            Interval = LibraryExplorerTypeahead.ToastIdleClear,
        };
        _explorerTypeaheadToastIdleTimer.Tick -= OnExplorerTypeaheadToastIdleTick;
        _explorerTypeaheadToastIdleTimer.Tick += OnExplorerTypeaheadToastIdleTick;
        _explorerTypeaheadToastIdleTimer.Stop();
        _explorerTypeaheadToastIdleTimer.Start();
    }

    /// <summary>
    /// 入力が落ち着いて1秒後。トーストを消し、積み上がった打ち込み文字も捨てる。
    /// 黄背景と渡り歩き用クエリは残す。
    /// </summary>
    private void OnExplorerTypeaheadToastIdleTick(object? sender, EventArgs e)
    {
        StopExplorerTypeaheadToastIdleTimer();
        _explorerTypeahead = "";
        _explorerTypeaheadIgnoreText = "";
        HideExplorerTypeaheadToast();
    }

    private void StopExplorerTypeaheadToastIdleTimer()
    {
        _explorerTypeaheadToastIdleTimer?.Stop();
    }

    private void HideExplorerTypeaheadToast()
    {
        StopExplorerTypeaheadToastIdleTimer();
        _explorerTypeaheadToast.Hide();
    }

    private void ActivateExplorerTypeaheadMatch()
    {
        if (!TryCollectExplorerTypeahead(out var items, out var names, out var current))
        {
            return;
        }

        var match = LibraryExplorerTypeahead.FindMatchIndex(names, _explorerTypeaheadWalk, current);
        SelectExplorerTypeaheadItem(items, match);
    }

    private bool ActivateExplorerTypeaheadStep(int direction)
    {
        if (!TryCollectExplorerTypeahead(out var items, out var names, out var current))
        {
            return false;
        }

        var match = LibraryExplorerTypeahead.FindStepMatchIndex(
            names,
            _explorerTypeaheadWalk,
            current,
            direction);
        if (match < 0)
        {
            return false;
        }

        SelectExplorerTypeaheadItem(items, match);
        SyncExplorerTypeaheadHighlights();
        return true;
    }

    private void SyncExplorerTypeaheadHighlights()
    {
        ClearExplorerTypeaheadHighlights();
        if (!IsExplorerTypeaheadActive
            || !TryCollectExplorerTypeahead(out var items, out var names, out _))
        {
            return;
        }

        for (var i = 0; i < items.Count; i++)
        {
            if (!LibraryExplorerTypeahead.Matches(names[i], _explorerTypeaheadWalk))
            {
                continue;
            }

            if (ApplyExplorerTypeaheadHighlight(items[i], names[i], _explorerTypeaheadWalk))
            {
                _explorerTypeaheadHighlighted.Add(items[i]);
            }
        }
    }

    private void ClearExplorerTypeaheadHighlights()
    {
        if (_explorerTypeaheadHighlighted.Count == 0)
        {
            return;
        }

        foreach (var item in _explorerTypeaheadHighlighted)
        {
            RestoreExplorerTypeaheadHeader(item);
        }

        _explorerTypeaheadHighlighted.Clear();
    }

    private static bool ApplyExplorerTypeaheadHighlight(TreeViewItem item, string name, string query)
    {
        if (!LibraryExplorerTypeahead.TryFindMatchRange(name, query, out var start, out var length)
            || length <= 0
            || (uint)start >= (uint)name.Length
            || start + length > name.Length)
        {
            return false;
        }

        if (item.Header is not TextBlock block)
        {
            block = new TextBlock();
            item.Header = block;
        }

        var missing = item.Tag is string path && LibraryExplorerPaths.IsMissing(path);
        var brushKey = missing ? MissingRegisteredPathForeBrushKey : "PrimaryForeBrush";
        block.Inlines.Clear();
        block.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        if (start > 0)
        {
            block.Inlines.Add(new Run(name[..start]));
        }

        block.Inlines.Add(new Run(name.Substring(start, length))
        {
            Background = ExplorerTypeaheadHitBack,
            Foreground = ExplorerTypeaheadHitFore,
        });
        if (start + length < name.Length)
        {
            block.Inlines.Add(new Run(name[(start + length)..]));
        }

        item.SetResourceReference(Control.ForegroundProperty, brushKey);
        return true;
    }

    private void RestoreExplorerTypeaheadHeader(TreeViewItem item)
    {
        if (item.Tag is not string path)
        {
            return;
        }

        ApplyFolderHeader(item, FolderHeaderText(item), LibraryExplorerPaths.IsMissing(path));
    }

    private static SolidColorBrush CreateExplorerTypeaheadHitBack()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x4C));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush CreateExplorerTypeaheadHitFore()
    {
        var brush = new SolidColorBrush(Colors.Black);
        brush.Freeze();
        return brush;
    }

    private bool TryCollectExplorerTypeahead(
        out List<TreeViewItem> items,
        out string[] names,
        out int current)
    {
        items = [];
        CollectExplorerTypeaheadItems(_folderTree.Items, items);
        names = new string[items.Count];
        current = -1;
        if (items.Count == 0)
        {
            return false;
        }

        var selected = _folderTree.SelectedItem as TreeViewItem;
        for (var i = 0; i < items.Count; i++)
        {
            names[i] = FolderHeaderText(items[i]);
            if (ReferenceEquals(items[i], selected))
            {
                current = i;
            }
        }

        return true;
    }

    private void SelectExplorerTypeaheadItem(List<TreeViewItem> items, int match)
    {
        if ((uint)match >= (uint)items.Count)
        {
            return;
        }

        var item = items[match];
        ClearTreeMultiSelect();
        _treeSelectionAnchor = item;
        EnsureExplorerAncestorsExpanded(item);
        item.IsSelected = true;
        item.BringIntoView();
        item.Focus();
    }

    /// <summary>
    /// 画面上に見えている行だけ（祖先がすべて展開済み）。未展開の子はまだ読まれていないので対象外。
    /// </summary>
    private static void CollectExplorerTypeaheadItems(ItemCollection items, List<TreeViewItem> dest)
    {
        foreach (var raw in items)
        {
            if (raw is not TreeViewItem item || item.Tag is not string)
            {
                continue;
            }

            dest.Add(item);
            if (item.IsExpanded && item.Items.Count > 0)
            {
                CollectExplorerTypeaheadItems(item.Items, dest);
            }
        }
    }

    private static void EnsureExplorerAncestorsExpanded(TreeViewItem item)
    {
        for (var parent = ItemsControl.ItemsControlFromItemContainer(item) as TreeViewItem;
             parent is not null;
             parent = ItemsControl.ItemsControlFromItemContainer(parent) as TreeViewItem)
        {
            parent.IsExpanded = true;
        }
    }

    public void SetExplorerFolder(string path) => RevealFolder(path);

    /// <summary>
    /// ディスク上のフォルダ一覧を取り込み直す。展開状態は残し、選択もできるだけ戻す。F5。
    /// 索引の再構築は Ctrl+F5。
    /// </summary>
    public void RefreshExplorer()
    {
        ClearExplorerTypeahead();
        var selected = SelectedExplorerFolders;
        if (_explorerSearchGroups.Count > 0)
        {
            ApplyExplorerSearchSync(_explorerSearchCommitted);
        }
        else
        {
            RebuildExplorerTree();
        }

        if (selected.Length == 0)
        {
            return;
        }

        RevealFolder(selected[0]);
        if (selected.Length > 1)
        {
            RestoreExplorerMultiSelect(selected);
        }
    }

    internal string[] ExplorerRootPaths => _explorerRoots.ToArray();

    public void SetExplorerExpanded(IEnumerable<string> paths)
    {
        _explorerExpanded.Clear();
        foreach (var path in LibraryExplorerPaths.ResolveExpanded(paths))
        {
            _explorerExpanded.Add(NormalizeFolderPath(path));
        }

        if (_folderTree.Items.Count > 0)
        {
            RestoreExplorerExpansion();
        }
    }

    internal string[] ExplorerExpandedPaths
    {
        get
        {
            var paths = _explorerExpanded.ToArray();
            Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
            return paths;
        }
    }

    public event EventHandler? FileNameColorsChanged;

    /// <summary>選択ファイルへ手動の色を付ける。null はファイル名ルールへ戻す。</summary>
    public void SetManualFileNameColor(IEnumerable<string> paths, int? colorIndex)
    {
        var list = paths as IReadOnlyList<string> ?? paths.ToArray();
        AppStorage.Settings.FileNameColorAssignments = colorIndex is int index
            ? FileNameColorAssignments.With(AppStorage.Settings.FileNameColorAssignments, list, index)
            : FileNameColorAssignments.Without(AppStorage.Settings.FileNameColorAssignments, list);
        AppStorage.Save();
        RefreshFileNameMarkers();
        FileNameColorsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void ApplyFileNameMarker(LibraryFileRow row)
    {
        var path = row.Tag is DocumentSession session ? session.Document.SourcePath : null;
        if (FileNameColorResolve.TryResolve(row.Name, path, out var index, out var pattern))
        {
            row.SetFileNameMarker(index, pattern);
            return;
        }

        row.SetFileNameMarker(-1, string.Empty);
    }

    public void RefreshFileNameMarkers()
    {
        foreach (var row in _rows)
        {
            ApplyFileNameMarker(row);
        }

        if (_group == LibraryFileGroup.Color && _grid.ItemsSource is not null)
        {
            var active = SelectedSession;
            var selected = SelectedSessions;
            LibraryFileList.ApplyGroupKeys(_rows, _group);
            BindRows(active, selected);
            return;
        }

        RequestFitColumns();
    }

    public void SetExplorerRoots(IEnumerable<string> roots)
    {
        var next = LibraryExplorerPaths.ResolveRoots(roots.ToArray());
        if (_explorerRoots.SequenceEqual(next, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var current = TryGetSelectedExplorerFolder(out var selected) ? selected : string.Empty;
        _explorerRoots = next;
        if (_explorerSearchGroups.Count > 0)
        {
            ApplyExplorerSearchSync(_explorerSearchCommitted);
        }
        else
        {
            BuildExplorerRoots();
        }
        if (!string.IsNullOrEmpty(current))
        {
            RevealFolder(current);
        }
    }

    public void SetFavorites(IEnumerable<string> paths)
    {
        var next = LibraryFavoritePaths.Resolve(paths.ToArray());
        _favoritesAnchorIndex = -1;
        _favoritesCaretIndex = -1;
        _favorites.Clear();
        foreach (var path in next)
        {
            _favorites.Add(LibraryFavoriteRow.FromPath(path));
        }
    }

    public string[] FavoritePaths =>
        _favorites.Select(row => row.Path).ToArray();

    public string[] SelectedFavoritePaths
    {
        get
        {
            if (_favoritesList.SelectedItems.Count == 0)
            {
                return [];
            }

            var result = new List<string>(_favoritesList.SelectedItems.Count);
            foreach (var item in _favoritesList.SelectedItems)
            {
                if (item is LibraryFavoriteRow row)
                {
                    result.Add(row.Path);
                }
            }

            return [.. result];
        }
    }

    public string[] SelectedExplorerFolders
    {
        get
        {
            if (_treeMultiSelected.Count > 0)
            {
                return _treeMultiSelected
                    .Where(item => item.Tag is string)
                    .Select(item => (string)item.Tag!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            return TryGetSelectedExplorerFolder(out var path) ? [path] : [];
        }
    }

    public void SetExplorerWidth(double width)
    {
        var clamped = DesignMetrics.ClampLibraryExplorerWidth(width);
        _sidePanesRememberedWidth = clamped;
        if (!_sidePanesVisible)
        {
            return;
        }

        _treeColumn.Width = new GridLength(clamped);
    }

    /// <summary>
    /// F9 ミニマム。ライブラリ／お気に入り列だけ畳む（プレイリストは残す）。幅は覚える。
    /// </summary>
    public void SetSidePanesVisible(bool show)
    {
        if (_sidePanesVisible == show)
        {
            return;
        }

        if (!show)
        {
            var current = _treeColumn.ActualWidth > 1
                ? ReadExplorerWidth()
                : (_sidePanesRememberedWidth > 0
                    ? _sidePanesRememberedWidth
                    : DesignMetrics.LibraryExplorerWidth);
            _sidePanesRememberedWidth = DesignMetrics.ClampLibraryExplorerWidth(current);
            _treeColumn.MinWidth = 0;
            _treeColumn.MaxWidth = 0;
            _treeColumn.Width = new GridLength(0);
            _treeSplitterColumn.Width = new GridLength(0);
            _explorerSplitter.Visibility = Visibility.Collapsed;
            _sidePanesVisible = false;
            if (IsExplorerFocused || IsFavoritesFocused)
            {
                FocusList();
            }

            return;
        }

        _sidePanesVisible = true;
        _treeColumn.MinWidth = DesignMetrics.LibraryExplorerMinWidth;
        _treeColumn.MaxWidth = DesignMetrics.LibraryExplorerMaxWidth;
        var width = _sidePanesRememberedWidth > 0
            ? _sidePanesRememberedWidth
            : DesignMetrics.LibraryExplorerWidth;
        _treeColumn.Width = new GridLength(DesignMetrics.ClampLibraryExplorerWidth(width));
        _treeSplitterColumn.Width = new GridLength(DesignMetrics.LibraryExplorerSplitterWidth);
        _explorerSplitter.Visibility = Visibility.Visible;
    }

    public double ReadExplorerWidth()
    {
        if (!_sidePanesVisible && _sidePanesRememberedWidth > 0)
        {
            return DesignMetrics.ClampLibraryExplorerWidth(_sidePanesRememberedWidth);
        }

        var raw = _treeColumn.ActualWidth > 0
            ? _treeColumn.ActualWidth
            : _treeColumn.Width.Value;
        return DesignMetrics.ClampLibraryExplorerWidth(raw);
    }

    public void SetFavoritesSplit(double ratio)
    {
        var clamped = DesignMetrics.ClampLibraryFavoritesSplit(ratio);
        _explorerTreeRow.Height = new GridLength(Math.Max(0.01d, 1d - clamped), GridUnitType.Star);
        _favoritesRow.Height = new GridLength(clamped, GridUnitType.Star);
    }

    public double ReadFavoritesSplit()
    {
        var tree = _explorerTreeRow.ActualHeight;
        var favorites = _favoritesRow.ActualHeight;
        var total = tree + favorites;
        if (total <= 1d)
        {
            return DesignMetrics.LibraryFavoritesSplitDefault;
        }

        return DesignMetrics.ClampLibraryFavoritesSplit(favorites / total);
    }

    public void OpenSelectedFolder(bool recursive = false)
    {
        var folders = LibraryShellFiles.ExistingPaths(SelectedExplorerFolders);
        if (folders.Length == 0)
        {
            return;
        }

        ClearExplorerTypeahead();
        ExplorerOpenRecursive = recursive;
        if (folders.Length == 1)
        {
            ExplorerFolderOpened?.Invoke(this, folders[0]);
            return;
        }

        ExplorerFoldersOpened?.Invoke(this, folders);
    }

    /// <summary>右クリック「プレイリストをクリアして追加」。Enter／Ctrl+Enter と同じ。</summary>
    public void ReplacePlaylistFromSelection(bool recursive = false)
    {
        if (LibraryShellFiles.ExistingPaths(SelectedExplorerFolders).Length == 0)
        {
            return;
        }

        ClearExplorerTypeahead();
        ExplorerOpenRecursive = recursive;
        ExplorerReplacePlaylistRequested?.Invoke(this, EventArgs.Empty);
    }

    public void AddSelectedExplorerToFavorites()
    {
        var folders = SelectedExplorerFolders;
        if (folders.Length == 0)
        {
            return;
        }

        AddFavoritePaths(folders);
    }

    public void AddSelectedExplorerToRoots() =>
        AddExplorerRootPaths(SelectedExplorerFolders);

    /// <summary>エクスプローラーからのドロップなどでツリーのルートへ追加。</summary>
    public void AddExplorerRootPaths(IEnumerable<string> paths)
    {
        var folders = LibraryExplorerPaths.ExistingFolders(paths);
        if (folders.Length == 0)
        {
            return;
        }

        var merged = LibraryExplorerPaths.ResolveRoots([.. _explorerRoots, .. folders]);
        if (merged.SequenceEqual(_explorerRoots, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        SetExplorerRoots(merged);
        ExplorerRootsChanged?.Invoke(this, _explorerRoots);
    }

    public void RemoveSelectedExplorerRoots()
    {
        var selected = SelectedExplorerFolders;
        if (selected.Length == 0)
        {
            return;
        }

        var drop = new HashSet<string>(
            selected.Select(NormalizeFolderPath),
            StringComparer.OrdinalIgnoreCase);
        var kept = _explorerRoots
            .Where(root => !drop.Contains(NormalizeFolderPath(root)))
            .ToArray();
        var next = LibraryExplorerPaths.ResolveRoots(kept);
        if (next.SequenceEqual(_explorerRoots, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        SetExplorerRoots(next);
        ExplorerRootsChanged?.Invoke(this, _explorerRoots);
    }

    /// <summary>
    /// 選択中のルートを設定の「上へ／下へ」と同じく1段動かす。ルート以外の選択は無視。端では動かない。
    /// </summary>
    public bool TryMoveSelectedExplorerRoots(int direction)
    {
        if (direction == 0 || _explorerRoots.Length < 2)
        {
            return false;
        }

        var selected = SelectedExplorerRootSet();
        if (selected.Count == 0)
        {
            return false;
        }

        var items = _explorerRoots.ToList();
        var before = items.ToArray();
        LibraryExplorerPaths.MoveSelected(items, selected, direction);
        return ApplyExplorerRootsOrder(items, before, selected);
    }

    /// <summary>ドラッグドロップでルートを insertIndex（移動前の位置）へ移す。</summary>
    public bool TryReorderExplorerRoots(IEnumerable<string> draggedPaths, int insertIndex)
    {
        if (_explorerRoots.Length < 2)
        {
            return false;
        }

        var want = new HashSet<string>(
            draggedPaths.Select(NormalizeFolderPath),
            StringComparer.OrdinalIgnoreCase);
        var selected = _explorerRoots
            .Where(root => want.Contains(NormalizeFolderPath(root)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selected.Count == 0)
        {
            return false;
        }

        var items = _explorerRoots.ToList();
        var before = items.ToArray();
        LibraryExplorerPaths.MoveSelectedToIndex(items, selected, insertIndex);
        return ApplyExplorerRootsOrder(items, before, selected);
    }

    /// <summary>設定のルートリストと同じ。plain DataObject 用（OLE 経由だと消える）。</summary>
    internal const string ExplorerRootReorderFormat = "Mga.LibraryExplorerRootReorder";

    internal bool IsExplorerRootReorderActive =>
        _explorerRootReorderDrag.Length > 0;

    internal bool IsExplorerRootReorderData(System.Windows.IDataObject data) =>
        IsExplorerRootReorderActive
        || data.GetDataPresent(ExplorerRootReorderFormat);

    internal bool TryAcceptExplorerRootReorder(DragEventArgs e)
    {
        if (!IsExplorerRootReorderData(e.Data))
        {
            return false;
        }

        // 設定と同じ Move 優先。無ければ Allowed にある効果で受ける（禁止カーソル回避）。
        if ((e.AllowedEffects & DragDropEffects.Move) != 0)
        {
            e.Effects = DragDropEffects.Move;
        }
        else if ((e.AllowedEffects & DragDropEffects.Copy) != 0)
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
        return e.Effects != DragDropEffects.None;
    }

    internal bool TryDropExplorerRootReorder(DragEventArgs e)
    {
        if (!IsExplorerRootReorderData(e.Data))
        {
            return false;
        }

        e.Handled = true;
        var dragged = _explorerRootReorderDrag;
        if (dragged.Length == 0
            && e.Data.GetData(ExplorerRootReorderFormat) is string[] fromData)
        {
            dragged = fromData;
        }

        if (dragged.Length == 0)
        {
            return false;
        }

        var insert = ExplorerRootDropIndex(e.GetPosition(_folderTree));
        return TryReorderExplorerRoots(dragged, insert);
    }

    private bool ApplyExplorerRootsOrder(
        IList<string> items,
        IReadOnlyList<string> before,
        IReadOnlyCollection<string> selectAfter)
    {
        if (before.SequenceEqual(items, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var next = LibraryExplorerPaths.ResolveRoots(items.ToArray());
        if (next.SequenceEqual(_explorerRoots, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        // ルート並べ替えは TreeViewItem を作り直すので、事前にフォーカス有無を覚える。
        var keepFocus = _folderTree.IsKeyboardFocusWithin;
        _explorerRoots = next;
        if (_explorerSearchGroups.Count > 0)
        {
            ApplyExplorerSearchSync(_explorerSearchCommitted);
        }
        else
        {
            BuildExplorerRoots();
        }

        SelectExplorerRootPaths(selectAfter);
        if (keepFocus)
        {
            // Loaded／Input で再試行。作り直し直後の Focus 取りこぼしを防ぐ。
            RequestExplorerFocus();
        }

        ExplorerRootsChanged?.Invoke(this, _explorerRoots);
        return true;
    }

    private HashSet<string> SelectedExplorerRootSet()
    {
        var selectedPaths = new HashSet<string>(
            SelectedExplorerFolders.Select(NormalizeFolderPath),
            StringComparer.OrdinalIgnoreCase);
        return _explorerRoots
            .Where(root => selectedPaths.Contains(NormalizeFolderPath(root)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private int ExplorerRootDropIndex(Point point)
    {
        TreeViewItem? over = null;
        foreach (var item in _folderTree.Items.OfType<TreeViewItem>())
        {
            if (item.Tag is not string)
            {
                continue;
            }

            try
            {
                var topLeft = item.TranslatePoint(new Point(0, 0), _folderTree);
                var height = item.ActualHeight > 0 ? item.ActualHeight : item.RenderSize.Height;
                if (height <= 0)
                {
                    continue;
                }

                if (point.Y >= topLeft.Y && point.Y < topLeft.Y + height)
                {
                    over = item;
                    break;
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        if (over is null)
        {
            return _folderTree.Items.Count;
        }

        var index = _folderTree.Items.IndexOf(over);
        if (index < 0)
        {
            return _folderTree.Items.Count;
        }

        try
        {
            var top = over.TranslatePoint(new Point(0, 0), _folderTree).Y;
            var height = over.ActualHeight > 0 ? over.ActualHeight : over.RenderSize.Height;
            return point.Y > top + (height / 2d) ? index + 1 : index;
        }
        catch (InvalidOperationException)
        {
            return index;
        }
    }

    internal IReadOnlyList<string> ExplorerRoots => _explorerRoots;

    internal bool SelectionContainsExplorerRoot()
    {
        var roots = new HashSet<string>(
            _explorerRoots.Select(NormalizeFolderPath),
            StringComparer.OrdinalIgnoreCase);
        return SelectedExplorerFolders.Any(path => roots.Contains(NormalizeFolderPath(path)));
    }

    internal bool SelectionCanRegisterExplorerRoot()
    {
        var roots = new HashSet<string>(
            _explorerRoots.Select(NormalizeFolderPath),
            StringComparer.OrdinalIgnoreCase);
        return SelectedExplorerFolders.Any(path => !roots.Contains(NormalizeFolderPath(path)));
    }

    public void RemoveSelectedFavorites()
    {
        var selected = SelectedFavoritePaths;
        if (selected.Length == 0)
        {
            return;
        }

        var text = selected.Length == 1
            ? UiStrings.ConfirmRemoveFavorite(FavoriteDisplayName(selected[0]))
            : UiStrings.ConfirmRemoveFavorites(selected.Length);
        var answer = ConfirmChoiceWindow.Show(
            Window.GetWindow(this),
            text,
            UiStrings.AppName,
            MessageBoxButton.OKCancel,
            MessageBoxResult.OK);
        FocusFavorites();
        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        var drop = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
        for (var i = _favorites.Count - 1; i >= 0; i--)
        {
            if (drop.Contains(_favorites[i].Path))
            {
                _favorites.RemoveAt(i);
            }
        }

        FavoritesChanged?.Invoke(this, FavoritePaths);
        FocusFavorites();
    }

    private static string FavoriteDisplayName(string path)
    {
        var name = LibraryFavoritePaths.DisplayName(path);
        return string.IsNullOrEmpty(name) ? path : name;
    }

    public void AddFavoritePaths(IEnumerable<string> paths)
    {
        var merged = LibraryFavoritePaths.Merge(FavoritePaths, paths);
        if (merged.Length == FavoritePaths.Length
            && merged.SequenceEqual(FavoritePaths, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        SetFavorites(merged);
        FavoritesChanged?.Invoke(this, FavoritePaths);
    }

    public bool TryGetSelectedExplorerFolder(out string path)
    {
        // 欠損ルートも選択対象（ルートから外す／赤字表示の確認用）。開く操作は ExistingPaths 側で弾く。
        if (_folderTree.SelectedItem is TreeViewItem { Tag: string folder }
            && folder.Length > 0)
        {
            path = folder;
            return true;
        }

        path = string.Empty;
        return false;
    }

    private void BuildExplorerTypeaheadToast(FrameworkElement placementTarget)
    {
        _explorerTypeaheadToast.FontSize = DesignMetrics.From96(22);
        _explorerTypeaheadToast.Padding = DesignMetrics.PlaylistVideoTimecodePadding;
        _explorerTypeaheadToast.TextAlignment = TextAlignment.Center;
        _explorerTypeaheadToast.PlacementTarget = placementTarget;
        _explorerTypeaheadToast.Placement = PlacementMode.Center;
    }

    private static void ApplyPaneTitleStyle(TextBlock label, Thickness? margin = null)
    {
        label.Margin = margin ?? new Thickness(8, 6, 8, 2);
        label.FontSize = 11;
        label.FontWeight = FontWeights.SemiBold;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryForeBrush");
    }

    private Grid CreateSearchEditor(TextBox box, TextBlock hint)
    {
        box.FontSize = LibraryListFontSize;
        box.Height = DesignMetrics.AudioInputHeight;
        box.AcceptsReturn = false;
        box.AcceptsTab = false;
        box.VerticalContentAlignment = VerticalAlignment.Center;
        box.SetResourceReference(StyleProperty, "LibrarySearchBoxStyle");
        box.TextChanged += (_, _) => SyncSearchHint(box, hint);
        KeyboardNavigation.SetIsTabStop(box, false);

        hint.FontSize = LibraryListFontSize;
        hint.Margin = new Thickness(8, 0, 0, 0);
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.IsHitTestVisible = false;
        hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedForeBrush");

        var host = new Grid { VerticalAlignment = VerticalAlignment.Center };
        host.Children.Add(box);
        host.Children.Add(hint);
        SyncSearchHint(box, hint);
        return host;
    }

    private static void SyncSearchHint(TextBox box, TextBlock hint) =>
        hint.Visibility = string.IsNullOrEmpty(box.Text) && !box.IsKeyboardFocused
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void RestoreSearchBox(TextBox box, string committed, TextBlock hint)
    {
        if (box.Text == committed)
        {
            SyncSearchHint(box, hint);
            return;
        }

        box.Text = committed;
        SyncSearchHint(box, hint);
    }

    private void CommitPlaylistSearch()
    {
        _playlistSearchCommitted = _playlistSearchBox.Text;
        ApplyPlaylistSearch(_playlistSearchCommitted);
    }

    private bool PlaylistRowMatches(LibraryFileRow row) =>
        LibrarySearchQuery.MatchesRow(row, _playlistSearchGroups);

    private void ApplyPlaylistSearch(string text)
    {
        _playlistSearchGroups.Clear();
        _playlistSearchGroups.AddRange(LibrarySearchQuery.Parse(text));
        var active = SelectedSession;
        var selected = SelectedSessions;
        BindRows(active, selected);
    }

    internal void ApplyExplorerSearchSync(string text)
    {
        if (_explorerSearchBox.Text != text)
        {
            _explorerSearchBox.Text = text;
        }

        SyncSearchHint(_explorerSearchBox, _explorerSearchHint);
        _explorerSearchCommitted = text;
        var groups = LibrarySearchQuery.Parse(text);
        _explorerSearchGroups.Clear();
        _explorerSearchGroups.AddRange(groups);
        if (groups.Count == 0)
        {
            _explorerVisibleFolders = null;
            _explorerTagTexts = null;
            RebuildExplorerTree();
            return;
        }

        _explorerTagTexts = LibraryCatalog.LoadProbedTagTexts(_explorerRoots);
        var index = CollectExplorerIndex(_explorerRoots);
        ApplyExplorerFilter(index.Files, index.Folders, groups, _explorerTagTexts);
    }

    internal void ApplyPlaylistSearchSync(string text)
    {
        if (_playlistSearchBox.Text != text)
        {
            _playlistSearchBox.Text = text;
        }

        SyncSearchHint(_playlistSearchBox, _playlistSearchHint);
        _playlistSearchCommitted = text;
        ApplyPlaylistSearch(text);
    }

    internal IReadOnlyList<string> ExplorerVisibleFolderPaths
    {
        get
        {
            var paths = new List<string>();
            CollectExplorerFolderPaths(_folderTree.Items, paths);
            return paths;
        }
    }

    internal LibraryExplorerPlaylistWalk SnapshotExplorerPlaylistWalk()
    {
        if (_explorerSearchGroups.Count == 0)
        {
            return LibraryExplorerPlaylistWalk.Inactive;
        }

        var groups = new string[_explorerSearchGroups.Count][];
        for (var i = 0; i < _explorerSearchGroups.Count; i++)
        {
            groups[i] = (string[])_explorerSearchGroups[i].Clone();
        }

        var visible = _explorerVisibleFolders is { } set
            ? new HashSet<string>(set, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new List<string>(_explorerRoots.Length);
        foreach (var root in _explorerRoots)
        {
            var full = LibrarySearchQuery.NormalizeFolder(root);
            if (full.Length > 0)
            {
                roots.Add(full);
            }
        }

        return new LibraryExplorerPlaylistWalk(groups, visible, roots, _explorerTagTexts);
    }

    private static void CollectExplorerFolderPaths(ItemCollection items, List<string> paths)
    {
        foreach (var raw in items)
        {
            if (raw is not TreeViewItem { Tag: string path } item)
            {
                continue;
            }

            paths.Add(NormalizeFolderPath(path));
            if (item.Items.Count > 0)
            {
                CollectExplorerFolderPaths(item.Items, paths);
            }
        }
    }

    private void ApplyExplorerFilter(
        IReadOnlyList<string> files,
        IReadOnlyList<string> folders,
        IReadOnlyList<string[]> groups,
        IReadOnlyDictionary<string, string[]>? tagTexts)
    {
        var visible = LibrarySearchQuery.VisibleFolders(_explorerRoots, files, folders, groups, tagTexts);
        _explorerVisibleFolders = visible;
        RebuildExplorerTree();
    }

    private void RebuildExplorerTree()
    {
        if (_explorerVisibleFolders is { } visible)
        {
            BuildFilteredExplorer(visible);
            return;
        }

        BuildExplorerRoots();
    }

    private (string[] Files, string[] Folders) CollectExplorerIndex(IReadOnlyList<string> roots)
    {
        var files = CollectExplorerFiles(roots);
        var folders = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (string.IsNullOrEmpty(root))
            {
                continue;
            }

            CollectExplorerFolders(root, folders, seen);
        }

        return (files, [.. folders]);
    }

    private void CollectExplorerFolders(string path, List<string> folders, HashSet<string> seen)
    {
        var full = NormalizeFolderPath(path);
        if (full.Length == 0 || !seen.Add(full))
        {
            return;
        }

        folders.Add(full);
        foreach (var child in EnumerateSubdirs(path))
        {
            CollectExplorerFolders(child, folders, seen);
        }
    }

    private static string[] CollectExplorerFiles(IReadOnlyList<string> roots)
    {
        // 検索のファイル発見は毎回ディスク。索引に頼ると F5 無しで新規ファイルが欠ける。
        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (string.IsNullOrEmpty(root))
            {
                continue;
            }

            foreach (var file in AudioCodec.CollectPlayerOpenableFromDirectory(root, recursive: true))
            {
                if (seen.Add(file))
                {
                    files.Add(file);
                }
            }
        }

        return [.. files];
    }

    private void BuildFilteredExplorer(HashSet<string> visible)
    {
        _treeMultiSelected.Clear();
        _treeSelectionAnchor = null;
        _treeSyncing = true;
        _treeExpansionBusy = true;
        try
        {
            _folderTree.Items.Clear();
            foreach (var root in _explorerRoots)
            {
                if (string.IsNullOrEmpty(root))
                {
                    continue;
                }

                var full = NormalizeFolderPath(root);
                if (!visible.Contains(full))
                {
                    continue;
                }

                var item = AddFolderItem(_folderTree.Items, root, ExplorerRootHeader(root));
                if (!LibraryExplorerPaths.IsMissing(root))
                {
                    LoadFilteredChildren(item, visible);
                    item.IsExpanded = true;
                }
            }
        }
        finally
        {
            _treeExpansionBusy = false;
            _treeSyncing = false;
        }
    }

    private void LoadFilteredChildren(TreeViewItem item, HashSet<string> visible)
    {
        if (item.Tag is not string path)
        {
            return;
        }

        item.Items.Clear();
        foreach (var dir in EnumerateSubdirs(path))
        {
            if (!visible.Contains(NormalizeFolderPath(dir)))
            {
                continue;
            }

            var child = AddFolderItem(item.Items, dir, FolderDisplayName(dir));
            LoadFilteredChildren(child, visible);
            child.IsExpanded = true;
        }
    }

    private void BuildExplorerRoots()
    {
        ClearExplorerTypeahead();
        _treeMultiSelected.Clear();
        _treeSelectionAnchor = null;
        _folderTree.Items.Clear();
        foreach (var root in _explorerRoots)
        {
            if (string.IsNullOrEmpty(root))
            {
                continue;
            }

            AddFolderItem(_folderTree.Items, root, ExplorerRootHeader(root));
        }

        RestoreExplorerExpansion();
    }

    private void RelabelExplorerRoots()
    {
        foreach (var item in _folderTree.Items.OfType<TreeViewItem>())
        {
            if (item.Tag is not string path)
            {
                continue;
            }

            ApplyFolderHeader(item, ExplorerRootHeader(path), LibraryExplorerPaths.IsMissing(path));
        }
    }

    /// <summary>登録パスが見つからないときの文字色（エラー文字）。</summary>
    internal const string MissingRegisteredPathForeBrushKey = "StatusBarErrorDetailForeBrush";

    /// <summary>
    /// 見出し TextBlock に色を載せる。TreeViewItem.Foreground だけだと ContentPresenter 経由で
    /// PrimaryFore に上書きされ、赤字にならないことがある。
    /// </summary>
    private void ApplyFolderHeader(TreeViewItem item, string text, bool missing)
    {
        var cut = item.Tag is string path && IsCutPath(path);
        var brushKey = missing
            ? MissingRegisteredPathForeBrushKey
            : cut
                ? "MutedForeBrush"
                : "PrimaryForeBrush";
        if (item.Header is TextBlock block)
        {
            block.Text = text;
            block.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        }
        else
        {
            block = new TextBlock { Text = text };
            block.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
            item.Header = block;
        }

        item.SetResourceReference(Control.ForegroundProperty, brushKey);
    }

    private static string FolderHeaderText(TreeViewItem item) =>
        item.Header switch
        {
            string text => text,
            TextBlock block => block.Text ?? string.Empty,
            _ => item.Header?.ToString() ?? string.Empty,
        };

    private static string ExplorerRootHeader(string path)
    {
        var full = NormalizeFolderPath(path);
        if (PathsEqual(full, SpecialFolderPath(Environment.SpecialFolder.Desktop)))
        {
            return UiStrings.LibraryExplorerDesktop;
        }

        if (PathsEqual(full, SpecialFolderPath(Environment.SpecialFolder.MyDocuments)))
        {
            return UiStrings.LibraryExplorerDocuments;
        }

        if (PathsEqual(full, SpecialFolderPath(Environment.SpecialFolder.MyMusic)))
        {
            return UiStrings.LibraryExplorerMusic;
        }

        if (PathsEqual(full, SpecialFolderPath(Environment.SpecialFolder.MyPictures)))
        {
            return UiStrings.LibraryExplorerPictures;
        }

        if (PathsEqual(full, SpecialFolderPath(Environment.SpecialFolder.MyVideos)))
        {
            return UiStrings.LibraryExplorerVideos;
        }

        try
        {
            var drive = new DriveInfo(path);
            if (drive.RootDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Equals(full, StringComparison.OrdinalIgnoreCase))
            {
                return DriveHeader(drive);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
        }

        return FolderDisplayName(path);
    }

    private static string SpecialFolderPath(Environment.SpecialFolder folder)
    {
        try
        {
            var path = Environment.GetFolderPath(folder);
            return string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)
                ? string.Empty
                : NormalizeFolderPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            return string.Empty;
        }
    }

    private static string DriveHeader(DriveInfo drive)
    {
        var letter = drive.Name.TrimEnd('\\', '/');
        try
        {
            if (!string.IsNullOrWhiteSpace(drive.VolumeLabel))
            {
                return $"{drive.VolumeLabel} ({letter})";
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return letter;
    }

    private TreeViewItem AddFolderItem(ItemCollection items, string path, string header)
    {
        var missing = LibraryExplorerPaths.IsMissing(path);
        var item = new TreeViewItem { Tag = path };
        ApplyFolderHeader(item, header, missing);
        var style = _folderTree.ItemContainerStyle;
        if (style is not null)
        {
            item.Style = style;
            item.ItemContainerStyle = style;
        }

        item.Expanded += FolderItem_Expanded;
        item.Collapsed += FolderItem_Collapsed;
        if (!missing && HasAnySubdir(path))
        {
            var dummy = new TreeViewItem();
            if (style is not null)
            {
                dummy.Style = style;
            }

            item.Items.Add(dummy);
        }

        items.Add(item);
        SyncTreeIndentDepth(item);
        return item;
    }

    private void FolderItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(sender, e.OriginalSource))
        {
            return;
        }

        if (sender is not TreeViewItem item)
        {
            return;
        }

        LoadChildren(item);
        RememberExplorerExpanded(item, expanded: true);
        if (_treeExpansionBusy)
        {
            return;
        }

        ApplyTreeMultiSelectChrome();
        // 子のレイアウト後に、展開した行がビューポート先頭へ来るよう寄せる。
        Dispatcher.BeginInvoke(
            () => ScrollExplorerItemToTop(item),
            DispatcherPriority.Loaded);
    }

    private void ScrollExplorerItemToTop(TreeViewItem item)
    {
        if (_treeExpansionBusy || !item.IsExpanded || !item.IsVisible)
        {
            return;
        }

        if (FindDescendantScrollViewer(_folderTree) is not { } scroll)
        {
            return;
        }

        try
        {
            item.UpdateLayout();
            var top = item.TransformToAncestor(scroll).Transform(new Point(0, 0)).Y;
            scroll.ScrollToVerticalOffset(Math.Max(0, scroll.VerticalOffset + top));
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void FolderItem_Collapsed(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(sender, e.OriginalSource))
        {
            return;
        }

        if (sender is TreeViewItem item)
        {
            RememberExplorerExpanded(item, expanded: false);
            if (!_treeExpansionBusy)
            {
                ApplyTreeMultiSelectChrome();
            }
        }
    }

    private void LoadChildren(TreeViewItem item)
    {
        if (item.Tag is not string path)
        {
            return;
        }

        if (item.Items.Count == 1 && item.Items[0] is TreeViewItem dummy && dummy.Tag is null)
        {
            item.Items.Clear();
        }
        else if (item.Items.OfType<TreeViewItem>().Any(child => child.Tag is string))
        {
            return;
        }
        else
        {
            item.Items.Clear();
        }

        foreach (var dir in EnumerateSubdirs(path))
        {
            if (_explorerVisibleFolders is { } visible
                && !visible.Contains(NormalizeFolderPath(dir)))
            {
                continue;
            }

            var child = AddFolderItem(item.Items, dir, FolderDisplayName(dir));
            if (_explorerVisibleFolders is not null
                || _explorerExpanded.Contains(NormalizeFolderPath(dir)))
            {
                child.IsExpanded = true;
            }
        }
    }

    private void RevealFolder(string path)
    {
        var target = LibraryExplorerPaths.Resolve(path);
        if (string.IsNullOrEmpty(target))
        {
            return;
        }

        if (_folderTree.Items.Count == 0)
        {
            BuildExplorerRoots();
        }

        _treeSyncing = true;
        try
        {
            SelectFolderPath(target);
        }
        finally
        {
            _treeSyncing = false;
        }
    }

    private void RestoreExplorerMultiSelect(IReadOnlyList<string> paths)
    {
        _treeMultiSelected.Clear();
        _treeSyncing = true;
        try
        {
            foreach (var path in paths)
            {
                var target = LibraryExplorerPaths.Resolve(path);
                if (string.IsNullOrEmpty(target) || !Directory.Exists(target))
                {
                    continue;
                }

                SelectFolderPath(target);
                if (_folderTree.SelectedItem is TreeViewItem item)
                {
                    _treeMultiSelected.Add(item);
                }
            }
        }
        finally
        {
            _treeSyncing = false;
        }

        ApplyTreeMultiSelectChrome();
    }

    /// <summary>トップレベルのルート項目だけを選び直す（欠損ルートも可）。</summary>
    private void SelectExplorerRootPaths(IReadOnlyCollection<string> paths)
    {
        var want = new HashSet<string>(
            paths.Select(NormalizeFolderPath),
            StringComparer.OrdinalIgnoreCase);
        _treeMultiSelected.Clear();
        _treeSelectionAnchor = null;
        _treeSyncing = true;
        try
        {
            TreeViewItem? last = null;
            foreach (var item in _folderTree.Items.OfType<TreeViewItem>())
            {
                if (item.Tag is not string path
                    || !want.Contains(NormalizeFolderPath(path)))
                {
                    continue;
                }

                item.IsSelected = true;
                _treeMultiSelected.Add(item);
                last = item;
            }

            if (last is not null)
            {
                _treeSelectionAnchor = last;
                last.BringIntoView();
            }
        }
        finally
        {
            _treeSyncing = false;
        }

        ApplyTreeMultiSelectChrome();
    }

    private void SelectFolderPath(string target)
    {
        var full = NormalizeFolderPath(target);
        TreeViewItem? best = null;
        var bestLen = -1;
        foreach (var root in _folderTree.Items.OfType<TreeViewItem>())
        {
            if (root.Tag is not string rootPath)
            {
                continue;
            }

            var prefix = NormalizeFolderPath(rootPath);
            if (IsSameOrChild(full, prefix) && prefix.Length > bestLen)
            {
                best = root;
                bestLen = prefix.Length;
            }
        }

        if (best is null)
        {
            return;
        }

        ExpandTo(best, full);
    }

    private void ExpandTo(TreeViewItem item, string target)
    {
        item.IsExpanded = true;
        LoadChildren(item);
        var itemPath = NormalizeFolderPath((string)item.Tag!);
        if (PathsEqual(itemPath, target))
        {
            item.IsSelected = true;
            item.BringIntoView();
            return;
        }

        foreach (var child in item.Items.OfType<TreeViewItem>())
        {
            if (child.Tag is not string childPath)
            {
                continue;
            }

            var prefix = NormalizeFolderPath(childPath);
            if (IsSameOrChild(target, prefix))
            {
                ExpandTo(child, target);
                return;
            }
        }

        item.IsSelected = true;
        item.BringIntoView();
    }

    internal bool ExpandSelectedExplorerSubtree()
    {
        var items = SelectedExplorerTreeItems();
        if (items.Count == 0)
        {
            return false;
        }

        _treeExpansionBusy = true;
        try
        {
            foreach (var item in items)
            {
                ExpandExplorerSubtree(item);
            }
        }
        finally
        {
            _treeExpansionBusy = false;
        }

        NotifyExplorerExpanded();
        return true;
    }

    internal bool CollapseSelectedExplorerSubtree()
    {
        var items = SelectedExplorerTreeItems();
        if (items.Count == 0)
        {
            return false;
        }

        _treeExpansionBusy = true;
        try
        {
            foreach (var item in items)
            {
                CollapseExplorerSubtree(item);
            }
        }
        finally
        {
            _treeExpansionBusy = false;
        }

        NotifyExplorerExpanded();
        return true;
    }

    /// <summary>ルートの各フォルダを、選択中の / と同じく配下ごと閉じる。</summary>
    internal bool CollapseAllExplorerRoots()
    {
        var roots = _folderTree.Items.OfType<TreeViewItem>()
            .Where(item => item.Tag is string)
            .ToList();
        if (roots.Count == 0)
        {
            return false;
        }

        _treeExpansionBusy = true;
        try
        {
            foreach (var item in roots)
            {
                CollapseExplorerSubtree(item);
            }
        }
        finally
        {
            _treeExpansionBusy = false;
        }

        NotifyExplorerExpanded();
        return true;
    }

    private List<TreeViewItem> SelectedExplorerTreeItems()
    {
        if (_treeMultiSelected.Count > 0)
        {
            return [.. _treeMultiSelected.Where(item => item.Tag is string)];
        }

        return _folderTree.SelectedItem is TreeViewItem { Tag: string } selected
            ? [selected]
            : [];
    }

    private void ExpandExplorerSubtree(TreeViewItem item)
    {
        LoadChildren(item);
        item.IsExpanded = true;
        RememberExplorerExpanded(item, expanded: true);
        foreach (var child in item.Items.OfType<TreeViewItem>())
        {
            if (child.Tag is string)
            {
                ExpandExplorerSubtree(child);
            }
        }
    }

    private void CollapseExplorerSubtree(TreeViewItem item)
    {
        foreach (var child in item.Items.OfType<TreeViewItem>())
        {
            if (child.Tag is string)
            {
                CollapseExplorerSubtree(child);
            }
        }

        item.IsExpanded = false;
        RememberExplorerExpanded(item, expanded: false);
    }

    private void RestoreExplorerExpansion()
    {
        if (_explorerExpanded.Count == 0)
        {
            return;
        }

        _treeExpansionBusy = true;
        try
        {
            foreach (var root in _folderTree.Items.OfType<TreeViewItem>())
            {
                RestoreExplorerItem(root);
            }
        }
        finally
        {
            _treeExpansionBusy = false;
        }
    }

    private void RestoreExplorerItem(TreeViewItem item)
    {
        if (item.Tag is not string path)
        {
            return;
        }

        if (!_explorerExpanded.Contains(NormalizeFolderPath(path)))
        {
            return;
        }

        item.IsExpanded = true;
        LoadChildren(item);
        foreach (var child in item.Items.OfType<TreeViewItem>())
        {
            RestoreExplorerItem(child);
        }
    }

    private void RememberExplorerExpanded(TreeViewItem item, bool expanded)
    {
        if (_explorerVisibleFolders is not null)
        {
            return;
        }

        if (item.Tag is not string path)
        {
            return;
        }

        var full = NormalizeFolderPath(path);
        if (expanded)
        {
            _explorerExpanded.Add(full);
        }
        else
        {
            _explorerExpanded.Remove(full);
        }

        if (!_treeExpansionBusy && !_treeSyncing)
        {
            NotifyExplorerExpanded();
        }
    }

    private void NotifyExplorerExpanded() =>
        ExplorerExpandedChanged?.Invoke(this, ExplorerExpandedPaths);

    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_treeSyncing)
        {
            return;
        }

        if (e.NewValue is not TreeViewItem { Tag: string path } item)
        {
            return;
        }

        if (!_treeRangeBusy)
        {
            var modifiers = Keyboard.Modifiers;
            if ((modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0)
            {
                _treeSelectionAnchor = item;
            }

            if ((modifiers & ModifierKeys.Control) == 0
                && (modifiers & ModifierKeys.Shift) == 0
                && !_treeMultiSelected.Contains(item))
            {
                ClearTreeMultiSelect();
                _treeMultiSelected.Add(item);
                ApplyTreeMultiSelectChrome();
            }
        }

        // 欠損ルートは選択だけ残し、最後に開いた場所としては覚えない。
        if (Directory.Exists(path))
        {
            ExplorerFolderChanged?.Invoke(this, path);
        }
    }

    private void FolderTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CancelPlaylistFocusRestore();
        ReleasePlaylistFocusHold();
        ClearTreeDrag();
        if (e.OriginalSource is not DependencyObject origin
            || FindTreeViewItem(origin) is not { Tag: string } item
            || FindTreeExpandToggle(origin) is not null
            || FindAncestor<CheckBox>(origin) is not null)
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        var control = (modifiers & ModifierKeys.Control) != 0;
        var shift = (modifiers & ModifierKeys.Shift) != 0;
        if (control || shift)
        {
            BeginExplorerPlainPress(item, control, shift);
            _treeDragStart = e.GetPosition(null);
            e.Handled = true;
            return;
        }

        BeginExplorerPlainPress(item, control: false);
        _treeDragStart = e.GetPosition(null);
        if (_treePendingSingleSelect is not null)
        {
            e.Handled = true;
        }
    }

    private void FolderTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        ReleasePlaylistFocusHold();
        if (e.OriginalSource is DependencyObject origin
            && FindTreeViewItem(origin) is { Tag: string } item)
        {
            if (!_treeMultiSelected.Contains(item))
            {
                ClearTreeMultiSelect();
                _treeMultiSelected.Add(item);
                ApplyTreeMultiSelectChrome();
                _treeSelectionAnchor = item;
                item.IsSelected = true;
            }
        }
    }

    private void FolderTree_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        if (LibraryPlayerMode.IsExplorerExpandAll(key, modifiers))
        {
            ClearExplorerTypeahead();
            ExpandSelectedExplorerSubtree();
            e.Handled = true;
            return;
        }

        if (LibraryPlayerMode.IsExplorerCollapseSubtree(key, modifiers))
        {
            ClearExplorerTypeahead();
            CollapseSelectedExplorerSubtree();
            e.Handled = true;
            return;
        }

        if (LibraryPlayerMode.IsExplorerCollapseAllRoots(key, modifiers))
        {
            ClearExplorerTypeahead();
            CollapseAllExplorerRoots();
            e.Handled = true;
            return;
        }

        if (key is Key.Up or Key.Down && modifiers == ModifierKeys.Alt)
        {
            ClearExplorerTypeahead();
            TryMoveSelectedExplorerRoots(key == Key.Up ? -1 : 1);
            e.Handled = true;
            return;
        }

        if (key is Key.Left or Key.Right && modifiers == ModifierKeys.None)
        {
            ClearExplorerTypeahead();
            return;
        }

        if (key == Key.Space && modifiers == ModifierKeys.None)
        {
            TryExplorerSpaceExpandOrMoveDown();
            e.Handled = true;
            return;
        }

        if (key == Key.Space && modifiers == ModifierKeys.Shift)
        {
            TryExplorerShiftSpaceCollapseOrMoveUp();
            e.Handled = true;
            return;
        }

        if (key is Key.Up or Key.Down && modifiers == ModifierKeys.None)
        {
            var direction = key == Key.Up ? -1 : 1;
            if (IsExplorerTypeaheadActive && TryStepExplorerTypeahead(direction))
            {
                e.Handled = true;
                return;
            }

            // ヒットが無い ↑↓ は Esc と同じく検索を閉じ、続けて通常の行移動をする。
            if (IsExplorerTypeaheadActive)
            {
                ClearExplorerTypeahead();
            }

            MoveExplorerSelection(direction, extend: false);
            e.Handled = true;
            return;
        }

        if (key is Key.Up or Key.Down && modifiers == ModifierKeys.Shift)
        {
            ClearExplorerTypeahead();
            MoveExplorerSelection(key == Key.Up ? -1 : 1, extend: true);
            e.Handled = true;
        }
    }

    private void FolderTree_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!LibraryExplorerTypeahead.IsTypeaheadText(e.Text))
        {
            return;
        }

        if (!string.IsNullOrEmpty(_explorerTypeaheadIgnoreText)
            && string.Equals(e.Text, _explorerTypeaheadIgnoreText, StringComparison.OrdinalIgnoreCase))
        {
            _explorerTypeaheadIgnoreText = "";
            e.Handled = true;
            return;
        }

        if (TryAppendExplorerTypeahead(e.Text))
        {
            e.Handled = true;
        }
    }

    private void ToggleTreeMultiSelect(TreeViewItem item)
    {
        if (!_treeMultiSelected.Add(item))
        {
            _treeMultiSelected.Remove(item);
        }

        ApplyTreeMultiSelectChrome();
    }

    private void ClearTreeMultiSelect()
    {
        if (_treeMultiSelected.Count == 0)
        {
            return;
        }

        _treeMultiSelected.Clear();
        ApplyTreeMultiSelectChrome();
    }

    private void ApplyTreeMultiSelectChrome()
    {
        var selected = CyanSelectionBrush();
        var run = new List<TreeViewItem>();
        void Flush()
        {
            for (var i = 0; i < run.Count; i++)
            {
                run[i].SetValue(SelectionBandEdgeProperty, SelectionBandCorners.EdgeAt(i, run.Count));
            }

            run.Clear();
        }

        void Walk(ItemCollection items)
        {
            foreach (var raw in items)
            {
                if (raw is not TreeViewItem child)
                {
                    continue;
                }

                SyncTreeIndentDepth(child);
                var marked = _treeMultiSelected.Contains(child);
                child.SetValue(TreeMarkedProperty, marked);
                child.Background = marked ? selected : Brushes.Transparent;
                if (marked || child.IsSelected)
                {
                    run.Add(child);
                }
                else
                {
                    Flush();
                    child.SetValue(SelectionBandEdgeProperty, SelectionBandEdge.None);
                }

                if (child.IsExpanded && child.Items.Count > 0)
                {
                    Walk(child.Items);
                }
            }
        }

        Walk(_folderTree.Items);
        Flush();
    }

    private void FolderTree_DragOver(object sender, DragEventArgs e)
    {
        if (TryAcceptExplorerRootReorder(e))
        {
            return;
        }

        // 外部からのフォルダ登録。中身は Drop で選別。効果は AllowedEffects と交差させる。
        try
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.None;
            }
            else if ((e.AllowedEffects & DragDropEffects.Copy) != 0)
            {
                e.Effects = DragDropEffects.Copy;
            }
            else if ((e.AllowedEffects & DragDropEffects.Move) != 0)
            {
                e.Effects = DragDropEffects.Move;
            }
            else if ((e.AllowedEffects & DragDropEffects.Link) != 0)
            {
                e.Effects = DragDropEffects.Link;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }
        catch (Exception)
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void FolderTree_Drop(object sender, DragEventArgs e)
    {
        if (TryDropExplorerRootReorder(e))
        {
            return;
        }

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        try
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped)
            {
                e.Handled = true;
                AddExplorerRootPaths(dropped);
            }
        }
        catch (Exception)
        {
            e.Handled = true;
        }
    }

    private void FolderTree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_treeDragStart is null
            || _treeDragItem is null
            || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var paths = _treeDragPaths.Length > 0
            ? _treeDragPaths
            : SelectedExplorerTransferPaths();
        var item = _treeDragItem;
        var reorderRoots = IsExplorerRootItem(item)
            ? ExplorerRootPathsForDrag(item)
            : [];
        // コピー先が無くても、ルート並び替えドラッグは始める（欠損ルートなど）。
        if (paths.Length == 0 && reorderRoots.Length == 0)
        {
            return;
        }

        var delta = e.GetPosition(null) - _treeDragStart.Value;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        ClearTreeDrag();
        BeginExplorerDrag(item, paths, reorderRoots);
    }

    /// <summary>トップレベル（_folderTree.Items）の項目か。</summary>
    private bool IsExplorerRootItem(TreeViewItem item)
    {
        foreach (var root in _folderTree.Items.OfType<TreeViewItem>())
        {
            if (ReferenceEquals(root, item))
            {
                return true;
            }
        }

        return false;
    }

    private string[] ExplorerRootPathsForDrag(TreeViewItem item)
    {
        var selected = SelectedExplorerRootSet();
        if (item.Tag is string path)
        {
            var full = _explorerRoots.FirstOrDefault(root =>
                string.Equals(
                    NormalizeFolderPath(root),
                    NormalizeFolderPath(path),
                    StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(full))
            {
                selected.Add(full);
            }
        }

        // ルート配列の並びを保つ。
        return [.. _explorerRoots.Where(selected.Contains)];
    }

    private void BeginExplorerDrag(
        DependencyObject source,
        string[] copyPaths,
        string[] reorderRoots)
    {
        if (reorderRoots.Length == 0)
        {
            BeginFileCopyDrag(source, copyPaths);
            return;
        }

        // 設定のルートリストと同じ: plain DataObject + Move のみ。
        // OLE コピー用オブジェクトに載せると形式が消え、禁止カーソルになる。
        _explorerRootReorderDrag = reorderRoots;
        try
        {
            DragDrop.DoDragDrop(
                _folderTree,
                new DataObject(ExplorerRootReorderFormat, reorderRoots),
                DragDropEffects.Move);
        }
        finally
        {
            _explorerRootReorderDrag = [];
        }
    }

    private void FolderTree_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        CompleteExplorerPlainPress();

    private void ClearTreeDrag()
    {
        _treeDragStart = null;
        _treeDragItem = null;
        _treeDragPaths = [];
        _treePendingSingleSelect = null;
    }

    internal void BeginExplorerPlainPress(TreeViewItem item, bool control, bool shift = false)
    {
        if (shift)
        {
            ApplyExplorerShiftClick(item);
            _treePendingSingleSelect = null;
            ArmTreeDrag(item);
            return;
        }

        if (control)
        {
            ToggleTreeMultiSelect(item);
            item.IsSelected = true;
            _treePendingSingleSelect = null;
            ArmTreeDrag(item);
            return;
        }

        if (ShouldHoldExplorerMultiSelect(item))
        {
            _treePendingSingleSelect = item;
            ArmTreeDrag(item);
            return;
        }

        ClearTreeMultiSelect();
        _treeMultiSelected.Add(item);
        ApplyTreeMultiSelectChrome();
        _treeSelectionAnchor = item;
        ArmTreeDrag(item);
    }

    internal void CompleteExplorerPlainPress()
    {
        var pending = _treePendingSingleSelect;
        ClearTreeDrag();
        if (pending is null)
        {
            return;
        }

        ClearTreeMultiSelect();
        _treeMultiSelected.Add(pending);
        ApplyTreeMultiSelectChrome();
        _treeSelectionAnchor = pending;
        pending.IsSelected = true;
    }

    internal void AbandonExplorerPendingClick() => _treePendingSingleSelect = null;

    internal bool ShouldHoldExplorerMultiSelect(TreeViewItem item) =>
        _treeMultiSelected.Count > 1 && _treeMultiSelected.Contains(item);

    /// <summary>
    /// Space: 黄色ハイライトを消し、未展開で子があれば展開。展開済み（または葉）なら一つ下へ。
    /// </summary>
    internal bool TryExplorerSpaceExpandOrMoveDown()
    {
        ClearExplorerTypeahead();
        var item = CurrentExplorerItem();
        if (item is null)
        {
            return false;
        }

        if (item.HasItems && !item.IsExpanded)
        {
            item.IsExpanded = true;
            return true;
        }

        MoveExplorerSelection(1, extend: false);
        return true;
    }

    /// <summary>
    /// Shift+Space: 黄色ハイライトを消し、展開中なら1段階閉じる。既に閉じている（または葉）なら一つ上へ。
    /// </summary>
    internal bool TryExplorerShiftSpaceCollapseOrMoveUp()
    {
        ClearExplorerTypeahead();
        var item = CurrentExplorerItem();
        if (item is null)
        {
            return false;
        }

        if (item.HasItems && item.IsExpanded)
        {
            item.IsExpanded = false;
            return true;
        }

        MoveExplorerSelection(-1, extend: false);
        return true;
    }

    internal void MoveExplorerSelection(int delta, bool extend = false)
    {
        if (delta == 0)
        {
            return;
        }

        var items = VisibleExplorerItems();
        if (items.Count == 0)
        {
            return;
        }

        var caret = CurrentExplorerItem();
        var index = caret is null ? -1 : items.IndexOf(caret);
        if (index < 0)
        {
            index = delta > 0 ? 0 : items.Count - 1;
        }
        else
        {
            index = Math.Clamp(index + delta, 0, items.Count - 1);
        }

        ApplyExplorerMoveSelection(items[index], caret, extend);
    }

    internal void ApplyExplorerShiftClick(TreeViewItem item)
    {
        ApplyExplorerMoveSelection(item, CurrentExplorerItem(), extend: true);
    }

    private void ApplyExplorerMoveSelection(TreeViewItem current, TreeViewItem? caret, bool extend)
    {
        _treeRangeBusy = true;
        try
        {
            if (!extend)
            {
                _treeSelectionAnchor = current;
                _explorerCaret = current;
                ClearTreeMultiSelect();
                _treeMultiSelected.Add(current);
                ApplyTreeMultiSelectChrome();
                current.IsSelected = true;
                current.BringIntoView();
                current.Focus();
                return;
            }

            _treeSelectionAnchor ??= caret ?? current;
            _explorerCaret = current;
            SelectExplorerVisibleRange(_treeSelectionAnchor, current);
            current.IsSelected = true;
            current.BringIntoView();
            current.Focus();
        }
        finally
        {
            _treeRangeBusy = false;
        }
    }

    private void SelectExplorerVisibleRange(TreeViewItem from, TreeViewItem to)
    {
        var items = VisibleExplorerItems();
        var start = items.IndexOf(from);
        var end = items.IndexOf(to);
        if (end < 0)
        {
            return;
        }

        if (start < 0)
        {
            start = end;
        }

        var lo = Math.Min(start, end);
        var hi = Math.Max(start, end);
        _treeMultiSelected.Clear();
        for (var i = lo; i <= hi; i++)
        {
            _treeMultiSelected.Add(items[i]);
        }

        ApplyTreeMultiSelectChrome();
    }

    private TreeViewItem? CurrentExplorerItem()
    {
        if (_folderTree.SelectedItem is TreeViewItem { Tag: string } selected)
        {
            return selected;
        }

        foreach (var item in _treeMultiSelected)
        {
            if (item.Tag is string)
            {
                return item;
            }
        }

        return null;
    }

    private List<TreeViewItem> VisibleExplorerItems()
    {
        var items = new List<TreeViewItem>();
        CollectVisibleExplorerItems(_folderTree.Items, items);
        return items;
    }

    private static void CollectVisibleExplorerItems(ItemCollection items, List<TreeViewItem> dest)
    {
        foreach (var raw in items)
        {
            if (raw is not TreeViewItem item || item.Tag is not string)
            {
                continue;
            }

            dest.Add(item);
            if (item.IsExpanded && item.Items.Count > 0)
            {
                CollectVisibleExplorerItems(item.Items, dest);
            }
        }
    }

    private void ArmTreeDrag(TreeViewItem item)
    {
        _treeDragItem = item;
        _treeDragPaths = SelectedExplorerTransferPaths();
    }

    private static TreeViewItem? FindTreeViewItem(DependencyObject? origin)
    {
        while (origin is not null)
        {
            if (origin is TreeViewItem item)
            {
                return item;
            }

            origin = VisualTreeHelper.GetParent(origin);
        }

        return null;
    }

    private static ToggleButton? FindTreeExpandToggle(DependencyObject? origin)
    {
        while (origin is not null)
        {
            if (origin is ToggleButton toggle)
            {
                return toggle;
            }

            if (origin is TreeViewItem)
            {
                return null;
            }

            origin = VisualTreeHelper.GetParent(origin);
        }

        return null;
    }

    private void ExplorerSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        var width = ReadExplorerWidth();
        SetExplorerWidth(width);
        ExplorerWidthChanged?.Invoke(this, width);
    }

    private void FavoritesSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        var ratio = ReadFavoritesSplit();
        SetFavoritesSplit(ratio);
        FavoritesSplitChanged?.Invoke(this, ratio);
    }

    private static IEnumerable<string> EnumerateSubdirs(string path)
    {
        string[] dirs;
        try
        {
            dirs = Directory.GetDirectories(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            yield break;
        }

        Array.Sort(dirs, StringComparer.CurrentCultureIgnoreCase);
        foreach (var dir in dirs)
        {
            if (!ShouldSkipFolder(dir))
            {
                yield return dir;
            }
        }
    }

    private bool HasAnySubdir(string path)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(path))
            {
                if (ShouldSkipFolder(dir))
                {
                    continue;
                }

                if (_explorerVisibleFolders is { } visible
                    && !visible.Contains(NormalizeFolderPath(dir)))
                {
                    continue;
                }

                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }

        return false;
    }

    private static bool ShouldSkipFolder(string dir)
    {
        var name = Path.GetFileName(dir);
        if (string.IsNullOrEmpty(name)
            || name[0] == '$'
            || name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            var attr = File.GetAttributes(dir);
            return (attr & (FileAttributes.Hidden | FileAttributes.System)) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static string FolderDisplayName(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private static string NormalizeFolderPath(string path) =>
        PathUtil.TryGetFullPathTrimmedSeparators(path, out var full)
            ? full
            : PathUtil.TrimDirectorySeparators(path);

    private static bool PathsEqual(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool IsSameOrChild(string path, string parent)
    {
        if (PathsEqual(path, parent))
        {
            return true;
        }

        var prefix = parent + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private void FavoritesList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        if (key == Key.Delete && modifiers == ModifierKeys.None)
        {
            RemoveSelectedFavorites();
            e.Handled = true;
            return;
        }

        if (LibraryPlayerMode.IsPlaylistFolderAddKey(key, modifiers))
        {
            ActivateSelectedFavorites(
                clearPlaylist: LibraryPlayerMode.ClearsPlaylistOnFolderAdd(modifiers),
                recursive: LibraryPlayerMode.RecursesPlaylistFolderAdd(modifiers));
            e.Handled = true;
            return;
        }

        if (key is Key.Up or Key.Down
            && modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            MoveFavoritesSelection(key == Key.Up ? -1 : 1, extend: modifiers == ModifierKeys.Shift);
            e.Handled = true;
        }
    }

    private void FavoritesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_favoritesRangeBusy || (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            RefreshFavoritesSelectionBandEdges();
            return;
        }

        _favoritesAnchorIndex = _favoritesList.SelectedIndex;
        _favoritesCaretIndex = _favoritesAnchorIndex;
        RefreshFavoritesSelectionBandEdges();
    }

    private void RefreshFavoritesSelectionBandEdges()
    {
        var count = _favorites.Count;
        if (count <= 0)
        {
            return;
        }

        var selected = new bool[count];
        foreach (var raw in _favoritesList.SelectedItems)
        {
            if (raw is LibraryFavoriteRow row)
            {
                var index = _favorites.IndexOf(row);
                if (index >= 0)
                {
                    selected[index] = true;
                }
            }
        }

        var i = 0;
        while (i < count)
        {
            if (_favoritesList.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem idle
                && !selected[i])
            {
                idle.SetValue(SelectionBandEdgeProperty, SelectionBandEdge.None);
            }

            if (!selected[i])
            {
                i++;
                continue;
            }

            var start = i;
            while (i < count && selected[i])
            {
                i++;
            }

            var runLength = i - start;
            for (var offset = 0; offset < runLength; offset++)
            {
                if (_favoritesList.ItemContainerGenerator.ContainerFromIndex(start + offset) is ListBoxItem item)
                {
                    item.SetValue(
                        SelectionBandEdgeProperty,
                        SelectionBandCorners.EdgeAt(offset, runLength));
                }
            }
        }
    }

    internal void MoveFavoritesSelection(int delta, bool extend = false)
    {
        if (delta == 0)
        {
            return;
        }

        var count = _favorites.Count;
        if (count <= 0)
        {
            return;
        }

        var caret = FavoritesCaretIndex();
        var index = caret;
        if (index < 0)
        {
            index = delta > 0 ? 0 : count - 1;
        }
        else
        {
            index = Math.Clamp(index + delta, 0, count - 1);
        }

        ApplyFavoritesMoveSelection(index, caret, extend);
    }

    internal void ApplyFavoritesShiftClick(int index)
    {
        ApplyFavoritesMoveSelection(index, FavoritesCaretIndex(), extend: true);
    }

    private int FavoritesCaretIndex()
    {
        if (Keyboard.FocusedElement is ListBoxItem focused)
        {
            var focusedIndex = _favoritesList.ItemContainerGenerator.IndexFromContainer(focused);
            if (focusedIndex >= 0)
            {
                return focusedIndex;
            }
        }

        if (_favoritesCaretIndex >= 0 && _favoritesCaretIndex < _favorites.Count)
        {
            return _favoritesCaretIndex;
        }

        return _favoritesList.SelectedIndex;
    }

    private void ApplyFavoritesMoveSelection(int index, int caret, bool extend)
    {
        _favoritesRangeBusy = true;
        try
        {
            if (!extend)
            {
                _favoritesAnchorIndex = index;
                _favoritesCaretIndex = index;
                _favoritesList.SelectedIndex = index;
                RevealFavorite(index);
                return;
            }

            if (_favoritesAnchorIndex < 0)
            {
                _favoritesAnchorIndex = Math.Max(0, caret);
            }

            _favoritesCaretIndex = index;
            SelectFavoritesRange(_favoritesAnchorIndex, index);
            RevealFavorite(index);
        }
        finally
        {
            _favoritesRangeBusy = false;
            RefreshFavoritesSelectionBandEdges();
        }
    }

    private void SelectFavoritesRange(int from, int to)
    {
        var count = _favorites.Count;
        if (count <= 0)
        {
            return;
        }

        from = Math.Clamp(from, 0, count - 1);
        to = Math.Clamp(to, 0, count - 1);
        var lo = Math.Min(from, to);
        var hi = Math.Max(from, to);
        _favoritesList.SelectedItems.Clear();
        for (var i = lo; i <= hi; i++)
        {
            _favoritesList.SelectedItems.Add(_favorites[i]);
        }
    }

    private void RevealFavorite(int index)
    {
        if (index < 0 || index >= _favorites.Count)
        {
            return;
        }

        var row = _favorites[index];
        _favoritesList.ScrollIntoView(row);
        if (_favoritesList.ItemContainerGenerator.ContainerFromIndex(index) is UIElement item)
        {
            item.Focus();
        }
    }

    /// <summary>Enter 系は修飾でクリア／再帰を決める。追加のみのときは再生しない。</summary>
    internal void ActivateSelectedFavorites(bool clearPlaylist, bool recursive = false)
    {
        // 欠損だけのときはスルー（クリアだけ走らせない）。
        var paths = LibraryShellFiles.ExistingPaths(SelectedFavoritePaths);
        if (paths.Length == 0)
        {
            return;
        }

        FavoritesActivated?.Invoke(
            this,
            new LibraryFavoritesActivateEventArgs(paths, clearPlaylist, recursive));
    }

    private void FavoritesList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CancelPlaylistFocusRestore();
        _favoritesDragStart = e.GetPosition(null);
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0
            || e.OriginalSource is not DependencyObject origin)
        {
            return;
        }

        var index = FavoritesIndexFromOrigin(origin);
        if (index < 0)
        {
            return;
        }

        e.Handled = true;
        ApplyFavoritesShiftClick(index);
    }

    private int FavoritesIndexFromOrigin(DependencyObject? origin)
    {
        while (origin is not null)
        {
            if (origin is ListBoxItem item)
            {
                return _favoritesList.ItemContainerGenerator.IndexFromContainer(item);
            }

            origin = VisualTreeHelper.GetParent(origin);
        }

        return -1;
    }

    private void FavoritesList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_favoritesDragStart is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var paths = SelectedFavoritePaths;
        if (paths.Length == 0)
        {
            return;
        }

        var delta = e.GetPosition(null) - _favoritesDragStart.Value;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _favoritesDragStart = null;
        BeginFileCopyDrag(_favoritesList, LibraryShellFiles.ExistingPaths(paths));
    }

    private void FavoritesList_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void FavoritesList_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)
            || e.Data.GetData(DataFormats.FileDrop) is not string[] dropped)
        {
            return;
        }

        e.Handled = true;
        AddFavoritePaths(dropped);
    }
}
