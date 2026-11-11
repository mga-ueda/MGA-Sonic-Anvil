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

internal sealed partial class LibraryBrowserView
{
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
        itemStyle.Setters.Add(new Setter(UIElement.ClipToBoundsProperty, false));
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
        // 選択帯は行データフラグ。DataGrid のセル IsSelected には依存しない。
        chrome.Bindings.Add(new Binding(nameof(LibraryFileRow.IsPlaylistSelected)));
        chrome.Bindings.Add(new Binding(nameof(UIElement.IsMouseOver))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
        });
        band.SetBinding(Border.BackgroundProperty, chrome);

        var corners = new MultiBinding { Converter = PlaylistRowCornerConverter.Instance };
        corners.Bindings.Add(new Binding(nameof(LibraryFileRow.SelectionBandEdge)));
        corners.Bindings.Add(new Binding(nameof(LibraryFileRow.IsPlaylistSelected)));
        corners.Bindings.Add(new Binding(nameof(UIElement.IsMouseOver))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
        });
        corners.Bindings.Add(DesignMetrics.RadiusBinding());
        band.SetBinding(Border.CornerRadiusProperty, corners);

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

    private sealed class PlaylistRowCornerConverter : IMultiValueConverter
    {
        internal static readonly PlaylistRowCornerConverter Instance = new();

        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var radius = values is { Length: > 3 } && values[3] is double pixels
                ? pixels
                : DesignMetrics.ControlCornerRadius;
            var selected = values is { Length: > 1 } && values[1] is true;
            if (selected)
            {
                var edge = values[0] is SelectionBandEdge bandEdge && bandEdge != SelectionBandEdge.None
                    ? bandEdge
                    : SelectionBandEdge.Solo;
                return SelectionBandCorners.Of(edge, radius);
            }

            if (values is { Length: > 2 } && values[2] is true)
            {
                return SelectionBandCorners.Of(SelectionBandEdge.Solo, radius);
            }

            return default(CornerRadius);
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

    internal static readonly DependencyProperty SelectionBandEdgeProperty = DependencyProperty.RegisterAttached(
        "SelectionBandEdge",
        typeof(SelectionBandEdge),
        typeof(LibraryBrowserView),
        new FrameworkPropertyMetadata(SelectionBandEdge.None));

    internal static readonly DependencyProperty TreeIndentDepthProperty = DependencyProperty.RegisterAttached(
        "TreeIndentDepth",
        typeof(int),
        typeof(LibraryBrowserView),
        new FrameworkPropertyMetadata(0));

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
        bd.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        bd.SetValue(UIElement.ClipToBoundsProperty, false);
        bd.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        // インデント分だけ左へ伸ばし、選択帯の左端をルート行に揃える（階段状の直角を消す）。
        bd.SetBinding(
            FrameworkElement.MarginProperty,
            new Binding
            {
                Path = new PropertyPath(TreeIndentDepthProperty),
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
                Converter = TreeBandBleedMarginConverter.Instance,
            });
        var treePadding = new MultiBinding { Converter = TreeBandBleedPaddingConverter.Instance };
        treePadding.Bindings.Add(new Binding(nameof(Control.Padding))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
        });
        treePadding.Bindings.Add(new Binding
        {
            Path = new PropertyPath(TreeIndentDepthProperty),
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
        });
        bd.SetBinding(Border.PaddingProperty, treePadding);
        var treeCorners = new MultiBinding { Converter = TreeNavCornerConverter.Instance };
        treeCorners.Bindings.Add(new Binding
        {
            Path = new PropertyPath(SelectionBandEdgeProperty),
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
        });
        treeCorners.Bindings.Add(new Binding(nameof(TreeViewItem.IsSelected))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
        });
        treeCorners.Bindings.Add(new Binding
        {
            Path = new PropertyPath(TreeMarkedProperty),
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
        });
        treeCorners.Bindings.Add(new Binding(nameof(UIElement.IsMouseOver))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
        });
        treeCorners.Bindings.Add(DesignMetrics.RadiusBinding());
        bd.SetBinding(Border.CornerRadiusProperty, treeCorners);
        bd.AppendChild(row);

        var items = new FrameworkElementFactory(typeof(ItemsPresenter));
        items.Name = "ItemsHost";
        items.SetValue(FrameworkElement.MarginProperty, new Thickness(TreeChildIndent, 0, 0, 0));
        items.SetValue(UIElement.ClipToBoundsProperty, false);

        var root = new FrameworkElementFactory(typeof(StackPanel));
        root.SetValue(UIElement.ClipToBoundsProperty, false);
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
        var listCorners = new MultiBinding { Converter = ListNavCornerConverter.Instance };
        listCorners.Bindings.Add(new Binding
        {
            Path = new PropertyPath(SelectionBandEdgeProperty),
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
        });
        listCorners.Bindings.Add(new Binding(nameof(ListBoxItem.IsSelected))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
        });
        listCorners.Bindings.Add(new Binding(nameof(UIElement.IsMouseOver))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
        });
        listCorners.Bindings.Add(DesignMetrics.RadiusBinding());
        bd.SetBinding(Border.CornerRadiusProperty, listCorners);
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

    private sealed class TreeBandBleedMarginConverter : IValueConverter
    {
        internal static readonly TreeBandBleedMarginConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var depth = value is int pixels ? Math.Max(0, pixels) : 0;
            var bleed = depth * TreeChildIndent;
            return new Thickness(-bleed, 0, 0, 0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            Binding.DoNothing;
    }

    private sealed class TreeBandBleedPaddingConverter : IMultiValueConverter
    {
        internal static readonly TreeBandBleedPaddingConverter Instance = new();

        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var padding = values is { Length: > 0 } && values[0] is Thickness box
                ? box
                : new Thickness(2, 1, 4, 1);
            var depth = values is { Length: > 1 } && values[1] is int pixels
                ? Math.Max(0, pixels)
                : 0;
            var bleed = depth * TreeChildIndent;
            return new Thickness(padding.Left + bleed, padding.Top, padding.Right, padding.Bottom);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture) =>
            [];
    }

    private sealed class TreeNavCornerConverter : IMultiValueConverter
    {
        internal static readonly TreeNavCornerConverter Instance = new();

        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var radius = values is { Length: > 4 } && values[4] is double pixels
                ? pixels
                : DesignMetrics.ControlCornerRadius;
            var selected = (values is { Length: > 1 } && values[1] is true)
                || (values is { Length: > 2 } && values[2] is true);
            if (selected)
            {
                var edge = values[0] is SelectionBandEdge bandEdge && bandEdge != SelectionBandEdge.None
                    ? bandEdge
                    : SelectionBandEdge.Solo;
                return SelectionBandCorners.Of(edge, radius);
            }

            if (values is { Length: > 3 } && values[3] is true)
            {
                return SelectionBandCorners.Of(SelectionBandEdge.Solo, radius);
            }

            return default(CornerRadius);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture) =>
            [];
    }

    internal static int MeasureTreeIndentDepth(TreeViewItem item)
    {
        var depth = 0;
        for (DependencyObject? node = item;
             ItemsControl.ItemsControlFromItemContainer(node) is TreeViewItem parent;
             node = parent)
        {
            depth++;
        }

        return depth;
    }

    internal static Thickness TreeBandBleedMargin(int depth) =>
        new(-Math.Max(0, depth) * TreeChildIndent, 0, 0, 0);

    internal static Thickness TreeBandBleedPadding(Thickness padding, int depth) =>
        new(
            padding.Left + Math.Max(0, depth) * TreeChildIndent,
            padding.Top,
            padding.Right,
            padding.Bottom);

    private static void SyncTreeIndentDepth(TreeViewItem item) =>
        item.SetValue(TreeIndentDepthProperty, MeasureTreeIndentDepth(item));

    private sealed class ListNavCornerConverter : IMultiValueConverter
    {
        internal static readonly ListNavCornerConverter Instance = new();

        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var radius = values is { Length: > 3 } && values[3] is double pixels
                ? pixels
                : DesignMetrics.ControlCornerRadius;
            if (values is { Length: > 1 } && values[1] is true)
            {
                var edge = values[0] is SelectionBandEdge bandEdge && bandEdge != SelectionBandEdge.None
                    ? bandEdge
                    : SelectionBandEdge.Solo;
                return SelectionBandCorners.Of(edge, radius);
            }

            if (values is { Length: > 2 } && values[2] is true)
            {
                return SelectionBandCorners.Of(SelectionBandEdge.Solo, radius);
            }

            return default(CornerRadius);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture) =>
            [];
    }
}
