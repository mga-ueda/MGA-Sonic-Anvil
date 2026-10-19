using System.Collections.ObjectModel;
using System.ComponentModel;
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

/// <summary>F10 上段。左フォルダツリー、中央リスト。ジャケットは背面のぼかしだけ。</summary>
internal sealed class LibraryBrowserView : UserControl
{
    /// <summary>色抽出用の縮小長辺。ピクセル数はごく少ない。</summary>
    internal const int GlowSampleEdge = 12;
    internal const double GlowDriftScaleFrom = 1.08;
    internal const double GlowDriftScaleTo = 1.22;
    internal const double GlowDriftX = 72;
    internal const double GlowDriftY = 52;
    internal const double GlowDriftScaleSeconds = 11;
    internal const double GlowDriftXSeconds = 9;
    internal const double GlowDriftYSeconds = 13;
    internal const int GlowDriftFrameRate = 16;
    /// <summary>全画面ウォッシュのキャッシュ解像度。1 だとクロスフェードのたびに巨大ビットマップを作り直す。</summary>
    internal const double GlowCacheRenderAtScale = 0.35;
    /// <summary>
    /// ウォッシュのレイアウト寸法。ウィンドウサイズには追従させない。
    /// 追従すると BitmapCache がモード切替（F11／F12 → F10／F9）のたびに全面を描き直す。
    /// </summary>
    internal const double GlowCacheWidth = 1600;
    internal const double GlowCacheHeight = 900;
    internal const double GlowCrossfadeSeconds = 1;
    internal const int GlowCrossfadeFrameRate = 30;
    internal const int GlowWashTurnSteps = 4;
    internal const double GlowWashTurnSeconds = 20;
    internal const double GlowWashTurnFadeSeconds = 10;
    /// <summary>フルウィンドウの Scale 1.22 とドリフトが端を空けない余白。</summary>
    private const double GlowDriftBleed = 240;
    /// <summary>ツリーとプレイリストで揃える文字サイズ。</summary>
    internal const double LibraryListFontSize = 11;
    /// <summary>セル左右 Margin。</summary>
    internal const double LibraryColumnCellPadX = 8;
    /// <summary>ツリー／お気に入り／プレイリストの選択塗り。ジャケットを透かす。</summary>
    internal static byte LibrarySelectionFillAlpha =>
        PlayerChrome.Get("PlayerSelectionFillBrush", UiTheme.Dark).A;
    /// <summary>ライトは白地にシアンが沈むので、選択を濃くする。</summary>
    internal static byte LibrarySelectionFillAlphaLight =>
        PlayerChrome.Get("PlayerSelectionFillBrush", UiTheme.Light).A;
    /// <summary>同マウスオーバー塗り。選択より濃く、行が判る明るさ。</summary>
    internal static byte LibraryHoverFillAlpha => PlayerChrome.Get("PlayerHoverFillBrush").A;
    /// <summary>列見出し左右 Padding。</summary>
    internal const double LibraryColumnHeaderPadX = 8;
    /// <summary>ソート中の見出しの ▼▲（余白込み）。</summary>
    internal const double LibraryColumnSortPad =
        LibrarySortHeader.MarksGap + ((LibrarySortHeader.MarkMargin + LibrarySortHeader.MarkWidth) * 2);
    internal const double PaneFocusLineHeight = 36;
    internal static byte PaneFocusLineAlpha =>
        PlayerChrome.Get("PlayerPaneFocusLineBrush", UiTheme.Dark).A;
    /// <summary>ライトは白地に白が乗らないので、暗い色をやや濃くする。</summary>
    internal static byte PaneFocusLineAlphaLight =>
        PlayerChrome.Get("PlayerPaneFocusLineBrush", UiTheme.Light).A;
    internal const string PlaylistRowBandName = "PlaylistRowBand";
    internal static Color FallbackWashNavy => PlayerChrome.Get("PlayerFallbackWashNavyBrush");
    internal static Color FallbackWashBlue => PlayerChrome.Get("PlayerFallbackWashBlueBrush");
    internal static Color FallbackWashCyan => PlayerChrome.Get("PlayerFallbackWashCyanBrush");
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
    private bool _glowDriftUsesWave;
    private int _glowDriftStartCount;
    private readonly DispatcherTimer _washTurnTimer = new()
    {
        Interval = TimeSpan.FromSeconds(GlowWashTurnSeconds),
    };
    private Brush[] _washTurns = [];
    private int _washTurn;
    private readonly Border _veil = new();
    private readonly Grid _waveGlow = new();
    /// <summary>固定寸法のウォッシュを窓いっぱいに置く。Canvas の要求サイズは 0 なので窓を押し広げない。</summary>
    private readonly Canvas _waveGlowCanvas = new()
    {
        IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
    };
    private readonly Border _waveGlowFrom = CreateGlowWashLayer();
    private readonly Border _waveGlowTo = CreateGlowWashLayer();
    private readonly Border _waveVeil = new();
    private Brush? _glowWash;
    private readonly ScaleTransform _waveGlowScale = new(GlowDriftScaleFrom, GlowDriftScaleFrom);
    private readonly TranslateTransform _waveGlowTranslate = new();
    private readonly ScaleTransform _waveGlowCover = new(1, 1);
    private Grid? _waveGlowHost;
    private bool _extendGlow;
    private readonly Grid _root = new();
    private readonly ColumnDefinition _treeColumn = new();
    private readonly ColumnDefinition _treeSplitterColumn = new();
    private readonly RowDefinition _explorerTreeRow = new() { Height = new GridLength(1, GridUnitType.Star) };
    private readonly RowDefinition _favoritesRow = new() { Height = new GridLength(1, GridUnitType.Star) };
    private readonly LibraryHoverSplitter _favoritesSplitter = new();
    private readonly LibraryHoverSplitter _explorerSplitter = new();
    private bool _sidePanesVisible = true;
    private double _sidePanesRememberedWidth;
    private readonly TreeView _folderTree = new();
    private readonly TextBlock _explorerLabel = new();
    private readonly TextBlock _favoritesLabel = new();
    private readonly ListBox _favoritesList = new();
    private readonly Border _treeFocusLine = CreatePaneFocusLine();
    private readonly Border _favoritesFocusLine = CreatePaneFocusLine();
    private readonly Border _listFocusLine = CreatePaneFocusLine();
    /// <summary>フォルダツリー＋見出し。ルート登録ドロップの当たり判定用。</summary>
    private FrameworkElement? _explorerDropHost;
    private readonly ObservableCollection<LibraryFavoriteRow> _favorites = [];
    private readonly HashSet<TreeViewItem> _treeMultiSelected = [];
    private readonly HashSet<string> _cutPaths = new(StringComparer.OrdinalIgnoreCase);
    private TreeViewItem? _treeSelectionAnchor;
    private string[] _explorerRoots = LibraryExplorerPaths.ResolveRoots(null);
    private readonly HashSet<string> _explorerExpanded = new(StringComparer.OrdinalIgnoreCase);
    private bool _treeExpansionBusy;
    private bool _explorerBringIntoViewBusy;
    private readonly ComboBox _groupCombo = new();
    private readonly TextBlock _playlistLabel = new();
    private readonly TextBlock _groupLabel = new();
    private readonly TextBox _explorerSearchBox = new();
    private readonly TextBlock _explorerSearchHint = new();
    private readonly TextBox _playlistSearchBox = new();
    private readonly TextBlock _playlistSearchHint = new();
    private readonly CheckBox _shuffleCheck = new();
    private readonly CheckBox _autoPlayCheck = new();
    private readonly CheckBox _autoPlayNextCheck = new();
    private readonly LibraryPlaylistShuffle _shuffle = new();
    private bool _shuffleEnabled;
    private bool _shuffleBusy;
    private bool _autoPlayEnabled = true;
    private bool _autoPlayBusy;
    private bool _autoPlayNextEnabled;
    private bool _autoPlayNextBusy;
    private readonly List<string[]> _playlistSearchGroups = [];
    private readonly List<string[]> _explorerSearchGroups = [];
    private string _explorerSearchCommitted = string.Empty;
    private string _playlistSearchCommitted = string.Empty;
    private int _explorerFocusTicket;
    private int _listFocusTicket;

    /// <summary>
    /// Enter 登録でプレイリストへ移したあと。ツリーの選択項目がフォーカスを取り戻しても戻さない。
    /// 名前変更・削除・F5 の <see cref="FocusExplorer"/> と、ツリーのクリックで外す。
    /// </summary>
    private bool _holdPlaylistFocus;

    private bool _playlistFocusReclaimQueued;

    private bool _playlistFocusReclaiming;

    private int _playlistFocusReclaimCount;

    private DispatcherTimer? _playlistFocusHoldTimer;

    /// <summary>行の組み直しでツリーへ戻されても、この回数まではプレイリストへ戻す。</summary>
    private const int PlaylistFocusReclaimLimit = 48;
    private HashSet<string>? _explorerVisibleFolders;
    private Dictionary<string, string[]>? _explorerTagTexts;
    /// <summary>トーストに出す打ち込み中の文字。トースト消去と同時に捨てる。</summary>
    private string _explorerTypeahead = "";
    /// <summary>黄背景と ↑↓ 渡り歩き用。Esc まで残す。</summary>
    private string _explorerTypeaheadWalk = "";
    private string _explorerTypeaheadIgnoreText = "";
    private readonly Grid _transferToast = new()
    {
        IsHitTestVisible = false,
        Opacity = 0,
        Visibility = Visibility.Collapsed,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
    };

    private readonly TextBlock _transferToastText = new();
    private readonly FrostedPlate _transferToastPlate = new();
    private DispatcherTimer? _transferToastTimer;

    private readonly Grid _explorerTypeaheadToast = new()
    {
        Opacity = 0,
        IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _explorerTypeaheadToastText = new();
    private readonly HashSet<TreeViewItem> _explorerTypeaheadHighlighted = [];
    private DispatcherTimer? _explorerTypeaheadToastIdleTimer;
    private static readonly SolidColorBrush ExplorerTypeaheadHitBack = CreateExplorerTypeaheadHitBack();
    private static readonly SolidColorBrush ExplorerTypeaheadHitFore = CreateExplorerTypeaheadHitFore();
    private readonly StackPanel _playlistGroupHost = new() { Orientation = Orientation.Horizontal };
    private readonly DataGrid _grid = new();
    private readonly LibraryVisualStage _visualStage = new();

    /// <summary>B で固定した PDF ページ。静止の背面レイヤー（ドリフトやウォッシュ加工なし）。</summary>
    private readonly Grid _pinnedPdfHost = new()
    {
        Background = Brushes.Black,
        ClipToBounds = true,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
    };

    private readonly Image _pinnedPdfImage = new()
    {
        Stretch = Stretch.Uniform,
        IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly Border _pinnedPdfDim = new()
    {
        Background = Brushes.Black,
        Opacity = LibraryVisualStage.StoppedDimOpacity,
        IsHitTestVisible = false,
    };

    private bool _pdfBackgroundPinned;
    private string? _playlistVisualSourcePath;
    private DataGridColumn _groupSpacer = null!;
    private bool _gridScrollHooked;
    private readonly Dictionary<LibraryFileColumn, DataGridColumn> _columns = [];
    private readonly Dictionary<LibraryFileColumn, LibrarySortHeader> _sortHeaders = [];
    private CancellationTokenSource? _playlistWaveCts;
    private int _playlistWaveGeneration;
    private CancellationTokenSource? _playlistLoudnessCts;
    private int _playlistLoudnessGeneration;
    private LibraryPlaylistWaveformSize _playlistWaveSizeWave = LibraryPlaylistWaveformSize.L;
    private LibraryPlaylistWaveformSize _playlistWaveSizeMp3 = LibraryPlaylistWaveformSize.L;
    private int _defaultSampleRate = DefaultAudioFormat.SampleRate;
    private int _defaultBitsPerSample = DefaultAudioFormat.BitsPerSample;
    private int _defaultChannels = ChannelLayout.Stereo.Channels;
    private ContextMenu? _columnHeaderMenu;
    private int _sortChromeTicket;
    private LibraryFileColumn[] _waveColumns = [.. LibraryColumnFilter.WaveDefaults];
    private LibraryFileColumn[] _mp3Columns = [.. LibraryColumnFilter.Mp3Defaults];
    private LibraryFileColumn[] _videoColumns = [.. LibraryColumnFilter.VideoDefaults];
    private LibraryFileColumn[] _mixedColumns =
        LibraryColumnFilter.Union(
            LibraryColumnFilter.WaveDefaults,
            LibraryColumnFilter.Mp3Defaults,
            LibraryColumnFilter.VideoDefaults);
    private bool _mixedColumnsCustomized;
    private LibraryFileColumn[] _visibleColumns = [.. LibraryColumnFilter.WaveDefaults];
    private bool _columnOrderBusy;
    private int _columnOrderCommitGate;
    private bool _columnChromeFadingOut;
    private int _columnChromeFadeTicket;
    private HashSet<LibraryFileColumn>? _fadingColumnSnapshot;
    private LibraryPlaylistColumnKind _playlistColumnKind = LibraryPlaylistColumnKind.Mixed;
    private bool _syncing;
    private int _syncGeneration;
    private int _playlistSelectionNotifyDepth;
    private bool _playlistSelectionNotifyPending;
    private bool _deferActivate;
    private bool _restoreListFocus;
    private int _restoreListFocusGeneration;
    private LibraryFileColumn? _sortColumn = LibraryFileColumn.Album;

    /// <summary>列見出しのソートが付いているときだけ true。</summary>
    public bool PlaylistSortActive => _sortColumn is not null;
    private LibrarySortDirection _sortDirection = LibrarySortDirection.Ascending;
    private LibraryFileGroup _group = LibraryFileGroup.Folder;
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

    /// <summary>ドリフトを実際に開始した回数。継続では増えない。</summary>
    internal int GlowDriftStartCount => _glowDriftStartCount;

    /// <summary>モード突入で背景を描き直さず、今の動きを続ける。</summary>
    internal bool AmbientGlowAnimating => _glowDriftRunning && _artworkGlow;

    internal double WaveGlowLayoutWidth => _waveGlow.Width;

    internal double WaveGlowCoverScale => _waveGlowCover.ScaleX;
    private int _groupArtLoad;
    private bool _treeSyncing;
    private Point? _treeDragStart;
    private TreeViewItem? _treeDragItem;
    private string[] _treeDragPaths = [];
    /// <summary>DoDragDrop 中だけ。OLE 経由だとカスタム形式が消えるのでフィールドで持つ。</summary>
    private string[] _explorerRootReorderDrag = [];
    private TreeViewItem? _treePendingSingleSelect;
    private bool _treeRangeBusy;
    private Point? _favoritesDragStart;
    private int _favoritesAnchorIndex = -1;
    private int _favoritesCaretIndex = -1;
    private bool _favoritesRangeBusy;
    private Point? _playlistDragStart;
    private string[] _playlistDragPaths = [];
    private bool _playlistDragBusy;
    private LibraryFileRow? _playlistPendingSingleSelect;
    /// <summary>コンテキストメニュー閉鎖直後の MouseUp で plain-press 確定しない。</summary>
    private bool _suppressPlaylistPlainPressComplete;
    /// <summary>
    /// 右クリックメニューの外側クリック dismiss は、下にあるコントロール（例: トランスポートの
    /// プレイヤー切替ボタン）へクリックが貫通する。メニューが開いてから閉じた直後のアイドルまで
    /// 立てておき、その貫通クリックでプレイヤーを抜けて（＝選択以外のトラックが消えて）しまうのを防ぐ。
    /// </summary>
    private bool _contextMenuDismissGuard;
    private int _contextMenuDismissDepth;

    public event EventHandler<DocumentSession>? SessionActivated;

    /// <summary>プレイリストの選択集合が変わった。複数選択の平均ラウドネス用。</summary>
    public event EventHandler? PlaylistSelectionChanged;

    /// <summary>ダブルクリック。停止中でも再生する。</summary>
    public event EventHandler<DocumentSession>? SessionPlayRequested;

    public event EventHandler? PlaylistVisualEnded;

    public event EventHandler<TimeSpan>? PlaylistVisualOpened;

    public event EventHandler? PlaylistVisualStateChanged;

    public event EventHandler<LibraryColumnPresets>? VisibleColumnPresetsChanged;

    public event EventHandler<LibraryFileGroup>? GroupChanged;

    public event EventHandler<LibraryPlaylistSort>? SortChanged;

    public event EventHandler<string>? ExplorerFolderChanged;

    /// <summary>ツリーの展開セットが変わった（パスはフルパス）。</summary>
    public event EventHandler<IReadOnlyList<string>>? ExplorerExpandedChanged;

    public event EventHandler<string>? ExplorerFolderOpened;

    /// <summary>複数フォルダを追加。引数はフォルダパス。再帰は <see cref="ExplorerOpenRecursive"/>。</summary>
    public event EventHandler<IReadOnlyList<string>>? ExplorerFoldersOpened;

    /// <summary>直近の <see cref="OpenSelectedFolder"/> が再帰追加か。</summary>
    public bool ExplorerOpenRecursive { get; private set; }

    public event EventHandler<double>? ExplorerWidthChanged;

    /// <summary>お気に入りペインの高さ比（0–1）。</summary>
    public event EventHandler<double>? FavoritesSplitChanged;

    public event EventHandler<IReadOnlyList<string>>? FavoritesChanged;

    public event EventHandler<IReadOnlyList<string>>? ExplorerRootsChanged;

    /// <summary>お気に入りをプレイリストへ。ClearPlaylist ならクリアして追加（再生は Auto Play）。</summary>
    public event EventHandler<LibraryFavoritesActivateEventArgs>? FavoritesActivated;

    /// <summary>ツリーの Enter と同じ。プレイリストを空にしてから選んだフォルダ直下を載せる。</summary>
    public event EventHandler? ExplorerReplacePlaylistRequested;

    public event EventHandler? ClearPlaylistRequested;

    public event EventHandler? MatchLoudnessRequested;

    public event EventHandler? ExportMp3Requested;

    public event EventHandler? PlaylistPreviousRequested;

    public event EventHandler? PlaylistNextRequested;

    public event EventHandler? PlaylistRestartRequested;

    /// <summary>Ctrl+F5 と同じ索引の再構築。</summary>
    public event EventHandler? LibraryCatalogRebuildRequested;

    public event EventHandler<LibraryPane>? LibraryPasteRequested;

    public event EventHandler<LibraryPane>? LibraryRenameRequested;

    public event EventHandler<LibraryPane>? LibraryDeleteRequested;

    public event EventHandler? PlaybackPitchUpRequested;

    public event EventHandler? PlaybackPitchDownRequested;

    public event EventHandler? PlaybackPitchResetRequested;

    /// <summary>リストの右クリックメニューを開いた／閉じた。F10 誤解除や plain-press 確定を抑止する。</summary>
    public event EventHandler? ListContextMenuOpened;

    public event EventHandler? ListContextMenuClosed;

    internal event Action? GroupArtworkChanged;

    internal event EventHandler? GroupViewportChanged;

    public bool JacketReplaceEnabled { get; private set; }

    public LibraryBrowserView()
    {
        Focusable = false;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        BuildLayout();
        _visualStage.Ended += (_, _) => PlaylistVisualEnded?.Invoke(this, EventArgs.Empty);
        _visualStage.Opened += (_, duration) => PlaylistVisualOpened?.Invoke(this, duration);
        ConfigureGrid();
        ConfigureGroupCombo();
        ApplyActiveColumnVisibility(notify: false);
        ApplyLocalizedText();
        ApplyGridStyles();
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.None);
        KeyboardNavigation.SetDirectionalNavigation(this, KeyboardNavigationMode.None);
        _folderTree.IsKeyboardFocusWithinChanged += (_, _) =>
        {
            SyncPaneFocusChrome();
            // ツリー以外へフォーカスが移ったら打ち込みを捨てる。
            if (!_folderTree.IsKeyboardFocusWithin)
            {
                ClearExplorerTypeahead();
            }
        };
        _favoritesList.IsKeyboardFocusWithinChanged += (_, _) => SyncPaneFocusChrome();
        _grid.IsKeyboardFocusWithinChanged += Grid_IsKeyboardFocusWithinChanged;
        PreviewMouseMove += PlaylistHost_PreviewMouseMove;
        PreviewMouseLeftButtonUp += PlaylistHost_PreviewMouseLeftButtonUp;
        _groupCombo.IsKeyboardFocusWithinChanged += (_, _) => SyncPaneFocusChrome();
        _explorerSearchBox.IsKeyboardFocusWithinChanged += (_, _) =>
        {
            if (!_explorerSearchBox.IsKeyboardFocusWithin)
            {
                RestoreSearchBox(_explorerSearchBox, _explorerSearchCommitted, _explorerSearchHint);
            }

            SyncPaneFocusChrome();
            SyncSearchHint(_explorerSearchBox, _explorerSearchHint);
        };
        _playlistSearchBox.IsKeyboardFocusWithinChanged += (_, _) =>
        {
            if (!_playlistSearchBox.IsKeyboardFocusWithin)
            {
                RestoreSearchBox(_playlistSearchBox, _playlistSearchCommitted, _playlistSearchHint);
            }

            SyncPaneFocusChrome();
            SyncSearchHint(_playlistSearchBox, _playlistSearchHint);
        };
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
            CancelColumnChromeFade(resetOpacity: true);
            if (_rows.Count == 0)
            {
                CollapseAllPlaylistColumns();
                _grid.HeadersVisibility = DataGridHeadersVisibility.None;
            }
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
        headerStyle.Setters.Add(new Setter(TextOptions.TextRenderingModeProperty, TextRenderingMode.Grayscale));
        headerStyle.Setters.Add(new Setter(TextOptions.TextFormattingModeProperty, TextFormattingMode.Display));
        headerStyle.Setters.Add(new Setter(UIElement.SnapsToDevicePixelsProperty, true));
        headerStyle.Setters.Add(new Setter(Control.TemplateProperty, ColumnHeaderTemplate()));
        _columnHeaderMenu ??= CreateColumnHeaderContextMenu();
        headerStyle.Setters.Add(new Setter(FrameworkElement.ContextMenuProperty, _columnHeaderMenu));
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
        var fore = ResolveThemeBrush("PrimaryForeBrush");
        ApplyNavSelectionResources(_grid, selected, fore);
        SetPlaylistBandLeft(_grid, ShowsGroupJackets ? GroupJacketColumnWidth : 0);
        RefreshSortChrome();
    }

    public bool IsListKeyboardFocused => _grid.IsKeyboardFocusWithin || IsPlaylistSearchFocused;

    public bool IsGroupComboFocused => _groupCombo.IsKeyboardFocusWithin;

    public bool IsExplorerSearchFocused => _explorerSearchBox.IsKeyboardFocusWithin;

    public bool IsPlaylistSearchFocused => _playlistSearchBox.IsKeyboardFocusWithin;

    public bool IsSearchFocused => IsExplorerSearchFocused || IsPlaylistSearchFocused;

    public bool IsExplorerFocused => _folderTree.IsKeyboardFocusWithin || IsExplorerSearchFocused;

    public bool IsExplorerTreeFocused => _folderTree.IsKeyboardFocusWithin;

    public bool IsPlaylistGridFocused => _grid.IsKeyboardFocusWithin;

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
            if (ReferenceEquals(origin, _folderTree)
                || ReferenceEquals(origin, _explorerLabel)
                || ReferenceEquals(origin, _explorerSearchBox)
                || ReferenceEquals(origin, _explorerSearchHint))
            {
                return true;
            }

            origin = VisualTreeHelper.GetParent(origin);
        }

        return false;
    }

    /// <summary>
    /// ドラッグ中の OriginalSource がツリー外扱いになることがあるので、座標でも見る。
    /// </summary>
    public bool IsPointerOverExplorer(DragEventArgs e)
    {
        if (e.OriginalSource is System.Windows.DependencyObject origin
            && IsExplorerOrigin(origin))
        {
            return true;
        }

        var host = _explorerDropHost ?? (FrameworkElement)_folderTree;
        try
        {
            var p = e.GetPosition(host);
            return IsInside(host, p);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>WM_DROPFILES 用。スクリーン座標がフォルダツリー帯か。</summary>
    public bool IsScreenPointOverExplorer(Point screenPoint) =>
        IsScreenPointOver(_explorerDropHost ?? (FrameworkElement)_folderTree, screenPoint);

    /// <summary>WM_DROPFILES 用。スクリーン座標がお気に入り帯か。</summary>
    public bool IsScreenPointOverFavorites(Point screenPoint) =>
        IsScreenPointOver(_favoritesList, screenPoint);

    private static bool IsScreenPointOver(FrameworkElement host, Point screenPoint)
    {
        try
        {
            return IsInside(host, host.PointFromScreen(screenPoint));
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsInside(FrameworkElement host, Point local) =>
        local.X >= 0
        && local.Y >= 0
        && local.X <= host.ActualWidth
        && local.Y <= host.ActualHeight;

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
            if (ReferenceEquals(origin, _grid)
                || ReferenceEquals(origin, _playlistLabel)
                || ReferenceEquals(origin, _playlistSearchBox)
                || ReferenceEquals(origin, _playlistSearchHint))
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
        || _grid.ContextMenu is { IsOpen: true }
        || _columnHeaderMenu is { IsOpen: true };

    /// <summary>
    /// 右クリックメニューが開いている間と、外側クリックで閉じた同じ入力中（アイドルまで）は true。
    /// 外側クリックが下のコントロールへ貫通してプレイヤーを抜けてしまうのを呼び出し側で弾くために使う。
    /// </summary>
    public bool IsContextMenuDismissGuardActive =>
        _contextMenuDismissGuard || HasOpenContextMenu;

    internal object? BoundItemsSource => _grid.ItemsSource;

    internal int BoundRowCount => _items.Count;

    internal FrameworkElement FileGrid => _grid;

    internal ComboBox GroupCombo => _groupCombo;

    internal TextBox ExplorerSearchBox => _explorerSearchBox;

    internal TextBox PlaylistSearchBox => _playlistSearchBox;

    internal TextBlock ExplorerSearchHint => _explorerSearchHint;

    internal TextBlock PlaylistSearchHint => _playlistSearchHint;

    internal int RootColumnCount => _root.ColumnDefinitions.Count;

    internal GridSplitter FavoritesSplitter => _favoritesSplitter;

    internal GridSplitter ExplorerSplitter => _explorerSplitter;

    internal TreeViewItem? ExplorerFirstFolder =>
        _folderTree.Items.OfType<TreeViewItem>().FirstOrDefault();

    internal Style? ExplorerItemStyle => _folderTree.ItemContainerStyle;

    internal void RecalculateColumnWidths() => FitColumns();

    internal double ColumnPixelWidth(LibraryFileColumn column) =>
        _columns.TryGetValue(column, out var gridColumn) ? gridColumn.Width.Value : 0;

    internal Visibility ColumnVisibility(LibraryFileColumn column) =>
        _columns.TryGetValue(column, out var gridColumn)
            ? gridColumn.Visibility
            : Visibility.Collapsed;

    internal bool ColumnChromeFadingOut => _columnChromeFadingOut;

    internal IReadOnlyList<LibraryFileColumn> VisibleColumnOrder => _visibleColumns;

    internal int ColumnDisplayIndex(LibraryFileColumn column) =>
        _columns.TryGetValue(column, out var gridColumn) ? gridColumn.DisplayIndex : -1;

    internal int GroupSpacerDisplayIndex => _groupSpacer.DisplayIndex;

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

    /// <summary>表示順の次の曲。最後の次は先頭。1曲なら同じ曲。ランダム時は一巡まで非重複。</summary>
    public DocumentSession? NextPlaylistSession(DocumentSession? current) =>
        PlaylistSessionByLoop(current, previous: false);

    /// <summary>表示順の前の曲。先頭の前は末尾。1曲なら同じ曲。ランダム時は抽選順を戻る。</summary>
    public DocumentSession? PreviousPlaylistSession(DocumentSession? current) =>
        PlaylistSessionByLoop(current, previous: true);

    public bool PlaylistVisualPlaying => _visualStage.IsPlaying;

    /// <summary>本再生を止めて静止している（暗い 1/3 速プレビューではない）。</summary>
    public bool PlaylistVisualFramePaused => _visualStage.IsFramePaused;

    public bool PlaylistVisualClockRunning => _visualStage.VisualClockRunning;

    public bool PlaylistVisualShown => _visualStage.IsShown;

    public bool PlaylistVisualIsVideo => _visualStage.IsVideo;

    public bool PlaylistVisualIsPdf => _visualStage.IsPdf;

    public bool TryGetPlaylistVideoNaturalPixels(out int width, out int height) =>
        _visualStage.TryGetNaturalVideoPixels(out width, out height);

    public bool PdfBackgroundPinned => _pdfBackgroundPinned;

    public TimeSpan PlaylistVisualPosition => _visualStage.Position;

    public TimeSpan PlaylistVisualDuration => _visualStage.Duration;

    /// <summary>いま背面に出している映像／PDF の元ファイル。プロキシパスではない。</summary>
    public bool IsPlaylistVisualSameSource(string? sourcePath) =>
        !string.IsNullOrWhiteSpace(sourcePath)
        && !string.IsNullOrWhiteSpace(_playlistVisualSourcePath)
        && string.Equals(_playlistVisualSourcePath, sourcePath, StringComparison.OrdinalIgnoreCase);

    public void HidePlaylistVisual()
    {
        _playlistVisualSourcePath = null;
        _visualStage.Hide();
        PlaceArtworkGlow();
        PlaylistVisualStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ShowPlaylistVisual(DocumentSession session, bool play)
    {
        var path = session.Document.SourcePath;
        if (string.IsNullOrWhiteSpace(path) || !LibraryPlaylistDocuments.IsVisual(session.Document))
        {
            HidePlaylistVisual();
            return;
        }

        // プロキシ差し替え判定用。代入前に同じ論理ソースかを見る。
        var sameSourceClip = IsPlaylistVisualSameSource(path);
        _playlistVisualSourcePath = path;

        if (LibraryPlaylistDocuments.IsPdf(session.Document))
        {
            var position = session.Document.SampleRate > 0
                ? TimeSpan.FromSeconds(session.Document.CursorFrame / (double)session.Document.SampleRate)
                : TimeSpan.Zero;
            _visualStage.ShowPdf(path, play, position);
            PlaceArtworkGlow();
            PlaylistVisualStateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var playPath = path;
        if (LibraryPlaylistDocuments.IsVideo(path) && VideoProxy.TryGetDisplayPath(path, out var display))
        {
            playPath = display;
        }

        var fallback = TimeSpan.Zero;
        var tagged = session.Document.Tags.DurationSeconds;
        if (tagged > 0)
        {
            fallback = TimeSpan.FromSeconds(tagged);
        }
        else if (session.Document.SampleRate > 0 && session.Document.FrameCount > 0)
        {
            fallback = TimeSpan.FromSeconds(session.Document.FrameCount / (double)session.Document.SampleRate);
        }

        _visualStage.ShowVideo(playPath, play, fallback, sameSourceClip: sameSourceClip);
        if (play
            && session.Document.SampleRate > 0
            && session.Document.CursorFrame > 0)
        {
            _visualStage.Seek(TimeSpan.FromSeconds(
                session.Document.CursorFrame / (double)session.Document.SampleRate));
        }

        PlaceArtworkGlow();
        PlaylistVisualStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetPlaylistVisualPlaying(bool playing)
    {
        _visualStage.SetPlaying(playing);
        PlaylistVisualStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>本再生をその場で一時停止（暗いプレビューへ落とさない）。</summary>
    public void PausePlaylistVisualFrame()
    {
        _visualStage.PauseAtCurrentFrame();
        PlaylistVisualStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>本再生停止後、指定位置の暗いプレビューへ移す。</summary>
    public void EnterPlaylistVisualDimPreview(TimeSpan position)
    {
        _visualStage.EnterDimPreviewAt(position);
        PlaylistVisualStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 暗いプレビュー／速度ランプ中の同じ動画を、await を挟まず 1 倍・明るい映像へ戻す。
    /// 本再生フラグは立てない（現在位置の引き継ぎ用。クロームは bridge 側で隠す）。
    /// </summary>
    public void PromotePlaylistVisualToMainPlay()
    {
        if (!PlaylistVisualShown || !PlaylistVisualIsVideo || PlaylistVisualPlaying)
        {
            return;
        }

        _visualStage.InterruptPreviewSpeedRampForMainPlay();
        PlaylistVisualStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SeekPlaylistVisual(TimeSpan position) => _visualStage.Seek(position);

    public void SetPlaylistVisualSpeed(double ratio) => _visualStage.SetSpeedRatio(ratio);

    public bool TryStepPlaylistPdfPage(int delta) => _visualStage.TryStepPdfPage(delta);

    public bool TryGoPlaylistPdfPageEdge(int edge) => _visualStage.TryGoPdfPageEdge(edge);

    public bool TryGoPlaylistPdfPageNumber(int pageNumber1Based) =>
        _visualStage.TryGoPdfPageNumber(pageNumber1Based);

    /// <summary>0 始まりの表示中 PDF ページ。PDF 以外は 0。</summary>
    public int PlaylistPdfPageIndex => _visualStage.PdfPageIndex;

    /// <summary>表示中 PDF の総ページ数。</summary>
    public int PlaylistPdfPageCount => _visualStage.PdfPageCount;

    public bool TryZoomPlaylistPdf(int direction, bool isRepeat) =>
        _visualStage.TryZoomPdf(direction, isRepeat);

    public void ResetPlaylistPdfZoomLatch() => _visualStage.ResetPdfZoomLatch();

    /// <summary>
    /// 表示中の PDF ページを静止背景へ固定する（既に固定中なら差し替え）。
    /// 呼び出し側で表示を閉じる。
    /// </summary>
    public bool TryPinPdfBackground()
    {
        if (!_visualStage.TryClonePdfBitmap(out var bitmap) || bitmap is null)
        {
            return false;
        }

        _pinnedPdfImage.Source = bitmap;
        _pdfBackgroundPinned = true;
        PlaceArtworkGlow();
        return true;
    }

    public void ClearPdfBackgroundPin()
    {
        if (!_pdfBackgroundPinned && _pinnedPdfHost.Visibility != Visibility.Visible)
        {
            _pinnedPdfImage.Source = null;
            return;
        }

        _pdfBackgroundPinned = false;
        _pinnedPdfImage.Source = null;
        PlaceArtworkGlow();
    }

    public bool ShuffleEnabled
    {
        get => _shuffleEnabled;
        set => SetShuffleEnabled(value);
    }

    public event EventHandler? ShuffleChanged;

    public void ToggleShuffle() => ShuffleEnabled = !ShuffleEnabled;

    public bool AutoPlayNextEnabled
    {
        get => _autoPlayNextEnabled;
        set => SetAutoPlayNextEnabled(value);
    }

    public event EventHandler? AutoPlayNextChanged;

    public void ToggleAutoPlayNext() => AutoPlayNextEnabled = !AutoPlayNextEnabled;

    /// <summary>Enter（クリアして追加）で先頭を再生するか。既定オン。設定に覚える。</summary>
    public bool AutoPlayEnabled
    {
        get => _autoPlayEnabled;
        set => SetAutoPlayEnabled(value, raise: true);
    }

    public event EventHandler? AutoPlayChanged;

    public void ToggleAutoPlay() => AutoPlayEnabled = !AutoPlayEnabled;

    private void SetShuffleEnabled(bool enabled)
    {
        var changed = _shuffleEnabled != enabled;
        if (!changed && _shuffleCheck.IsChecked == enabled)
        {
            return;
        }

        _shuffleEnabled = enabled;
        if (changed)
        {
            _shuffle.Clear();
        }

        if (_shuffleCheck.IsChecked != enabled)
        {
            _shuffleBusy = true;
            try
            {
                _shuffleCheck.IsChecked = enabled;
            }
            finally
            {
                _shuffleBusy = false;
            }
        }

        if (changed)
        {
            ShuffleChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetAutoPlayEnabled(bool enabled, bool raise)
    {
        var changed = _autoPlayEnabled != enabled;
        if (!changed && _autoPlayCheck.IsChecked == enabled)
        {
            return;
        }

        _autoPlayEnabled = enabled;

        if (_autoPlayCheck.IsChecked != enabled)
        {
            _autoPlayBusy = true;
            try
            {
                _autoPlayCheck.IsChecked = enabled;
            }
            finally
            {
                _autoPlayBusy = false;
            }
        }

        if (changed && raise)
        {
            AutoPlayChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetAutoPlayNextEnabled(bool enabled)
    {
        var changed = _autoPlayNextEnabled != enabled;
        if (!changed && _autoPlayNextCheck.IsChecked == enabled)
        {
            return;
        }

        _autoPlayNextEnabled = enabled;

        if (_autoPlayNextCheck.IsChecked != enabled)
        {
            _autoPlayNextBusy = true;
            try
            {
                _autoPlayNextCheck.IsChecked = enabled;
            }
            finally
            {
                _autoPlayNextBusy = false;
            }
        }

        if (changed)
        {
            AutoPlayNextChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// テンキー 4／6 の曲移動。選択だけ進め、再生はキーを離してから行う（↑↓ と同じ）。
    /// wrap なら端で反対側へ回る。長押し連続は wrap=false で端に一旦止まる。
    /// </summary>
    public void SelectPlaylistStep(DocumentSession? current, int direction, bool wrap)
    {
        if (direction == 0)
        {
            return;
        }

        var session = PlaylistSessionByLoop(current, previous: direction < 0, wrap);
        if (session is null)
        {
            return;
        }

        var index = 0;
        foreach (var row in PlaylistRows())
        {
            if (row.Tag is not DocumentSession item)
            {
                continue;
            }

            if (ReferenceEquals(item, session))
            {
                ApplyMoveSelectionFile(index, extend: false);
                return;
            }

            index++;
        }
    }

    /// <summary>
    /// 再生中の 60fps からも呼ばれる。DataGrid.Items はグループ化ビューを歩いて
    /// コンテナ生成まで起こすので使わない。グループ時は CollectionView.Groups を
    /// フラット化した表示順、未グループ時は並べ済みの _items を見る。
    /// </summary>
    private DocumentSession? PlaylistSessionByLoop(DocumentSession? current, bool previous, bool wrap = true)
    {
        var rows = PlaylistRows();
        var count = 0;
        foreach (var row in rows)
        {
            if (row.Tag is DocumentSession)
            {
                count++;
            }
        }

        if (count <= 0)
        {
            return null;
        }

        var index = -1;
        var i = 0;
        foreach (var row in rows)
        {
            if (row.Tag is not DocumentSession session)
            {
                continue;
            }

            if (current is not null && ReferenceEquals(session, current))
            {
                index = i;
                break;
            }

            i++;
        }

        int at;
        if (index < 0)
        {
            at = _shuffleEnabled
                ? (previous ? _shuffle.Last(count) : _shuffle.First(count))
                : (previous
                    ? LibraryPlayerMode.PreviousLoopIndex(count, -1)
                    : LibraryPlayerMode.NextLoopIndex(count, -1));
        }
        else
        {
            var delta = previous ? -1 : 1;
            at = _shuffleEnabled
                ? (previous
                    ? _shuffle.Previous(count, index, wrap: wrap)
                    : _shuffle.Next(count, index, wrap: wrap))
                : LibraryPlayerMode.StepLoopIndex(count, index, delta, wrap);
        }

        if (at < 0)
        {
            return null;
        }

        i = 0;
        foreach (var row in rows)
        {
            if (row.Tag is not DocumentSession session)
            {
                continue;
            }

            if (i == at)
            {
                return session;
            }

            i++;
        }

        return null;
    }

    /// <summary>
    /// 画面上の再生・選択移動順。グループ化時は ListCollectionView.Groups の並び
    /// （DataGrid と同じ）を使い、ソースの _items 順には戻さない。
    /// </summary>
    private IReadOnlyList<LibraryFileRow> PlaylistRows()
    {
        if (_items.Count == 0)
        {
            return _rows;
        }

        if (_group == LibraryFileGroup.None
            || _grid.ItemsSource is not ListCollectionView { Groups.Count: > 0 } view)
        {
            return _items;
        }

        var ordered = new List<LibraryFileRow>(_items.Count);
        foreach (var entry in view.Groups)
        {
            if (entry is CollectionViewGroup group)
            {
                AppendGroupedPlaylistRows(group, ordered);
            }
        }

        return ordered.Count == _items.Count ? ordered : _items;
    }

    /// <summary>
    /// 画面上の並び（列ソートとグループ）での位置。検索で隠れている行も含む。
    /// </summary>
    public bool TryGetDisplayedTrackPosition(DocumentSession session, out int index0Based, out int count)
    {
        index0Based = -1;
        count = 0;
        foreach (var row in PlaylistDisplayRows())
        {
            if (row.Tag is not DocumentSession item)
            {
                continue;
            }

            if (index0Based < 0 && ReferenceEquals(item, session))
            {
                index0Based = count;
            }

            count++;
        }

        return index0Based >= 0 && count > 0;
    }

    /// <summary>番号表示用。検索中でも登録全件の表示順を使う。</summary>
    private IReadOnlyList<LibraryFileRow> PlaylistDisplayRows()
    {
        if (_rows.Count == 0)
        {
            return _rows;
        }

        if (_playlistSearchGroups.Count == 0)
        {
            return PlaylistRows();
        }

        if (_group == LibraryFileGroup.None)
        {
            return _rows;
        }

        return GroupRowsInDisplayOrder(_rows);
    }

    private static List<LibraryFileRow> GroupRowsInDisplayOrder(IReadOnlyList<LibraryFileRow> rows)
    {
        var order = new List<string>();
        var buckets = new Dictionary<string, List<LibraryFileRow>>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var key = row.GroupKey ?? string.Empty;
            if (!buckets.TryGetValue(key, out var list))
            {
                list = [];
                buckets[key] = list;
                order.Add(key);
            }

            list.Add(row);
        }

        var result = new List<LibraryFileRow>(rows.Count);
        foreach (var key in order)
        {
            result.AddRange(buckets[key]);
        }

        return result;
    }

    private static bool SameSessionSet(
        IReadOnlyList<LibraryFileRow> rows,
        IReadOnlyList<DocumentSession> sessions)
    {
        if (rows.Count != sessions.Count)
        {
            return false;
        }

        var seen = new HashSet<DocumentSession>();
        foreach (var row in rows)
        {
            if (row.Tag is not DocumentSession session || !seen.Add(session))
            {
                return false;
            }
        }

        foreach (var session in sessions)
        {
            if (!seen.Contains(session))
            {
                return false;
            }
        }

        return true;
    }

    private static void SyncPlaylistWaveformsFromDocuments(IReadOnlyList<DocumentSession> sessions)
    {
        var barCount = LibraryPlaylistWaveform.BarCount;
        foreach (var session in sessions)
        {
            var path = session.Document.SourcePath;
            if (string.IsNullOrWhiteSpace(path)
                || !PeakPyramid.CanBuildPlaylistBarsFromPeaks(session.Document.Peaks))
            {
                continue;
            }

            LibraryPlaylistWaveform.Replace(
                path,
                barCount,
                PeakPyramid.BuildPlaylistBarsFromPeaks(session.Document.Peaks, barCount));
        }
    }

    private static void AppendGroupedPlaylistRows(CollectionViewGroup group, List<LibraryFileRow> dest)
    {
        foreach (var item in group.Items)
        {
            if (item is CollectionViewGroup child)
            {
                AppendGroupedPlaylistRows(child, dest);
            }
            else if (item is LibraryFileRow row)
            {
                dest.Add(row);
            }
        }
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

        RefreshGroupJacketsForActiveRow();
    }

    public void FocusList()
    {
        ClearExplorerTypeahead();
        EnsureListFocused();
        if (_grid.SelectedItem is not null)
        {
            EnsureRowVisible(_grid.SelectedItem);
        }
    }

    public void FocusExplorerSearch()
    {
        ClearExplorerTypeahead();
        CancelPlaylistFocusRestore();
        ReleasePlaylistFocusHold();
        _explorerSearchBox.Focus();
        _explorerSearchBox.SelectAll();
    }

    public void FocusPlaylistSearch()
    {
        CancelPlaylistFocusRestore();
        _playlistSearchBox.Focus();
        _playlistSearchBox.SelectAll();
    }

    public void FocusPaneSearch(LibraryPane pane)
    {
        if (LibraryPlayerMode.SearchPane(pane) == LibraryPane.List)
        {
            FocusPlaylistSearch();
            return;
        }

        FocusExplorerSearch();
    }

    /// <summary>Enter。入力を検索に反映し、ツリーまたはプレイリストへフォーカスを返す。</summary>
    public void CommitSearchFocus()
    {
        if (IsExplorerSearchFocused)
        {
            ApplyExplorerSearchSync(_explorerSearchBox.Text);
            RequestExplorerFocus();
            return;
        }

        if (IsPlaylistSearchFocused)
        {
            CommitPlaylistSearch();
            RequestListFocus();
        }
    }

    /// <summary>Esc。未確定の入力を捨てて、ツリーまたはプレイリストへフォーカスを返す。</summary>
    public void CancelSearchFocus()
    {
        if (IsExplorerSearchFocused)
        {
            RestoreSearchBox(_explorerSearchBox, _explorerSearchCommitted, _explorerSearchHint);
            RequestExplorerFocus();
            return;
        }

        if (IsPlaylistSearchFocused)
        {
            RestoreSearchBox(_playlistSearchBox, _playlistSearchCommitted, _playlistSearchHint);
            RequestListFocus();
        }
    }

    internal void RequestExplorerFocus()
    {
        ReleasePlaylistFocusHold();
        _listFocusTicket++;
        var ticket = ++_explorerFocusTicket;
        Dispatcher.BeginInvoke(
            () =>
            {
                if (ticket != _explorerFocusTicket)
                {
                    return;
                }

                FocusExplorer();
            },
            DispatcherPriority.Loaded);
        Dispatcher.BeginInvoke(
            () =>
            {
                if (ticket != _explorerFocusTicket)
                {
                    return;
                }

                if (!IsExplorerTreeFocused)
                {
                    FocusExplorer();
                }
            },
            DispatcherPriority.Input);
    }

    /// <summary>発行したフォーカスチケット。後から <see cref="RequestListFocusIfCurrent"/> で再試行する。</summary>
    internal int RequestListFocus()
    {
        _explorerFocusTicket++;
        var ticket = ++_listFocusTicket;
        // フォルダ Enter は自動再生のオン／オフに関わらずここを通る。
        // 行の組み直しでプレイリストから外れても、短いあいだだけ戻す。
        ArmPlaylistFocusHold();
        Dispatcher.BeginInvoke(
            () =>
            {
                if (ticket != _listFocusTicket)
                {
                    return;
                }

                FocusList();
            },
            DispatcherPriority.Loaded);
        Dispatcher.BeginInvoke(
            () =>
            {
                if (ticket != _listFocusTicket)
                {
                    return;
                }

                if (!IsPlaylistGridFocused)
                {
                    FocusList();
                }
            },
            DispatcherPriority.Input);
        // 行追加や列幅の組み直しは Background で、Loaded／Input のフォーカスをツリーへ戻す。
        // それらが終わったあとに移す。名前変更・削除の FocusExplorer はチケットを進めて止める。
        Dispatcher.BeginInvoke(
            () =>
            {
                if (ticket != _listFocusTicket)
                {
                    return;
                }

                FocusList();
            },
            DispatcherPriority.ApplicationIdle);
        return ticket;
    }

    /// <summary>
    /// <paramref name="ticket"/> がまだ最新なら、プレイリストへもう一度フォーカスする。
    /// あいだにツリーへ戻す操作（名前変更・削除・F5）があれば何もしない。
    /// </summary>
    internal void RequestListFocusIfCurrent(int ticket)
    {
        if (ticket != _listFocusTicket)
        {
            return;
        }

        RequestListFocus();
    }

    /// <summary>
    /// 画面上で removed のいちばん上の行の、1行上。その行も消えるならさらに上。無ければ null。
    /// </summary>
    public string? ExplorerRowAbove(IReadOnlyList<string> removed)
    {
        if (removed.Count == 0)
        {
            return null;
        }

        var drop = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in removed)
        {
            drop.Add(NormalizeFolderPath(path));
        }

        var visible = new List<string>();
        CollectVisibleExplorerPaths(_folderTree.Items, visible);
        var top = -1;
        for (var i = 0; i < visible.Count; i++)
        {
            if (drop.Contains(visible[i]))
            {
                top = i;
                break;
            }
        }

        if (top <= 0)
        {
            return null;
        }

        for (var i = top - 1; i >= 0; i--)
        {
            if (!drop.Contains(visible[i]))
            {
                return visible[i];
            }
        }

        return null;
    }

    private static void CollectVisibleExplorerPaths(ItemCollection items, List<string> dest)
    {
        foreach (var raw in items)
        {
            if (raw is not TreeViewItem item || item.Tag is not string path)
            {
                continue;
            }

            dest.Add(NormalizeFolderPath(path));
            if (item.IsExpanded && item.Items.Count > 0)
            {
                CollectVisibleExplorerPaths(item.Items, dest);
            }
        }
    }

    public void FocusExplorer()
    {
        CancelPlaylistFocusRestore();
        // 名前変更・フォルダ削除・F5 はツリーに残す。
        // 遅延の RequestListFocus でプレイリストへ吸い戻されないようにする。
        ReleasePlaylistFocusHold();
        _listFocusTicket++;
        _folderTree.UpdateLayout();
        if (_folderTree.SelectedItem is TreeViewItem selected)
        {
            selected.BringIntoView();
            if (selected.Focus())
            {
                return;
            }
        }

        if (_folderTree.Items.OfType<TreeViewItem>().FirstOrDefault() is { } first)
        {
            first.IsSelected = true;
            first.BringIntoView();
            if (first.Focus())
            {
                return;
            }
        }

        _folderTree.Focus();
    }

    private void ArmPlaylistFocusHold()
    {
        _holdPlaylistFocus = true;
        _playlistFocusReclaimCount = 0;
        _playlistFocusHoldTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _playlistFocusHoldTimer.Stop();
        _playlistFocusHoldTimer.Tick -= PlaylistFocusHoldTimer_Tick;
        _playlistFocusHoldTimer.Tick += PlaylistFocusHoldTimer_Tick;
        _playlistFocusHoldTimer.Start();
    }

    private void PlaylistFocusHoldTimer_Tick(object? sender, EventArgs e)
    {
        ReleasePlaylistFocusHold();
    }

    private void ReleasePlaylistFocusHold()
    {
        _holdPlaylistFocus = false;
        _playlistFocusHoldTimer?.Stop();
    }

    private void Grid_IsKeyboardFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        SyncPaneFocusChrome();
        if (_holdPlaylistFocus && e.NewValue is false)
        {
            QueuePlaylistFocusReclaim();
        }
    }

    /// <summary>
    /// フォーカス変更の途中では呼ばない。戻す予約は一つに重ねない。
    /// </summary>
    private void QueuePlaylistFocusReclaim()
    {
        if (!_holdPlaylistFocus || _playlistFocusReclaimQueued)
        {
            return;
        }

        _playlistFocusReclaimQueued = true;
        Dispatcher.BeginInvoke(
            () =>
            {
                _playlistFocusReclaimQueued = false;
                TryReclaimPlaylistFocus();
            },
            DispatcherPriority.Input);
    }

    private void TryReclaimPlaylistFocus()
    {
        if (_playlistFocusReclaiming || !_holdPlaylistFocus || IsPlaylistGridFocused)
        {
            return;
        }

        if (_playlistFocusReclaimCount >= PlaylistFocusReclaimLimit)
        {
            return;
        }

        _playlistFocusReclaimCount++;
        _playlistFocusReclaiming = true;
        try
        {
            FocusList();
        }
        finally
        {
            _playlistFocusReclaiming = false;
        }
    }

    /// <summary>切り取り・貼り付けのあと、操作を始めたペインへフォーカスを戻す。</summary>
    public void FocusPane(LibraryPane pane)
    {
        switch (pane)
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

    internal string ExplorerTypeaheadQuery => _explorerTypeahead;

    /// <summary>黄背景／↑↓が有効なセッション中か（トースト用バッファが空でも true）。</summary>
    internal bool IsExplorerTypeaheadActive => _explorerTypeaheadWalk.Length > 0;

    /// <summary>
    /// 打ち込みセッションを終わらせる（Esc・プレイリスト登録／再生・他 UI へのフォーカス）。
    /// 黄背景・渡り歩き・トーストをすべて消す。入力アイドルでのトースト消しとは別。
    /// </summary>
    public bool ClearExplorerTypeahead(bool hideToastInstant = true)
    {
        StopExplorerTypeaheadToastIdleTimer();
        ClearExplorerTypeaheadHighlights();
        _explorerTypeaheadIgnoreText = "";
        var had = _explorerTypeahead.Length > 0 || _explorerTypeaheadWalk.Length > 0;
        _explorerTypeahead = "";
        _explorerTypeaheadWalk = "";
        HideExplorerTypeaheadToast(hideToastInstant);
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
            HideExplorerTypeaheadToast(instant: true);
            return;
        }

        _explorerTypeaheadToastText.Text = _explorerTypeahead;
        LibraryPlayerMode.FadeElementOpacity(
            _explorerTypeaheadToast,
            to: 1,
            instant: true,
            hitTestVisible: false);
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
        HideExplorerTypeaheadToast(instant: false);
    }

    private void StopExplorerTypeaheadToastIdleTimer()
    {
        _explorerTypeaheadToastIdleTimer?.Stop();
    }

    private void HideExplorerTypeaheadToast(bool instant)
    {
        StopExplorerTypeaheadToastIdleTimer();
        LibraryPlayerMode.FadeElementOpacity(
            _explorerTypeaheadToast,
            to: 0,
            instant,
            hitTestVisible: false,
            seconds: instant ? null : LibraryPlayerMode.ChromeFadeSeconds);
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

    public void FocusFavorites()
    {
        ClearExplorerTypeahead();
        CancelPlaylistFocusRestore();
        // 遅延の RequestListFocus でプレイリストへ吸い戻されないようにする。
        ReleasePlaylistFocusHold();
        _listFocusTicket++;
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
            foreach (var menu in new[]
                     {
                         _folderTree.ContextMenu,
                         _favoritesList.ContextMenu,
                         _grid.ContextMenu,
                         _columnHeaderMenu,
                     })
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

        if (_columnHeaderMenu is { } columns)
        {
            columns.IsOpen = false;
        }
    }

    internal ContextMenu? ColumnHeaderContextMenuForTests => _columnHeaderMenu;

    /// <summary>選択中の実ファイル／フォルダをクリップボードへコピー（移動ではない）。</summary>
    public void CopySelected()
    {
        if (ActivePane == LibraryPane.Favorites)
        {
            return;
        }

        CopyPaths(SelectedCopyPaths());
    }

    /// <summary>選択中の場所をエクスプローラーで開く。</summary>
    public void ShowSelectedInExplorer() =>
        LibraryShellFiles.TryOpenInExplorer(SelectedRevealPaths());

    internal string[] SelectedCopyPaths() => CopyPathsFor(ActivePane);

    internal string[] SelectedRevealPaths() => RevealPathsFor(ActivePane);

    private void CopyPaths(string[] paths)
    {
        if (paths.Length == 0)
        {
            return;
        }

        ClearCutGhost();
        var origin = ActivePane == LibraryPane.Explorer
            ? LibraryShellFiles.OriginTree
            : LibraryShellFiles.OriginPlaylist;
        SystemClipboard.TrySetLibraryTransfer(paths, move: false, origin, Window.GetWindow(this));
        ShowTransferToast("Copy", paths);
    }

    public void CutSelected() => CutPaths(ActivePane);

    private void CutPaths(LibraryPane pane)
    {
        if (pane == LibraryPane.Favorites)
        {
            return;
        }

        var paths = CopyPathsFor(pane);
        if (paths.Length == 0)
        {
            return;
        }
        var origin = pane == LibraryPane.Explorer
            ? LibraryShellFiles.OriginTree
            : LibraryShellFiles.OriginPlaylist;
        MarkCutGhost(paths);
        FocusPane(pane);
        SystemClipboard.TrySetLibraryTransfer(
            paths,
            move: true,
            origin,
            Window.GetWindow(this),
            () => FocusPane(pane));
        ShowTransferToast("Cut", paths);
    }

    /// <summary>10件までは表示名を1行ずつ、11件以上は件数だけ。</summary>
    public void ShowTransferToast(string action, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        var detail = paths.Count <= 10
            ? string.Join(Environment.NewLine, paths.Select(TransferDisplayName))
            : $"{paths.Count} items";
        _transferToastText.Text = paths.Count is > 1 and <= 10
            ? action + Environment.NewLine + detail
            : $"{action}: {detail}";
        _transferToast.Visibility = Visibility.Visible;
        AlignTransferToastToWindowCenter();
        LibraryPlayerMode.FadeElementOpacity(_transferToast, to: 1, instant: true, hitTestVisible: false);
        Dispatcher.BeginInvoke(AlignTransferToastToWindowCenter, DispatcherPriority.Loaded);
        _transferToastTimer ??= new DispatcherTimer { Interval = LibraryExplorerTypeahead.ToastIdleClear };
        _transferToastTimer.Stop();
        _transferToastTimer.Tick -= OnTransferToastTick;
        _transferToastTimer.Tick += OnTransferToastTick;
        _transferToastTimer.Start();
    }

    private void OnTransferToastTick(object? sender, EventArgs e)
    {
        _transferToastTimer?.Stop();
        LibraryPlayerMode.FadeElementOpacity(
            _transferToast,
            to: 0,
            instant: false,
            hitTestVisible: false,
            seconds: LibraryPlayerMode.ChromeFadeSeconds);
    }

    private void BuildTransferToast()
    {
        _transferToastText.FontFamily = new FontFamily("Consolas");
        _transferToastText.FontSize = DesignMetrics.From96(22);
        _transferToastText.FontWeight = FontWeights.Normal;
        _transferToastText.TextWrapping = TextWrapping.NoWrap;
        _transferToastText.TextAlignment = TextAlignment.Center;
        _transferToastText.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryForeBrush");
        _transferToastPlate.HorizontalAlignment = HorizontalAlignment.Center;
        _transferToastPlate.VerticalAlignment = VerticalAlignment.Center;
        _transferToastPlate.Margin = new Thickness(16);
        _transferToastPlate.Source = _root;
        _transferToastPlate.CornerRadius = DesignMetrics.ControlCorner;
        _transferToastPlate.Children.Add(new Border
        {
            Padding = DesignMetrics.PlaylistVideoTimecodePadding,
            Background = Brushes.Transparent,
            Child = _transferToastText,
        });
        _transferToast.Children.Add(_transferToastPlate);
        SizeChanged += (_, _) =>
        {
            if (_transferToast.Visibility == Visibility.Visible)
            {
                AlignTransferToastToWindowCenter();
            }
        };
        Grid.SetColumnSpan(_transferToast, _root.ColumnDefinitions.Count);
        Panel.SetZIndex(_transferToast, 40);
        _root.Children.Add(_transferToast);
    }

    /// <summary>
    /// トーストの親はライブラリ領域なので、その中央はウィンドウ中央より上になる。
    /// ウィンドウ内容の縦中央へずらす。
    /// </summary>
    private void AlignTransferToastToWindowCenter()
    {
        var window = Window.GetWindow(this);
        if (window?.Content is not FrameworkElement root
            || _transferToast.ActualHeight <= 0
            || root.ActualHeight <= 0)
        {
            _transferToastPlate.RenderTransform = Transform.Identity;
            return;
        }

        var origin = _transferToast.TranslatePoint(new Point(0, 0), root);
        var dy = root.ActualHeight / 2 - (origin.Y + _transferToast.ActualHeight / 2);
        _transferToastPlate.RenderTransform = Math.Abs(dy) < 0.5
            ? Transform.Identity
            : new TranslateTransform(0, dy);
    }

    private static string TransferDisplayName(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }

    /// <summary>切り取り中のパスを暗くする。前の切り取り表示は消す。</summary>
    public void MarkCutGhost(IReadOnlyList<string> paths)
    {
        _cutPaths.Clear();
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            try
            {
                _cutPaths.Add(LibraryFileTransfer.NormalizePrefix(path));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
            }
        }

        RefreshCutChrome();
    }

    /// <summary>切り取りの暗転を消す。表示が変わったら true。</summary>
    public bool ClearCutGhost()
    {
        if (_cutPaths.Count == 0)
        {
            return false;
        }

        _cutPaths.Clear();
        RefreshCutChrome();
        return true;
    }

    private bool IsCutPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || _cutPaths.Count == 0)
        {
            return false;
        }

        try
        {
            return _cutPaths.Contains(LibraryFileTransfer.NormalizePrefix(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private void RefreshCutChrome()
    {
        ApplyCutChromeToTree(_folderTree.Items);
        foreach (var row in _rows)
        {
            row.CutPending = row.Tag is DocumentSession session && IsCutPath(session.Document.SourcePath);
        }
    }

    private void ApplyCutChromeToTree(ItemCollection items)
    {
        foreach (var raw in items)
        {
            if (raw is not TreeViewItem item)
            {
                continue;
            }

            if (item.Tag is string path)
            {
                ApplyFolderHeader(item, FolderHeaderText(item), LibraryExplorerPaths.IsMissing(path));
            }

            if (item.Items.Count > 0)
            {
                ApplyCutChromeToTree(item.Items);
            }
        }
    }

    public void RemapFilesystemPaths(string oldPrefix, string newPrefix, bool refreshTree, string? selectFolder)
    {
        var favorites = FavoritePaths;
        var nextFavorites = new string[favorites.Length];
        var favoritesChanged = false;
        for (var i = 0; i < favorites.Length; i++)
        {
            if (LibraryFileTransfer.TryMapPath(favorites[i], oldPrefix, newPrefix, out var mapped)
                && !string.Equals(mapped, favorites[i], StringComparison.Ordinal))
            {
                nextFavorites[i] = mapped;
                favoritesChanged = true;
            }
            else
            {
                nextFavorites[i] = favorites[i];
            }
        }

        if (favoritesChanged)
        {
            SetFavorites(nextFavorites);
            FavoritesChanged?.Invoke(this, FavoritePaths);
        }

        var expanded = _explorerExpanded.ToArray();
        _explorerExpanded.Clear();
        foreach (var path in expanded)
        {
            _explorerExpanded.Add(
                LibraryFileTransfer.TryMapPath(path, oldPrefix, newPrefix, out var mapped)
                    ? NormalizeFolderPath(mapped)
                    : path);
        }

        var rootsChanged = false;
        var nextRoots = new string[_explorerRoots.Length];
        for (var i = 0; i < _explorerRoots.Length; i++)
        {
            if (LibraryFileTransfer.TryMapPath(_explorerRoots[i], oldPrefix, newPrefix, out var mapped)
                && !string.Equals(mapped, _explorerRoots[i], StringComparison.Ordinal))
            {
                nextRoots[i] = mapped;
                rootsChanged = true;
            }
            else
            {
                nextRoots[i] = _explorerRoots[i];
            }
        }

        if (rootsChanged)
        {
            SetExplorerRoots(nextRoots);
            ExplorerRootsChanged?.Invoke(this, _explorerRoots);
        }
        else if (refreshTree)
        {
            RefreshExplorer();
        }

        if (!string.IsNullOrWhiteSpace(selectFolder))
        {
            RevealFolder(selectFolder);
        }
    }

    public void FocusExplorerFolder(string path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            RevealFolder(path);
        }
    }

    private string[] CopyPathsFor(LibraryPane pane) =>
        pane switch
        {
            LibraryPane.Explorer => SelectedExplorerTransferPaths(),
            LibraryPane.Favorites => LibraryShellFiles.ExistingPaths(SelectedFavoritePaths),
            _ => SelectedPlaylistCopyPaths(),
        };

    private string[] RevealPathsFor(LibraryPane pane) =>
        pane switch
        {
            LibraryPane.Explorer => LibraryShellFiles.ExistingPaths(SelectedExplorerFolders),
            LibraryPane.Favorites => LibraryShellFiles.ExistingPaths(SelectedFavoritePaths),
            _ => SelectedPlaylistCopyPaths(),
        };

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

    public void MoveSelection(int delta, bool extend = false, bool wrap = true)
    {
        if (delta == 0)
        {
            return;
        }

        var count = CountPlaylistFiles();
        if (count <= 0)
        {
            return;
        }

        var index = PlaylistFileIndexOfSelected();
        if (index < 0)
        {
            index = delta > 0 ? 0 : count - 1;
        }
        else if (extend)
        {
            // Shift 範囲選択は端で止める（反対側へ回ると範囲が崩れる）。
            index = Math.Clamp(index + delta, 0, count - 1);
        }
        else
        {
            // 単発は先頭の↑→末尾、末尾の↓→先頭。長押し連続は wrap=false で端に一旦止まる。
            index = LibraryPlayerMode.StepLoopIndex(count, index, delta, wrap);
        }

        if (index < 0)
        {
            return;
        }

        ApplyMoveSelectionFile(index, extend);
    }

    public void MoveSelectionToEdge(int edge, bool extend = false)
    {
        var index = LibraryPlayerMode.EdgeIndex(CountPlaylistFiles(), edge);
        if (index < 0)
        {
            return;
        }

        ApplyMoveSelectionFile(index, extend);
    }

    public void MoveSelectionPage(int direction, bool extend = false, bool wrap = true)
    {
        if (direction == 0)
        {
            return;
        }

        var step = LibraryPlayerMode.PageStep(VisibleRowCount());
        MoveSelection(direction < 0 ? -step : step, extend, wrap);
    }

    private int CountPlaylistFiles()
    {
        var count = 0;
        foreach (var row in PlaylistRows())
        {
            if (row.Tag is DocumentSession)
            {
                count++;
            }
        }

        return count;
    }

    private int PlaylistFileIndexOfSelected()
    {
        var selected = SelectedSession;
        if (selected is null)
        {
            return -1;
        }

        var index = 0;
        foreach (var row in PlaylistRows())
        {
            if (row.Tag is not DocumentSession session)
            {
                continue;
            }

            if (ReferenceEquals(session, selected))
            {
                return index;
            }

            index++;
        }

        return -1;
    }

    private LibraryFileRow? PlaylistFileAt(int index)
    {
        var i = 0;
        foreach (var row in PlaylistRows())
        {
            if (row.Tag is not DocumentSession)
            {
                continue;
            }

            if (i == index)
            {
                return row;
            }

            i++;
        }

        return null;
    }

    private void ApplyMoveSelectionFile(int index, bool extend)
    {
        var row = PlaylistFileAt(index);
        if (row is null)
        {
            return;
        }

        _deferActivate = true;
        try
        {
            if (!extend)
            {
                _anchorIndex = index;
                _grid.SelectedItem = row;
            }
            else
            {
                SelectPlaylistFileRange(_anchorIndex, index);
            }

            // キー移動は SelectedItem だけ変えがちで、CurrentItem が先頭のまま残る。
            // 行波形の遅延描画でレイアウトが走ると、古い CurrentItem を手がかりに
            // シアン帯が1行目へ寄って見えることがある。
            PinSelectionCurrent();
        }
        finally
        {
            _deferActivate = false;
        }

        EnsureListFocused();
        EnsureRowVisible(row);
    }

    private void SelectPlaylistFileRange(int from, int to)
    {
        var count = CountPlaylistFiles();
        if (count <= 0)
        {
            return;
        }

        from = Math.Clamp(from, 0, count - 1);
        to = Math.Clamp(to, 0, count - 1);
        var lo = Math.Min(from, to);
        var hi = Math.Max(from, to);
        _syncing = true;
        SuspendPlaylistSelectionNotify();
        try
        {
            _grid.SelectedItems.Clear();
            var i = 0;
            LibraryFileRow? current = null;
            foreach (var row in PlaylistRows())
            {
                if (row.Tag is not DocumentSession)
                {
                    continue;
                }

                if (i >= lo && i <= hi)
                {
                    _grid.SelectedItems.Add(row);
                    if (i == to)
                    {
                        current = row;
                    }
                }

                i++;
            }

            if (current is not null)
            {
                _grid.SelectedItem = current;
                i = 0;
                foreach (var row in PlaylistRows())
                {
                    if (row.Tag is not DocumentSession)
                    {
                        continue;
                    }

                    if (i >= lo && i <= hi && !_grid.SelectedItems.Contains(row))
                    {
                        _grid.SelectedItems.Add(row);
                    }

                    i++;
                }
            }

            PinSelectionCurrent();
        }
        finally
        {
            _syncing = false;
            ResumePlaylistSelectionNotify();
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
        // フォーカス済みでも CurrentItem は選択行へ揃える。
        // IsKeyboardFocusWithin の早期 return だと、矢印キー移動後に CurrentItem が
        // 先頭のまま残り、行波形描画中のレイアウトでシアンが1行目へ寄る。
        PinSelectionCurrent();

        if (_grid.IsKeyboardFocusWithin)
        {
            return;
        }

        // 未選択でも先頭を選ばない（シアン＝IsSelected が1行目へ固定されるのを防ぐ）。

        if (_grid.SelectedItem is { } item)
        {
            if (_grid.ItemContainerGenerator.ContainerFromItem(item) is UIElement row
                && row.Focus())
            {
                return;
            }
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
        if (_playlistDragBusy)
        {
            return;
        }

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
                var top = RowsViewportTop();
                if (bounds.Top >= top && bounds.Bottom <= _grid.ActualHeight)
                {
                    // ScrollIntoView は行だけをビューへ寄せ、先頭グループ名を列見出しの裏へ隠すことがある。
                    RevealGroupHeaderIfNearlyClipped(item);
                    return;
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        _grid.ScrollIntoView(item);
        var generation = _syncGeneration;
        Dispatcher.BeginInvoke(
            () =>
            {
                if (generation == _syncGeneration)
                {
                    RevealGroupHeaderIfNearlyClipped(item);
                }
            },
            DispatcherPriority.Loaded);
    }

    /// <summary>
    /// グループ先頭行を <see cref="DataGrid.ScrollIntoView"/> したとき、
    /// <see cref="ScrollUnit.Item"/> だと VerticalOffset=1 になり、グループ名だけが
    /// 列見出し（ZIndex 付き）の裏へ隠れる。深い位置の選択では動かさない。
    /// </summary>
    private void RevealGroupHeaderIfNearlyClipped(object item)
    {
        if (_group == LibraryFileGroup.None
            || FindDataGridScrollViewer() is not { } scroll
            || _grid.ItemContainerGenerator.ContainerFromItem(item) is not DependencyObject row)
        {
            return;
        }

        var groupItem = FindAncestor<GroupItem>(row);
        if (groupItem is null || FindDescendant<LibraryGroupHeader>(groupItem) is not { } header)
        {
            return;
        }

        try
        {
            var viewportTop = RowsViewportTop();
            var headerTop = header.TransformToAncestor(_grid).Transform(new Point(0, 0)).Y;
            var clipped = viewportTop - headerTop;
            if (clipped <= 0.5)
            {
                return;
            }

            // グループ名の高さ＋余白ぶんだけ隠れているときだけ戻す（途中スクロールは触らない）。
            var slack = Math.Max(header.ActualHeight, 1) + header.Margin.Top + header.Margin.Bottom + 2;
            if (clipped > slack)
            {
                return;
            }

            // CanContentScroll（ScrollUnit.Item）のオフセットは項目単位。ピクセル減算はしない。
            var next = scroll.CanContentScroll
                ? Math.Max(0, scroll.VerticalOffset - 1)
                : Math.Max(0, scroll.VerticalOffset - clipped);
            if (Math.Abs(scroll.VerticalOffset - next) > 0.01)
            {
                scroll.ScrollToVerticalOffset(next);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void SelectAllRows()
    {
        if (_grid.Items.Count == 0)
        {
            return;
        }

        _syncing = true;
        SuspendPlaylistSelectionNotify();
        try
        {
            _grid.SelectAll();
        }
        finally
        {
            _syncing = false;
            ResumePlaylistSelectionNotify();
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
                                                                                                        _explorerLabel.Text = UiStrings.LibraryExplorerLabel;
        _favoritesLabel.Text = UiStrings.LibraryFavoritesLabel;
        _playlistLabel.Text = UiStrings.LibraryPlaylistLabel;
        _shuffleCheck.Content = UiStrings.LabelLibraryShuffle;
        _autoPlayCheck.Content = UiStrings.LabelLibraryAutoPlay;
        _autoPlayNextCheck.Content = UiStrings.LabelLibraryAutoPlayNext;
        _explorerSearchHint.Text = UiStrings.LibrarySearchHint;
        _playlistSearchHint.Text = UiStrings.LibrarySearchHint;
        RebuildExplorerContextMenu();
        RebuildFavoritesContextMenu();
        RebuildListContextMenu();
        RebuildColumnHeaderContextMenu();
        RelabelExplorerRoots();
        FitColumns();
        SyncGroupChrome();
    }

    public void RefreshAppearance()
    {
        _fallbackWashTurns = null;
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
        PlayerChrome.Get("PlayerGlowVeilBrush", theme).A / 255d;

    public void SetSessions(
        IReadOnlyList<DocumentSession> sessions,
        DocumentSession? active,
        IReadOnlyList<DocumentSession>? selected = null,
        bool rebuild = false)
    {
        var preserve = selected ?? SelectedSessions;
        var rows = new LibraryFileRow[sessions.Count];
        for (var i = 0; i < sessions.Count; i++)
        {
            rows[i] = CreateRowWithDefaults(sessions[i]);
        }

        BeginColumnOrderCommitGate();
        try
        {
            if (sessions.Count == 0)
            {
                PrepareEmptyPlaylistColumnFade();
            }

            var sameSessions = SameSessionSet(_rows, sessions);
            _rows = SortRows(rows);
            LibraryFileList.ApplyGroupKeys(_rows, _group);
            if (!sameSessions)
            {
                _shuffle.Clear();
            }
            BeginRowSync();
            try
            {
                if (!rebuild && TryPatchBoundRows(_rows))
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

            EnforceFadingColumnSnapshotIfEmpty();
            RefreshEffectiveColumns();
        }
        finally
        {
            EndColumnOrderCommitGate();
        }

        InvalidateGroupJackets();
        if (rebuild)
        {
            SyncPlaylistWaveformsFromDocuments(sessions);
        }

        _ = EnsureGroupArtworkAsync();
        if (!_columnChromeFadingOut)
        {
            RefreshPlaylistWaveformSize(reschedule: true);
            SchedulePlaylistLoudness();
        }
    }

    public void UpdateSessionRow(DocumentSession session)
    {
        var next = CreateRowWithDefaults(session);
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

            RefreshEffectiveColumns();
            InvalidateGroupJackets();
            return;
        }
    }

    /// <summary>
    /// フォルダ読み込み用。1 件追加して現在の並べ替え位置へ挿入する。全件 SetSessions より軽い。
    /// </summary>
    public void AppendSession(DocumentSession session, bool select) =>
        AppendSessionCore(session, select, refreshColumns: true);

    /// <summary>
    /// 複数曲を一度に挿入する。列があるときはその位置、無いときは末尾。列再計算は最後の1回だけ。
    /// </summary>
    public void AppendSessions(IReadOnlyList<DocumentSession> sessions, DocumentSession? select)
    {
        if (sessions.Count == 0)
        {
            return;
        }

        for (var i = 0; i < sessions.Count; i++)
        {
            var session = sessions[i];
            var pick = select is not null && ReferenceEquals(select, session);
            AppendSessionCore(session, pick, refreshColumns: i + 1 == sessions.Count);
        }
    }

    /// <summary>
    /// 現行の列ソート位置へ1件入れる。ソート見出しが無いときは末尾。入れた行の番号を返す。
    /// </summary>
    public int InsertSessionByCurrentSort(DocumentSession session, bool select) =>
        AppendSessionCore(session, select, refreshColumns: false);

    /// <summary>表示から外す。内部の曲リストは触らない。</summary>
    public void RemovePlaylistSession(DocumentSession session)
    {
        var rowIndex = -1;
        for (var i = 0; i < _rows.Count; i++)
        {
            if (ReferenceEquals(_rows[i].Tag, session))
            {
                rowIndex = i;
                break;
            }
        }

        if (rowIndex < 0)
        {
            return;
        }

        var nextRows = new LibraryFileRow[_rows.Count - 1];
        for (var i = 0; i < rowIndex; i++)
        {
            nextRows[i] = _rows[i];
        }

        for (var i = rowIndex + 1; i < _rows.Count; i++)
        {
            nextRows[i - 1] = _rows[i];
        }

        BeginRowSync();
        try
        {
            _rows = nextRows;
            for (var i = _items.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_items[i].Tag, session))
                {
                    _items.RemoveAt(i);
                }
            }
        }
        finally
        {
            EndRowSync();
        }
    }

    /// <summary>
    /// すでに載っている曲を、現行ソートの位置へ入れ直す。見出しが無いときは末尾。
    /// 返る番号は、曲を全部外したあとに入れた順の行番号。
    /// </summary>
    public int[] RepositionByCurrentSort(IReadOnlyList<DocumentSession> sessions)
    {
        if (sessions.Count == 0)
        {
            return [];
        }

        var selected = SelectedSessions;
        var active = SelectedSession;
        foreach (var session in sessions)
        {
            RemovePlaylistSession(session);
        }

        var indices = new int[sessions.Count];
        for (var i = 0; i < sessions.Count; i++)
        {
            indices[i] = AppendSessionCore(sessions[i], select: false, refreshColumns: false);
        }

        if (selected.Length > 0)
        {
            var keep = active is not null && selected.Contains(active) ? active : selected[0];
            ApplyRowSelectionCore(keep, selected);
        }

        return indices;
    }

    private int AppendSessionCore(DocumentSession session, bool select, bool refreshColumns)
    {
        var row = CreateRowWithDefaults(session);
        row.GroupKey = LibraryFileList.GroupLabel(row, _group);

        // 非選択追加では DataGrid の自動選択→SessionActivated を Loaded まで抑止する。
        var heldActivate = _deferActivate;
        if (!select)
        {
            _deferActivate = true;
        }

        try
        {
            if (_grid.ItemsSource is null || _rows.Count == 0)
            {
                BeginColumnOrderCommitGate();
                try
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

                    if (refreshColumns)
                    {
                        RefreshEffectiveColumns();
                    }
                }
                finally
                {
                    EndColumnOrderCommitGate();
                }

                return 0;
            }

            var insertAt = _items.Count;
            if (_sortColumn is { } sortColumn)
            {
                insertAt = 0;
                while (insertAt < _items.Count
                    && LibraryFileList.Compare(_items[insertAt], row, sortColumn, _sortDirection) <= 0)
                {
                    insertAt++;
                }
            }

            var rowIndex = _rows.Count;
            if (_sortColumn is { } sortForRows)
            {
                rowIndex = 0;
                while (rowIndex < _rows.Count
                    && LibraryFileList.Compare(_rows[rowIndex], row, sortForRows, _sortDirection) <= 0)
                {
                    rowIndex++;
                }
            }

            BeginColumnOrderCommitGate();
            try
            {
                BeginRowSync();
                try
                {
                    var nextRows = new LibraryFileRow[_rows.Count + 1];
                    for (var i = 0; i < rowIndex; i++)
                    {
                        nextRows[i] = _rows[i];
                    }

                    nextRows[rowIndex] = row;
                    for (var i = rowIndex; i < _rows.Count; i++)
                    {
                        nextRows[i + 1] = _rows[i];
                    }

                    _rows = nextRows;
                    if (PlaylistRowMatches(row))
                    {
                        _items.Insert(insertAt, row);
                        if (select)
                        {
                            ApplyRowSelectionCore(session, [session]);
                        }
                    }
                }
                finally
                {
                    EndRowSync();
                }

                if (refreshColumns)
                {
                    RefreshEffectiveColumns();
                }
            }
            finally
            {
                EndColumnOrderCommitGate();
            }

            return rowIndex;
        }
        finally
        {
            if (!select)
            {
                // EndRowSync の _syncing 解除（Loaded）より後で戻し、自動選択の窓を閉じる。
                var gen = _syncGeneration;
                Dispatcher.BeginInvoke(
                    () =>
                    {
                        if (gen == _syncGeneration)
                        {
                            _deferActivate = heldActivate;
                        }
                    },
                    DispatcherPriority.ContextIdle);
            }
        }
    }

    public void FinishIncrementalSessionLoad()
    {
        RefreshEffectiveColumns();
        InvalidateGroupJackets();
        _ = EnsureGroupArtworkAsync();
        RefreshPlaylistWaveformSize(reschedule: true);
        SchedulePlaylistLoudness();
    }

    public void SetArtwork(AudioDocument? document, bool keepCurrentIfEmpty = false) =>
        ApplyArtwork(
            document?.Artwork,
            document is { } d && AllowsJacketReplace(d.SourceKind),
            keepCurrentIfEmpty);

    /// <summary>
    /// デコードとウォッシュ生成（重い）。UI スレッド以外で呼ぶ。結果はすべて Freeze 済み。
    /// </summary>
    internal static PreparedArtworkPresentation PrepareArtworkPresentation(byte[]? bytes)
    {
        var next = bytes is { Length: > 0 } ? bytes : null;
        if (next is null)
        {
            return new PreparedArtworkPresentation(null, null, CreateFallbackWashTurns(), decodedOk: false);
        }

        var decoded = TryCreateBitmap(next, ArtworkDecodeMaxEdge);
        if (decoded is null)
        {
            return new PreparedArtworkPresentation(next, null, CreateFallbackWashTurns(), decodedOk: false);
        }

        return new PreparedArtworkPresentation(
            next,
            decoded,
            CreateAmbientWashTurns(decoded),
            decodedOk: true);
    }

    /// <summary>裏で作ったウォッシュを載せるだけ。デコードしない。</summary>
    internal void ApplyPreparedArtwork(
        PreparedArtworkPresentation prepared,
        bool allowReplace,
        bool keepCurrentIfEmpty = false)
    {
        JacketReplaceEnabled = allowReplace;
        var next = prepared.Bytes;
        if (ArtworkEquals(_artworkBytes, next) && _jacketBitmap is not null)
        {
            return;
        }

        if (next is null && keepCurrentIfEmpty && _artworkBytes is not null)
        {
            return;
        }

        if (next is not null && !prepared.DecodedOk)
        {
            // 次の画像が読めないときは、今の実ジャケットを外してフォールバックへ落とさない。
            if (_artworkBytes is not null)
            {
                return;
            }
        }

        if (next is not null && prepared.DecodedOk && prepared.Bitmap is not null)
        {
            _artworkBytes = next;
            _jacketBitmap = prepared.Bitmap;
            ApplyArtworkGlow(prepared.Bitmap, prepared.Turns);
            return;
        }

        _artworkBytes = next;
        _jacketBitmap = null;
        ApplyArtworkGlow(null, prepared.Turns);
    }

    private void ApplyArtwork(byte[]? bytes, bool allowReplace, bool keepCurrentIfEmpty = false)
    {
        ApplyPreparedArtwork(PrepareArtworkPresentation(bytes), allowReplace, keepCurrentIfEmpty);
    }

    private void ShowJacketDisplay(BitmapSource? decoded, bool realArt)
    {
        var bitmap = decoded;
        _jacketBitmap = bitmap;
        ApplyArtworkGlow(realArt && bitmap is not null ? bitmap : null);
    }

    internal readonly struct PreparedArtworkPresentation
    {
        public PreparedArtworkPresentation(
            byte[]? bytes,
            BitmapSource? bitmap,
            Brush[] turns,
            bool decodedOk)
        {
            Bytes = bytes;
            Bitmap = bitmap;
            Turns = turns;
            DecodedOk = decodedOk;
        }

        public byte[]? Bytes { get; }

        public BitmapSource? Bitmap { get; }

        public Brush[] Turns { get; }

        public bool DecodedOk { get; }
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
        DetachBoundRows();
        _items.Clear();
        foreach (var row in _rows)
        {
            if (PlaylistRowMatches(row))
            {
                _items.Add(row);
            }
        }

        var view = new ListCollectionView(_items);
        if (_group != LibraryFileGroup.None)
        {
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LibraryFileRow.GroupKey)));
        }

        // 新規 ListCollectionView は CurrentItem が先頭を指す。選択適用前に外して、
        // WPF が先頭行へ IsSelected を寄せる足がかりを与えない。
        view.MoveCurrentToPosition(-1);
        _grid.ItemsSource = view;
        ApplyRowSelectionCore(active, selected);
        if (_grid.SelectedItem is null && _items.Count > 0 && _playlistSearchGroups.Count > 0)
        {
            if (_items[0].Tag is DocumentSession first)
            {
                ApplyRowSelectionCore(first, [first]);
            }
        }

        RefreshSortChrome();
    }

    /// <summary>
    /// DataGrid のグループ化 CollectionView を切る。ItemsSource を差し替えるだけでは
    /// グループと行コンテナが残り、フォルダを渡り歩くほど操作が重くなる。
    /// </summary>
    private void DetachBoundRows()
    {
        if (_grid.ItemsSource is ListCollectionView view)
        {
            _grid.ItemsSource = null;
            view.GroupDescriptions.Clear();
            view.DetachFromSourceCollection();
            return;
        }

        _grid.ItemsSource = null;
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
        SuspendPlaylistSelectionNotify();
        try
        {
            ApplyRowSelectionCoreCore(active, selected);
        }
        finally
        {
            ResumePlaylistSelectionNotify();
        }
    }

    private void ApplyRowSelectionCoreCore(DocumentSession? active, IReadOnlyList<DocumentSession>? selected)
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
            PinSelectionCurrent();
            EnsureRowVisible(current);
        }
        else
        {
            _anchorIndex = -1;
            PinSelectionCurrent();
        }
    }

    /// <summary>
    /// DataGrid / CollectionView の CurrentItem を選択行へ揃える。
    /// 未選択なら外す。再生追従や強制復帰はしない。
    /// </summary>
    private void PinSelectionCurrent()
    {
        if (_grid.SelectedItem is { } item)
        {
            if (!ReferenceEquals(_grid.CurrentItem, item))
            {
                _grid.CurrentItem = item;
            }

            PinCollectionViewCurrent(item);
            return;
        }

        PinCollectionViewCurrent(null);
    }

    /// <summary>
    /// グループ化 <see cref="ListCollectionView"/> の CurrentItem は既定で先頭（行1）を指す。
    /// 行の差し替え（UpdateSessionRow の Replace）やレイアウト更新のたびに WPF が
    /// この CurrentItem を手がかりに IsSelected を先頭へ寄せ、シアンが1行目へ飛ぶ。
    /// 選択中の行へ CurrentItem を固定し、未選択なら外す。再生追従や強制復帰はしない。
    /// </summary>
    private void PinCollectionViewCurrent(object? current)
    {
        if (_grid.ItemsSource is not ICollectionView view)
        {
            return;
        }

        if (current is null)
        {
            if (view.CurrentItem is not null || view.CurrentPosition >= 0)
            {
                view.MoveCurrentToPosition(-1);
            }

            return;
        }

        if (!ReferenceEquals(view.CurrentItem, current))
        {
            view.MoveCurrentTo(current);
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
        SuspendPlaylistSelectionNotify();
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

            PinSelectionCurrent();
        }
        finally
        {
            _syncing = false;
            ResumePlaylistSelectionNotify();
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

    private void BuildLayout()
    {
        var treeWidth = DesignMetrics.LibraryExplorerWidth;
        _sidePanesRememberedWidth = treeWidth;
        _treeColumn.Width = new GridLength(treeWidth);
        _treeColumn.MinWidth = DesignMetrics.LibraryExplorerMinWidth;
        _treeColumn.MaxWidth = DesignMetrics.LibraryExplorerMaxWidth;
        _treeSplitterColumn.Width = new GridLength(DesignMetrics.LibraryExplorerSplitterWidth);
        _root.ColumnDefinitions.Add(_treeColumn);
        _root.ColumnDefinitions.Add(_treeSplitterColumn);
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        ApplyExplorerStyle();
        ApplyFavoritesStyle();
        ImeComposition.Disable(_folderTree);
        ImeComposition.Disable(_favoritesList);
        // ルート並び替え／外部フォルダ登録。ツリー本体に AllowDrop。
        _folderTree.AllowDrop = true;
        _folderTree.DragOver += FolderTree_DragOver;
        _folderTree.Drop += FolderTree_Drop;
        _folderTree.SelectedItemChanged += FolderTree_SelectedItemChanged;
        _folderTree.PreviewMouseLeftButtonDown += FolderTree_PreviewMouseLeftButtonDown;
        _folderTree.PreviewMouseMove += FolderTree_PreviewMouseMove;
        _folderTree.PreviewMouseLeftButtonUp += FolderTree_PreviewMouseLeftButtonUp;
        _folderTree.PreviewMouseRightButtonDown += FolderTree_PreviewMouseRightButtonDown;
        _folderTree.PreviewKeyDown += FolderTree_PreviewKeyDown;
        _folderTree.PreviewTextInput += FolderTree_PreviewTextInput;
        // 上段 0–9 は割合ジャンプ。ツリーのインクリメンタル検索には使わない。
        _folderTree.IsTextSearchEnabled = false;
        BindLibraryCopyCommand(_folderTree, LibraryPane.Explorer);
        _folderTree.CommandBindings.Add(new CommandBinding(ApplicationCommands.Delete, (_, e) => e.Handled = true));
        BuildExplorerRoots();
        RebuildExplorerContextMenu();

        ApplyPaneTitleStyle(_explorerLabel);
        ApplyPaneTitleStyle(_favoritesLabel);
        ApplyPaneTitleStyle(_playlistLabel, new Thickness(0, 0, 8, 0));
        _favoritesList.ItemsSource = _favorites;
        _favoritesList.ItemTemplate = FavoriteRowTemplate();
        _favoritesList.SelectionMode = SelectionMode.Extended;
        _favoritesList.BorderThickness = new Thickness(0);
        _favoritesList.Background = Brushes.Transparent;
        _favoritesList.Padding = new Thickness(4, 2, 4, 6);
        _favoritesList.FontSize = 11;
        _favoritesList.SetResourceReference(ForegroundProperty, "PrimaryForeBrush");
        // 上段 0–9 は割合ジャンプ。名前の先頭一致ジャンプはしない。
        _favoritesList.IsTextSearchEnabled = false;
        _favoritesList.PreviewKeyDown += FavoritesList_PreviewKeyDown;
        _favoritesList.SelectionChanged += FavoritesList_SelectionChanged;
        _favoritesList.PreviewMouseLeftButtonDown += FavoritesList_PreviewMouseLeftButtonDown;
        _favoritesList.PreviewMouseMove += FavoritesList_PreviewMouseMove;
        _favoritesList.PreviewMouseLeftButtonUp += (_, _) => _favoritesDragStart = null;
        BindLibraryCopyCommand(_favoritesList, LibraryPane.Favorites);
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
        var explorerHeader = new DockPanel { LastChildFill = true };
        var explorerSearch = CreateSearchEditor(_explorerSearchBox, _explorerSearchHint);
        explorerSearch.Width = DesignMetrics.From96(120);
        explorerSearch.MinWidth = DesignMetrics.From96(72);
        explorerSearch.Margin = new Thickness(0, 6, 8, 6);
        explorerSearch.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(explorerSearch, Dock.Right);
        explorerHeader.Children.Add(explorerSearch);
        _explorerLabel.VerticalAlignment = VerticalAlignment.Center;
        explorerHeader.Children.Add(_explorerLabel);
        DockPanel.SetDock(explorerHeader, Dock.Top);
        explorerPane.Children.Add(explorerHeader);
        var treeBody = new Grid { Background = Brushes.Transparent };
        treeBody.Children.Add(_folderTree);
        var treeOverlay = new Grid
        {
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        BuildExplorerTypeaheadToast(treeBody);
        treeOverlay.Children.Add(_explorerTypeaheadToast);
        Panel.SetZIndex(_explorerTypeaheadToast, 20);
        var treeStack = new Grid { Background = Brushes.Transparent };
        treeStack.Children.Add(treeBody);
        treeStack.Children.Add(treeOverlay);
        explorerPane.Children.Add(treeStack);
        var treeHost = new Grid { Background = Brushes.Transparent };
        // 見出し上へのドロップもルート登録として受け取る。
        treeHost.AllowDrop = true;
        treeHost.DragOver += FolderTree_DragOver;
        treeHost.Drop += FolderTree_Drop;
        _explorerDropHost = treeHost;
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
        favoritesSplitter.VerticalAlignment = VerticalAlignment.Top;
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
        var playlistSearch = CreateSearchEditor(_playlistSearchBox, _playlistSearchHint);
        playlistSearch.Width = DesignMetrics.From96(180);
        playlistSearch.MinWidth = DesignMetrics.From96(96);
        playlistSearch.Margin = new Thickness(8, 0, 8, 0);
        playlistSearch.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(playlistSearch, Dock.Right);
        bar.Children.Add(playlistSearch);

        _shuffleCheck.Style = TryFindResource("DarkCheckBoxStyle") as Style;
        _shuffleCheck.Content = UiStrings.LabelLibraryShuffle;
        _shuffleCheck.IsChecked = false;
        _shuffleCheck.Focusable = false;
        _shuffleCheck.VerticalAlignment = VerticalAlignment.Center;
        _shuffleCheck.FontSize = 11;
        _shuffleCheck.Margin = new Thickness(8, 0, 0, 0);
        _shuffleCheck.SetResourceReference(Control.ForegroundProperty, "PrimaryForeBrush");
        _shuffleCheck.Checked += ShuffleCheck_Changed;
        _shuffleCheck.Unchecked += ShuffleCheck_Changed;
        DockPanel.SetDock(_shuffleCheck, Dock.Right);
        bar.Children.Add(_shuffleCheck);

        _autoPlayNextCheck.Style = TryFindResource("DarkCheckBoxStyle") as Style;
        _autoPlayNextCheck.Content = UiStrings.LabelLibraryAutoPlayNext;
        _autoPlayNextCheck.IsChecked = false;
        _autoPlayNextCheck.Focusable = false;
        _autoPlayNextCheck.VerticalAlignment = VerticalAlignment.Center;
        _autoPlayNextCheck.FontSize = 11;
        _autoPlayNextCheck.Margin = new Thickness(8, 0, 0, 0);
        _autoPlayNextCheck.SetResourceReference(Control.ForegroundProperty, "PrimaryForeBrush");
        _autoPlayNextCheck.Checked += AutoPlayNextCheck_Changed;
        _autoPlayNextCheck.Unchecked += AutoPlayNextCheck_Changed;
        DockPanel.SetDock(_autoPlayNextCheck, Dock.Right);
        bar.Children.Add(_autoPlayNextCheck);

        _autoPlayCheck.Style = TryFindResource("DarkCheckBoxStyle") as Style;
        _autoPlayCheck.Content = UiStrings.LabelLibraryAutoPlay;
        _autoPlayCheck.IsChecked = true;
        _autoPlayCheck.Focusable = false;
        _autoPlayCheck.VerticalAlignment = VerticalAlignment.Center;
        _autoPlayCheck.FontSize = 11;
        _autoPlayCheck.Margin = new Thickness(8, 0, 0, 0);
        _autoPlayCheck.SetResourceReference(Control.ForegroundProperty, "PrimaryForeBrush");
        _autoPlayCheck.Checked += AutoPlayCheck_Changed;
        _autoPlayCheck.Unchecked += AutoPlayCheck_Changed;
        DockPanel.SetDock(_autoPlayCheck, Dock.Right);
        bar.Children.Add(_autoPlayCheck);

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
        _glowHost.CacheMode = CreateGlowBitmapCache();
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
        splitter.HorizontalAlignment = HorizontalAlignment.Left;
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
        BuildTransferToast();

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

    private void BuildExplorerTypeaheadToast(FrameworkElement captureSource)
    {
        _explorerTypeaheadToastText.FontFamily = new FontFamily("Consolas");
        _explorerTypeaheadToastText.FontSize = DesignMetrics.From96(22);
        _explorerTypeaheadToastText.FontWeight = FontWeights.Normal;
        _explorerTypeaheadToastText.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryForeBrush");
        _explorerTypeaheadToastText.TextAlignment = TextAlignment.Center;
        _explorerTypeaheadToastText.TextWrapping = TextWrapping.NoWrap;
        var plate = new FrostedPlate
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Source = captureSource,
            CornerRadius = DesignMetrics.ControlCorner,
        };
        plate.Children.Add(new Border
        {
            Padding = DesignMetrics.PlaylistVideoTimecodePadding,
            Background = Brushes.Transparent,
            Child = _explorerTypeaheadToastText,
        });
        _explorerTypeaheadToast.Children.Add(plate);
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

    /// <summary>
    /// 1px のマスに置いても当たりがマスで切れない。はみ出しはツリー側ではなく隣ペインへ。
    /// </summary>
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
        var fore = ResolveThemeBrush("PrimaryForeBrush");
        ApplyNavSelectionResources(_folderTree, selected, fore);

        var itemStyle = new Style(typeof(TreeViewItem));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2, 1, 4, 1)));
        itemStyle.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
        itemStyle.Setters.Add(new Setter(Control.TemplateProperty, TreeNavTemplate(hover, selected)));
        itemStyle.Setters.Add(new EventSetter(
            RequestBringIntoViewEvent,
            new RequestBringIntoViewEventHandler(ExplorerItem_RequestBringIntoView)));
        _folderTree.Resources[typeof(TreeViewItem)] = itemStyle;
        _folderTree.ItemContainerStyle = itemStyle;
        ApplyTreeItemStyle(_folderTree.Items, itemStyle);
        RelabelExplorerRoots();
        ApplyTreeMultiSelectChrome();
    }

    /// <summary>
    /// 既定の BringIntoView は長い名前で横に飛ぶ。縦は ScrollViewer に任せ、横位置は戻す。
    /// </summary>
    private void ExplorerItem_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (_explorerBringIntoViewBusy || sender is not TreeViewItem item)
        {
            return;
        }

        if (FindDescendantScrollViewer(_folderTree) is not { } scroll)
        {
            return;
        }

        if (FindAncestor<ScrollBar>(e.OriginalSource as DependencyObject) is not null
            || IsExplorerHorizontalScrollHeld(scroll))
        {
            e.Handled = true;
            return;
        }

        var header = FindExplorerHeader(item) ?? item;
        var height = header.ActualHeight > 0 ? header.ActualHeight : 1;
        var keepX = scroll.HorizontalOffset;
        e.Handled = true;
        _explorerBringIntoViewBusy = true;
        try
        {
            header.BringIntoView(new Rect(0, 0, 1, height));
        }
        finally
        {
            _explorerBringIntoViewBusy = false;
        }

        RestoreExplorerHorizontalOffset(scroll, keepX);
    }

    private static void RestoreExplorerHorizontalOffset(ScrollViewer scroll, double offset)
    {
        if (IsExplorerHorizontalScrollHeld(scroll))
        {
            return;
        }

        scroll.ScrollToHorizontalOffset(offset);
    }

    private static bool IsExplorerHorizontalScrollHeld(ScrollViewer scroll)
    {
        var count = VisualTreeHelper.GetChildrenCount(scroll);
        for (var i = 0; i < count; i++)
        {
            if (FindHorizontalScrollThumb(VisualTreeHelper.GetChild(scroll, i)) is { IsDragging: true })
            {
                return true;
            }
        }

        return false;
    }

    private static Thumb? FindHorizontalScrollThumb(DependencyObject root)
    {
        if (root is ScrollBar { Orientation: Orientation.Horizontal } bar)
        {
            return FindDescendant<Thumb>(bar);
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindHorizontalScrollThumb(VisualTreeHelper.GetChild(root, i));
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static FrameworkElement? FindExplorerHeader(DependencyObject root)
    {
        if (root is ItemsPresenter)
        {
            return null;
        }

        if (root is FrameworkElement { Name: "Bd" } header)
        {
            return header;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindExplorerHeader(VisualTreeHelper.GetChild(root, i));
            if (found is not null)
            {
                return found;
            }
        }

        return null;
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
        var fore = ResolveThemeBrush("PrimaryForeBrush");
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
        _favoritesList.ItemTemplate = FavoriteRowTemplate();
    }

    /// <summary>
    /// 名前の TextBlock に色を載せる。ListBoxItem.Foreground だと ContentPresenter 経由で上書きされやすい。
    /// </summary>
    private static DataTemplate FavoriteRowTemplate()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.Name = "FavoriteName";
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(LibraryFavoriteRow.Name)));
        text.SetValue(TextBlock.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush"));
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);

        var template = new DataTemplate(typeof(LibraryFavoriteRow)) { VisualTree = text };
        var missing = new DataTrigger
        {
            Binding = new Binding(nameof(LibraryFavoriteRow.IsMissing)),
            Value = true,
        };
        missing.Setters.Add(new Setter(
            TextBlock.ForegroundProperty,
            new DynamicResourceExtension(MissingRegisteredPathForeBrushKey),
            "FavoriteName"));
        template.Triggers.Add(missing);
        return template;
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
        PlayerChrome.Get("PlayerSelectionFillBrush", theme).A;

    internal static Color LibrarySelectionRgb(Color accent, UiTheme theme) =>
        PlayerChrome.Get("PlayerSelectionFillBrush", theme);

    internal static Brush CyanSelectionBrush() =>
        PlayerChrome.Brush("PlayerSelectionFillBrush");

    internal static Brush GrayHoverBrush() =>
        PlayerChrome.Brush("PlayerHoverFillBrush");

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

    private sealed class FileNameMarkerBrushConverter : IValueConverter
    {
        internal static readonly FileNameMarkerBrushConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is int index && FileNameColorMarkerVisual.TryBrush(index, out var brush))
            {
                return brush;
            }

            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            Binding.DoNothing;
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

    private static Brush ResolveThemeBrush(string key) =>
        WpfControlHelpers.FrozenBrush(Theme.Get(key));

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
        // 文字列 Header のときも TreeViewItem.Foreground を拾う（欠落ルートの赤字用）。
        header.SetValue(
            TextElement.ForegroundProperty,
            new TemplateBindingExtension(Control.ForegroundProperty));

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
            || FindTreeExpandToggle(origin) is not null)
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
                ClearTreeMultiSelect();
                _treeMultiSelected.Add(current);
                ApplyTreeMultiSelectChrome();
                current.IsSelected = true;
                current.BringIntoView();
                current.Focus();
                return;
            }

            _treeSelectionAnchor ??= caret ?? current;
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
        AddNameColumn();
        AddColumn(LibraryFileColumn.Title, nameof(LibraryFileRow.Title));
        AddColumn(LibraryFileColumn.Album, nameof(LibraryFileRow.Album));
        AddColumn(LibraryFileColumn.Artist, nameof(LibraryFileRow.Artist));
        AddColumn(LibraryFileColumn.Composer, nameof(LibraryFileRow.Composer));
        AddColumn(
            LibraryFileColumn.Duration,
            nameof(LibraryFileRow.DurationText),
            right: true,
            trim: false);
        AddColumn(LibraryFileColumn.Track, nameof(LibraryFileRow.Track), right: true);
        AddColumn(LibraryFileColumn.Disc, nameof(LibraryFileRow.Disc), right: true);
        AddColumn(LibraryFileColumn.Year, nameof(LibraryFileRow.Year), right: true);
        AddColumn(LibraryFileColumn.Genre, nameof(LibraryFileRow.Genre));
        AddColumn(LibraryFileColumn.Date, nameof(LibraryFileRow.DateText));
        AddColumn(LibraryFileColumn.Comment, nameof(LibraryFileRow.Comment));
        AddColumn(LibraryFileColumn.ParentFolder, nameof(LibraryFileRow.ParentFolder));
        AddWaveformColumn();
        AddColumn(LibraryFileColumn.AlbumArtist, nameof(LibraryFileRow.AlbumArtist));
        AddColumn(LibraryFileColumn.Kind, nameof(LibraryFileRow.Kind));
        AddColumn(
            LibraryFileColumn.SampleRate,
            nameof(LibraryFileRow.SampleRateText),
            right: true,
            nonDefaultBinding: nameof(LibraryFileRow.SampleRateNonDefault));
        AddColumn(
            LibraryFileColumn.BitDepth,
            nameof(LibraryFileRow.BitDepthText),
            right: true,
            nonDefaultBinding: nameof(LibraryFileRow.BitDepthNonDefault));
        AddColumn(
            LibraryFileColumn.Channels,
            nameof(LibraryFileRow.ChannelsText),
            right: true,
            nonDefaultBinding: nameof(LibraryFileRow.ChannelsNonDefault));
        AddColumn(LibraryFileColumn.BitRate, nameof(LibraryFileRow.BitRateText), right: true);
        AddColumn(LibraryFileColumn.VideoSize, nameof(LibraryFileRow.VideoSizeText), right: true);
        AddColumn(LibraryFileColumn.FrameRate, nameof(LibraryFileRow.FrameRateText), right: true);
        AddColumn(LibraryFileColumn.VideoCodec, nameof(LibraryFileRow.VideoCodec));
        AddColumn(LibraryFileColumn.AudioCodec, nameof(LibraryFileRow.AudioCodec));
        AddColumn(
            LibraryFileColumn.Loudness,
            nameof(LibraryFileRow.LoudnessText),
            right: true,
            loudnessTone: true);
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
        ImeComposition.Disable(_grid);
        // 上段 0–9 は波形の割合ジャンプ。行の TextSearch に吸われてフォーカスがリストへ戻るのを防ぐ。
        _grid.IsTextSearchEnabled = false;
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
        // 列再レイアウトで CurrentItem が先頭へ動いても、選択シアンを追従させない。
        _grid.IsSynchronizedWithCurrentItem = false;
        _grid.ClipboardCopyMode = DataGridClipboardCopyMode.None;
        BindLibraryCopyCommand(_grid, LibraryPane.List);
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
        _grid.ColumnReordered += Grid_ColumnReordered;
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

    /// <summary>
    /// グループ左のジャケット枠。実ジャケットが1件も無いときは出さない。
    /// </summary>
    private bool ShowsGroupJackets
    {
        get
        {
            if (_group == LibraryFileGroup.None)
            {
                return false;
            }

            foreach (var row in _rows)
            {
                if (row.HasArtwork)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private void SyncGroupChrome()
    {
        _grid.HeadersVisibility = _rows.Count > 0 || _columnChromeFadingOut
            ? DataGridHeadersVisibility.Column
            : DataGridHeadersVisibility.None;
        var showJackets = ShowsGroupJackets;
        if (!_columnChromeFadingOut)
        {
            _groupSpacer.Visibility = showJackets ? Visibility.Visible : Visibility.Collapsed;
            _grid.FrozenColumnCount = showJackets ? 1 : 0;
            SetPlaylistBandLeft(_grid, showJackets ? GroupJacketColumnWidth : 0);
        }

        if (!KeepsColumnLayoutWhileEmpty())
        {
            ApplyColumnDisplayOrder(_visibleColumns);
        }
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

    private static T? FindAncestor<T>(DependencyObject? origin)
        where T : DependencyObject
    {
        while (origin is not null)
        {
            if (origin is T match)
            {
                return match;
            }

            origin = origin is Visual
                ? VisualTreeHelper.GetParent(origin)
                : LogicalTreeHelper.GetParent(origin);
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
            new(LibraryFileGroup.Color, UiStrings.LibraryGroupColor),
        };
        _groupCombo.SelectedValue = selected;
    }

    public void SetVisibleColumnPresets(
        IEnumerable<LibraryFileColumn> waveColumns,
        IEnumerable<LibraryFileColumn> mp3Columns,
        IEnumerable<LibraryFileColumn>? mixedColumns = null,
        bool mixedCustomized = false,
        IEnumerable<LibraryFileColumn>? videoColumns = null)
    {
        _waveColumns = LibraryColumnFilter.ResolveWave(
            LibraryColumnFilter.Serialize(waveColumns, LibraryColumnFilter.WaveDefaults));
        _mp3Columns = LibraryColumnFilter.ResolveMp3(
            LibraryColumnFilter.Serialize(mp3Columns, LibraryColumnFilter.Mp3Defaults));
        _videoColumns = LibraryColumnFilter.ResolveVideo(
            videoColumns is null
                ? null
                : LibraryColumnFilter.Serialize(videoColumns, LibraryColumnFilter.VideoDefaults));
        _mixedColumnsCustomized = mixedCustomized && mixedColumns is not null;
        _mixedColumns = _mixedColumnsCustomized
            ? LibraryColumnFilter.ResolveMixed(
                LibraryColumnFilter.Serialize(mixedColumns!, LibraryColumnFilter.MixedDefaults),
                _waveColumns,
                _mp3Columns,
                _videoColumns)
            : LibraryColumnFilter.Union(_waveColumns, _mp3Columns, _videoColumns);
        ApplyActiveColumnVisibility(notify: false, forceLayout: true);
    }

    /// <summary>テスト用。Wave / MP3 / Video プリセットへ同じ列を入れる。</summary>
    public void SetVisibleColumns(IEnumerable<LibraryFileColumn> columns)
    {
        var ordered = LibraryColumnFilter.ResolveWave(
            LibraryColumnFilter.Serialize(columns, LibraryColumnFilter.WaveDefaults));
        SetVisibleColumnPresets(ordered, ordered, videoColumns: ordered);
    }

    internal IReadOnlyList<LibraryFileColumn> WaveColumnOrder => _waveColumns;

    internal IReadOnlyList<LibraryFileColumn> Mp3ColumnOrder => _mp3Columns;

    internal IReadOnlyList<LibraryFileColumn> VideoColumnOrder => _videoColumns;

    internal IReadOnlyList<LibraryFileColumn> MixedColumnOrder => _mixedColumns;

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

    private void ApplyColumnVisibility(IReadOnlyList<LibraryFileColumn> visible, bool notify)
    {
        var kind = CurrentPlaylistColumnKind();
        LibraryColumnFilter.ApplyVisibleChange(
            kind,
            _waveColumns,
            _mp3Columns,
            _videoColumns,
            _mixedColumns,
            visible,
            out var wave,
            out var mp3,
            out var video,
            out var mixed);
        _waveColumns = wave;
        _mp3Columns = mp3;
        _videoColumns = video;
        _mixedColumns = mixed;
        // Wave / MP3 / Video を変えたら混在は和集合へ戻す。混在を弄ったときだけユーザー設定になる。
        _mixedColumnsCustomized = kind == LibraryPlaylistColumnKind.Mixed;

        ApplyActiveColumnVisibility(notify);
    }

    private void ApplyActiveColumnVisibility(bool notify, bool forceLayout = false)
    {
        if (forceLayout || !KeepsColumnLayoutWhileEmpty())
        {
            _visibleColumns = LibraryColumnFilter.ResolveActive(
                _rows,
                _waveColumns,
                _mp3Columns,
                _mixedColumns,
                _videoColumns);
            ApplyColumnDisplayOrder(_visibleColumns);
        }

        ApplyEffectiveColumnVisibility();
        EnforceFadingColumnSnapshotIfEmpty();

        if (notify)
        {
            VisibleColumnPresetsChanged?.Invoke(
                this,
                new LibraryColumnPresets(
                    _waveColumns,
                    _mp3Columns,
                    _videoColumns,
                    _mixedColumns,
                    PersistMixed: _mixedColumnsCustomized));
        }

        RequestFitColumns();
        SyncGroupChrome();
        if (!_columnChromeFadingOut)
        {
            RefreshPlaylistWaveformSize(reschedule: true);
            SchedulePlaylistLoudness();
        }
    }

    /// <summary>
    /// 設定でオンの列のうち、プレイリストに値が無い列は隠す。値が付けば出す。
    /// 空のプレイリストは列をすべて隠す。ソートは曲が入るまで残す。
    /// 表示中から空にしたときはメーターと同じ 1 秒でフェードアウトする。
    /// ソート中の列が隠れたらソート無しにする。
    /// </summary>
    private void ApplyEffectiveColumnVisibility()
    {
        if (_rows.Count > 0)
        {
            _playlistColumnKind = LibraryColumnFilter.ClassifyPlaylistColumns(_rows);
            _visibleColumns = LibraryColumnFilter.ResolveActive(
                _rows,
                _waveColumns,
                _mp3Columns,
                _mixedColumns,
                _videoColumns);
            var enabled = new HashSet<LibraryFileColumn>(_visibleColumns);
            var used = CurrentUsedColumns();
            if (_sortColumn is { } sort
                && !LibraryColumnFilter.IsEffectivelyVisible(sort, enabled, used))
            {
                ClearSort(resort: false);
                NotifySortChanged();
            }

            CancelColumnChromeFade(resetOpacity: true);
            foreach (var pair in _columns)
            {
                pair.Value.Visibility = LibraryColumnFilter.IsEffectivelyVisible(pair.Key, enabled, used)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            return;
        }

        if (_columnChromeFadingOut)
        {
            EnforceFadingColumnSnapshot();
            return;
        }

        if (!AnyPlaylistColumnShown())
        {
            CancelColumnChromeFade(resetOpacity: true);
            return;
        }

        if (BeginColumnChromeFadeOut())
        {
            return;
        }

        CollapseAllPlaylistColumns();
    }

    private LibraryPlaylistColumnKind CurrentPlaylistColumnKind() =>
        _rows.Count == 0
            ? _playlistColumnKind
            : LibraryColumnFilter.ClassifyPlaylistColumns(_rows);

    private void BeginColumnOrderCommitGate() => _columnOrderCommitGate++;

    private void EndColumnOrderCommitGate()
    {
        if (_columnOrderCommitGate > 0)
        {
            _columnOrderCommitGate--;
        }
    }

    private bool SuppressesColumnOrderCommit() =>
        _columnOrderBusy
        || _columnOrderCommitGate > 0
        || _columnChromeFadingOut
        || _fadingColumnSnapshot is not null;

    private void PrepareEmptyPlaylistColumnFade()
    {
        if (_columnChromeFadingOut || !AnyPlaylistColumnShown())
        {
            return;
        }

        BeginColumnChromeFadeOut();
    }

    private bool KeepsColumnLayoutWhileEmpty() =>
        _rows.Count == 0
        && (AnyPlaylistColumnShown() || _columnChromeFadingOut || _fadingColumnSnapshot is not null);

    private bool AnyPlaylistColumnShown()
    {
        foreach (var pair in _columns)
        {
            if (pair.Value.Visibility == Visibility.Visible)
            {
                return true;
            }
        }

        return false;
    }

    private bool BeginColumnChromeFadeOut()
    {
        if (!IsLoaded)
        {
            return false;
        }

        if (FindDescendant<DataGridColumnHeadersPresenter>(_grid) is not { } headers)
        {
            UpdateLayout();
            headers = FindDescendant<DataGridColumnHeadersPresenter>(_grid);
            if (headers is null)
            {
                return false;
            }
        }

        _columnChromeFadingOut = true;
        CaptureFadingColumnSnapshot();
        var ticket = ++_columnChromeFadeTicket;
        var from = headers.Opacity;
        headers.BeginAnimation(UIElement.OpacityProperty, null);
        headers.Opacity = from;
        if (from <= 0.001)
        {
            FinishColumnChromeFadeOut(ticket);
            return true;
        }

        var anim = LibraryPlayerMode.CreateMeterFade(from, 0);
        anim.Completed += (_, _) => FinishColumnChromeFadeOut(ticket);
        headers.BeginAnimation(UIElement.OpacityProperty, anim);
        return true;
    }

    private void FinishColumnChromeFadeOut(int ticket)
    {
        if (ticket != _columnChromeFadeTicket)
        {
            return;
        }

        _columnChromeFadingOut = false;
        _fadingColumnSnapshot = null;
        if (_rows.Count > 0)
        {
            ResetColumnHeaderOpacity();
            ApplyEffectiveColumnVisibility();
            SyncGroupChrome();
            RequestFitColumns();
            return;
        }

        CollapseAllPlaylistColumns();
        ResetColumnHeaderOpacity();
        SyncGroupChrome();
        RequestFitColumns();
    }

    private void CaptureFadingColumnSnapshot()
    {
        var snapshot = new HashSet<LibraryFileColumn>();
        foreach (var pair in _columns)
        {
            if (pair.Value.Visibility == Visibility.Visible)
            {
                snapshot.Add(pair.Key);
            }
        }

        _fadingColumnSnapshot = snapshot;
    }

    private void EnforceFadingColumnSnapshotIfEmpty()
    {
        if (_rows.Count == 0)
        {
            EnforceFadingColumnSnapshot();
        }
    }

    private void EnforceFadingColumnSnapshot()
    {
        if (_fadingColumnSnapshot is not { Count: > 0 } snapshot)
        {
            return;
        }

        foreach (var pair in _columns)
        {
            pair.Value.Visibility = snapshot.Contains(pair.Key)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void CollapseAllPlaylistColumns()
    {
        foreach (var pair in _columns)
        {
            pair.Value.Visibility = Visibility.Collapsed;
        }
    }

    private void CancelColumnChromeFade(bool resetOpacity)
    {
        _columnChromeFadeTicket++;
        _columnChromeFadingOut = false;
        _fadingColumnSnapshot = null;
        if (resetOpacity)
        {
            ResetColumnHeaderOpacity();
        }
    }

    private void ResetColumnHeaderOpacity()
    {
        if (FindDescendant<DataGridColumnHeadersPresenter>(_grid) is not { } headers)
        {
            return;
        }

        headers.BeginAnimation(UIElement.OpacityProperty, null);
        headers.Opacity = 1;
    }

    private HashSet<LibraryFileColumn>? CurrentUsedColumns() =>
        LibraryColumnFilter.UsedColumns(_rows);

    private void ClearSort(bool resort)
    {
        if (_sortColumn is null)
        {
            return;
        }

        _sortColumn = null;
        foreach (var item in _columns)
        {
            item.Value.SortDirection = null;
        }

        RefreshSortChrome();
        if (!resort)
        {
            return;
        }

        var active = SelectedSession;
        var selected = SelectedSessions;
        BindRows(active, selected);
    }

    private IReadOnlyList<LibraryFileRow> SortRows(IReadOnlyList<LibraryFileRow> rows) =>
        _sortColumn is { } column
            ? LibraryFileList.Sort(rows, column, _sortDirection)
            : rows;

    private void RefreshEffectiveColumns()
    {
        BeginColumnOrderCommitGate();
        try
        {
            if (!KeepsColumnLayoutWhileEmpty())
            {
                var next = LibraryColumnFilter.ResolveActive(
                    _rows,
                    _waveColumns,
                    _mp3Columns,
                    _mixedColumns,
                    _videoColumns);
                if (!next.SequenceEqual(_visibleColumns))
                {
                    _visibleColumns = next;
                    ApplyColumnDisplayOrder(_visibleColumns);
                }
            }

            ApplyEffectiveColumnVisibility();
            EnforceFadingColumnSnapshotIfEmpty();
            SyncGroupChrome();
            RequestFitColumns();
        }
        finally
        {
            EndColumnOrderCommitGate();
        }
    }

    private void ApplyColumnDisplayOrder(IReadOnlyList<LibraryFileColumn> visible)
    {
        if (_columns.Count == 0)
        {
            return;
        }

        _columnOrderBusy = true;
        try
        {
            var ordered = new List<DataGridColumn>(_grid.Columns.Count);
            var grouped = _group != LibraryFileGroup.None;
            if (grouped)
            {
                ordered.Add(_groupSpacer);
            }

            var seen = new HashSet<LibraryFileColumn>();
            foreach (var column in visible)
            {
                if (_columns.TryGetValue(column, out var gridColumn) && seen.Add(column))
                {
                    ordered.Add(gridColumn);
                }
            }

            foreach (var column in LibraryColumnFilter.All)
            {
                if (!seen.Contains(column) && _columns.TryGetValue(column, out var gridColumn))
                {
                    ordered.Add(gridColumn);
                }
            }

            if (!grouped)
            {
                ordered.Add(_groupSpacer);
            }

            for (var i = 0; i < ordered.Count; i++)
            {
                if (ordered[i].DisplayIndex != i)
                {
                    ordered[i].DisplayIndex = i;
                }
            }
        }
        finally
        {
            _columnOrderBusy = false;
        }
    }

    private LibraryFileColumn[] ReadVisibleColumnOrder()
    {
        var visible = new List<(int Index, LibraryFileColumn Column)>(_columns.Count);
        foreach (var pair in _columns)
        {
            if (pair.Value.Visibility != Visibility.Visible
                && (_rows.Count > 0 || Array.IndexOf(_visibleColumns, pair.Key) < 0))
            {
                continue;
            }

            visible.Add((pair.Value.DisplayIndex, pair.Key));
        }

        visible.Sort(static (left, right) => left.Index.CompareTo(right.Index));
        var result = new LibraryFileColumn[visible.Count];
        for (var i = 0; i < visible.Count; i++)
        {
            result[i] = visible[i].Column;
        }

        if (result.Length == 0 || Array.IndexOf(result, LibraryFileColumn.Name) < 0)
        {
            return [.. _visibleColumns];
        }

        return result;
    }

    private void Grid_ColumnReordered(object? sender, DataGridColumnEventArgs e)
    {
        if (_columnOrderBusy)
        {
            return;
        }

        Dispatcher.BeginInvoke(CommitColumnOrder, DispatcherPriority.Background);
    }

    internal void CommitColumnOrder()
    {
        if (SuppressesColumnOrderCommit())
        {
            return;
        }

        var next = ReadVisibleColumnOrder();
        ApplyColumnDisplayOrder(next);
        if (next.SequenceEqual(_visibleColumns))
        {
            return;
        }

        ApplyColumnVisibility(next, notify: true);
    }

    internal void SetColumnEnabledForTests(LibraryFileColumn column, bool enabled)
    {
        var selected = new HashSet<LibraryFileColumn>(_visibleColumns) { LibraryFileColumn.Name };
        if (enabled)
        {
            selected.Add(column);
        }
        else
        {
            selected.Remove(column);
        }

        ApplyColumnVisibility(LibraryColumnFilter.Merge(_visibleColumns, selected), notify: true);
    }

    internal void MoveVisibleColumnForTests(LibraryFileColumn column, int displayIndex)
    {
        if (!_columns.TryGetValue(column, out var gridColumn))
        {
            return;
        }

        gridColumn.DisplayIndex = displayIndex;
        CommitColumnOrder();
    }

    private static string ColumnHeader(LibraryFileColumn column) =>
        UiStrings.LibraryColumnHeader(column);

    private void AddGroupSpacerColumn()
    {
        var jacket = new FrameworkElementFactory(typeof(LibraryRowJacketImage));
        jacket.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        jacket.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        var cell = new DataTemplate(typeof(LibraryFileRow)) { VisualTree = jacket };
        _groupSpacer = new DataGridTemplateColumn
        {
            Header = string.Empty,
            CellTemplate = cell,
            Width = new DataGridLength(GroupJacketColumnWidth),
            MinWidth = GroupJacketColumnWidth,
            MaxWidth = GroupJacketColumnWidth,
            IsReadOnly = true,
            CanUserSort = false,
            CanUserResize = false,
            CanUserReorder = false,
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

    private void AddNameColumn()
    {
        var marker = new FrameworkElementFactory(typeof(Border));
        marker.Name = "NameMarker";
        marker.SetValue(FrameworkElement.WidthProperty, FileNameColorMarkerVisual.Width);
        marker.SetValue(FrameworkElement.HeightProperty, FileNameColorMarkerVisual.Height);
        marker.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        marker.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, FileNameColorMarkerVisual.Gap, 0));
        marker.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        marker.SetValue(DockPanel.DockProperty, Dock.Left);
        marker.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        marker.SetBinding(
            Border.BackgroundProperty,
            new Binding(nameof(LibraryFileRow.FileNameMarkerIndex))
            {
                Converter = FileNameMarkerBrushConverter.Instance,
            });

        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.Name = "NameText";
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(LibraryFileRow.Name)));
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        text.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        text.SetValue(TextBlock.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush"));

        var panel = new FrameworkElementFactory(typeof(DockPanel));
        panel.SetValue(
            FrameworkElement.MarginProperty,
            new Thickness(LibraryColumnCellPadX, 0, LibraryColumnCellPadX, 0));
        panel.AppendChild(marker);
        panel.AppendChild(text);

        var cellTemplate = new DataTemplate(typeof(LibraryFileRow)) { VisualTree = panel };
        var showMarker = new DataTrigger
        {
            Binding = new Binding(nameof(LibraryFileRow.HasFileNameMarker)),
            Value = true,
        };
        showMarker.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "NameMarker"));
        cellTemplate.Triggers.Add(showMarker);
        var cutDim = new DataTrigger
        {
            Binding = new Binding(nameof(LibraryFileRow.CutPending)),
            Value = true,
        };
        cutDim.Setters.Add(new Setter(
            TextBlock.ForegroundProperty,
            new DynamicResourceExtension("MutedForeBrush"),
            "NameText"));
        cellTemplate.Triggers.Add(cutDim);

        var gridColumn = new DataGridTemplateColumn
        {
            CellTemplate = cellTemplate,
            MinWidth = 0,
            Width = DataGridLength.SizeToCells,
            IsReadOnly = true,
            CanUserSort = true,
            SortMemberPath = nameof(LibraryFileRow.Name),
        };
        var sortHeader = new LibrarySortHeader(ColumnHeader(LibraryFileColumn.Name));
        _sortHeaders[LibraryFileColumn.Name] = sortHeader;
        gridColumn.Header = sortHeader;
        _columns[LibraryFileColumn.Name] = gridColumn;
        _grid.Columns.Add(gridColumn);
    }

    private void AddColumn(
        LibraryFileColumn column,
        string binding,
        bool right = false,
        string? nonDefaultBinding = null,
        bool trim = true,
        bool loudnessTone = false)
    {
        var text = new Style(typeof(TextBlock));
        text.Setters.Add(new Setter(
            TextBlock.TextTrimmingProperty,
            trim ? TextTrimming.CharacterEllipsis : TextTrimming.None));
        text.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(LibraryColumnCellPadX, 0, LibraryColumnCellPadX, 0)));
        text.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        text.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));
        var cutDim = new DataTrigger
        {
            Binding = new Binding(nameof(LibraryFileRow.CutPending)),
            Value = true,
        };
        cutDim.Setters.Add(new Setter(
            TextBlock.ForegroundProperty,
            new DynamicResourceExtension("MutedForeBrush")));
        text.Triggers.Add(cutDim);
        if (right)
        {
            text.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
        }

        if (nonDefaultBinding is not null)
        {
            // 既定 Wave フォーマットと違うレート／ビット／Ch だけ着色（行全体は塗らない）。色は色設定。
            var mismatch = new DataTrigger
            {
                Binding = new Binding(nonDefaultBinding),
                Value = true,
            };
            mismatch.Setters.Add(new Setter(
                TextBlock.ForegroundProperty,
                new DynamicResourceExtension(LibraryPlayerMode.FormatMismatchForeBrushKey)));
            text.Triggers.Add(mismatch);
        }

        if (loudnessTone)
        {
            // メーターと同じ3色。ターゲット以下が青、+3 LU までが黄、それより上が赤。
            AddLoudnessToneTrigger(text, LoudnessTraffic.Safe, "LoudnessSafeBrush");
            AddLoudnessToneTrigger(text, LoudnessTraffic.Caution, "LoudnessCautionBrush");
            AddLoudnessToneTrigger(text, LoudnessTraffic.Danger, "LoudnessDangerBrush");
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

    private static void AddLoudnessToneTrigger(Style text, LoudnessTraffic tone, string brushKey)
    {
        var trigger = new DataTrigger
        {
            Binding = new Binding(nameof(LibraryFileRow.LoudnessTone)),
            Value = tone,
        };
        trigger.Setters.Add(new Setter(
            TextBlock.ForegroundProperty,
            new DynamicResourceExtension(brushKey)));
        text.Triggers.Add(trigger);
    }

    private void AddWaveformColumn()
    {
        var width = LibraryPlaylistWaveform.ColumnWidth;
        var factory = new FrameworkElementFactory(typeof(LibraryPlaylistWaveformCell));
        factory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        factory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        var cellTemplate = new DataTemplate(typeof(LibraryFileRow)) { VisualTree = factory };
        var gridColumn = new DataGridTemplateColumn
        {
            CellTemplate = cellTemplate,
            MinWidth = width,
            MaxWidth = width,
            Width = new DataGridLength(width),
            IsReadOnly = true,
            CanUserSort = false,
            CanUserResize = false,
        };
        var sortHeader = new LibrarySortHeader(ColumnHeader(LibraryFileColumn.Waveform));
        _sortHeaders[LibraryFileColumn.Waveform] = sortHeader;
        gridColumn.Header = sortHeader;
        _columns[LibraryFileColumn.Waveform] = gridColumn;
        _grid.Columns.Add(gridColumn);
    }

    private void RequestFitColumns()
    {
        if (!IsLoaded || _fitColumnsQueued || _columnChromeFadingOut)
        {
            return;
        }

        _fitColumnsQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _fitColumnsQueued = false;
            if (_columnChromeFadingOut)
            {
                return;
            }

            FitColumns();
        }, DispatcherPriority.Background);
    }

    private void FitColumns()
    {
        if (_columnChromeFadingOut)
        {
            return;
        }

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

            if (kind == LibraryFileColumn.Waveform)
            {
                SetColumnPixelWidth(column, LibraryPlaylistWaveform.ColumnWidth);
                continue;
            }

            // 見出しは改行あり。最も長い行の幅で列を決める。
            var headerWidth = 0d;
            foreach (var line in ColumnHeader(kind).Split('\n'))
            {
                headerWidth = Math.Max(headerWidth, Measure(line, headerFace));
            }

            var max = headerWidth + headerPad;
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
                if (kind == LibraryFileColumn.Name && row.HasFileNameMarker)
                {
                    // マーカーはデバイスピクセルへスナップする。幅ぴったりだと末尾が「...」に置き換わる。
                    var slot = FileNameColorMarkerVisual.SlotWidth * dpi;
                    width += (Math.Ceiling(slot - 1e-6) / dpi) + 2;
                }

                if (kind == LibraryFileColumn.Duration)
                {
                    // 列幅ぴったりだとホバー再スナップで末尾ミリ秒が欠けて、時間が変わったように見える。
                    width += 2;
                }

                if (width > max)
                {
                    max = width;
                }
            }

            var fitted = Math.Clamp(
                Math.Ceiling(max),
                1,
                DesignMetrics.LibraryColumnMaxWidth);
            SetColumnPixelWidth(column, fitted);
        }
    }

    /// <summary>
    /// 幅が同じなら触らない。MinWidth=0 経由の再レイアウトは無駄な列再計算を招くため避ける。
    /// </summary>
    private static bool SetColumnPixelWidth(DataGridColumn column, double width)
    {
        width = Math.Max(0, width);
        if (column.Width.IsAbsolute
            && Math.Abs(column.Width.Value - width) < 0.5
            && Math.Abs(column.MinWidth - width) < 0.5
            && (double.IsPositiveInfinity(column.MaxWidth) || Math.Abs(column.MaxWidth - width) < 0.5))
        {
            return false;
        }

        column.MinWidth = 0;
        column.MaxWidth = width;
        column.Width = new DataGridLength(width, DataGridLengthUnitType.Pixel);
        column.MinWidth = width;
        return true;
    }

    private void SchedulePlaylistWaveforms()
    {
        CancelPlaylistWaveforms();
        if (!IsWaveformColumnEffectivelyVisible() || _items.Count == 0)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _playlistWaveCts = cts;
        var generation = ++_playlistWaveGeneration;
        var token = cts.Token;
        _ = RunPlaylistWaveformsAsync(generation, token);
    }

    private void CancelPlaylistWaveforms()
    {
        _playlistWaveGeneration++;
        if (_playlistWaveCts is null)
        {
            return;
        }

        try
        {
            _playlistWaveCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _playlistWaveCts.Dispose();
        _playlistWaveCts = null;
    }

    private bool IsWaveformColumnEffectivelyVisible()
    {
        if (!_columns.TryGetValue(LibraryFileColumn.Waveform, out var column)
            || column.Visibility != Visibility.Visible)
        {
            return false;
        }

        return LibraryColumnFilter.IsEffectivelyVisible(
            LibraryFileColumn.Waveform,
            _visibleColumns,
            CurrentUsedColumns());
    }

    /// <summary>設定の Wave / MP3 それぞれの S/M/L を反映する。</summary>
    public void SetPlaylistWaveformOptions(
        LibraryPlaylistWaveformSize waveSize,
        LibraryPlaylistWaveformSize mp3Size)
    {
        _playlistWaveSizeWave = LibraryPlaylistWaveformSizes.Clamp(waveSize);
        _playlistWaveSizeMp3 = LibraryPlaylistWaveformSizes.Clamp(mp3Size);
        ApplyActiveColumnVisibility(notify: false);
        RefreshPlaylistWaveformSize(reschedule: true);
    }

    private void RefreshPlaylistWaveformSize(bool reschedule)
    {
        var next = LibraryPlaylistWaveformSizes.Resolve(
            _playlistWaveSizeWave,
            _playlistWaveSizeMp3,
            _rows);
        LibraryPlaylistWaveform.SetEffectiveSize(next);
        ApplyWaveformColumnWidth();
        if (reschedule)
        {
            SchedulePlaylistWaveforms();
        }
    }

    private void ApplyWaveformColumnWidth()
    {
        if (!_columns.TryGetValue(LibraryFileColumn.Waveform, out var column))
        {
            return;
        }

        SetColumnPixelWidth(column, LibraryPlaylistWaveform.ColumnWidth);
    }

    public void ApplyCompletedPlaylistWaveform(string path, PeakPyramid peaks)
    {
        if (!IsWaveformColumnEffectivelyVisible())
        {
            return;
        }

        LibraryPlaylistWaveform.SetFromCompletedPeaks(path, peaks);
    }

    private async Task RunPlaylistWaveformsAsync(int generation, CancellationToken cancellationToken)
    {
        // リスト描画と入力を優先。ApplicationIdle のあとで波形を埋め始める。
        try
        {
            await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (generation != _playlistWaveGeneration || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var barCount = LibraryPlaylistWaveform.BarCount;
        var paths = new List<(string Path, PeakPyramid? Peaks)>(_items.Count);
        foreach (var row in _items)
        {
            if (row.Tag is not DocumentSession session
                || session.Document.SourcePath is not { Length: > 0 } path)
            {
                continue;
            }

            if (LibraryPlaylistWaveform.HasEntry(path, barCount))
            {
                continue;
            }

            paths.Add((path, PeakPyramid.CanBuildPlaylistBarsFromPeaks(session.Document.Peaks)
                ? session.Document.Peaks
                : null));
        }

        foreach (var (path, peaks) in paths)
        {
            if (generation != _playlistWaveGeneration || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (LibraryPlaylistWaveform.BarCount != barCount)
            {
                return;
            }

            if (LibraryPlaylistWaveform.HasEntry(path, barCount))
            {
                continue;
            }

            float[] bars;
            try
            {
                bars = await Task.Run(
                    () => LibraryPlaylistWaveform.BuildBars(path, peaks, barCount, cancellationToken),
                    cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                bars = new float[barCount];
            }

            if (generation != _playlistWaveGeneration || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            // 走査中に先読みピークが埋めたら、推定尺の粗い棒で上書きしない。
            LibraryPlaylistWaveform.TrySet(path, barCount, bars);

            // 1 曲ごとに UI へ制御を返す（キー・スクロールを止めない）。
            try
            {
                await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Background);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    private void SchedulePlaylistLoudness()
    {
        CancelPlaylistLoudness();
        if (!IsLoudnessColumnEffectivelyVisible() || _items.Count == 0)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _playlistLoudnessCts = cts;
        var generation = ++_playlistLoudnessGeneration;
        var token = cts.Token;
        _ = RunPlaylistLoudnessAsync(generation, token);
    }

    private void CancelPlaylistLoudness()
    {
        _playlistLoudnessGeneration++;
        if (_playlistLoudnessCts is null)
        {
            return;
        }

        try
        {
            _playlistLoudnessCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _playlistLoudnessCts.Dispose();
        _playlistLoudnessCts = null;
    }

    private bool IsLoudnessColumnEffectivelyVisible()
    {
        if (!_columns.TryGetValue(LibraryFileColumn.Loudness, out var column)
            || column.Visibility != Visibility.Visible)
        {
            return false;
        }

        return LibraryColumnFilter.IsEffectivelyVisible(
            LibraryFileColumn.Loudness,
            _visibleColumns,
            CurrentUsedColumns());
    }

    private async Task RunPlaylistLoudnessAsync(int generation, CancellationToken cancellationToken)
    {
        try
        {
            await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (generation != _playlistLoudnessGeneration || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        // CreateRow でキャッシュ済みは既に Tags へ載っている。ここは未計測の本計測だけ。
        var sessions = new List<DocumentSession>(_items.Count);
        foreach (var row in _items)
        {
            if (row.Tag is not DocumentSession session)
            {
                continue;
            }

            var document = session.Document;
            if (!AudioCodec.IsUncompressedPcm(document.SourceKind)
                || document.SourcePath is not { Length: > 0 } sourcePath)
            {
                continue;
            }

            if (document.Tags.HasFileLoudness)
            {
                if (LibraryMediaCache.TryGetFileLoudness(sourcePath, out _))
                {
                    continue;
                }

                document.ApplyTags(document.Tags.WithoutFileLoudness());
            }

            sessions.Add(session);
        }

        var pending = new List<DocumentSession>(16);
        foreach (var session in sessions)
        {
            if (generation != _playlistLoudnessGeneration || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (!IsLoudnessColumnEffectivelyVisible())
            {
                return;
            }

            var document = session.Document;
            if (document.Tags.HasFileLoudness
                || document.SourcePath is not { Length: > 0 } path)
            {
                continue;
            }

            FileLoudness? measured;
            try
            {
                measured = await Task.Run(
                    () =>
                    {
                        // 行作成後に5項目キャッシュが載った／他経路で埋まった場合。
                        if (LibraryMediaCache.TryGetFileLoudness(path, out var cached))
                        {
                            return (FileLoudness?)cached;
                        }

                        if (!LoudnessMeterEngine.TryMeasureFileLoudness(
                                document,
                                out var value,
                                cancellationToken))
                        {
                            return null;
                        }

                        LibraryMediaCache.PutFileLoudness(path, value);
                        return value;
                    },
                    cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                continue;
            }

            if (generation != _playlistLoudnessGeneration || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (measured is null)
            {
                continue;
            }

            document.ApplyTags(document.Tags.WithFileLoudness(measured.Value));
            pending.Add(session);

            // まとめて行を差し替え、選択の再適用・フォーカス奪取を毎回やらない。
            if (pending.Count < 16)
            {
                continue;
            }

            RefreshLoudnessRows(pending);
            pending.Clear();
            try
            {
                await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Background);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }

        if (pending.Count > 0
            && generation == _playlistLoudnessGeneration
            && !cancellationToken.IsCancellationRequested)
        {
            RefreshLoudnessRows(pending);
        }
    }

    /// <summary>
    /// ラウドネス列だけ更新。列再計算や EnsureListFocused はしない（Shift+上下の選択を壊さない）。
    /// </summary>
    private void RefreshLoudnessRows(IReadOnlyList<DocumentSession> sessions)
    {
        if (sessions.Count == 0)
        {
            return;
        }

        var touch = new HashSet<DocumentSession>(sessions);
        var preserve = SelectedSessions;
        var active = SelectedSession;
        var keepFocus = _grid.IsKeyboardFocusWithin;
        var changed = false;
        var nextRows = _rows.ToArray();
        BeginRowSync();
        try
        {
            for (var i = 0; i < _items.Count; i++)
            {
                if (_items[i].Tag is not DocumentSession session || !touch.Contains(session))
                {
                    continue;
                }

                var next = CreateRowWithDefaults(session);
                next.GroupKey = LibraryFileList.GroupLabel(next, _group);
                if (LibraryFileList.SameContent(_items[i], next))
                {
                    continue;
                }

                for (var r = 0; r < nextRows.Length; r++)
                {
                    if (ReferenceEquals(nextRows[r].Tag, session))
                    {
                        nextRows[r] = next;
                        break;
                    }
                }

                _items[i] = next;
                changed = true;
            }

            if (changed)
            {
                _rows = nextRows;
            }

            if (preserve.Length > 0)
            {
                ApplyRowSelectionCore(active ?? preserve[0], preserve);
            }
        }
        finally
        {
            EndRowSync();
        }

        // フォーカスが外れたときだけ戻す。毎回 Ensure するとキー選択が阻害される。
        if (keepFocus && !_grid.IsKeyboardFocusWithin)
        {
            EnsureListFocused();
        }

        NotePlaylistSelectionChanged();
    }

    private static string CellText(LibraryFileRow row, LibraryFileColumn column) =>
        LibraryFileList.CellText(row, column);

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
            pair.Value.ShowSort(_sortColumn is { } sort && pair.Key == sort, ascending);
        }

        foreach (var item in _columns)
        {
            var next = _sortColumn is { } sort && item.Key == sort
                ? (ListSortDirection?)direction
                : null;
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
                or LibraryFileColumn.Date
                or LibraryFileColumn.Track
                or LibraryFileColumn.Disc
                or LibraryFileColumn.Year
                or LibraryFileColumn.Jacket
                ? LibrarySortDirection.Descending
                : LibrarySortDirection.Ascending;
        }

        ApplySortDirectionToColumns();

        RefreshSortChrome();
        var active = SelectedSession;
        var selected = SelectedSessions;
        _rows = SortRows(_rows);
        LibraryFileList.ApplyGroupKeys(_rows, _group);
        BindRows(active, selected);
        NotifySortChanged();
    }

    private void NotifySortChanged() =>
        SortChanged?.Invoke(this, new LibraryPlaylistSort(_sortColumn, _sortDirection));

    private void ApplySortDirectionToColumns()
    {
        foreach (var item in _columns)
        {
            item.Value.SortDirection = item.Key == _sortColumn
                ? _sortDirection == LibrarySortDirection.Ascending
                    ? System.ComponentModel.ListSortDirection.Ascending
                    : System.ComponentModel.ListSortDirection.Descending
                : null;
        }
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
        NotePlaylistSelectionChanged();
        if (_syncing || _deferActivate || SelectedSession is not { } session)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            _anchorIndex = _grid.SelectedIndex;
        }

        RefreshGroupJacketsForActiveRow();
        SessionActivated?.Invoke(this, session);
    }

    private void Grid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left
            || e.OriginalSource is not DependencyObject origin)
        {
            return;
        }

        // 右クリックメニューを外側クリックで閉じる dismiss は「閉じるだけ」。
        // この貫通クリックが DataGrid の既定選択 → SessionActivated（＝別トラックへ切替・再生）や
        // DnD を起こすと、プレイヤーで選択以外のトラックが消える不具合になる。
        // dismiss クリックはメニューが閉じた直後に届くので _grid.ContextMenu.IsOpen だけでは拾えない。
        // 閉じてからアイドルまで立つ dismiss ガードも見て、確実に握りつぶす（e.Handled で既定選択を止める）。
        if (_grid.ContextMenu is { IsOpen: true } || IsContextMenuDismissGuardActive)
        {
            AbandonPlaylistPendingClick();
            ReleasePlaylistMouseCapture();
            _suppressPlaylistPlainPressComplete = true;
            e.Handled = true;
            return;
        }

        if (IsPlaylistSelectionChrome(origin)
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
            BeginPlaylistPlainPress(row, e.ClickCount);
            _playlistDragStart = e.GetPosition(null);
            if (_playlistPendingSingleSelect is not null)
            {
                e.Handled = true;
                CaptureMouse();
            }

            return;
        }

        ClearPlaylistDrag();

        var index = _grid.Items.IndexOf(row);
        if (index < 0)
        {
            return;
        }

        e.Handled = true;
        ApplyPlaylistModifierClick(index, shift, control);
        EnsureListFocused();
    }

    private void PlaylistHost_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_playlistDragBusy
            || _playlistDragStart is null
            || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var delta = e.GetPosition(null) - _playlistDragStart.Value;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var paths = _playlistDragPaths.Length > 0
            ? _playlistDragPaths
            : SelectedPlaylistCopyPaths();
        if (paths.Length == 0)
        {
            return;
        }

        ClearPlaylistDrag();
        CancelPlaylistFocusRestore();
        _playlistDragBusy = true;
        ReleasePlaylistMouseCapture();
        if (Mouse.Captured is not null)
        {
            Mouse.Capture(null);
        }

        // DataGrid の選択ドラッグと同じマウス処理から同期で入れると、
        // OLE が即キャンセルされる。フォーカス復帰の Loaded も入れ子ループで走る。
        Dispatcher.BeginInvoke(
            () =>
            {
                try
                {
                    if (Mouse.LeftButton != MouseButtonState.Pressed)
                    {
                        return;
                    }

                    if (Mouse.Captured is not null)
                    {
                        Mouse.Capture(null);
                    }

                    BeginFileCopyDrag(this, paths);
                }
                finally
                {
                    _playlistDragBusy = false;
                }
            },
            DispatcherPriority.Input);
    }

    private void PlaylistHost_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // メニュー外クリックで閉じた直後の MouseUp で、保留中の単一選択確定が走らないようにする。
        if (HasOpenContextMenu || _suppressPlaylistPlainPressComplete)
        {
            AbandonPlaylistPendingClick();
            ReleasePlaylistMouseCapture();
            return;
        }

        CompletePlaylistPlainPress();
    }

    private void ClearPlaylistDrag()
    {
        _playlistDragStart = null;
        _playlistDragPaths = [];
        _playlistPendingSingleSelect = null;
    }

    internal void AbandonPlaylistPendingClick() => _playlistPendingSingleSelect = null;

    private void ReleasePlaylistMouseCapture()
    {
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }
    }

    internal void BeginPlaylistPlainPress(LibraryFileRow row, int clickCount)
    {
        _playlistDragPaths = PlaylistCopyPathsFromRow(row);
        _playlistPendingSingleSelect = ShouldHoldPlaylistMultiSelect(row, clickCount)
            ? row
            : null;
    }

    internal void CompletePlaylistPlainPress()
    {
        ReleasePlaylistMouseCapture();
        var pending = _playlistPendingSingleSelect;
        var busy = _playlistDragBusy;
        ClearPlaylistDrag();
        if (pending is null || busy)
        {
            return;
        }

        SelectOnlyPlaylistRow(pending);
    }

    internal bool ShouldHoldPlaylistMultiSelect(LibraryFileRow row, int clickCount) =>
        clickCount == 1
        && _grid.SelectedItems.Count > 1
        && _grid.SelectedItems.Contains(row);

    internal void SelectOnlyPlaylistRow(LibraryFileRow row)
    {
        if (row.Tag is not DocumentSession session)
        {
            return;
        }

        _syncing = true;
        try
        {
            ApplyRowSelectionCore(session, [session]);
        }
        finally
        {
            _syncing = false;
        }

        RefreshGroupJacketsForActiveRow();
        SessionActivated?.Invoke(this, session);
    }

    internal string[] PlaylistCopyPathsFromRow(LibraryFileRow row)
    {
        if (row.Tag is not DocumentSession clicked)
        {
            return [];
        }

        var selected = SelectedSessions;
        DocumentSession[] sessions = selected.Contains(clicked) && selected.Length > 0
            ? selected
            : [clicked];
        return LibraryShellFiles.ExistingPaths(sessions.Select(session => session.Document.SourcePath));
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

        RefreshGroupJacketsForActiveRow();
    }

    private void ToggleSelectionAt(int index)
    {
        if (index < 0 || index >= _grid.Items.Count)
        {
            return;
        }

        var item = _grid.Items[index];
        _syncing = true;
        SuspendPlaylistSelectionNotify();
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
            ResumePlaylistSelectionNotify();
        }
    }

    private void SuspendPlaylistSelectionNotify() => _playlistSelectionNotifyDepth++;

    private void ResumePlaylistSelectionNotify()
    {
        if (_playlistSelectionNotifyDepth > 0)
        {
            _playlistSelectionNotifyDepth--;
        }

        if (_playlistSelectionNotifyDepth == 0 && _playlistSelectionNotifyPending)
        {
            _playlistSelectionNotifyPending = false;
            PlaylistSelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void NotePlaylistSelectionChanged()
    {
        if (_playlistSelectionNotifyDepth > 0)
        {
            _playlistSelectionNotifyPending = true;
            return;
        }

        PlaylistSelectionChanged?.Invoke(this, EventArgs.Empty);
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

    /// <summary>保存済みの列ソート。起動時に使い、変更は通知しない。</summary>
    public void SetSort(LibraryPlaylistSort sort)
    {
        if (_sortColumn == sort.Column
            && (sort.Column is null || _sortDirection == sort.Direction))
        {
            return;
        }

        _sortColumn = sort.Column;
        if (sort.Column is not null)
        {
            _sortDirection = sort.Direction;
        }

        ApplySortDirectionToColumns();
        RefreshSortChrome();
        if (_rows.Count == 0)
        {
            return;
        }

        var active = SelectedSession;
        var selected = SelectedSessions;
        _rows = SortRows(_rows);
        LibraryFileList.ApplyGroupKeys(_rows, _group);
        BindRows(active, selected);
    }

    private void ShuffleCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_shuffleBusy)
        {
            return;
        }

        SetShuffleEnabled(_shuffleCheck.IsChecked == true);
    }

    private void AutoPlayCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_autoPlayBusy)
        {
            return;
        }

        SetAutoPlayEnabled(_autoPlayCheck.IsChecked == true, raise: true);
    }

    private void AutoPlayNextCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_autoPlayNextBusy)
        {
            return;
        }

        SetAutoPlayNextEnabled(_autoPlayNextCheck.IsChecked == true);
    }

    private bool TryPatchBoundRows(IReadOnlyList<LibraryFileRow> rows)
    {
        if (_playlistSearchGroups.Count > 0
            || _grid.ItemsSource is null
            || _items.Count != rows.Count)
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
        var veil = PlayerChrome.Get("PlayerGlowVeilBrush");
        var brush = WpfControlHelpers.FrozenBrush(Color.FromRgb(veil.R, veil.G, veil.B));
        _veil.Background = brush;
        _waveVeil.Background = brush;
        var opacity = veil.A / 255d;
        _veil.Opacity = opacity;
        _waveVeil.Opacity = opacity;
    }

    /// <summary>
    /// ウィンドウ全体を一枚のウォッシュ。プレイヤー中だけ伸ばし、リスト側の別アニメは畳む。
    /// </summary>
    internal void BindWaveformGlow(Grid host)
    {
        _waveGlowHost = host;
        // レイアウト寸法は固定。窓の拡縮はカバー用スケールだけを変え、ビットマップは描き直さない。
        _waveGlow.Width = GlowCacheWidth;
        _waveGlow.Height = GlowCacheHeight;
        var drift = new TransformGroup();
        drift.Children.Add(_waveGlowCover);
        drift.Children.Add(_waveGlowScale);
        drift.Children.Add(_waveGlowTranslate);
        _waveGlow.RenderTransform = drift;
        _waveGlow.RenderTransformOrigin = new Point(0.5, 0.5);
        _waveGlow.CacheMode = CreateGlowBitmapCache();
        _waveGlow.IsHitTestVisible = false;
        _waveGlow.SnapsToDevicePixels = false;
        _waveGlow.UseLayoutRounding = false;
        _waveGlow.Visibility = Visibility.Collapsed;
        host.SizeChanged += (_, _) => SyncWaveGlowCover();
        _waveVeil.IsHitTestVisible = false;
        _waveVeil.Visibility = Visibility.Collapsed;
        if (_waveGlow.Children.Count == 0)
        {
            _waveGlow.Children.Add(_waveGlowFrom);
            _waveGlow.Children.Add(_waveGlowTo);
        }

        _waveGlowCanvas.Children.Add(_waveGlow);
        host.Children.Add(_waveGlowCanvas);
        host.Children.Add(_waveVeil);
        ApplyGlowVeil();
        CopyGlowWash(_glowFrom, _glowTo, _waveGlowFrom, _waveGlowTo);

        PlaceArtworkGlow();
        SyncWaveGlowCover();
        ContinueGlowDrift();
    }

    /// <summary>動画／PDF を窓全体の背面（ウォッシュより前、クロームより後ろ）に置く。</summary>
    internal void BindWindowVisual(Grid host)
    {
        host.IsHitTestVisible = false;
        host.ClipToBounds = true;
        if (_pinnedPdfHost.Children.Count == 0)
        {
            _pinnedPdfHost.Children.Add(_pinnedPdfImage);
            _pinnedPdfHost.Children.Add(_pinnedPdfDim);
        }

        if (!ReferenceEquals(VisualTreeHelper.GetParent(_pinnedPdfHost), host))
        {
            host.Children.Insert(0, _pinnedPdfHost);
        }

        if (!ReferenceEquals(VisualTreeHelper.GetParent(_visualStage), host))
        {
            host.Children.Add(_visualStage);
        }
    }

    internal FrameworkElement VideoHudFrostTarget => _visualStage;

    /// <summary>動画本再生／ミニのメーター・波形・ファイル名の背後を少し暗くする。</summary>
    internal void SetVideoHudFrost(bool on, Geometry? clip) => _visualStage.SetHudFrost(on, clip);

    /// <summary>エディタの背景。プレイヤーのフォールバックウォッシュをウィンドウ全体に出す。</summary>
    internal void UseWindowFallbackWash()
    {
        ClearPdfBackgroundPin();
        _extendGlow = true;
        ApplyArtworkGlow(null);
    }

    /// <summary>プレイヤー表示中はウィンドウ全体に一枚で広げる。編集画面では畳む。</summary>
    internal void SetGlowExtendsWaveform(bool extend)
    {
        if (_extendGlow == extend)
        {
            return;
        }

        _extendGlow = extend;
        PlaceArtworkGlow();
        SyncWaveGlowCover();
        ContinueGlowDrift();
    }

    private bool UseUnifiedGlow => _artworkGlow && _extendGlow && _waveGlowHost is not null;

    private void ApplyArtworkGlow(BitmapSource? bitmap, Brush[]? preparedTurns = null)
    {
        var turns = preparedTurns
            ?? (bitmap is null ? CreateFallbackWashTurns() : CreateAmbientWashTurns(bitmap));
        GlowUsesFallback = bitmap is null;
        var glowChanged = !_artworkGlow;
        _artworkGlow = true;
        ApplyWashTurns(turns);

        PlaceArtworkGlow();
        SyncWaveGlowCover();
        ContinueGlowDrift();

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
        PlayerChrome.Get("PlayerPaneFocusLineBrush", theme);

    internal static Brush CreatePaneFocusLineBrush() =>
        CreatePaneFocusLineBrush(UiThemeService.Painted);

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

    /// <summary>
    /// ドリフトをビットマップ化して、背面ウォッシュの再描画がリストまで汚さない。
    /// </summary>
    private static BitmapCache CreateGlowBitmapCache() => new()
    {
        EnableClearType = false,
        SnapsToDevicePixels = false,
        RenderAtScale = GlowCacheRenderAtScale,
    };

    private void PlaceArtworkGlow()
    {
        // PDF 固定背景は PlayerVisualHost 側。通常のジャケットウォッシュは重ねない。
        if (_pdfBackgroundPinned)
        {
            _pinnedPdfHost.Visibility = Visibility.Visible;
            _glowHost.Visibility = Visibility.Collapsed;
            _veil.Visibility = Visibility.Collapsed;
            HideWaveGlowHost();
            Background = Brushes.Transparent;
            _root.Background = Brushes.Transparent;
            return;
        }

        _pinnedPdfHost.Visibility = Visibility.Collapsed;

        if (_visualStage.IsShown)
        {
            _glowHost.Visibility = Visibility.Collapsed;
            _veil.Visibility = Visibility.Collapsed;
            Background = Brushes.Transparent;
            _root.Background = Brushes.Transparent;
            if (UseUnifiedGlow)
            {
                _waveGlow.Visibility = Visibility.Visible;
                _waveVeil.Visibility = Visibility.Visible;
                _waveGlowHost!.Visibility = Visibility.Visible;
            }
            else
            {
                HideWaveGlowHost();
            }

            return;
        }

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

    /// <summary>
    /// すでに動いているドリフトは止めない。止めると位置が初期値に戻り、キャッシュも描き直される。
    /// アニメの載せ先（窓全体／リスト内）が変わったときだけ付け替える。
    /// </summary>
    private void ContinueGlowDrift()
    {
        var useWave = UseUnifiedGlow;
        if (_glowDriftRunning && _glowDriftUsesWave == useWave)
        {
            SyncWashTurns();
            return;
        }

        if (_glowDriftRunning)
        {
            StopGlowDrift();
        }

        SyncGlowDrift();
        SyncWashTurns();
    }

    /// <summary>ホストサイズに合わせて拡大するだけ。要素寸法は変えない。</summary>
    private void SyncWaveGlowCover()
    {
        if (_waveGlowHost is null)
        {
            return;
        }

        var width = _waveGlowHost.ActualWidth;
        var height = _waveGlowHost.ActualHeight;
        if (width < 1 || height < 1)
        {
            return;
        }

        Canvas.SetLeft(_waveGlow, (width - GlowCacheWidth) / 2);
        Canvas.SetTop(_waveGlow, (height - GlowCacheHeight) / 2);
        var coverX = (width + (GlowDriftBleed * 2)) / GlowCacheWidth;
        var coverY = (height + (GlowDriftBleed * 2)) / GlowCacheHeight;
        var cover = Math.Max(coverX, coverY);
        if (Math.Abs(_waveGlowCover.ScaleX - cover) < 0.0001
            && Math.Abs(_waveGlowCover.ScaleY - cover) < 0.0001)
        {
            return;
        }

        _waveGlowCover.ScaleX = cover;
        _waveGlowCover.ScaleY = cover;
    }

    private void SyncGlowDrift()
    {
        if (_pdfBackgroundPinned)
        {
            if (_glowDriftRunning)
            {
                StopGlowDrift();
            }

            return;
        }

        var run = _artworkGlow && GlowAnimationAlive();
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
        _glowDriftUsesWave = UseUnifiedGlow;
        if (_glowDriftUsesWave)
        {
            AnimateGlowDrift(_waveGlowScale, _waveGlowTranslate);
        }
        else
        {
            AnimateGlowDrift(_glowScale, _glowTranslate);
        }

        _glowDriftRunning = true;
        _glowDriftStartCount++;
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

    private bool GlowAnimationAlive() =>
        IsLoaded && (IsVisible || (_extendGlow && _waveGlowHost is { Visibility: Visibility.Visible }));

    private void SyncWashTurns()
    {
        var run = _artworkGlow && GlowAnimationAlive() && _washTurns.Length > 1;
        if (!run)
        {
            StopWashTurns();
            return;
        }

        // 色の回りも、動いているタイマーはリセットしない。
        if (_washTurnTimer.IsEnabled)
        {
            return;
        }

        StartWashTurns();
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
        AddColorBlob(group, FallbackWashBlue, new Point(0.30, 0.62), 0.46, alpha: 175);
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
            return Enumerable.Repeat(PlayerChrome.Get("PlayerFallbackWashNavyBrush"), count).ToArray();
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
                ? PlayerChrome.Get("PlayerFallbackWashNavyBrush")
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
        // 1トーン落として、背面アニメが前面のUIを食わないようにする。
        l = Math.Clamp(Math.Max(l, lightness * 0.9) - 0.06, 0.22, 0.65);
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

    internal static BitmapSource? TryCreateBitmap(byte[]? bytes, int decodeMaxEdge = 0)
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

    /// <summary>設定のラウドネスターゲット。列の青／黄／赤をやり直す。</summary>
    public void ApplyLoudnessTarget(double targetLufs)
    {
        var target = LoudnessMeterEngine.ClampTargetLufs(targetLufs);
        var seen = new HashSet<LibraryFileRow>();
        PaintLoudnessTones(_rows, target, seen);
        PaintLoudnessTones(_items, target, seen);
    }

    private static void PaintLoudnessTones(
        IEnumerable<LibraryFileRow> rows,
        double target,
        HashSet<LibraryFileRow> seen)
    {
        foreach (var row in rows)
        {
            if (!seen.Add(row))
            {
                continue;
            }

            row.LoudnessTone = LibraryFileRow.ToneFor(row.IntegratedLufs, target);
        }
    }

    /// <summary>設定の既定 Wave フォーマット。違うレート／ビット／Ch をオレンジにする基準。</summary>
    public void SetDefaultAudioFormat(DefaultAudioFormat.Spec format)
    {
        var rate = format.SampleRate;
        var bits = format.BitsPerSample;
        var channels = format.Channels;
        if (_defaultSampleRate == rate
            && _defaultBitsPerSample == bits
            && _defaultChannels == channels)
        {
            return;
        }

        _defaultSampleRate = rate;
        _defaultBitsPerSample = bits;
        _defaultChannels = channels;
        if (_rows.Count == 0)
        {
            return;
        }

        var active = SelectedSession;
        var selected = SelectedSessions;
        var rows = new LibraryFileRow[_rows.Count];
        for (var i = 0; i < _rows.Count; i++)
        {
            rows[i] = _rows[i].Tag is DocumentSession session
                ? CreateRowWithDefaults(session)
                : _rows[i];
        }

        _rows = SortRows(rows);
        LibraryFileList.ApplyGroupKeys(_rows, _group);
        BindRows(active, selected);
    }

    internal static LibraryFileRow CreateRow(DocumentSession session) =>
        CreateRow(
            session,
            DefaultAudioFormat.SampleRate,
            DefaultAudioFormat.BitsPerSample,
            ChannelLayout.Stereo.Channels);

    private LibraryFileRow CreateRowWithDefaults(DocumentSession session)
    {
        var row = CreateRow(session, _defaultSampleRate, _defaultBitsPerSample, _defaultChannels);
        row.CutPending = IsCutPath(session.Document.SourcePath);
        return row;
    }

    internal static LibraryFileRow CreateRow(
        DocumentSession session,
        int defaultSampleRate,
        int defaultBitsPerSample,
        int defaultChannels)
    {
        var document = session.Document;
        var path = document.SourcePath;
        // ディスクキャッシュがあれば行生成時に載せる（非同期1件ずつ差し替えで選択を壊さない）。
        var tags = HydrateIntegratedLufsFromCache(document);
        var deferred = document.IsDeferredLoad;
        // MP3 は Xing と Media Foundation／実デコードがエンコーダ遅延ぶん（数十 ms）ずれる。
        // リストは最初に出したタグ尺のままにする（ホバーやピーク確定でミリ秒が跳ねない）。
        // PDF のページ秒は時計用だけで、時間列には出さない。
        // 読み込み済みの Wave / AIFF は編集後の尺。タグ尺のままだとエディタの結果が残る。
        var livePcm = !deferred
            && document.SourceKind is AudioFileKind.Wave or AudioFileKind.Aiff
            && document.DurationSeconds > 0;
        var duration = LibraryPlaylistDocuments.IsPdf(document)
            ? 0
            : livePcm
                ? document.DurationSeconds
                : tags.DurationSeconds > 0
                    ? tags.DurationSeconds
                    : deferred ? 0 : document.DurationSeconds;
        var tagRateOk = AudioCodec.IsPlausibleAudioSampleRate(tags.SampleRate);
        var hideAudioFormat = LibraryPlaylistDocuments.IsVisual(document) && !tagRateOk;
        // PDF と音声の無い動画の 48kHz/16bit は再生時計用ダミー。列には出さない。
        // 動画はタグの音声フォーマットを優先（ホバー時の MF プローブで列が 0.001kHz 等に跳ねない）。
        var preferTagFormat = deferred || LibraryPlaylistDocuments.IsVideo(document);
        // MP3 / M4A にビット深度は無い（デコード仮定の 16bit を列に出さない）。ビットレートは出す。
        // 動画は非圧縮 PCM 音声（ipcm 等）だけビット深度を出す。圧縮・不明は空欄。
        var hideBitDepth = document.SourceKind is AudioFileKind.Mp3 or AudioFileKind.M4a
            || (LibraryPlaylistDocuments.IsVideo(document)
                && !VideoCodecProbe.IsUncompressedAudio(tags.AudioCodec));
        // WAVE / AIFF の「ビットレート」は PCM から機械的に出るだけなので列は空欄。圧縮音声だけ出す。
        var hideBitRate = document.SourceKind is AudioFileKind.Wave or AudioFileKind.Aiff;
        var sampleRate = hideAudioFormat
            ? 0
            : preferTagFormat && tagRateOk
                ? tags.SampleRate
                : deferred ? 0 : document.SampleRate;
        var bits = hideAudioFormat || hideBitDepth
            ? 0
            : preferTagFormat && tags.BitsPerSample > 0
                ? tags.BitsPerSample
                : deferred ? 0 : document.BitsPerSample;
        var channels = hideAudioFormat
            ? 0
            : preferTagFormat && tags.Channels > 0
                ? tags.Channels
                : deferred ? 0 : document.Channels;
        var bitRate = hideBitRate ? 0 : tags.BitRateKbps;
        if (!hideBitRate && bitRate <= 0 && sampleRate > 0 && channels > 0 && bits > 0)
        {
            bitRate = (int)Math.Round(sampleRate * channels * bits / 1000d);
        }

        var hasArt = document.HasArtwork || tags.HasArtwork;
        var row = new LibraryFileRow
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
            SampleRateNonDefault = LibraryPlayerMode.HighlightsFormatValue(sampleRate, defaultSampleRate),
            BitDepthNonDefault = LibraryPlayerMode.HighlightsFormatValue(bits, defaultBitsPerSample),
            ChannelsNonDefault = LibraryPlayerMode.HighlightsFormatValue(channels, defaultChannels),
            BitRateKbps = bitRate,
            BitRateText = LibraryPlaylistDocuments.IsVideo(document)
                ? UiStrings.FormatVideoBitRate(bitRate)
                : UiStrings.FormatBitRate(bitRate),
            FileBytes = document.FileBytes,
            SizeText = UiStrings.FormatFileBytesCompact(document.FileBytes),
            FileDate = document.FileLastWriteTime ?? default,
            DateText = UiStrings.FormatFileDate(document.FileLastWriteTime),
            Folder = string.IsNullOrEmpty(path)
                ? string.Empty
                : Path.GetDirectoryName(path) ?? string.Empty,
            ParentFolder = LibraryFileList.ParentFolderName(path),
            HasArtwork = hasArt,
            JacketText = hasArt ? UiStrings.LibraryJacketMark : string.Empty,
            VideoWidth = tags.VideoWidth,
            VideoHeight = tags.VideoHeight,
            VideoSizeText = UiStrings.FormatVideoSize(tags.VideoWidth, tags.VideoHeight),
            FrameRate = tags.FrameRate,
            FrameRateText = UiStrings.FormatFrameRate(tags.FrameRate),
            VideoCodec = tags.VideoCodec,
            AudioCodec = tags.AudioCodec,
            IntegratedLufs = AudioCodec.IsUncompressedPcm(document.SourceKind)
                ? tags.IntegratedLufs
                : null,
            LoudnessText = AudioCodec.IsUncompressedPcm(document.SourceKind)
                ? UiStrings.FormatIntegratedLufs(tags.IntegratedLufs)
                : string.Empty,
        };
        row.LoudnessTone = LibraryFileRow.ToneFor(
            row.IntegratedLufs,
            AppStorage.Settings.ResolvedLoudnessTargetLufs());
        ApplyFileNameMarker(row);
        return row;
    }

    /// <summary>
    /// library-media-cache の .lufs を Tags へ載せる。
    /// ファイルのサイズか更新日時がキャッシュと違うときは、メモリ上の値を捨てて測り直す。
    /// </summary>
    internal static AudioFileTags HydrateIntegratedLufsFromCache(AudioDocument document)
    {
        var tags = document.Tags;
        if (!AudioCodec.IsUncompressedPcm(document.SourceKind))
        {
            return tags;
        }

        if (document.SourcePath is not { Length: > 0 } path)
        {
            return tags;
        }

        if (tags.HasFileLoudness && LibraryMediaCache.TryGetFileLoudness(path, out _))
        {
            return tags;
        }

        if (tags.HasFileLoudness)
        {
            tags = tags.WithoutFileLoudness();
            document.ApplyTags(tags);
        }

        if (LibraryMediaCache.TryGetFileLoudness(path, out var loudness))
        {
            document.ApplyTags(tags.WithFileLoudness(loudness));
            return document.Tags;
        }

        if (tags.IntegratedLufs is not null
            || !LibraryMediaCache.TryGetLoudness(path, out var lufs))
        {
            return tags;
        }

        document.ApplyTags(tags.WithIntegratedLufs(lufs));
        return document.Tags;
    }

    internal static string FormatKind(AudioFileKind kind) => LibraryPlaylistSearchTexts.FormatKind(kind);

    internal BitmapSource? ArtworkForGroup(CollectionViewGroup? group)
    {
        if (_group == LibraryFileGroup.None || group?.Name is not { } name)
        {
            return null;
        }

        if (GroupShowsRowVideoJackets(group))
        {
            return null;
        }

        var key = name.ToString() ?? string.Empty;
        var active = _grid.SelectedItem as LibraryFileRow;
        var activeInGroup = LibraryGroupJacketPick.ContainsActive(group.Items, active);
        if (!activeInGroup && _groupJackets.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (!_groupJackets.ContainsKey(key))
        {
            var first = LibraryGroupJacketPick.Resolve(group.Items, active: null);
            _groupJackets[key] = BitmapForGroupRow(first);
        }

        if (activeInGroup)
        {
            return BitmapForGroupRow(LibraryGroupJacketPick.Resolve(group.Items, active));
        }

        return _groupJackets[key];
    }

    private BitmapSource? BitmapForGroupRow(LibraryFileRow? row)
    {
        if (row?.Tag is DocumentSession session
            && session.Document.Artwork is { Length: > 0 } bytes)
        {
            var decoded = TryCreateBitmap(bytes, ArtworkDecodeMaxEdge);
            if (decoded is not null)
            {
                return decoded;
            }
        }

        return null;
    }

    internal static bool GroupShowsRowVideoJackets(CollectionViewGroup group) =>
        LibraryArtworkScan.UsesRowJackets(group.Items);

    internal CollectionViewGroup? PlaylistGroupAt(int index)
    {
        if (_grid.ItemsSource is not ListCollectionView { Groups: { } groups }
            || index < 0
            || index >= groups.Count)
        {
            return null;
        }

        return groups[index] as CollectionViewGroup;
    }

    internal CollectionViewGroup? PlaylistGroupNamed(string name)
    {
        if (_grid.ItemsSource is not ListCollectionView { Groups: { } groups })
        {
            return null;
        }

        foreach (var item in groups)
        {
            if (item is CollectionViewGroup group
                && string.Equals(group.Name?.ToString(), name, StringComparison.Ordinal))
            {
                return group;
            }
        }

        return null;
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

    /// <summary>選択行のジャケットへグループ枠を付け替える。1曲目のキャッシュは残す。</summary>
    private void RefreshGroupJacketsForActiveRow()
    {
        if (_group == LibraryFileGroup.None)
        {
            return;
        }

        GroupArtworkChanged?.Invoke();
        _ = EnsureSessionArtworkAsync(SelectedSession, generation: null);
    }

    private async Task EnsureGroupArtworkAsync()
    {
        var gen = ++_groupArtLoad;
        var pending = new List<(DocumentSession Session, string Path)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var selected = SelectedSession;
        var grouped = _group != LibraryFileGroup.None;
        foreach (var row in _rows)
        {
            if (row.Tag is not DocumentSession session
                || session.Document.HasArtwork
                || !LibraryArtworkScan.CanRead(session.Document.SourceKind)
                || session.Document.SourcePath is not { Length: > 0 } path)
            {
                continue;
            }

            if (!LibraryArtworkScan.ShouldLoad(
                    grouped,
                    session.Document.SourceKind,
                    ReferenceEquals(session, selected),
                    row.GroupKey,
                    seen))
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

            await ApplyEmbeddedArtworkAsync(session, path, gen).ConfigureAwait(true);
        }
    }

    private Task EnsureSessionArtworkAsync(DocumentSession? session, int? generation)
    {
        if (session is null
            || session.Document.HasArtwork
            || !LibraryArtworkScan.CanRead(session.Document.SourceKind)
            || session.Document.SourcePath is not { Length: > 0 } path)
        {
            return Task.CompletedTask;
        }

        return ApplyEmbeddedArtworkAsync(session, path, generation);
    }

    private async Task ApplyEmbeddedArtworkAsync(DocumentSession session, string path, int? generation)
    {
        var kind = session.Document.SourceKind;
        byte[] bytes;
        try
        {
            if (LibraryMediaCache.TryGetArtworkProbe(path, out var hasCached))
            {
                bytes = hasCached && LibraryMediaCache.TryGetArtwork(path, out var cached)
                    ? cached
                    : [];
            }
            else
            {
                bytes = LibraryArtworkScan.IsPdfKind(kind)
                    ? await LibraryPdfPages.RenderPngAsync(path, 0, VideoArtwork.Edge).ConfigureAwait(true) ?? []
                    : await Task.Run(() => ReadEmbeddedArtwork(kind, path)).ConfigureAwait(true);
                var artPath = path;
                if (bytes.Length > 0)
                {
                    var copy = bytes;
                    _ = Task.Run(() => LibraryMediaCache.PutArtwork(artPath, copy));
                }
                else
                {
                    _ = Task.Run(() => LibraryMediaCache.PutNoArtwork(artPath));
                }
            }
        }
        catch (Exception)
        {
            return;
        }
        if (generation is { } gen && gen != _groupArtLoad)
        {
            return;
        }

        if (bytes.Length == 0 || session.Document.HasArtwork)
        {
            return;
        }

        session.Document.SetArtwork(bytes);
        UpdateSessionRow(session);
        if (ReferenceEquals(session, SelectedSession))
        {
            SetArtwork(session.Document);
        }
    }

    private static byte[] ReadEmbeddedArtwork(AudioFileKind kind, string path)
    {
        if (kind == AudioFileKind.M4a)
        {
            return M4aArtwork.TryRead(path, out var art) ? art : [];
        }

        if (LibraryArtworkScan.IsVideoKind(kind))
        {
            return VideoArtwork.TryRead(path, out var frame) ? frame : [];
        }

        return Id3Artwork.TryRead(path, out var mp3) ? mp3 : [];
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
            InputGestureText = "Shift+8",
        };
        expand.Click += (_, _) => ExpandSelectedExplorerSubtree();
        var collapse = new MenuItem
        {
            Header = UiStrings.LibraryMenuCollapseSubtree,
            InputGestureText = "/",
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
            InputGestureText = "V",
        };
        ArmMenuItemClick(loudness, () => MatchLoudnessRequested?.Invoke(this, EventArgs.Empty));
        var mp3 = new MenuItem
        {
            Header = UiStrings.LibraryMenuSaveMp3,
            InputGestureText = "Ctrl+Shift+M",
        };
        ArmMenuItemClick(mp3, () => ExportMp3Requested?.Invoke(this, EventArgs.Empty));
        var previous = new MenuItem
        {
            Header = UiStrings.LibraryMenuPreviousTrack,
            InputGestureText = "P",
        };
        ArmMenuItemClick(previous, () => PlaylistPreviousRequested?.Invoke(this, EventArgs.Empty));
        var next = new MenuItem
        {
            Header = UiStrings.LibraryMenuNextTrack,
            InputGestureText = "N",
        };
        ArmMenuItemClick(next, () => PlaylistNextRequested?.Invoke(this, EventArgs.Empty));
        var restart = new MenuItem
        {
            Header = UiStrings.LibraryMenuRestartTrack,
            InputGestureText = "R",
        };
        ArmMenuItemClick(restart, () => PlaylistRestartRequested?.Invoke(this, EventArgs.Empty));
        var autoPlay = new MenuItem
        {
            Header = UiStrings.LibraryMenuAutoPlay,
            InputGestureText = "Alt+P",
            IsCheckable = true,
        };
        ArmMenuItemClick(autoPlay, () => AutoPlayEnabled = autoPlay.IsChecked == true);
        var clear = new MenuItem { Header = UiStrings.LibraryMenuClearFromPlaylist };
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
        var other = Submenu(
            UiStrings.LibraryMenuCatOther,
            loudness,
            mp3,
            new Separator(),
            copyNames,
            copyPaths,
            explorer);
        var color = FileNameColorMarkerMenu.Create(
            SelectedColorPaths,
            chosen => SetManualFileNameColor(SelectedColorPaths(), chosen));
        menu.Items.Add(color);
        menu.Items.Add(pitch);
        menu.Items.Add(edit);
        menu.Items.Add(other);
        menu.Opened += (_, _) =>
        {
            // メニュー表示中に残った plain-press を捨て、閉じたあとの MouseUp で単一選択確定しない。
            AbandonPlaylistPendingClick();
            ReleasePlaylistMouseCapture();
            _suppressPlaylistPlainPressComplete = true;
            _contextMenuDismissGuard = true;
            ListContextMenuOpened?.Invoke(this, EventArgs.Empty);
            var paths = SelectedPlaylistCopyPaths();
            var selected = SelectedSessions;
            var hasSelection = selected.Length > 0;
            loudness.IsEnabled = hasSelection;
            mp3.IsEnabled = LibraryPlayerMode.HasMp3ExportCandidate(selected);
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
            SyncSubmenuEnabled(pitch, edit, other);
        };
        menu.Closed += (_, _) =>
        {
            AbandonPlaylistPendingClick();
            ReleasePlaylistMouseCapture();
            _suppressPlaylistPlainPressComplete = true;
            ListContextMenuClosed?.Invoke(this, EventArgs.Empty);
            // 外側クリックの dismiss は、閉じたのと同じ入力の中で下のコントロールへ貫通する。
            // ガードはこの入力が終わってアイドルへ落ちるまで維持する（貫通クリックより後に解除）。
            Dispatcher.BeginInvoke(
                () =>
                {
                    _suppressPlaylistPlainPressComplete = false;
                    _contextMenuDismissGuard = false;
                },
                DispatcherPriority.ApplicationIdle);
        };
        _grid.ContextMenu = menu;
    }

    /// <summary>
    /// メニューの外側クリック dismiss が下のコントロールへ貫通するぶんのガードを張る。
    /// 開いてから、閉じた同じ入力が終わってアイドルへ落ちるまで true を維持する。
    /// 波形メニューなど、このビューの外で作ったメニューも同じ貫通を止めるために使う。
    /// </summary>
    internal void ArmContextMenuDismissGuard(ContextMenu menu)
    {
        menu.Opened += (_, _) =>
        {
            _contextMenuDismissDepth++;
            _contextMenuDismissGuard = true;
        };
        menu.Closed += (_, _) =>
        {
            _contextMenuDismissGuard = true;
            Dispatcher.BeginInvoke(
                () =>
                {
                    if (_contextMenuDismissDepth > 0)
                    {
                        _contextMenuDismissDepth--;
                    }

                    if (_contextMenuDismissDepth == 0)
                    {
                        _contextMenuDismissGuard = false;
                    }
                },
                DispatcherPriority.ApplicationIdle);
        };
    }

    /// <summary>
    /// メニュー外クリックの dismiss と MenuItem.Click が競合しないよう、
    /// 項目上でのマウス／Enter／Space だけをコマンド実行とみなす。
    /// </summary>
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
            InputGestureText = "Ctrl++",
        };
        pitchUp.Click += (_, _) => PlaybackPitchUpRequested?.Invoke(this, EventArgs.Empty);
        pitchDown = new MenuItem
        {
            Header = UiStrings.LibraryMenuPitchDown,
            InputGestureText = "Ctrl+-",
        };
        pitchDown.Click += (_, _) => PlaybackPitchDownRequested?.Invoke(this, EventArgs.Empty);
        pitchReset = new MenuItem
        {
            Header = UiStrings.LibraryMenuPitchReset,
            InputGestureText = "Ctrl+0",
        };
        pitchReset.Click += (_, _) => PlaybackPitchResetRequested?.Invoke(this, EventArgs.Empty);
        copyNames = new MenuItem { Header = UiStrings.LibraryMenuCopyFileName };
        copyNames.Click += (_, _) => CopyPathTexts(CopyPathsFor(pane), fullPath: false);
        copyPaths = new MenuItem { Header = UiStrings.LibraryMenuCopyFilePath };
        copyPaths.Click += (_, _) => CopyPathTexts(CopyPathsFor(pane), fullPath: true);
        explorer = new MenuItem { Header = UiStrings.LibraryMenuOpenInExplorer };
        explorer.Click += (_, _) => LibraryShellFiles.TryOpenInExplorer(RevealPathsFor(pane));
    }

    private void CopyPathTexts(string[] paths, bool fullPath)
    {
        var text = fullPath
            ? DocumentFileNames.ClipboardFullPaths(paths)
            : DocumentFileNames.ClipboardFileNames(paths);
        if (text.Length == 0)
        {
            return;
        }

        SystemClipboard.TrySetText(text, Window.GetWindow(this));
    }

    private void BindLibraryCopyCommand(UIElement target, LibraryPane pane)
    {
        target.CommandBindings.Add(new CommandBinding(
            ApplicationCommands.Copy,
            (_, e) =>
            {
                CopyPaths(CopyPathsFor(pane));
                e.Handled = true;
            },
            (_, e) =>
            {
                e.CanExecute = pane != LibraryPane.Favorites && CopyPathsFor(pane).Length > 0;
                e.Handled = true;
            }));
    }

    private string[] SelectedExplorerTransferPaths()
    {
        var paths = SelectedExplorerFolders;
        if (paths.Length == 0)
        {
            return [];
        }

        var walk = SnapshotExplorerPlaylistWalk();
        if (walk.Active)
        {
            paths = LibrarySearchQuery.CollectPlaylistFiles(
                paths,
                walk.Groups,
                walk.Visible,
                walk.Roots,
                walk.TagTexts);
        }

        return LibraryShellFiles.ExistingPaths(paths);
    }

    private string[] SelectedColorPaths()
    {
        var sessions = SelectedSessions;
        if (sessions.Length == 0)
        {
            return [];
        }

        var paths = new List<string>(sessions.Length);
        foreach (var session in sessions)
        {
            if (session.Document.SourcePath is { Length: > 0 } path)
            {
                paths.Add(path);
            }
        }

        return [.. paths];
    }

    private string[] SelectedPlaylistCopyPaths()
    {
        var sessions = SelectedSessions;
        if (sessions.Length == 0)
        {
            return [];
        }

        var paths = new string[sessions.Length];
        for (var i = 0; i < sessions.Length; i++)
        {
            paths[i] = sessions[i].Document.SourcePath ?? string.Empty;
        }

        return LibraryShellFiles.ExistingPaths(paths);
    }

    private static void BeginFileCopyDrag(DependencyObject source, string[] paths)
    {
        var data = LibraryShellFiles.CreateOleCopyData(paths);
        if (data is null)
        {
            return;
        }

        DragDrop.DoDragDrop(source, data, DragDropEffects.Copy);
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
            return;
        }

        _favoritesAnchorIndex = _favoritesList.SelectedIndex;
        _favoritesCaretIndex = _favoritesAnchorIndex;
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
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        _label = new TextBlock
        {
            Text = label,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true,
        };
        // 背面グロー／動画プレビューが動くと ClearType の色付きフリンジが
        // 背景色に追従して約 1px 揺れて見える。グレースケールで止める。
        TextOptions.SetTextRenderingMode(_label, TextRenderingMode.Grayscale);
        TextOptions.SetTextFormattingMode(_label, TextFormattingMode.Display);
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
    public LibraryFavoritesActivateEventArgs(
        IReadOnlyList<string> paths,
        bool clearPlaylist,
        bool recursive = true)
    {
        Paths = paths;
        ClearPlaylist = clearPlaylist;
        Recursive = recursive;
    }

    public IReadOnlyList<string> Paths { get; }

    public bool ClearPlaylist { get; }

    public bool Recursive { get; }
}

internal sealed class LibraryFavoriteRow
{
    public required string Path { get; init; }

    public required string Name { get; init; }

    public bool IsMissing { get; init; }

    public static LibraryFavoriteRow FromPath(string path) =>
        new()
        {
            Path = path,
            Name = LibraryFavoritePaths.DisplayName(path),
            IsMissing = LibraryFavoritePaths.IsMissing(path),
        };
}

/// <summary>ジャケット下の床映り込み（同寸の上下反転＋接点側が濃く下へフェード）。</summary>
internal static class LibraryJacketReflection
{
    /// <summary>鏡面の見える高さ（元画像に対する比率）。</summary>
    internal const double HeightFactor = 0.40;
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

    internal void RefreshMask() => OpacityMask = CreateOpacityMask();

    internal static Brush CreateOpacityMask()
    {
        var peak = Theme.Get("PlayerJacketReflectionBrush");
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1),
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
        };
        brush.GradientStops.Add(new GradientStop(peak, 0));
        brush.GradientStops.Add(new GradientStop(
            Color.FromArgb((byte)(peak.A * 0.45), peak.R, peak.G, peak.B), 0.35));
        brush.GradientStops.Add(new GradientStop(
            Color.FromArgb((byte)(peak.A * 0.12), peak.R, peak.G, peak.B), 0.72));
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        brush.Freeze();
        return brush;
    }
}

/// <summary>グループ時の各動画／PDF 行ジャケット。音楽グループは左の1枚のまま。</summary>
internal sealed class LibraryRowJacketImage : Image
{
    public LibraryRowJacketImage()
    {
        var width = DesignMetrics.LibraryVisualJacketWidth;
        var height = DesignMetrics.LibraryVisualJacketHeight;
        Width = width;
        Height = height;
        MaxWidth = width;
        MaxHeight = height;
        MinWidth = width;
        Stretch = Stretch.Uniform;
        Margin = new Thickness(8, 2, 8, 2);
        SnapsToDevicePixels = true;
        IsHitTestVisible = false;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        DataContextChanged += (_, _) => Reload();
        Loaded += (_, _) => Reload();
    }

    private void Reload()
    {
        if (DataContext is not LibraryFileRow row
            || !LibraryColumnFilter.IsVisualFamilyKind(LibraryColumnFilter.EffectiveKind(row)))
        {
            Source = null;
            MinHeight = 0;
            Height = 0;
            Visibility = Visibility.Collapsed;
            return;
        }

        MinHeight = DesignMetrics.LibraryVisualJacketHeight;
        Height = DesignMetrics.LibraryVisualJacketHeight;
        Visibility = Visibility.Visible;
        if (row.Tag is DocumentSession session
            && session.Document.Artwork is { Length: > 0 } bytes)
        {
            var decoded = LibraryBrowserView.TryCreateBitmap(bytes, 256);
            if (decoded is not null)
            {
                Source = decoded;
                return;
            }
        }

        Source = null;
        MinHeight = 0;
        Height = 0;
        Visibility = Visibility.Collapsed;
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
        var art = owner?.ArtworkForGroup(DataContext as CollectionViewGroup);
        if (art is null)
        {
            _face.Source = null;
            _reflection.Source = null;
            Visibility = Visibility.Collapsed;
            return;
        }

        _face.Source = art;
        _reflection.Source = art;
        _reflection.RefreshMask();
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
