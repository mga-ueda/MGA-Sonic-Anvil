using System.Collections.ObjectModel;
using System.ComponentModel;
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

/// <summary>F10 上段。左フォルダツリー、中央リスト。ジャケットは背面のぼかしだけ。</summary>
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
    internal const double GlowCrossfadeSeconds = 1;
    internal const int GlowCrossfadeFrameRate = 30;
    internal const int GlowWashTurnSteps = 4;
    internal const double GlowWashTurnSeconds = 20;
    internal const double GlowWashTurnFadeSeconds = 10;
    private const double GlowDriftBleed = 80;
    /// <summary>ツリーとプレイリストで揃える文字サイズ。</summary>
    internal const double LibraryListFontSize = 11;
    /// <summary>セル左右 Margin。</summary>
    internal const double LibraryColumnCellPadX = 8;
    /// <summary>ツリー／お気に入り／プレイリストの選択塗り。ジャケットを透かす。</summary>
    internal const byte LibrarySelectionFillAlpha = 0x28;
    /// <summary>ライトは白地にシアンが沈むので、選択を濃くする。</summary>
    internal const byte LibrarySelectionFillAlphaLight = 0x70;
    /// <summary>ライトの選択シアンを白へ寄せて、少し明るい帯にする。</summary>
    internal const double LibrarySelectionLightTowardWhite = 0.32;
    /// <summary>同マウスオーバー塗り。選択より濃く、行が判る明るさ。</summary>
    internal const byte LibraryHoverFillAlpha = 0x98;
    /// <summary>列見出し左右 Padding。</summary>
    internal const double LibraryColumnHeaderPadX = 8;
    /// <summary>ソート中の見出しの ▼▲（余白込み）。</summary>
    internal const double LibraryColumnSortPad =
        LibrarySortHeader.MarksGap + ((LibrarySortHeader.MarkMargin + LibrarySortHeader.MarkWidth) * 2);
    internal const double PaneFocusLineHeight = 36;
    internal const byte PaneFocusLineAlpha = 0x14;
    /// <summary>ライトは白地に白が乗らないので、暗い色をやや濃くする。</summary>
    internal const byte PaneFocusLineAlphaLight = 0x3C;
    internal const string PlaylistRowBandName = "PlaylistRowBand";
    internal static readonly Color FallbackWashNavy = Color.FromRgb(0x1B, 0x3A, 0x6B);
    internal static readonly Color FallbackWashCyan = Color.FromRgb(0x00, 0xF5, 0xFF);
    internal static readonly Color FallbackWashWhite = Color.FromRgb(0xFF, 0xFF, 0xFF);
    private static Brush[]? _fallbackWashTurns;
    /// <summary>ジャケット／グロー用デコードの長辺上限。APIC 原寸展開を避ける。</summary>
    private const int ArtworkDecodeMaxEdge = 512;

    private readonly Grid _host = new();
    private readonly Grid _glowHost = new();
    private readonly Border _glowFrom = CreateGlowWashLayer();
    private readonly Border _glowTo = CreateGlowWashLayer();
    private readonly ScaleTransform _glowScale = new(GlowDriftScaleFrom, GlowDriftScaleFrom);
    private readonly TranslateTransform _glowTranslate = new();
    private bool _glowDriftRunning;
    private readonly DispatcherTimer _washTurnTimer = new()
    {
        Interval = TimeSpan.FromSeconds(GlowWashTurnSeconds),
    };
    private Brush[] _washTurns = [];
    private int _washTurn;
    private readonly Border _veil = new();
    private readonly Grid _waveGlow = new();
    private readonly Border _waveGlowFrom = CreateGlowWashLayer();
    private readonly Border _waveGlowTo = CreateGlowWashLayer();
    private readonly Border _waveVeil = new();
    private Brush? _glowWash;
    private readonly ScaleTransform _waveGlowScale = new(GlowDriftScaleFrom, GlowDriftScaleFrom);
    private readonly TranslateTransform _waveGlowTranslate = new();
    private Grid? _waveGlowHost;
    private bool _extendGlow;
    private readonly Grid _root = new();
    private readonly ColumnDefinition _treeColumn = new();
    private readonly RowDefinition _explorerTreeRow = new() { Height = new GridLength(1, GridUnitType.Star) };
    private readonly RowDefinition _favoritesRow = new() { Height = new GridLength(1, GridUnitType.Star) };
    private readonly LibraryHoverSplitter _favoritesSplitter = new();
    private readonly LibraryHoverSplitter _explorerSplitter = new();
    private readonly TreeView _folderTree = new();
    private readonly TextBlock _explorerLabel = new();
    private readonly TextBlock _favoritesLabel = new();
    private readonly ListBox _favoritesList = new();
    private readonly Border _treeFocusLine = CreatePaneFocusLine();
    private readonly Border _favoritesFocusLine = CreatePaneFocusLine();
    private readonly Border _listFocusLine = CreatePaneFocusLine();
    private readonly ObservableCollection<LibraryFavoriteRow> _favorites = [];
    private readonly HashSet<TreeViewItem> _treeMultiSelected = [];
    private string[] _explorerRoots = LibraryExplorerPaths.ResolveRoots(null);
    private readonly HashSet<string> _explorerExpanded = new(StringComparer.OrdinalIgnoreCase);
    private bool _treeExpansionBusy;
    private readonly ComboBox _groupCombo = new();
    private readonly TextBlock _playlistLabel = new();
    private readonly TextBlock _groupLabel = new();
    private readonly StackPanel _playlistGroupHost = new() { Orientation = Orientation.Horizontal };
    private readonly DataGrid _grid = new();
    private DataGridTextColumn _groupSpacer = null!;
    private bool _gridScrollHooked;
    private readonly Dictionary<LibraryFileColumn, DataGridTextColumn> _columns = [];
    private readonly Dictionary<LibraryFileColumn, LibrarySortHeader> _sortHeaders = [];
    private int _sortChromeTicket;
    private HashSet<LibraryFileColumn> _visibleColumns = [.. LibraryColumnFilter.Defaults];
    private bool _syncing;
    private int _syncGeneration;
    private bool _deferActivate;
    private bool _restoreListFocus;
    private int _restoreListFocusGeneration;
    private LibraryFileColumn _sortColumn = LibraryFileColumn.Album;
    private LibrarySortDirection _sortDirection = LibrarySortDirection.Ascending;
    private LibraryFileGroup _group = LibraryFileGroup.Album;
    private IReadOnlyList<LibraryFileRow> _rows = [];
    private ObservableCollection<LibraryFileRow> _items = [];
    private readonly Dictionary<string, BitmapSource?> _groupJackets = new(StringComparer.Ordinal);
    private bool _groupJacketsInvalidateQueued;
    private bool _fitColumnsQueued;
    private int _anchorIndex;
    private bool _artworkGlow;
    private byte[]? _artworkBytes;
    private BitmapSource? _jacketBitmap;
    internal bool GlowUsesFallback { get; private set; }
    private int _groupArtLoad;
    private bool _treeSyncing;
    private Point? _treeDragStart;
    private TreeViewItem? _treeDragItem;
    private Point? _favoritesDragStart;

    public event EventHandler<DocumentSession>? SessionActivated;

    /// <summary>ダブルクリック。停止中でも再生する。</summary>
    public event EventHandler<DocumentSession>? SessionPlayRequested;

    public event EventHandler<IReadOnlyCollection<LibraryFileColumn>>? VisibleColumnsChanged;

    public event EventHandler<LibraryFileGroup>? GroupChanged;

    public event EventHandler<string>? ExplorerFolderChanged;

    /// <summary>ツリーの展開セットが変わった（パスはフルパス）。</summary>
    public event EventHandler<IReadOnlyList<string>>? ExplorerExpandedChanged;

    public event EventHandler<string>? ExplorerFolderOpened;

    /// <summary>複数フォルダを再帰追加。引数はフォルダパス。</summary>
    public event EventHandler<IReadOnlyList<string>>? ExplorerFoldersOpened;

    public event EventHandler<double>? ExplorerWidthChanged;

    /// <summary>お気に入りペインの高さ比（0–1）。</summary>
    public event EventHandler<double>? FavoritesSplitChanged;

    public event EventHandler<IReadOnlyList<string>>? FavoritesChanged;

    /// <summary>お気に入りをプレイリストへ。true ならクリアして追加し再生。</summary>
    public event EventHandler<LibraryFavoritesActivateEventArgs>? FavoritesActivated;

    /// <summary>ツリーの Enter と同じ。プレイリストを空にしてから選んだフォルダを載せる。</summary>
    public event EventHandler? ExplorerReplacePlaylistRequested;

    public event EventHandler? ClearPlaylistRequested;

    internal event Action? GroupArtworkChanged;

    internal event EventHandler? GroupViewportChanged;

    public bool JacketReplaceEnabled { get; private set; }

    public LibraryBrowserView()
    {
        Focusable = false;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        BuildLayout();
        ConfigureGrid();
        ConfigureGroupCombo();
        ApplyColumnVisibility(_visibleColumns, notify: false);
        ApplyLocalizedText();
        ApplyGridStyles();
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.None);
        KeyboardNavigation.SetDirectionalNavigation(this, KeyboardNavigationMode.None);
        _folderTree.IsKeyboardFocusWithinChanged += (_, _) => SyncPaneFocusChrome();
        _favoritesList.IsKeyboardFocusWithinChanged += (_, _) => SyncPaneFocusChrome();
        _grid.IsKeyboardFocusWithinChanged += (_, _) => SyncPaneFocusChrome();
        _groupCombo.IsKeyboardFocusWithinChanged += (_, _) => SyncPaneFocusChrome();
        _washTurnTimer.Tick += (_, _) => AdvanceWashTurn();
        Loaded += (_, _) =>
        {
            FitColumns();
            SyncGroupChrome();
            EnsureGroupScrollHook();
            PinColumnHeaders();
            SyncGlowDrift();
            SyncWashTurns();
            SyncPaneFocusChrome();
        };
        IsVisibleChanged += (_, _) =>
        {
            EnsureGroupScrollHook();
            SyncGlowDrift();
            SyncWashTurns();
        };
        Unloaded += (_, _) =>
        {
            StopGlowDrift();
            StopWashTurns();
        };
        SetArtwork(null);
    }

    internal static bool AllowsJacketReplace(AudioFileKind kind) => kind == AudioFileKind.Mp3;

    internal bool IsPlaceholderJacket => _artworkBytes is null;

    /// <summary>ジャケットのぼかしがツリー・お気に入り・プレイリストまで届く。</summary>
    internal bool GlowFillsLibraryChrome =>
        Grid.GetColumnSpan(_glowHost) == _root.ColumnDefinitions.Count
        && Grid.GetColumnSpan(_veil) == _root.ColumnDefinitions.Count
        && ReferenceEquals(VisualTreeHelper.GetParent(_glowHost), _root)
        && ReferenceEquals(VisualTreeHelper.GetParent(_veil), _root);

    internal ImageSource? JacketDisplaySource => _jacketBitmap;

    private void ApplyGridStyles()
    {
        var selected = CyanSelectionBrush();
        var hover = GrayHoverBrush();

        var headerStyle = new Style(typeof(DataGridColumnHeader));
        headerStyle.Setters.Add(new Setter(
            Control.BackgroundProperty,
            _artworkGlow
                ? Brushes.Transparent
                : new DynamicResourceExtension("ColorPanelBackBrush")));
        headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));
        headerStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        headerStyle.Setters.Add(new Setter(
            Control.PaddingProperty,
            new Thickness(LibraryColumnHeaderPadX, 2, LibraryColumnHeaderPadX, 2)));
        headerStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        headerStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
        headerStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        headerStyle.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        headerStyle.Setters.Add(new Setter(Control.TemplateProperty, ColumnHeaderTemplate()));
        _grid.ColumnHeaderStyle = headerStyle;
        PinColumnHeaders();

        _grid.CellStyle = CreatePlaylistCellStyle();
        ApplyGroupSpacerCellStyle();
        ApplyColumnCellStyles();

        var rowStyle = new Style(typeof(DataGridRow));
        rowStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        rowStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        rowStyle.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        rowStyle.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0)));
        rowStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));
        rowStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        rowStyle.Setters.Add(new Setter(Control.TemplateProperty, PlaylistRowTemplate(selected, hover)));
        _grid.RowStyle = rowStyle;
        _grid.RowBackground = Brushes.Transparent;
        _grid.AlternatingRowBackground = Brushes.Transparent;
        var fore = ResolveThemeBrush("PrimaryForeBrush", Color.FromRgb(0xE8, 0xE8, 0xEA));
        ApplyNavSelectionResources(_grid, selected, fore);
        SetPlaylistBandLeft(_grid, _group != LibraryFileGroup.None ? GroupJacketColumnWidth : 0);
        RefreshSortChrome();
    }

    public bool IsListKeyboardFocused => _grid.IsKeyboardFocusWithin;

    public bool IsGroupComboFocused => _groupCombo.IsKeyboardFocusWithin;

    public bool IsExplorerFocused => _folderTree.IsKeyboardFocusWithin;

    public bool IsFavoritesFocused => _favoritesList.IsKeyboardFocusWithin;

    internal Visibility TreeFocusLineVisibility => _treeFocusLine.Visibility;

    internal Visibility FavoritesFocusLineVisibility => _favoritesFocusLine.Visibility;

    internal Visibility ListFocusLineVisibility => _listFocusLine.Visibility;

    internal string ExplorerPaneTitle => _explorerLabel.Text;

    internal string FavoritesPaneTitle => _favoritesLabel.Text;

    internal string PlaylistPaneTitle => _playlistLabel.Text;

    internal Dock PlaylistGroupDock => DockPanel.GetDock(_playlistGroupHost);

    public bool IsExplorerOrigin(System.Windows.DependencyObject? origin)
    {
        while (origin is not null)
        {
            if (ReferenceEquals(origin, _folderTree) || ReferenceEquals(origin, _explorerLabel))
            {
                return true;
            }

            origin = VisualTreeHelper.GetParent(origin);
        }

        return false;
    }

    public bool IsFavoritesOrigin(System.Windows.DependencyObject? origin)
    {
        while (origin is not null)
        {
            if (ReferenceEquals(origin, _favoritesList) || ReferenceEquals(origin, _favoritesLabel))
            {
                return true;
            }

            origin = VisualTreeHelper.GetParent(origin);
        }

        return false;
    }

    public bool IsListOrigin(System.Windows.DependencyObject? origin)
    {
        while (origin is not null)
        {
            if (ReferenceEquals(origin, _grid) || ReferenceEquals(origin, _playlistLabel))
            {
                return true;
            }

            origin = VisualTreeHelper.GetParent(origin);
        }

        return false;
    }

    public bool HasOpenContextMenu =>
        _folderTree.ContextMenu is { IsOpen: true }
        || _favoritesList.ContextMenu is { IsOpen: true }
        || _grid.ContextMenu is { IsOpen: true };

    internal object? BoundItemsSource => _grid.ItemsSource;

    internal FrameworkElement FileGrid => _grid;

    internal int RootColumnCount => _root.ColumnDefinitions.Count;

    internal GridSplitter FavoritesSplitter => _favoritesSplitter;

    internal GridSplitter ExplorerSplitter => _explorerSplitter;

    internal TreeViewItem? ExplorerFirstFolder =>
        _folderTree.Items.OfType<TreeViewItem>().FirstOrDefault();

    internal Style? ExplorerItemStyle => _folderTree.ItemContainerStyle;

    internal void RecalculateColumnWidths() => FitColumns();

    internal double ColumnPixelWidth(LibraryFileColumn column) =>
        _columns.TryGetValue(column, out var gridColumn) ? gridColumn.Width.Value : 0;

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

    /// <summary>表示順の次の曲。最後の次は先頭。1曲なら同じ曲。</summary>
    public DocumentSession? NextPlaylistSession(DocumentSession? current)
    {
        var order = PlaylistOrder();
        if (order.Count == 0)
        {
            return null;
        }

        var index = -1;
        if (current is not null)
        {
            for (var i = 0; i < order.Count; i++)
            {
                if (ReferenceEquals(order[i], current))
                {
                    index = i;
                    break;
                }
            }
        }

        var next = LibraryPlayerMode.NextLoopIndex(order.Count, index);
        return next < 0 ? null : order[next];
    }

    /// <summary>表示順の前の曲。先頭の前は末尾。1曲なら同じ曲。</summary>
    public DocumentSession? PreviousPlaylistSession(DocumentSession? current)
    {
        var order = PlaylistOrder();
        if (order.Count == 0)
        {
            return null;
        }

        var index = -1;
        if (current is not null)
        {
            for (var i = 0; i < order.Count; i++)
            {
                if (ReferenceEquals(order[i], current))
                {
                    index = i;
                    break;
                }
            }
        }

        var previous = LibraryPlayerMode.PreviousLoopIndex(order.Count, index);
        return previous < 0 ? null : order[previous];
    }

    /// <summary>再生追従で行を選ぶ。クリック再生は起こさない。複数選択は今の曲が含まれていれば残す。</summary>
    public void SelectSessionQuiet(DocumentSession session)
    {
        var keep = SelectedSessions;
        if (keep.Length == 0 || Array.IndexOf(keep, session) < 0)
        {
            keep = [session];
        }

        var keepFocus = _restoreListFocus || _grid.IsKeyboardFocusWithin;
        _deferActivate = true;
        try
        {
            ApplyRowSelectionCore(session, keep);
        }
        finally
        {
            _deferActivate = false;
        }

        if (keepFocus)
        {
            EnsureListFocused();
        }

        if (_grid.SelectedItem is not null)
        {
            EnsureRowVisible(_grid.SelectedItem);
        }
    }

    private List<DocumentSession> PlaylistOrder()
    {
        var order = new List<DocumentSession>();
        if (_grid.ItemsSource is not null)
        {
            foreach (var item in _grid.Items)
            {
                if (item is LibraryFileRow { Tag: DocumentSession session })
                {
                    order.Add(session);
                }
            }
        }

        if (order.Count > 0)
        {
            return order;
        }

        foreach (var row in _rows)
        {
            if (row.Tag is DocumentSession session)
            {
                order.Add(session);
            }
        }

        return order;
    }

    public void FocusList()
    {
        EnsureListFocused();
        if (_grid.SelectedItem is not null)
        {
            EnsureRowVisible(_grid.SelectedItem);
        }
    }

    public void FocusExplorer()
    {
        CancelPlaylistFocusRestore();
        if (_folderTree.SelectedItem is TreeViewItem selected)
        {
            selected.BringIntoView();
            selected.Focus();
            return;
        }

        if (_folderTree.Items.OfType<TreeViewItem>().FirstOrDefault() is { } first)
        {
            first.IsSelected = true;
            first.BringIntoView();
            first.Focus();
            return;
        }

        _folderTree.Focus();
    }

    public void FocusFavorites()
    {
        CancelPlaylistFocusRestore();
        if (_favoritesList.SelectedItem is { } selected
            && _favoritesList.ItemContainerGenerator.ContainerFromItem(selected) is UIElement row)
        {
            row.Focus();
            return;
        }

        if (_favoritesList.Items.Count > 0)
        {
            _favoritesList.SelectedIndex = 0;
            if (_favoritesList.ItemContainerGenerator.ContainerFromIndex(0) is UIElement first)
            {
                first.Focus();
                return;
            }
        }

        _favoritesList.Focus();
    }

    public void CyclePaneFocus(bool reverse)
    {
        switch (LibraryPlayerMode.NextPane(ActivePane, reverse))
        {
            case LibraryPane.Explorer:
                FocusExplorer();
                break;
            case LibraryPane.Favorites:
                FocusFavorites();
                break;
            default:
                FocusList();
                break;
        }
    }

    private void SyncPaneFocusChrome()
    {
        _treeFocusLine.Visibility = IsExplorerFocused ? Visibility.Visible : Visibility.Collapsed;
        _favoritesFocusLine.Visibility = IsFavoritesFocused ? Visibility.Visible : Visibility.Collapsed;
        _listFocusLine.Visibility = IsListKeyboardFocused || IsGroupComboFocused
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    internal LibraryPane ActivePane
    {
        get
        {
            if (IsExplorerFocused)
            {
                return LibraryPane.Explorer;
            }

            if (IsFavoritesFocused)
            {
                return LibraryPane.Favorites;
            }

            return LibraryPane.List;
        }
    }

    /// <summary>メニューキー。フォーカス中のツリー／お気に入り／プレイリストのメニューを開く。</summary>
    public bool TryOpenKeyboardContextMenu()
    {
        if (IsExplorerFocused)
        {
            return OpenOwnedContextMenu(_folderTree);
        }

        if (IsFavoritesFocused)
        {
            return OpenOwnedContextMenu(_favoritesList);
        }

        if (IsListKeyboardFocused)
        {
            return OpenOwnedContextMenu(_grid);
        }

        return false;
    }

    internal string? OpenContextMenuHeader =>
        OpenContextMenuItems.FirstOrDefault()?.Header as string;

    internal MenuItem[] OpenContextMenuItems
    {
        get
        {
            foreach (var menu in new[] { _folderTree.ContextMenu, _favoritesList.ContextMenu, _grid.ContextMenu })
            {
                if (menu is { IsOpen: true })
                {
                    return menu.Items.OfType<MenuItem>().ToArray();
                }
            }

            return [];
        }
    }

    internal void CloseKeyboardContextMenu()
    {
        if (_folderTree.ContextMenu is { } explorer)
        {
            explorer.IsOpen = false;
        }

        if (_favoritesList.ContextMenu is { } favorites)
        {
            favorites.IsOpen = false;
        }

        if (_grid.ContextMenu is { } list)
        {
            list.IsOpen = false;
        }
    }

    private static bool OpenOwnedContextMenu(FrameworkElement owner)
    {
        if (owner.ContextMenu is not { } menu)
        {
            return false;
        }

        var anchor = Keyboard.FocusedElement as FrameworkElement;
        if (anchor is null
            || (!ReferenceEquals(anchor, owner) && !owner.IsAncestorOf(anchor)))
        {
            anchor = owner;
        }

        menu.PlacementTarget = anchor;
        menu.Placement = PlacementMode.Center;
        menu.IsOpen = true;
        return true;
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

        if (_grid.Items.Count > 0 && _grid.SelectedIndex < 0)
        {
            _grid.SelectedIndex = 0;
        }

        if (_grid.SelectedItem is { } item
            && _grid.ItemContainerGenerator.ContainerFromItem(item) is UIElement row
            && row.Focus())
        {
            return;
        }

        if (_grid.SelectedIndex >= 0
            && _grid.ItemContainerGenerator.ContainerFromIndex(_grid.SelectedIndex) is UIElement realized
            && realized.Focus())
        {
            return;
        }

        Keyboard.Focus(_grid);
        _grid.Focus();
    }

    private void RememberPlaylistClickFocus()
    {
        _restoreListFocus = true;
        var gen = ++_restoreListFocusGeneration;
        Dispatcher.BeginInvoke(
            () =>
            {
                if (gen != _restoreListFocusGeneration)
                {
                    return;
                }

                RestorePlaylistFocusIfNeeded();
                _restoreListFocus = false;
            },
            DispatcherPriority.Loaded);
    }

    private void CancelPlaylistFocusRestore()
    {
        _restoreListFocus = false;
        _restoreListFocusGeneration++;
    }

    private void RestorePlaylistFocusIfNeeded()
    {
        if (_restoreListFocus || _grid.IsKeyboardFocusWithin)
        {
            EnsureListFocused();
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
        }

        FillGroupOptions();
        TipService.Set(_grid, UiStrings.TipLibraryList);
        TipService.Set(_groupCombo, UiStrings.TipLibraryList);
        TipService.Set(_folderTree, UiStrings.TipLibraryExplorer);
        TipService.Set(_favoritesList, UiStrings.TipLibraryFavorites);
        _explorerLabel.Text = UiStrings.LibraryExplorerLabel;
        _favoritesLabel.Text = UiStrings.LibraryFavoritesLabel;
        _playlistLabel.Text = UiStrings.LibraryPlaylistLabel;
        RebuildExplorerContextMenu();
        RebuildFavoritesContextMenu();
        RebuildListContextMenu();
        RelabelExplorerRoots();
        FitColumns();
        SyncGroupChrome();
    }

    public void RefreshAppearance()
    {
        ApplyGlowVeil();
        ApplyGridStyles();
        ApplyExplorerStyle();
        ApplyFavoritesStyle();
        ApplyPaneFocusLineBrush();
        if (_artworkBytes is null)
        {
            ShowJacketDisplay(null, realArt: false);
        }

        InvalidateGroupJackets();
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
            var keepFocus = _restoreListFocus || _grid.IsKeyboardFocusWithin;
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

            if (keepFocus)
            {
                EnsureListFocused();
            }

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

    public void SetArtwork(AudioDocument? document, bool keepCurrentIfEmpty = false) =>
        ApplyArtwork(
            document?.Artwork,
            document is { } d && AllowsJacketReplace(d.SourceKind),
            keepCurrentIfEmpty);

    private void ApplyArtwork(byte[]? bytes, bool allowReplace, bool keepCurrentIfEmpty = false)
    {
        JacketReplaceEnabled = allowReplace;
        var next = bytes is { Length: > 0 } ? bytes : null;
        if (ArtworkEquals(_artworkBytes, next) && _jacketBitmap is not null)
        {
            return;
        }

        if (next is null && keepCurrentIfEmpty && _artworkBytes is not null)
        {
            return;
        }

        if (next is not null)
        {
            var decoded = TryCreateBitmap(next, ArtworkDecodeMaxEdge);
            if (decoded is not null)
            {
                _artworkBytes = next;
                ShowJacketDisplay(decoded, realArt: true);
                return;
            }

            // 次の画像が読めないときは、今の実ジャケットを外してフォールバックへ落とさない。
            if (_artworkBytes is not null)
            {
                return;
            }
        }

        _artworkBytes = next;
        ShowJacketDisplay(null, realArt: false);
    }

    private void ShowJacketDisplay(BitmapSource? decoded, bool realArt)
    {
        var bitmap = decoded ?? LibraryPlaceholderJacket.Bitmap;
        _jacketBitmap = bitmap;
        ApplyArtworkGlow(realArt ? bitmap : null);
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
        RefreshSortChrome();
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

            var current = _grid.Items[to];
            _grid.SelectedItem = current;
            for (var i = lo; i <= hi; i++)
            {
                var item = _grid.Items[i];
                if (!_grid.SelectedItems.Contains(item))
                {
                    _grid.SelectedItems.Add(item);
                }
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    public void SetExplorerFolder(string path) => RevealFolder(path);

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

    public void SetExplorerRoots(IEnumerable<string> roots)
    {
        var next = LibraryExplorerPaths.ResolveRoots(roots.ToArray());
        if (_explorerRoots.SequenceEqual(next, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var current = TryGetSelectedExplorerFolder(out var selected) ? selected : string.Empty;
        _explorerRoots = next;
        BuildExplorerRoots();
        if (!string.IsNullOrEmpty(current))
        {
            RevealFolder(current);
        }
    }

    public void SetFavorites(IEnumerable<string> paths)
    {
        var next = LibraryFavoritePaths.Resolve(paths.ToArray());
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
        _treeColumn.Width = new GridLength(clamped);
    }

    public double ReadExplorerWidth()
    {
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

    public void OpenSelectedFolder()
    {
        var folders = SelectedExplorerFolders;
        if (folders.Length == 0)
        {
            return;
        }

        if (folders.Length == 1)
        {
            ExplorerFolderOpened?.Invoke(this, folders[0]);
            return;
        }

        ExplorerFoldersOpened?.Invoke(this, folders);
    }

    /// <summary>右クリック「プレイリストをクリアして追加」。Enter と同じ。</summary>
    public void ReplacePlaylistFromSelection()
    {
        if (SelectedExplorerFolders.Length == 0)
        {
            return;
        }

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

    public void RemoveSelectedFavorites()
    {
        var selected = SelectedFavoritePaths;
        if (selected.Length == 0)
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
        _root.ColumnDefinitions.Add(_treeColumn);
        _root.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(DesignMetrics.LibraryExplorerSplitterWidth),
        });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        ApplyExplorerStyle();
        ApplyFavoritesStyle();
        _folderTree.AllowDrop = false;
        _folderTree.SelectedItemChanged += FolderTree_SelectedItemChanged;
        _folderTree.MouseDoubleClick += FolderTree_MouseDoubleClick;
        _folderTree.PreviewMouseLeftButtonDown += FolderTree_PreviewMouseLeftButtonDown;
        _folderTree.PreviewMouseMove += FolderTree_PreviewMouseMove;
        _folderTree.PreviewMouseLeftButtonUp += FolderTree_PreviewMouseLeftButtonUp;
        _folderTree.PreviewMouseRightButtonDown += FolderTree_PreviewMouseRightButtonDown;
        _folderTree.PreviewKeyDown += FolderTree_PreviewKeyDown;
        _folderTree.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, e) => e.Handled = true));
        _folderTree.CommandBindings.Add(new CommandBinding(ApplicationCommands.Cut, (_, e) => e.Handled = true));
        _folderTree.CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste, (_, e) => e.Handled = true));
        _folderTree.CommandBindings.Add(new CommandBinding(ApplicationCommands.Delete, (_, e) => e.Handled = true));
        BuildExplorerRoots();
        RebuildExplorerContextMenu();

        ApplyPaneTitleStyle(_explorerLabel);
        ApplyPaneTitleStyle(_favoritesLabel);
        ApplyPaneTitleStyle(_playlistLabel, new Thickness(0, 0, 8, 0));
        _favoritesList.ItemsSource = _favorites;
        _favoritesList.DisplayMemberPath = nameof(LibraryFavoriteRow.Name);
        _favoritesList.SelectionMode = SelectionMode.Extended;
        _favoritesList.BorderThickness = new Thickness(0);
        _favoritesList.Background = Brushes.Transparent;
        _favoritesList.Padding = new Thickness(4, 2, 4, 6);
        _favoritesList.FontSize = 11;
        _favoritesList.SetResourceReference(ForegroundProperty, "PrimaryForeBrush");
        _favoritesList.PreviewKeyDown += FavoritesList_PreviewKeyDown;
        _favoritesList.PreviewMouseLeftButtonDown += FavoritesList_PreviewMouseLeftButtonDown;
        _favoritesList.PreviewMouseMove += FavoritesList_PreviewMouseMove;
        _favoritesList.PreviewMouseLeftButtonUp += (_, _) => _favoritesDragStart = null;
        _favoritesList.AllowDrop = true;
        _favoritesList.PreviewDragOver += FavoritesList_PreviewDragOver;
        _favoritesList.Drop += FavoritesList_Drop;
        RebuildFavoritesContextMenu();

        var treePane = new Grid();
        _explorerTreeRow.MinHeight = DesignMetrics.LibraryFavoritesMinHeight;
        _favoritesRow.MinHeight = DesignMetrics.LibraryFavoritesMinHeight;
        treePane.RowDefinitions.Add(_explorerTreeRow);
        treePane.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(DesignMetrics.LibraryExplorerSplitterWidth),
        });
        treePane.RowDefinitions.Add(_favoritesRow);
        treePane.Background = Brushes.Transparent;
        treePane.ClipToBounds = false;

        var explorerPane = new DockPanel { Background = Brushes.Transparent };
        DockPanel.SetDock(_explorerLabel, Dock.Top);
        explorerPane.Children.Add(_explorerLabel);
        explorerPane.Children.Add(_folderTree);
        var treeHost = new Grid { Background = Brushes.Transparent };
        treeHost.Children.Add(explorerPane);
        treeHost.Children.Add(_treeFocusLine);
        Grid.SetRow(treeHost, 0);
        treePane.Children.Add(treeHost);

        var favoritesPane = new DockPanel();
        DockPanel.SetDock(_favoritesLabel, Dock.Top);
        favoritesPane.Children.Add(_favoritesLabel);
        favoritesPane.Children.Add(_favoritesList);
        var favoritesHost = new Grid { Background = Brushes.Transparent };
        favoritesHost.Children.Add(favoritesPane);
        favoritesHost.Children.Add(_favoritesFocusLine);
        Grid.SetRow(favoritesHost, 2);
        treePane.Children.Add(favoritesHost);

        var favoritesSplitter = _favoritesSplitter;
        favoritesSplitter.Height = DesignMetrics.LibraryFavoritesSplitterHitHeight;
        favoritesSplitter.HorizontalAlignment = HorizontalAlignment.Stretch;
        favoritesSplitter.VerticalAlignment = VerticalAlignment.Center;
        favoritesSplitter.ResizeBehavior = GridResizeBehavior.PreviousAndNext;
        favoritesSplitter.ResizeDirection = GridResizeDirection.Rows;
        favoritesSplitter.Cursor = Cursors.SizeNS;
        favoritesSplitter.SnapsToDevicePixels = true;
        ApplyLibrarySplitterChrome(favoritesSplitter);
        favoritesSplitter.DragCompleted += FavoritesSplitter_DragCompleted;
        Grid.SetRow(favoritesSplitter, 1);
        Panel.SetZIndex(favoritesSplitter, 8);
        treePane.Children.Add(favoritesSplitter);

        Grid.SetColumn(treePane, 0);
        _root.Children.Add(treePane);

        _groupLabel.VerticalAlignment = VerticalAlignment.Center;
        _groupLabel.Margin = new Thickness(0, 0, 8, 0);
        _groupLabel.FontSize = 11;
        _groupLabel.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryForeBrush");
        _groupCombo.MinWidth = DesignMetrics.From96(140);
        _groupCombo.Height = DesignMetrics.AudioInputHeight;
        _groupCombo.FontSize = 11;
        _groupCombo.VerticalContentAlignment = VerticalAlignment.Center;
        _groupCombo.SetResourceReference(StyleProperty, "LibraryComboBoxStyle");
        _groupCombo.SelectionChanged += GroupCombo_SelectionChanged;

        _playlistGroupHost.VerticalAlignment = VerticalAlignment.Center;
        _playlistGroupHost.Children.Add(_groupLabel);
        _playlistGroupHost.Children.Add(_groupCombo);

        var bar = new DockPanel { Margin = new Thickness(8, 6, 8, 6) };
        DockPanel.SetDock(_playlistGroupHost, Dock.Right);
        bar.Children.Add(_playlistGroupHost);
        _playlistLabel.VerticalAlignment = VerticalAlignment.Center;
        _playlistLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        bar.Children.Add(_playlistLabel);

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
        _glowHost.Children.Add(_glowFrom);
        _glowHost.Children.Add(_glowTo);

        _veil.IsHitTestVisible = false;
        _veil.Visibility = Visibility.Collapsed;
        ApplyGlowVeil();

        var listPane = new Grid { Background = Brushes.Transparent };
        listPane.Children.Add(list);
        listPane.Children.Add(_listFocusLine);
        Grid.SetColumn(listPane, 2);
        _root.Children.Add(listPane);

        var splitter = _explorerSplitter;
        splitter.Width = DesignMetrics.LibrarySplitterHitThickness;
        splitter.HorizontalAlignment = HorizontalAlignment.Center;
        splitter.VerticalAlignment = VerticalAlignment.Stretch;
        splitter.ResizeBehavior = GridResizeBehavior.PreviousAndNext;
        splitter.ResizeDirection = GridResizeDirection.Columns;
        splitter.Cursor = Cursors.SizeWE;
        splitter.SnapsToDevicePixels = true;
        ApplyLibrarySplitterChrome(splitter);
        splitter.DragCompleted += ExplorerSplitter_DragCompleted;
        Grid.SetColumn(splitter, 1);
        Panel.SetZIndex(splitter, 8);
        _root.Children.Add(splitter);

        Grid.SetColumnSpan(_glowHost, _root.ColumnDefinitions.Count);
        Grid.SetColumnSpan(_veil, _root.ColumnDefinitions.Count);
        _root.Children.Insert(0, _veil);
        _root.Children.Insert(0, _glowHost);
        _root.ClipToBounds = false;
        _root.SetResourceReference(Panel.BackgroundProperty, "SurfaceBackBrush");

        _host.ClipToBounds = true;
        _host.Children.Add(_root);
        Content = _host;
        SetResourceReference(BackgroundProperty, "SurfaceBackBrush");
        ApplyLibraryScrollBarStyle();
        LibraryScrollReveal.Attach(_folderTree);
        LibraryScrollReveal.Attach(_favoritesList);
        LibraryScrollReveal.Attach(_grid);
        LibrarySplitterReveal.Attach(_root, _explorerSplitter, _favoritesSplitter, _treeColumn, _explorerTreeRow);
    }

    private static void ApplyPaneTitleStyle(TextBlock label, Thickness? margin = null)
    {
        label.Margin = margin ?? new Thickness(8, 6, 8, 2);
        label.FontSize = 11;
        label.FontWeight = FontWeights.SemiBold;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryForeBrush");
    }

    private void ApplyLibraryScrollBarStyle()
    {
        if (TryFindResource("LibraryScrollBarStyle") is not Style style)
        {
            return;
        }

        Resources.Add(typeof(ScrollBar), style);
    }

    private void ApplyLibrarySplitterChrome(GridSplitter splitter)
    {
        splitter.Focusable = false;
        if (TryFindResource("LibrarySplitterStyle") is Style style)
        {
            splitter.Style = style;
            splitter.Focusable = false;
            return;
        }

        splitter.Background = Brushes.Transparent;
    }

    /// <summary>1px のマスに置いても、当たりがマスで切れない。</summary>
    private sealed class LibraryHoverSplitter : GridSplitter
    {
        protected override Geometry GetLayoutClip(Size layoutSlotSize) => null!;
    }

    private void ApplyExplorerStyle()
    {
        _folderTree.Background = Brushes.Transparent;
        _folderTree.BorderThickness = new Thickness(0);
        _folderTree.Padding = new Thickness(4, 6, 4, 6);
        _folderTree.FontSize = LibraryListFontSize;
        ScrollViewer.SetHorizontalScrollBarVisibility(_folderTree, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(_folderTree, ScrollBarVisibility.Auto);
        _folderTree.SetResourceReference(ForegroundProperty, "PrimaryForeBrush");
        VirtualizingPanel.SetIsVirtualizing(_folderTree, true);
        VirtualizingPanel.SetVirtualizationMode(_folderTree, VirtualizationMode.Recycling);

        // 既定のシステム選択色（非アクティブ時の白など）をアプリの色に差し替える。
        var hover = GrayHoverBrush();
        var selected = CyanSelectionBrush();
        var fore = ResolveThemeBrush("PrimaryForeBrush", Color.FromRgb(0xE8, 0xE8, 0xEA));
        ApplyNavSelectionResources(_folderTree, selected, fore);

        var itemStyle = new Style(typeof(TreeViewItem));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2, 1, 4, 1)));
        itemStyle.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
        itemStyle.Setters.Add(new Setter(Control.TemplateProperty, TreeNavTemplate(hover, selected)));
        _folderTree.Resources[typeof(TreeViewItem)] = itemStyle;
        _folderTree.ItemContainerStyle = itemStyle;
        ApplyTreeItemStyle(_folderTree.Items, itemStyle);
        ApplyTreeMultiSelectChrome();
    }

    private static void ApplyTreeItemStyle(ItemCollection items, Style style)
    {
        foreach (var raw in items)
        {
            if (raw is not TreeViewItem item)
            {
                continue;
            }

            item.Style = style;
            item.ItemContainerStyle = style;
            ApplyTreeItemStyle(item.Items, style);
        }
    }

    private void ApplyFavoritesStyle()
    {
        var hover = GrayHoverBrush();
        var selected = CyanSelectionBrush();
        var fore = ResolveThemeBrush("PrimaryForeBrush", Color.FromRgb(0xE8, 0xE8, 0xEA));
        ApplyNavSelectionResources(_favoritesList, selected, fore);

        var itemStyle = new Style(typeof(ListBoxItem));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4, 2, 6, 2)));
        itemStyle.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        itemStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        itemStyle.Setters.Add(new Setter(Control.TemplateProperty, ListNavTemplate(hover, selected)));
        _favoritesList.ItemContainerStyle = itemStyle;
    }

    private static void ApplyNavSelectionResources(FrameworkElement host, Brush selected, Brush fore)
    {
        host.Resources[SystemColors.HighlightBrushKey] = selected;
        host.Resources[SystemColors.HighlightTextBrushKey] = fore;
        host.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = selected;
        host.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = fore;
        // ControlBrush はスクロールの右下角に出る。選択色に使うと黒い四角になる。
        host.Resources[SystemColors.ControlBrushKey] = Brushes.Transparent;
    }

    internal static byte LibrarySelectionFillAlphaFor(UiTheme theme) =>
        theme == UiTheme.Light ? LibrarySelectionFillAlphaLight : LibrarySelectionFillAlpha;

    internal static Color LibrarySelectionRgb(Color accent, UiTheme theme) =>
        theme == UiTheme.Light
            ? MixRgbTowardWhite(accent, LibrarySelectionLightTowardWhite)
            : accent;

    internal static Brush CyanSelectionBrush()
    {
        var theme = UiThemeService.Current;
        return WithAlpha(
            LibrarySelectionRgb(Theme.Get("AccentCyanBrush"), theme),
            LibrarySelectionFillAlphaFor(theme));
    }

    internal static Brush GrayHoverBrush() =>
        WithAlpha(
            ResolveThemeBrush("MenuHighlightBackBrush", Color.FromRgb(0x37, 0x37, 0x3A)),
            LibraryHoverFillAlpha);

    private static Brush WithAlpha(Color color, byte alpha)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private static Color MixRgbTowardWhite(Color color, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            MixRgbChannel(color.R, amount),
            MixRgbChannel(color.G, amount),
            MixRgbChannel(color.B, amount));
    }

    private static byte MixRgbChannel(byte channel, double amount) =>
        (byte)Math.Clamp((int)Math.Round(channel + ((255 - channel) * amount)), 0, 255);

    private static Brush WithAlpha(Brush source, byte alpha)
    {
        var color = source is SolidColorBrush solid
            ? solid.Color
            : Color.FromRgb(0x37, 0x37, 0x3A);
        return WithAlpha(color, alpha);
    }

    private void PinColumnHeaders()
    {
        if (FindDescendant<DataGridColumnHeadersPresenter>(_grid) is { } headers)
        {
            Panel.SetZIndex(headers, 8);
        }
    }

    private static ControlTemplate ColumnHeaderTemplate()
    {
        var header = new FrameworkElementFactory(typeof(ContentPresenter));
        header.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        header.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        header.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);

        var bd = new FrameworkElementFactory(typeof(Border));
        bd.Name = "Bd";
        bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        bd.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        bd.SetValue(Border.BorderThicknessProperty, new Thickness(0));
        bd.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        bd.AppendChild(header);

        var root = new FrameworkElementFactory(typeof(Grid));
        root.AppendChild(bd);
        root.AppendChild(HeaderGripper("PART_LeftHeaderGripper", HorizontalAlignment.Left));
        root.AppendChild(HeaderGripper("PART_RightHeaderGripper", HorizontalAlignment.Right));
        return new ControlTemplate(typeof(DataGridColumnHeader)) { VisualTree = root };
    }

    private static FrameworkElementFactory HeaderGripper(string name, HorizontalAlignment align)
    {
        var thumb = new FrameworkElementFactory(typeof(Thumb));
        thumb.Name = name;
        thumb.SetValue(FrameworkElement.WidthProperty, 8d);
        thumb.SetValue(FrameworkElement.HorizontalAlignmentProperty, align);
        thumb.SetValue(FrameworkElement.CursorProperty, Cursors.SizeWE);
        thumb.SetValue(UIElement.OpacityProperty, 0d);
        thumb.SetValue(Control.BackgroundProperty, Brushes.Transparent);
        thumb.SetValue(Control.TemplateProperty, InvisibleThumbTemplate());
        return thumb;
    }

    private static ControlTemplate InvisibleThumbTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        return new ControlTemplate(typeof(Thumb)) { VisualTree = border };
    }

    private void ApplyColumnCellStyles()
    {
        foreach (var (_, gridColumn) in _columns)
        {
            gridColumn.CellStyle = CreatePlaylistCellStyle();
        }
    }

    /// <summary>
    /// 選択帯は行に塗る。セルに塗ると列境界の 1px 隙間が帯を切る。
    /// </summary>
    private Style CreatePlaylistCellStyle()
    {
        var cell = new Style(typeof(DataGridCell));
        cell.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        cell.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        cell.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        cell.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        cell.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
        cell.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        cell.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        cell.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));
        cell.Setters.Add(new Setter(Control.TemplateProperty, PlaylistCellTemplate()));
        cell.Setters.Add(new Setter(UIElement.SnapsToDevicePixelsProperty, true));
        var selected = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));
        selected.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        selected.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        cell.Triggers.Add(selected);
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
        focus.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        focus.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        cell.Triggers.Add(focus);
        return cell;
    }

    internal static ControlTemplate PlaylistCellTemplate()
    {
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        content.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
        content.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

        var bd = new FrameworkElementFactory(typeof(Border));
        bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        bd.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        bd.SetValue(Border.BorderThicknessProperty, new Thickness(0));
        bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        bd.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
        bd.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        bd.AppendChild(content);
        return new ControlTemplate(typeof(DataGridCell)) { VisualTree = bd };
    }

    /// <summary>
    /// 選択帯はセルではなく行の Border に塗る。DataGrid.RowBackground の coerce で
    /// 行 Background が潰されても、テンプレート側の帯は列境界の 1px 隙間を埋める。
    /// </summary>
    internal static ControlTemplate PlaylistRowTemplate(Brush selected, Brush hover)
    {
        var presenter = new FrameworkElementFactory(typeof(DataGridCellsPresenter));
        presenter.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        presenter.SetValue(Panel.ZIndexProperty, 1);
        presenter.SetValue(
            DataGridCellsPresenter.ItemsPanelProperty,
            new TemplateBindingExtension(DataGridRow.ItemsPanelProperty));

        var band = new FrameworkElementFactory(typeof(Border));
        band.Name = PlaylistRowBandName;
        band.SetValue(FrameworkElement.NameProperty, PlaylistRowBandName);
        band.SetValue(Panel.ZIndexProperty, 0);
        band.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        band.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        band.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        band.SetValue(Border.BorderThicknessProperty, new Thickness(0));
        band.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        band.SetBinding(
            FrameworkElement.MarginProperty,
            new Binding
            {
                Path = new PropertyPath(PlaylistBandLeftProperty),
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGrid), 1),
                Converter = PlaylistBandLeftConverter.Instance,
            });
        var chrome = new MultiBinding
        {
            Converter = PlaylistRowBandConverter.Instance,
            ConverterParameter = new PlaylistRowBandChrome(selected, hover),
        };
        chrome.Bindings.Add(new Binding(nameof(DataGridRow.IsSelected))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
        });
        chrome.Bindings.Add(new Binding(nameof(UIElement.IsMouseOver))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
        });
        band.SetBinding(Border.BackgroundProperty, chrome);

        var host = new FrameworkElementFactory(typeof(Grid));
        host.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        host.AppendChild(band);
        host.AppendChild(presenter);
        return new ControlTemplate(typeof(DataGridRow)) { VisualTree = host };
    }

    private sealed class PlaylistRowBandChrome
    {
        internal PlaylistRowBandChrome(Brush selected, Brush hover)
        {
            Selected = selected;
            Hover = hover;
        }

        internal Brush Selected { get; }
        internal Brush Hover { get; }
    }

    private sealed class PlaylistRowBandConverter : IMultiValueConverter
    {
        internal static readonly PlaylistRowBandConverter Instance = new();

        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (parameter is not PlaylistRowBandChrome chrome)
            {
                return Brushes.Transparent;
            }

            if (values is { Length: > 0 } && values[0] is true)
            {
                return chrome.Selected;
            }

            if (values is { Length: > 1 } && values[1] is true)
            {
                return chrome.Hover;
            }

            return Brushes.Transparent;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture) =>
            [];
    }

    private sealed class PlaylistBandLeftConverter : IValueConverter
    {
        internal static readonly PlaylistBandLeftConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var left = value is double pixels ? Math.Max(0, pixels) : 0;
            return new Thickness(left, 0, 0, 0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            Binding.DoNothing;
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

    private static readonly DependencyProperty TreeMarkedProperty = DependencyProperty.RegisterAttached(
        "TreeMarked",
        typeof(bool),
        typeof(LibraryBrowserView),
        new PropertyMetadata(false));

    internal static readonly DependencyProperty PlaylistBandLeftProperty = DependencyProperty.RegisterAttached(
        "PlaylistBandLeft",
        typeof(double),
        typeof(LibraryBrowserView),
        new FrameworkPropertyMetadata(0d));

    internal static void SetPlaylistBandLeft(DependencyObject target, double value) =>
        target.SetValue(PlaylistBandLeftProperty, value);

    internal static double GetPlaylistBandLeft(DependencyObject target) =>
        (double)target.GetValue(PlaylistBandLeftProperty);

    /// <summary>見出しの上だけグレー。子の上では親が反応しない。選択と Ctrl 複数はシアン。</summary>
    private static ControlTemplate TreeNavTemplate(Brush hover, Brush selected)
    {
        var expander = new FrameworkElementFactory(typeof(ToggleButton));
        expander.Name = "Expander";
        expander.SetValue(UIElement.FocusableProperty, false);
        expander.SetValue(Control.WidthProperty, 16d);
        expander.SetValue(Control.HeightProperty, 16d);
        expander.SetValue(Control.ForegroundProperty, new TemplateBindingExtension(Control.ForegroundProperty));
        expander.SetValue(Control.TemplateProperty, TreeExpanderTemplate());
        expander.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(TreeViewItem.IsExpanded))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
            Mode = BindingMode.TwoWay,
        });

        expander.SetValue(DockPanel.DockProperty, Dock.Left);

        var header = new FrameworkElementFactory(typeof(ContentPresenter));
        header.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        header.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
        header.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

        var row = new FrameworkElementFactory(typeof(DockPanel));
        row.AppendChild(expander);
        row.AppendChild(header);

        var bd = new FrameworkElementFactory(typeof(Border));
        bd.Name = "Bd";
        bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        bd.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        bd.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        bd.AppendChild(row);

        var items = new FrameworkElementFactory(typeof(ItemsPresenter));
        items.Name = "ItemsHost";
        items.SetValue(FrameworkElement.MarginProperty, new Thickness(16, 0, 0, 0));

        var root = new FrameworkElementFactory(typeof(StackPanel));
        root.AppendChild(bd);
        root.AppendChild(items);

        var template = new ControlTemplate(typeof(TreeViewItem)) { VisualTree = root };
        template.Triggers.Add(new Trigger
        {
            Property = TreeViewItem.HasItemsProperty,
            Value = false,
            Setters = { new Setter(UIElement.VisibilityProperty, Visibility.Hidden, "Expander") },
        });
        template.Triggers.Add(new Trigger
        {
            Property = TreeViewItem.IsExpandedProperty,
            Value = false,
            Setters = { new Setter(UIElement.VisibilityProperty, Visibility.Collapsed, "ItemsHost") },
        });

        var hoverOver = new Trigger
        {
            SourceName = "Bd",
            Property = UIElement.IsMouseOverProperty,
            Value = true,
        };
        hoverOver.Setters.Add(new Setter(Border.BackgroundProperty, hover, "Bd"));
        template.Triggers.Add(hoverOver);
        template.Triggers.Add(new Trigger
        {
            Property = TreeViewItem.IsSelectedProperty,
            Value = true,
            Setters = { new Setter(Border.BackgroundProperty, selected, "Bd") },
        });
        template.Triggers.Add(new Trigger
        {
            Property = TreeMarkedProperty,
            Value = true,
            Setters = { new Setter(Border.BackgroundProperty, selected, "Bd") },
        });
        return template;
    }

    private static ControlTemplate TreeExpanderTemplate()
    {
        var glyph = new FrameworkElementFactory(typeof(TextBlock));
        glyph.Name = "Glyph";
        glyph.SetValue(TextBlock.TextProperty, "▸");
        glyph.SetValue(TextBlock.FontSizeProperty, 9d);
        glyph.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        glyph.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        glyph.SetValue(TextBlock.ForegroundProperty, new TemplateBindingExtension(Control.ForegroundProperty));

        var template = new ControlTemplate(typeof(ToggleButton)) { VisualTree = glyph };
        template.Triggers.Add(new Trigger
        {
            Property = ToggleButton.IsCheckedProperty,
            Value = true,
            Setters = { new Setter(TextBlock.TextProperty, "▾", "Glyph") },
        });
        return template;
    }

    private static ControlTemplate ListNavTemplate(Brush hover, Brush selected)
    {
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));

        var bd = new FrameworkElementFactory(typeof(Border));
        bd.Name = "Bd";
        bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        bd.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        bd.AppendChild(presenter);

        var template = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = bd };
        var hoverOver = new Trigger
        {
            SourceName = "Bd",
            Property = UIElement.IsMouseOverProperty,
            Value = true,
        };
        hoverOver.Setters.Add(new Setter(Border.BackgroundProperty, hover, "Bd"));
        template.Triggers.Add(hoverOver);
        template.Triggers.Add(new Trigger
        {
            Property = ListBoxItem.IsSelectedProperty,
            Value = true,
            Setters = { new Setter(Border.BackgroundProperty, selected, "Bd") },
        });
        return template;
    }

    private void BuildExplorerRoots()
    {
        _treeMultiSelected.Clear();
        _folderTree.Items.Clear();
        foreach (var root in _explorerRoots)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
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
        var style = _folderTree.ItemContainerStyle;
        if (style is not null)
        {
            item.Style = style;
            item.ItemContainerStyle = style;
        }

        item.Expanded += FolderItem_Expanded;
        item.Collapsed += FolderItem_Collapsed;
        if (HasAnySubdir(path))
        {
            var dummy = new TreeViewItem();
            if (style is not null)
            {
                dummy.Style = style;
            }

            item.Items.Add(dummy);
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
            RememberExplorerExpanded(item, expanded: true);
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
            var child = AddFolderItem(item.Items, dir, FolderDisplayName(dir));
            if (_explorerExpanded.Contains(NormalizeFolderPath(dir)))
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

        if (e.NewValue is TreeViewItem { Tag: string path } item && Directory.Exists(path))
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0
                && (_treeMultiSelected.Count != 1 || !_treeMultiSelected.Contains(item)))
            {
                ClearTreeMultiSelect();
                _treeMultiSelected.Add(item);
                ApplyTreeMultiSelectChrome();
            }

            ExplorerFolderChanged?.Invoke(this, path);
        }
    }

    private void FolderTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenSelectedFolder();
    }

    private void FolderTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CancelPlaylistFocusRestore();
        _treeDragStart = null;
        _treeDragItem = null;
        if (e.OriginalSource is not DependencyObject origin
            || FindTreeViewItem(origin) is not { Tag: string } item
            || FindTreeExpandToggle(origin) is not null)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            ToggleTreeMultiSelect(item);
            item.IsSelected = true;
            e.Handled = true;
            return;
        }

        ClearTreeMultiSelect();
        _treeMultiSelected.Add(item);
        ApplyTreeMultiSelectChrome();
        _treeDragStart = e.GetPosition(null);
        _treeDragItem = item;
    }

    private void FolderTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject origin
            && FindTreeViewItem(origin) is { Tag: string } item)
        {
            if (!_treeMultiSelected.Contains(item))
            {
                ClearTreeMultiSelect();
                _treeMultiSelected.Add(item);
                ApplyTreeMultiSelectChrome();
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
            ExpandSelectedExplorerSubtree();
            e.Handled = true;
            return;
        }

        if (LibraryPlayerMode.IsExplorerCollapseSubtree(key, modifiers))
        {
            CollapseSelectedExplorerSubtree();
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
        void Walk(ItemCollection items)
        {
            foreach (var raw in items)
            {
                if (raw is not TreeViewItem child)
                {
                    continue;
                }

                var marked = _treeMultiSelected.Contains(child);
                child.SetValue(TreeMarkedProperty, marked);
                child.Background = marked ? selected : Brushes.Transparent;
                if (child.Items.Count > 0)
                {
                    Walk(child.Items);
                }
            }
        }

        Walk(_folderTree.Items);
    }

    private void FolderTree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_treeDragStart is null
            || _treeDragItem is null
            || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var paths = SelectedExplorerFolders;
        if (paths.Length == 0)
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
        var data = new DataObject(DataFormats.FileDrop, paths);
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
        AddGroupSpacerColumn();
        AddColumn(LibraryFileColumn.Name, nameof(LibraryFileRow.Name));
        AddColumn(LibraryFileColumn.Title, nameof(LibraryFileRow.Title));
        AddColumn(LibraryFileColumn.Artist, nameof(LibraryFileRow.Artist));
        AddColumn(LibraryFileColumn.Album, nameof(LibraryFileRow.Album));
        AddColumn(LibraryFileColumn.Track, nameof(LibraryFileRow.Track), right: true);
        AddColumn(LibraryFileColumn.Disc, nameof(LibraryFileRow.Disc), right: true);
        AddColumn(LibraryFileColumn.Year, nameof(LibraryFileRow.Year), right: true);
        AddColumn(LibraryFileColumn.Genre, nameof(LibraryFileRow.Genre));
        AddColumn(LibraryFileColumn.Composer, nameof(LibraryFileRow.Composer));
        AddColumn(LibraryFileColumn.Duration, nameof(LibraryFileRow.DurationText), right: true);
        AddColumn(LibraryFileColumn.Comment, nameof(LibraryFileRow.Comment));
        AddColumn(LibraryFileColumn.AlbumArtist, nameof(LibraryFileRow.AlbumArtist));
        AddColumn(LibraryFileColumn.Kind, nameof(LibraryFileRow.Kind));
        AddColumn(LibraryFileColumn.SampleRate, nameof(LibraryFileRow.SampleRateText), right: true);
        AddColumn(LibraryFileColumn.BitDepth, nameof(LibraryFileRow.BitDepthText), right: true);
        AddColumn(LibraryFileColumn.Channels, nameof(LibraryFileRow.ChannelsText), right: true);
        AddColumn(LibraryFileColumn.BitRate, nameof(LibraryFileRow.BitRateText), right: true);
        AddColumn(LibraryFileColumn.Size, nameof(LibraryFileRow.SizeText), right: true);
        AddColumn(LibraryFileColumn.Folder, nameof(LibraryFileRow.Folder));
        AddColumn(LibraryFileColumn.Jacket, nameof(LibraryFileRow.JacketText));

        if (_columns.TryGetValue(LibraryFileColumn.Album, out var albumColumn))
        {
            albumColumn.SortDirection = System.ComponentModel.ListSortDirection.Ascending;
        }

        RefreshSortChrome();

        _grid.AutoGenerateColumns = false;
        _grid.IsReadOnly = true;
        _grid.CanUserAddRows = false;
        _grid.CanUserDeleteRows = false;
        _grid.CanUserReorderColumns = true;
        _grid.CanUserSortColumns = true;
        _grid.CanUserResizeRows = false;
        _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        _grid.GridLinesVisibility = DataGridGridLinesVisibility.None;
        _grid.HorizontalGridLinesBrush = Brushes.Transparent;
        _grid.VerticalGridLinesBrush = Brushes.Transparent;
        _grid.SelectionUnit = DataGridSelectionUnit.FullRow;
        _grid.SelectionMode = DataGridSelectionMode.Extended;
        _grid.ClipboardCopyMode = DataGridClipboardCopyMode.None;
        // 1000 行超を全部実体化すると ↓ リピートと列幅再計算が止まる。グループ時も仮想化する。
        _grid.EnableRowVirtualization = true;
        VirtualizingPanel.SetIsVirtualizing(_grid, true);
        VirtualizingPanel.SetIsVirtualizingWhenGrouping(_grid, true);
        VirtualizingPanel.SetVirtualizationMode(_grid, VirtualizationMode.Standard);
        VirtualizingPanel.SetScrollUnit(_grid, ScrollUnit.Item);
        _grid.RowHeaderWidth = 0;
        _grid.MinRowHeight = 24;
        _grid.VerticalContentAlignment = VerticalAlignment.Center;
        _grid.BorderThickness = new Thickness(0);
        _grid.UseLayoutRounding = true;
        _grid.SnapsToDevicePixels = true;
        _grid.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _grid.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _grid.Background = Brushes.Transparent;
        _grid.FontSize = LibraryListFontSize;
        _grid.SetResourceReference(ForegroundProperty, "PrimaryForeBrush");
        _grid.Sorting += Grid_Sorting;
        _grid.SelectionChanged += Grid_SelectionChanged;
        _grid.PreviewMouseLeftButtonDown += Grid_PreviewMouseLeftButtonDown;
        _grid.MouseDoubleClick += Grid_MouseDoubleClick;
        RebuildListContextMenu();

        var headerFactory = new FrameworkElementFactory(typeof(LibraryGroupHeader));
        headerFactory.SetValue(DockPanel.DockProperty, Dock.Top);

        var jacket = new FrameworkElementFactory(typeof(LibraryGroupJacketImage));
        jacket.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        jacket.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
        jacket.SetValue(Panel.ZIndexProperty, 1);
        jacket.SetValue(UIElement.IsHitTestVisibleProperty, false);

        var items = new FrameworkElementFactory(typeof(ItemsPresenter));
        items.SetValue(Panel.ZIndexProperty, 0);

        var stage = new FrameworkElementFactory(typeof(Grid));
        stage.SetValue(FrameworkElement.ClipToBoundsProperty, true);
        stage.AppendChild(items);
        stage.AppendChild(jacket);

        var body = new FrameworkElementFactory(typeof(DockPanel));
        body.SetValue(DockPanel.LastChildFillProperty, true);
        body.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 10));
        body.SetValue(FrameworkElement.ClipToBoundsProperty, false);
        body.AppendChild(headerFactory);
        body.AppendChild(stage);

        var template = new ControlTemplate(typeof(GroupItem)) { VisualTree = body };
        var container = new Style(typeof(GroupItem));
        container.Setters.Add(new Setter(Control.TemplateProperty, template));
        container.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        container.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));

        container.Setters.Add(new Setter(FrameworkElement.ClipToBoundsProperty, false));

        _grid.GroupStyle.Clear();
        _grid.GroupStyle.Add(new GroupStyle
        {
            ContainerStyle = container,
            HidesIfEmpty = true,
        });
        SyncGroupChrome();
    }

    /// <summary>グループ左のジャケット幅（余白込み）。先頭のスペーサ列と一致させる。</summary>
    internal static double GroupJacketColumnWidth =>
        DesignMetrics.LibraryGroupJacketSize + 16;

    internal DataGridHeadersVisibility HeadersVisibility => _grid.HeadersVisibility;

    internal Visibility GroupSpacerVisibility => _groupSpacer.Visibility;

    internal int FrozenColumnCount => _grid.FrozenColumnCount;

    private void SyncGroupChrome()
    {
        _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        var grouped = _group != LibraryFileGroup.None;
        _groupSpacer.Visibility = grouped ? Visibility.Visible : Visibility.Collapsed;
        _grid.FrozenColumnCount = grouped ? 1 : 0;
        SetPlaylistBandLeft(_grid, grouped ? GroupJacketColumnWidth : 0);
    }

    internal void EnsureGroupScrollHook()
    {
        if (_gridScrollHooked)
        {
            return;
        }

        if (FindDataGridScrollViewer() is not { } scroll)
        {
            return;
        }

        scroll.ScrollChanged += GridScroll_Changed;
        _grid.SizeChanged += (_, _) => GroupViewportChanged?.Invoke(this, EventArgs.Empty);
        _gridScrollHooked = true;
        GroupViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    private void GridScroll_Changed(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0
            && e.ViewportHeightChange == 0
            && e.ExtentHeightChange == 0
            && e.HorizontalChange == 0
            && e.ViewportWidthChange == 0
            && e.ExtentWidthChange == 0)
        {
            return;
        }

        GroupViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    internal double RowsViewportTop()
    {
        if (FindDescendant<DataGridColumnHeadersPresenter>(_grid) is { } headers
            && headers.ActualHeight > 0)
        {
            try
            {
                var top = headers.TransformToAncestor(_grid).Transform(new Point(0, 0)).Y;
                return top + headers.ActualHeight;
            }
            catch (InvalidOperationException)
            {
            }
        }

        return _grid.ColumnHeaderHeight > 0 && !double.IsNaN(_grid.ColumnHeaderHeight)
            ? _grid.ColumnHeaderHeight
            : 28;
    }

    internal double GroupHorizontalOffset() =>
        FindDataGridScrollViewer()?.HorizontalOffset ?? 0;

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
        SyncGroupChrome();
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

    private void ApplyColumnVisibility(HashSet<LibraryFileColumn> visible, bool notify)
    {
        visible.Add(LibraryFileColumn.Name);
        _visibleColumns = visible;
        foreach (var pair in _columns)
        {
            pair.Value.Visibility = visible.Contains(pair.Key)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (notify)
        {
            VisibleColumnsChanged?.Invoke(this, visible);
        }

        RequestFitColumns();
        SyncGroupChrome();
    }

    private static string ColumnHeader(LibraryFileColumn column) =>
        UiStrings.LibraryColumnLabel(column);

    private void AddGroupSpacerColumn()
    {
        var empty = new Style(typeof(TextBlock));
        empty.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
        _groupSpacer = new DataGridTextColumn
        {
            Header = string.Empty,
            Binding = new Binding(nameof(LibraryFileRow.Name)),
            Width = new DataGridLength(GroupJacketColumnWidth),
            MinWidth = GroupJacketColumnWidth,
            MaxWidth = GroupJacketColumnWidth,
            IsReadOnly = true,
            CanUserSort = false,
            CanUserResize = false,
            CanUserReorder = false,
            ElementStyle = empty,
        };
        ApplyGroupSpacerCellStyle();
        _grid.Columns.Add(_groupSpacer);
    }

    private void ApplyGroupSpacerCellStyle()
    {
        if (_groupSpacer is null)
        {
            return;
        }

        _groupSpacer.CellStyle = CreatePlaylistCellStyle();
    }

    private void AddColumn(
        LibraryFileColumn column,
        string binding,
        bool right = false)
    {
        var text = new Style(typeof(TextBlock));
        text.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        text.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(LibraryColumnCellPadX, 0, LibraryColumnCellPadX, 0)));
        text.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        if (right)
        {
            text.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
        }

        var gridColumn = new DataGridTextColumn
        {
            Binding = new Binding(binding),
            MinWidth = 0,
            Width = DataGridLength.SizeToCells,
            IsReadOnly = true,
            ElementStyle = text,
            CanUserSort = true,
            SortMemberPath = binding,
        };
        var sortHeader = new LibrarySortHeader(ColumnHeader(column));
        _sortHeaders[column] = sortHeader;
        gridColumn.Header = sortHeader;
        _columns[column] = gridColumn;
        _grid.Columns.Add(gridColumn);
    }

    private void RequestFitColumns()
    {
        if (!IsLoaded || _fitColumnsQueued)
        {
            return;
        }

        _fitColumnsQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _fitColumnsQueued = false;
            FitColumns();
        }, DispatcherPriority.Background);
    }

    private void FitColumns()
    {
        var fontSize = _grid.FontSize > 0 ? _grid.FontSize : LibraryListFontSize;
        var cellPad = LibraryColumnCellPadX * 2;
        var headerPad = LibraryColumnHeaderPadX * 2;
        var family = _grid.FontFamily ?? new FontFamily("Yu Gothic UI");
        var typeface = new Typeface(family, _grid.FontStyle, _grid.FontWeight, _grid.FontStretch);
        var headerFace = new Typeface(family, _grid.FontStyle, FontWeights.SemiBold, _grid.FontStretch);
        var dpi = 1d;
        if (_grid.IsLoaded)
        {
            try
            {
                dpi = VisualTreeHelper.GetDpi(_grid).PixelsPerDip;
            }
            catch (InvalidOperationException)
            {
            }
        }

        var cache = new Dictionary<string, double>(StringComparer.Ordinal);
        double Measure(string text, Typeface face)
        {
            if (text.Length == 0)
            {
                return 0;
            }

            var key = ReferenceEquals(face, headerFace) ? text + "\u0001" : text;
            if (!cache.TryGetValue(key, out var width))
            {
                var formatted = new FormattedText(
                    text,
                    System.Globalization.CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    face,
                    fontSize,
                    Brushes.Black,
                    dpi);
                width = formatted.WidthIncludingTrailingWhitespace;
                if (formatted.OverhangLeading < 0)
                {
                    width -= formatted.OverhangLeading;
                }

                if (formatted.OverhangAfter > 0)
                {
                    width += formatted.OverhangAfter;
                }

                cache[key] = width;
            }

            return width;
        }

        foreach (var (kind, column) in _columns)
        {
            if (column.Visibility != Visibility.Visible)
            {
                continue;
            }

            var max = Measure(ColumnHeader(kind), headerFace) + headerPad;
            if (kind == _sortColumn)
            {
                max += LibraryColumnSortPad;
            }

            foreach (var row in _items)
            {
                var text = CellText(row, kind);
                if (text.Length == 0)
                {
                    continue;
                }

                var width = Measure(text, typeface) + cellPad;
                if (width > max)
                {
                    max = width;
                }
            }

            var fitted = Math.Clamp(
                Math.Ceiling(max),
                1,
                DesignMetrics.LibraryColumnMaxWidth);
            column.MinWidth = 0;
            column.Width = new DataGridLength(fitted, DataGridLengthUnitType.Pixel);
            column.MinWidth = fitted;
        }
    }

    private static string CellText(LibraryFileRow row, LibraryFileColumn column) =>
        column switch
        {
            LibraryFileColumn.Title => row.Title,
            LibraryFileColumn.Artist => row.Artist,
            LibraryFileColumn.AlbumArtist => row.AlbumArtist,
            LibraryFileColumn.Album => row.Album,
            LibraryFileColumn.Track => row.Track,
            LibraryFileColumn.Disc => row.Disc,
            LibraryFileColumn.Year => row.Year,
            LibraryFileColumn.Genre => row.Genre,
            LibraryFileColumn.Composer => row.Composer,
            LibraryFileColumn.Comment => row.Comment,
            LibraryFileColumn.Duration => row.DurationText,
            LibraryFileColumn.Kind => row.Kind,
            LibraryFileColumn.SampleRate => row.SampleRateText,
            LibraryFileColumn.BitDepth => row.BitDepthText,
            LibraryFileColumn.Channels => row.ChannelsText,
            LibraryFileColumn.BitRate => row.BitRateText,
            LibraryFileColumn.Size => row.SizeText,
            LibraryFileColumn.Folder => row.Folder,
            LibraryFileColumn.Jacket => row.JacketText,
            _ => row.Name,
        };

    private void SetColumnHeader(LibraryFileColumn column, string header)
    {
        if (_sortHeaders.TryGetValue(column, out var sortHeader))
        {
            sortHeader.Label = header;
        }
    }

    /// <summary>
    /// ItemsSource の差し替えで DataGrid が SortDirection を落としても、見出し側の ▼▲ は残す。
    /// </summary>
    private void RefreshSortChrome()
    {
        ApplySortChrome();
        var ticket = ++_sortChromeTicket;
        Dispatcher.BeginInvoke(
            () =>
            {
                if (ticket == _sortChromeTicket)
                {
                    ApplySortChrome();
                }
            },
            DispatcherPriority.ContextIdle);
    }

    private void ApplySortChrome()
    {
        var ascending = _sortDirection == LibrarySortDirection.Ascending;
        var direction = ascending
            ? ListSortDirection.Ascending
            : ListSortDirection.Descending;
        foreach (var pair in _sortHeaders)
        {
            pair.Value.ShowSort(pair.Key == _sortColumn, ascending);
        }

        foreach (var item in _columns)
        {
            var next = item.Key == _sortColumn ? (ListSortDirection?)direction : null;
            if (!Equals(item.Value.SortDirection, next))
            {
                item.Value.SortDirection = next;
            }
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

        RefreshSortChrome();
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

    private void Grid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left
            || e.OriginalSource is not DependencyObject origin
            || IsPlaylistSelectionChrome(origin)
            || FindPlaylistRow(origin) is not { } row)
        {
            return;
        }

        RememberPlaylistClickFocus();
        var modifiers = Keyboard.Modifiers;
        var control = (modifiers & ModifierKeys.Control) != 0;
        var shift = (modifiers & ModifierKeys.Shift) != 0;
        if (!control && !shift)
        {
            return;
        }

        var index = _grid.Items.IndexOf(row);
        if (index < 0)
        {
            return;
        }

        e.Handled = true;
        ApplyPlaylistModifierClick(index, shift, control);
        EnsureListFocused();
    }

    internal void ApplyPlaylistModifierClick(int index, bool shift, bool control)
    {
        _deferActivate = true;
        try
        {
            if (shift)
            {
                var from = _anchorIndex >= 0 ? _anchorIndex : Math.Max(0, _grid.SelectedIndex);
                SelectRange(from, index);
            }
            else if (control)
            {
                ToggleSelectionAt(index);
            }
        }
        finally
        {
            _deferActivate = false;
        }
    }

    private void ToggleSelectionAt(int index)
    {
        if (index < 0 || index >= _grid.Items.Count)
        {
            return;
        }

        var item = _grid.Items[index];
        _syncing = true;
        try
        {
            if (_grid.SelectedItems.Contains(item))
            {
                _grid.SelectedItems.Remove(item);
            }
            else
            {
                _grid.SelectedItems.Add(item);
            }

            _anchorIndex = index;
            if (_grid.SelectedItems.Contains(item))
            {
                _grid.CurrentItem = item;
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private static LibraryFileRow? FindPlaylistRow(DependencyObject origin)
    {
        while (origin is not null)
        {
            if (origin is DataGridRow { Item: LibraryFileRow row })
            {
                return row;
            }

            if (origin is DataGrid)
            {
                return null;
            }

            origin = VisualTreeHelper.GetParent(origin);
        }

        return null;
    }

    private static bool IsPlaylistSelectionChrome(DependencyObject origin)
    {
        while (origin is not null)
        {
            if (origin is DataGridColumnHeader or Thumb or LibraryGroupHeader or LibraryGroupJacketImage)
            {
                return true;
            }

            if (origin is DataGrid)
            {
                return false;
            }

            origin = VisualTreeHelper.GetParent(origin);
        }

        return false;
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedSession is { } session)
        {
            SessionPlayRequested?.Invoke(this, session);
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
        SyncGroupChrome();
        BindRows(active, selected);
        InvalidateGroupJackets();
        _ = EnsureGroupArtworkAsync();
        GroupChanged?.Invoke(this, group);
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

        var keepFocus = _restoreListFocus || _grid.IsKeyboardFocusWithin;
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
        if (keepFocus)
        {
            EnsureListFocused();
        }

        return true;
    }

    private void ApplyGlowVeil()
    {
        var light = UiThemeService.Current == UiTheme.Light;
        var opacity = GlowVeilOpacityFor(UiThemeService.Current);
        _veil.Opacity = opacity;
        _waveVeil.Opacity = opacity;
        if (light)
        {
            _veil.Background = Brushes.White;
            _waveVeil.Background = Brushes.White;
        }
        else
        {
            _veil.SetResourceReference(Border.BackgroundProperty, "SurfaceBackBrush");
            _waveVeil.SetResourceReference(Border.BackgroundProperty, "SurfaceBackBrush");
        }
    }

    /// <summary>
    /// リストと波形をまたぐ一枚のウォッシュ。プレイヤー中だけ伸ばし、リスト側の別アニメは畳む。
    /// </summary>
    internal void BindWaveformGlow(Grid host)
    {
        _waveGlowHost = host;
        var drift = new TransformGroup();
        drift.Children.Add(_waveGlowScale);
        drift.Children.Add(_waveGlowTranslate);
        _waveGlow.RenderTransform = drift;
        _waveGlow.RenderTransformOrigin = new Point(0.5, 0.5);
        _waveGlow.IsHitTestVisible = false;
        _waveGlow.SnapsToDevicePixels = false;
        _waveGlow.UseLayoutRounding = false;
        _waveGlow.Margin = new Thickness(-GlowDriftBleed);
        _waveGlow.Visibility = Visibility.Collapsed;
        _waveVeil.IsHitTestVisible = false;
        _waveVeil.Visibility = Visibility.Collapsed;
        if (_waveGlow.Children.Count == 0)
        {
            _waveGlow.Children.Add(_waveGlowFrom);
            _waveGlow.Children.Add(_waveGlowTo);
        }

        host.Children.Add(_waveGlow);
        host.Children.Add(_waveVeil);
        ApplyGlowVeil();
        CopyGlowWash(_glowFrom, _glowTo, _waveGlowFrom, _waveGlowTo);

        PlaceArtworkGlow();
        RestartGlowDrift();
        SyncWashTurns();
    }

    /// <summary>プレイヤー表示中は波形まで一枚で広げる。編集画面では畳む。</summary>
    internal void SetGlowExtendsWaveform(bool extend)
    {
        if (_extendGlow == extend)
        {
            return;
        }

        _extendGlow = extend;
        PlaceArtworkGlow();
        RestartGlowDrift();
    }

    private bool UseUnifiedGlow => _artworkGlow && _extendGlow && _waveGlowHost is not null;

    private void ApplyArtworkGlow(BitmapSource? bitmap)
    {
        var turns = bitmap is null
            ? CreateFallbackWashTurns()
            : CreateAmbientWashTurns(bitmap);
        GlowUsesFallback = bitmap is null;
        var glowChanged = !_artworkGlow;
        _artworkGlow = true;
        ApplyWashTurns(turns);

        PlaceArtworkGlow();
        RestartGlowDrift();

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

    private void ApplyWashTurns(Brush[] turns)
    {
        if (ReferenceEquals(_washTurns, turns) && _washTurns.Length > 0)
        {
            return;
        }

        _washTurns = turns;
        _washTurn = 0;
        var wash = turns[0];
        if (!ReferenceEquals(_glowWash, wash))
        {
            CrossfadeGlowWash(wash);
        }

        SyncWashTurns();
    }

    private void CrossfadeGlowWash(Brush wash) =>
        CrossfadeGlowWash(wash, GlowCrossfadeSeconds);

    private void CrossfadeGlowWash(Brush wash, double seconds)
    {
        var previous = _glowWash;
        _glowWash = wash;
        if (previous is null)
        {
            ApplyGlowWashImmediate(wash);
            return;
        }

        BeginGlowCrossfade(_glowFrom, _glowTo, previous, wash, seconds);
        BeginGlowCrossfade(_waveGlowFrom, _waveGlowTo, previous, wash, seconds);
    }

    private void ApplyGlowWashImmediate(Brush wash)
    {
        ApplyGlowWashImmediate(_glowFrom, _glowTo, wash);
        ApplyGlowWashImmediate(_waveGlowFrom, _waveGlowTo, wash);
    }

    private static void ApplyGlowWashImmediate(Border from, Border to, Brush wash)
    {
        StopGlowCrossfade(from, to);
        from.Background = null;
        from.Opacity = 0;
        to.Background = wash;
        to.Opacity = 1;
    }

    private static void BeginGlowCrossfade(Border from, Border to, Brush previous, Brush next, double seconds)
    {
        StopGlowCrossfade(from, to);
        from.Background = previous;
        from.Opacity = 1;
        to.Opacity = 0;
        to.Background = next;
        from.BeginAnimation(UIElement.OpacityProperty, CreateGlowCrossfade(1, 0, seconds));
        to.BeginAnimation(UIElement.OpacityProperty, CreateGlowCrossfade(0, 1, seconds));
    }

    private static void StopGlowCrossfade(Border from, Border to)
    {
        var fromOpacity = from.Opacity;
        var toOpacity = to.Opacity;
        from.BeginAnimation(UIElement.OpacityProperty, null);
        to.BeginAnimation(UIElement.OpacityProperty, null);
        from.Opacity = fromOpacity;
        to.Opacity = toOpacity;
    }

    private static void CopyGlowWash(Border from, Border to, Border waveFrom, Border waveTo)
    {
        StopGlowCrossfade(waveFrom, waveTo);
        waveFrom.Background = from.Background;
        waveFrom.Opacity = from.Opacity;
        waveTo.Background = to.Background;
        waveTo.Opacity = to.Opacity;
    }

    private static Border CreatePaneFocusLine() => new()
    {
        Height = PaneFocusLineHeight,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Top,
        Background = CreatePaneFocusLineBrush(),
        IsHitTestVisible = false,
        SnapsToDevicePixels = true,
        Visibility = Visibility.Collapsed,
    };

    private void ApplyPaneFocusLineBrush()
    {
        var brush = CreatePaneFocusLineBrush();
        _treeFocusLine.Background = brush;
        _favoritesFocusLine.Background = brush;
        _listFocusLine.Background = brush;
    }

    internal static Color PaneFocusLineColor(UiTheme theme) =>
        theme == UiTheme.Light
            ? Color.FromArgb(PaneFocusLineAlphaLight, 0x2C, 0x2C, 0x30)
            : Color.FromArgb(PaneFocusLineAlpha, 255, 255, 255);

    internal static Brush CreatePaneFocusLineBrush() =>
        CreatePaneFocusLineBrush(UiThemeService.Current);

    internal static Brush CreatePaneFocusLineBrush(UiTheme theme)
    {
        var top = PaneFocusLineColor(theme);
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1),
        };
        brush.GradientStops.Add(new GradientStop(top, 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, top.R, top.G, top.B), 1));
        brush.Freeze();
        return brush;
    }

    private static Border CreateGlowWashLayer() => new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
        IsHitTestVisible = false,
        SnapsToDevicePixels = false,
        UseLayoutRounding = false,
        Opacity = 0,
    };

    private void PlaceArtworkGlow()
    {
        if (UseUnifiedGlow)
        {
            _glowHost.Visibility = Visibility.Collapsed;
            _veil.Visibility = Visibility.Collapsed;
            _waveGlow.Visibility = Visibility.Visible;
            _waveVeil.Visibility = Visibility.Visible;
            _waveGlowHost!.Visibility = Visibility.Visible;
            Background = Brushes.Transparent;
            _root.Background = Brushes.Transparent;
            return;
        }

        if (_artworkGlow)
        {
            _glowHost.Visibility = Visibility.Visible;
            _veil.Visibility = Visibility.Visible;
        }
        else
        {
            _glowHost.Visibility = Visibility.Collapsed;
            _veil.Visibility = Visibility.Collapsed;
        }

        HideWaveGlowHost();
        RestoreLibrarySurface();
    }

    private void HideWaveGlowHost()
    {
        _waveGlow.Visibility = Visibility.Collapsed;
        _waveVeil.Visibility = Visibility.Collapsed;
        if (_waveGlowHost is not null)
        {
            _waveGlowHost.Visibility = Visibility.Collapsed;
        }
    }

    private void RestoreLibrarySurface()
    {
        SetResourceReference(BackgroundProperty, "SurfaceBackBrush");
        _root.SetResourceReference(Panel.BackgroundProperty, "SurfaceBackBrush");
    }

    private void RestartGlowDrift()
    {
        if (_glowDriftRunning)
        {
            StopGlowDrift();
        }

        SyncGlowDrift();
        SyncWashTurns();
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
        if (UseUnifiedGlow)
        {
            AnimateGlowDrift(_waveGlowScale, _waveGlowTranslate);
        }
        else
        {
            AnimateGlowDrift(_glowScale, _glowTranslate);
        }

        _glowDriftRunning = true;
    }

    private static void AnimateGlowDrift(ScaleTransform scale, TranslateTransform translate)
    {
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, CreateGlowDriftPulse(GlowDriftScaleFrom, GlowDriftScaleTo, GlowDriftScaleSeconds));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, CreateGlowDriftPulse(GlowDriftScaleFrom, GlowDriftScaleTo, GlowDriftScaleSeconds));
        translate.BeginAnimation(TranslateTransform.XProperty, CreateGlowDriftPulse(-GlowDriftX, GlowDriftX, GlowDriftXSeconds));
        translate.BeginAnimation(TranslateTransform.YProperty, CreateGlowDriftPulse(-GlowDriftY, GlowDriftY, GlowDriftYSeconds));
    }

    private void StopGlowDrift()
    {
        _glowScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _glowScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _glowTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        _glowTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        _waveGlowScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _waveGlowScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _waveGlowTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        _waveGlowTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        _glowScale.ScaleX = GlowDriftScaleFrom;
        _glowScale.ScaleY = GlowDriftScaleFrom;
        _glowTranslate.X = 0;
        _glowTranslate.Y = 0;
        _waveGlowScale.ScaleX = GlowDriftScaleFrom;
        _waveGlowScale.ScaleY = GlowDriftScaleFrom;
        _waveGlowTranslate.X = 0;
        _waveGlowTranslate.Y = 0;
        _glowDriftRunning = false;
    }

    private void SyncWashTurns()
    {
        var run = _artworkGlow && IsVisible && IsLoaded && _washTurns.Length > 1;
        if (run)
        {
            StartWashTurns();
        }
        else
        {
            StopWashTurns();
        }
    }

    private void StartWashTurns()
    {
        _washTurnTimer.Stop();
        _washTurnTimer.Interval = TimeSpan.FromSeconds(GlowWashTurnSeconds);
        _washTurnTimer.Start();
    }

    private void StopWashTurns() => _washTurnTimer.Stop();

    private void AdvanceWashTurn()
    {
        if (_washTurns.Length == 0)
        {
            return;
        }

        _washTurn = (_washTurn + 1) % _washTurns.Length;
        CrossfadeGlowWash(_washTurns[_washTurn], GlowWashTurnFadeSeconds);
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

    internal static DoubleAnimation CreateGlowCrossfade(double from, double to) =>
        CreateGlowCrossfade(from, to, GlowCrossfadeSeconds);

    /// <summary>
    /// 均等パワー（sin/cos）。直線だと中間で特徴色が沈む。
    /// </summary>
    internal static DoubleAnimation CreateGlowCrossfade(double from, double to, double seconds)
    {
        var rising = to > from;
        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromSeconds(seconds),
            FillBehavior = FillBehavior.HoldEnd,
            EasingFunction = new SineEase
            {
                EasingMode = rising ? EasingMode.EaseOut : EasingMode.EaseIn,
            },
        };
        Timeline.SetDesiredFrameRate(anim, GlowCrossfadeFrameRate);
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
    /// ホストは回さず、90度刻みで描き直した4枚をクロスフェードする。
    /// </summary>
    internal static Brush CreateAmbientWash(BitmapSource source, int quarterTurns = 0)
    {
        ArgumentNullException.ThrowIfNull(source);
        return CreateWash(SampleArtworkColors(source, 3), quarterTurns);
    }

    internal static Brush[] CreateAmbientWashTurns(BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var colors = SampleArtworkColors(source, 3);
        var turns = new Brush[GlowWashTurnSteps];
        for (var i = 0; i < turns.Length; i++)
        {
            turns[i] = CreateWash(colors, i);
        }

        return turns;
    }

    /// <summary>ジャケットが無い、または未読み込みのときのネイビー・シアン・白。</summary>
    internal static Brush CreateFallbackAmbientWash() => CreateFallbackWashTurns()[0];

    internal static Brush[] CreateFallbackWashTurns()
    {
        if (_fallbackWashTurns is not null)
        {
            return _fallbackWashTurns;
        }

        var turns = new Brush[GlowWashTurnSteps];
        for (var i = 0; i < turns.Length; i++)
        {
            turns[i] = CreateFallbackWash(i);
        }

        _fallbackWashTurns = turns;
        return turns;
    }

    private static Brush CreateFallbackWash(int quarterTurns)
    {
        var group = new DrawingGroup();
        var navy = new SolidColorBrush(FallbackWashNavy);
        navy.Freeze();
        group.Children.Add(new GeometryDrawing(navy, null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
        AddColorBlob(group, FallbackWashCyan, new Point(0.72, 0.38), 0.52, alpha: 200);
        AddColorBlob(group, FallbackWashWhite, new Point(0.30, 0.62), 0.46, alpha: 175);
        AddColorBlob(group, FallbackWashNavy, new Point(0.50, 0.22), 0.58, alpha: 170);
        ApplyWashTurn(group, quarterTurns);
        return FreezeWash(group);
    }

    private static Brush CreateWash(Color[] colors, int quarterTurns)
    {
        var group = new DrawingGroup();
        AddColorBlob(group, colors[0], new Point(0.32, 0.38), 0.55, alpha: 210);
        AddColorBlob(group, colors[1], new Point(0.72, 0.42), 0.50, alpha: 180);
        AddColorBlob(group, colors[2], new Point(0.48, 0.78), 0.58, alpha: 160);
        ApplyWashTurn(group, quarterTurns);
        return FreezeWash(group);
    }

    private static void ApplyWashTurn(DrawingGroup group, int quarterTurns)
    {
        var turns = ((quarterTurns % GlowWashTurnSteps) + GlowWashTurnSteps) % GlowWashTurnSteps;
        if (turns == 0)
        {
            return;
        }

        var rotate = new RotateTransform(turns * 90, 0.5, 0.5);
        rotate.Freeze();
        group.Transform = rotate;
    }

    private static DrawingBrush FreezeWash(DrawingGroup group)
    {
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

        bitmap ??= LibraryPlaceholderJacket.Bitmap;
        _groupJackets[key] = bitmap;
        return bitmap;
    }

    private void InvalidateGroupJackets()
    {
        _groupJackets.Clear();
        if (_groupJacketsInvalidateQueued)
        {
            return;
        }

        _groupJacketsInvalidateQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _groupJacketsInvalidateQueued = false;
            GroupArtworkChanged?.Invoke();
        }, DispatcherPriority.Background);
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
                || session.Document.SourceKind is not (AudioFileKind.Mp3 or AudioFileKind.M4a)
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

            var kind = session.Document.SourceKind;
            var bytes = await Task.Run(() =>
            {
                if (kind == AudioFileKind.M4a)
                {
                    return M4aArtwork.TryRead(path, out var art) ? art : [];
                }

                return Id3Artwork.TryRead(path, out var mp3) ? mp3 : [];
            }).ConfigureAwait(true);
            if (gen != _groupArtLoad || bytes.Length == 0)
            {
                continue;
            }

            session.Document.SetArtwork(bytes);
            UpdateSessionRow(session);
        }
    }

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
        append.Click += (_, _) => OpenSelectedFolder();
        var add = new MenuItem { Header = UiStrings.LibraryMenuAddToFavorites };
        add.Click += (_, _) => AddSelectedExplorerToFavorites();
        menu.Items.Add(replace);
        menu.Items.Add(append);
        menu.Items.Add(add);
        menu.Opened += (_, _) =>
        {
            var enabled = SelectedExplorerFolders.Length > 0;
            replace.IsEnabled = enabled;
            append.IsEnabled = enabled;
            add.IsEnabled = enabled;
        };
        _folderTree.ContextMenu = menu;
    }

    private void RebuildFavoritesContextMenu()
    {
        var menu = new ContextMenu();
        var remove = new MenuItem { Header = UiStrings.LibraryMenuRemoveFromFavorites };
        remove.Click += (_, _) => RemoveSelectedFavorites();
        menu.Items.Add(remove);
        menu.Opened += (_, _) =>
        {
            remove.IsEnabled = SelectedFavoritePaths.Length > 0;
        };
        _favoritesList.ContextMenu = menu;
    }

    private void RebuildListContextMenu()
    {
        var menu = new ContextMenu();
        var clear = new MenuItem { Header = UiStrings.LibraryMenuClearFromPlaylist };
        clear.Click += (_, _) => ClearPlaylistRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(clear);
        menu.Opened += (_, _) =>
        {
            clear.IsEnabled = SelectedSessions.Length > 0;
        };
        _grid.ContextMenu = menu;
    }

    private void FavoritesList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            RemoveSelectedFavorites();
            e.Handled = true;
            return;
        }

        if (key == Key.Enter
            && Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            var paths = SelectedFavoritePaths;
            if (paths.Length == 0)
            {
                e.Handled = true;
                return;
            }

            FavoritesActivated?.Invoke(
                this,
                new LibraryFavoritesActivateEventArgs(paths, clearPlaylist: Keyboard.Modifiers == ModifierKeys.None));
            e.Handled = true;
        }
    }

    private void FavoritesList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CancelPlaylistFocusRestore();
        _favoritesDragStart = e.GetPosition(null);
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
        var data = new DataObject(DataFormats.FileDrop, paths);
        DragDrop.DoDragDrop(_favoritesList, data, DragDropEffects.Copy);
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

    private sealed record GroupOption(LibraryFileGroup Group, string Label);
}

/// <summary>
/// 列見出し。▼▲ を自分で持つ。DataGrid の SortDirection トリガーは読み込みで消える。
/// </summary>
internal sealed class LibrarySortHeader : StackPanel
{
    internal const double MarkWidth = 6;
    internal const double MarkMargin = 3;
    internal const double MarksGap = 4;

    private readonly TextBlock _label;
    private readonly StackPanel _marks;
    private readonly System.Windows.Shapes.Path _down;
    private readonly System.Windows.Shapes.Path _up;

    public LibrarySortHeader(string label)
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        _label = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _marks = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(MarksGap, 0, 0, 0),
        };
        _down = CreateMark("ArrowDown", "M0,0 L8,0 L4,6 Z");
        _up = CreateMark("ArrowUp", "M0,6 L8,6 L4,0 Z");
        _marks.Children.Add(_down);
        _marks.Children.Add(_up);
        Children.Add(_label);
        Children.Add(_marks);
        ShowSort(active: false, ascending: true);
    }

    public string Label
    {
        get => _label.Text;
        set => _label.Text = value;
    }

    public void ShowSort(bool active, bool ascending)
    {
        var show = active ? Visibility.Visible : Visibility.Collapsed;
        _marks.Visibility = show;
        _down.Visibility = show;
        _up.Visibility = show;
        if (!active)
        {
            return;
        }

        _up.SetResourceReference(
            System.Windows.Shapes.Shape.FillProperty,
            ascending ? "AccentCyanBrush" : "PrimaryForeBrush");
        _down.SetResourceReference(
            System.Windows.Shapes.Shape.FillProperty,
            ascending ? "PrimaryForeBrush" : "AccentCyanBrush");
        _up.Opacity = 1;
        _down.Opacity = 1;
    }

    private static System.Windows.Shapes.Path CreateMark(string name, string data) =>
        new()
        {
            Name = name,
            Data = Geometry.Parse(data),
            Width = MarkWidth,
            Height = 5,
            Stretch = Stretch.Fill,
            Margin = new Thickness(MarkMargin, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
}

internal sealed class LibraryFavoritesActivateEventArgs : EventArgs
{
    public LibraryFavoritesActivateEventArgs(IReadOnlyList<string> paths, bool clearPlaylist)
    {
        Paths = paths;
        ClearPlaylist = clearPlaylist;
    }

    public IReadOnlyList<string> Paths { get; }

    public bool ClearPlaylist { get; }
}

internal sealed class LibraryFavoriteRow
{
    public required string Path { get; init; }

    public required string Name { get; init; }

    public static LibraryFavoriteRow FromPath(string path) =>
        new()
        {
            Path = path,
            Name = LibraryFavoritePaths.DisplayName(path),
        };
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
    private readonly TranslateTransform _stick = new();
    private readonly LibraryGroupHorizontalPin _pin;
    private LibraryBrowserView? _owner;

    public LibraryGroupJacketImage()
    {
        _pin = new LibraryGroupHorizontalPin(_stick);
        Orientation = Orientation.Vertical;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(8, 0, 8, 4);
        Width = DesignMetrics.LibraryGroupJacketSize;
        IsHitTestVisible = false;
        RenderTransform = _stick;

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
        SizeChanged += (_, _) => UpdateSticky();
    }

    internal double StickyOffsetY => _stick.Y;

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
        _owner?.EnsureGroupScrollHook();
        if (_owner is not null)
        {
            _owner.GroupArtworkChanged -= Reload;
            _owner.GroupArtworkChanged += Reload;
            _owner.GroupViewportChanged -= OnViewportChanged;
            _owner.GroupViewportChanged += OnViewportChanged;
        }

        Reload();
        UpdateSticky();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_owner is not null)
        {
            _owner.GroupArtworkChanged -= Reload;
            _owner.GroupViewportChanged -= OnViewportChanged;
            _owner = null;
        }

        _stick.Y = 0;
        _pin.Reset();
    }

    private void OnViewportChanged(object? sender, EventArgs e) => UpdateSticky();

    private void UpdateSticky()
    {
        if (_owner is null || Parent is not FrameworkElement stage || ActualHeight < 1)
        {
            return;
        }

        _owner.EnsureGroupScrollHook();

        try
        {
            var headerBottom = _owner.RowsViewportTop();
            var natural = TransformToAncestor(_owner.FileGrid).Transform(new Point(0, 0)).Y - _stick.Y;
            var max = Math.Max(0, stage.ActualHeight - ActualHeight);
            var offset = Math.Clamp(headerBottom - natural, 0, max);
            if (Math.Abs(_stick.Y - offset) > 0.5)
            {
                _stick.Y = offset;
            }

            _pin.Update(_owner, this);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void Reload()
    {
        var owner = _owner ?? FindOwner();
        var art = owner?.ArtworkForGroup(DataContext as CollectionViewGroup)
            ?? LibraryPlaceholderJacket.Bitmap;
        _face.Source = art;
        _reflection.Source = art;
        Visibility = Visibility.Visible;
        Opacity = 1;
        _reflection.Visibility = Visibility.Visible;
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

/// <summary>
/// 横スクロール分だけ右へ戻し、グループ名とジャケットをビューポート左に残す。
/// </summary>
internal sealed class LibraryGroupHorizontalPin
{
    private readonly TranslateTransform _stick;
    private double? _restX;

    public LibraryGroupHorizontalPin(TranslateTransform stick) => _stick = stick;

    public void Reset()
    {
        _restX = null;
        _stick.X = 0;
    }

    public void Update(LibraryBrowserView owner, FrameworkElement element)
    {
        double natural;
        try
        {
            natural = element.TransformToAncestor(owner.FileGrid).Transform(new Point(0, 0)).X - _stick.X;
        }
        catch (InvalidOperationException)
        {
            return;
        }

        var scroll = owner.GroupHorizontalOffset();
        if (scroll < 1)
        {
            _restX = natural;
        }
        else
        {
            _restX ??= natural + scroll;
        }

        var target = Math.Max(0, _restX.Value - natural);
        if (Math.Abs(_stick.X - target) > 0.5)
        {
            _stick.X = target;
        }
    }
}

internal sealed class LibraryGroupHeader : TextBlock
{
    private readonly TranslateTransform _stick = new();
    private readonly LibraryGroupHorizontalPin _pin;
    private LibraryBrowserView? _owner;

    public LibraryGroupHeader()
    {
        _pin = new LibraryGroupHorizontalPin(_stick);
        SetBinding(TextProperty, new Binding("Name"));
        FontWeight = FontWeights.SemiBold;
        FontSize = LibraryBrowserView.LibraryListFontSize;
        Margin = new Thickness(8, 8, 8, 4);
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalAlignment = HorizontalAlignment.Left;
        SetResourceReference(ForegroundProperty, "PrimaryForeBrush");
        RenderTransform = _stick;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _owner = FindOwner();
        _owner?.EnsureGroupScrollHook();
        if (_owner is not null)
        {
            _owner.GroupViewportChanged -= OnViewportChanged;
            _owner.GroupViewportChanged += OnViewportChanged;
        }

        UpdatePin();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_owner is not null)
        {
            _owner.GroupViewportChanged -= OnViewportChanged;
            _owner = null;
        }

        _pin.Reset();
    }

    private void OnViewportChanged(object? sender, EventArgs e) => UpdatePin();

    private void UpdatePin()
    {
        if (_owner is null)
        {
            return;
        }

        _owner.EnsureGroupScrollHook();
        _pin.Update(_owner, this);
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
