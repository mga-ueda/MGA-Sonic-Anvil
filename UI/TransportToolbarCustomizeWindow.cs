using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>ツールバーに出すコマンドを選ぶ専用ダイアログ。並びは常におまかせ配置。</summary>
internal sealed class TransportToolbarCustomizeWindow : Window
{
    private readonly ListBox _availableList = CreateList();
    private readonly ListBox _currentList = CreateList();
    private readonly RoundedButton _addButton;
    private readonly RoundedButton _removeButton;
    private readonly TextBlock _descriptionText;
    private readonly WrapPanel _iconStatesPanel;
    private readonly Action<string[]>? _preview;
    private readonly string[] _openedAt;
    private LayoutRow? _hoveredRow;
    private bool _ready;

    public string[] SelectedLayout { get; private set; }

    public static string[]? Show(
        Window owner,
        IEnumerable<string>? layout,
        IEnumerable<string>? hidden,
        Action<string[]>? preview = null)
    {
        var initial = TransportToolbarCatalog.ResolveLayout(layout, hidden);
        var dialog = new TransportToolbarCustomizeWindow(initial, preview)
        {
            Owner = owner,
        };
        WindowPaintReveal.EnsureRevealedForDialog(owner);
        return AcrylicDialogChrome.ShowDialog(dialog) == true
            ? dialog.SelectedLayout
            : null;
    }

    private TransportToolbarCustomizeWindow(string[] initial, Action<string[]>? preview)
    {
        _preview = preview;
        _openedAt = [.. initial];
        SelectedLayout = [.. initial];
        Title = UiStrings.DialogTransportToolbarCustomizeTitle;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        MinWidth = 640;
        MinHeight = 420;
        Width = 720;
        Height = 480;
        Foreground = (Brush)FindResource("PrimaryForeBrush");
        AcrylicDialogChrome.Prepare(
            this,
            ResizeMode.CanResizeWithGrip,
            SizeToContent.Manual);

        _addButton = CreateActionButton(
            UiStrings.ButtonTransportToolbarAdd,
            AddSelected,
            new Thickness(0, 0, 0, 8));
        _removeButton = CreateActionButton(
            UiStrings.ButtonTransportToolbarRemove,
            RemoveSelected,
            new Thickness(0, 0, 0, 0));
        var resetButton = CreateActionButton(
            UiStrings.ButtonTransportToolbarResetLayout,
            ResetToDefault,
            new Thickness(0, 0, 0, 0));

        _descriptionText = new TextBlock
        {
            Text = UiStrings.LabelTransportToolbarDescriptionHint,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            MinHeight = 36,
            Margin = new Thickness(0, 0, 0, 10),
            VerticalAlignment = VerticalAlignment.Top,
        };
        _descriptionText.SetResourceReference(TextBlock.ForegroundProperty, "MutedForeBrush");
        _iconStatesPanel = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, -4, 0, 10),
            // 説明の切替でリストの高さが揺れないよう、アイコンが 1 種類の項目でも行の高さを確保する。
            MinHeight = 22,
            Visibility = Visibility.Hidden,
        };

        _availableList.MouseDoubleClick += (_, _) => AddSelected();
        _currentList.MouseDoubleClick += (_, e) =>
        {
            if (_currentList.SelectedItem is LayoutRow { IsSeparator: false, IsCategoryHeader: false })
            {
                RemoveSelected();
            }

            e.Handled = true;
        };
        WireDescriptionHover(_availableList);
        WireDescriptionHover(_currentList);
        _availableList.SelectionChanged += (_, _) =>
        {
            RefreshButtons();
            RefreshDescription();
        };
        _currentList.SelectionChanged += (_, _) =>
        {
            RefreshButtons();
            RefreshDescription();
        };

        ActionButtonLooks.ApplyClear(_addButton);
        ActionButtonLooks.ApplyClear(_removeButton);
        ActionButtonLooks.ApplyClear(resetButton);

        var ok = CreateDialogButton(UiStrings.ButtonOk, accept: true, accent: true, isDefault: true);
        var cancel = CreateDialogButton(UiStrings.ButtonCancel, accept: false, isCancel: true);

        var midButtons = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0),
        };
        midButtons.Children.Add(_addButton);
        midButtons.Children.Add(_removeButton);

        var lists = new Grid();
        lists.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        lists.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        lists.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        lists.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        lists.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var availableHeader = CreateHeader(UiStrings.LabelTransportToolbarAvailable);
        var currentHeader = CreateHeader(UiStrings.LabelTransportToolbarCurrent);
        Grid.SetRow(availableHeader, 0);
        Grid.SetColumn(availableHeader, 0);
        Grid.SetRow(currentHeader, 0);
        Grid.SetColumn(currentHeader, 2);
        Grid.SetRow(_availableList, 1);
        Grid.SetColumn(_availableList, 0);
        Grid.SetRow(midButtons, 1);
        Grid.SetColumn(midButtons, 1);
        Grid.SetRow(_currentList, 1);
        Grid.SetColumn(_currentList, 2);
        lists.Children.Add(availableHeader);
        lists.Children.Add(currentHeader);
        lists.Children.Add(_availableList);
        lists.Children.Add(midButtons);
        lists.Children.Add(_currentList);

        var bottomLeft = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        bottomLeft.Children.Add(resetButton);

        var bottomRight = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        bottomRight.Children.Add(ok);
        bottomRight.Children.Add(cancel);

        var bottomButtons = new Grid();
        bottomButtons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottomButtons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(bottomLeft, 0);
        Grid.SetColumn(bottomRight, 1);
        bottomButtons.Children.Add(bottomLeft);
        bottomButtons.Children.Add(bottomRight);

        var bottom = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        bottom.Children.Add(_descriptionText);
        bottom.Children.Add(_iconStatesPanel);
        bottom.Children.Add(bottomButtons);

        var root = new DockPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(lists);
        AcrylicDialogChrome.SetBody(this, root, DesignMetrics.AudioPad);
        AppDialogKeys.Attach(this, Close);

        LoadLayout(initial);
        _ready = true;
        RefreshDescription();
        NotifyPreview();
    }

    private void Accept()
    {
        SelectedLayout = ReadLayout();
        DialogResult = true;
        Close();
    }

    private void LoadLayout(IEnumerable<string> layout)
    {
        var normalized = TransportToolbarCatalog.NormalizeLayout(layout);
        var placed = new HashSet<TransportCommand>();
        _currentList.Items.Clear();
        foreach (var entry in normalized)
        {
            if (TransportToolbarCatalog.IsSeparator(entry))
            {
                _currentList.Items.Add(LayoutRow.Separator(TransportToolbarCatalog.IsSectionSeparator(entry)));
                continue;
            }

            if (!TransportToolbarCatalog.TryGetItem(entry, out var item))
            {
                continue;
            }

            placed.Add(item.Command);
            _currentList.Items.Add(LayoutRow.ForCommand(item));
        }

        RebuildAvailableList(placed);
        RefreshButtons();
    }

    private void RebuildAvailableList(HashSet<TransportCommand> placed)
    {
        _availableList.Items.Clear();
        string? currentGroup = null;
        foreach (var (item, group) in TransportToolbarCatalog.OrderedWithGroupTitles())
        {
            if (placed.Contains(item.Command))
            {
                continue;
            }

            if (!string.Equals(currentGroup, group, StringComparison.Ordinal))
            {
                currentGroup = group;
                _availableList.Items.Add(LayoutRow.Category(group));
            }

            _availableList.Items.Add(LayoutRow.ForCommand(item));
        }
    }

    private HashSet<TransportCommand> ReadPlacedCommands()
    {
        var placed = new HashSet<TransportCommand>();
        foreach (var row in _currentList.Items.OfType<LayoutRow>())
        {
            if (!row.IsSeparator && !row.IsCategoryHeader)
            {
                placed.Add(row.Command);
            }
        }

        return placed;
    }

    private string[] ReadLayout() =>
        TransportToolbarCatalog.BuildAutoLayout(
            ReadPlacedCommands().Select(command => command.ToString()));

    private void AddSelected()
    {
        var selected = _availableList.SelectedItems.OfType<LayoutRow>()
            .Where(row => !row.IsSeparator && !row.IsCategoryHeader)
            .ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        // 連続追加のため、追加後も候補リストの同じ位置付近を選んだままフォーカスを戻す。
        var resumeIndex = FirstSelectedIndex(_availableList, selected);
        var placed = ReadPlacedCommands();
        foreach (var row in selected)
        {
            placed.Add(row.Command);
        }

        LoadLayout(TransportToolbarCatalog.BuildAutoLayout(
            placed.Select(command => command.ToString())));
        SelectNearestCommandRow(_availableList, resumeIndex);
        NotifyPreview();
    }

    private void RemoveSelected()
    {
        var selected = _currentList.SelectedItems.OfType<LayoutRow>()
            .Where(row => !row.IsSeparator && !row.IsCategoryHeader)
            .ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        // 連続削除のため、削除後も配置リストの同じ位置付近を選んだままフォーカスを戻す。
        var resumeIndex = FirstSelectedIndex(_currentList, selected);
        var placed = ReadPlacedCommands();
        foreach (var row in selected)
        {
            placed.Remove(row.Command);
        }

        LoadLayout(TransportToolbarCatalog.BuildAutoLayout(
            placed.Select(command => command.ToString())));
        SelectNearestCommandRow(_currentList, resumeIndex);
        NotifyPreview();
    }

    private static int FirstSelectedIndex(ListBox list, IReadOnlyList<LayoutRow> selected)
    {
        var min = int.MaxValue;
        foreach (var row in selected)
        {
            var index = list.Items.IndexOf(row);
            if (index >= 0 && index < min)
            {
                min = index;
            }
        }

        return min == int.MaxValue ? 0 : min;
    }

    private static void SelectNearestCommandRow(ListBox list, int preferredIndex)
    {
        if (list.Items.Count == 0)
        {
            list.UnselectAll();
            list.Focus();
            return;
        }

        LayoutRow? pick = null;
        for (var i = Math.Max(0, preferredIndex); i < list.Items.Count; i++)
        {
            if (list.Items[i] is LayoutRow { IsSeparator: false, IsCategoryHeader: false } row)
            {
                pick = row;
                break;
            }
        }

        if (pick is null)
        {
            for (var i = Math.Min(preferredIndex, list.Items.Count) - 1; i >= 0; i--)
            {
                if (list.Items[i] is LayoutRow { IsSeparator: false, IsCategoryHeader: false } row)
                {
                    pick = row;
                    break;
                }
            }
        }

        list.UnselectAll();
        if (pick is null)
        {
            list.Focus();
            return;
        }

        list.SelectedItem = pick;
        list.ScrollIntoView(pick);
        // Items 再構築直後はコンテナ未生成のため、フォーカスはレイアウト後に当てる。
        list.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new Action(() =>
            {
                if (!list.IsKeyboardFocusWithin)
                {
                    list.Focus();
                }

                if (list.ItemContainerGenerator.ContainerFromItem(pick) is ListBoxItem container)
                {
                    container.Focus();
                }
            }));
    }

    private void ResetToDefault()
    {
        LoadLayout(TransportToolbarCatalog.CreateDefaultLayout());
        NotifyPreview();
    }

    private void RefreshButtons()
    {
        _addButton.IsEnabled = _availableList.SelectedItems.OfType<LayoutRow>()
            .Any(row => !row.IsSeparator && !row.IsCategoryHeader);
        _removeButton.IsEnabled = _currentList.SelectedItems.OfType<LayoutRow>()
            .Any(row => !row.IsSeparator && !row.IsCategoryHeader);
    }

    private void WireDescriptionHover(ListBox list)
    {
        list.MouseMove += (_, e) =>
        {
            var row = FindDescribableRow(list, e.GetPosition(list));
            if (ReferenceEquals(_hoveredRow, row))
            {
                return;
            }

            _hoveredRow = row;
            RefreshDescription();
        };
        list.MouseLeave += (_, _) =>
        {
            if (_hoveredRow is null)
            {
                return;
            }

            _hoveredRow = null;
            RefreshDescription();
        };
    }

    private static LayoutRow? FindDescribableRow(ListBox list, Point position)
    {
        if (list.InputHitTest(position) is not DependencyObject hit)
        {
            return null;
        }

        for (var current = hit; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is ListBoxItem { DataContext: LayoutRow row })
            {
                return row.IsCategoryHeader ? null : row;
            }

            if (ReferenceEquals(current, list))
            {
                break;
            }
        }

        return null;
    }

    private void RefreshDescription()
    {
        // 追加候補（左）を配置済み（右）より優先して説明する。
        var row = _hoveredRow
            ?? _availableList.SelectedItem as LayoutRow
            ?? _currentList.SelectedItem as LayoutRow;
        if (row is null || row.IsCategoryHeader)
        {
            _descriptionText.Text = UiStrings.LabelTransportToolbarDescriptionHint;
            ShowIconStates(default, []);
            return;
        }

        _descriptionText.Text = row.IsSeparator
            ? UiStrings.LabelTransportToolbarSeparatorDescription
            : row.Description;
        ShowIconStates(row.Command, row.Variants);
    }

    private void ShowIconStates(
        TransportCommand command,
        IReadOnlyList<TransportToolbarCatalog.IconVariant> variants)
    {
        _iconStatesPanel.Children.Clear();
        if (variants.Count == 0)
        {
            _iconStatesPanel.Visibility = Visibility.Hidden;
            return;
        }

        var header = new TextBlock
        {
            Text = UiStrings.LabelTransportToolbarIconStates,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        header.SetResourceReference(TextBlock.ForegroundProperty, "MutedForeBrush");
        _iconStatesPanel.Children.Add(header);
        foreach (var variant in variants)
        {
            var item = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 14, 0),
            };
            // トグル面の色は TransportIconButton 側。Glyph だと LatchToggle 背景が出ない。
            item.Children.Add(new TransportIconButton
            {
                CommandKind = command,
                Icon = variant.Icon,
                IsPlaying = variant.Playing,
                IsLatched = variant.Latched,
                Width = 22,
                Height = 22,
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                Focusable = false,
            });
            var state = new TextBlock
            {
                Text = variant.StateText,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            };
            state.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryForeBrush");
            item.Children.Add(state);
            _iconStatesPanel.Children.Add(item);
        }

        _iconStatesPanel.Visibility = Visibility.Visible;
    }

    private void NotifyPreview()
    {
        if (!_ready || _preview is null)
        {
            return;
        }

        _preview(ReadLayout());
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DialogResult != true && _preview is not null)
        {
            _preview([.. _openedAt]);
        }

        base.OnClosed(e);
    }

    private RoundedButton CreateActionButton(string text, Action action, Thickness margin)
    {
        var button = new RoundedButton
        {
            Content = text,
            Height = DesignMetrics.AudioInputHeight,
            MinWidth = 88,
            Padding = new Thickness(10, 0, 10, 0),
            Margin = margin,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        button.Click += (_, _) => action();
        return button;
    }

    private RoundedButton CreateDialogButton(
        string text,
        bool accept,
        bool accent = false,
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
        AppDialogKeys.PrepareActionButton(button, isDefault, isCancel);
        if (accent)
        {
            ActionButtonLooks.ApplyAccent(button);
        }
        else
        {
            ActionButtonLooks.ApplyClear(button);
        }

        button.Click += (_, _) =>
        {
            if (accept)
            {
                Accept();
            }
            else
            {
                Close();
            }
        };
        return button;
    }

    private static TextBlock CreateHeader(string text) =>
        new()
        {
            Text = text,
            FontWeight = FontWeights.Bold,
            FontSize = 11.333,
            Margin = new Thickness(0, 0, 0, 6),
        };

    private static ListBox CreateList()
    {
        var list = new ListBox
        {
            FontSize = 11,
            Padding = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            SelectionMode = SelectionMode.Extended,
            BorderThickness = new Thickness(1),
        };
        list.SetResourceReference(Control.ForegroundProperty, "PrimaryForeBrush");
        list.SetResourceReference(Control.BackgroundProperty, "DialogInputBackBrush");
        list.SetResourceReference(Control.BorderBrushProperty, "ChromeBorderBrush");
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Auto);
        ScrollViewer.SetCanContentScroll(list, false);

        var highlight = WpfControlHelpers.FrozenBrush(Theme.Get("MenuHighlightBackBrush"));
        var fore = WpfControlHelpers.FrozenBrush(Theme.Get("PrimaryForeBrush"));
        list.Resources[SystemColors.HighlightBrushKey] = highlight;
        list.Resources[SystemColors.HighlightTextBrushKey] = fore;
        list.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = highlight;
        list.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = fore;
        list.Resources[SystemColors.ControlBrushKey] = Brushes.Transparent;

        var itemStyle = new Style(typeof(ListBoxItem));
        itemStyle.Setters.Add(new Setter(
            Control.ForegroundProperty,
            new DynamicResourceExtension("PrimaryForeBrush")));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 3, 6, 3)));
        // 境界行は中身が空なので、Stretch しないと罫線の幅が 0 になる。
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        itemStyle.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        var categoryTrigger = new DataTrigger
        {
            Binding = new System.Windows.Data.Binding(nameof(LayoutRow.IsCategoryHeader)),
            Value = true,
        };
        categoryTrigger.Setters.Add(new Setter(UIElement.FocusableProperty, false));
        categoryTrigger.Setters.Add(new Setter(UIElement.IsHitTestVisibleProperty, false));
        categoryTrigger.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 8, 6, 2)));
        itemStyle.Triggers.Add(categoryTrigger);
        list.ItemContainerStyle = itemStyle;

        var root = new FrameworkElementFactory(typeof(Grid));
        root.SetValue(FrameworkElement.MinHeightProperty, 22d);
        root.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);

        var category = new FrameworkElementFactory(typeof(TextBlock));
        category.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(LayoutRow.Label)));
        category.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        category.SetValue(TextBlock.FontSizeProperty, 10.5d);
        category.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        category.SetValue(
            TextBlock.ForegroundProperty,
            new DynamicResourceExtension("MutedForeBrush"));
        category.SetBinding(
            UIElement.VisibilityProperty,
            new System.Windows.Data.Binding(nameof(LayoutRow.CategoryVisibility)));
        root.AppendChild(category);

        var commandPanel = new FrameworkElementFactory(typeof(StackPanel));
        commandPanel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        commandPanel.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        commandPanel.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        commandPanel.SetBinding(
            UIElement.VisibilityProperty,
            new System.Windows.Data.Binding(nameof(LayoutRow.CommandVisibility)));

        var icon = new FrameworkElementFactory(typeof(TransportIconGlyph));
        icon.SetBinding(TransportIconGlyph.IconProperty, new System.Windows.Data.Binding(nameof(LayoutRow.Icon)));
        icon.SetValue(FrameworkElement.WidthProperty, 20d);
        icon.SetValue(FrameworkElement.HeightProperty, 20d);
        icon.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
        icon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        commandPanel.AppendChild(icon);

        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(LayoutRow.Label)));
        label.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        label.SetValue(
            TextBlock.ForegroundProperty,
            new DynamicResourceExtension("PrimaryForeBrush"));
        commandPanel.AppendChild(label);

        var otherIconsPanel = new FrameworkElementFactory(typeof(StackPanel));
        otherIconsPanel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var otherIcon = new FrameworkElementFactory(typeof(TransportIconGlyph));
        otherIcon.SetBinding(TransportIconGlyph.IconProperty, new System.Windows.Data.Binding(nameof(TransportToolbarCatalog.IconVariant.Icon)));
        otherIcon.SetBinding(TransportIconGlyph.IsPlayingProperty, new System.Windows.Data.Binding(nameof(TransportToolbarCatalog.IconVariant.Playing)));
        otherIcon.SetBinding(TransportIconGlyph.IsLatchedProperty, new System.Windows.Data.Binding(nameof(TransportToolbarCatalog.IconVariant.Latched)));
        otherIcon.SetBinding(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding(nameof(TransportToolbarCatalog.IconVariant.StateText)));
        otherIcon.SetValue(FrameworkElement.WidthProperty, 16d);
        otherIcon.SetValue(FrameworkElement.HeightProperty, 16d);
        otherIcon.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 0, 0, 0));
        otherIcon.SetValue(UIElement.OpacityProperty, 0.85);
        var otherIcons = new FrameworkElementFactory(typeof(ItemsControl));
        otherIcons.SetBinding(ItemsControl.ItemsSourceProperty, new System.Windows.Data.Binding(nameof(LayoutRow.OtherVariants)));
        otherIcons.SetValue(ItemsControl.ItemsPanelProperty, new ItemsPanelTemplate(otherIconsPanel));
        otherIcons.SetValue(ItemsControl.ItemTemplateProperty, new DataTemplate { VisualTree = otherIcon });
        otherIcons.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 0, 0, 0));
        otherIcons.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        otherIcons.SetValue(UIElement.FocusableProperty, false);
        commandPanel.AppendChild(otherIcons);
        root.AppendChild(commandPanel);

        var rule = new FrameworkElementFactory(typeof(SeparatorRule));
        rule.SetBinding(FrameworkElement.HeightProperty, new System.Windows.Data.Binding(nameof(LayoutRow.SeparatorHeight)));
        rule.SetBinding(UIElement.OpacityProperty, new System.Windows.Data.Binding(nameof(LayoutRow.SeparatorOpacity)));
        rule.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        rule.SetBinding(
            UIElement.VisibilityProperty,
            new System.Windows.Data.Binding(nameof(LayoutRow.SeparatorVisibility)));
        root.AppendChild(rule);

        list.ItemTemplate = new DataTemplate(typeof(LayoutRow)) { VisualTree = root };
        return list;
    }

    private sealed class LayoutRow
    {
        private LayoutRow(
            bool separator,
            bool category,
            TransportCommand command,
            TransportIcon icon,
            string label,
            string description,
            bool section = false)
        {
            IsSeparator = separator;
            IsSectionSeparator = section;
            IsCategoryHeader = category;
            Command = command;
            Icon = icon;
            Label = label;
            Description = description;
            Variants = separator || category
                ? []
                : TransportToolbarCatalog.IconVariants(command);
        }

        /// <summary>状態プレビュー用。先頭は通常時で、行の主アイコンと同じ。</summary>
        public IReadOnlyList<TransportToolbarCatalog.IconVariant> Variants { get; }
        public IReadOnlyList<TransportToolbarCatalog.IconVariant> OtherVariants => [.. Variants.Skip(1)];

        public bool IsSeparator { get; }

        /// <summary>グループ内の区切り（メニューのセパレータ／サブメニュー境界）。カテゴリ境界より控えめに描く。</summary>
        public bool IsSectionSeparator { get; }
        public double SeparatorHeight => IsSectionSeparator ? 10d : 22d;
        public double SeparatorOpacity => IsSectionSeparator ? 0.45 : 1d;
        public bool IsCategoryHeader { get; }
        public TransportCommand Command { get; }
        public TransportIcon Icon { get; }
        public string Label { get; }
        public string Description { get; }
        public Visibility CategoryVisibility =>
            IsCategoryHeader ? Visibility.Visible : Visibility.Collapsed;
        public Visibility CommandVisibility =>
            IsSeparator || IsCategoryHeader ? Visibility.Collapsed : Visibility.Visible;
        public Visibility SeparatorVisibility =>
            IsSeparator ? Visibility.Visible : Visibility.Collapsed;

        public static LayoutRow Separator(bool section) =>
            new(
                true,
                false,
                default,
                TransportIcon.PlayPause,
                string.Empty,
                UiStrings.LabelTransportToolbarSeparatorDescription,
                section);

        public static LayoutRow Category(string title) =>
            new(
                false,
                true,
                default,
                TransportIcon.PlayPause,
                title,
                string.Empty);

        public static LayoutRow ForCommand(TransportToolbarCatalog.Item item) =>
            new(false, false, item.Command, item.Icon, item.Label(), TransportToolbarCatalog.Describe(item));
    }

    /// <summary>リスト内のカテゴリ境界。幅いっぱいに 1px の横線を描く。</summary>
    private sealed class SeparatorRule : FrameworkElement
    {
        public SeparatorRule()
        {
            SnapsToDevicePixels = true;
            MinWidth = 24;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var width = RenderSize.Width;
            var height = RenderSize.Height;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            var y = Math.Floor(height * 0.5) + 0.5;
            var pen = new Pen(WpfControlHelpers.FrozenBrush(Theme.Get("MutedForeBrush")), 1)
            {
                StartLineCap = PenLineCap.Flat,
                EndLineCap = PenLineCap.Flat,
            };
            pen.Freeze();
            dc.DrawLine(pen, new Point(4, y), new Point(Math.Max(4, width - 4), y));
        }
    }
}
