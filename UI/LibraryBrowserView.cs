using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>F10 上段。左フォルダツリー、中央リスト、右は再生中ジャケットと列チェック。</summary>
internal sealed class LibraryBrowserView : UserControl
{
    /// <summary>色抽出用の縮小長辺。ピクセル数はごく少ない。</summary>
    internal const int GlowSampleEdge = 12;
    private const double GlowVeilOpacityDark = 0.42;
    private const double GlowVeilOpacityLight = 0.58;
    internal const double GlowDriftScaleFrom = 1.08;
    internal const double GlowDriftScaleTo = 1.22;
    internal const double GlowDriftX = 72;
    internal const double GlowDriftY = 52;
    internal const double GlowDriftScaleSeconds = 11;
    internal const double GlowDriftXSeconds = 9;
    internal const double GlowDriftYSeconds = 13;
    internal const int GlowDriftFrameRate = 16;
    private const double GlowDriftBleed = 80;
    /// <summary>ジャケット／グロー用デコードの長辺上限。APIC 原寸展開を避ける。</summary>
    private const int ArtworkDecodeMaxEdge = 512;

    private readonly Grid _host = new();
    private readonly Grid _glowHost = new();
    private readonly ScaleTransform _glowScale = new(GlowDriftScaleFrom, GlowDriftScaleFrom);
    private readonly TranslateTransform _glowTranslate = new();
    private bool _glowDriftRunning;
    private readonly Border _veil = new();
    private readonly Grid _root = new();
    private readonly ColumnDefinition _treeColumn = new();
    private readonly TreeView _folderTree = new();
    private readonly ColumnDefinition _jacketColumn = new();
    private readonly Border _jacketFrame = new();
    private readonly Image _jacketImage = new();
    private readonly StackPanel _jacketStack = new();
    private LibraryJacketReflectionView _jacketReflection = null!;
    private readonly ScrollViewer _columnFilterScroll = new();
    private readonly StackPanel _columnFilterPanel = new();
    private readonly Dictionary<LibraryFileColumn, CheckBox> _columnChecks = [];
    private readonly ComboBox _groupCombo = new();
    private readonly TextBlock _groupLabel = new();
    private readonly DataGrid _grid = new();
    private readonly Dictionary<LibraryFileColumn, DataGridTextColumn> _columns = [];
    private HashSet<LibraryFileColumn> _visibleColumns = [.. LibraryColumnFilter.Defaults];
    private bool _syncing;
    private int _syncGeneration;
    private bool _syncingColumns;
    private bool _deferActivate;
    private LibraryFileColumn _sortColumn = LibraryFileColumn.Album;
    private LibrarySortDirection _sortDirection = LibrarySortDirection.Ascending;
    private LibraryFileGroup _group = LibraryFileGroup.Album;
    private IReadOnlyList<LibraryFileRow> _rows = [];
    private ObservableCollection<LibraryFileRow> _items = [];
    private readonly Dictionary<string, BitmapSource?> _groupJackets = new(StringComparer.Ordinal);
    private int _anchorIndex;
    private bool _artworkGlow;
    private byte[]? _artworkBytes;
    private int _groupArtLoad;
    private bool _treeSyncing;
    private Point? _treeDragStart;
    private TreeViewItem? _treeDragItem;

    public event EventHandler<DocumentSession>? SessionActivated;

    public event EventHandler<string>? ArtworkDropped;

    public event EventHandler<IReadOnlyCollection<LibraryFileColumn>>? VisibleColumnsChanged;

    public event EventHandler<LibraryFileGroup>? GroupChanged;

    public event EventHandler<string>? ExplorerFolderChanged;

    public event EventHandler<string>? ExplorerFolderOpened;

    public event EventHandler<double>? ExplorerWidthChanged;

    internal event Action? GroupArtworkChanged;

    public LibraryBrowserView()
    {
        Focusable = false;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        BuildLayout();
        ConfigureGrid();
        ConfigureGroupCombo();
        BuildColumnFilter();
        ApplyColumnVisibility(_visibleColumns, notify: false);
        ApplyLocalizedText();
        ApplyGridStyles();
        Loaded += (_, _) =>
        {
            FitColumns();
            SyncGroupColumnHeadersVisibility();
            EnsureGroupHeaderScrollHook();
            SyncGlowDrift();
        };
        IsVisibleChanged += (_, _) => SyncGlowDrift();
        Unloaded += (_, _) => StopGlowDrift();
    }

    private void ApplyGridStyles()
    {
        var accent = Theme.Get("AccentCyanBrush");
        var selected = new SolidColorBrush(Color.FromArgb(80, accent.R, accent.G, accent.B));
        selected.Freeze();

        var headerStyle = new Style(typeof(DataGridColumnHeader));
        headerStyle.Setters.Add(new Setter(
            Control.BackgroundProperty,
            _artworkGlow
                ? Brushes.Transparent
                : new DynamicResourceExtension("ColorPanelBackBrush")));
        headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));
        headerStyle.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("ChromeBorderBrush")));
        headerStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
        headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 4, 8, 4)));
        headerStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        headerStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        headerStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        _grid.ColumnHeaderStyle = headerStyle;

        var cellStyle = new Style(typeof(DataGridCell));
        cellStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        cellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        cellStyle.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        cellStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        cellStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));
        var selectedTrigger = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, selected));
        selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));
        cellStyle.Triggers.Add(selectedTrigger);
        _grid.CellStyle = cellStyle;

        var rowStyle = new Style(typeof(DataGridRow));
        rowStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        rowStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));
        rowStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        var rowSelected = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
        rowSelected.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        rowStyle.Triggers.Add(rowSelected);
        _grid.RowStyle = rowStyle;
        _grid.RowBackground = Brushes.Transparent;
        _grid.AlternatingRowBackground = Brushes.Transparent;
    }

    public bool IsListKeyboardFocused => _grid.IsKeyboardFocusWithin;

    public bool IsGroupComboFocused => _groupCombo.IsKeyboardFocusWithin;

    public bool IsColumnFilterFocused => _columnFilterScroll.IsKeyboardFocusWithin;

    public bool IsExplorerFocused => _folderTree.IsKeyboardFocusWithin;

    public bool IsGroupComboOrigin(System.Windows.DependencyObject? origin)
    {
        while (origin is not null)
        {
            if (ReferenceEquals(origin, _groupCombo))
            {
                return true;
            }

            origin = VisualTreeHelper.GetParent(origin);
        }

        return false;
    }

    public bool IsColumnFilterOrigin(System.Windows.DependencyObject? origin)
    {
        while (origin is not null)
        {
            if (ReferenceEquals(origin, _columnFilterScroll) || ReferenceEquals(origin, _columnFilterPanel))
            {
                return true;
            }

            origin = VisualTreeHelper.GetParent(origin);
        }

        return false;
    }

    public bool IsJacketOrigin(System.Windows.DependencyObject? origin)
    {
        while (origin is not null)
        {
            if (ReferenceEquals(origin, _jacketFrame)
                || ReferenceEquals(origin, _jacketImage)
                || ReferenceEquals(origin, _jacketStack)
                || ReferenceEquals(origin, _jacketReflection))
            {
                return true;
            }

            origin = VisualTreeHelper.GetParent(origin);
        }

        return false;
    }

    public bool IsExplorerOrigin(System.Windows.DependencyObject? origin)
    {
        while (origin is not null)
        {
            if (ReferenceEquals(origin, _folderTree))
            {
                return true;
            }

            origin = VisualTreeHelper.GetParent(origin);
        }

        return false;
    }

    internal object? BoundItemsSource => _grid.ItemsSource;

    internal int RootColumnCount => _root.ColumnDefinitions.Count;

    public DocumentSession? SelectedSession =>
        _grid.SelectedItem is LibraryFileRow { Tag: DocumentSession session } ? session : null;

    public DocumentSession[] SelectedSessions
    {
        get
        {
            if (_grid.SelectedItems.Count == 0)
            {
                return [];
            }

            var result = new List<DocumentSession>(_grid.SelectedItems.Count);
            foreach (var item in _grid.SelectedItems)
            {
                if (item is LibraryFileRow { Tag: DocumentSession session })
                {
                    result.Add(session);
                }
            }

            return [.. result];
        }
    }

    public void FocusList()
    {
        EnsureListFocused();
        if (_grid.SelectedItem is not null)
        {
            EnsureRowVisible(_grid.SelectedItem);
        }
    }

    public void MoveSelection(int delta, bool extend = false)
    {
        if (delta == 0 || _grid.Items.Count == 0)
        {
            return;
        }

        var index = _grid.SelectedIndex;
        if (index < 0)
        {
            index = delta > 0 ? 0 : _grid.Items.Count - 1;
        }
        else
        {
            index = Math.Clamp(index + delta, 0, _grid.Items.Count - 1);
        }

        ApplyMoveSelection(index, extend);
    }

    public void MoveSelectionToEdge(int edge, bool extend = false)
    {
        var index = LibraryPlayerMode.EdgeIndex(_grid.Items.Count, edge);
        if (index < 0)
        {
            return;
        }

        ApplyMoveSelection(index, extend);
    }

    public void MoveSelectionPage(int direction, bool extend = false)
    {
        if (direction == 0 || _grid.Items.Count == 0)
        {
            return;
        }

        var step = LibraryPlayerMode.PageStep(VisibleRowCount());
        MoveSelection(direction < 0 ? -step : step, extend);
    }

    private void ApplyMoveSelection(int index, bool extend)
    {
        _deferActivate = true;
        try
        {
            if (!extend)
            {
                _anchorIndex = index;
                _grid.SelectedIndex = index;
            }
            else
            {
                SelectRange(_anchorIndex, index);
            }
        }
        finally
        {
            _deferActivate = false;
        }

        EnsureListFocused();
        if (_grid.Items[index] is LibraryFileRow row)
        {
            EnsureRowVisible(row);
        }
    }

    private int VisibleRowCount()
    {
        if (FindDataGridScrollViewer() is { } scroll)
        {
            if (scroll.CanContentScroll && scroll.ViewportHeight >= 1)
            {
                return Math.Max(1, (int)scroll.ViewportHeight);
            }

            if (scroll.ViewportHeight > 0)
            {
                var rowHeight = RowHeightHint();
                if (rowHeight > 1)
                {
                    return Math.Max(1, (int)(scroll.ViewportHeight / rowHeight));
                }
            }
        }

        return 1;
    }

    private double RowHeightHint()
    {
        var item = _grid.SelectedItem ?? (_grid.Items.Count > 0 ? _grid.Items[0] : null);
        if (item is not null
            && _grid.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement element
            && element.ActualHeight > 1)
        {
            return element.ActualHeight;
        }

        return 24;
    }

    private ScrollViewer? FindDataGridScrollViewer()
    {
        return FindDescendantScrollViewer(_grid);
    }

    private static ScrollViewer? FindDescendantScrollViewer(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer scroll)
            {
                return scroll;
            }

            var nested = FindDescendantScrollViewer(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private void EnsureListFocused()
    {
        if (_grid.IsKeyboardFocusWithin)
        {
            return;
        }

        if (_grid.SelectedItem is { } item
            && _grid.ItemContainerGenerator.ContainerFromItem(item) is UIElement row)
        {
            row.Focus();
            return;
        }

        if (_grid.SelectedItem is not null || _grid.Items.Count == 0)
        {
            _grid.Focus();
        }
    }

    private void EnsureRowVisible(object item)
    {
        if (_grid.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement element
            && _grid.ActualHeight > 0)
        {
            try
            {
                var bounds = element.TransformToAncestor(_grid)
                    .TransformBounds(new Rect(element.RenderSize));
                var header = _grid.ColumnHeaderHeight;
                if (double.IsNaN(header) || header <= 0)
                {
                    header = 28;
                }

                if (bounds.Top >= header && bounds.Bottom <= _grid.ActualHeight)
                {
                    return;
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        _grid.ScrollIntoView(item);
    }

    public void SelectAllRows()
    {
        if (_grid.Items.Count == 0)
        {
            return;
        }

        _syncing = true;
        try
        {
            _grid.SelectAll();
        }
        finally
        {
            _syncing = false;
        }

        FocusList();
    }

    public void ApplyLocalizedText()
    {
        _groupLabel.Text = UiStrings.LibraryGroupLabel;
        foreach (var column in LibraryColumnFilter.All)
        {
            SetColumnHeader(column, ColumnHeader(column));
            if (_columnChecks.TryGetValue(column, out var check))
            {
                check.Content = new TextBlock
                {
                    Text = ColumnHeader(column),
                    TextWrapping = TextWrapping.Wrap,
                };
            }
        }

        FillGroupOptions();
        TipService.Set(_grid, UiStrings.TipLibraryList);
        TipService.Set(_jacketFrame, UiStrings.TipLibraryJacket);
        TipService.Set(_columnFilterScroll, UiStrings.TipLibraryColumns);
        TipService.Set(_groupCombo, UiStrings.TipLibraryList);
        TipService.Set(_folderTree, UiStrings.TipLibraryExplorer);
        RelabelExplorerRoots();
        FitColumns();
        NotifyGroupColumnHeadersChanged();
    }

    public void RefreshAppearance()
    {
        ApplyGlowVeil();
        ApplyGridStyles();
        ApplyExplorerStyle();
    }

    internal static double GlowVeilOpacityFor(UiTheme theme) =>
        theme == UiTheme.Light ? GlowVeilOpacityLight : GlowVeilOpacityDark;

    public void SetSessions(
        IReadOnlyList<DocumentSession> sessions,
        DocumentSession? active,
        IReadOnlyList<DocumentSession>? selected = null)
    {
        var preserve = selected ?? SelectedSessions;
        var rows = new LibraryFileRow[sessions.Count];
        for (var i = 0; i < sessions.Count; i++)
        {
            rows[i] = CreateRow(sessions[i]);
        }

        _rows = LibraryFileList.Sort(rows, _sortColumn, _sortDirection);
        LibraryFileList.ApplyGroupKeys(_rows, _group);
        BeginRowSync();
        try
        {
            if (TryPatchBoundRows(_rows))
            {
                ApplyRowSelectionCore(active, preserve);
            }
            else
            {
                BindRowsCore(active, preserve);
            }
        }
        finally
        {
            EndRowSync();
        }

        RequestFitColumns();
        InvalidateGroupJackets();
        _ = EnsureGroupArtworkAsync();
    }

    public void UpdateSessionRow(DocumentSession session)
    {
        var next = CreateRow(session);
        for (var i = 0; i < _items.Count; i++)
        {
            if (!ReferenceEquals(_items[i].Tag, session))
            {
                continue;
            }

            next.GroupKey = LibraryFileList.GroupLabel(next, _group);
            if (LibraryFileList.SameContent(_items[i], next))
            {
                InvalidateGroupJackets();
                return;
            }

            var preserve = SelectedSessions;
            var updated = new LibraryFileRow[_rows.Count];
            for (var r = 0; r < _rows.Count; r++)
            {
                updated[r] = ReferenceEquals(_rows[r].Tag, session) ? next : _rows[r];
            }

            _rows = updated;
            BeginRowSync();
            try
            {
                _items[i] = next;
                ApplyRowSelectionCore(session, preserve.Length > 0 ? preserve : [session]);
            }
            finally
            {
                EndRowSync();
            }

            RequestFitColumns();
            InvalidateGroupJackets();
            return;
        }
    }

    /// <summary>
    /// フォルダ読み込み用。1 件追加して現在の並べ替え位置へ挿入する。全件 SetSessions より軽い。
    /// </summary>
    public void AppendSession(DocumentSession session, bool select)
    {
        var row = CreateRow(session);
        row.GroupKey = LibraryFileList.GroupLabel(row, _group);

        if (_grid.ItemsSource is null || _items.Count == 0)
        {
            _rows = [row];
            BeginRowSync();
            try
            {
                BindRowsCore(select ? session : null, select ? [session] : null);
            }
            finally
            {
                EndRowSync();
            }

            RequestFitColumns();
            return;
        }

        var insertAt = 0;
        while (insertAt < _items.Count
            && LibraryFileList.Compare(_items[insertAt], row, _sortColumn, _sortDirection) <= 0)
        {
            insertAt++;
        }

        BeginRowSync();
        try
        {
            _items.Insert(insertAt, row);
            var nextRows = new LibraryFileRow[_rows.Count + 1];
            for (var i = 0; i < insertAt; i++)
            {
                nextRows[i] = _rows[i];
            }

            nextRows[insertAt] = row;
            for (var i = insertAt; i < _rows.Count; i++)
            {
                nextRows[i + 1] = _rows[i];
            }

            _rows = nextRows;
            if (select)
            {
                ApplyRowSelectionCore(session, [session]);
            }
        }
        finally
        {
            EndRowSync();
        }
    }

    public void FinishIncrementalSessionLoad()
    {
        RequestFitColumns();
        InvalidateGroupJackets();
        _ = EnsureGroupArtworkAsync();
    }

    public void SetArtwork(byte[]? bytes)
    {
        if (ArtworkEquals(_artworkBytes, bytes))
        {
            return;
        }

        _artworkBytes = bytes is { Length: > 0 } ? bytes : null;
        var bitmap = TryCreateBitmap(_artworkBytes, ArtworkDecodeMaxEdge);
        _jacketImage.Source = bitmap;
        _jacketReflection.Source = bitmap;
        _jacketReflection.Visibility = bitmap is null ? Visibility.Collapsed : Visibility.Visible;
        SyncJacketReflectionSize();
        ApplyArtworkGlow(bitmap);
    }

    private void SyncJacketReflectionSize()
    {
        var w = _jacketImage.ActualWidth;
        var h = _jacketImage.ActualHeight;
        if (w < 1 || h < 1)
        {
            if (_jacketImage.Source is not BitmapSource bmp || bmp.PixelWidth < 1 || bmp.PixelHeight < 1)
            {
                return;
            }

            LibraryJacketReflectionView.FitWithin(
                DesignMetrics.LibraryJacketSize,
                bmp.PixelWidth,
                bmp.PixelHeight,
                out w,
                out h);
        }

        _jacketReflection.SyncFaceSize(w, h);
    }

    private void BindRows(DocumentSession? active, IReadOnlyList<DocumentSession>? selected)
    {
        BeginRowSync();
        try
        {
            BindRowsCore(active, selected);
        }
        finally
        {
            EndRowSync();
        }

        RequestFitColumns();
    }

    private void BindRowsCore(DocumentSession? active, IReadOnlyList<DocumentSession>? selected)
    {
        _items = new ObservableCollection<LibraryFileRow>(_rows);
        var view = new ListCollectionView(_items);
        if (_group != LibraryFileGroup.None)
        {
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LibraryFileRow.GroupKey)));
        }

        _grid.ItemsSource = view;
        ApplyRowSelectionCore(active, selected);
    }

    private void BeginRowSync()
    {
        _syncing = true;
        _syncGeneration++;
    }

    private void EndRowSync()
    {
        var gen = _syncGeneration;
        Dispatcher.BeginInvoke(
            () =>
            {
                if (gen == _syncGeneration)
                {
                    _syncing = false;
                }
            },
            DispatcherPriority.Loaded);
    }

    private IReadOnlyList<LibraryFileRow> BoundRows =>
        _grid.ItemsSource is null ? _rows : _items;

    private void ApplyRowSelectionCore(DocumentSession? active, IReadOnlyList<DocumentSession>? selected)
    {
        var keep = selected is { Count: > 0 }
            ? selected
            : active is null ? [] : (IReadOnlyList<DocumentSession>)[active];
        LibraryFileRow? current = null;
        _grid.SelectedItems.Clear();
        foreach (var row in BoundRows)
        {
            if (row.Tag is not DocumentSession session)
            {
                continue;
            }

            var isKept = false;
            foreach (var item in keep)
            {
                if (ReferenceEquals(item, session))
                {
                    isKept = true;
                    break;
                }
            }

            if (!isKept)
            {
                continue;
            }

            _grid.SelectedItems.Add(row);
            if (active is not null && ReferenceEquals(session, active))
            {
                current = row;
            }

            current ??= row;
        }

        if (current is null && active is not null)
        {
            foreach (var row in BoundRows)
            {
                if (ReferenceEquals(row.Tag, active))
                {
                    _grid.SelectedItems.Add(row);
                    current = row;
                    break;
                }
            }
        }

        if (current is not null)
        {
            _grid.SelectedItem = current;
            foreach (var row in BoundRows)
            {
                if (row.Tag is DocumentSession session
                    && !_grid.SelectedItems.Contains(row))
                {
                    foreach (var item in keep)
                    {
                        if (ReferenceEquals(item, session))
                        {
                            _grid.SelectedItems.Add(row);
                            break;
                        }
                    }
                }
            }

            _anchorIndex = _grid.SelectedIndex;
            _grid.ScrollIntoView(current);
        }
        else
        {
            _anchorIndex = -1;
        }
    }

    private void SelectRange(int from, int to)
    {
        if (_grid.Items.Count == 0)
        {
            return;
        }

        from = Math.Clamp(from, 0, _grid.Items.Count - 1);
        to = Math.Clamp(to, 0, _grid.Items.Count - 1);
        var lo = Math.Min(from, to);
        var hi = Math.Max(from, to);
        _syncing = true;
        try
        {
            _grid.SelectedItems.Clear();
            for (var i = lo; i <= hi; i++)
            {
                _grid.SelectedItems.Add(_grid.Items[i]);
            }

            _grid.SelectedItem = _grid.Items[to];
        }
        finally
        {
            _syncing = false;
        }
    }

    public void SetExplorerFolder(string path) => RevealFolder(path);

    public void SetExplorerWidth(double width)
    {
        var clamped = DesignMetrics.ClampLibraryExplorerWidth(width);
        _treeColumn.Width = new GridLength(clamped);
    }

    public double ReadExplorerWidth()
    {
        var raw = _treeColumn.ActualWidth > 0
            ? _treeColumn.ActualWidth
            : _treeColumn.Width.Value;
        return DesignMetrics.ClampLibraryExplorerWidth(raw);
    }

    public void OpenSelectedFolder()
    {
        if (TryGetSelectedExplorerFolder(out var folder))
        {
            ExplorerFolderOpened?.Invoke(this, folder);
        }
    }

    public bool TryGetSelectedExplorerFolder(out string path)
    {
        if (_folderTree.SelectedItem is TreeViewItem { Tag: string folder }
            && Directory.Exists(folder))
        {
            path = folder;
            return true;
        }

        path = string.Empty;
        return false;
    }

    private void BuildLayout()
    {
        var treeWidth = DesignMetrics.LibraryExplorerWidth;
        _treeColumn.Width = new GridLength(treeWidth);
        _treeColumn.MinWidth = DesignMetrics.LibraryExplorerMinWidth;
        _treeColumn.MaxWidth = DesignMetrics.LibraryExplorerMaxWidth;
        var jacketOuter = DesignMetrics.LibraryJacketSize + 12;
        _jacketColumn.Width = new GridLength(jacketOuter);
        _jacketColumn.MinWidth = jacketOuter;
        _jacketColumn.MaxWidth = jacketOuter;
        _root.ColumnDefinitions.Add(_treeColumn);
        _root.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(DesignMetrics.MeterColumnSplitterWidth),
        });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _root.ColumnDefinitions.Add(_jacketColumn);

        ApplyExplorerStyle();
        _folderTree.ContextMenu = null;
        _folderTree.AllowDrop = false;
        _folderTree.SelectedItemChanged += FolderTree_SelectedItemChanged;
        _folderTree.MouseDoubleClick += FolderTree_MouseDoubleClick;
        _folderTree.PreviewMouseLeftButtonDown += FolderTree_PreviewMouseLeftButtonDown;
        _folderTree.PreviewMouseMove += FolderTree_PreviewMouseMove;
        _folderTree.PreviewMouseLeftButtonUp += FolderTree_PreviewMouseLeftButtonUp;
        _folderTree.PreviewMouseRightButtonDown += (_, e) => e.Handled = true;
        _folderTree.PreviewMouseRightButtonUp += (_, e) => e.Handled = true;
        _folderTree.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, e) => e.Handled = true));
        _folderTree.CommandBindings.Add(new CommandBinding(ApplicationCommands.Cut, (_, e) => e.Handled = true));
        _folderTree.CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste, (_, e) => e.Handled = true));
        _folderTree.CommandBindings.Add(new CommandBinding(ApplicationCommands.Delete, (_, e) => e.Handled = true));
        BuildExplorerRoots();

        var treeHost = new Border
        {
            BorderThickness = new Thickness(0),
            Child = _folderTree,
        };
        treeHost.SetResourceReference(Border.BackgroundProperty, "SurfaceBackBrush");
        Grid.SetColumn(treeHost, 0);
        _root.Children.Add(treeHost);

        var splitter = new GridSplitter
        {
            Width = DesignMetrics.MeterColumnSplitterWidth,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            ResizeDirection = GridResizeDirection.Columns,
            Cursor = Cursors.SizeWE,
        };
        splitter.SetResourceReference(BackgroundProperty, "ChromeBorderBrush");
        splitter.DragCompleted += ExplorerSplitter_DragCompleted;
        Grid.SetColumn(splitter, 1);
        _root.Children.Add(splitter);

        _jacketImage.Stretch = Stretch.Uniform;
        _jacketImage.SnapsToDevicePixels = true;
        _jacketImage.MaxWidth = DesignMetrics.LibraryJacketSize;
        _jacketImage.MaxHeight = DesignMetrics.LibraryJacketSize;
        // 枠は最大正方形。実画像サイズに合わせて反射を密着させる。
        _jacketImage.Width = double.NaN;
        _jacketImage.Height = double.NaN;
        _jacketImage.HorizontalAlignment = HorizontalAlignment.Center;
        _jacketImage.VerticalAlignment = VerticalAlignment.Bottom;
        RenderOptions.SetBitmapScalingMode(_jacketImage, BitmapScalingMode.HighQuality);
        _jacketFrame.Child = _jacketImage;
        _jacketFrame.Width = DesignMetrics.LibraryJacketSize;
        _jacketFrame.MinHeight = 0;
        _jacketFrame.HorizontalAlignment = HorizontalAlignment.Left;
        _jacketFrame.VerticalAlignment = VerticalAlignment.Top;
        _jacketFrame.Margin = new Thickness(0);
        _jacketFrame.Padding = new Thickness(0);
        _jacketFrame.AllowDrop = true;
        _jacketFrame.BorderThickness = new Thickness(0);
        _jacketFrame.Background = Brushes.Transparent;
        _jacketFrame.PreviewDragOver += Jacket_PreviewDragOver;
        _jacketFrame.Drop += Jacket_Drop;
        _jacketImage.SizeChanged += (_, _) => SyncJacketReflectionSize();

        _jacketReflection = new LibraryJacketReflectionView(DesignMetrics.LibraryJacketSize)
        {
            Visibility = Visibility.Collapsed,
        };

        _jacketStack.Orientation = Orientation.Vertical;
        _jacketStack.HorizontalAlignment = HorizontalAlignment.Left;
        _jacketStack.Margin = new Thickness(4, 8, 8, 8);
        _jacketStack.Children.Add(_jacketFrame);
        _jacketStack.Children.Add(_jacketReflection);

        _columnFilterScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _columnFilterScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _columnFilterScroll.Margin = new Thickness(4, 0, 8, 8);
        _columnFilterScroll.Padding = new Thickness(0);
        _columnFilterScroll.Focusable = true;
        _columnFilterScroll.Background = Brushes.Transparent;
        _columnFilterPanel.Background = Brushes.Transparent;
        _columnFilterScroll.Content = _columnFilterPanel;
        _columnFilterPanel.Orientation = Orientation.Vertical;

        var right = new DockPanel();
        right.SetResourceReference(Panel.BackgroundProperty, "SurfaceBackBrush");
        DockPanel.SetDock(_jacketStack, Dock.Top);
        right.Children.Add(_jacketStack);
        right.Children.Add(_columnFilterScroll);
        Grid.SetColumn(right, 3);
        _root.Children.Add(right);

        _groupLabel.VerticalAlignment = VerticalAlignment.Center;
        _groupLabel.Margin = new Thickness(0, 0, 8, 0);
        _groupLabel.FontSize = 11;
        _groupLabel.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryForeBrush");
        _groupCombo.MinWidth = DesignMetrics.From96(140);
        _groupCombo.Height = DesignMetrics.AudioInputHeight;
        _groupCombo.FontSize = 11;
        _groupCombo.VerticalContentAlignment = VerticalAlignment.Center;
        _groupCombo.SetResourceReference(StyleProperty, "DarkComboBoxStyle");
        _groupCombo.SelectionChanged += GroupCombo_SelectionChanged;

        var bar = new DockPanel { LastChildFill = false, Margin = new Thickness(8, 6, 8, 6) };
        bar.Children.Add(_groupLabel);
        bar.Children.Add(_groupCombo);

        var list = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        list.Children.Add(bar);
        list.Children.Add(_grid);

        var drift = new TransformGroup();
        drift.Children.Add(_glowScale);
        drift.Children.Add(_glowTranslate);
        _glowHost.RenderTransform = drift;
        _glowHost.RenderTransformOrigin = new Point(0.5, 0.5);
        _glowHost.IsHitTestVisible = false;
        _glowHost.SnapsToDevicePixels = false;
        _glowHost.UseLayoutRounding = false;
        _glowHost.Margin = new Thickness(-GlowDriftBleed);
        _glowHost.Visibility = Visibility.Collapsed;

        _veil.IsHitTestVisible = false;
        _veil.Visibility = Visibility.Collapsed;
        ApplyGlowVeil();

        var listPane = new Grid { ClipToBounds = true };
        listPane.SetResourceReference(Panel.BackgroundProperty, "SurfaceBackBrush");
        listPane.Children.Add(_glowHost);
        listPane.Children.Add(_veil);
        listPane.Children.Add(list);
        Grid.SetColumn(listPane, 2);
        _root.Children.Add(listPane);
        _root.Background = Brushes.Transparent;

        _host.ClipToBounds = true;
        _host.Children.Add(_root);
        Content = _host;
        SetResourceReference(BackgroundProperty, "SurfaceBackBrush");
    }

    private void ApplyExplorerStyle()
    {
        _folderTree.Background = Brushes.Transparent;
        _folderTree.BorderThickness = new Thickness(0);
        _folderTree.Padding = new Thickness(4, 6, 4, 6);
        _folderTree.FontSize = 11;
        ScrollViewer.SetHorizontalScrollBarVisibility(_folderTree, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(_folderTree, ScrollBarVisibility.Auto);
        _folderTree.SetResourceReference(ForegroundProperty, "PrimaryForeBrush");
        VirtualizingPanel.SetIsVirtualizing(_folderTree, true);
        VirtualizingPanel.SetVirtualizationMode(_folderTree, VirtualizationMode.Recycling);

        // 既定のシステム選択色（非アクティブ時の白など）をアプリのハイライトに差し替える。
        var highlight = ResolveThemeBrush("MenuHighlightBackBrush", Color.FromRgb(0x37, 0x37, 0x3A));
        var fore = ResolveThemeBrush("PrimaryForeBrush", Color.FromRgb(0xE8, 0xE8, 0xEA));
        _folderTree.Resources[SystemColors.HighlightBrushKey] = highlight;
        _folderTree.Resources[SystemColors.HighlightTextBrushKey] = fore;
        _folderTree.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = highlight;
        _folderTree.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = fore;
        _folderTree.Resources[SystemColors.ControlBrushKey] = Brushes.Transparent;

        var itemStyle = new Style(typeof(TreeViewItem));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2, 1, 2, 1)));
        itemStyle.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        var selected = new Trigger { Property = TreeViewItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));
        selected.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("MenuHighlightBackBrush")));
        itemStyle.Triggers.Add(selected);
        var inactive = new MultiTrigger();
        inactive.Conditions.Add(new Condition(TreeViewItem.IsSelectedProperty, true));
        inactive.Conditions.Add(new Condition(TreeViewItem.IsSelectionActiveProperty, false));
        inactive.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));
        inactive.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("MenuHighlightBackBrush")));
        itemStyle.Triggers.Add(inactive);
        _folderTree.ItemContainerStyle = itemStyle;
    }

    private static Brush ResolveThemeBrush(string key, Color fallback)
    {
        if (Application.Current?.TryFindResource(key) is Brush brush)
        {
            return brush;
        }

        var created = new SolidColorBrush(fallback);
        created.Freeze();
        return created;
    }

    private void BuildExplorerRoots()
    {
        _folderTree.Items.Clear();
        AddSpecialRoot(Environment.SpecialFolder.Desktop, UiStrings.LibraryExplorerDesktop);
        AddSpecialRoot(Environment.SpecialFolder.MyDocuments, UiStrings.LibraryExplorerDocuments);
        AddSpecialRoot(Environment.SpecialFolder.MyMusic, UiStrings.LibraryExplorerMusic);
        AddSpecialRoot(Environment.SpecialFolder.MyPictures, UiStrings.LibraryExplorerPictures);
        AddSpecialRoot(Environment.SpecialFolder.MyVideos, UiStrings.LibraryExplorerVideos);
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady)
                {
                    continue;
                }

                AddFolderItem(_folderTree.Items, drive.RootDirectory.FullName, DriveHeader(drive));
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void RelabelExplorerRoots()
    {
        foreach (var item in _folderTree.Items.OfType<TreeViewItem>())
        {
            if (item.Tag is not string path)
            {
                continue;
            }

            item.Header = ExplorerRootHeader(path);
        }
    }

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

    private void AddSpecialRoot(Environment.SpecialFolder folder, string header)
    {
        var path = SpecialFolderPath(folder);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        AddFolderItem(_folderTree.Items, path, header);
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
        var item = new TreeViewItem
        {
            Header = header,
            Tag = path,
        };
        item.Expanded += FolderItem_Expanded;
        if (HasAnySubdir(path))
        {
            item.Items.Add(new TreeViewItem());
        }

        items.Add(item);
        return item;
    }

    private void FolderItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(sender, e.OriginalSource))
        {
            return;
        }

        if (sender is TreeViewItem item)
        {
            LoadChildren(item);
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
            AddFolderItem(item.Items, dir, FolderDisplayName(dir));
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

    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_treeSyncing)
        {
            return;
        }

        if (e.NewValue is TreeViewItem { Tag: string path } && Directory.Exists(path))
        {
            ExplorerFolderChanged?.Invoke(this, path);
        }
    }

    private void FolderTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenSelectedFolder();
    }

    private void FolderTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _treeDragStart = null;
        _treeDragItem = null;
        if (e.OriginalSource is DependencyObject origin
            && FindTreeViewItem(origin) is { Tag: string } item
            && FindTreeExpandToggle(origin) is null)
        {
            _treeDragStart = e.GetPosition(null);
            _treeDragItem = item;
        }
    }

    private void FolderTree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_treeDragStart is null
            || _treeDragItem is null
            || e.LeftButton != MouseButtonState.Pressed
            || _treeDragItem.Tag is not string path
            || !Directory.Exists(path))
        {
            return;
        }

        var delta = e.GetPosition(null) - _treeDragStart.Value;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var item = _treeDragItem;
        _treeDragStart = null;
        _treeDragItem = null;
        var data = new DataObject(DataFormats.FileDrop, new[] { path });
        DragDrop.DoDragDrop(item, data, DragDropEffects.Copy);
    }

    private void FolderTree_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _treeDragStart = null;
        _treeDragItem = null;
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

    private static bool HasAnySubdir(string path)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(path))
            {
                if (!ShouldSkipFolder(dir))
                {
                    return true;
                }
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

    private static string NormalizeFolderPath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

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

    private void ConfigureGrid()
    {
        AddColumn(LibraryFileColumn.Name, nameof(LibraryFileRow.Name), min: 96);
        AddColumn(LibraryFileColumn.Title, nameof(LibraryFileRow.Title), min: 80);
        AddColumn(LibraryFileColumn.Artist, nameof(LibraryFileRow.Artist), min: 72);
        AddColumn(LibraryFileColumn.Album, nameof(LibraryFileRow.Album), min: 72);
        AddColumn(LibraryFileColumn.Track, nameof(LibraryFileRow.Track), min: 44, right: true);
        AddColumn(LibraryFileColumn.Disc, nameof(LibraryFileRow.Disc), min: 40, right: true);
        AddColumn(LibraryFileColumn.Year, nameof(LibraryFileRow.Year), min: 48, right: true);
        AddColumn(LibraryFileColumn.Genre, nameof(LibraryFileRow.Genre), min: 64);
        AddColumn(LibraryFileColumn.Composer, nameof(LibraryFileRow.Composer), min: 72);
        AddColumn(LibraryFileColumn.Duration, nameof(LibraryFileRow.DurationText), min: 88, right: true);
        AddColumn(LibraryFileColumn.Comment, nameof(LibraryFileRow.Comment), min: 72);
        AddColumn(LibraryFileColumn.AlbumArtist, nameof(LibraryFileRow.AlbumArtist), min: 80);
        AddColumn(LibraryFileColumn.Kind, nameof(LibraryFileRow.Kind), min: 56);
        AddColumn(LibraryFileColumn.SampleRate, nameof(LibraryFileRow.SampleRateText), min: 64, right: true);
        AddColumn(LibraryFileColumn.BitDepth, nameof(LibraryFileRow.BitDepthText), min: 52, right: true);
        AddColumn(LibraryFileColumn.Channels, nameof(LibraryFileRow.ChannelsText), min: 44, right: true);
        AddColumn(LibraryFileColumn.BitRate, nameof(LibraryFileRow.BitRateText), min: 72, right: true);
        AddColumn(LibraryFileColumn.Size, nameof(LibraryFileRow.SizeText), min: 72, right: true);
        AddColumn(LibraryFileColumn.Folder, nameof(LibraryFileRow.Folder), min: 80);
        AddColumn(LibraryFileColumn.Jacket, nameof(LibraryFileRow.JacketText), min: 72);

        if (_columns.TryGetValue(LibraryFileColumn.Album, out var albumColumn))
        {
            albumColumn.SortDirection = System.ComponentModel.ListSortDirection.Ascending;
        }

        _grid.AutoGenerateColumns = false;
        _grid.IsReadOnly = true;
        _grid.CanUserAddRows = false;
        _grid.CanUserDeleteRows = false;
        _grid.CanUserReorderColumns = true;
        _grid.CanUserSortColumns = true;
        _grid.CanUserResizeRows = false;
        _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        _grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
        _grid.SelectionUnit = DataGridSelectionUnit.FullRow;
        _grid.SelectionMode = DataGridSelectionMode.Extended;
        _grid.ClipboardCopyMode = DataGridClipboardCopyMode.None;
        _grid.EnableRowVirtualization = false;
        _grid.RowHeaderWidth = 0;
        _grid.MinRowHeight = 24;
        _grid.VerticalContentAlignment = VerticalAlignment.Center;
        _grid.BorderThickness = new Thickness(0);
        _grid.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _grid.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _grid.Background = Brushes.Transparent;
        _grid.SetResourceReference(ForegroundProperty, "PrimaryForeBrush");
        _grid.SetResourceReference(DataGrid.HorizontalGridLinesBrushProperty, "ChromeBorderBrush");
        _grid.Sorting += Grid_Sorting;
        _grid.SelectionChanged += Grid_SelectionChanged;
        _grid.MouseDoubleClick += Grid_MouseDoubleClick;

        var headerFactory = new FrameworkElementFactory(typeof(TextBlock));
        headerFactory.SetBinding(TextBlock.TextProperty, new Binding("Name"));
        headerFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        headerFactory.SetValue(TextBlock.FontSizeProperty, 13d);
        headerFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
        headerFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        headerFactory.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryForeBrush");

        var lineFactory = new FrameworkElementFactory(typeof(Border));
        lineFactory.SetValue(FrameworkElement.HeightProperty, 1d);
        lineFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        lineFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 10, 0, 0));
        lineFactory.SetResourceReference(Border.BackgroundProperty, "ChromeBorderBrush");

        var titleBar = new FrameworkElementFactory(typeof(DockPanel));
        titleBar.SetValue(DockPanel.LastChildFillProperty, true);
        titleBar.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 8, 8, 4));
        titleBar.SetValue(DockPanel.DockProperty, Dock.Top);
        headerFactory.SetValue(DockPanel.DockProperty, Dock.Left);
        titleBar.AppendChild(headerFactory);
        titleBar.AppendChild(lineFactory);

        // 列名はグループ見出しの下・ジャケット右。上部ヘッダーはグループ時に隠す。
        var headers = new FrameworkElementFactory(typeof(LibraryGroupColumnHeaders));
        headers.SetValue(DockPanel.DockProperty, Dock.Top);
        headers.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 2));

        var jacket = new FrameworkElementFactory(typeof(LibraryGroupJacketImage));
        jacket.SetValue(DockPanel.DockProperty, Dock.Left);

        var items = new FrameworkElementFactory(typeof(ItemsPresenter));

        var content = new FrameworkElementFactory(typeof(DockPanel));
        content.SetValue(DockPanel.LastChildFillProperty, true);
        content.AppendChild(headers);
        content.AppendChild(items);

        var stage = new FrameworkElementFactory(typeof(DockPanel));
        stage.SetValue(DockPanel.LastChildFillProperty, true);
        stage.AppendChild(jacket);
        stage.AppendChild(content);

        var body = new FrameworkElementFactory(typeof(DockPanel));
        body.SetValue(DockPanel.LastChildFillProperty, true);
        body.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 10));
        body.AppendChild(titleBar);
        body.AppendChild(stage);

        var template = new ControlTemplate(typeof(GroupItem)) { VisualTree = body };
        var container = new Style(typeof(GroupItem));
        container.Setters.Add(new Setter(Control.TemplateProperty, template));
        container.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        container.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));

        _grid.GroupStyle.Clear();
        _grid.GroupStyle.Add(new GroupStyle
        {
            ContainerStyle = container,
            HidesIfEmpty = true,
        });
        SyncGroupColumnHeadersVisibility();
    }

    /// <summary>グループ左のジャケット幅（余白込み）。</summary>
    internal static double GroupJacketColumnWidth =>
        DesignMetrics.LibraryGroupJacketSize + 16;

    private void SyncGroupColumnHeadersVisibility()
    {
        // グループ時は見出しの下に列名を出すので、上部ヘッダーは隠す。
        _grid.HeadersVisibility = _group == LibraryFileGroup.None
            ? DataGridHeadersVisibility.Column
            : DataGridHeadersVisibility.None;
        NotifyGroupColumnHeadersChanged();
    }

    internal event EventHandler? GroupColumnHeadersChanged;
    internal event EventHandler<double>? GroupHeaderScrollOffsetChanged;

    private void NotifyGroupColumnHeadersChanged() =>
        GroupColumnHeadersChanged?.Invoke(this, EventArgs.Empty);

    private bool _gridScrollHooked;

    private void EnsureGroupHeaderScrollHook()
    {
        if (_gridScrollHooked)
        {
            return;
        }

        if (FindDescendant<ScrollViewer>(_grid) is not { } scroll)
        {
            return;
        }

        scroll.ScrollChanged += GridScroll_ScrollChanged;
        _gridScrollHooked = true;
    }

    private void GridScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.HorizontalChange == 0 && e.ExtentWidthChange == 0 && e.ViewportWidthChange == 0)
        {
            return;
        }

        GroupHeaderScrollOffsetChanged?.Invoke(this, ((ScrollViewer)sender).HorizontalOffset);
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            var nested = FindDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    internal IEnumerable<(string Header, double Width, DataGridColumn Column)> EnumerateVisibleGridColumns()
    {
        foreach (var column in _grid.Columns)
        {
            if (column.Visibility != Visibility.Visible)
            {
                continue;
            }

            var width = column.ActualWidth > 1 ? column.ActualWidth : column.Width.DisplayValue;
            if (width <= 0)
            {
                width = column.MinWidth;
            }

            yield return (column.Header?.ToString() ?? string.Empty, width, column);
        }
    }

    internal void SortByGridColumn(DataGridColumn column)
    {
        var args = new DataGridSortingEventArgs(column);
        Grid_Sorting(_grid, args);
    }

    private void ConfigureGroupCombo()
    {
        _groupCombo.DisplayMemberPath = nameof(GroupOption.Label);
        _groupCombo.SelectedValuePath = nameof(GroupOption.Group);
    }

    private void FillGroupOptions()
    {
        var selected = _group;
        _groupCombo.ItemsSource = new GroupOption[]
        {
            new(LibraryFileGroup.None, UiStrings.LibraryGroupNone),
            new(LibraryFileGroup.Title, UiStrings.LibraryGroupTitle),
            new(LibraryFileGroup.Artist, UiStrings.LibraryGroupArtist),
            new(LibraryFileGroup.Album, UiStrings.LibraryGroupAlbum),
            new(LibraryFileGroup.Genre, UiStrings.LibraryGroupGenre),
            new(LibraryFileGroup.Year, UiStrings.LibraryGroupYear),
            new(LibraryFileGroup.Kind, UiStrings.LibraryGroupKind),
            new(LibraryFileGroup.SampleRate, UiStrings.LibraryGroupSampleRate),
            new(LibraryFileGroup.BitDepth, UiStrings.LibraryGroupBitDepth),
            new(LibraryFileGroup.Channels, UiStrings.LibraryGroupChannels),
            new(LibraryFileGroup.Folder, UiStrings.LibraryGroupFolder),
        };
        _groupCombo.SelectedValue = selected;
    }

    public void SetVisibleColumns(IEnumerable<LibraryFileColumn> columns)
    {
        ApplyColumnVisibility(LibraryColumnFilter.Resolve(LibraryColumnFilter.Serialize(columns)), notify: false);
    }

    public void SetGroup(LibraryFileGroup group)
    {
        if (_group == group)
        {
            if (!Equals(_groupCombo.SelectedValue, group))
            {
                _groupCombo.SelectedValue = group;
            }

            return;
        }

        _group = group;
        _groupCombo.SelectedValue = group;
        SyncGroupColumnHeadersVisibility();
        if (_grid.ItemsSource is null)
        {
            return;
        }

        var active = SelectedSession;
        var selected = SelectedSessions;
        LibraryFileList.ApplyGroupKeys(_rows, _group);
        BindRows(active, selected);
        InvalidateGroupJackets();
        _ = EnsureGroupArtworkAsync();
    }

    private void BuildColumnFilter()
    {
        _columnFilterPanel.Children.Clear();
        _columnChecks.Clear();
        foreach (var column in LibraryColumnFilter.All)
        {
            var locked = LibraryColumnFilter.IsLocked(column);
            var check = new CheckBox
            {
                Tag = column,
                Content = new TextBlock
                {
                    Text = ColumnHeader(column),
                    TextWrapping = TextWrapping.Wrap,
                },
                Margin = new Thickness(0, 0, 0, 6),
                IsEnabled = !locked,
                VerticalContentAlignment = VerticalAlignment.Center,
                Focusable = true,
            };
            check.SetResourceReference(StyleProperty, "DarkCheckBoxStyle");

            check.Checked += ColumnCheck_Changed;
            check.Unchecked += ColumnCheck_Changed;
            _columnChecks[column] = check;
            _columnFilterPanel.Children.Add(check);
        }
    }

    private void ColumnCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingColumns || sender is not CheckBox { Tag: LibraryFileColumn column } check)
        {
            return;
        }

        if (LibraryColumnFilter.IsLocked(column))
        {
            check.IsChecked = true;
            return;
        }

        var set = new HashSet<LibraryFileColumn> { LibraryFileColumn.Name };
        foreach (var pair in _columnChecks)
        {
            if (pair.Value.IsChecked == true)
            {
                set.Add(pair.Key);
            }
        }

        ApplyColumnVisibility(set, notify: true);
    }

    private void ApplyColumnVisibility(HashSet<LibraryFileColumn> visible, bool notify)
    {
        visible.Add(LibraryFileColumn.Name);
        _visibleColumns = visible;
        _syncingColumns = true;
        try
        {
            foreach (var pair in _columns)
            {
                pair.Value.Visibility = visible.Contains(pair.Key)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            foreach (var pair in _columnChecks)
            {
                pair.Value.IsChecked = visible.Contains(pair.Key);
            }
        }
        finally
        {
            _syncingColumns = false;
        }

        if (notify)
        {
            VisibleColumnsChanged?.Invoke(this, visible);
        }

        RequestFitColumns();
        NotifyGroupColumnHeadersChanged();
    }

    private static string ColumnHeader(LibraryFileColumn column) =>
        column switch
        {
            LibraryFileColumn.Title => UiStrings.LibraryColumnTitle,
            LibraryFileColumn.Artist => UiStrings.LibraryColumnArtist,
            LibraryFileColumn.AlbumArtist => UiStrings.LibraryColumnAlbumArtist,
            LibraryFileColumn.Album => UiStrings.LibraryColumnAlbum,
            LibraryFileColumn.Track => UiStrings.LibraryColumnTrack,
            LibraryFileColumn.Disc => UiStrings.LibraryColumnDisc,
            LibraryFileColumn.Year => UiStrings.LibraryColumnYear,
            LibraryFileColumn.Genre => UiStrings.LibraryColumnGenre,
            LibraryFileColumn.Composer => UiStrings.LibraryColumnComposer,
            LibraryFileColumn.Comment => UiStrings.LibraryColumnComment,
            LibraryFileColumn.Kind => UiStrings.LibraryColumnKind,
            LibraryFileColumn.Duration => UiStrings.LibraryColumnDuration,
            LibraryFileColumn.SampleRate => UiStrings.LibraryColumnSampleRate,
            LibraryFileColumn.BitDepth => UiStrings.LibraryColumnBitDepth,
            LibraryFileColumn.Channels => UiStrings.LibraryColumnChannels,
            LibraryFileColumn.BitRate => UiStrings.LibraryColumnBitRate,
            LibraryFileColumn.Size => UiStrings.LibraryColumnSize,
            LibraryFileColumn.Folder => UiStrings.LibraryColumnFolder,
            LibraryFileColumn.Jacket => UiStrings.LibraryColumnJacket,
            _ => UiStrings.LibraryColumnName,
        };

    private void AddColumn(
        LibraryFileColumn column,
        string binding,
        double min,
        bool right = false)
    {
        var text = new Style(typeof(TextBlock));
        text.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        text.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(8, 0, 8, 0)));
        text.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        if (right)
        {
            text.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
        }

        var gridColumn = new DataGridTextColumn
        {
            Binding = new Binding(binding),
            MinWidth = DesignMetrics.From96(min),
            Width = DataGridLength.Auto,
            IsReadOnly = true,
            ElementStyle = text,
            CanUserSort = true,
            SortMemberPath = binding,
        };
        _columns[column] = gridColumn;
        _grid.Columns.Add(gridColumn);
    }

    private void RequestFitColumns()
    {
        if (!IsLoaded)
        {
            return;
        }

        Dispatcher.BeginInvoke(FitColumns, DispatcherPriority.Loaded);
    }

    private void FitColumns()
    {
        foreach (var column in _grid.Columns)
        {
            if (column.Visibility != Visibility.Visible)
            {
                continue;
            }

            column.Width = 0;
            column.Width = new DataGridLength(1, DataGridLengthUnitType.Auto);
        }

        EnsureGroupHeaderScrollHook();
        NotifyGroupColumnHeadersChanged();
    }

    private void SetColumnHeader(LibraryFileColumn column, string header)
    {
        if (_columns.TryGetValue(column, out var gridColumn))
        {
            gridColumn.Header = header;
        }
    }

    private void Grid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        var column = ColumnFromGrid(e.Column);
        if (_sortColumn == column)
        {
            _sortDirection = _sortDirection == LibrarySortDirection.Ascending
                ? LibrarySortDirection.Descending
                : LibrarySortDirection.Ascending;
        }
        else
        {
            _sortColumn = column;
            _sortDirection = column is LibraryFileColumn.Duration
                or LibraryFileColumn.SampleRate
                or LibraryFileColumn.BitDepth
                or LibraryFileColumn.Channels
                or LibraryFileColumn.BitRate
                or LibraryFileColumn.Size
                or LibraryFileColumn.Track
                or LibraryFileColumn.Disc
                or LibraryFileColumn.Year
                or LibraryFileColumn.Jacket
                ? LibrarySortDirection.Descending
                : LibrarySortDirection.Ascending;
        }

        foreach (var item in _columns)
        {
            item.Value.SortDirection = item.Key == _sortColumn
                ? _sortDirection == LibrarySortDirection.Ascending
                    ? System.ComponentModel.ListSortDirection.Ascending
                    : System.ComponentModel.ListSortDirection.Descending
                : null;
        }

        var active = SelectedSession;
        var selected = SelectedSessions;
        _rows = LibraryFileList.Sort(_rows, _sortColumn, _sortDirection);
        LibraryFileList.ApplyGroupKeys(_rows, _group);
        BindRows(active, selected);
    }

    private LibraryFileColumn ColumnFromGrid(DataGridColumn column)
    {
        foreach (var pair in _columns)
        {
            if (ReferenceEquals(pair.Value, column))
            {
                return pair.Key;
            }
        }

        return LibraryFileColumn.Name;
    }

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _deferActivate || SelectedSession is not { } session)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            _anchorIndex = _grid.SelectedIndex;
        }

        SessionActivated?.Invoke(this, session);
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedSession is { } session)
        {
            SessionActivated?.Invoke(this, session);
        }
    }

    private void GroupCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_groupCombo.SelectedValue is not LibraryFileGroup group || group == _group)
        {
            return;
        }

        _group = group;
        var active = SelectedSession;
        var selected = SelectedSessions;
        LibraryFileList.ApplyGroupKeys(_rows, _group);
        SyncGroupColumnHeadersVisibility();
        BindRows(active, selected);
        InvalidateGroupJackets();
        _ = EnsureGroupArtworkAsync();
        GroupChanged?.Invoke(this, group);
    }

    private void Jacket_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (!TryGetDroppedImage(e, out _))
        {
            return;
        }

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void Jacket_Drop(object sender, DragEventArgs e)
    {
        if (!TryGetDroppedImage(e, out var path))
        {
            return;
        }

        e.Handled = true;
        ArtworkDropped?.Invoke(this, path);
    }

    internal static bool TryGetDroppedImage(DragEventArgs e, out string path)
    {
        path = string.Empty;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)
            || e.Data.GetData(DataFormats.FileDrop) is not string[] files
            || files.Length == 0)
        {
            return false;
        }

        foreach (var file in files)
        {
            if (Id3Artwork.IsImagePath(file) && File.Exists(file))
            {
                path = file;
                return true;
            }
        }

        return false;
    }

    internal static bool TryReadImageBytes(string path, out byte[] bytes)
    {
        bytes = [];
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        if (bytes.Length == 0)
        {
            return false;
        }

        var mime = Id3Artwork.LooksLikeImage(bytes) ? Id3Artwork.MimeFromImage(bytes) : string.Empty;
        if (mime is "image/jpeg" or "image/png")
        {
            return true;
        }

        return TryEncodePng(bytes, out bytes);
    }

    private static bool TryEncodePng(byte[] source, out byte[] png)
    {
        png = [];
        try
        {
            BitmapSource bitmap;
            using (var stream = new MemoryStream(source, writable: false))
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                bitmap = image;
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = new MemoryStream();
            encoder.Save(output);
            png = output.ToArray();
            return png.Length > 0;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    private bool TryPatchBoundRows(IReadOnlyList<LibraryFileRow> rows)
    {
        if (_grid.ItemsSource is null || _items.Count != rows.Count)
        {
            return false;
        }

        for (var i = 0; i < rows.Count; i++)
        {
            if (!ReferenceEquals(_items[i].Tag, rows[i].Tag))
            {
                return false;
            }
        }

        for (var i = 0; i < rows.Count; i++)
        {
            if (LibraryFileList.SameContent(_items[i], rows[i]))
            {
                continue;
            }

            var previous = _items[i];
            var wasSelected = _grid.SelectedItems.Contains(previous);
            var wasCurrent = ReferenceEquals(_grid.SelectedItem, previous);
            _items[i] = rows[i];
            if (wasSelected && !_grid.SelectedItems.Contains(_items[i]))
            {
                _grid.SelectedItems.Add(_items[i]);
            }

            if (wasCurrent)
            {
                _grid.SelectedItem = _items[i];
            }
        }

        _rows = [.. _items];
        return true;
    }

    private void ApplyGlowVeil()
    {
        var light = UiThemeService.Current == UiTheme.Light;
        _veil.Opacity = GlowVeilOpacityFor(UiThemeService.Current);
        if (light)
        {
            _veil.Background = Brushes.White;
        }
        else
        {
            _veil.SetResourceReference(Border.BackgroundProperty, "SurfaceBackBrush");
        }
    }

    private void ApplyArtworkGlow(BitmapSource? bitmap)
    {
        var show = bitmap is not null;
        var glowChanged = _artworkGlow != show;
        _artworkGlow = show;
        if (show)
        {
            _glowHost.Background = CreateAmbientWash(bitmap!);
            _glowHost.Visibility = Visibility.Visible;
            _veil.Visibility = Visibility.Visible;
        }
        else
        {
            _glowHost.Visibility = Visibility.Collapsed;
            _veil.Visibility = Visibility.Collapsed;
            _glowHost.Background = null;
        }

        SyncGlowDrift();

        if (glowChanged)
        {
            var preserve = SelectedSessions;
            var active = SelectedSession;
            BeginRowSync();
            try
            {
                ApplyGridStyles();
                ApplyRowSelectionCore(active, preserve);
            }
            finally
            {
                EndRowSync();
            }
        }
    }

    private void SyncGlowDrift()
    {
        var run = _artworkGlow && IsVisible && IsLoaded;
        if (run == _glowDriftRunning)
        {
            return;
        }

        if (run)
        {
            StartGlowDrift();
        }
        else
        {
            StopGlowDrift();
        }
    }

    private void StartGlowDrift()
    {
        _glowScale.BeginAnimation(ScaleTransform.ScaleXProperty, CreateGlowDriftPulse(GlowDriftScaleFrom, GlowDriftScaleTo, GlowDriftScaleSeconds));
        _glowScale.BeginAnimation(ScaleTransform.ScaleYProperty, CreateGlowDriftPulse(GlowDriftScaleFrom, GlowDriftScaleTo, GlowDriftScaleSeconds));
        _glowTranslate.BeginAnimation(TranslateTransform.XProperty, CreateGlowDriftPulse(-GlowDriftX, GlowDriftX, GlowDriftXSeconds));
        _glowTranslate.BeginAnimation(TranslateTransform.YProperty, CreateGlowDriftPulse(-GlowDriftY, GlowDriftY, GlowDriftYSeconds));
        _glowDriftRunning = true;
    }

    private void StopGlowDrift()
    {
        _glowScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _glowScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _glowTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        _glowTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        _glowScale.ScaleX = GlowDriftScaleFrom;
        _glowScale.ScaleY = GlowDriftScaleFrom;
        _glowTranslate.X = 0;
        _glowTranslate.Y = 0;
        _glowDriftRunning = false;
    }

    internal static DoubleAnimation CreateGlowDriftPulse(double from, double to, double seconds)
    {
        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromSeconds(seconds),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Timeline.SetDesiredFrameRate(anim, GlowDriftFrameRate);
        return anim;
    }

    private static bool ArtworkEquals(byte[]? left, byte[]? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.AsSpan().SequenceEqual(right);
    }

    /// <summary>
    /// ジャケットの特徴色で放射グラデの色だまりを重ねた超軽量ウォッシュ。BlurEffect は使わない。
    /// </summary>
    internal static Brush CreateAmbientWash(BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var colors = SampleArtworkColors(source, 3);
        var group = new DrawingGroup();
        AddColorBlob(group, colors[0], new Point(0.32, 0.38), 0.55, alpha: 210);
        AddColorBlob(group, colors[1], new Point(0.72, 0.42), 0.50, alpha: 180);
        AddColorBlob(group, colors[2], new Point(0.48, 0.78), 0.58, alpha: 160);
        group.Freeze();

        var brush = new DrawingBrush(group)
        {
            Stretch = Stretch.Fill,
            TileMode = TileMode.None,
            Viewport = new Rect(0, 0, 1, 1),
            ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
            Viewbox = new Rect(0, 0, 1, 1),
            ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
        };
        brush.Freeze();
        return brush;
    }

    private static void AddColorBlob(
        DrawingGroup group,
        Color color,
        Point center,
        double radius,
        byte alpha)
    {
        var brush = new RadialGradientBrush
        {
            GradientOrigin = center,
            Center = center,
            RadiusX = radius,
            RadiusY = radius * 0.88,
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, color.R, color.G, color.B), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(alpha * 0.45), color.R, color.G, color.B), 0.45));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
        brush.Freeze();
        group.Children.Add(new GeometryDrawing(brush, null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
    }
    /// <summary>彩度の高いピクセルから特徴色を最大 count 色取り出す。</summary>
    internal static Color[] SampleArtworkColors(BitmapSource source, int count)
    {
        ArgumentNullException.ThrowIfNull(source);
        count = Math.Clamp(count, 1, 6);
        var pixels = CopySamplePixels(source, GlowSampleEdge);
        if (pixels.Length < 4)
        {
            return Enumerable.Repeat(Color.FromRgb(0x5A, 0x6A, 0x88), count).ToArray();
        }

        var candidates = new List<(Color Color, double Score, double Hue)>(pixels.Length / 4);
        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            var b = pixels[i];
            var g = pixels[i + 1];
            var r = pixels[i + 2];
            var a = pixels[i + 3];
            if (a < 32)
            {
                continue;
            }

            ToHsl(r, g, b, out var h, out var s, out var l);
            if (s < 0.08 || l is < 0.08 or > 0.92)
            {
                continue;
            }

            // 彩度と中間明度を優先。暗すぎ／白飛びは弱い。
            var score = s * (1.0 - Math.Abs(l - 0.45) * 1.4);
            candidates.Add((BoostAmbient(Color.FromRgb(r, g, b), s, l), score, h));
        }

        if (candidates.Count == 0)
        {
            // ほぼ無彩度なら平均色を少し持ち上げる。
            long sumR = 0, sumG = 0, sumB = 0, n = 0;
            for (var i = 0; i + 3 < pixels.Length; i += 4)
            {
                if (pixels[i + 3] < 32)
                {
                    continue;
                }

                sumB += pixels[i];
                sumG += pixels[i + 1];
                sumR += pixels[i + 2];
                n++;
            }

            var avg = n == 0
                ? Color.FromRgb(0x5A, 0x6A, 0x88)
                : BoostAmbient(
                    Color.FromRgb((byte)(sumR / n), (byte)(sumG / n), (byte)(sumB / n)),
                    saturation: 0.35,
                    lightness: 0.45);
            return Enumerable.Repeat(avg, count).ToArray();
        }

        candidates.Sort((left, right) => right.Score.CompareTo(left.Score));
        var picked = new List<Color>(count);
        foreach (var candidate in candidates)
        {
            if (picked.Any(existing => HueDistance(existing, candidate.Hue) < 28))
            {
                continue;
            }

            picked.Add(candidate.Color);
            if (picked.Count >= count)
            {
                break;
            }
        }

        while (picked.Count < count)
        {
            picked.Add(picked[^1]);
        }

        return picked.ToArray();
    }

    private static byte[] CopySamplePixels(BitmapSource source, int maxEdge)
    {
        maxEdge = Math.Max(4, maxEdge);
        var edge = Math.Max(source.PixelWidth, source.PixelHeight);
        BitmapSource sample = source;
        if (edge > maxEdge)
        {
            var scale = maxEdge / (double)edge;
            var width = Math.Max(1, (int)Math.Round(source.PixelWidth * scale));
            var height = Math.Max(1, (int)Math.Round(source.PixelHeight * scale));
            sample = new TransformedBitmap(source, new ScaleTransform(width / (double)source.PixelWidth, height / (double)source.PixelHeight));
        }

        var converted = new FormatConvertedBitmap(sample, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    private static Color BoostAmbient(Color color, double saturation, double lightness)
    {
        ToHsl(color.R, color.G, color.B, out var h, out var s, out var l);
        s = Math.Clamp(Math.Max(s, saturation) * 1.25 + 0.08, 0.2, 0.95);
        l = Math.Clamp(Math.Max(l, lightness * 0.9), 0.28, 0.72);
        FromHsl(h, s, l, out var r, out var g, out var b);
        return Color.FromRgb(r, g, b);
    }

    private static double HueDistance(Color color, double hue)
    {
        ToHsl(color.R, color.G, color.B, out var h, out _, out _);
        var d = Math.Abs(h - hue) % 360;
        return d > 180 ? 360 - d : d;
    }

    private static void ToHsl(byte r, byte g, byte b, out double h, out double s, out double l)
    {
        var rf = r / 255d;
        var gf = g / 255d;
        var bf = b / 255d;
        var max = Math.Max(rf, Math.Max(gf, bf));
        var min = Math.Min(rf, Math.Min(gf, bf));
        l = (max + min) * 0.5;
        var d = max - min;
        if (d < 1e-6)
        {
            h = 0;
            s = 0;
            return;
        }

        s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        if (max == rf)
        {
            h = ((gf - bf) / d + (gf < bf ? 6 : 0)) * 60;
        }
        else if (max == gf)
        {
            h = ((bf - rf) / d + 2) * 60;
        }
        else
        {
            h = ((rf - gf) / d + 4) * 60;
        }
    }

    private static void FromHsl(double h, double s, double l, out byte r, out byte g, out byte b)
    {
        if (s < 1e-6)
        {
            var gray = (byte)Math.Clamp((int)Math.Round(l * 255), 0, 255);
            r = g = b = gray;
            return;
        }

        double Hue(double p, double q, double t)
        {
            if (t < 0)
            {
                t += 1;
            }

            if (t > 1)
            {
                t -= 1;
            }

            if (t < 1d / 6)
            {
                return p + (q - p) * 6 * t;
            }

            if (t < 0.5)
            {
                return q;
            }

            if (t < 2d / 3)
            {
                return p + (q - p) * (2d / 3 - t) * 6;
            }

            return p;
        }

        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;
        var hk = h / 360d;
        r = (byte)Math.Clamp((int)Math.Round(Hue(p, q, hk + 1d / 3) * 255), 0, 255);
        g = (byte)Math.Clamp((int)Math.Round(Hue(p, q, hk) * 255), 0, 255);
        b = (byte)Math.Clamp((int)Math.Round(Hue(p, q, hk - 1d / 3) * 255), 0, 255);
    }

    private static BitmapSource? TryCreateBitmap(byte[]? bytes, int decodeMaxEdge = 0)
    {
        if (bytes is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            if (decodeMaxEdge > 0)
            {
                image.DecodePixelWidth = decodeMaxEdge;
            }

            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    internal static LibraryFileRow CreateRow(DocumentSession session)
    {
        var document = session.Document;
        var path = document.SourcePath;
        var tags = document.Tags;
        var deferred = document.IsDeferredLoad;
        var duration = deferred ? tags.DurationSeconds : document.DurationSeconds;
        var sampleRate = deferred ? tags.SampleRate : document.SampleRate;
        var bits = deferred ? tags.BitsPerSample : document.BitsPerSample;
        var channels = deferred ? tags.Channels : document.Channels;
        var bitRate = tags.BitRateKbps;
        if (bitRate <= 0 && sampleRate > 0 && channels > 0 && bits > 0)
        {
            bitRate = (int)Math.Round(sampleRate * channels * bits / 1000d);
        }

        var hasArt = document.HasArtwork || tags.HasArtwork;
        return new LibraryFileRow
        {
            Tag = session,
            Name = session.DisplayName,
            Title = tags.Title,
            Artist = tags.Artist,
            AlbumArtist = tags.AlbumArtist,
            Album = tags.Album,
            Track = tags.Track,
            TrackNumber = tags.TrackNumber,
            Disc = tags.Disc,
            DiscNumber = tags.DiscNumber,
            Year = tags.Year,
            YearNumber = tags.YearNumber,
            Genre = tags.Genre,
            Composer = tags.Composer,
            Comment = tags.Comment,
            Kind = FormatKind(document.SourceKind),
            DurationSeconds = duration,
            DurationText = duration > 0 ? UiStrings.FormatDuration(duration) : string.Empty,
            SampleRate = sampleRate,
            SampleRateText = sampleRate > 0 ? UiStrings.FormatSampleRate(sampleRate) : string.Empty,
            BitDepth = bits,
            BitDepthText = bits > 0 ? UiStrings.FormatBitDepth(bits) : string.Empty,
            Channels = channels,
            ChannelsText = channels > 0 ? UiStrings.FormatChannels(channels) : string.Empty,
            BitRateKbps = bitRate,
            BitRateText = UiStrings.FormatBitRate(bitRate),
            FileBytes = document.FileBytes,
            SizeText = UiStrings.FormatFileBytesCompact(document.FileBytes),
            Folder = string.IsNullOrEmpty(path)
                ? string.Empty
                : Path.GetDirectoryName(path) ?? string.Empty,
            HasArtwork = hasArt,
            JacketText = hasArt ? UiStrings.LibraryJacketMark : string.Empty,
        };
    }

    internal static string FormatKind(AudioFileKind kind) => kind switch
    {
        AudioFileKind.Mp3 => "MP3",
        AudioFileKind.M4a => "M4A",
        AudioFileKind.Aiff => "AIFF",
        _ => "WAVE",
    };

    internal BitmapSource? ArtworkForGroup(CollectionViewGroup? group)
    {
        if (_group == LibraryFileGroup.None || group?.Name is not { } name)
        {
            return null;
        }

        var key = name.ToString() ?? string.Empty;
        if (_groupJackets.TryGetValue(key, out var cached))
        {
            return cached;
        }

        BitmapSource? bitmap = null;
        foreach (var item in group.Items)
        {
            if (item is LibraryFileRow { Tag: DocumentSession session }
                && session.Document.Artwork is { Length: > 0 } bytes)
            {
                bitmap = TryCreateBitmap(bytes, ArtworkDecodeMaxEdge);
                break;
            }
        }

        _groupJackets[key] = bitmap;
        return bitmap;
    }

    private void InvalidateGroupJackets()
    {
        _groupJackets.Clear();
        GroupArtworkChanged?.Invoke();
    }

    private async Task EnsureGroupArtworkAsync()
    {
        if (_group == LibraryFileGroup.None)
        {
            return;
        }

        var gen = ++_groupArtLoad;
        var pending = new List<(DocumentSession Session, string Path)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in _items)
        {
            if (row.Tag is not DocumentSession session
                || !seen.Add(row.GroupKey)
                || session.Document.HasArtwork
                || session.Document.SourcePath is not { Length: > 0 } path)
            {
                continue;
            }

            pending.Add((session, path));
        }

        foreach (var (session, path) in pending)
        {
            if (gen != _groupArtLoad)
            {
                return;
            }

            var bytes = await Task.Run(() =>
            {
                if (Id3Artwork.TryRead(path, out var art))
                {
                    return art;
                }

                return Array.Empty<byte>();
            }).ConfigureAwait(true);
            if (gen != _groupArtLoad || bytes.Length == 0)
            {
                continue;
            }

            session.Document.SetArtwork(bytes);
            UpdateSessionRow(session);
        }
    }

    private sealed record GroupOption(LibraryFileGroup Group, string Label);
}

internal sealed class LibraryGroupColumnHeaders : Border
{
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal };
    private readonly TranslateTransform _scroll = new();
    private LibraryBrowserView? _owner;
    private bool _syncing;

    public LibraryGroupColumnHeaders()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0, 0, 0, 1);
        SetResourceReference(BorderBrushProperty, "ChromeBorderBrush");
        Padding = new Thickness(0, 0, 0, 2);
        var host = new Grid { ClipToBounds = true, Height = 24 };
        _row.RenderTransform = _scroll;
        _row.HorizontalAlignment = HorizontalAlignment.Left;
        _row.VerticalAlignment = VerticalAlignment.Center;
        host.Children.Add(_row);
        Child = host;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) => Sync();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _owner = FindOwner();
        if (_owner is null)
        {
            return;
        }

        _owner.GroupColumnHeadersChanged -= OnOwnerChanged;
        _owner.GroupColumnHeadersChanged += OnOwnerChanged;
        _owner.GroupHeaderScrollOffsetChanged -= OnScrollOffset;
        _owner.GroupHeaderScrollOffsetChanged += OnScrollOffset;
        Sync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_owner is null)
        {
            return;
        }

        _owner.GroupColumnHeadersChanged -= OnOwnerChanged;
        _owner.GroupHeaderScrollOffsetChanged -= OnScrollOffset;
        _owner = null;
    }

    private void OnOwnerChanged(object? sender, EventArgs e) => Sync();

    private void OnScrollOffset(object? sender, double offset) =>
        _scroll.X = -offset;

    private void Sync()
    {
        if (_syncing)
        {
            return;
        }

        var owner = _owner ?? FindOwner();
        if (owner is null)
        {
            return;
        }

        _syncing = true;
        try
        {
            _row.Children.Clear();
            foreach (var (header, width, column) in owner.EnumerateVisibleGridColumns())
            {
                var label = new TextBlock
                {
                    Text = header,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 11,
                    Margin = new Thickness(8, 2, 8, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                label.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryForeBrush");

                var cell = new Border
                {
                    Width = Math.Max(24, width),
                    Child = label,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0, 0, 1, 0),
                    Cursor = Cursors.Hand,
                    Tag = column,
                };
                cell.SetResourceReference(Border.BorderBrushProperty, "ChromeBorderBrush");
                cell.MouseLeftButtonUp += Header_Click;
                _row.Children.Add(cell);
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private void Header_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DataGridColumn column })
        {
            return;
        }

        (_owner ?? FindOwner())?.SortByGridColumn(column);
        e.Handled = true;
    }

    private LibraryBrowserView? FindOwner()
    {
        DependencyObject? current = this;
        while (current is not null)
        {
            if (current is LibraryBrowserView view)
            {
                return view;
            }

            current = VisualTreeHelper.GetParent(current)
                ?? (current as FrameworkElement)?.Parent;
        }

        return null;
    }
}

/// <summary>ジャケット下の床映り込み（同寸の上下反転＋接点側が濃く下へフェード）。</summary>
internal static class LibraryJacketReflection
{
    /// <summary>鏡面の見える高さ（元画像に対する比率）。</summary>
    internal const double HeightFactor = 0.40;
    /// <summary>接点付近の不透明度（255 基準）。やや薄めから始める。</summary>
    internal const byte PeakOpacity = 78;
    /// <summary>本体と鏡面のすき間（DIP）。</summary>
    internal const double GapDip = 1;
}

internal sealed class LibraryJacketReflectionView : Border
{
    private readonly Image _image = new();

    public LibraryJacketReflectionView(double faceSize)
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
        ClipToBounds = true;
        Margin = new Thickness(0, LibraryJacketReflection.GapDip, 0, 0);
        Padding = new Thickness(0);
        BorderThickness = new Thickness(0);
        // マスクは反転の外側。接点（上）が濃く、下へ透明。
        OpacityMask = CreateOpacityMask();

        _image.Stretch = Stretch.Uniform;
        _image.SnapsToDevicePixels = true;
        _image.VerticalAlignment = VerticalAlignment.Top;
        _image.HorizontalAlignment = HorizontalAlignment.Center;
        // レイアウト上は同寸のまま反転し、上端クリップで床側だけ見せる。
        _image.LayoutTransform = new ScaleTransform(1, -1);
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        Child = _image;
        SyncFaceSize(faceSize, faceSize);
    }

    public ImageSource? Source
    {
        get => _image.Source;
        set => _image.Source = value;
    }

    public void SyncFaceSize(double faceWidth, double faceHeight)
    {
        faceWidth = Math.Max(1, faceWidth);
        faceHeight = Math.Max(1, faceHeight);
        Width = faceWidth;
        Height = faceHeight * LibraryJacketReflection.HeightFactor;
        _image.Width = faceWidth;
        _image.Height = faceHeight;
    }

    internal static void FitWithin(
        double maxEdge,
        int pixelWidth,
        int pixelHeight,
        out double width,
        out double height)
    {
        maxEdge = Math.Max(1, maxEdge);
        pixelWidth = Math.Max(1, pixelWidth);
        pixelHeight = Math.Max(1, pixelHeight);
        var scale = Math.Min(maxEdge / pixelWidth, maxEdge / pixelHeight);
        width = pixelWidth * scale;
        height = pixelHeight * scale;
    }

    internal static Brush CreateOpacityMask()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1),
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
        };
        brush.GradientStops.Add(new GradientStop(
            Color.FromArgb(LibraryJacketReflection.PeakOpacity, 255, 255, 255), 0));
        brush.GradientStops.Add(new GradientStop(
            Color.FromArgb((byte)(LibraryJacketReflection.PeakOpacity * 0.45), 255, 255, 255), 0.35));
        brush.GradientStops.Add(new GradientStop(
            Color.FromArgb((byte)(LibraryJacketReflection.PeakOpacity * 0.12), 255, 255, 255), 0.72));
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        brush.Freeze();
        return brush;
    }
}

internal sealed class LibraryGroupJacketImage : StackPanel
{
    private readonly Image _face = new();
    private readonly LibraryJacketReflectionView _reflection;
    private LibraryBrowserView? _owner;

    public LibraryGroupJacketImage()
    {
        Orientation = Orientation.Vertical;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(8, 0, 8, 4);
        Width = DesignMetrics.LibraryGroupJacketSize;

        var size = DesignMetrics.LibraryGroupJacketSize;
        _face.MaxWidth = size;
        _face.MaxHeight = size;
        _face.Width = double.NaN;
        _face.Height = double.NaN;
        _face.HorizontalAlignment = HorizontalAlignment.Center;
        // 非正方形は切り抜かず最大正方形に収め、下端で反射と密着。
        _face.Stretch = Stretch.Uniform;
        _face.SnapsToDevicePixels = true;
        RenderOptions.SetBitmapScalingMode(_face, BitmapScalingMode.HighQuality);
        _face.SizeChanged += (_, _) => SyncReflectionSize();

        _reflection = new LibraryJacketReflectionView(size);
        Children.Add(_face);
        Children.Add(_reflection);

        DataContextChanged += (_, _) => Reload();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void SyncReflectionSize()
    {
        var w = _face.ActualWidth;
        var h = _face.ActualHeight;
        if (w < 1 || h < 1)
        {
            if (_face.Source is not BitmapSource bmp || bmp.PixelWidth < 1 || bmp.PixelHeight < 1)
            {
                return;
            }

            LibraryJacketReflectionView.FitWithin(
                DesignMetrics.LibraryGroupJacketSize,
                bmp.PixelWidth,
                bmp.PixelHeight,
                out w,
                out h);
        }

        _reflection.SyncFaceSize(w, h);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _owner = FindOwner();
        if (_owner is not null)
        {
            _owner.GroupArtworkChanged -= Reload;
            _owner.GroupArtworkChanged += Reload;
        }

        Reload();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_owner is not null)
        {
            _owner.GroupArtworkChanged -= Reload;
            _owner = null;
        }
    }

    private void Reload()
    {
        var owner = _owner ?? FindOwner();
        var art = owner?.ArtworkForGroup(DataContext as CollectionViewGroup);
        _face.Source = art;
        _reflection.Source = art;
        // Collapsed だと列幅が潰れてヘッダーとずれるので、枠は常に確保する。
        Visibility = Visibility.Visible;
        Opacity = art is null ? 0 : 1;
        _reflection.Visibility = art is null ? Visibility.Hidden : Visibility.Visible;
        SyncReflectionSize();
    }

    private LibraryBrowserView? FindOwner()
    {
        DependencyObject? current = this;
        while (current is not null)
        {
            if (current is LibraryBrowserView view)
            {
                return view;
            }

            current = VisualTreeHelper.GetParent(current)
                ?? LogicalTreeHelper.GetParent(current);
        }

        return null;
    }
}
