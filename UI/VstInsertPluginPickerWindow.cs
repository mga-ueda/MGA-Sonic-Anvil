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
/// メーカー／種別のツリー手繰り、ライブラリと同じインクリメンタルサーチ、キーボード操作。
/// モーダル表示のため本体ショートカットは反応しない。
/// </summary>
internal sealed class VstInsertPluginPickerWindow : Window
{
    private static readonly SolidColorBrush TypeaheadHitBack = CreateHitBack();
    private static readonly SolidColorBrush TypeaheadHitFore = CreateHitFore();

    private readonly IReadOnlyList<VstPickerEntry> _plugins;
    private readonly TreeView _tree = new();
    private readonly RadioButton _vendorRadio;
    private readonly RadioButton _categoryRadio;
    private readonly AcrylicToastHost _typeaheadToast = new();
    private readonly HashSet<TreeViewItem> _typeaheadHighlighted = [];
    private readonly Dictionary<TreeViewItem, string> _headerNames = new();

    private string _typeahead = "";
    private string _typeaheadWalk = "";
    private string _typeaheadIgnoreText = "";
    private DispatcherTimer? _typeaheadToastIdleTimer;
    private VstInsertPickerGroupMode _groupMode;
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
            Margin = new Thickness(0, 0, 0, 10),
        };
        radioRow.Children.Add(_vendorRadio);
        radioRow.Children.Add(_categoryRadio);

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
        var ok = CreateButton(UiStrings.ButtonOk, accent: true, isDefault: true);
        ok.Click += (_, _) => TryAcceptSelected();
        buttons.Children.Add(ok);
        var cancel = CreateButton(UiStrings.ButtonCancel, accent: false, isCancel: true);
        cancel.Click += (_, _) => Close();
        buttons.Children.Add(cancel);

        var root = new DockPanel();
        DockPanel.SetDock(radioRow, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(radioRow);
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
        AppDialogKeys.Attach(this, Close, interceptEscape: () => ClearTypeahead());
        // Attach の Loaded がボタンへフォーカスするため、その後でツリーへ寄せる。
        Loaded += (_, _) => Dispatcher.BeginInvoke(FocusTreeInitial, DispatcherPriority.Input);
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
        FocusTreeInitial();
    }

    private void RebuildTree()
    {
        _tree.Items.Clear();
        _headerNames.Clear();
        _typeaheadHighlighted.Clear();

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

        if (_groupMode == VstInsertPickerGroupMode.Vendor)
        {
            foreach (var group in VstInsertPluginMenu.GroupPickerByVendor(_plugins))
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
            foreach (var plugin in _plugins.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            {
                AddPluginUnderCategory(plugin);
            }

            SortItemCollection(_tree.Items);
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
        if (!ReferenceEquals(sender, e.OriginalSource) || sender is not TreeViewItem item)
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
        if (!LibraryExplorerTypeahead.IsTypeaheadText(e.Text))
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

            MoveSelection(direction);
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

    private void MoveSelection(int delta)
    {
        if (delta == 0 || !TryCollectTypeahead(out var items, out _, out var current))
        {
            return;
        }

        if (items.Count == 0)
        {
            return;
        }

        var index = current < 0
            ? (delta > 0 ? 0 : items.Count - 1)
            : Math.Clamp(current + delta, 0, items.Count - 1);
        var item = items[index];
        item.IsSelected = true;
        item.BringIntoView();
        item.Focus();
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
