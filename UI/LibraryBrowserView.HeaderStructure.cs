using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

internal sealed partial class LibraryBrowserView
{
    /// <summary>XML ツリーの子インデント（等幅スペース約4文字分）。</summary>
    private static double StructureXmlChildIndent => DesignMetrics.From96(28);
    /// <summary>ヘッダ構造ツリー用。Vista 以降の Windows に標準で入る等幅。</summary>
    private static readonly FontFamily StructureTreeMonoFont = new("Consolas");
    /// <summary>情報エリア用。同上 Consolas（Cascadia は環境によって無い）。</summary>
    private static readonly FontFamily StructureDetailMonoFont = StructureTreeMonoFont;

    /// <summary>
    /// Consolas の │ は em 高さいっぱいに描かれるので、行送り＝フォントサイズで繋げる。
    /// </summary>
    private const double StructureDetailLineHeightFactor = 1d;
    /// <summary>Fit 再入・Extent 補正のループ防止。</summary>
    private bool _structureFitBusy;
    /// <summary>上限到達後は Extent 補正で幅を積み増さない。</summary>
    private bool _structureDetailClipGrowBlocked;
    /// <summary>チャンク切替などのプログラムスクロールではバーを出さない。</summary>
    private bool _structureDetailScrollRevealSuppress;

    private void BuildHeaderStructurePane()
    {
        _structureColumn.Width = new GridLength(DesignMetrics.LibraryHeaderStructureWidth);
        _structureColumn.MinWidth = DesignMetrics.LibraryHeaderStructureMinWidth;
        _structureSplitterColumn.Width = new GridLength(DesignMetrics.LibraryExplorerSplitterWidth);

        ApplyPaneTitleStyle(_structureLabel, new Thickness(8, 6, 8, 8));
        _structureLabel.Text = UiStrings.LibraryHeaderStructureLabel;
        ApplyPaneTitleStyle(_structureDetailLabel, new Thickness(8, 6, 8, 8));
        _structureDetailLabel.Text = UiStrings.LibraryHeaderStructureDetailLabel;

        _structureErrorBanner.Margin = new Thickness(8, 0, 8, 4);
        _structureErrorBanner.Padding = new Thickness(8, 4, 8, 4);
        _structureErrorBanner.Background = Brushes.Transparent;
        _structureErrorBanner.Visibility = Visibility.Collapsed;
        _structureErrorBanner.Child = _structureErrorText;
        _structureErrorText.TextWrapping = TextWrapping.Wrap;
        _structureErrorText.FontSize = LibraryListFontSize;
        _structureErrorText.FontWeight = FontWeights.Normal;
        _structureErrorText.SetResourceReference(
            TextBlock.ForegroundProperty,
            MissingRegisteredPathForeBrushKey);

        ApplyHeaderStructureTreeStyle();
        _structureTree.SelectedItemChanged += StructureTree_SelectedItemChanged;
        _structureTree.IsKeyboardFocusWithinChanged += (_, _) => SyncPaneFocusChrome();
        _structureTree.PreviewKeyDown += StructurePane_PreviewKeyDown;
        RebuildHeaderStructureTreeContextMenu();

        _structureDetailBox.IsReadOnly = true;
        _structureDetailBox.AcceptsReturn = true;
        _structureDetailBox.TextWrapping = TextWrapping.NoWrap;
        _structureDetailBox.BorderThickness = new Thickness(0);
        _structureDetailBox.Background = Brushes.Transparent;
        _structureDetailBox.CaretBrush = Brushes.Transparent;
        _structureDetailBox.Padding = new Thickness(8, 6, 8, 8);
        _structureDetailBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        // 横スクロールは禁止。足りなければ Fit で列幅を広げる。
        _structureDetailBox.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _structureDetailBox.SetResourceReference(Control.ForegroundProperty, "PrimaryForeBrush");
        // Style で Regular を固定（テーマや継承の SemiBold/Bold を受けない）。
        var detailStyle = new Style(typeof(TextBox));
        detailStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Normal));
        detailStyle.Setters.Add(new Setter(Control.FontStyleProperty, FontStyles.Normal));
        detailStyle.Setters.Add(new Setter(Control.FontStretchProperty, FontStretches.Normal));
        _structureDetailBox.Style = detailStyle;
        ApplyHeaderStructureDetailTypography(_structureDetailBox);
        ApplyStructureDetailLineHeight(_structureDetailBox, LibraryListFontSize);
        HookStructureDetailScrollFit(_structureDetailBox);
        _structureDetailBox.IsKeyboardFocusWithinChanged += (_, _) => SyncPaneFocusChrome();
        _structureDetailBox.PreviewKeyDown += StructurePane_PreviewKeyDown;
        ScrollViewer.SetCanContentScroll(_structureDetailBox, true);

        _structureDetailMeta.FontFamily = StructureDetailMonoFont;
        _structureDetailMeta.TextWrapping = TextWrapping.NoWrap;
        _structureDetailMeta.Margin = new Thickness(8, 6, 8, 2);
        _structureDetailMeta.Visibility = Visibility.Collapsed;
        _structureDetailMeta.SetResourceReference(TextBlock.ForegroundProperty, "MutedForeBrush");
        ApplyHeaderStructureDetailTypography(_structureDetailMeta);
        ApplyStructureDetailLineHeight(_structureDetailMeta, LibraryListFontSize);

        ApplyHeaderStructureXmlTreeStyle();
        _structureXmlTree.Visibility = Visibility.Collapsed;
        _structureXmlTree.IsKeyboardFocusWithinChanged += (_, _) => SyncPaneFocusChrome();
        _structureXmlTree.PreviewKeyDown += StructurePane_PreviewKeyDown;

        _structureTreeColumn.Width = new GridLength(DesignMetrics.LibraryHeaderStructureTreeWidth);
        _structureTreeColumn.MinWidth = DesignMetrics.LibraryHeaderStructureTreeMinWidth;
        _structureDetailColumn.Width = new GridLength(1, GridUnitType.Star);
        _structureDetailColumn.MinWidth = DesignMetrics.From96(96);
        var body = new Grid { Background = Brushes.Transparent };
        body.ColumnDefinitions.Add(_structureTreeColumn);
        body.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(DesignMetrics.LibraryExplorerSplitterWidth),
        });
        body.ColumnDefinitions.Add(_structureDetailColumn);
        _structureBody = body;

        // ヘッダ構造（ツリー）と情報エリアを列単位で分離。各列に見出し＋フォーカスグラデ。
        var treePane = new DockPanel { Background = Brushes.Transparent };
        DockPanel.SetDock(_structureLabel, Dock.Top);
        treePane.Children.Add(_structureLabel);
        treePane.Children.Add(_structureTree);
        var treeHost = new Grid { Background = Brushes.Transparent };
        treeHost.Children.Add(treePane);
        treeHost.Children.Add(_structureFocusLine);
        Grid.SetColumn(treeHost, 0);
        body.Children.Add(treeHost);

        var innerSplitter = new LibraryHoverSplitter
        {
            Width = DesignMetrics.LibrarySplitterHitThickness,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Stretch,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            ResizeDirection = GridResizeDirection.Columns,
            Cursor = Cursors.SizeWE,
            SnapsToDevicePixels = true,
        };
        ApplyLibrarySplitterChrome(innerSplitter);
        Grid.SetColumn(innerSplitter, 1);
        Panel.SetZIndex(innerSplitter, 8);
        body.Children.Add(innerSplitter);

        var detailBody = new Grid { Background = Brushes.Transparent };
        detailBody.Children.Add(_structureDetailBox);
        detailBody.Children.Add(_structureXmlTree);
        var detailContent = new DockPanel { Background = Brushes.Transparent };
        DockPanel.SetDock(_structureDetailMeta, Dock.Top);
        detailContent.Children.Add(_structureDetailMeta);
        detailContent.Children.Add(detailBody);
        var detailPane = new DockPanel { Background = Brushes.Transparent };
        DockPanel.SetDock(_structureDetailLabel, Dock.Top);
        detailPane.Children.Add(_structureDetailLabel);
        detailPane.Children.Add(detailContent);
        var detailHost = new Grid { Background = Brushes.Transparent };
        detailHost.Children.Add(detailPane);
        detailHost.Children.Add(_structureDetailFocusLine);
        Grid.SetColumn(detailHost, 2);
        body.Children.Add(detailHost);

        var pane = new DockPanel { Background = Brushes.Transparent };
        DockPanel.SetDock(_structureErrorBanner, Dock.Top);
        pane.Children.Add(_structureErrorBanner);
        pane.Children.Add(body);

        var host = new Grid { Background = Brushes.Transparent };
        host.Children.Add(pane);
        _structurePaneHost = host;
        ApplyStructureContentFonts();
        SizeChanged += (_, _) => FitHeaderStructurePaneWidths();
        if (!_headerStructureVisible)
        {
            ApplyHeaderStructureCollapsedChrome();
        }
    }

    /// <summary>非表示時の列・スプリッター・ホスト。イベントは上げない。</summary>
    private void ApplyHeaderStructureCollapsedChrome()
    {
        _structureColumn.MinWidth = 0;
        _structureColumn.MaxWidth = 0;
        _structureColumn.Width = new GridLength(0);
        _structureSplitterColumn.Width = new GridLength(0);
        _structureSplitter.Visibility = Visibility.Collapsed;
        if (_structurePaneHost is { } host)
        {
            host.Visibility = Visibility.Collapsed;
        }
    }

    private void ApplyHeaderStructureTreeStyle()
    {
        _structureTree.Background = Brushes.Transparent;
        _structureTree.BorderThickness = new Thickness(0);
        _structureTree.Padding = new Thickness(4, 4, 4, 6);
        ApplyHeaderStructureDetailTypography(_structureTree);
        ScrollViewer.SetHorizontalScrollBarVisibility(_structureTree, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(_structureTree, ScrollBarVisibility.Auto);
        _structureTree.SetResourceReference(ForegroundProperty, "PrimaryForeBrush");
        VirtualizingPanel.SetIsVirtualizing(_structureTree, true);
        VirtualizingPanel.SetVirtualizationMode(_structureTree, VirtualizationMode.Recycling);

        var hover = GrayHoverBrush();
        var selected = CyanSelectionBrush();
        var fore = ResolveThemeBrush("PrimaryForeBrush");
        ApplyNavSelectionResources(_structureTree, selected, fore);

        var itemStyle = new Style(typeof(TreeViewItem));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2, 1, 4, 1)));
        itemStyle.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        itemStyle.Setters.Add(new Setter(UIElement.ClipToBoundsProperty, false));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
        itemStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Normal));
        itemStyle.Setters.Add(new Setter(Control.FontStretchProperty, FontStretches.Normal));
        itemStyle.Setters.Add(new Setter(Control.TemplateProperty, StructureTreeNavTemplate(hover, selected)));
        _structureTree.Resources[typeof(TreeViewItem)] = itemStyle;
        _structureTree.ItemContainerStyle = itemStyle;
    }

    private void ApplyHeaderStructureXmlTreeStyle()
    {
        _structureXmlTree.Background = Brushes.Transparent;
        _structureXmlTree.BorderThickness = new Thickness(0);
        _structureXmlTree.Padding = new Thickness(4, 4, 4, 6);
        _structureXmlTree.FontFamily = StructureDetailMonoFont;
        ApplyHeaderStructureDetailTypography(_structureXmlTree);
        // 横スクロールは禁止。足りなければ Fit で列幅を広げる。
        ScrollViewer.SetHorizontalScrollBarVisibility(_structureXmlTree, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(_structureXmlTree, ScrollBarVisibility.Auto);
        _structureXmlTree.SetResourceReference(ForegroundProperty, "PrimaryForeBrush");
        // XML はパース済みを一括表示。仮想化すると Extent が不正確でツマミ精度が落ちる。
        VirtualizingPanel.SetIsVirtualizing(_structureXmlTree, false);
        HookStructureDetailScrollFit(_structureXmlTree);
        // 展開で深い行が出たとき幅を取り直す。
        _structureXmlTree.AddHandler(
            TreeViewItem.ExpandedEvent,
            new RoutedEventHandler((_, _) => ScheduleXmlDetailWidthSync()),
            handledEventsToo: true);

        var hover = GrayHoverBrush();
        var selected = CyanSelectionBrush();
        var fore = ResolveThemeBrush("PrimaryForeBrush");
        ApplyNavSelectionResources(_structureXmlTree, selected, fore);

        var itemStyle = new Style(typeof(TreeViewItem));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        // 縦パディングなし。三角もフォント高に合わせて行間を詰める。
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2, 0, 4, 0)));
        itemStyle.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        itemStyle.Setters.Add(new Setter(UIElement.ClipToBoundsProperty, false));
        // 選択帯は情報エリアの左端から右端まで。
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        itemStyle.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
        itemStyle.Setters.Add(new Setter(Control.FontFamilyProperty, StructureDetailMonoFont));
        itemStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Normal));
        itemStyle.Setters.Add(new Setter(Control.FontStretchProperty, FontStretches.Normal));
        itemStyle.Setters.Add(new Setter(
            Control.TemplateProperty,
            StructureTreeNavTemplate(
                hover,
                selected,
                // 字下げは各行 Padding 側。ItemsHost マージンだと選択帯も右へ寄る。
                childIndent: 0,
                stretchRow: true,
                expanderSize: StructureXmlExpanderSize,
                expanderGlyphSize: 9)));
        _structureXmlTree.Resources[typeof(TreeViewItem)] = itemStyle;
        _structureXmlTree.ItemContainerStyle = itemStyle;
    }

    /// <summary>XML 行の三角。フォント高＋α（葉でも Hidden で幅を占有する）。</summary>
    private static double StructureXmlExpanderSize => LibraryListFontSize + 1;

    public void FocusHeaderStructure()
    {
        if (!_headerStructureVisible)
        {
            FocusList();
            return;
        }

        ClearExplorerTypeahead();
        CancelPlaylistFocusRestore();
        ReleasePlaylistFocusHold();
        _listFocusTicket++;
        if (_structureTree.SelectedItem is TreeViewItem selected)
        {
            selected.Focus();
            SyncPaneFocusChrome();
            return;
        }

        if (_structureTree.Items.OfType<TreeViewItem>().FirstOrDefault() is { } first)
        {
            first.IsSelected = true;
            first.Focus();
            SyncPaneFocusChrome();
            return;
        }

        _structureTree.Focus();
        SyncPaneFocusChrome();
    }

    public void FocusHeaderStructureDetail()
    {
        if (!_headerStructureVisible)
        {
            FocusList();
            return;
        }

        ClearExplorerTypeahead();
        CancelPlaylistFocusRestore();
        ReleasePlaylistFocusHold();
        _listFocusTicket++;
        if (_structureXmlTree.Visibility == Visibility.Visible)
        {
            if (_structureXmlTree.SelectedItem is TreeViewItem selected)
            {
                selected.Focus();
                SyncPaneFocusChrome();
                return;
            }

            if (_structureXmlTree.Items.OfType<TreeViewItem>().FirstOrDefault() is { } first)
            {
                first.IsSelected = true;
                first.Focus();
                SyncPaneFocusChrome();
                return;
            }

            _structureXmlTree.Focus();
            SyncPaneFocusChrome();
            return;
        }

        _structureDetailBox.Focus();
        SyncPaneFocusChrome();
    }

    private void StructurePane_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        // ツリー ↔ 情報エリア ↔ プレイリスト。
        if (key == Key.Left && modifiers == ModifierKeys.Control)
        {
            if (IsHeaderStructureDetailFocused)
            {
                FocusHeaderStructure();
            }
            else
            {
                FocusList();
            }

            e.Handled = true;
            return;
        }

        if (key == Key.Right
            && modifiers == ModifierKeys.Control
            && IsHeaderStructureTreeFocused)
        {
            FocusHeaderStructureDetail();
            e.Handled = true;
            return;
        }

        if (TryHandleHeaderStructureExpandKey(key, modifiers))
        {
            e.Handled = true;
            return;
        }

        if (IsHeaderStructureDetailFocused
            && modifiers == ModifierKeys.None
            && (key is Key.Up or Key.Down or Key.PageUp or Key.PageDown)
            && TryHandleHeaderStructureDetailNavKey(key))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// ヘッダ構造／XML の展開キー。ライブラリツリーと同じ *／/／Shift+/ と、三角行の←→。
    /// </summary>
    internal bool TryHandleHeaderStructureExpandKey(Key key, ModifierKeys modifiers)
    {
        var tree = ResolveFocusedStructureNavTree();
        if (tree is null)
        {
            return false;
        }

        if (LibraryPlayerMode.IsExplorerExpandAll(key, modifiers))
        {
            if (tree.SelectedItem is TreeViewItem selected)
            {
                ExpandStructureSubtree(selected);
            }

            return true;
        }

        if (LibraryPlayerMode.IsExplorerCollapseSubtree(key, modifiers))
        {
            if (tree.SelectedItem is TreeViewItem selected)
            {
                CollapseStructureSubtree(selected);
            }

            return true;
        }

        if (LibraryPlayerMode.IsExplorerCollapseAllRoots(key, modifiers))
        {
            CollapseAllStructureRoots(tree);
            return true;
        }

        if (key is Key.Left or Key.Right && modifiers == ModifierKeys.None)
        {
            return TryArrowExpandCollapseStructureItem(tree, key);
        }

        return false;
    }

    private TreeView? ResolveFocusedStructureNavTree()
    {
        if (_structureXmlTree.Visibility == Visibility.Visible
            && (IsHeaderStructureDetailFocused || _structureXmlTree.IsKeyboardFocusWithin))
        {
            return _structureXmlTree;
        }

        if (IsHeaderStructureTreeFocused || _structureTree.IsKeyboardFocusWithin)
        {
            return _structureTree;
        }

        return null;
    }

    private static void ExpandStructureSubtree(TreeViewItem item)
    {
        if (!item.HasItems)
        {
            return;
        }

        item.IsExpanded = true;
        foreach (var child in item.Items.OfType<TreeViewItem>())
        {
            ExpandStructureSubtree(child);
        }
    }

    private static void CollapseStructureSubtree(TreeViewItem item)
    {
        foreach (var child in item.Items.OfType<TreeViewItem>())
        {
            CollapseStructureSubtree(child);
        }

        if (item.HasItems)
        {
            item.IsExpanded = false;
        }
    }

    private static void CollapseAllStructureRoots(TreeView tree)
    {
        foreach (var root in tree.Items.OfType<TreeViewItem>())
        {
            CollapseStructureSubtree(root);
        }
    }

    /// <summary>三角がある行だけ ←→ で展開／折りたたみ（WPF ツリーと同じ）。</summary>
    private bool TryArrowExpandCollapseStructureItem(TreeView tree, Key key)
    {
        if (tree.SelectedItem is not TreeViewItem item || !item.HasItems)
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
                SelectStructureNavItem(tree, firstChild);
            }

            return true;
        }

        // Left
        if (item.IsExpanded)
        {
            item.IsExpanded = false;
            return true;
        }

        if (ItemsControl.ItemsControlFromItemContainer(item) is TreeViewItem parent)
        {
            SelectStructureNavItem(tree, parent);
            return true;
        }

        return true;
    }

    private void SelectStructureNavItem(TreeView tree, TreeViewItem item)
    {
        if (ReferenceEquals(tree, _structureTree))
        {
            SelectStructureTreeItem(item, remember: true);
            return;
        }

        item.IsSelected = true;
        item.BringIntoView();
        if (IsHeaderStructureDetailFocused || tree.IsKeyboardFocusWithin)
        {
            item.Focus();
        }
    }

    /// <summary>
    /// 情報エリアの↑↓／PgUp／PgDn。
    /// XML 表示中は行選択を動かす（Pg は1ページ分）。テキストはスクロールのみ。
    /// </summary>
    internal bool TryHandleHeaderStructureDetailNavKey(Key key)
    {
        if (!IsHeaderStructureDetailFocused)
        {
            return false;
        }

        if (_structureXmlTree.Visibility == Visibility.Visible
            && key is Key.Up or Key.Down or Key.PageUp or Key.PageDown)
        {
            var delta = key switch
            {
                Key.Up => -1,
                Key.Down => 1,
                Key.PageUp => -XmlDetailPageStep(),
                Key.PageDown => XmlDetailPageStep(),
                _ => 0,
            };
            MoveHeaderStructureXmlSelection(delta);
            return true;
        }

        return TryScrollHeaderStructureDetail(key);
    }

    /// <summary>情報テキストをスクロールする。XML ナビは行選択側へ。</summary>
    internal bool TryScrollHeaderStructureDetail(Key key)
    {
        if (!IsHeaderStructureDetailFocused)
        {
            return false;
        }

        if (_structureXmlTree.Visibility == Visibility.Visible)
        {
            return false;
        }

        var viewer = FindDescendantScrollViewer(_structureDetailBox);
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

    /// <summary>XML 表示中の1ページ分の行数（プレイリストと同じ visible-1）。</summary>
    private int XmlDetailPageStep()
    {
        var row = Math.Max(1d, LibraryListFontSize + 1);
        var viewer = FindDescendantScrollViewer(_structureXmlTree);
        var visible = viewer is { ViewportHeight: > 1 }
            ? Math.Max(1, (int)Math.Floor(viewer.ViewportHeight / row))
            : 10;
        return LibraryPlayerMode.PageStep(visible);
    }

    /// <summary>XML ツリーの表示中行を選択移動する（BringIntoView で追随スクロール）。</summary>
    internal void MoveHeaderStructureXmlSelection(int delta)
    {
        if (delta == 0 || _structureXmlTree.Visibility != Visibility.Visible)
        {
            return;
        }

        var items = EditorHeaderChunkStripView.FlattenVisibleTreeItems(_structureXmlTree.Items);
        if (items.Count == 0)
        {
            return;
        }

        var current = _structureXmlTree.SelectedItem as TreeViewItem;
        var index = current is null ? -1 : items.IndexOf(current);
        if (index < 0)
        {
            index = delta > 0 ? -1 : items.Count;
        }

        var next = Math.Clamp(index + delta, 0, items.Count - 1);
        var item = items[next];
        item.IsSelected = true;
        item.BringIntoView();
        if (IsHeaderStructureDetailFocused)
        {
            item.Focus();
        }
    }

    /// <summary>
    /// 選択変更からのヘッダ再読込。
    /// ナビキー押し中は解析せず、離したときに読む。
    /// 複数選択の和集合は選択ハイライト確定後に少し待ってから読む。
    /// </summary>
    private void RequestHeaderStructureRefresh()
    {
        if (_suppressHeaderStructureRefresh || !_headerStructureVisible)
        {
            return;
        }

        if (IsPlaylistNavigationKeyHeld())
        {
            ScheduleDeferredHeaderStructureRefresh();
            return;
        }

        // 全選択など。和集合のディスク走査を選択 UI の後ろへ回す。
        if (SelectedSessions.Length > 1)
        {
            ScheduleSettledHeaderStructureRefresh();
            return;
        }

        _headerStructureRefreshDeferred = false;
        _headerStructureRefreshDeferTicket++;
        RefreshHeaderStructure();
    }

    /// <summary>テスト用。null なら実キー状態を見る。</summary>
    internal Func<bool>? PlaylistNavigationKeyHeldForTest { get; set; }

    /// <summary>テスト用。キー離し待ちのヘッダ再読込が溜まっているか。</summary>
    internal bool IsHeaderStructureRefreshDeferred => _headerStructureRefreshDeferred;

    /// <summary>テスト用。いま表示しているヘッダの読み込み元パス（和集合キー含む）。</summary>
    internal string? HeaderStructureLoadedPath => _structureLoadedPath;

    private bool IsPlaylistNavigationKeyHeld() =>
        PlaylistNavigationKeyHeldForTest?.Invoke()
        ?? (Keyboard.IsKeyDown(Key.Up)
            || Keyboard.IsKeyDown(Key.Down)
            || Keyboard.IsKeyDown(Key.Home)
            || Keyboard.IsKeyDown(Key.End)
            || Keyboard.IsKeyDown(Key.PageUp)
            || Keyboard.IsKeyDown(Key.PageDown)
            || Keyboard.IsKeyDown(Key.NumPad4)
            || Keyboard.IsKeyDown(Key.NumPad6));

    private void ScheduleDeferredHeaderStructureRefresh()
    {
        _headerStructureRefreshDeferred = true;
        var ticket = ++_headerStructureRefreshDeferTicket;
        Dispatcher.BeginInvoke(
            () =>
            {
                if (ticket != _headerStructureRefreshDeferTicket
                    || !_headerStructureRefreshDeferred)
                {
                    return;
                }

                if (IsPlaylistNavigationKeyHeld())
                {
                    return;
                }

                FlushDeferredHeaderStructureRefresh();
            },
            DispatcherPriority.Background);
    }

    /// <summary>
    /// 複数選択確定後、短く待ってから和集合を読む。連続操作はチケットでまとめる。
    /// </summary>
    private void ScheduleSettledHeaderStructureRefresh()
    {
        _headerStructureRefreshDeferred = true;
        var ticket = ++_headerStructureRefreshDeferTicket;
        var timer = new DispatcherTimer { Interval = HeaderStructureSettleDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (ticket != _headerStructureRefreshDeferTicket
                || !_headerStructureRefreshDeferred)
            {
                return;
            }

            if (IsPlaylistNavigationKeyHeld())
            {
                return;
            }

            FlushDeferredHeaderStructureRefresh();
        };
        timer.Start();
    }

    /// <summary>↑↓／Page／テンキー 4・6 を離したあと、または複数選択の待ち後に溜めたヘッダ解析を実行する。</summary>
    internal void FlushDeferredHeaderStructureRefresh()
    {
        if (!_headerStructureRefreshDeferred)
        {
            return;
        }

        if (IsPlaylistNavigationKeyHeld())
        {
            return;
        }

        _headerStructureRefreshDeferred = false;
        _headerStructureRefreshDeferTicket++;
        if (!_suppressHeaderStructureRefresh)
        {
            RefreshHeaderStructure();
        }
    }

    private void RefreshHeaderStructure()
    {
        // 非表示時は読まない。選択変更・追加（Append/SetSessions）もここ経由なので、
        // オフ中のプレイリスト操作の重さにヘッダ解析が乗らない。
        if (!_headerStructureVisible)
        {
            return;
        }

        if (SnapshotPlaylistFiles().Count == 0)
        {
            ClearHeaderStructure();
            return;
        }

        var sessions = SelectedSessions;
        if (sessions.Length == 0)
        {
            ClearHeaderStructure();
            return;
        }

        if (sessions.Length > 1)
        {
            BeginHeaderStructureUnion(sessions);
            return;
        }

        // 単一選択は即時。進行中の和集合は破棄する。
        _structureUnionBuildTicket++;
        _structureUnionPendingKey = null;

        var path = sessions[0].Document.SourcePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            ClearHeaderStructure();
            return;
        }

        if (string.Equals(path, _structureLoadedPath, StringComparison.OrdinalIgnoreCase)
            && _structureDocument is not null
            && !_structureDocument.IsChunkUnion)
        {
            return;
        }

        _structureLoadedPath = path;
        var document = WaveContainerStructure.TryRead(path);
        _structureDocument = document;
        PopulateHeaderStructure(document);
    }

    /// <summary>
    /// 複数選択のチャンク和集合。ディスク走査は裏スレッド、TreeView 更新だけ UI。
    /// </summary>
    private void BeginHeaderStructureUnion(IReadOnlyList<DocumentSession> sessions)
    {
        var paths = new List<string>(sessions.Count);
        foreach (var session in sessions)
        {
            var p = session.Document.SourcePath;
            if (!string.IsNullOrWhiteSpace(p))
            {
                paths.Add(p);
            }
        }

        if (paths.Count == 0)
        {
            ClearHeaderStructure();
            return;
        }

        var cacheParts = new string[paths.Count];
        for (var i = 0; i < paths.Count; i++)
        {
            cacheParts[i] = paths[i].ToUpperInvariant();
        }

        var cacheKey = "\u0001union\u0001" + string.Join('\u0001', cacheParts);
        if (string.Equals(cacheKey, _structureLoadedPath, StringComparison.Ordinal)
            && _structureDocument is { IsChunkUnion: true })
        {
            _structureUnionPendingKey = null;
            return;
        }

        if (string.Equals(cacheKey, _structureUnionPendingKey, StringComparison.Ordinal))
        {
            return;
        }

        _structureUnionPendingKey = cacheKey;
        var ticket = ++_structureUnionBuildTicket;
        _ = Task.Run(() => WaveContainerStructure.BuildChunkUnion(paths))
            .ContinueWith(task =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (ticket != _structureUnionBuildTicket
                        || !string.Equals(cacheKey, _structureUnionPendingKey, StringComparison.Ordinal)
                        || !_headerStructureVisible
                        || _suppressHeaderStructureRefresh)
                    {
                        return;
                    }

                    if (task.IsFaulted || task.IsCanceled)
                    {
                        return;
                    }

                    var document = task.Result;
                    _structureUnionPendingKey = null;
                    _structureLoadedPath = cacheKey;
                    _structureDocument = document;
                    PopulateHeaderStructure(document);
                });
            });
    }

    /// <summary>
    /// 選択なし／プレイリスト空／H で隠すとき。情報は出さず、幅の高水位も忘れる。
    /// </summary>
    private void ClearHeaderStructure()
    {
        DiscardHeaderStructureContent();
        _structureDetailClipGrowBlocked = false;
        _structureUserWidth = null;
        _structureAutoHeldWidth = 0;
        _structureTreeHeldWidth = 0;
        _structureDetailHeldWidth = 0;
    }

    /// <summary>ツリー／本文だけ空ける。列幅の記憶は触らない（表示中の再解析抑止用）。</summary>
    private void DiscardHeaderStructureContent()
    {
        _structureTreeSelectAll = false;
        _structureUnionBuildTicket++;
        _structureUnionPendingKey = null;
        _structureLoadedPath = null;
        _structureDocument = null;
        _structureTree.Items.Clear();
        _structureErrorBanner.Visibility = Visibility.Collapsed;
        _structureErrorText.Text = string.Empty;
        ShowStructureDetail(string.Empty);
    }

    private void PopulateHeaderStructure(WaveStructureDocument document)
    {
        _structureTree.Items.Clear();
        if (document.IsUnsupported)
        {
            // mp3 等はメッセージを出さず空欄のまま。
            _structureErrorBanner.Visibility = Visibility.Collapsed;
            _structureErrorText.Text = string.Empty;
            ShowStructureDetail(string.Empty);
            return;
        }

        if (document.IsChunkUnion)
        {
            _structureErrorBanner.Visibility = Visibility.Collapsed;
            _structureErrorText.Text = string.Empty;
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
                _structureTree.Items.Add(
                    CreateStructureTreeItem(
                        root,
                        depth: 0,
                        unionNameWidth,
                        countWidth,
                        unionFileCounts: true));
            }

            // 複数選択は和集合一覧のみ。情報エリアは出さない。
            ShowStructureDetail(string.Empty);
            return;
        }

        if (document.HasErrors)
        {
            _structureErrorText.Text = string.Join(Environment.NewLine, document.Errors);
            _structureErrorBanner.Visibility = Visibility.Visible;
        }
        else
        {
            _structureErrorBanner.Visibility = Visibility.Collapsed;
            _structureErrorText.Text = string.Empty;
        }

        // コンテナ根はツリー親にせず1行。子をトップレベルにする。
        var displayRoots = WaveStructureNode.ForTreeDisplay(document.Roots);
        var sizeFieldWidth = WaveStructureNode.MaxSizeFieldWidth(displayRoots);
        var nameFieldWidth = WaveStructureNode.NameFieldWidth(WaveStructureNode.MaxDepth(displayRoots));
        TreeViewItem? first = null;
        foreach (var root in displayRoots)
        {
            var item = CreateStructureTreeItem(root, depth: 0, nameFieldWidth, sizeFieldWidth);
            _structureTree.Items.Add(item);
            first ??= item;
        }

        if (first is not null)
        {
            if (!TrySelectRememberedStructureChunk())
            {
                // 記憶チャンクが無いときだけ1行目を見せる。記憶自体は消さない。
                SelectStructureTreeItem(first, remember: false);
            }
        }
        else if (document.HasErrors)
        {
            ShowStructureDetail(string.Join(Environment.NewLine, document.Errors));
        }
        else
        {
            ShowStructureDetail(UiStrings.WaveStructureSelectNode);
        }
    }

    private TreeViewItem CreateStructureTreeItem(
        WaveStructureNode node,
        int depth,
        int nameFieldWidth,
        int sizeFieldWidth,
        bool unionFileCounts = false)
    {
        var item = new TreeViewItem
        {
            Tag = node,
            IsExpanded = node.Children.Count > 0 && node.Children.Count <= 24,
        };
        if (_structureTree.ItemContainerStyle is { } style)
        {
            item.Style = style;
            item.ItemContainerStyle = style;
        }

        // 不正／空 FourCC=赤、独自=オレンジ、専門メタ=シアン、コア=通常。
        // 空 FourCC は IsCustom にも当たるが、不正色を優先して情報欄と揃える。
        var brushKey = node.IsInvalid || WaveStructureNode.IsBlankFourCc(node.Id)
            ? MissingRegisteredPathForeBrushKey
            : node.IsCustom
                ? WaveStructureCustomChunkForeBrushKey
                : node.IsSpecialized
                    ? WaveStructureSpecializedChunkForeBrushKey
                    : "PrimaryForeBrush";
        var header = new TextBlock
        {
            Text = unionFileCounts
                ? node.FormatUnionTreeCaption(nameFieldWidth, sizeFieldWidth)
                : node.FormatTreeCaption(depth, nameFieldWidth, sizeFieldWidth),
            FontFamily = StructureTreeMonoFont,
            FontSize = LibraryListFontSize,
            FontWeight = FontWeights.Normal,
            FontStyle = FontStyles.Normal,
            FontStretch = FontStretches.Normal,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.None,
        };
        ApplyHeaderStructureDetailTypography(header);
        header.FontFamily = StructureTreeMonoFont;
        header.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        item.Header = header;
        item.FontWeight = FontWeights.Normal;
        item.SetResourceReference(Control.ForegroundProperty, brushKey);

        foreach (var child in node.Children)
        {
            item.Items.Add(
                CreateStructureTreeItem(child, depth + 1, nameFieldWidth, sizeFieldWidth, unionFileCounts));
        }

        return item;
    }

    private void StructureTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        // 通常クリック／矢印移動では全選択マークを外す。
        ClearHeaderStructureTreeSelectAllMarks();

        if (_structureDocument is { IsChunkUnion: true })
        {
            ShowStructureDetail(string.Empty);
            if (_structureTree.SelectedItem is TreeViewItem { Tag: WaveStructureNode unionNode })
            {
                SelectPlaylistSessionsMatchingUnionChunk(unionNode);
            }

            return;
        }

        if (_structureTree.SelectedItem is TreeViewItem { Tag: WaveStructureNode node } item)
        {
            if (!_structureSuppressRemember)
            {
                RememberStructureChunkPath(item);
            }

            ShowStructureNode(node);
        }
    }

    private void RebuildHeaderStructureTreeContextMenu()
    {
        var menu = new ContextMenu();
        menu.Opened += (_, _) =>
        {
            menu.Items.Clear();
            var hasRows = FlattenVisibleStructureItems().Count > 0;
            var selectAll = new MenuItem
            {
                Header = UiStrings.WaveMenuSelectAll,
                InputGestureText = "Ctrl+A",
                IsEnabled = hasRows,
            };
            selectAll.Click += (_, _) => SelectAllHeaderStructureItems();
            var copy = new MenuItem
            {
                Header = UiStrings.WaveMenuCopy,
                InputGestureText = "Ctrl+C",
                IsEnabled = hasRows,
            };
            copy.Click += (_, _) => CopyHeaderStructureSelection();
            menu.Items.Add(selectAll);
            menu.Items.Add(copy);
        };
        ArmContextMenuDismissGuard(menu);
        _structureTree.ContextMenu = menu;
    }

    /// <summary>表示中のチャンク行をすべてマーク（コピー用）。</summary>
    public void SelectAllHeaderStructureItems()
    {
        if (!_headerStructureVisible)
        {
            return;
        }

        var items = FlattenVisibleStructureItems();
        if (items.Count == 0)
        {
            return;
        }

        _structureTreeSelectAll = true;
        // プレイリストと同じ連続帯：先頭／末尾だけ角丸、中間は直角。
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            item.SetValue(TreeMarkedProperty, true);
            item.SetValue(
                SelectionBandEdgeProperty,
                SelectionBandCorners.EdgeAt(i, items.Count));
        }

        if (!_structureTree.IsKeyboardFocusWithin)
        {
            FocusHeaderStructure();
        }
    }

    /// <summary>
    /// 全選択中なら全行、そうでなければ選択行をタブ区切りでクリップボードへ。
    /// </summary>
    public void CopyHeaderStructureSelection()
    {
        if (!_headerStructureVisible)
        {
            return;
        }

        var text = BuildHeaderStructureCopyText();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        SystemClipboard.TrySetText(text, Window.GetWindow(this));
    }

    private string BuildHeaderStructureCopyText()
    {
        if (_structureTreeSelectAll)
        {
            var lines = new List<string>();
            foreach (var item in FlattenVisibleStructureItems())
            {
                if (StructureTreeItemCopyLine(item) is { Length: > 0 } line)
                {
                    lines.Add(line);
                }
            }

            return string.Join(Environment.NewLine, lines);
        }

        if (_structureTree.SelectedItem is TreeViewItem selected)
        {
            return StructureTreeItemCopyLine(selected) ?? string.Empty;
        }

        return string.Empty;
    }

    /// <summary>コピー用。表示の " - " ではなくタブ区切り（FourCC〈TAB〉値）。</summary>
    private string? StructureTreeItemCopyLine(TreeViewItem item)
    {
        if (item.Tag is not WaveStructureNode node)
        {
            return EditorHeaderChunkStripView.SanitizeCopyText(item.Header switch
            {
                TextBlock block => block.Text,
                string text => text,
                _ => item.Header?.ToString(),
            });
        }

        // 空 FourCC は ----。通常は FourCC（末尾空白は表貼り付け用に落とす）。
        var name = node.FormatDisplayFourCc().TrimEnd();
        if (name.Length == 0)
        {
            name = WaveStructureNode.BlankFourCcListLabel;
        }

        if (_structureDocument is { IsChunkUnion: true })
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{name}\t{node.Size:#,0}");
        }

        var depth = MeasureTreeIndentDepth(item);
        var indent = depth > 0
            ? new string(' ', depth * WaveStructureNode.TreeIndentSpaces)
            : string.Empty;
        return indent + name + "\t" + WaveStructureNode.FormatSize(node.Size);
    }

    private void ClearHeaderStructureTreeSelectAllMarks()
    {
        if (!_structureTreeSelectAll)
        {
            return;
        }

        _structureTreeSelectAll = false;
        foreach (var item in FlattenVisibleStructureItems())
        {
            item.SetValue(TreeMarkedProperty, false);
            item.SetValue(SelectionBandEdgeProperty, SelectionBandEdge.None);
        }
    }

    /// <summary>
    /// 和集合のチャンク行を選んだとき、そのチャンクを持つプレイリスト行を選択する。
    /// 和集合ツリー自体は作り直さない。
    /// </summary>
    private void SelectPlaylistSessionsMatchingUnionChunk(WaveStructureNode node)
    {
        if (node.MemberPaths.Count == 0)
        {
            return;
        }

        var pathSet = new HashSet<string>(node.MemberPaths, StringComparer.OrdinalIgnoreCase);
        var selected = new List<DocumentSession>();
        DocumentSession? caret = null;
        foreach (var row in BoundRows)
        {
            if (row.Tag is not DocumentSession session)
            {
                continue;
            }

            var path = session.Document.SourcePath;
            if (string.IsNullOrWhiteSpace(path) || !pathSet.Contains(path))
            {
                continue;
            }

            selected.Add(session);
            caret ??= session;
        }

        if (selected.Count == 0 || caret is null)
        {
            return;
        }

        _suppressHeaderStructureRefresh = true;
        try
        {
            ApplyRowSelectionCore(caret, selected);
        }
        finally
        {
            _suppressHeaderStructureRefresh = false;
        }
    }

    private void ShowStructureNode(WaveStructureNode? node)
    {
        if (_structureDocument is { IsChunkUnion: true })
        {
            ShowStructureDetail(string.Empty);
            return;
        }

        if (node is null)
        {
            ShowStructureDetail(UiStrings.WaveStructureSelectNode);
            return;
        }

        // ファイル選択時はヘッダのみ。本文はチャンク選択で読む。
        if (node.ContentPending
            && !string.IsNullOrWhiteSpace(_structureLoadedPath)
            && _structureTree.SelectedItem is TreeViewItem selected)
        {
            node = WaveContainerStructure.EnsureContent(_structureLoadedPath, node);
            selected.Tag = node;
        }

        if (node.Kind == WaveStructureContentKind.Xml && !node.IsInvalid)
        {
            var meta = WaveStructureNode.FormatKeyValueTable(
            [
                ("Offset", string.Create(CultureInfo.InvariantCulture, $"0x{node.Offset:X} ({node.Offset})")),
                ("Size", WaveStructureNode.FormatSize(node.Size)),
            ]);
            if (TryShowStructureXml(node.Content, meta))
            {
                return;
            }
        }

        ShowStructureDetail(WaveStructureNode.FormatNodeDetail(node));
    }

    private bool TryShowStructureXml(string xml, string meta)
    {
        if (!WaveStructureXmlTree.TryPopulate(_structureXmlTree, xml))
        {
            return false;
        }

        _structureDetailClipGrowBlocked = false;
        _structureDetailMeta.Text = meta;
        _structureDetailMeta.Visibility = Visibility.Visible;
        _structureDetailBox.Visibility = Visibility.Collapsed;
        _structureXmlTree.Visibility = Visibility.Visible;
        ResetStructureDetailScroll(_structureXmlTree);
        FitHeaderStructurePaneWidths();
        ScheduleXmlDetailWidthSync();
        return true;
    }

    /// <summary>情報／XML のスクロール位置を先頭へ。レイアウト後にもう一度。Reveal は抑止する。</summary>
    private void ResetStructureDetailScroll(FrameworkElement host)
    {
        _structureDetailScrollRevealSuppress = true;
        ScrollStructureDetailHostToHome(host);
        Dispatcher.BeginInvoke(
            () =>
            {
                ScrollStructureDetailHostToHome(host);
                _structureDetailScrollRevealSuppress = false;
            },
            DispatcherPriority.Loaded);
    }

    private static void ScrollStructureDetailHostToHome(FrameworkElement host)
    {
        if (host is TextBoxBase textBox)
        {
            textBox.ScrollToHome();
        }

        if (FindDescendantScrollViewer(host) is { } viewer)
        {
            viewer.ScrollToHome();
        }
    }

    public void MoveHeaderStructureSelection(int delta)
    {
        if (delta == 0)
        {
            return;
        }

        var items = FlattenVisibleStructureItems();
        if (items.Count == 0)
        {
            return;
        }

        var current = _structureTree.SelectedItem as TreeViewItem;
        var index = current is null ? -1 : items.IndexOf(current);
        if (index < 0)
        {
            index = delta > 0 ? -1 : items.Count;
        }

        var next = Math.Clamp(index + delta, 0, items.Count - 1);
        SelectStructureTreeItem(items[next], remember: true);
    }

    public void MoveHeaderStructureSelectionToEdge(int edge)
    {
        var items = FlattenVisibleStructureItems();
        if (items.Count == 0)
        {
            return;
        }

        SelectStructureTreeItem(edge < 0 ? items[0] : items[^1], remember: true);
    }

    private void SelectStructureTreeItem(TreeViewItem item, bool remember)
    {
        ExpandStructureAncestors(item);
        var previousSuppress = _structureSuppressRemember;
        _structureSuppressRemember = !remember;
        try
        {
            item.IsSelected = true;
            item.BringIntoView();
            if (IsHeaderStructureFocused)
            {
                item.Focus();
            }

            if (item.Tag is WaveStructureNode node)
            {
                if (remember)
                {
                    RememberStructureChunkPath(item);
                }

                ShowStructureNode(node);
            }
        }
        finally
        {
            _structureSuppressRemember = previousSuppress;
        }
    }

    private List<TreeViewItem> FlattenVisibleStructureItems() =>
        EditorHeaderChunkStripView.FlattenVisibleTreeItems(_structureTree.Items);

    private void RememberStructureChunkPath(TreeViewItem item)
    {
        var path = new List<string>();
        DependencyObject? current = item;
        while (current is not null)
        {
            if (current is TreeViewItem { Tag: WaveStructureNode node })
            {
                path.Insert(0, node.SelectionKey);
            }

            if (current is TreeView || ReferenceEquals(current, _structureTree))
            {
                break;
            }

            current = current is TreeViewItem container
                ? ItemsControl.ItemsControlFromItemContainer(container)
                : VisualTreeHelper.GetParent(current);
        }

        if (path.Count > 0)
        {
            _structureRememberedChunkPath = path.ToArray();
        }
    }

    private bool TrySelectRememberedStructureChunk()
    {
        var remembered = _structureRememberedChunkPath;
        if (remembered is not { Length: > 0 })
        {
            return false;
        }

        if (TryFindStructureItemByPath(remembered) is { } exact)
        {
            SelectStructureTreeItem(exact, remember: true);
            return true;
        }

        // 完全一致がなければ末尾のチャンク種別だけ探す（例: fmt  / bext / data）。
        var key = remembered[^1];
        if (TryFindStructureItemByKey(key) is { } byKey)
        {
            SelectStructureTreeItem(byKey, remember: true);
            return true;
        }

        return false;
    }

    private TreeViewItem? TryFindStructureItemByPath(IReadOnlyList<string> path)
    {
        ItemCollection items = _structureTree.Items;
        TreeViewItem? current = null;
        for (var i = 0; i < path.Count; i++)
        {
            TreeViewItem? match = null;
            foreach (var raw in items)
            {
                if (raw is TreeViewItem { Tag: WaveStructureNode node } item
                    && string.Equals(node.SelectionKey, path[i], StringComparison.Ordinal))
                {
                    match = item;
                    break;
                }
            }

            if (match is null)
            {
                return null;
            }

            current = match;
            if (i + 1 < path.Count)
            {
                match.IsExpanded = true;
                items = match.Items;
            }
        }

        return current;
    }

    private TreeViewItem? TryFindStructureItemByKey(string key)
    {
        TreeViewItem? found = null;
        void Walk(ItemCollection items)
        {
            if (found is not null)
            {
                return;
            }

            foreach (var raw in items)
            {
                if (raw is not TreeViewItem item)
                {
                    continue;
                }

                if (item.Tag is WaveStructureNode node
                    && string.Equals(node.SelectionKey, key, StringComparison.Ordinal))
                {
                    found = item;
                    return;
                }

                if (item.Items.Count > 0)
                {
                    item.IsExpanded = true;
                    Walk(item.Items);
                    if (found is not null)
                    {
                        return;
                    }
                }
            }
        }

        Walk(_structureTree.Items);
        return found;
    }

    private static void ExpandStructureAncestors(TreeViewItem item)
    {
        for (DependencyObject? current = ItemsControl.ItemsControlFromItemContainer(item);
             current is not null;
             current = ItemsControl.ItemsControlFromItemContainer(current) as DependencyObject
                 ?? VisualTreeHelper.GetParent(current))
        {
            if (current is TreeViewItem parent)
            {
                parent.IsExpanded = true;
            }

            if (current is TreeView)
            {
                break;
            }
        }
    }

    private void ShowStructureDetail(string text)
    {
        _structureDetailClipGrowBlocked = false;
        _structureXmlTree.Items.Clear();
        _structureXmlTree.Visibility = Visibility.Collapsed;
        _structureDetailMeta.Visibility = Visibility.Collapsed;
        _structureDetailMeta.Text = string.Empty;
        _structureDetailBox.Visibility = Visibility.Visible;
        // キー／値表やグリッドは固定ピッチ前提。メッセージ類も同じ等幅で揃える。
        _structureDetailBox.FontFamily = StructureDetailMonoFont;
        ApplyHeaderStructureDetailTypography(_structureDetailBox);
        _structureDetailBox.Text = text;
        // バイナリ等は TextBox が VerticalOffset を残すので、チャンク切替では先頭へ。
        ResetStructureDetailScroll(_structureDetailBox);
        FitHeaderStructurePaneWidths();
    }

    /// <summary>情報表示／構造ツリーは常に Regular。Display で細くシャープに描く。</summary>
    internal static void ApplyHeaderStructureDetailTypography(Control control)
    {
        control.FontSize = LibraryListFontSize;
        control.FontWeight = FontWeights.Normal;
        control.FontStyle = FontStyles.Normal;
        control.FontStretch = FontStretches.Normal;
        TextOptions.SetTextFormattingMode(control, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(control, TextRenderingMode.ClearType);
        TextOptions.SetTextHintingMode(control, TextHintingMode.Fixed);
        TextElement.SetFontWeight(control, FontWeights.Normal);
        TextElement.SetFontStyle(control, FontStyles.Normal);
    }

    internal static void ApplyHeaderStructureDetailTypography(TextBlock block)
    {
        block.FontSize = LibraryListFontSize;
        block.FontWeight = FontWeights.Normal;
        block.FontStyle = FontStyles.Normal;
        block.FontStretch = FontStretches.Normal;
        TextOptions.SetTextFormattingMode(block, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(block, TextRenderingMode.ClearType);
        TextOptions.SetTextHintingMode(block, TextHintingMode.Fixed);
    }

    /// <summary>情報テキストの行送り。│ が上下で密着しないようにする。</summary>
    internal static void ApplyStructureDetailLineHeight(DependencyObject target, double fontSize)
    {
        TextBlock.SetLineStackingStrategy(target, LineStackingStrategy.BlockLineHeight);
        TextBlock.SetLineHeight(target, Math.Max(1, fontSize * StructureDetailLineHeightFactor));
    }

    /// <summary>
    /// 情報／iXML の列幅 Fit。スクロールバーはホバーに加え、実スクロールでも既定時間出す。
    /// </summary>
    private void HookStructureDetailScrollFit(FrameworkElement host)
    {
        host.Loaded += StructureDetailScrollHost_Loaded;
        if (host is TextBox textBox)
        {
            textBox.AddHandler(
                ScrollViewer.ScrollChangedEvent,
                new ScrollChangedEventHandler(StructureDetailScrollHost_ScrollChanged),
                handledEventsToo: true);
        }
    }

    private void StructureDetailScrollHost_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement host)
        {
            return;
        }

        if (FindDescendantScrollViewer(host) is { } viewer)
        {
            viewer.ScrollChanged -= StructureDetailScrollHost_ScrollChanged;
            viewer.ScrollChanged += StructureDetailScrollHost_ScrollChanged;
        }
    }

    private void StructureDetailScrollHost_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is DependencyObject root)
        {
            GrowHeaderStructureDetailIfClipped(root);
        }

        // カーソルがエリア外／ホバー切れでも、実スクロールしたら既定時間（約3秒）出す。
        if (_structureDetailScrollRevealSuppress
            || (e.VerticalChange == 0 && e.HorizontalChange == 0))
        {
            return;
        }

        if (TryResolveStructureDetailScrollRevealHost(sender) is { } host)
        {
            LibraryScrollReveal.RevealTemporarily(host);
        }
    }

    private FrameworkElement? TryResolveStructureDetailScrollRevealHost(object sender)
    {
        if (IsStructureDetailScrollSender(sender, _structureDetailBox))
        {
            return _structureDetailBox;
        }

        if (IsStructureDetailScrollSender(sender, _structureXmlTree))
        {
            return _structureXmlTree;
        }

        return null;
    }

    private static bool IsStructureDetailScrollSender(object sender, FrameworkElement host)
    {
        if (ReferenceEquals(sender, host))
        {
            return true;
        }

        if (sender is not DependencyObject node)
        {
            return false;
        }

        for (var current = node; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, host))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// FormattedText 計測の誤差で横が足りないとき、実 Extent に合わせて情報列を広げる。
    /// </summary>
    private void GrowHeaderStructureDetailIfClipped(DependencyObject root)
    {
        if (_structureFitBusy
            || _structureDetailClipGrowBlocked
            || _structureUserWidth is not null)
        {
            return;
        }

        var viewer = root as ScrollViewer ?? FindDescendantScrollViewer(root);
        var overflow = 0d;
        if (viewer is { ViewportWidth: > 1 })
        {
            overflow = Math.Max(0, viewer.ExtentWidth - viewer.ViewportWidth);
        }

        // TreeView は Extent が内容幅を拾えないことがあるので、実測も見る。
        if (_structureXmlTree.Visibility == Visibility.Visible)
        {
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var need = MeasureXmlDetailNaturalWidth(dpi);
            var have = _structureDetailColumn.ActualWidth > 1
                ? _structureDetailColumn.ActualWidth
                : _structureDetailHeldWidth;
            overflow = Math.Max(overflow, need - have);
        }

        if (overflow <= 0.5)
        {
            return;
        }

        ApplyDetailWidthGrowth(overflow + DesignMetrics.From96(4));
    }

    private void ScheduleXmlDetailWidthSync()
    {
        if (_structureXmlTree.Visibility != Visibility.Visible)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            () =>
            {
                if (_structureXmlTree.Visibility != Visibility.Visible
                    || _structureFitBusy
                    || _structureDetailClipGrowBlocked
                    || _structureUserWidth is not null)
                {
                    return;
                }

                var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                var need = MeasureXmlDetailNaturalWidth(dpi);
                if (need <= _structureDetailHeldWidth + 0.5)
                {
                    return;
                }

                ApplyDetailWidthGrowth(need - _structureDetailHeldWidth);
            },
            DispatcherPriority.Loaded);
    }

    private void ApplyDetailWidthGrowth(double add)
    {
        if (add <= 0.5 || _structureFitBusy)
        {
            return;
        }

        _structureDetailHeldWidth += add;
        _structureAutoHeldWidth = Math.Max(
            _structureAutoHeldWidth,
            _structureTreeHeldWidth
                + DesignMetrics.LibraryExplorerSplitterWidth
                + _structureDetailHeldWidth);
        var before = _structureColumn.ActualWidth;
        _structureFitBusy = true;
        try
        {
            FitHeaderStructurePaneWidths();
        }
        finally
        {
            _structureFitBusy = false;
        }

        // プレイリスト下限などで広げられなかったら、以後の自動拡幅は止める（横スクロールはしない）。
        if (_structureColumn.ActualWidth <= before + 0.5)
        {
            _structureDetailClipGrowBlocked = true;
        }
    }

    /// <summary>XML 情報列の自然幅（ヘッダ実測＋クローム）。</summary>
    private double MeasureXmlDetailNaturalWidth(double pixelsPerDip)
    {
        var typeface = new Typeface(
            StructureDetailMonoFont,
            FontStyles.Normal,
            FontWeights.Normal,
            FontStretches.Normal);
        var content = MeasureStructureTreeWidth(
            _structureXmlTree.Items,
            depth: 0,
            childIndent: StructureXmlChildIndent,
            typeface,
            LibraryListFontSize,
            pixelsPerDip,
            expanderWidth: StructureXmlExpanderSize);
        if (_structureDetailMeta.Visibility == Visibility.Visible)
        {
            var metaWidth = MeasureMultilineWidth(
                    _structureDetailMeta.Text,
                    typeface,
                    LibraryListFontSize,
                    pixelsPerDip)
                + _structureDetailMeta.Margin.Left
                + _structureDetailMeta.Margin.Right;
            content = Math.Max(content, metaWidth);
        }

        // Padding + Item Padding(2+4) + 縦バー + ギャップ + 計測誤差。
        var chrome = _structureXmlTree.Padding.Left
            + _structureXmlTree.Padding.Right
            + 6
            + DesignMetrics.SettingsScrollBarWidth
            + DesignMetrics.SettingsScrollBarGap
            + DesignMetrics.From96(20);
        var titleFace = new Typeface(
            _structureDetailLabel.FontFamily,
            FontStyles.Normal,
            FontWeights.SemiBold,
            FontStretches.Normal);
        var labelWidth = MeasureLineWidth(
                _structureDetailLabel.Text,
                titleFace,
                _structureDetailLabel.FontSize,
                pixelsPerDip)
            + _structureDetailLabel.Margin.Left
            + _structureDetailLabel.Margin.Right;
        return Math.Max(DesignMetrics.From96(96), Math.Max(labelWidth, content + chrome));
    }

    /// <summary>
    /// ヘッダ情報列の表示切替。隠すとき幅の高水位とツリー選択記憶も忘れる（再表示は1行目）。
    /// フォーカス中ならプレイリストへ戻す。再表示は内容に合わせて自動フィット。
    /// </summary>
    public void SetHeaderStructureVisible(bool show, bool focusWhenShown = true)
    {
        if (_headerStructureVisible == show)
        {
            return;
        }

        if (!show)
        {
            // 中身・幅・選択行記憶を捨てる。オフ中の選択追従コストも切る。
            _headerStructureRefreshDeferred = false;
            _headerStructureRefreshDeferTicket++;
            ClearHeaderStructure();
            _structureRememberedChunkPath = null;
            ApplyHeaderStructureCollapsedChrome();

            _headerStructureVisible = false;
            if (IsHeaderStructureFocused)
            {
                FocusList();
            }

            HeaderStructureVisibleChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        _headerStructureVisible = true;
        _structureDetailClipGrowBlocked = false;
        if (_structurePaneHost is { } shown)
        {
            shown.Visibility = Visibility.Visible;
        }

        _structureSplitter.Visibility = Visibility.Visible;
        _structureSplitterColumn.Width = new GridLength(DesignMetrics.LibraryExplorerSplitterWidth);
        // SetSidePanesVisible と同様、幅を入れる前に拘束を戻す（MaxWidth=0 のまま Fit すると見切れる）。
        _structureColumn.MinWidth = DesignMetrics.LibraryHeaderStructureMinWidth;
        _structureColumn.MaxWidth = DesignMetrics.LibraryHeaderStructureMaxWidth;
        _structureUserWidth = null;

        RefreshHeaderStructure();
        FitHeaderStructurePaneWidths();
        // Refresh 後のフォーカスは Loaded 以降（ツリー行がフォーカス可能になってから）。
        ScheduleHeaderStructureFitAfterShow(focusWhenShown);

        HeaderStructureVisibleChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>再表示直後はレイアウト前に Fit することがあるので、Loaded 後にもう一度合わせて見切れを取る。</summary>
    private void ScheduleHeaderStructureFitAfterShow(bool focusWhenShown = false)
    {
        Dispatcher.BeginInvoke(
            () =>
            {
                if (!_headerStructureVisible || _structureBody is null)
                {
                    return;
                }

                FitHeaderStructurePaneWidths();
                if (FindDescendantScrollViewer(_structureDetailBox) is { } detailScroll)
                {
                    GrowHeaderStructureDetailIfClipped(detailScroll);
                }

                if (_structureXmlTree.Visibility == Visibility.Visible)
                {
                    if (FindDescendantScrollViewer(_structureXmlTree) is { } xmlScroll)
                    {
                        GrowHeaderStructureDetailIfClipped(xmlScroll);
                    }
                    else
                    {
                        ScheduleXmlDetailWidthSync();
                    }
                }

                if (focusWhenShown)
                {
                    FocusHeaderStructure();
                }
            },
            DispatcherPriority.Loaded);
    }

    private void FitHeaderStructurePaneWidths()
    {
        if (_structureBody is null || !_headerStructureVisible)
        {
            return;
        }

        var baseFont = LibraryListFontSize;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var treeTypeface = new Typeface(
            StructureTreeMonoFont,
            FontStyles.Normal,
            FontWeights.Normal,
            FontStretches.Normal);
        var detailTypeface = new Typeface(
            StructureDetailMonoFont,
            FontStyles.Normal,
            FontWeights.Normal,
            FontStretches.Normal);

        var titleFace = new Typeface(
            _structureLabel.FontFamily,
            FontStyles.Normal,
            FontWeights.SemiBold,
            FontStretches.Normal);
        var treeLabelWidth = MeasureLineWidth(_structureLabel.Text, titleFace, _structureLabel.FontSize, dpi)
            + _structureLabel.Margin.Left
            + _structureLabel.Margin.Right;
        var detailLabelWidth = MeasureLineWidth(
                _structureDetailLabel.Text,
                titleFace,
                _structureDetailLabel.FontSize,
                dpi)
            + _structureDetailLabel.Margin.Left
            + _structureDetailLabel.Margin.Right;

        var treeEmpty = _structureTree.Items.Count == 0;
        var detailEmpty = _structureXmlTree.Visibility != Visibility.Visible
            && string.IsNullOrEmpty(_structureDetailBox.Text);

        double treeNatural;
        if (treeEmpty)
        {
            // 空のときは見出しが見える幅だけでよい。
            treeNatural = treeLabelWidth;
        }
        else
        {
            var treeContent = MeasureStructureTreeWidth(
                _structureTree.Items,
                depth: 0,
                childIndent: 0,
                treeTypeface,
                baseFont,
                dpi,
                expanderWidth: 16);
            treeNatural = Math.Max(
                DesignMetrics.LibraryHeaderStructureTreeMinWidth,
                treeContent
                    + _structureTree.Padding.Left
                    + _structureTree.Padding.Right
                    + 24);
            treeNatural = Math.Max(treeNatural, treeLabelWidth);
        }

        double detailNatural;
        if (detailEmpty)
        {
            detailNatural = detailLabelWidth;
        }
        else if (_structureXmlTree.Visibility == Visibility.Visible)
        {
            detailNatural = MeasureXmlDetailNaturalWidth(dpi);
        }
        else
        {
            var detailContent = MeasureMultilineWidth(
                _structureDetailBox.Text,
                detailTypeface,
                baseFont,
                dpi);
            // TextBox Padding + 縦バー（出た瞬間に幅を食う）+ 計測誤差余裕。
            var detailChrome = _structureDetailBox.Padding.Left
                + _structureDetailBox.Padding.Right
                + DesignMetrics.SettingsScrollBarWidth
                + DesignMetrics.SettingsScrollBarGap
                + DesignMetrics.From96(12);
            detailNatural = Math.Max(
                DesignMetrics.From96(96),
                Math.Max(detailLabelWidth, detailContent + detailChrome));
        }

        // ツリー／情報は一度広がったら、内容が狭くなっても自動では戻さない。
        _structureTreeHeldWidth = Math.Max(_structureTreeHeldWidth, treeNatural);
        treeNatural = _structureTreeHeldWidth;
        _structureDetailHeldWidth = Math.Max(_structureDetailHeldWidth, detailNatural);
        detailNatural = _structureDetailHeldWidth;

        var innerSplitter = DesignMetrics.LibraryExplorerSplitterWidth;
        var contentNeeded = treeNatural + innerSplitter + detailNatural;
        // 中身があるときだけ従来の床を使う。空は見出し幅の合計のまま。
        if (!treeEmpty || !detailEmpty)
        {
            contentNeeded = Math.Max(
                DesignMetrics.LibraryHeaderStructureMinWidth,
                contentNeeded);
        }

        // プレイリストとの2等分はしない。内容に必要な幅だけ広げ、自動では縮めない。
        // ユーザーがスプリッターを動かしたら、その幅をモード退出まで優先する。
        var structureWidth = contentNeeded;
        var available = ActualWidth;
        if (available > 1)
        {
            var libraryWidth = _sidePanesVisible
                ? Math.Max(0, _treeColumn.ActualWidth) + _treeSplitterColumn.ActualWidth
                : 0;
            var structureSplitter = _structureSplitterColumn.ActualWidth > 0
                ? _structureSplitterColumn.ActualWidth
                : DesignMetrics.LibraryExplorerSplitterWidth;
            var work = Math.Max(0, available - libraryWidth);
            var widthFloor = treeEmpty && detailEmpty
                ? contentNeeded
                : DesignMetrics.LibraryHeaderStructureMinWidth;
            var maxFromPlaylist = Math.Max(
                widthFloor,
                work - structureSplitter - DesignMetrics.LibraryPlaylistMinWidth);
            var maxWidth = Math.Min(maxFromPlaylist, DesignMetrics.LibraryHeaderStructureMaxWidth);
            if (_structureUserWidth is { } userWidth)
            {
                structureWidth = Math.Min(
                    DesignMetrics.ClampLibraryHeaderStructureWidth(userWidth),
                    maxFromPlaylist);
            }
            else
            {
                _structureAutoHeldWidth = Math.Max(_structureAutoHeldWidth, contentNeeded);
                structureWidth = Math.Min(_structureAutoHeldWidth, maxWidth);
            }
        }
        else if (_structureUserWidth is { } userWidth)
        {
            structureWidth = DesignMetrics.ClampLibraryHeaderStructureWidth(userWidth);
        }
        else
        {
            _structureAutoHeldWidth = Math.Max(_structureAutoHeldWidth, contentNeeded);
            var fallback = treeEmpty && detailEmpty
                ? contentNeeded
                : Math.Max(DesignMetrics.LibraryHeaderStructureWidth, contentNeeded);
            structureWidth = Math.Min(
                _structureAutoHeldWidth > 0 ? _structureAutoHeldWidth : fallback,
                DesignMetrics.LibraryHeaderStructureMaxWidth);
        }

        var treeWidth = treeNatural;
        var detailWidth = detailNatural;
        if (treeWidth + innerSplitter + detailWidth <= structureWidth)
        {
            if (treeEmpty && detailEmpty)
            {
                // 空のときは余剰をどちらにも振らず、見出し幅のまま。
            }
            else
            {
                // 余った分は情報表示側へ。
                detailWidth = Math.Max(detailWidth, structureWidth - innerSplitter - treeWidth);
            }
        }
        else
        {
            // 上限超過時は情報の自然幅を優先し、先にツリーを最小まで削る。
            var remain = Math.Max(0, structureWidth - innerSplitter);
            var treeFloor = treeEmpty
                ? treeLabelWidth
                : DesignMetrics.LibraryHeaderStructureTreeMinWidth;
            var detailFloor = detailEmpty ? detailLabelWidth : DesignMetrics.From96(96);
            treeWidth = Math.Min(
                treeNatural,
                Math.Max(treeFloor, remain - detailNatural));
            detailWidth = Math.Max(detailFloor, remain - treeWidth);
        }

        _structureTreeColumn.Width = new GridLength(treeWidth);
        _structureTreeColumn.MinWidth = treeEmpty
            ? treeWidth
            : DesignMetrics.LibraryHeaderStructureTreeMinWidth;
        _structureDetailColumn.Width = new GridLength(detailWidth);
        _structureDetailColumn.MinWidth = detailEmpty
            ? detailWidth
            : DesignMetrics.From96(96);
        _structureColumn.Width = new GridLength(structureWidth);
        _structureColumn.MinWidth = treeEmpty && detailEmpty
            ? structureWidth
            : DesignMetrics.LibraryHeaderStructureMinWidth;
        _structureColumn.MaxWidth = DesignMetrics.LibraryHeaderStructureMaxWidth;
        ApplyStructureContentFonts();
    }

    /// <summary>フォント拡大はしない。リストと同じサイズで揃える。</summary>
    private void ApplyStructureContentFonts()
    {
        var font = LibraryListFontSize;
        _structureTree.FontSize = font;
        _structureTree.FontFamily = StructureTreeMonoFont;
        _structureTree.FontWeight = FontWeights.Normal;
        _structureTree.FontStretch = FontStretches.Normal;
        ApplyStructureTreeItemFontSize(_structureTree.Items, font, StructureTreeMonoFont, packLines: false);
        _structureDetailBox.FontSize = font;
        _structureDetailBox.FontFamily = StructureDetailMonoFont;
        _structureDetailBox.FontWeight = FontWeights.Normal;
        _structureDetailBox.FontStyle = FontStyles.Normal;
        _structureDetailBox.FontStretch = FontStretches.Normal;
        TextElement.SetFontWeight(_structureDetailBox, FontWeights.Normal);
        ApplyStructureDetailLineHeight(_structureDetailBox, font);
        _structureDetailMeta.FontSize = font;
        _structureDetailMeta.FontFamily = StructureDetailMonoFont;
        _structureDetailMeta.FontWeight = FontWeights.Normal;
        _structureDetailMeta.FontStyle = FontStyles.Normal;
        _structureDetailMeta.FontStretch = FontStretches.Normal;
        ApplyStructureDetailLineHeight(_structureDetailMeta, font);
        _structureXmlTree.FontSize = font;
        _structureXmlTree.FontFamily = StructureDetailMonoFont;
        _structureXmlTree.FontWeight = FontWeights.Normal;
        _structureXmlTree.FontStretch = FontStretches.Normal;
        ApplyStructureTreeItemFontSize(_structureXmlTree.Items, font, StructureDetailMonoFont, packLines: true);
    }

    private static void ApplyStructureTreeItemFontSize(
        ItemCollection items,
        double fontSize,
        FontFamily fontFamily,
        bool packLines)
    {
        foreach (var raw in items)
        {
            if (raw is not TreeViewItem item)
            {
                continue;
            }

            item.FontWeight = FontWeights.Normal;
            item.FontStretch = FontStretches.Normal;
            if (item.Header is TextBlock header)
            {
                header.FontSize = fontSize;
                header.FontFamily = fontFamily;
                header.FontWeight = FontWeights.Normal;
                header.FontStyle = FontStyles.Normal;
                header.FontStretch = FontStretches.Normal;
                TextOptions.SetTextFormattingMode(header, TextFormattingMode.Display);
                TextOptions.SetTextRenderingMode(header, TextRenderingMode.ClearType);
                TextOptions.SetTextHintingMode(header, TextHintingMode.Fixed);
                if (packLines)
                {
                    TextBlock.SetLineStackingStrategy(header, LineStackingStrategy.BlockLineHeight);
                    TextBlock.SetLineHeight(header, fontSize);
                }
            }

            if (item.Items.Count > 0)
            {
                ApplyStructureTreeItemFontSize(item.Items, fontSize, fontFamily, packLines);
            }
        }
    }

    internal static double MeasureStructureTreeWidth(
        ItemCollection items,
        int depth,
        double childIndent,
        Typeface typeface,
        double fontSize,
        double pixelsPerDip,
        double expanderWidth)
    {
        var max = 0d;
        foreach (var raw in items)
        {
            if (raw is not TreeViewItem item)
            {
                continue;
            }

            // XML は Inline 付き TextBlock なので DesiredSize 実測の方が FormattedText より正確。
            var textWidth = MeasureTreeHeaderWidth(item.Header, typeface, fontSize, pixelsPerDip);
            // チャンクツリーはキャプション内スペース、XML は childIndent×深さ。
            // +6 は TreeViewItem Padding(2+4)、+8 は選択帯・誤差。
            var width = textWidth
                + (childIndent * depth)
                + expanderWidth
                + 6
                + 8;
            max = Math.Max(max, width);
            if (item.Items.Count > 0)
            {
                max = Math.Max(
                    max,
                    MeasureStructureTreeWidth(
                        item.Items,
                        depth + 1,
                        childIndent,
                        typeface,
                        fontSize,
                        pixelsPerDip,
                        expanderWidth));
            }
        }

        return max;
    }

    private static double MeasureTreeHeaderWidth(
        object? header,
        Typeface typeface,
        double fontSize,
        double pixelsPerDip)
    {
        if (header is FrameworkElement element)
        {
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (element.DesiredSize.Width > 0)
            {
                return element.DesiredSize.Width;
            }
        }

        var caption = header switch
        {
            TextBlock block => block.Text ?? string.Empty,
            string text => text,
            _ => header?.ToString() ?? string.Empty,
        };
        return MeasureLineWidth(caption, typeface, fontSize, pixelsPerDip);
    }

    private static double MeasureMultilineWidth(
        string? text,
        Typeface typeface,
        double fontSize,
        double pixelsPerDip)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var max = 0d;
        var start = 0;
        while (start <= text.Length)
        {
            var end = text.IndexOfAny(['\r', '\n'], start);
            if (end < 0)
            {
                end = text.Length;
            }

            var line = text[start..end];
            max = Math.Max(max, MeasureLineWidth(line, typeface, fontSize, pixelsPerDip));
            if (end >= text.Length)
            {
                break;
            }

            start = end + 1;
            if (start < text.Length && text[end] == '\r' && text[start] == '\n')
            {
                start++;
            }
        }

        return max;
    }

    private static double MeasureLineWidth(
        string text,
        Typeface typeface,
        double fontSize,
        double pixelsPerDip)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        // UI と同じ Display 計測。Ideal だと実描画より狭く見積もることがある。
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black,
            numberSubstitution: null,
            TextFormattingMode.Display,
            pixelsPerDip);
        return formatted.WidthIncludingTrailingWhitespace;
    }

    private void StructureSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        var width = _structureColumn.ActualWidth > 1
            ? _structureColumn.ActualWidth
            : _structureColumn.Width.Value;
        if (width > 1)
        {
            var clamped = DesignMetrics.ClampLibraryHeaderStructureWidth(width);
            _structureUserWidth = clamped;
            // ユーザーが狭めた／広げた幅を高水位にも反映する。
            _structureAutoHeldWidth = clamped;
            var innerSplitter = DesignMetrics.LibraryExplorerSplitterWidth;
            var treeWidth = _structureTreeColumn.ActualWidth > 1
                ? _structureTreeColumn.ActualWidth
                : _structureTreeColumn.Width.Value;
            var treeFloor = DesignMetrics.LibraryHeaderStructureTreeMinWidth;
            var detailFloor = DesignMetrics.From96(96);
            _structureTreeHeldWidth = Math.Max(treeFloor, Math.Min(treeWidth, clamped - innerSplitter - detailFloor));
            _structureDetailHeldWidth = Math.Max(
                detailFloor,
                clamped - innerSplitter - _structureTreeHeldWidth);
        }

        FitHeaderStructurePaneWidths();
    }

    /// <summary>プレイヤーモード退出時にスプリッター／高水位の記憶を捨てる（H 隠しも ClearHeaderStructure）。</summary>
    internal void ClearHeaderStructureUserLayout()
    {
        if (_structureUserWidth is null
            && _structureAutoHeldWidth <= 0
            && _structureDetailHeldWidth <= 0
            && _structureTreeHeldWidth <= 0)
        {
            return;
        }

        _structureUserWidth = null;
        _structureAutoHeldWidth = 0;
        _structureDetailHeldWidth = 0;
        _structureTreeHeldWidth = 0;
        FitHeaderStructurePaneWidths();
    }

    internal bool IsHeaderStructureTreeFocused => _structureTree.IsKeyboardFocusWithin;

    internal bool IsHeaderStructureDetailFocused =>
        _structureDetailBox.IsKeyboardFocusWithin
        || _structureXmlTree.IsKeyboardFocusWithin;

    internal bool IsHeaderStructureFocused =>
        IsHeaderStructureTreeFocused || IsHeaderStructureDetailFocused;
}
