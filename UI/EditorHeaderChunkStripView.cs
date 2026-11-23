using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>
/// エディタ chrome のヘッダ情報。タイムコードと編集履歴のあいだ。
/// 左＝チャンクリスト、余裕があれば右＝情報。枠は別々（HistoryStrip と同色）。
/// アクティブ側の枠は PrimaryFore（白系）。選択帯はプレイヤーと同じシアン＋角丸。
/// スクロールバーはライブラリと同じ細さで常時表示。
/// 幅はプレイヤー情報列と同じく、一度広げたら自動では戻さない（H トグル／エディタ退出で忘れる）。
/// 帯に載らないときは MainWindow 側が波形とレベルメーターのあいだへ出す。
/// </summary>
internal sealed class EditorHeaderChunkStripView : Border
{
    private static readonly FontFamily MonoFont = new("Consolas");
    /// <summary>ツリー枠と情報枠のすき間（chrome 帯同士の Margin 6 に揃える）。</summary>
    internal static double PaneGap => DesignMetrics.From96(6);
    private readonly Grid _body = new();
    private readonly Border _treeFrame = new();
    private readonly Border _detailFrame = new();
    private readonly TreeView _tree = new();
    private readonly TextBox _detail = new();
    private readonly ColumnDefinition _treeColumn = new();
    private readonly ColumnDefinition _detailColumn = new();
    private readonly StackPanel _repairPanel = new();
    private readonly RoundedButton _repairAskButton = new();
    private readonly StackPanel _repairConfirmPanel = new();
    private readonly TextBlock _repairPrompt = new();
    private readonly RoundedButton _repairOverwriteButton = new();
    private bool _repairConfirming;
    private IReadOnlyList<string> _repairPaths = Array.Empty<string>();
    private string? _loadedPath;
    private WaveStructureDocument? _loadedDocument;
    private int _unionBuildTicket;
    private string? _unionPendingKey;
    private bool _chunkApplyDeferred;
    private int _chunkApplyTicket;
    private string _detailText = string.Empty;
    private bool _inlineDetail;
    private double _treeFittedWidth = DesignMetrics.EditorHeaderChunkStripMinWidth;
    private double _detailFittedWidth = DesignMetrics.EditorHeaderDetailMinWidth;
    /// <summary>ツリー列の高水位。自動では下げない。</summary>
    private double _treeHeldWidth;
    /// <summary>情報列の高水位。自動では下げない。</summary>
    private double _detailHeldWidth;
    /// <summary>複数選択（コピー用マーク）。Ctrl+A／Shift範囲／Ctrl個別。</summary>
    private readonly HashSet<TreeViewItem> _multiSelected = [];
    private TreeViewItem? _selectionAnchor;
    private bool _rangeBusy;

    public EditorHeaderChunkStripView()
    {
        MinWidth = DesignMetrics.EditorHeaderChunkStripMinWidth;
        MaxWidth = DesignMetrics.EditorHeaderChunkStripMaxWidth;
        Width = DesignMetrics.EditorHeaderChunkStripMinWidth;
        Height = DesignMetrics.SpectrumHeight;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        // 外形の枠は持たず、ツリー／情報それぞれが枠を持つ。
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);

        _treeColumn.Width = new GridLength(1, GridUnitType.Star);
        _detailColumn.Width = new GridLength(0);
        _detailColumn.MinWidth = 0;
        _body.ColumnDefinitions.Add(_treeColumn);
        _body.ColumnDefinitions.Add(_detailColumn);

        ConfigurePaneFrame(_treeFrame);
        ConfigurePaneFrame(_detailFrame);
        _detailFrame.Margin = new Thickness(PaneGap, 0, 0, 0);
        _detailFrame.Visibility = Visibility.Collapsed;

        _tree.BorderThickness = new Thickness(0);
        _tree.Background = Brushes.Transparent;
        _tree.Padding = new Thickness(2, 0, 2, 0);
        _tree.FontFamily = MonoFont;
        _tree.FontSize = 11;
        _tree.Focusable = true;
        ScrollViewer.SetHorizontalScrollBarVisibility(_tree, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(_tree, ScrollBarVisibility.Auto);
        _tree.SetResourceReference(Control.ForegroundProperty, "PrimaryForeBrush");
        ApplyAlwaysVisibleScrollBarStyle(_tree);
        ApplyTreeItemStyle();
        RebuildTreeContextMenu();
        _tree.SelectedItemChanged += Tree_SelectedItemChanged;
        _tree.PreviewMouseLeftButtonDown += Tree_PreviewMouseLeftButtonDown;
        _tree.PreviewMouseRightButtonDown += Tree_PreviewMouseRightButtonDown;
        // 展開で深い行のキャプション幅が変わったとき取り直す。
        _tree.AddHandler(
            TreeViewItem.ExpandedEvent,
            new RoutedEventHandler((_, _) => FitContentWidth()),
            handledEventsToo: true);
        _tree.AddHandler(
            TreeViewItem.CollapsedEvent,
            new RoutedEventHandler((_, _) => FitContentWidth()),
            handledEventsToo: true);
        _tree.IsKeyboardFocusWithinChanged += (_, _) => SyncPaneFocusChrome();
        BuildRepairPanel();
        // 帯が狭いので修復 UI はツリーの冒頭（上）へ。
        var treePane = new DockPanel { Background = Brushes.Transparent };
        DockPanel.SetDock(_repairPanel, Dock.Top);
        treePane.Children.Add(_repairPanel);
        treePane.Children.Add(_tree);
        _treeFrame.Child = treePane;
        Grid.SetColumn(_treeFrame, 0);
        _body.Children.Add(_treeFrame);

        _detail.IsReadOnly = true;
        _detail.AcceptsReturn = true;
        _detail.TextWrapping = TextWrapping.NoWrap;
        _detail.BorderThickness = new Thickness(0);
        _detail.Background = Brushes.Transparent;
        _detail.CaretBrush = Brushes.Transparent;
        _detail.Padding = new Thickness(6, 0, 2, 0);
        _detail.FontFamily = MonoFont;
        _detail.FontSize = 11;
        _detail.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        // プレイヤー情報列と同じ。横スクロール禁止・幅 Fit で見切れを防ぐ。
        _detail.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _detail.Focusable = true;
        _detail.SetResourceReference(Control.ForegroundProperty, "PrimaryForeBrush");
        TextOptions.SetTextFormattingMode(_detail, TextFormattingMode.Display);
        ApplyAlwaysVisibleScrollBarStyle(_detail);
        _detail.IsKeyboardFocusWithinChanged += (_, _) => SyncPaneFocusChrome();
        _detailFrame.Child = _detail;
        Grid.SetColumn(_detailFrame, 1);
        _body.Children.Add(_detailFrame);

        Child = _body;
        SyncPaneFocusChrome();
    }

    private static void ConfigurePaneFrame(Border frame)
    {
        frame.Background = Brushes.Transparent;
        frame.BorderThickness = new Thickness(1);
        frame.SetResourceReference(BorderBrushProperty, "VstInsertSlotEmptyBackBrush");
        DesignMetrics.BindCorner(frame);
        frame.Padding = new Thickness(2, 2, 2, 2);
        frame.SnapsToDevicePixels = true;
        frame.UseLayoutRounding = true;
        frame.ClipToBounds = true;
    }

    /// <summary>フォーカス中の枠だけ PrimaryFore（白系）。</summary>
    private void SyncPaneFocusChrome()
    {
        SetPaneFrameActive(_treeFrame, _tree.IsKeyboardFocusWithin);
        SetPaneFrameActive(
            _detailFrame,
            _detailFrame.Visibility == Visibility.Visible && _detail.IsKeyboardFocusWithin);
    }

    private static void SetPaneFrameActive(Border frame, bool active)
    {
        frame.SetResourceReference(
            BorderBrushProperty,
            active ? "PrimaryForeBrush" : "VstInsertSlotEmptyBackBrush");
    }

    /// <summary>選択中チャンクの情報本文。未選択／空なら空文字。</summary>
    public string DetailText => _detailText;

    /// <summary>選択中チャンク。未選択なら null。</summary>
    public WaveStructureNode? SelectedNode { get; private set; }

    /// <summary>チャンク列の希望幅（枠・余白込み。高水位込み）。</summary>
    public double TreeFittedWidth => Math.Max(_treeHeldWidth, _treeFittedWidth);

    /// <summary>詳細列の希望幅（余白・高水位込み）。本文が空なら 0。</summary>
    public double DetailFittedWidth =>
        string.IsNullOrEmpty(_detailText) ? 0 : Math.Max(_detailHeldWidth, _detailFittedWidth);

    public event EventHandler? DetailChanged;

    /// <summary>修復結果などの短いステータス表示。</summary>
    public event Action<string>? RepairMessage;

    /// <summary>
    /// エディタ編集: ヘッダ修復。即時上書きせず作業コピーへ。成功で true。
    /// </summary>
    public Func<IReadOnlyList<string>, Task<bool>>? ApplyRepairEditAsync { get; set; }

    /// <summary>
    /// エディタ編集: チャンク削除。即時上書きせず作業コピーへ。成功で true。
    /// </summary>
    public Func<IReadOnlyDictionary<string, IReadOnlyCollection<string>>, int, Task<bool>>?
        ApplyChunkDeleteEditAsync
    { get; set; }

    /// <summary>チャンネルレイアウト編集の対象パス（選択タブの作業コピー優先）。</summary>
    public Func<IReadOnlyList<string>>? ResolveChannelLayoutPaths { get; set; }

    /// <summary>
    /// エディタ編集: チャンネルレイアウト。即時上書きせず作業コピーへ。成功で true。
    /// </summary>
    public Func<IReadOnlyList<string>, int, string?, Task<bool>>? ApplyChannelLayoutEditAsync { get; set; }

    /// <summary>
    /// 和集合チャンク選択時。MemberPaths 付きノードを渡し、対応タブを選ばせる。
    /// </summary>
    public Action<IReadOnlyList<WaveStructureNode>>? ApplyUnionChunkTabSelection { get; set; }

    /// <summary>テスト用。null なら実キー状態を見る。</summary>
    internal Func<bool>? NavigationKeyHeldForTest { get; set; }

    /// <summary>テスト用。読み込みキー（単一パス or \u0001union\u0001…）。</summary>
    internal string? LoadedPath => _loadedPath;

    /// <summary>テスト用。和集合ドキュメントか。</summary>
    internal bool IsChunkUnion => _loadedDocument is { IsChunkUnion: true };

    private void ApplyAlwaysVisibleScrollBarStyle(Control host)
    {
        if (TryFindResource("EditorHeaderScrollBarStyle") is Style style)
        {
            host.Resources[typeof(ScrollBar)] = style;
        }
    }

    /// <summary>文書パスからチャンクツリーを読み込む。未対応／失敗時は空。</summary>
    public void LoadFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Clear();
            return;
        }

        // 進行中の和集合は破棄する。
        _unionBuildTicket++;
        _unionPendingKey = null;

        if (string.Equals(path, _loadedPath, StringComparison.OrdinalIgnoreCase)
            && _loadedDocument is not null
            && !_loadedDocument.IsChunkUnion
            && _tree.Items.Count > 0)
        {
            return;
        }

        _loadedPath = path;
        var document = WaveContainerStructure.TryRead(path);
        _loadedDocument = document;
        Populate(document);
    }

    /// <summary>
    /// 複数タブのチャンク和集合。ディスク走査は裏スレッド、TreeView 更新だけ UI。
    /// </summary>
    public void LoadUnionFromPaths(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            Clear();
            return;
        }

        if (paths.Count == 1)
        {
            LoadFromPath(paths[0]);
            return;
        }

        var cacheParts = new string[paths.Count];
        for (var i = 0; i < paths.Count; i++)
        {
            cacheParts[i] = paths[i].ToUpperInvariant();
        }

        var cacheKey = "\u0001union\u0001" + string.Join('\u0001', cacheParts);
        if (string.Equals(cacheKey, _loadedPath, StringComparison.Ordinal)
            && _loadedDocument is { IsChunkUnion: true })
        {
            _unionPendingKey = null;
            return;
        }

        if (string.Equals(cacheKey, _unionPendingKey, StringComparison.Ordinal))
        {
            return;
        }

        _unionPendingKey = cacheKey;
        var ticket = ++_unionBuildTicket;
        var snapshot = paths.ToArray();
        _ = Task.Run(() => WaveContainerStructure.BuildChunkUnion(snapshot))
            .ContinueWith(task =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (ticket != _unionBuildTicket
                        || !string.Equals(cacheKey, _unionPendingKey, StringComparison.Ordinal))
                    {
                        return;
                    }

                    if (task.IsFaulted || task.IsCanceled)
                    {
                        return;
                    }

                    _unionPendingKey = null;
                    _loadedPath = cacheKey;
                    _loadedDocument = task.Result;
                    Populate(task.Result);
                });
            });
    }

    /// <summary>H トグル／エディタ退出用。中身と幅の高水位を忘れる。</summary>
    public void Clear()
    {
        DiscardContent();
        _treeHeldWidth = 0;
        _detailHeldWidth = 0;
        Width = DesignMetrics.EditorHeaderChunkStripMinWidth;
        MaxWidth = DesignMetrics.EditorHeaderChunkStripMaxWidth;
    }

    /// <summary>解析結果だけ捨てる（Clear から呼ばれる）。</summary>
    private void DiscardContent()
    {
        _multiSelected.Clear();
        _selectionAnchor = null;
        _rangeBusy = false;
        _chunkApplyDeferred = false;
        _chunkApplyTicket++;
        _unionBuildTicket++;
        _unionPendingKey = null;
        _loadedPath = null;
        _loadedDocument = null;
        _tree.Items.Clear();
        SelectedNode = null;
        SetDetailText(string.Empty);
        SetRepairTargets(Array.Empty<string>());
        ApplyInlineDetailLayout();
    }

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (!_rangeBusy && e.NewValue is TreeViewItem selected)
        {
            var modifiers = Keyboard.Modifiers;
            if ((modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0)
            {
                _selectionAnchor = selected;
            }

            // Preview で先にマーク済みでも、選択が確定したあとに帯の角を取り直す。
            if ((modifiers & ModifierKeys.Control) == 0
                && (modifiers & ModifierKeys.Shift) == 0)
            {
                if (!_multiSelected.Contains(selected))
                {
                    ClearMultiSelect();
                    _multiSelected.Add(selected);
                }

                ApplyMultiSelectChrome();
            }
        }

        RequestChunkSelectionApply();
    }

    private void Tree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject origin
            || FindTreeViewItem(origin) is not { } item
            || FindTreeExpandToggle(origin) is not null)
        {
            // 余白クリックでもチャンクツリーをアクティブにする（先頭行の自動選択はしない）。
            ActivateTreeFromEmptyClick();
            return;
        }

        var modifiers = Keyboard.Modifiers;
        var control = (modifiers & ModifierKeys.Control) != 0;
        var shift = (modifiers & ModifierKeys.Shift) != 0;
        BeginPlainPress(item, control, shift);
        if (control || shift)
        {
            e.Handled = true;
        }
    }

    /// <summary>右クリック行が未マークなら、その1行だけを対象にする（複数マーク中はそのまま）。</summary>
    private void Tree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject origin
            || FindTreeViewItem(origin) is not { } item
            || FindTreeExpandToggle(origin) is not null)
        {
            ActivateTreeFromEmptyClick();
            return;
        }

        if (_multiSelected.Contains(item))
        {
            return;
        }

        BeginPlainPress(item, control: false, shift: false);
    }

    private void ActivateTreeFromEmptyClick()
    {
        if (_tree.SelectedItem is TreeViewItem selected)
        {
            selected.Focus();
        }
        else
        {
            _tree.Focus();
        }

        SyncPaneFocusChrome();
    }

    /// <summary>次回 LoadFromPath／LoadUnionFromPaths で必ず再解析する。</summary>
    public void InvalidateLoadedCache()
    {
        _loadedPath = null;
        _loadedDocument = null;
        _unionPendingKey = null;
        _unionBuildTicket++;
    }

    /// <summary>修復対象パスを差し替える。空ならボタンを隠す。</summary>
    public void SetRepairTargets(IReadOnlyList<string> paths)
    {
        _repairPaths = paths.Count == 0 ? Array.Empty<string>() : paths.ToArray();
        ResetRepairUi(show: _repairPaths.Count > 0);
        FitContentWidth();
    }

    private void BuildRepairPanel()
    {
        _repairPanel.Orientation = Orientation.Vertical;
        _repairPanel.Margin = new Thickness(2, 2, 2, 4);
        _repairPanel.HorizontalAlignment = HorizontalAlignment.Stretch;
        _repairPanel.Visibility = Visibility.Collapsed;

        _repairAskButton.Content = UiStrings.WaveHeaderRepairButton;
        _repairAskButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _repairAskButton.MinHeight = DesignMetrics.From96(22);
        _repairAskButton.Padding = new Thickness(6, 2, 6, 2);
        _repairAskButton.Margin = new Thickness(0);
        _repairAskButton.FontSize = 11;
        ActionButtonLooks.ApplyClear(_repairAskButton);
        _repairAskButton.Click += (_, _) => ShowRepairConfirm();

        _repairPrompt.Text = UiStrings.WaveHeaderRepairPrompt;
        _repairPrompt.TextWrapping = TextWrapping.Wrap;
        _repairPrompt.Margin = new Thickness(0, 0, 0, 4);
        _repairPrompt.FontSize = 11;
        _repairPrompt.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryForeBrush");

        // エディタは即時上書き／別名保存しない。編集として履歴へ載せるだけ。
        _repairOverwriteButton.Content = UiStrings.WaveHeaderRepairApplyEdit;
        _repairOverwriteButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _repairOverwriteButton.MinHeight = DesignMetrics.From96(22);
        _repairOverwriteButton.Padding = new Thickness(6, 2, 6, 2);
        _repairOverwriteButton.Margin = new Thickness(0);
        _repairOverwriteButton.FontSize = 11;
        ActionButtonLooks.ApplyOrange(_repairOverwriteButton);
        _repairOverwriteButton.Click += (_, _) => _ = RepairAsEditAsync();

        _repairConfirmPanel.Orientation = Orientation.Vertical;
        _repairConfirmPanel.Visibility = Visibility.Collapsed;
        _repairConfirmPanel.Children.Add(_repairPrompt);
        _repairConfirmPanel.Children.Add(_repairOverwriteButton);

        _repairPanel.Children.Add(_repairAskButton);
        _repairPanel.Children.Add(_repairConfirmPanel);
    }

    private void ResetRepairUi(bool show)
    {
        _repairConfirming = false;
        _repairAskButton.Visibility = Visibility.Visible;
        _repairConfirmPanel.Visibility = Visibility.Collapsed;
        _repairAskButton.IsEnabled = true;
        _repairOverwriteButton.IsEnabled = true;
        _repairAskButton.Content = UiStrings.WaveHeaderRepairButton;
        _repairPrompt.Text = FormatRepairPrompt();
        _repairOverwriteButton.Content = UiStrings.WaveHeaderRepairApplyEdit;
        _repairPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private string FormatRepairPrompt() =>
        _repairPaths.Count > 1
            ? UiStrings.WaveHeaderRepairPromptMany(_repairPaths.Count)
            : UiStrings.WaveHeaderRepairPrompt;

    private void ShowRepairConfirm()
    {
        if (_repairPanel.Visibility != Visibility.Visible || _repairPaths.Count == 0)
        {
            return;
        }

        _repairConfirming = true;
        _repairPrompt.Text = FormatRepairPrompt();
        _repairAskButton.Visibility = Visibility.Collapsed;
        _repairConfirmPanel.Visibility = Visibility.Visible;
        FitContentWidth();
    }

    private async Task RepairAsEditAsync()
    {
        if (!_repairConfirming || _repairPaths.Count == 0)
        {
            return;
        }

        if (ApplyRepairEditAsync is null)
        {
            RepairMessage?.Invoke(UiStrings.WaveHeaderRepairFailed("unavailable"));
            ResetRepairUi(show: true);
            return;
        }

        var sources = _repairPaths.ToArray();
        _repairOverwriteButton.IsEnabled = false;
        try
        {
            var ok = await ApplyRepairEditAsync(sources).ConfigureAwait(true);
            if (!ok)
            {
                ResetRepairUi(show: true);
                ShowRepairConfirm();
                return;
            }

            _repairPaths = Array.Empty<string>();
            ResetRepairUi(show: false);
        }
        catch (Exception ex)
        {
            RepairMessage?.Invoke(UiStrings.WaveHeaderRepairFailed(ex.Message));
            ResetRepairUi(show: true);
            ShowRepairConfirm();
        }
    }

    /// <summary>チャンクツリーにキーボードフォーカスがある。</summary>
    public bool IsTreeFocused => _tree.IsKeyboardFocusWithin;

    internal TreeView Tree => _tree;

    /// <summary>
    /// メニューキー／Shift+F10。フォーカスを波形へ移さず、チャンクツリーのメニューを開く。
    /// </summary>
    public bool TryOpenKeyboardContextMenu()
    {
        if (_tree.ContextMenu is not { } menu)
        {
            return false;
        }

        var anchor = Keyboard.FocusedElement as FrameworkElement;
        if (anchor is null
            || (!ReferenceEquals(anchor, _tree) && !_tree.IsAncestorOf(anchor)))
        {
            // 詳細側フォーカスでもメニューはツリーに出す（プレイヤーのヘッダ構造と同じ）。
            anchor = _tree;
        }

        menu.PlacementTarget = anchor;
        menu.Placement = PlacementMode.Center;
        menu.IsOpen = true;
        return true;
    }

    /// <summary>帯内のインライン詳細にキーボードフォーカスがある。</summary>
    public bool IsInlineDetailFocused => _detail.IsKeyboardFocusWithin;

    /// <summary>トグルでオンにしたとき／詳細から戻るとき、チャンクツリーへフォーカスする。</summary>
    public void FocusTree()
    {
        if (_tree.SelectedItem is TreeViewItem selected)
        {
            selected.Focus();
            return;
        }

        if (_tree.Items.OfType<TreeViewItem>().FirstOrDefault() is { } first)
        {
            first.IsSelected = true;
            first.Focus();
            return;
        }

        _tree.Focus();
    }

    /// <summary>帯内インライン詳細へフォーカス。非表示なら false。</summary>
    public bool FocusInlineDetail()
    {
        if (!_inlineDetail || _detailFrame.Visibility != Visibility.Visible)
        {
            return false;
        }

        _detail.Focus();
        return true;
    }

    /// <summary>表示中のチャンク行をすべてマーク（コピー／和集合→タブ同期用）。</summary>
    public void SelectAllItems()
    {
        var items = FlattenVisibleItems();
        if (items.Count == 0)
        {
            return;
        }

        _multiSelected.Clear();
        foreach (var item in items)
        {
            _multiSelected.Add(item);
        }

        _selectionAnchor = _tree.SelectedItem as TreeViewItem ?? items[0];
        ApplyMultiSelectChrome();

        if (!_tree.IsKeyboardFocusWithin)
        {
            FocusTree();
        }

        RequestChunkSelectionApply();
    }

    /// <summary>全選択中なら全行、そうでなければ選択行をタブ区切りでクリップボードへ。</summary>
    public void CopySelection()
    {
        var text = BuildCopyText();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        SystemClipboard.TrySetText(text, Window.GetWindow(this));
    }

    public void MoveSelection(int delta, bool extend = false)
    {
        if (delta == 0)
        {
            return;
        }

        var items = FlattenVisibleItems();
        if (items.Count == 0)
        {
            return;
        }

        var caret = _tree.SelectedItem as TreeViewItem;
        var index = caret is null ? -1 : items.IndexOf(caret);
        if (index < 0)
        {
            index = delta > 0 ? -1 : items.Count;
        }

        var next = Math.Clamp(index + delta, 0, items.Count - 1);
        ApplyMoveSelection(items[next], caret, extend);
    }

    public void MoveSelectionToEdge(int edge, bool extend = false)
    {
        var items = FlattenVisibleItems();
        if (items.Count == 0)
        {
            return;
        }

        var caret = _tree.SelectedItem as TreeViewItem;
        ApplyMoveSelection(edge < 0 ? items[0] : items[^1], caret, extend);
    }

    internal int MultiSelectedCount => _multiSelected.Count;

    internal void BeginPlainPress(TreeViewItem item, bool control, bool shift = false)
    {
        if (shift)
        {
            ApplyShiftClick(item);
            return;
        }

        if (control)
        {
            ToggleMultiSelect(item);
            // SelectedItemChanged がアンカーを上書きしないよう握る。
            _rangeBusy = true;
            try
            {
                SelectTreeItem(item);
            }
            finally
            {
                _rangeBusy = false;
            }

            RequestChunkSelectionApply();
            return;
        }

        ClearMultiSelect();
        _multiSelected.Add(item);
        _selectionAnchor = item;
        ApplyMultiSelectChrome();
        RequestChunkSelectionApply();
    }

    internal void ApplyShiftClick(TreeViewItem item) =>
        ApplyMoveSelection(item, _tree.SelectedItem as TreeViewItem, extend: true);

    private void ApplyMoveSelection(TreeViewItem current, TreeViewItem? caret, bool extend)
    {
        _rangeBusy = true;
        try
        {
            if (!extend)
            {
                _selectionAnchor = current;
                ClearMultiSelect();
                _multiSelected.Add(current);
                ApplyMultiSelectChrome();
                SelectTreeItem(current);
                return;
            }

            _selectionAnchor ??= caret ?? current;
            SelectVisibleRange(_selectionAnchor, current);
            SelectTreeItem(current);
        }
        finally
        {
            _rangeBusy = false;
        }

        RequestChunkSelectionApply();
    }

    private bool IsNavigationKeyHeld() =>
        NavigationKeyHeldForTest?.Invoke()
        ?? (Keyboard.IsKeyDown(Key.Up)
            || Keyboard.IsKeyDown(Key.Down)
            || Keyboard.IsKeyDown(Key.Home)
            || Keyboard.IsKeyDown(Key.End)
            || Keyboard.IsKeyDown(Key.PageUp)
            || Keyboard.IsKeyDown(Key.PageDown));

    /// <summary>
    /// チャンク選択副作用。ナビキー押し中は保留し、離してから詳細／和集合→タブ同期する。
    /// </summary>
    private void RequestChunkSelectionApply()
    {
        if (IsNavigationKeyHeld())
        {
            ScheduleDeferredChunkApply();
            return;
        }

        ApplyChunkSelectionNow();
    }

    private void ScheduleDeferredChunkApply()
    {
        _chunkApplyDeferred = true;
        var ticket = ++_chunkApplyTicket;
        Dispatcher.BeginInvoke(
            () =>
            {
                if (ticket != _chunkApplyTicket || !_chunkApplyDeferred)
                {
                    return;
                }

                if (IsNavigationKeyHeld())
                {
                    return;
                }

                FlushDeferredChunkApply();
            },
            DispatcherPriority.Background);
    }

    /// <summary>ナビキーを離したあと、溜めたチャンク選択を確定する。</summary>
    internal void FlushDeferredChunkApply()
    {
        if (!_chunkApplyDeferred)
        {
            return;
        }

        if (IsNavigationKeyHeld())
        {
            return;
        }

        ApplyChunkSelectionNow();
    }

    private void ApplyChunkSelectionNow()
    {
        _chunkApplyDeferred = false;
        _chunkApplyTicket++;

        if (_loadedDocument is { IsChunkUnion: true })
        {
            SelectedNode = null;
            SetDetailText(string.Empty, notify: false);
            ApplyInlineDetailLayout();
            FitContentWidth();
            DetailChanged?.Invoke(this, EventArgs.Empty);
            ApplyUnionChunkTabSelection?.Invoke(CollectUnionChunkSelectionNodes());
            return;
        }

        RefreshSelectedDetail();
    }

    /// <summary>
    /// 和集合ツリーのマーク行（無ければキャレット行）のチャンクノード。
    /// </summary>
    internal List<WaveStructureNode> CollectUnionChunkSelectionNodes()
    {
        var nodes = new List<WaveStructureNode>();
        foreach (var item in FlattenVisibleItems())
        {
            if (!_multiSelected.Contains(item)
                || item.Tag is not WaveStructureNode node)
            {
                continue;
            }

            nodes.Add(node);
        }

        if (nodes.Count == 0
            && _tree.SelectedItem is TreeViewItem { Tag: WaveStructureNode selected })
        {
            nodes.Add(selected);
        }

        return nodes;
    }

    private void ToggleMultiSelect(TreeViewItem item)
    {
        if (!_multiSelected.Add(item))
        {
            _multiSelected.Remove(item);
        }

        ApplyMultiSelectChrome();
    }

    private void ClearMultiSelect()
    {
        if (_multiSelected.Count == 0)
        {
            return;
        }

        _multiSelected.Clear();
        ApplyMultiSelectChrome();
    }

    private void ApplyMultiSelectChrome()
    {
        var run = new List<TreeViewItem>();
        void Flush()
        {
            for (var i = 0; i < run.Count; i++)
            {
                run[i].SetValue(
                    LibraryBrowserView.SelectionBandEdgeProperty,
                    SelectionBandCorners.EdgeAt(i, run.Count));
            }

            run.Clear();
        }

        foreach (var item in FlattenVisibleItems())
        {
            var marked = _multiSelected.Contains(item);
            item.SetValue(LibraryBrowserView.TreeMarkedProperty, marked);
            // IsSelected は帯に入れない（Preview 中の前行選択と混ざると角が歪む）。
            if (marked)
            {
                run.Add(item);
            }
            else
            {
                Flush();
                item.SetValue(
                    LibraryBrowserView.SelectionBandEdgeProperty,
                    SelectionBandEdge.None);
            }
        }

        Flush();
    }

    private void SelectVisibleRange(TreeViewItem from, TreeViewItem to)
    {
        var items = FlattenVisibleItems();
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
        _multiSelected.Clear();
        for (var i = lo; i <= hi; i++)
        {
            _multiSelected.Add(items[i]);
        }

        ApplyMultiSelectChrome();
    }

    /// <summary>
    /// ヘッダ構造／XML と同じ展開キー。*／/／Shift+/ と三角行の←→。
    /// </summary>
    public bool TryHandleExpandKey(Key key, ModifierKeys modifiers)
    {
        if (LibraryPlayerMode.IsExplorerExpandAll(key, modifiers))
        {
            if (_tree.SelectedItem is TreeViewItem selected)
            {
                ExpandSubtree(selected);
            }

            return true;
        }

        if (LibraryPlayerMode.IsExplorerCollapseSubtree(key, modifiers))
        {
            if (_tree.SelectedItem is TreeViewItem selected)
            {
                CollapseSubtree(selected);
            }

            return true;
        }

        if (LibraryPlayerMode.IsExplorerCollapseAllRoots(key, modifiers))
        {
            foreach (var root in _tree.Items.OfType<TreeViewItem>())
            {
                CollapseSubtree(root);
            }

            return true;
        }

        if (key is Key.Left or Key.Right && modifiers == ModifierKeys.None)
        {
            return TryArrowExpandCollapse(key);
        }

        return false;
    }

    /// <summary>インライン詳細テキストのスクロール。XML ナビは呼び出し側。</summary>
    public bool TryScrollInlineDetail(Key key)
    {
        if (!IsInlineDetailFocused || _detailFrame.Visibility != Visibility.Visible)
        {
            return false;
        }

        var viewer = FindDescendantScrollViewer(_detail);
        if (viewer is null)
        {
            return false;
        }

        switch (key)
        {
            case Key.Up:
                viewer.LineUp();
                return true;
            case Key.Down:
                viewer.LineDown();
                return true;
            case Key.PageUp:
                viewer.PageUp();
                return true;
            case Key.PageDown:
                viewer.PageDown();
                return true;
            default:
                return false;
        }
    }

    /// <summary>右側インライン詳細の出し分け。幅再計算も行う。</summary>
    public void SetInlineDetailVisible(bool visible)
    {
        if (_inlineDetail == visible)
        {
            ApplyInlineDetailLayout();
            FitContentWidth();
            return;
        }

        _inlineDetail = visible;
        ApplyInlineDetailLayout();
        FitContentWidth();
    }

    private void ApplyInlineDetailLayout()
    {
        var show = _inlineDetail && !string.IsNullOrEmpty(_detailText);
        _detailFrame.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            var detailW = DetailFittedWidth;
            var treeW = TreeFittedWidth;
            _detailColumn.MinWidth = DesignMetrics.EditorHeaderDetailMinWidth + PaneGap;
            _detailColumn.Width = new GridLength(detailW + PaneGap);
            // 本文幅（高水位）＋すき間まで伸ばす。上限で切ると横スクロール／見切れになる。
            MaxWidth = treeW + PaneGap + detailW;
        }
        else
        {
            _detailColumn.MinWidth = 0;
            _detailColumn.Width = new GridLength(0);
            MaxWidth = DesignMetrics.EditorHeaderChunkStripMaxWidth;
        }

        SyncPaneFocusChrome();
    }

    private void Populate(WaveStructureDocument document)
    {
        _multiSelected.Clear();
        _selectionAnchor = null;
        _tree.Items.Clear();
        if (document.IsUnsupported || document.Roots.Count == 0)
        {
            SetDetailText(string.Empty);
            ApplyInlineDetailLayout();
            Width = DesignMetrics.EditorHeaderChunkStripMinWidth;
            return;
        }

        if (document.IsChunkUnion)
        {
            var unionNameWidth = WaveStructureNode.NameFieldWidth(0);
            var countWidth = 1;
            foreach (var root in document.Roots)
            {
                countWidth = Math.Max(
                    countWidth,
                    root.Size.ToString("#,0", CultureInfo.InvariantCulture).Length);
            }

            foreach (var root in document.Roots)
            {
                _tree.Items.Add(
                    CreateItem(root, depth: 0, unionNameWidth, countWidth, unionFileCounts: true));
            }

            // 複数タブは和集合一覧のみ。情報エリアは出さない。
            SelectedNode = null;
            SetDetailText(string.Empty);
            FitContentWidth();
            return;
        }

        // コンテナ根はツリー親にせず1行。子をトップレベルにする。
        var displayRoots = WaveStructureNode.ForTreeDisplay(document.Roots);
        var sizeFieldWidth = WaveStructureNode.MaxSizeFieldWidth(displayRoots);
        var nameFieldWidth = WaveStructureNode.NameFieldWidth(WaveStructureNode.MaxDepth(displayRoots));
        TreeViewItem? first = null;
        foreach (var root in displayRoots)
        {
            var item = CreateItem(root, depth: 0, nameFieldWidth, sizeFieldWidth);
            _tree.Items.Add(item);
            first ??= item;
        }

        if (first is not null)
        {
            SelectFirstRowDetail();
        }
        else
        {
            SelectedNode = null;
            SetDetailText(string.Empty);
            FitContentWidth();
        }
    }

    /// <summary>
    /// ツリー1行目を選び、その詳細を必ず載せる。
    /// H 再表示／読込直後用。選択記憶は持たない。
    /// </summary>
    public void SelectFirstRowDetail()
    {
        if (_tree.Items.OfType<TreeViewItem>().FirstOrDefault() is not { } first)
        {
            SelectedNode = null;
            SetDetailText(string.Empty);
            ApplyInlineDetailLayout();
            FitContentWidth();
            return;
        }

        // IsSelected だけだと SelectedItemChanged が飛ばず詳細が空のまま残ることがある。
        first.IsSelected = true;
        ApplyNodeDetail(first);
    }

    private void RefreshSelectedDetail()
    {
        if (_tree.SelectedItem is TreeViewItem item)
        {
            ApplyNodeDetail(item);
            return;
        }

        SelectedNode = null;
        SetDetailText(string.Empty, notify: false);
        ApplyInlineDetailLayout();
        FitContentWidth();
        DetailChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyNodeDetail(TreeViewItem item, bool notify = true)
    {
        if (item.Tag is not WaveStructureNode node)
        {
            SelectedNode = null;
            SetDetailText(string.Empty, notify: false);
            ApplyInlineDetailLayout();
            FitContentWidth();
            if (notify)
            {
                DetailChanged?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        // 和集合は詳細を出さない。単一ファイル時だけ本文を読む。
        if (_loadedDocument is { IsChunkUnion: true })
        {
            SelectedNode = null;
            SetDetailText(string.Empty, notify: false);
            ApplyInlineDetailLayout();
            FitContentWidth();
            if (notify)
            {
                DetailChanged?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        // ファイル読込時はヘッダのみ。本文はチャンク選択で読む。
        if (node.ContentPending
            && !string.IsNullOrWhiteSpace(_loadedPath)
            && !_loadedPath.Contains('\u0001'))
        {
            node = WaveContainerStructure.EnsureContent(_loadedPath, node);
            item.Tag = node;
        }

        SelectedNode = node;
        SetDetailText(WaveStructureNode.FormatNodeDetail(node), notify: false);
        ApplyInlineDetailLayout();
        FitContentWidth();
        if (notify)
        {
            DetailChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetDetailText(string text, bool notify = true)
    {
        if (!string.Equals(_detailText, text, StringComparison.Ordinal))
        {
            _detailText = text;
            _detail.Text = text;
            _detailFittedWidth = MeasureDetailWidth(text);
            // バイナリ等は TextBox が VerticalOffset を残すので、チャンク切替では先頭へ。
            // 高さ確定前だと戻し切れないことがあるので Loaded でもう一度。
            _detail.ScrollToHome();
            _detail.Dispatcher.BeginInvoke(
                () => _detail.ScrollToHome(),
                DispatcherPriority.Loaded);
        }

        if (notify)
        {
            DetailChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>キャプション／詳細幅に合わせて帯幅を決める。高水位は自動では下げない。</summary>
    private void FitContentWidth()
    {
        var treeNatural = MeasureTreeWidth();
        var hasTreeOrRepair = _tree.Items.Count > 0
            || _repairPanel.Visibility == Visibility.Visible;
        if (hasTreeOrRepair)
        {
            _treeHeldWidth = Math.Max(_treeHeldWidth, treeNatural);
        }

        _treeFittedWidth = hasTreeOrRepair
            ? Math.Max(_treeHeldWidth, treeNatural)
            : DesignMetrics.EditorHeaderChunkStripMinWidth;

        if (!hasTreeOrRepair)
        {
            Width = DesignMetrics.EditorHeaderChunkStripMinWidth;
            return;
        }

        if (_inlineDetail && !string.IsNullOrEmpty(_detailText))
        {
            var detailNatural = MeasureDetailWidth(_detailText);
            _detailHeldWidth = Math.Max(_detailHeldWidth, detailNatural);
            _detailFittedWidth = _detailHeldWidth;
            _detailColumn.Width = new GridLength(_detailFittedWidth + PaneGap);
            var total = _treeFittedWidth + PaneGap + _detailFittedWidth;
            MaxWidth = total;
            Width = Math.Max(
                DesignMetrics.EditorHeaderChunkStripMinWidth
                    + PaneGap
                    + DesignMetrics.EditorHeaderDetailMinWidth,
                total);
            return;
        }

        Width = Math.Clamp(
            _treeFittedWidth,
            DesignMetrics.EditorHeaderChunkStripMinWidth,
            DesignMetrics.EditorHeaderChunkStripMaxWidth);
    }

    private double MeasureTreeWidth()
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (dpi <= 0)
        {
            dpi = 1;
        }

        var typeface = new Typeface(MonoFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var content = _tree.Items.Count == 0
            ? 0d
            : LibraryBrowserView.MeasureStructureTreeWidth(
                _tree.Items,
                depth: 0,
                childIndent: 0,
                typeface,
                fontSize: 11,
                dpi,
                expanderWidth: 16);

        if (_repairPanel.Visibility == Visibility.Visible)
        {
            foreach (var text in new[]
                     {
                         UiStrings.WaveHeaderRepairButton,
                         FormatRepairPrompt(),
                         UiStrings.WaveHeaderRepairApplyEdit,
                     })
            {
                var ft = new FormattedText(
                    text,
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    11,
                    Brushes.Black,
                    dpi);
                content = Math.Max(content, ft.WidthIncludingTrailingWhitespace);
            }
        }

        if (_tree.Items.Count == 0 && _repairPanel.Visibility != Visibility.Visible)
        {
            return DesignMetrics.EditorHeaderChunkStripMinWidth;
        }

        var chrome = _treeFrame.BorderThickness.Left
            + _treeFrame.BorderThickness.Right
            + _treeFrame.Padding.Left
            + _treeFrame.Padding.Right
            + _tree.Padding.Left
            + _tree.Padding.Right
            + _repairPanel.Margin.Left
            + _repairPanel.Margin.Right
            + DesignMetrics.SettingsScrollBarWidth
            + DesignMetrics.From96(4);
        var needed = content + chrome;
        return Math.Clamp(
            needed,
            DesignMetrics.EditorHeaderChunkStripMinWidth,
            DesignMetrics.EditorHeaderChunkStripMaxWidth);
    }

    private double MeasureDetailWidth(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (dpi <= 0)
        {
            dpi = 1;
        }

        var typeface = new Typeface(MonoFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double maxLine = 0;
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0)
            {
                continue;
            }

            var ft = new FormattedText(
                line,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                11,
                Brushes.Black,
                dpi);
            maxLine = Math.Max(maxLine, ft.WidthIncludingTrailingWhitespace);
        }

        var chrome = _detailFrame.BorderThickness.Left
            + _detailFrame.BorderThickness.Right
            + _detailFrame.Padding.Left
            + _detailFrame.Padding.Right
            + _detail.Padding.Left
            + _detail.Padding.Right
            + DesignMetrics.SettingsScrollBarWidth
            + DesignMetrics.From96(4);
        // 行の自然幅。収まらなければ呼び出し側が波形オーバーレイへ逃がす。
        return Math.Max(DesignMetrics.EditorHeaderDetailMinWidth, maxLine + chrome);
    }

    private TreeViewItem CreateItem(
        WaveStructureNode node,
        int depth,
        int nameFieldWidth,
        int sizeFieldWidth,
        bool unionFileCounts = false)
    {
        // 可=赤、注意=オレンジ、保護=シアン、不可=通常。不正／空 FourCC はエラー色。
        var brushKey = WaveChunkCleanPolicy.ResolveForeBrushKey(node);
        var item = new TreeViewItem
        {
            Tag = node,
            // エディタは全閉じを既定。
            IsExpanded = false,
            Header = new TextBlock
            {
                Text = unionFileCounts
                    ? node.FormatUnionTreeCaption(nameFieldWidth, sizeFieldWidth)
                    : node.FormatTreeCaption(depth, nameFieldWidth, sizeFieldWidth),
                FontFamily = MonoFont,
                FontSize = 11,
                FontWeight = FontWeights.Normal,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
        };
        if (item.Header is TextBlock header)
        {
            header.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        }

        item.SetResourceReference(Control.ForegroundProperty, brushKey);
        if (_tree.ItemContainerStyle is { } style)
        {
            item.Style = style;
            item.ItemContainerStyle = style;
        }

        foreach (var child in node.Children)
        {
            item.Items.Add(
                CreateItem(child, depth + 1, nameFieldWidth, sizeFieldWidth, unionFileCounts));
        }

        return item;
    }

    private void ApplyTreeItemStyle()
    {
        var hover = LibraryBrowserView.GrayHoverBrush();
        var selected = LibraryBrowserView.CyanSelectionBrush();
        var fore = TryFindResource("PrimaryForeBrush") as Brush ?? Brushes.WhiteSmoke;
        LibraryBrowserView.ApplyNavSelectionResources(_tree, selected, fore);

        var itemStyle = new Style(typeof(TreeViewItem));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2, 1, 4, 1)));
        itemStyle.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        itemStyle.Setters.Add(new Setter(UIElement.ClipToBoundsProperty, false));
        itemStyle.Setters.Add(new Setter(Control.FontFamilyProperty, MonoFont));
        itemStyle.Setters.Add(new Setter(Control.FontSizeProperty, 11d));
        itemStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Normal));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
        itemStyle.Setters.Add(new Setter(
            Control.TemplateProperty,
            LibraryBrowserView.StructureTreeNavTemplate(hover, selected, stretchRow: true)));
        _tree.ItemContainerStyle = itemStyle;
        _tree.Resources[typeof(TreeViewItem)] = itemStyle;
    }

    private void RebuildTreeContextMenu()
    {
        var menu = new ContextMenu();
        menu.Opened += (_, _) =>
        {
            menu.Items.Clear();
            var hasRows = FlattenVisibleItems().Count > 0;
            var selected = CollectDeleteTargets();
            var selectAll = new MenuItem
            {
                Header = UiStrings.WaveMenuSelectAll,
                InputGestureText = "Ctrl+A",
                IsEnabled = hasRows,
            };
            selectAll.Click += (_, _) => SelectAllItems();
            var copy = new MenuItem
            {
                Header = UiStrings.WaveMenuCopy,
                InputGestureText = "Ctrl+C",
                IsEnabled = hasRows,
            };
            copy.Click += (_, _) => CopySelection();
            var layout = new MenuItem
            {
                Header = UiStrings.WaveChannelLayoutEditMenu,
                IsEnabled = HasChannelLayoutTargets(),
            };
            layout.Click += (_, _) => _ = EditChannelLayoutAsync();
            var delete = new MenuItem
            {
                Header = selected.Count > 1
                    ? UiStrings.WaveChunkDeleteMenuMany
                    : UiStrings.WaveChunkDeleteMenu,
                // 不可のみ（****／fmt／data 等）はグレー。和集合は MemberPaths 経由。
                IsEnabled = selected.Count > 0
                    && (_loadedDocument is { IsChunkUnion: true }
                        || !string.IsNullOrWhiteSpace(_loadedPath))
                    && WaveChunkDeleteUi.HasDeletableSelection(selected),
            };
            delete.Click += (_, _) => _ = DeleteChunksAsync();
            menu.Items.Add(selectAll);
            menu.Items.Add(copy);
            menu.Items.Add(new Separator());
            menu.Items.Add(layout);
            menu.Items.Add(delete);
        };
        _tree.ContextMenu = menu;
    }

    private bool HasChannelLayoutTargets()
    {
        foreach (var path in ResolveChannelLayoutTargetPaths())
        {
            if (WaveChannelLayoutWriter.IsSupportedPath(path))
            {
                return true;
            }
        }

        return false;
    }

    private IReadOnlyList<string> ResolveChannelLayoutTargetPaths()
    {
        var fromHost = ResolveChannelLayoutPaths?.Invoke();
        if (fromHost is { Count: > 0 })
        {
            return fromHost;
        }

        if (_loadedDocument is { IsChunkUnion: true })
        {
            var paths = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in _loadedDocument.Roots)
            {
                foreach (var member in node.MemberPaths)
                {
                    if (seen.Add(member))
                    {
                        paths.Add(member);
                    }
                }
            }

            return paths;
        }

        return string.IsNullOrWhiteSpace(_loadedPath)
            ? Array.Empty<string>()
            : new[] { _loadedPath };
    }

    private async Task EditChannelLayoutAsync()
    {
        if (ApplyChannelLayoutEditAsync is null)
        {
            return;
        }

        var paths = ResolveChannelLayoutTargetPaths();
        var targets = new List<WaveChannelLayoutWriter.TargetInfo>();
        foreach (var path in paths)
        {
            if (WaveChannelLayoutWriter.TryDescribe(path, out var info))
            {
                targets.Add(info);
            }
        }

        if (targets.Count == 0)
        {
            RepairMessage?.Invoke(UiStrings.WaveChannelLayoutNoTargets);
            return;
        }

        var owner = Window.GetWindow(this);
        var result = ChannelLayoutEditDialog.Show(owner, targets);
        if (result is null)
        {
            return;
        }

        var apply = WaveChannelLayoutWriter.FilterTargetsForMask(targets, result.Value.Mask);
        if (apply.Count == 0)
        {
            RepairMessage?.Invoke(UiStrings.WaveChannelLayoutNoTargets);
            return;
        }

        _ = await ApplyChannelLayoutEditAsync(
            apply.Select(static t => t.Path).ToArray(),
            result.Value.Mask,
            result.Value.SpeakerPresetId).ConfigureAwait(true);
    }

    private List<TreeViewItem> CollectDeleteTargets()
    {
        var list = new List<TreeViewItem>();
        foreach (var item in FlattenVisibleItems())
        {
            if (_multiSelected.Contains(item))
            {
                list.Add(item);
            }
        }

        if (list.Count > 0)
        {
            return list;
        }

        if (_tree.SelectedItem is TreeViewItem selected)
        {
            list.Add(selected);
        }

        return list;
    }

    private async Task DeleteChunksAsync()
    {
        if (_loadedDocument is null && string.IsNullOrWhiteSpace(_loadedPath))
        {
            return;
        }

        var targets = CollectDeleteTargets();
        if (targets.Count == 0)
        {
            return;
        }

        // 和集合は MemberPaths 必須。単一パスに union キャッシュキーを渡さない。
        var singlePath = _loadedDocument?.IsChunkUnion == true
            ? null
            : _loadedPath;
        var plan = WaveChunkDeleteUi.BuildPlan(targets, singlePath);
        if (plan is null)
        {
            return;
        }

        var owner = Window.GetWindow(this);
        if (!WaveChunkDeleteUi.Confirm(owner, plan.Value))
        {
            return;
        }

        if (plan.Value.PathToChunkIds.Count == 0)
        {
            RepairMessage?.Invoke(UiStrings.WaveChunkDeleteForbiddenOnly);
            return;
        }

        if (ApplyChunkDeleteEditAsync is null)
        {
            RepairMessage?.Invoke(UiStrings.WaveChunkStripFailed("unavailable"));
            return;
        }

        try
        {
            var kinds = plan.Value.Entries.Count(static e => e.Verdict != WaveChunkCleanVerdict.Forbidden);
            _ = await ApplyChunkDeleteEditAsync(plan.Value.PathToChunkIds, kinds).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            RepairMessage?.Invoke(UiStrings.WaveChunkStripFailed(ex.Message));
        }
    }

    internal string BuildCopyText()
    {
        var lines = new List<string>();
        foreach (var item in FlattenVisibleItems())
        {
            if (!_multiSelected.Contains(item))
            {
                continue;
            }

            if (ItemCopyLine(item) is { Length: > 0 } line)
            {
                lines.Add(line);
            }
        }

        if (lines.Count > 0)
        {
            return string.Join(Environment.NewLine, lines);
        }

        if (_tree.SelectedItem is TreeViewItem selected)
        {
            return ItemCopyLine(selected) ?? string.Empty;
        }

        return string.Empty;
    }

    private string? ItemCopyLine(TreeViewItem item)
    {
        if (item.Tag is not WaveStructureNode node)
        {
            return SanitizeCopyText(item.Header switch
            {
                TextBlock block => block.Text,
                string text => text,
                _ => item.Header?.ToString(),
            });
        }

        var name = node.FormatDisplayFourCc().TrimEnd();
        if (name.Length == 0)
        {
            name = WaveStructureNode.BlankFourCcListLabel;
        }

        if (_loadedDocument is { IsChunkUnion: true })
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{name}\t{node.Size:#,0}");
        }

        var depth = LibraryBrowserView.MeasureTreeIndentDepth(item);
        var indent = depth > 0
            ? new string(' ', depth * WaveStructureNode.TreeIndentSpaces)
            : string.Empty;
        return indent + name + "\t" + WaveStructureNode.FormatSize(node.Size);
    }

    /// <summary>クリップボードは埋め込み NUL を嫌うので除去する。</summary>
    internal static string? SanitizeCopyText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        if (text.IndexOf('\0') < 0)
        {
            return text;
        }

        return text.Replace('\0', '?');
    }

    private void SelectTreeItem(TreeViewItem item)
    {
        ExpandAncestors(item);
        item.IsSelected = true;
        item.BringIntoView();
        if (IsTreeFocused || IsKeyboardFocusWithin)
        {
            item.Focus();
        }
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
            if (origin is ToggleButton toggle
                && string.Equals(toggle.Name, "Expander", StringComparison.Ordinal))
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

    private static void ExpandAncestors(TreeViewItem item)
    {
        for (var parent = ItemsControl.ItemsControlFromItemContainer(item) as TreeViewItem;
             parent is not null;
             parent = ItemsControl.ItemsControlFromItemContainer(parent) as TreeViewItem)
        {
            parent.IsExpanded = true;
        }
    }

    private bool TryArrowExpandCollapse(Key key)
    {
        if (_tree.SelectedItem is not TreeViewItem item || !item.HasItems)
        {
            return false;
        }

        if (key == Key.Right)
        {
            if (!item.IsExpanded)
            {
                item.IsExpanded = true;
                return true;
            }

            if (item.Items.OfType<TreeViewItem>().FirstOrDefault() is { } firstChild)
            {
                SelectTreeItem(firstChild);
            }

            return true;
        }

        if (item.IsExpanded)
        {
            item.IsExpanded = false;
            return true;
        }

        if (ItemsControl.ItemsControlFromItemContainer(item) is TreeViewItem parent)
        {
            SelectTreeItem(parent);
            return true;
        }

        return true;
    }

    private static void ExpandSubtree(TreeViewItem item)
    {
        if (!item.HasItems)
        {
            return;
        }

        item.IsExpanded = true;
        foreach (var child in item.Items.OfType<TreeViewItem>())
        {
            ExpandSubtree(child);
        }
    }

    private static void CollapseSubtree(TreeViewItem item)
    {
        foreach (var child in item.Items.OfType<TreeViewItem>())
        {
            CollapseSubtree(child);
        }

        if (item.HasItems)
        {
            item.IsExpanded = false;
        }
    }

    private List<TreeViewItem> FlattenVisibleItems() => FlattenVisibleTreeItems(_tree.Items);

    internal static List<TreeViewItem> FlattenVisibleTreeItems(ItemCollection roots)
    {
        var list = new List<TreeViewItem>();
        void Walk(ItemCollection items)
        {
            foreach (var raw in items)
            {
                if (raw is not TreeViewItem item)
                {
                    continue;
                }

                list.Add(item);
                if (item.IsExpanded && item.Items.Count > 0)
                {
                    Walk(item.Items);
                }
            }
        }

        Walk(roots);
        return list;
    }

    private static ScrollViewer? FindDescendantScrollViewer(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer viewer)
            {
                return viewer;
            }

            var nested = FindDescendantScrollViewer(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
