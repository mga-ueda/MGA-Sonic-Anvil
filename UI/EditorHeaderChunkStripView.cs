using System.Globalization;
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
    private string? _loadedPath;
    private string _detailText = string.Empty;
    private bool _inlineDetail;
    private double _treeFittedWidth = DesignMetrics.EditorHeaderChunkStripMinWidth;
    private double _detailFittedWidth = DesignMetrics.EditorHeaderDetailMinWidth;
    /// <summary>ツリー列の高水位。自動では下げない。</summary>
    private double _treeHeldWidth;
    /// <summary>情報列の高水位。自動では下げない。</summary>
    private double _detailHeldWidth;
    /// <summary>Ctrl+A 全選択マーク中。通常の選択変更で外す。</summary>
    private bool _selectAll;

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
        _tree.SelectedItemChanged += (_, _) =>
        {
            ClearSelectAllMarks();
            RefreshSelectedDetail();
        };
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
        _treeFrame.Child = _tree;
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

        if (string.Equals(path, _loadedPath, StringComparison.OrdinalIgnoreCase)
            && _tree.Items.Count > 0)
        {
            return;
        }

        _loadedPath = path;
        var document = WaveContainerStructure.TryRead(path);
        Populate(document);
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
        ClearSelectAllMarks();
        _loadedPath = null;
        _tree.Items.Clear();
        SelectedNode = null;
        SetDetailText(string.Empty);
        ApplyInlineDetailLayout();
    }

    /// <summary>チャンクツリーにキーボードフォーカスがある。</summary>
    public bool IsTreeFocused => _tree.IsKeyboardFocusWithin;

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

    /// <summary>表示中のチャンク行をすべてマーク（コピー用）。</summary>
    public void SelectAllItems()
    {
        var items = FlattenVisibleItems();
        if (items.Count == 0)
        {
            return;
        }

        _selectAll = true;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            item.SetValue(LibraryBrowserView.TreeMarkedProperty, true);
            item.SetValue(
                LibraryBrowserView.SelectionBandEdgeProperty,
                SelectionBandCorners.EdgeAt(i, items.Count));
        }

        if (!_tree.IsKeyboardFocusWithin)
        {
            FocusTree();
        }
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

    public void MoveSelection(int delta)
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

        var current = _tree.SelectedItem as TreeViewItem;
        var index = current is null ? -1 : items.IndexOf(current);
        if (index < 0)
        {
            index = delta > 0 ? -1 : items.Count;
        }

        var next = Math.Clamp(index + delta, 0, items.Count - 1);
        SelectTreeItem(items[next]);
    }

    public void MoveSelectionToEdge(int edge)
    {
        var items = FlattenVisibleItems();
        if (items.Count == 0)
        {
            return;
        }

        SelectTreeItem(edge < 0 ? items[0] : items[^1]);
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
        _tree.Items.Clear();
        if (document.IsUnsupported || document.Roots.Count == 0)
        {
            SetDetailText(string.Empty);
            ApplyInlineDetailLayout();
            Width = DesignMetrics.EditorHeaderChunkStripMinWidth;
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

        // ファイル読込時はヘッダのみ。本文はチャンク選択で読む。
        if (node.ContentPending && !string.IsNullOrWhiteSpace(_loadedPath))
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
        if (_tree.Items.Count > 0)
        {
            _treeHeldWidth = Math.Max(_treeHeldWidth, treeNatural);
        }

        _treeFittedWidth = _tree.Items.Count == 0
            ? DesignMetrics.EditorHeaderChunkStripMinWidth
            : Math.Max(_treeHeldWidth, treeNatural);

        if (_tree.Items.Count == 0)
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
        if (_tree.Items.Count == 0)
        {
            return DesignMetrics.EditorHeaderChunkStripMinWidth;
        }

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (dpi <= 0)
        {
            dpi = 1;
        }

        var typeface = new Typeface(MonoFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var content = LibraryBrowserView.MeasureStructureTreeWidth(
            _tree.Items,
            depth: 0,
            childIndent: 0,
            typeface,
            fontSize: 11,
            dpi,
            expanderWidth: 16);
        var chrome = _treeFrame.BorderThickness.Left
            + _treeFrame.BorderThickness.Right
            + _treeFrame.Padding.Left
            + _treeFrame.Padding.Right
            + _tree.Padding.Left
            + _tree.Padding.Right
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
        int sizeFieldWidth)
    {
        // 空 FourCC は IsCustom にも当たるが、不正色（赤）を優先。
        var brushKey = node.IsInvalid || WaveStructureNode.IsBlankFourCc(node.Id)
            ? LibraryBrowserView.MissingRegisteredPathForeBrushKey
            : node.IsCustom
                ? LibraryBrowserView.WaveStructureCustomChunkForeBrushKey
                : node.IsSpecialized
                    ? LibraryBrowserView.WaveStructureSpecializedChunkForeBrushKey
                    : "PrimaryForeBrush";
        var item = new TreeViewItem
        {
            Tag = node,
            // エディタは全閉じを既定。
            IsExpanded = false,
            Header = new TextBlock
            {
                Text = node.FormatTreeCaption(depth, nameFieldWidth, sizeFieldWidth),
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
            item.Items.Add(CreateItem(child, depth + 1, nameFieldWidth, sizeFieldWidth));
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
            menu.Items.Add(selectAll);
            menu.Items.Add(copy);
        };
        _tree.ContextMenu = menu;
    }

    private string BuildCopyText()
    {
        if (_selectAll)
        {
            var lines = new List<string>();
            foreach (var item in FlattenVisibleItems())
            {
                if (ItemCopyLine(item) is { Length: > 0 } line)
                {
                    lines.Add(line);
                }
            }

            return string.Join(Environment.NewLine, lines);
        }

        if (_tree.SelectedItem is TreeViewItem selected)
        {
            return ItemCopyLine(selected) ?? string.Empty;
        }

        return string.Empty;
    }

    private static string? ItemCopyLine(TreeViewItem item)
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

    private void ClearSelectAllMarks()
    {
        if (!_selectAll)
        {
            return;
        }

        _selectAll = false;
        foreach (var item in FlattenVisibleItems())
        {
            item.SetValue(LibraryBrowserView.TreeMarkedProperty, false);
            item.SetValue(LibraryBrowserView.SelectionBandEdgeProperty, SelectionBandEdge.None);
        }
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
