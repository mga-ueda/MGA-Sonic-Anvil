using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>
/// VST インサート用プラグイン選択ウィンドウ。
/// メーカー／種別のツリー手繰り、ライブラリと同じ検索構文の検索窓、
/// インクリメンタルサーチ、キーボード操作。モーダル表示のため本体ショートカットは反応しない。
/// </summary>
internal sealed class VstInsertPluginPickerWindow : Window
{
    private static readonly SolidColorBrush TypeaheadHitBack = CreateHitBack();
    private static readonly SolidColorBrush TypeaheadHitFore = CreateHitFore();

    private readonly IReadOnlyList<VstPickerEntry> _plugins;
    private readonly TreeView _tree = new();
    private readonly TextBox _searchBox = new();
    private readonly TextBlock _searchHint = new();
    private readonly RadioButton _vendorRadio;
    private readonly RadioButton _categoryRadio;
    private readonly AcrylicToastHost _typeaheadToast = new();
    private readonly HashSet<TreeViewItem> _typeaheadHighlighted = [];
    private readonly Dictionary<TreeViewItem, string> _headerNames = new();

    private string _filter = "";
    private List<string[]> _filterGroups = [];
    private string _typeahead = "";
    private string _typeaheadWalk = "";
    private string _typeaheadIgnoreText = "";
    private DispatcherTimer? _typeaheadToastIdleTimer;
    private VstInsertPickerGroupMode _groupMode;
    private bool _suppressExpandScroll;
    private bool _accepted;

    public VstPickerEntry? Selected { get; private set; }

    public static VstPickerEntry? Show(
        Window? owner,
        IReadOnlyList<VstPickerEntry> plugins)
    {
        var dialog = new VstInsertPluginPickerWindow(plugins);
        if (owner is not null)
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowPaintReveal.EnsureRevealedForDialog(owner);
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        AcrylicDialogChrome.ShowDialog(dialog);
        return dialog._accepted ? dialog.Selected : null;
    }

    private VstInsertPluginPickerWindow(IReadOnlyList<VstPickerEntry> plugins)
    {
        _plugins = plugins;
        _groupMode = VstInsertPluginMenu.ParseGroupMode(AppStorage.Settings.VstInsertPickerGroupBy);

        Title = UiStrings.LabelVstInsertPickerTitle;
        Width = DesignMetrics.From96(420);
        MinWidth = DesignMetrics.From96(320);
        Height = Math.Min(DesignMetrics.From96(560), VstInsertPluginMenu.ResolveMaxHeight() + DesignMetrics.From96(120));
        MinHeight = DesignMetrics.From96(360);
        Foreground = TryBrush("PrimaryForeBrush", Brushes.WhiteSmoke);
        AcrylicDialogChrome.Prepare(this, ResizeMode.CanResizeWithGrip, SizeToContent.Manual);

        _vendorRadio = CreateRadio(
            UiStrings.LabelVstInsertPickerGroupVendor,
            _groupMode == VstInsertPickerGroupMode.Vendor);
        _categoryRadio = CreateRadio(
            UiStrings.LabelVstInsertPickerGroupCategory,
            _groupMode == VstInsertPickerGroupMode.Category);
        _vendorRadio.Checked += (_, _) => OnGroupModeChanged(VstInsertPickerGroupMode.Vendor);
        _categoryRadio.Checked += (_, _) => OnGroupModeChanged(VstInsertPickerGroupMode.Category);

        var radioRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
        };
        radioRow.Children.Add(_vendorRadio);
        radioRow.Children.Add(_categoryRadio);

        var searchHost = CreateSearchEditor();

        ApplyTreeStyle();
        _tree.IsTextSearchEnabled = false;
        ImeComposition.Disable(_tree);
        _tree.PreviewKeyDown += Tree_PreviewKeyDown;
        _tree.PreviewTextInput += Tree_PreviewTextInput;
        _tree.MouseDoubleClick += Tree_MouseDoubleClick;
        _tree.SelectedItemChanged += (_, _) => ClearTypeaheadIfSelectionMovedByMouse();

        _tree.MinHeight = DesignMetrics.From96(220);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var ok = CreateButton(UiStrings.ButtonOkMnemonic, accent: true, isDefault: true);
        ok.Click += (_, _) => TryAcceptSelected();
        buttons.Children.Add(ok);
        var cancel = CreateButton(UiStrings.ButtonCancelMnemonic, accent: false);
        cancel.Click += (_, _) => Close();
        buttons.Children.Add(cancel);

        var root = new DockPanel();
        DockPanel.SetDock(radioRow, Dock.Top);
        DockPanel.SetDock(searchHost, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(radioRow);
        root.Children.Add(searchHost);
        root.Children.Add(buttons);
        root.Children.Add(_tree);

        AcrylicDialogChrome.SetBody(this, root, DesignMetrics.AudioPad);

        _typeaheadToast.FontSize = DesignMetrics.From96(22);
        _typeaheadToast.Padding = DesignMetrics.PlaylistVideoTimecodePadding;
        _typeaheadToast.TextAlignment = TextAlignment.Center;
        _typeaheadToast.PlacementTarget = _tree;
        _typeaheadToast.Placement = PlacementMode.Center;

        RebuildTree();
        Closed += (_, _) => ClearTypeahead();
        PreviewKeyDown += Window_PreviewKeyDown;
        PreviewTextInput += Window_PreviewTextInput;
        AppDialogKeys.Attach(this, Close, interceptEscape: TryInterceptEscape);
        // Attach の Loaded がボタンへフォーカスするため、その後で検索窓へ寄せる。
        Loaded += (_, _) => Dispatcher.BeginInvoke(FocusSearchInitial, DispatcherPriority.Input);
    }

    private bool TryInterceptEscape()
    {
        if (ClearTypeahead())
        {
            return true;
        }

        if (_searchBox.Text.Length == 0)
        {
            return false;
        }

        _searchBox.Text = "";
        if (!_searchBox.IsKeyboardFocusWithin)
        {
            FocusSearchInitial();
        }

        return true;
    }

    private void OnGroupModeChanged(VstInsertPickerGroupMode mode)
    {
        if (_groupMode == mode)
        {
            return;
        }

        _groupMode = mode;
        AppStorage.Settings.VstInsertPickerGroupBy = VstInsertPluginMenu.ToGroupModeSetting(mode);
        AppStorage.Save();
        ClearTypeahead();
        RebuildTree();
        if (!_searchBox.IsKeyboardFocusWithin)
        {
            FocusTreeInitial();
        }
    }

    private void RebuildTree()
    {
        _tree.Items.Clear();
        _headerNames.Clear();
        _typeaheadHighlighted.Clear();

        var plugins = FilteredPlugins();
        if (_plugins.Count == 0)
        {
            var empty = new TreeViewItem
            {
                Header = MakeHeader(UiStrings.LabelVstInsertNoneRecognized),
                IsEnabled = false,
                Focusable = false,
            };
            _tree.Items.Add(empty);
            return;
        }

        if (plugins.Count == 0)
        {
            return;
        }

        if (_groupMode == VstInsertPickerGroupMode.Vendor)
        {
            foreach (var group in VstInsertPluginMenu.GroupPickerByVendor(plugins))
            {
                var vendor = CreateGroupItem(group.Key);
                foreach (var plugin in group.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
                {
                    AddPluginUnderVendor(vendor, plugin);
                }

                SortItemCollection(vendor.Items);
                _tree.Items.Add(vendor);
            }

            SortItemCollection(_tree.Items);
        }
        else
        {
            foreach (var plugin in plugins.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            {
                AddPluginUnderCategory(plugin);
            }

            SortItemCollection(_tree.Items);
        }

        if (_filterGroups.Count > 0)
        {
            _suppressExpandScroll = true;
            try
            {
                ExpandAllGroups(_tree.Items);
            }
            finally
            {
                _suppressExpandScroll = false;
            }
        }

        SyncFilterHighlights();
    }

    private IReadOnlyList<VstPickerEntry> FilteredPlugins()
    {
        if (_filterGroups.Count == 0)
        {
            return _plugins;
        }

        var matched = new List<VstPickerEntry>();
        foreach (var plugin in _plugins)
        {
            if (MatchesFilter(plugin))
            {
                matched.Add(plugin);
            }
        }

        return matched;
    }

    private bool MatchesFilter(VstPickerEntry plugin) =>
        VstInsertPluginMenu.MatchesPickerFilter(plugin, _filterGroups, _groupMode);

    private static void ExpandAllGroups(ItemCollection items)
    {
        foreach (var raw in items)
        {
            if (raw is not TreeViewItem item || item.Tag is not null)
            {
                continue;
            }

            item.IsExpanded = true;
            if (item.Items.Count > 0)
            {
                ExpandAllGroups(item.Items);
            }
        }
    }

    /// <summary>メーカー →（種別入れ子）→ プラグイン。同一プラグインは1か所のみ。</summary>
    private void AddPluginUnderVendor(TreeViewItem vendor, VstPickerEntry plugin)
    {
        var segments = VstInsertPluginMenu.CategoryTreeHierarchyPath(plugin.Category);
        if (segments.Length == 0)
        {
            AddPluginLeaf(vendor, plugin);
            return;
        }

        AddPluginUnderSegments(vendor.Items, plugin, segments);
    }

    /// <summary>種別 → プラグイン（1段）。'|' / 「 · 」の各段は別ノードへ重複配置可。</summary>
    private void AddPluginUnderCategory(VstPickerEntry plugin)
    {
        var paths = VstInsertPluginMenu.CategoryTreePaths(plugin.Category);
        if (paths.Count == 0)
        {
            AddPluginLeaf(
                EnsureGroup(_tree.Items, UiStrings.LabelVstInsertCategoryUnknown),
                plugin);
            return;
        }

        foreach (var segments in paths)
        {
            AddPluginUnderSegments(_tree.Items, plugin, segments);
        }
    }

    private void AddPluginUnderSegments(
        ItemCollection roots,
        VstPickerEntry plugin,
        string[] segments)
    {
        var current = roots;
        TreeViewItem? group = null;
        foreach (var segment in segments)
        {
            group = EnsureGroup(current, segment);
            current = group.Items;
        }

        AddPluginLeaf(group!, plugin);
    }

    private void AddPluginLeaf(TreeViewItem group, VstPickerEntry plugin)
    {
        foreach (var raw in group.Items)
        {
            if (raw is TreeViewItem item
                && item.Tag is VstPickerEntry existing
                && SamePlugin(existing, plugin))
            {
                return;
            }
        }

        group.Items.Add(CreatePluginItem(plugin));
    }

    private static bool SamePlugin(VstPickerEntry a, VstPickerEntry b) =>
        string.Equals(a.Path, b.Path, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.ClassId, b.ClassId, StringComparison.Ordinal);

    private TreeViewItem EnsureGroup(ItemCollection items, string name)
    {
        foreach (var raw in items)
        {
            if (raw is TreeViewItem existing
                && existing.Tag is null
                && _headerNames.TryGetValue(existing, out var header)
                && string.Equals(header, name, StringComparison.OrdinalIgnoreCase))
            {
                return existing;
            }
        }

        var created = CreateGroupItem(name);
        items.Add(created);
        return created;
    }

    private static void SortItemCollection(ItemCollection items)
    {
        var list = items.Cast<TreeViewItem>().ToList();
        items.Clear();
        foreach (var item in list.OrderBy(GroupSortKey, StringComparer.OrdinalIgnoreCase))
        {
            if (item.Tag is null && item.Items.Count > 0)
            {
                SortItemCollection(item.Items);
            }

            items.Add(item);
        }
    }

    private static string GroupSortKey(TreeViewItem item)
    {
        if (item.Tag is VstPickerEntry entry)
        {
            return entry.Name;
        }

        return item.Header switch
        {
            TextBlock block => block.Text ?? string.Empty,
            string text => text,
            _ => item.Header?.ToString() ?? string.Empty,
        };
    }

    private TreeViewItem CreateGroupItem(string name)
    {
        var item = new TreeViewItem
        {
            Header = MakeHeader(name),
            Tag = null,
            IsExpanded = false,
        };
        _headerNames[item] = name;
        item.Expanded += TreeItem_Expanded;
        return item;
    }

    private void TreeItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (_suppressExpandScroll
            || !ReferenceEquals(sender, e.OriginalSource)
            || sender is not TreeViewItem item)
        {
            return;
        }

        // 子のレイアウト後に、展開した行がビューポート先頭へ来るよう寄せる。
        Dispatcher.BeginInvoke(
            () => ScrollItemToTop(item),
            DispatcherPriority.Loaded);
    }

    private void ScrollItemToTop(TreeViewItem item)
    {
        if (!item.IsExpanded || !item.IsVisible)
        {
            return;
        }

        if (FindDescendantScrollViewer(_tree) is not { } scroll)
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

    private TreeViewItem CreatePluginItem(VstPickerEntry entry)
    {
        var item = new TreeViewItem
        {
            Header = MakeHeader(entry.Name),
            Tag = entry,
        };
        _headerNames[item] = entry.Name;
        return item;
    }

    private static TextBlock MakeHeader(string text)
    {
        var block = new TextBlock { Text = text };
        block.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryForeBrush");
        return block;
    }

    private Grid CreateSearchEditor()
    {
        _searchBox.FontSize = DesignMetrics.From96(13);
        _searchBox.Height = DesignMetrics.AudioInputHeight;
        _searchBox.AcceptsReturn = false;
        _searchBox.AcceptsTab = false;
        _searchBox.VerticalContentAlignment = VerticalAlignment.Center;
        _searchBox.SetResourceReference(StyleProperty, "LibrarySearchBoxStyle");
        _searchBox.TextChanged += SearchBox_TextChanged;
        _searchBox.GotKeyboardFocus += (_, _) => SyncSearchHint();
        _searchBox.LostKeyboardFocus += (_, _) => SyncSearchHint();
        _searchBox.PreviewKeyDown += SearchBox_PreviewKeyDown;
        KeyboardNavigation.SetIsTabStop(_searchBox, false);

        _searchHint.Text = UiStrings.LabelVstInsertPickerSearchHint;
        _searchHint.FontSize = DesignMetrics.From96(13);
        _searchHint.Margin = new Thickness(8, 0, 0, 0);
        _searchHint.VerticalAlignment = VerticalAlignment.Center;
        _searchHint.IsHitTestVisible = false;
        _searchHint.SetResourceReference(TextBlock.ForegroundProperty, "MutedForeBrush");

        var host = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        host.Children.Add(_searchBox);
        host.Children.Add(_searchHint);
        SyncSearchHint();
        return host;
    }

    private void SyncSearchHint() =>
        _searchHint.Visibility = string.IsNullOrEmpty(_searchBox.Text) && !_searchBox.IsKeyboardFocused
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SyncSearchHint();
        var next = _searchBox.Text.Trim();
        if (string.Equals(_filter, next, StringComparison.Ordinal))
        {
            return;
        }

        _filter = next;
        _filterGroups = LibrarySearchQuery.Parse(next);
        ClearTypeahead();
        RebuildTree();
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key != Key.Down || Keyboard.Modifiers != ModifierKeys.None || ImeComposition.IsComposing)
        {
            return;
        }

        e.Handled = true;
        FocusTreeFromSearch();
    }

    private void FocusSearchInitial()
    {
        Keyboard.Focus(_searchBox);
        if (_searchBox.IsKeyboardFocusWithin)
        {
            _searchBox.SelectAll();
        }
    }

    private void FocusTreeFromSearch()
    {
        if (_tree.Items.Count == 0)
        {
            return;
        }

        _tree.UpdateLayout();
        if (_filterGroups.Count > 0 && TryFocusFirstPlugin())
        {
            return;
        }

        FocusTreeInitial();
    }

    private bool TryFocusFirstPlugin()
    {
        if (!TryCollectTypeahead(out var items, out _, out _))
        {
            return false;
        }

        foreach (var item in items)
        {
            if (item.Tag is not VstPickerEntry)
            {
                continue;
            }

            EnsureAncestorsExpanded(item);
            item.IsSelected = true;
            item.BringIntoView();
            Keyboard.Focus(item);
            if (!item.IsKeyboardFocusWithin)
            {
                _tree.Focus();
            }

            return true;
        }

        return false;
    }

    private void FocusTreeInitial()
    {
        if (_tree.Items.Count == 0)
        {
            return;
        }

        _tree.UpdateLayout();
        if (_tree.Items[0] is TreeViewItem first)
        {
            first.IsSelected = true;
            first.BringIntoView();
            Keyboard.Focus(first);
            if (!first.IsKeyboardFocusWithin)
            {
                _tree.Focus();
            }

            return;
        }

        Keyboard.Focus(_tree);
    }

    private void AcceptPlugin(VstPickerEntry entry)
    {
        Selected = entry;
        _accepted = true;
        Close();
    }

    private bool TryAcceptSelected()
    {
        if (_tree.SelectedItem is not TreeViewItem item)
        {
            return false;
        }

        if (item.Tag is VstPickerEntry entry)
        {
            AcceptPlugin(entry);
            return true;
        }

        if (item.HasItems && !item.IsExpanded)
        {
            item.IsExpanded = true;
            return true;
        }

        return false;
    }

    private void Tree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindTreeItem(e.OriginalSource as DependencyObject) is { Tag: VstPickerEntry entry })
        {
            e.Handled = true;
            AcceptPlugin(entry);
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        if (key == Key.F
            && modifiers is ModifierKeys.Control or ModifierKeys.Alt
            && !ImeComposition.IsComposing)
        {
            e.Handled = true;
            FocusSearchInitial();
            return;
        }

        // 検索窓への入力はタイプアヘッドへ回さない。
        if (_searchBox.IsKeyboardFocusWithin)
        {
            return;
        }

        // ラジオにフォーカスがあっても、ライブラリと同じく本体ショートカットへ漏らさない。
        if (TryHandleTypeaheadKey(key, modifiers))
        {
            e.Handled = true;
            return;
        }

        if (key == Key.Enter && modifiers == ModifierKeys.None)
        {
            if (TryAcceptSelected())
            {
                e.Handled = true;
            }
        }
    }

    private void Window_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_searchBox.IsKeyboardFocusWithin
            || !LibraryExplorerTypeahead.IsTypeaheadText(e.Text))
        {
            return;
        }

        // PreviewKeyDown で既に足した文字の TextInput は無視（二重追記防止）。
        if (!string.IsNullOrEmpty(_typeaheadIgnoreText)
            && string.Equals(e.Text, _typeaheadIgnoreText, StringComparison.OrdinalIgnoreCase))
        {
            _typeaheadIgnoreText = "";
            e.Handled = true;
            return;
        }

        if (TryAppendTypeahead(e.Text))
        {
            e.Handled = true;
        }
    }

    private void Tree_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        if (key is Key.Left or Key.Right && modifiers == ModifierKeys.None)
        {
            ClearTypeahead();
            return;
        }

        if (key == Key.Space && modifiers == ModifierKeys.None)
        {
            TrySpaceExpandOrMoveDown();
            e.Handled = true;
            return;
        }

        if (key == Key.Space && modifiers == ModifierKeys.Shift)
        {
            TryShiftSpaceCollapseOrMoveUp();
            e.Handled = true;
            return;
        }

        if (key is Key.Up or Key.Down && modifiers == ModifierKeys.None)
        {
            var direction = key == Key.Up ? -1 : 1;
            if (IsTypeaheadActive && TryStepTypeahead(direction))
            {
                e.Handled = true;
                return;
            }

            if (IsTypeaheadActive)
            {
                ClearTypeahead();
            }

            // 先頭で上／末尾で下は検索窓へ戻る。
            if (!TryMoveSelection(direction, leaveAtBoundary: true))
            {
                FocusSearchInitial();
            }

            e.Handled = true;
            return;
        }

        if (key == Key.Enter && modifiers == ModifierKeys.None)
        {
            if (TryAcceptSelected())
            {
                e.Handled = true;
            }
        }
    }

    private void Tree_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!LibraryExplorerTypeahead.IsTypeaheadText(e.Text))
        {
            return;
        }

        if (!string.IsNullOrEmpty(_typeaheadIgnoreText)
            && string.Equals(e.Text, _typeaheadIgnoreText, StringComparison.OrdinalIgnoreCase))
        {
            _typeaheadIgnoreText = "";
            e.Handled = true;
            return;
        }

        if (TryAppendTypeahead(e.Text))
        {
            e.Handled = true;
        }
    }

    private bool TryHandleTypeaheadKey(Key key, ModifierKeys modifiers)
    {
        if (!LibraryExplorerTypeahead.TryMapChar(key, modifiers, out var character))
        {
            return false;
        }

        // ツリー／ウィンドウ内ならラジオフォーカス中でも打ち込みを受ける。
        _typeaheadIgnoreText = character;
        return TryAppendTypeahead(character);
    }

    private bool IsTypeaheadActive => _typeaheadWalk.Length > 0;

    private bool ClearTypeahead()
    {
        StopTypeaheadToastIdleTimer();
        ClearTypeaheadHighlights();
        _typeaheadIgnoreText = "";
        var had = _typeahead.Length > 0 || _typeaheadWalk.Length > 0;
        _typeahead = "";
        _typeaheadWalk = "";
        HideTypeaheadToast();
        // タイプアヘッドを閉じたら、検索窓のヒット強調を戻す。
        SyncFilterHighlights();
        return had;
    }

    private bool TryAppendTypeahead(string next)
    {
        if (!LibraryExplorerTypeahead.IsTypeaheadText(next))
        {
            return false;
        }

        _typeahead = LibraryExplorerTypeahead.Append(_typeahead, next);
        _typeaheadWalk = _typeahead;
        ActivateTypeaheadMatch();
        ShowTypeaheadToast();
        SyncTypeaheadHighlights();
        return true;
    }

    private bool TryStepTypeahead(int direction)
    {
        if (!IsTypeaheadActive || direction == 0)
        {
            return false;
        }

        if (!TryCollectTypeahead(out var items, out var names, out var current))
        {
            return false;
        }

        var match = LibraryExplorerTypeahead.FindStepMatchIndex(
            names,
            _typeaheadWalk,
            current,
            direction);
        if (match < 0)
        {
            return false;
        }

        SelectTypeaheadItem(items, match);
        SyncTypeaheadHighlights();
        return true;
    }

    private void ActivateTypeaheadMatch()
    {
        if (!TryCollectTypeahead(out var items, out var names, out var current))
        {
            return;
        }

        var match = LibraryExplorerTypeahead.FindMatchIndex(names, _typeaheadWalk, current);
        SelectTypeaheadItem(items, match);
    }

    private void SelectTypeaheadItem(List<TreeViewItem> items, int match)
    {
        if ((uint)match >= (uint)items.Count)
        {
            return;
        }

        var item = items[match];
        EnsureAncestorsExpanded(item);
        item.IsSelected = true;
        item.BringIntoView();
        item.Focus();
    }

    private bool TryCollectTypeahead(
        out List<TreeViewItem> items,
        out string[] names,
        out int current)
    {
        items = [];
        CollectVisibleItems(_tree.Items, items);
        names = new string[items.Count];
        current = -1;
        if (items.Count == 0)
        {
            return false;
        }

        var selected = _tree.SelectedItem as TreeViewItem;
        for (var i = 0; i < items.Count; i++)
        {
            names[i] = HeaderName(items[i]);
            if (ReferenceEquals(items[i], selected))
            {
                current = i;
            }
        }

        return true;
    }

    private static void CollectVisibleItems(ItemCollection items, List<TreeViewItem> dest)
    {
        foreach (var raw in items)
        {
            if (raw is not TreeViewItem item || !item.IsEnabled)
            {
                continue;
            }

            dest.Add(item);
            if (item.IsExpanded && item.Items.Count > 0)
            {
                CollectVisibleItems(item.Items, dest);
            }
        }
    }

    private string HeaderName(TreeViewItem item) =>
        _headerNames.TryGetValue(item, out var name)
            ? name
            : item.Header switch
            {
                TextBlock block => block.Text ?? string.Empty,
                string text => text,
                _ => item.Header?.ToString() ?? string.Empty,
            };

    private void ShowTypeaheadToast()
    {
        if (_typeahead.Length == 0)
        {
            HideTypeaheadToast();
            return;
        }

        _typeaheadToast.ShowSticky(_typeahead);
        _typeaheadToastIdleTimer ??= new DispatcherTimer
        {
            Interval = LibraryExplorerTypeahead.ToastIdleClear,
        };
        _typeaheadToastIdleTimer.Tick -= OnTypeaheadToastIdleTick;
        _typeaheadToastIdleTimer.Tick += OnTypeaheadToastIdleTick;
        _typeaheadToastIdleTimer.Stop();
        _typeaheadToastIdleTimer.Start();
    }

    private void OnTypeaheadToastIdleTick(object? sender, EventArgs e)
    {
        StopTypeaheadToastIdleTimer();
        _typeahead = "";
        _typeaheadIgnoreText = "";
        HideTypeaheadToast();
    }

    private void StopTypeaheadToastIdleTimer() => _typeaheadToastIdleTimer?.Stop();

    private void HideTypeaheadToast()
    {
        StopTypeaheadToastIdleTimer();
        _typeaheadToast.Hide();
    }

    private void SyncTypeaheadHighlights()
    {
        ClearTypeaheadHighlights();
        if (!IsTypeaheadActive
            || !TryCollectTypeahead(out var items, out var names, out _))
        {
            return;
        }

        for (var i = 0; i < items.Count; i++)
        {
            if (!LibraryExplorerTypeahead.Matches(names[i], _typeaheadWalk))
            {
                continue;
            }

            if (ApplyTypeaheadHighlight(items[i], names[i], _typeaheadWalk))
            {
                _typeaheadHighlighted.Add(items[i]);
            }
        }
    }

    private void SyncFilterHighlights()
    {
        if (IsTypeaheadActive || _filterGroups.Count == 0)
        {
            return;
        }

        ClearTypeaheadHighlights();
        ApplyFilterHighlights(_tree.Items);
    }

    private void ApplyFilterHighlights(ItemCollection items)
    {
        foreach (var raw in items)
        {
            if (raw is not TreeViewItem item || !item.IsEnabled)
            {
                continue;
            }

            var name = HeaderName(item);
            if (ApplyLibraryFilterHighlight(item, name, _filterGroups))
            {
                _typeaheadHighlighted.Add(item);
            }

            if (item.Items.Count > 0)
            {
                ApplyFilterHighlights(item.Items);
            }
        }
    }

    /// <summary>
    /// ライブラリ検索の各語（ワイルドカード以外）が名前に含まれる範囲を黄色くする。
    /// </summary>
    private bool ApplyLibraryFilterHighlight(
        TreeViewItem item,
        string name,
        IReadOnlyList<string[]> groups)
    {
        if (name.Length == 0 || groups.Count == 0)
        {
            return false;
        }

        var marks = new bool[name.Length];
        var any = false;
        foreach (var terms in groups)
        {
            foreach (var term in terms)
            {
                if (string.IsNullOrEmpty(term) || term.Contains('*') || term.Contains('?'))
                {
                    continue;
                }

                var start = 0;
                while (start < name.Length)
                {
                    var index = name.IndexOf(term, start, StringComparison.OrdinalIgnoreCase);
                    if (index < 0)
                    {
                        break;
                    }

                    any = true;
                    for (var i = 0; i < term.Length; i++)
                    {
                        marks[index + i] = true;
                    }

                    start = index + Math.Max(1, term.Length);
                }
            }
        }

        if (!any)
        {
            return false;
        }

        if (item.Header is not TextBlock block)
        {
            block = new TextBlock();
            item.Header = block;
        }

        block.Inlines.Clear();
        block.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryForeBrush");
        var runStart = 0;
        while (runStart < name.Length)
        {
            var highlighted = marks[runStart];
            var runEnd = runStart + 1;
            while (runEnd < name.Length && marks[runEnd] == highlighted)
            {
                runEnd++;
            }

            var text = name[runStart..runEnd];
            if (highlighted)
            {
                block.Inlines.Add(new Run(text)
                {
                    Background = TypeaheadHitBack,
                    Foreground = TypeaheadHitFore,
                });
            }
            else
            {
                block.Inlines.Add(new Run(text));
            }

            runStart = runEnd;
        }

        return true;
    }

    private void ClearTypeaheadHighlights()
    {
        if (_typeaheadHighlighted.Count == 0)
        {
            return;
        }

        foreach (var item in _typeaheadHighlighted)
        {
            RestoreHeader(item);
        }

        _typeaheadHighlighted.Clear();
    }

    private bool ApplyTypeaheadHighlight(TreeViewItem item, string name, string query)
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

        block.Inlines.Clear();
        block.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryForeBrush");
        if (start > 0)
        {
            block.Inlines.Add(new Run(name[..start]));
        }

        block.Inlines.Add(new Run(name.Substring(start, length))
        {
            Background = TypeaheadHitBack,
            Foreground = TypeaheadHitFore,
        });
        if (start + length < name.Length)
        {
            block.Inlines.Add(new Run(name[(start + length)..]));
        }

        return true;
    }

    private void RestoreHeader(TreeViewItem item)
    {
        var name = HeaderName(item);
        item.Header = MakeHeader(name);
    }

    private void TrySpaceExpandOrMoveDown()
    {
        ClearTypeahead();
        if (_tree.SelectedItem is not TreeViewItem item)
        {
            return;
        }

        if (item.HasItems && !item.IsExpanded)
        {
            item.IsExpanded = true;
            return;
        }

        MoveSelection(1);
    }

    private void TryShiftSpaceCollapseOrMoveUp()
    {
        ClearTypeahead();
        if (_tree.SelectedItem is not TreeViewItem item)
        {
            return;
        }

        if (item.HasItems && item.IsExpanded)
        {
            item.IsExpanded = false;
            return;
        }

        MoveSelection(-1);
    }

    private void MoveSelection(int delta) =>
        TryMoveSelection(delta, leaveAtBoundary: false);

    /// <summary>
    /// 選択を1行動かす。leaveAtBoundary なら端で false（呼び出し側が検索窓へ）。
    /// </summary>
    private bool TryMoveSelection(int delta, bool leaveAtBoundary)
    {
        if (delta == 0 || !TryCollectTypeahead(out var items, out _, out var current))
        {
            return false;
        }

        if (items.Count == 0)
        {
            return false;
        }

        int index;
        if (current < 0)
        {
            index = delta > 0 ? 0 : items.Count - 1;
        }
        else
        {
            var next = current + delta;
            if (next < 0 || next >= items.Count)
            {
                if (leaveAtBoundary)
                {
                    return false;
                }

                index = Math.Clamp(next, 0, items.Count - 1);
            }
            else
            {
                index = next;
            }
        }

        var item = items[index];
        item.IsSelected = true;
        item.BringIntoView();
        item.Focus();
        return true;
    }

    private void ClearTypeaheadIfSelectionMovedByMouse()
    {
        // マウスで別行を選んだときは打ち込みセッションを閉じる（ライブラリと同じ）。
        if (!IsTypeaheadActive)
        {
            return;
        }

        // キー操作での選択変更直後は残す。クリック直後のみ消す。
        if (Mouse.LeftButton == MouseButtonState.Pressed)
        {
            ClearTypeahead();
        }
    }

    private static void EnsureAncestorsExpanded(TreeViewItem item)
    {
        for (var parent = ItemsControl.ItemsControlFromItemContainer(item) as TreeViewItem;
             parent is not null;
             parent = ItemsControl.ItemsControlFromItemContainer(parent) as TreeViewItem)
        {
            parent.IsExpanded = true;
        }
    }

    private static TreeViewItem? FindTreeItem(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is TreeViewItem item)
            {
                return item;
            }

            node = VisualTreeHelper.GetParent(node)
                   ?? (node is FrameworkElement fe ? fe.Parent : null);
        }

        return null;
    }

    private void ApplyTreeStyle()
    {
        _tree.Background = Brushes.Transparent;
        _tree.BorderThickness = new Thickness(0);
        _tree.Padding = new Thickness(4, 6, 4, 6);
        _tree.FontSize = DesignMetrics.From96(13);
        ScrollViewer.SetHorizontalScrollBarVisibility(_tree, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(_tree, ScrollBarVisibility.Auto);
        _tree.SetResourceReference(ForegroundProperty, "PrimaryForeBrush");
        VirtualizingPanel.SetIsVirtualizing(_tree, true);
        VirtualizingPanel.SetVirtualizationMode(_tree, VirtualizationMode.Recycling);

        var hover = LibraryBrowserView.GrayHoverBrush();
        var selected = LibraryBrowserView.CyanSelectionBrush();
        var fore = TryBrush("PrimaryForeBrush", Brushes.WhiteSmoke);
        _tree.Resources[SystemColors.HighlightBrushKey] = selected;
        _tree.Resources[SystemColors.HighlightTextBrushKey] = fore;
        _tree.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = selected;
        _tree.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = fore;
        _tree.Resources[SystemColors.ControlBrushKey] = Brushes.Transparent;

        var itemStyle = new Style(typeof(TreeViewItem));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2, 1, 4, 1)));
        itemStyle.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
        _tree.ItemContainerStyle = itemStyle;
    }

    private static RadioButton CreateRadio(string content, bool isChecked)
    {
        var radio = new RadioButton
        {
            Content = content,
            IsChecked = isChecked,
            Margin = new Thickness(0, 0, 16, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            GroupName = "VstInsertPickerGroupBy",
        };
        radio.SetResourceReference(ForegroundProperty, "PrimaryForeBrush");
        var access = new Style(typeof(AccessText));
        access.Setters.Add(new Setter(
            TextElement.ForegroundProperty,
            new DynamicResourceExtension("PrimaryForeBrush")));
        radio.Resources.Add(typeof(AccessText), access);
        return radio;
    }

    private static RoundedButton CreateButton(
        string text,
        bool accent,
        bool isDefault = false,
        bool isCancel = false)
    {
        var button = new RoundedButton
        {
            Content = text,
            MinWidth = DesignMetrics.ConfirmSaveButtonWidth,
            Height = DesignMetrics.AudioDialogButtonHeight,
            Margin = new Thickness(8, 0, 0, 0),
        };
        AppDialogKeys.PrepareActionButton(button, isDefault: isDefault, isCancel: isCancel);
        if (accent)
        {
            ActionButtonLooks.ApplyAccent(button);
        }
        else
        {
            ActionButtonLooks.ApplyClear(button);
        }

        return button;
    }

    private static Brush TryBrush(string key, Brush fallback)
    {
        try
        {
            if (Application.Current?.TryFindResource(key) is Brush brush)
            {
                return brush;
            }
        }
        catch
        {
        }

        return fallback;
    }

    private static SolidColorBrush CreateHitBack()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x4C));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush CreateHitFore()
    {
        var brush = new SolidColorBrush(Colors.Black);
        brush.Freeze();
        return brush;
    }
}
