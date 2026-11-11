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

/// <summary>F10 上段。左フォルダツリー、中央リスト。ジャケットは背面のぼかしだけ。</summary>
internal sealed partial class LibraryBrowserView : UserControl
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
    /// <summary>
    /// 全画面ウォッシュのキャッシュ解像度。1 だとクロスフェードのたびに巨大ビットマップを作り直す。
    /// 上げると階調の帯が減るが、ピクセル数は二乗で増える。
    /// </summary>
    internal const double GlowCacheRenderAtScale = 0.5;
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

    /// <summary>ペインフォーカスの白いグラデ帯。左上・右上だけ既定の GUI 角丸。</summary>
    internal static CornerRadius PaneFocusTopCorners(double? radius = null) =>
        SelectionBandCorners.Of(SelectionBandEdge.First, radius ?? DesignMetrics.ControlCornerRadius);
    internal const string PlaylistRowBandName = "PlaylistRowBand";

    /// <summary>ツリー子のインデント。選択帯をルート左端へ揃える基準。</summary>
    internal const double TreeChildIndent = 16;
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
    /// <summary>
    /// ドリフトするウォッシュの上。比率を保って中央で画面を覆う。タイルにはしない。
    /// </summary>
    private readonly Image _wallpaper = new()
    {
        Stretch = Stretch.UniformToFill,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
        IsHitTestVisible = false,
        Opacity = 0,
        Visibility = Visibility.Collapsed,
        SnapsToDevicePixels = false,
        UseLayoutRounding = false,
    };
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
    private TreeViewItem? _explorerCaret;
    private LibraryFileRow? _playlistCaretRow;
    /// <summary>F4。プレイリストのみ。オンのとき行頭にチェックボックスを出し、操作対象はチェック行。ツリーには出さない。</summary>
    private bool _checkSelectMode;
    private DataGridTemplateColumn? _checkColumn;
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
    private readonly MonitorLatchButton _shuffleLatch = MonitorLatchButton.CreateOption(UiStrings.LabelLibraryShuffle);
    private readonly MonitorLatchButton _autoPlayLatch = MonitorLatchButton.CreateOption(UiStrings.LabelLibraryAutoPlay);
    private readonly MonitorLatchButton _autoPlayNextLatch = MonitorLatchButton.CreateOption(UiStrings.LabelLibraryAutoPlayNext);
    private readonly MonitorLatchButton _checkSelectLatch = MonitorLatchButton.CreateOption(UiStrings.LabelLibraryCheckSelect);
    private readonly LibraryPlaylistShuffle _shuffle = new();
    private bool _shuffleEnabled;
    private bool _shuffleBusy;
    private bool _autoPlayEnabled = true;
    private bool _autoPlayBusy;
    private bool _autoPlayNextEnabled;
    private bool _autoPlayNextBusy;
    private bool _checkSelectBusy;
    private bool _autoLoopEnabled;
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
    private readonly AcrylicToastHost _transferToast = new();
    private readonly AcrylicToastHost _explorerTypeaheadToast = new();
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
    /// <summary>直前の範囲選択キャレット（ファイル行インデックス）。差分更新用。無効は -1。</summary>
    private int _playlistRangeCaretIndex = -1;
    private List<LibraryFileRow>? _playlistFilesSnapshot;
    private int _playlistFilesSnapshotGeneration = -1;
    private ScrollViewer? _playlistScrollViewer;
    /// <summary>選択帯用。DataGrid.SelectedItems には入れない。</summary>
    private readonly HashSet<LibraryFileRow> _playlistSelectedRows = [];
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
    /// プレイヤー切替ボタン）へクリックが貫通する。メニューが開いてから、閉じた同じ左クリックが
    /// 上がるまで立てておき、その貫通でプレイヤーを抜けて（＝選択以外のトラックが消えて）しまうのを防ぐ。
    /// </summary>
    private bool _contextMenuDismissGuard;
    /// <summary>Closed のたびに進め、古い Idle コールバックが後からガードを外さないようにする。</summary>
    private int _contextMenuDismissReleaseEpoch;
    /// <summary>左ボタン待ちタイマーが再評価する epoch。</summary>
    private int _contextMenuDismissWatchEpoch;
    private bool _contextMenuDismissLeftUpWatching;
    private readonly DispatcherTimer _contextMenuDismissLeftUpTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(16),
    };
    /// <summary>リストメニュー Closed 後、ガード解除と同時に ListContextMenuClosed を飛ばす。</summary>
    private bool _listContextMenuDismissPending;

    public event EventHandler<DocumentSession>? SessionActivated;

    /// <summary>プレイリストの選択集合が変わった。複数選択の平均ラウドネス用。</summary>
    public event EventHandler? PlaylistSelectionChanged;

    /// <summary>ダブルクリック。停止中でも再生する。</summary>
    public event EventHandler<DocumentSession>? SessionPlayRequested;

    public event EventHandler? PlaylistVisualEnded;

    public event EventHandler<TimeSpan>? PlaylistVisualOpened;

    /// <summary>プレイリスト動画を開けなかった。引数はダイアログ本文。</summary>
    public event EventHandler<string>? PlaylistVisualOpenFailed;

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

    /// <summary>プレイリスト右クリックから波形メニュー相当の編集・書き出しを依頼する。</summary>
    public event EventHandler<WaveMenuCommand>? PlaylistWaveMenuRequested;

    /// <summary>
    /// プレイリスト右クリックの有効状態用モデル。未設定時はプレイヤー相当のローカル推定。
    /// ウィンドウメニューと同じ <see cref="WaveformContextMenuBuilder.Build"/> ツリーに揃える。
    /// </summary>
    internal Func<WaveformContextMenuModel>? ResolvePlaylistWaveMenuModel { get; set; }

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
        _visualStage.OpenFailed += (_, message) => PlaylistVisualOpenFailed?.Invoke(this, message);
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
        PreviewMouseLeftButtonDown += LibraryBrowser_PreviewMouseLeftButtonDownForDismissGuard;
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
        _contextMenuDismissLeftUpTimer.Tick += (_, _) =>
            ContinueContextMenuDismissGuardRelease(_contextMenuDismissWatchEpoch);
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
            StopContextMenuDismissLeftUpWatcher();
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
    /// 右クリックメニューが開いている間と、外側クリックで閉じた同じ左クリックが上がるまでは true。
    /// 外側クリックが下のコントロールへ貫通してプレイヤーを抜けてしまうのを呼び出し側で弾くために使う。
    /// </summary>
    public bool IsContextMenuDismissGuardActive =>
        _contextMenuDismissGuard || HasOpenContextMenu;

    /// <summary>メニュー閉鎖直後〜 dismiss 左クリック解放まで。MouseUp での単一選択確定を抑止中か。</summary>
    internal bool IsPlaylistPlainPressSuppressedAfterMenu => _suppressPlaylistPlainPressComplete;

    /// <summary>
    /// テスト専用。null 以外なら <see cref="Mouse.LeftButton"/> の代わりに dismiss ガードの
    /// 「左ボタン押下中」判定へ使う（Idle 後も押下中ならガードが残ることを検証する）。
    /// </summary>
    internal Func<bool>? DismissGuardLeftHeldForTests { get; set; }

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

    public DocumentSession? SelectedSession
    {
        get
        {
            if (_playlistCaretRow is { Tag: DocumentSession caret }
                && _playlistCaretRow.IsPlaylistSelected)
            {
                return caret;
            }

            foreach (var row in _playlistSelectedRows)
            {
                if (row.Tag is DocumentSession selected)
                {
                    return selected;
                }
            }

            return _grid.SelectedItem is LibraryFileRow { Tag: DocumentSession fromGrid }
                ? fromGrid
                : null;
        }
    }

    /// <summary>チェック選択モード。初期オフ。設定には記憶せず、プレイヤー退出でオフへ戻す。</summary>
    public bool CheckSelectMode => _checkSelectMode;

    public event EventHandler? CheckSelectModeChanged;

    public DocumentSession[] SelectedSessions
    {
        get
        {
            // チェックモード中は右クリック／一括操作の対象をチェック行にする。
            if (_checkSelectMode)
            {
                return CheckedPlaylistSessions();
            }

            if (_playlistSelectedRows.Count == 0)
            {
                return [];
            }

            // 表示順で返す。
            var result = new List<DocumentSession>(_playlistSelectedRows.Count);
            foreach (var row in PlaylistRows())
            {
                if (row.IsPlaylistSelected && row.Tag is DocumentSession session)
                {
                    result.Add(session);
                }
            }

            return [.. result];
        }
    }

    private void SetPlaylistRowSelected(LibraryFileRow row, bool selected)
    {
        if (selected)
        {
            _playlistSelectedRows.Add(row);
            row.IsPlaylistSelected = true;
            return;
        }

        _playlistSelectedRows.Remove(row);
        row.IsPlaylistSelected = false;
    }

    private void ClearPlaylistRowSelectionFlags()
    {
        if (_playlistSelectedRows.Count == 0)
        {
            return;
        }

        foreach (var row in _playlistSelectedRows)
        {
            row.IsPlaylistSelected = false;
        }

        _playlistSelectedRows.Clear();
    }

    /// <summary>帯用フラグを揃え、DataGrid にはキャレット1件だけ載せる。</summary>
    private void SyncPlaylistCaretToGrid(LibraryFileRow? caret)
    {
        _playlistCaretRow = caret;
        if (caret is null)
        {
            if (_grid.SelectedItem is not null)
            {
                _grid.SelectedItem = null;
            }

            return;
        }

        if (!ReferenceEquals(_grid.SelectedItem, caret))
        {
            _grid.SelectedItem = caret;
        }
    }

    /// <summary>チェック選択モードのトグル（Shift+Space／Check Box）。</summary>
    public void ToggleCheckSelectMode()
    {
        SetCheckSelectMode(!_checkSelectMode);
    }

    /// <param name="raise">
    /// false なら変更しても <see cref="CheckSelectModeChanged"/> を出さない
    /// （プレイヤー退出時の自動オフなど）。
    /// </param>
    public void SetCheckSelectMode(bool on, bool raise = true)
    {
        if (_checkSelectMode == on)
        {
            SyncCheckSelectCheck();
            return;
        }

        _checkSelectMode = on;
        if (_checkColumn is not null)
        {
            _checkColumn.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }

        SyncCheckSelectCheck();

        if (!on)
        {
            // チェック行を選択として残してから、チェック状態は忘れる。
            // オンに戻したときは空のチェックから始まる。ツリーは対象外。
            ApplyPlaylistSelectionFromChecks();
            ClearAllPlaylistChecks();
        }

        if (raise)
        {
            CheckSelectModeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SyncCheckSelectCheck()
    {
        if (_checkSelectLatch.IsLatched == _checkSelectMode)
        {
            return;
        }

        _checkSelectBusy = true;
        try
        {
            _checkSelectLatch.IsLatched = _checkSelectMode;
        }
        finally
        {
            _checkSelectBusy = false;
        }
    }

    /// <summary>チェック済みプレイリスト行を DataGrid 選択へ載せる（F4 オフ時）。</summary>
    private void ApplyPlaylistSelectionFromChecks()
    {
        var checkedRows = new List<LibraryFileRow>();
        foreach (var row in PlaylistRows())
        {
            if (row.IsChecked)
            {
                checkedRows.Add(row);
            }
        }

        if (checkedRows.Count == 0)
        {
            return;
        }

        _syncing = true;
        SuspendPlaylistSelectionNotify();
        try
        {
            ClearPlaylistRowSelectionFlags();
            foreach (var row in checkedRows)
            {
                SetPlaylistRowSelected(row, true);
            }

            var caret = checkedRows[^1];
            SyncPlaylistCaretToGrid(caret);
            _playlistRangeCaretIndex = -1;
            _anchorIndex = _grid.Items.IndexOf(caret);
            PinSelectionCurrent();
        }
        finally
        {
            _syncing = false;
            ResumePlaylistSelectionNotify();
        }
    }

    /// <summary>
    /// Space。複数選択中ならその範囲をすべてチェック、単一ならカーソル行をトグル。
    /// プレイリスト専用（ツリーでは呼ばない）。
    /// </summary>
    public void ApplyPlaylistSpaceCheck()
    {
        if (!_checkSelectMode)
        {
            return;
        }

        if (_playlistSelectedRows.Count > 1)
        {
            // 範囲がすべてチェック済みなら外す。そうでなければすべて入れる。
            var allChecked = true;
            foreach (var row in _playlistSelectedRows)
            {
                if (!row.IsChecked)
                {
                    allChecked = false;
                    break;
                }
            }

            var next = !allChecked;
            foreach (var row in _playlistSelectedRows)
            {
                row.IsChecked = next;
            }

            return;
        }

        // 単一／カーソル行はトグル（チェック済みなら外す）。
        var caret = ResolvePlaylistCaretRow();
        if (caret is null)
        {
            return;
        }

        caret.IsChecked = !caret.IsChecked;
        _playlistCaretRow = caret;
        if (!ReferenceEquals(_grid.CurrentItem, caret))
        {
            _grid.CurrentItem = caret;
        }

        EnsureRowVisible(caret);
    }

    private DocumentSession[] CheckedPlaylistSessions()
    {
        var result = new List<DocumentSession>();
        foreach (var row in PlaylistRows())
        {
            if (row.IsChecked && row.Tag is DocumentSession session)
            {
                result.Add(session);
            }
        }

        return [.. result];
    }

    private void ClearAllPlaylistChecks()
    {
        foreach (var row in _items)
        {
            row.IsChecked = false;
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

    /// <summary>
    /// プロキシ作り直しの前に、DXGI が表示中の既存プロキシを閉じる（上書きできるように）。
    /// 閉じたら true。後で <see cref="ShowPlaylistVisual"/> を呼ぶと同じパスでも開き直す。
    /// </summary>
    public bool ReleasePlaylistVideoProxyForRewrite(string sourcePath) =>
        IsPlaylistVisualSameSource(sourcePath)
        && VideoProxy.TryGetCached(sourcePath, out var proxy)
        && _visualStage.ReleaseDxgiSourceForRewrite(proxy);

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

    /// <summary>ドラッグスクラブ用。再生中シークの蓄積を避ける。</summary>
    /// <param name="scrubPreview">ドラッグ中の粗いプレビュー。静止後に false で正確な絵。</param>
    public void ScrubPlaylistVisual(TimeSpan position, bool scrubPreview = false) =>
        _visualStage.ScrubTo(position, scrubPreview);

    public void SetPlaylistVisualSpeed(double ratio) => _visualStage.SetSpeedRatio(ratio);

    public bool TryStepPlaylistPdfPage(int delta) => _visualStage.TryStepPdfPage(delta);

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

    /// <summary>曲末で先頭へ戻してループするか。既定オフ。設定に覚える。Auto Next より優先。</summary>
    public bool AutoLoopEnabled
    {
        get => _autoLoopEnabled;
        set => SetAutoLoopEnabled(value);
    }

    public event EventHandler? AutoLoopChanged;

    public void ToggleAutoLoop() => AutoLoopEnabled = !AutoLoopEnabled;

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
        if (!changed && _shuffleLatch.IsLatched == enabled)
        {
            return;
        }

        _shuffleEnabled = enabled;
        if (changed)
        {
            _shuffle.Clear();
        }

        if (_shuffleLatch.IsLatched != enabled)
        {
            _shuffleBusy = true;
            try
            {
                _shuffleLatch.IsLatched = enabled;
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
        if (!changed && _autoPlayLatch.IsLatched == enabled)
        {
            return;
        }

        _autoPlayEnabled = enabled;

        if (_autoPlayLatch.IsLatched != enabled)
        {
            _autoPlayBusy = true;
            try
            {
                _autoPlayLatch.IsLatched = enabled;
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
        if (!changed && _autoPlayNextLatch.IsLatched == enabled)
        {
            return;
        }

        _autoPlayNextEnabled = enabled;

        if (_autoPlayNextLatch.IsLatched != enabled)
        {
            _autoPlayNextBusy = true;
            try
            {
                _autoPlayNextLatch.IsLatched = enabled;
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

    private void SetAutoLoopEnabled(bool enabled)
    {
        if (_autoLoopEnabled == enabled)
        {
            return;
        }

        _autoLoopEnabled = enabled;
        AutoLoopChanged?.Invoke(this, EventArgs.Empty);
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

    /// <summary>
    /// 表示順で <paramref name="anchor"/> の前後（削除対象を除く）。次グループ先頭を優先するため next を先に見る。
    /// </summary>
    public bool TryGetDisplayedNeighbors(
        DocumentSession anchor,
        HashSet<DocumentSession> dropSet,
        out DocumentSession? next,
        out DocumentSession? prev)
    {
        next = null;
        prev = null;
        DocumentSession? seen = null;
        var foundAnchor = false;
        foreach (var row in PlaylistDisplayRows())
        {
            if (row.Tag is not DocumentSession item)
            {
                continue;
            }

            if (ReferenceEquals(item, anchor) || dropSet.Contains(item))
            {
                if (ReferenceEquals(item, anchor))
                {
                    foundAnchor = true;
                }

                continue;
            }

            if (!foundAnchor)
            {
                seen = item;
                continue;
            }

            next = item;
            break;
        }

        prev = seen;
        return foundAnchor || next is not null || prev is not null;
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

    /// <summary>
    /// 遅延のツリー／リスト Focus 予約を無効化する（上部メニューへフォーカスを渡すときなど）。
    /// </summary>
    internal void CancelPendingFocusRequests()
    {
        _explorerFocusTicket++;
        _listFocusTicket++;
        ReleasePlaylistFocusHold();
        CancelPlaylistFocusRestore();
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
        if (_folderTree.ContextMenu is { IsOpen: true } explorer)
        {
            explorer.IsOpen = false;
        }

        if (_favoritesList.ContextMenu is { IsOpen: true } favorites)
        {
            favorites.IsOpen = false;
        }

        if (_grid.ContextMenu is { IsOpen: true } list)
        {
            list.IsOpen = false;
        }

        if (_columnHeaderMenu is { IsOpen: true } columns)
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

    /// <summary>アプリ終了時など、開いている Acrylic トーストを即閉じる。</summary>
    public void HideAcrylicToasts()
    {
        _transferToast.Hide(instant: true);
        HideExplorerTypeaheadToast();
    }

    /// <summary>10件までは表示名を1行ずつ、11件以上は件数だけ。</summary>
    public void ShowTransferToast(string action, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        EnsureTransferToastHost();
        var detail = paths.Count <= 10
            ? string.Join(Environment.NewLine, paths.Select(TransferDisplayName))
            : $"{paths.Count} items";
        var text = paths.Count is > 1 and <= 10
            ? action + Environment.NewLine + detail
            : $"{action}: {detail}";
        _transferToast.Show(text);
    }

    private void EnsureTransferToastHost()
    {
        if (_transferToast.PlacementTarget is not null)
        {
            return;
        }

        // コピー／切り取り等のファイル一覧は左寄せ。
        _transferToast.TextAlignment = TextAlignment.Left;
        _transferToast.Padding = DesignMetrics.PlaylistVideoTimecodePadding;
        _transferToast.FontSize = DesignMetrics.From96(22);
        // ウィンドウ中央（ライブラリ領域中央だと上寄りになる）。
        var window = Window.GetWindow(this);
        _transferToast.PlacementTarget = window?.Content as UIElement ?? _root;
        _transferToast.Placement = PlacementMode.Center;
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

            if (PathUtil.TryNormalizePrefix(path, out var full))
            {
                _cutPaths.Add(full);
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

        return PathUtil.TryNormalizePrefix(path, out var full) && _cutPaths.Contains(full);
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

        var files = SnapshotPlaylistFiles();
        var count = files.Count;
        if (count <= 0)
        {
            return;
        }

        var index = PlaylistFileIndexOfSelected(files);
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

        ApplyMoveSelectionFile(index, extend, files);
    }

    /// <summary>
    /// グループ境界へジャンプ。Ctrl+Shift+↑↓（extend）は現グループの端まで選択し、
    /// 既に端なら隣グループの端へ伸ばす。非 extend は隣グループ先頭へ。
    /// </summary>
    public void MoveSelectionByGroup(int direction, bool extend = false)
    {
        if (direction == 0)
        {
            return;
        }

        var files = SnapshotPlaylistFiles();
        var count = files.Count;
        if (count <= 0)
        {
            return;
        }

        var index = PlaylistFileIndexOfSelected(files);
        if (index < 0)
        {
            index = direction > 0 ? 0 : count - 1;
        }
        else
        {
            index = extend
                ? FindPlaylistGroupExtendIndex(files, index, direction)
                : FindPlaylistGroupEdgeIndex(files, index, direction);
        }

        ApplyMoveSelectionFile(index, extend, files);
    }

    public void MoveSelectionToEdge(int edge, bool extend = false)
    {
        var files = SnapshotPlaylistFiles();
        var index = LibraryPlayerMode.EdgeIndex(files.Count, edge);
        if (index < 0)
        {
            return;
        }

        ApplyMoveSelectionFile(index, extend, files);
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

    /// <summary>Shift+Page キーリピートのまとめ用。表示行数に応じた1ページ分の行数。</summary>
    internal int PlaylistPageStepRows() => LibraryPlayerMode.PageStep(VisibleRowCount());

    private List<LibraryFileRow> SnapshotPlaylistFiles()
    {
        if (_playlistFilesSnapshot is not null
            && _playlistFilesSnapshotGeneration == _syncGeneration)
        {
            return _playlistFilesSnapshot;
        }

        var files = new List<LibraryFileRow>(_items.Count);
        foreach (var row in PlaylistRows())
        {
            if (row.Tag is DocumentSession)
            {
                files.Add(row);
            }
        }

        _playlistFilesSnapshot = files;
        _playlistFilesSnapshotGeneration = _syncGeneration;
        return files;
    }

    private int PlaylistFileIndexOfSelected(IReadOnlyList<LibraryFileRow> files)
    {
        // 範囲選択中は SelectedItem を動かさない（代入すると Extended で全選択が潰れる）。
        // キャレット行を優先して次の Shift+↑↓ 位置を決める。
        if (_playlistCaretRow is not null)
        {
            for (var i = 0; i < files.Count; i++)
            {
                if (ReferenceEquals(files[i], _playlistCaretRow))
                {
                    return i;
                }
            }
        }

        var selected = SelectedSession;
        if (selected is null)
        {
            return -1;
        }

        for (var i = 0; i < files.Count; i++)
        {
            if (files[i].Tag is DocumentSession session && ReferenceEquals(session, selected))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>隣グループの先頭（↓／↑とも）へ。非 extend のジャンプ用。</summary>
    private static int FindPlaylistGroupEdgeIndex(
        IReadOnlyList<LibraryFileRow> files,
        int index,
        int direction)
    {
        index = Math.Clamp(index, 0, files.Count - 1);
        var key = files[index].GroupKey ?? string.Empty;
        if (direction > 0)
        {
            for (var i = index + 1; i < files.Count; i++)
            {
                if (!string.Equals(files[i].GroupKey ?? string.Empty, key, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return files.Count - 1;
        }

        for (var i = index - 1; i >= 0; i--)
        {
            if (!string.Equals(files[i].GroupKey ?? string.Empty, key, StringComparison.Ordinal))
            {
                // 前グループの先頭へ（そのグループ内をさらに遡る）。
                var prevKey = files[i].GroupKey ?? string.Empty;
                while (i > 0
                    && string.Equals(files[i - 1].GroupKey ?? string.Empty, prevKey, StringComparison.Ordinal))
                {
                    i--;
                }

                return i;
            }
        }

        return 0;
    }

    /// <summary>
    /// 範囲選択用。現グループの端（↓＝末尾／↑＝先頭）へ。
    /// 既に端なら隣グループの同じ側の端へ伸ばす（次グループ先頭は含めない）。
    /// </summary>
    private static int FindPlaylistGroupExtendIndex(
        IReadOnlyList<LibraryFileRow> files,
        int index,
        int direction)
    {
        index = Math.Clamp(index, 0, files.Count - 1);
        var key = files[index].GroupKey ?? string.Empty;
        if (direction > 0)
        {
            var last = index;
            for (var i = index + 1; i < files.Count; i++)
            {
                if (!string.Equals(files[i].GroupKey ?? string.Empty, key, StringComparison.Ordinal))
                {
                    break;
                }

                last = i;
            }

            if (last > index)
            {
                return last;
            }

            if (index >= files.Count - 1)
            {
                return index;
            }

            var nextKey = files[index + 1].GroupKey ?? string.Empty;
            var nextLast = index + 1;
            for (var i = index + 2; i < files.Count; i++)
            {
                if (!string.Equals(files[i].GroupKey ?? string.Empty, nextKey, StringComparison.Ordinal))
                {
                    break;
                }

                nextLast = i;
            }

            return nextLast;
        }

        var first = index;
        for (var i = index - 1; i >= 0; i--)
        {
            if (!string.Equals(files[i].GroupKey ?? string.Empty, key, StringComparison.Ordinal))
            {
                break;
            }

            first = i;
        }

        if (first < index)
        {
            return first;
        }

        if (index <= 0)
        {
            return index;
        }

        var prevKey = files[index - 1].GroupKey ?? string.Empty;
        var prevFirst = index - 1;
        for (var i = index - 2; i >= 0; i--)
        {
            if (!string.Equals(files[i].GroupKey ?? string.Empty, prevKey, StringComparison.Ordinal))
            {
                break;
            }

            prevFirst = i;
        }

        return prevFirst;
    }

    private void ApplyMoveSelectionFile(
        int index,
        bool extend,
        IReadOnlyList<LibraryFileRow>? files = null)
    {
        files ??= SnapshotPlaylistFiles();
        if ((uint)index >= (uint)files.Count)
        {
            return;
        }

        var row = files[index];
        _deferActivate = true;
        try
        {
            if (!extend)
            {
                _anchorIndex = index;
                _playlistRangeCaretIndex = -1;
                _syncing = true;
                SuspendPlaylistSelectionNotify();
                try
                {
                    ClearPlaylistRowSelectionFlags();
                    SetPlaylistRowSelected(row, true);
                    SyncPlaylistCaretToGrid(row);
                }
                finally
                {
                    _syncing = false;
                    ResumePlaylistSelectionNotify();
                }
            }
            else
            {
                SelectPlaylistFileRange(_anchorIndex, index, files);
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

        EnsureListFocused(pinCurrent: false);
        EnsureRowVisible(row);
    }

    private LibraryFileRow? ResolvePlaylistCaretRow()
    {
        if (_playlistCaretRow is { Tag: DocumentSession })
        {
            if (_playlistRangeCaretIndex >= 0
                || _playlistCaretRow.IsPlaylistSelected
                || ReferenceEquals(_grid.SelectedItem, _playlistCaretRow))
            {
                return _playlistCaretRow;
            }
        }

        if (_grid.CurrentItem is LibraryFileRow { Tag: DocumentSession } current)
        {
            return current;
        }

        return _grid.SelectedItem as LibraryFileRow;
    }

    private void SelectPlaylistFileRange(
        int from,
        int to,
        IReadOnlyList<LibraryFileRow>? files = null)
    {
        files ??= SnapshotPlaylistFiles();
        var count = files.Count;
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
            if (_playlistRangeCaretIndex < 0
                || !TryAdjustPlaylistFileRange(files, from, _playlistRangeCaretIndex, to))
            {
                SyncPlaylistFileRangeFull(files, lo, hi);
            }

            _playlistRangeCaretIndex = to;
            SyncPlaylistCaretToGrid(files[to]);
        }
        finally
        {
            _syncing = false;
            ResumePlaylistSelectionNotify();
        }
    }

    /// <summary>
    /// 同じアンカーからの連続範囲変更。端の増減だけ触る。前提が崩れていれば false。
    /// </summary>
    private bool TryAdjustPlaylistFileRange(
        IReadOnlyList<LibraryFileRow> files,
        int from,
        int oldTo,
        int newTo)
    {
        if ((uint)oldTo >= (uint)files.Count || oldTo == newTo)
        {
            return oldTo == newTo && (uint)oldTo < (uint)files.Count;
        }

        var oldLo = Math.Min(from, oldTo);
        var oldHi = Math.Max(from, oldTo);
        var expected = oldHi - oldLo + 1;
        if (_playlistSelectedRows.Count != expected)
        {
            return false;
        }

        var newLo = Math.Min(from, newTo);
        var newHi = Math.Max(from, newTo);

        for (var i = oldHi; i > newHi; i--)
        {
            SetPlaylistRowSelected(files[i], false);
        }

        for (var i = oldLo; i < newLo; i++)
        {
            SetPlaylistRowSelected(files[i], false);
        }

        for (var i = oldHi + 1; i <= newHi; i++)
        {
            SetPlaylistRowSelected(files[i], true);
        }

        for (var i = newLo; i < oldLo; i++)
        {
            SetPlaylistRowSelected(files[i], true);
        }

        return _playlistSelectedRows.Count == newHi - newLo + 1;
    }

    private void SyncPlaylistFileRangeFull(IReadOnlyList<LibraryFileRow> files, int lo, int hi)
    {
        var want = new HashSet<LibraryFileRow>();
        for (var i = lo; i <= hi; i++)
        {
            want.Add(files[i]);
        }

        if (_playlistSelectedRows.Count > 0)
        {
            foreach (var row in _playlistSelectedRows.ToArray())
            {
                if (!want.Contains(row))
                {
                    SetPlaylistRowSelected(row, false);
                }
            }
        }

        for (var i = lo; i <= hi; i++)
        {
            SetPlaylistRowSelected(files[i], true);
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

    /// <summary>選択は変えず、表示だけ1行単位で縦スクロールする。ScrollUnit.Item 前提。</summary>
    public void ScrollPlaylistByRows(int delta)
    {
        if (delta == 0 || FindDataGridScrollViewer() is not { } scroll)
        {
            return;
        }

        var next = Math.Clamp(
            scroll.VerticalOffset + delta,
            0,
            Math.Max(0, scroll.ScrollableHeight));
        if (Math.Abs(scroll.VerticalOffset - next) < 0.01)
        {
            return;
        }

        scroll.ScrollToVerticalOffset(next);
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
        if (_playlistScrollViewer is not null)
        {
            return _playlistScrollViewer;
        }

        _playlistScrollViewer = FindDescendantScrollViewer(_grid);
        return _playlistScrollViewer;
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

    private void EnsureListFocused(bool pinCurrent = true)
    {
        // フォーカス済みでも CurrentItem は選択行へ揃える。
        // IsKeyboardFocusWithin の早期 return だと、矢印キー移動後に CurrentItem が
        // 先頭のまま残り、行波形描画中のレイアウトでシアンが1行目へ寄る。
        if (pinCurrent)
        {
            PinSelectionCurrent();
        }

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

        // 既定の ScrollIntoView は長いセルで横へ飛ぶ。縦だけ寄せ、横位置は戻す。
        var scroll = FindDataGridScrollViewer();
        var keepX = scroll?.HorizontalOffset ?? 0;
        _grid.ScrollIntoView(item);
        if (scroll is not null)
        {
            RestorePlaylistHorizontalOffset(scroll, keepX);
        }

        var generation = _syncGeneration;
        Dispatcher.BeginInvoke(
            () =>
            {
                if (generation != _syncGeneration)
                {
                    return;
                }

                if (FindDataGridScrollViewer() is { } later)
                {
                    RestorePlaylistHorizontalOffset(later, keepX);
                }

                RevealGroupHeaderIfNearlyClipped(item);
            },
            DispatcherPriority.Loaded);
    }

    private static void RestorePlaylistHorizontalOffset(ScrollViewer scroll, double offset)
    {
        if (Math.Abs(scroll.HorizontalOffset - offset) < 0.5)
        {
            return;
        }

        scroll.ScrollToHorizontalOffset(offset);
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
        var files = SnapshotPlaylistFiles();
        if (files.Count == 0)
        {
            return;
        }

        _syncing = true;
        SuspendPlaylistSelectionNotify();
        try
        {
            ClearPlaylistRowSelectionFlags();
            foreach (var row in files)
            {
                SetPlaylistRowSelected(row, true);
            }

            _playlistRangeCaretIndex = -1;
            SyncPlaylistCaretToGrid(files[^1]);
            _anchorIndex = 0;
            PinSelectionCurrent();
        }
        finally
        {
            _syncing = false;
            ResumePlaylistSelectionNotify();
        }

        FocusList();
    }

    /// <summary>
    /// キャレット行と同じファイル名色の行だけを選ぶ。色が無い行では何もしない。
    /// </summary>
    public void SelectSameColorRows()
    {
        if (!CanSelectSameColorRows())
        {
            return;
        }

        var files = SnapshotPlaylistFiles();
        var caret = ResolvePlaylistCaretRow() ?? files[0];
        var colorIndex = caret.FileNameMarkerIndex;
        if (colorIndex < 0)
        {
            return;
        }

        LibraryFileRow? last = null;
        var firstIndex = -1;
        _syncing = true;
        SuspendPlaylistSelectionNotify();
        try
        {
            ClearPlaylistRowSelectionFlags();
            for (var i = 0; i < files.Count; i++)
            {
                var row = files[i];
                if (row.FileNameMarkerIndex != colorIndex)
                {
                    continue;
                }

                SetPlaylistRowSelected(row, true);
                last = row;
                if (firstIndex < 0)
                {
                    firstIndex = i;
                }
            }

            if (last is null || firstIndex < 0)
            {
                return;
            }

            _playlistRangeCaretIndex = -1;
            SyncPlaylistCaretToGrid(last);
            _anchorIndex = firstIndex;
            PinSelectionCurrent();
        }
        finally
        {
            _syncing = false;
            ResumePlaylistSelectionNotify();
        }

        FocusList();
    }

    /// <summary>プレイリストに行があり、かつ色マーカー付きの行があるときだけ同色選択できる。</summary>
    public bool CanSelectSameColorRows()
    {
        var files = SnapshotPlaylistFiles();
        if (files.Count == 0)
        {
            return false;
        }

        foreach (var row in files)
        {
            if (row.HasFileNameMarker)
            {
                return true;
            }
        }

        return false;
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
        _shuffleLatch.LatchLabel = UiStrings.LabelLibraryShuffle;
        _shuffleLatch.FitToLabel();
        TransportToolTip.Attach(_shuffleLatch, UiStrings.TipOptionLibraryShuffle);
        _autoPlayLatch.LatchLabel = UiStrings.LabelLibraryAutoPlay;
        _autoPlayLatch.FitToLabel();
        TransportToolTip.Attach(_autoPlayLatch, UiStrings.TipOptionLibraryAutoPlay);
        _checkSelectLatch.LatchLabel = UiStrings.LabelLibraryCheckSelect;
        _checkSelectLatch.FitToLabel();
        TransportToolTip.Attach(_checkSelectLatch, UiStrings.TipOptionLibraryCheckSelect);
        _autoPlayNextLatch.LatchLabel = UiStrings.LabelLibraryAutoPlayNext;
        _autoPlayNextLatch.FitToLabel();
        TransportToolTip.Attach(_autoPlayNextLatch, UiStrings.TipOptionLibraryAutoPlayNext);
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
        _shuffleLatch.RefreshAppearance();
        _autoPlayLatch.RefreshAppearance();
        _autoPlayNextLatch.RefreshAppearance();
        _checkSelectLatch.RefreshAppearance();
    }

    internal void SetOptionLatchWashThrough(bool wash)
    {
        foreach (var latch in new[] { _shuffleLatch, _autoPlayLatch, _autoPlayNextLatch, _checkSelectLatch })
        {
            latch.WashThrough = wash;
            latch.RefreshAppearance();
        }
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
        _playlistScrollViewer = null;
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
        var held = _syncing;
        _syncing = true;
        SuspendPlaylistSelectionNotify();
        try
        {
            ApplyRowSelectionCoreCore(active, selected);
        }
        finally
        {
            _syncing = held;
            ResumePlaylistSelectionNotify();
        }
    }

    private void ApplyRowSelectionCoreCore(DocumentSession? active, IReadOnlyList<DocumentSession>? selected)
    {
        var keep = selected is { Count: > 0 }
            ? selected
            : active is null ? [] : (IReadOnlyList<DocumentSession>)[active];
        LibraryFileRow? current = null;
        ClearPlaylistRowSelectionFlags();
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

            SetPlaylistRowSelected(row, true);
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
                    SetPlaylistRowSelected(row, true);
                    current = row;
                    break;
                }
            }
        }

        _playlistRangeCaretIndex = -1;
        if (current is not null)
        {
            SyncPlaylistCaretToGrid(current);
            _anchorIndex = _grid.SelectedIndex;
            PinSelectionCurrent();
            EnsureRowVisible(current);
        }
        else
        {
            SyncPlaylistCaretToGrid(null);
            _anchorIndex = -1;
            PinSelectionCurrent();
        }
    }

    /// <summary>
    /// DataGrid の CurrentItem はキャレット（なければ選択行）へ揃える。
    /// CollectionView の currency は<strong>選択行だけ</strong>に載せる。
    /// 未選択キャレットへ MoveCurrentTo すると WPF が IsSelected を先頭へ寄せ、
    /// シアンが1行目へ飛ぶ。
    /// </summary>
    private void PinSelectionCurrent()
    {
        var caret = ResolvePlaylistCaretRow();
        var selected = _grid.SelectedItem;
        var gridCurrent = caret ?? selected;
        if (gridCurrent is not null && !ReferenceEquals(_grid.CurrentItem, gridCurrent))
        {
            _grid.CurrentItem = gridCurrent;
        }

        // CollectionView は選択中の行のみ。キャレットが未選択なら SelectedItem へ。
        object? currency = null;
        if (caret is not null
            && (_playlistRangeCaretIndex >= 0 || caret.IsPlaylistSelected))
        {
            currency = caret;
        }
        else if (selected is not null)
        {
            currency = selected;
        }

        PinCollectionViewCurrent(currency);
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
        // グリッド行インデックス経路。ファイル行の差分キャッシュは使えない。
        _playlistRangeCaretIndex = -1;
        _syncing = true;
        SuspendPlaylistSelectionNotify();
        try
        {
            ClearPlaylistRowSelectionFlags();
            LibraryFileRow? caret = null;
            for (var i = lo; i <= hi; i++)
            {
                if (_grid.Items[i] is not LibraryFileRow row || row.Tag is not DocumentSession)
                {
                    continue;
                }

                SetPlaylistRowSelected(row, true);
                if (i == to)
                {
                    caret = row;
                }

                caret ??= row;
            }

            SyncPlaylistCaretToGrid(caret);
            PinSelectionCurrent();
        }
        finally
        {
            _syncing = false;
            ResumePlaylistSelectionNotify();
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


    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // マウスや通常キーの選択にキャレットを追従。
        // プログラム側の選択変更中（_syncing）は上書きしない。
        if (!_syncing && _grid.SelectedItem is LibraryFileRow selected)
        {
            _playlistCaretRow = selected;
            // Single モードの素クリックは DataGrid が SelectedItem だけ変える。
            // すでに帯選択内なら複数選択を崩さない（再生追従の Quiet など）。
            if ((Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) == 0
                && !selected.IsPlaylistSelected)
            {
                ClearPlaylistRowSelectionFlags();
                SetPlaylistRowSelected(selected, true);
                _playlistRangeCaretIndex = -1;
            }
        }

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

    /// <summary>
    /// メニュー閉鎖直後の dismiss 貫通を、グリッド以外（検索・ツリー境界など）でも握りつぶす。
    /// メニュー本体が開いているあいだは触らない（ポップアップ外のこのビューへの Down はグリッド側で処理）。
    /// </summary>
    private void LibraryBrowser_PreviewMouseLeftButtonDownForDismissGuard(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left
            || HasOpenContextMenu
            || !_contextMenuDismissGuard)
        {
            return;
        }

        AbandonPlaylistPendingClick();
        _suppressPlaylistPlainPressComplete = true;
        e.Handled = true;
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
        // 閉じた同じ左クリックが上がるまで立つ dismiss ガードも見て握りつぶす。
        if (_grid.ContextMenu is { IsOpen: true } || IsContextMenuDismissGuardActive)
        {
            AbandonPlaylistPendingClick();
            ReleasePlaylistMouseCapture();
            _suppressPlaylistPlainPressComplete = true;
            e.Handled = true;
            return;
        }

        // チェックボックスは選択ハンドラに吸わせず、そのままトグルさせる。
        if (FindAncestor<CheckBox>(origin) is not null)
        {
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
        // HasOpenContextMenu だけでは閉じた直後を拾えないので dismiss ガードも見る。
        if (HasOpenContextMenu
            || IsContextMenuDismissGuardActive
            || _suppressPlaylistPlainPressComplete)
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
        && _playlistSelectedRows.Count > 1
        && row.IsPlaylistSelected;

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

        if (_grid.Items[index] is not LibraryFileRow fileRow || fileRow.Tag is not DocumentSession)
        {
            return;
        }

        _syncing = true;
        SuspendPlaylistSelectionNotify();
        try
        {
            SetPlaylistRowSelected(fileRow, !fileRow.IsPlaylistSelected);
            _anchorIndex = index;
            _playlistRangeCaretIndex = -1;
            if (fileRow.IsPlaylistSelected)
            {
                SyncPlaylistCaretToGrid(fileRow);
            }
            else if (_playlistSelectedRows.Count > 0)
            {
                LibraryFileRow? next = null;
                foreach (var row in _playlistSelectedRows)
                {
                    next = row;
                    break;
                }

                SyncPlaylistCaretToGrid(next);
            }
            else
            {
                SyncPlaylistCaretToGrid(null);
            }

            // DataGrid の CurrentItem だけキャレットへ。CollectionView.MoveCurrentTo はしない
            // （未選択行へ載せるとシアンが1行目へ飛ぶ）。
            if (_playlistCaretRow is not null
                && !ReferenceEquals(_grid.CurrentItem, _playlistCaretRow))
            {
                _grid.CurrentItem = _playlistCaretRow;
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

        if (_playlistSelectionNotifyDepth != 0)
        {
            return;
        }

        RefreshPlaylistSelectionBandEdges();
        if (!_playlistSelectionNotifyPending)
        {
            return;
        }

        _playlistSelectionNotifyPending = false;
        PlaylistSelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NotePlaylistSelectionChanged()
    {
        if (_playlistSelectionNotifyDepth > 0)
        {
            _playlistSelectionNotifyPending = true;
            return;
        }

        RefreshPlaylistSelectionBandEdges();
        PlaylistSelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 連続選択の先頭／末尾だけ角丸。グループ見出しなど非ファイル行は帯を切る。
    /// </summary>
    private void RefreshPlaylistSelectionBandEdges()
    {
        var seen = new HashSet<LibraryFileRow>();
        var run = new List<LibraryFileRow>();
        void Flush()
        {
            for (var i = 0; i < run.Count; i++)
            {
                var row = run[i];
                row.SelectionBandEdge = SelectionBandCorners.EdgeAt(i, run.Count);
                seen.Add(row);
            }

            run.Clear();
        }

        foreach (var item in _grid.Items)
        {
            if (item is not LibraryFileRow row)
            {
                Flush();
                continue;
            }

            if (row.IsPlaylistSelected)
            {
                run.Add(row);
                continue;
            }

            Flush();
            if (row.SelectionBandEdge != SelectionBandEdge.None)
            {
                row.SelectionBandEdge = SelectionBandEdge.None;
            }
        }

        Flush();
        foreach (var row in _playlistSelectedRows)
        {
            if (!seen.Contains(row))
            {
                row.SelectionBandEdge = SelectionBandEdge.Solo;
            }
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
    internal const double HeightFactor = 0.28;
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
    private double _contentHeight;

    public LibraryGroupJacketImage()
    {
        _pin = new LibraryGroupHorizontalPin(_stick);
        Orientation = Orientation.Vertical;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(8, 0, 8, 0);
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

    /// <summary>
    /// レイアウト高はジャケット本体だけ。反射（影落とし）は曲行の上へ重ね、見切れ可。
    /// （反射まで入れると曲数が少ないときグループ間ギャップが広がる。）
    /// </summary>
    protected override Size MeasureOverride(Size constraint)
    {
        var measured = base.MeasureOverride(constraint);
        _contentHeight = measured.Height;
        var faceHeight = _face.DesiredSize.Height;
        return new Size(measured.Width, faceHeight);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var height = Math.Max(arrangeSize.Height, _contentHeight);
        base.ArrangeOverride(new Size(arrangeSize.Width, height));
        return arrangeSize;
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
        var jacketH = Math.Max(_contentHeight, ActualHeight);
        if (_owner is null || Parent is not FrameworkElement stage || jacketH < 1)
        {
            return;
        }

        _owner.EnsureGroupScrollHook();

        try
        {
            var headerBottom = _owner.RowsViewportTop();
            var natural = TransformToAncestor(_owner.FileGrid).Transform(new Point(0, 0)).Y - _stick.Y;
            var max = Math.Max(0, stage.ActualHeight - jacketH);
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
