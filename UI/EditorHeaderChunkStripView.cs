using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.UI;

/// <summary>
/// エディタ chrome のチャンクリスト。タイムコードと編集履歴のあいだ。
/// 枠は <see cref="HistoryStripView"/> と同じ（角丸＋ VST 空きスロット色）。
/// 選択帯はプレイヤーのヘッダ構造ツリーと同じ（シアン＋角丸）。
/// 幅は内容に合わせて可変（履歴帯幅を上限）。
/// </summary>
internal sealed class EditorHeaderChunkStripView : Border
{
    private static readonly FontFamily MonoFont = new("Consolas");
    private readonly TreeView _tree = new();
    private string? _loadedPath;

    public EditorHeaderChunkStripView()
    {
        MinWidth = DesignMetrics.EditorHeaderChunkStripMinWidth;
        MaxWidth = DesignMetrics.EditorHeaderChunkStripMaxWidth;
        Width = DesignMetrics.EditorHeaderChunkStripMinWidth;
        Height = DesignMetrics.SpectrumHeight;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(1);
        SetResourceReference(BorderBrushProperty, "VstInsertSlotEmptyBackBrush");
        DesignMetrics.BindCorner(this);
        Padding = new Thickness(2, 2, 2, 2);

        _tree.BorderThickness = new Thickness(0);
        _tree.Background = Brushes.Transparent;
        _tree.Padding = new Thickness(2, 0, 2, 0);
        _tree.FontFamily = MonoFont;
        _tree.FontSize = 11;
        _tree.Focusable = true;
        ScrollViewer.SetHorizontalScrollBarVisibility(_tree, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(_tree, ScrollBarVisibility.Auto);
        _tree.SetResourceReference(Control.ForegroundProperty, "PrimaryForeBrush");
        ApplyTreeItemStyle();
        // 展開で深い行のキャプション幅が変わったとき取り直す。
        _tree.AddHandler(
            TreeViewItem.ExpandedEvent,
            new RoutedEventHandler((_, _) => FitContentWidth()),
            handledEventsToo: true);
        _tree.AddHandler(
            TreeViewItem.CollapsedEvent,
            new RoutedEventHandler((_, _) => FitContentWidth()),
            handledEventsToo: true);
        Child = _tree;
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

    public void Clear()
    {
        _loadedPath = null;
        _tree.Items.Clear();
        Width = DesignMetrics.EditorHeaderChunkStripMinWidth;
    }

    /// <summary>トグルでオンにしたとき、チャンクツリーへフォーカスする。</summary>
    public void FocusTree()
    {
        if (_tree.Items.OfType<TreeViewItem>().FirstOrDefault() is { } first)
        {
            if (!first.IsSelected)
            {
                first.IsSelected = true;
            }

            first.Focus();
            return;
        }

        _tree.Focus();
    }

    private void Populate(WaveStructureDocument document)
    {
        _tree.Items.Clear();
        if (document.IsUnsupported || document.Roots.Count == 0)
        {
            Width = DesignMetrics.EditorHeaderChunkStripMinWidth;
            return;
        }

        // コンテナ根はツリー親にせず1行。子をトップレベルにする。
        var displayRoots = WaveStructureNode.ForTreeDisplay(document.Roots);
        var sizeFieldWidth = WaveStructureNode.MaxSizeFieldWidth(displayRoots);
        var nameFieldWidth = WaveStructureNode.NameFieldWidth(WaveStructureNode.MaxDepth(displayRoots));
        foreach (var root in displayRoots)
        {
            _tree.Items.Add(CreateItem(root, depth: 0, nameFieldWidth, sizeFieldWidth));
        }

        FitContentWidth();
    }

    /// <summary>キャプション幅に合わせて帯幅を決める。履歴帯幅を超えない。</summary>
    private void FitContentWidth()
    {
        if (_tree.Items.Count == 0)
        {
            Width = DesignMetrics.EditorHeaderChunkStripMinWidth;
            return;
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
        var chrome = BorderThickness.Left
            + BorderThickness.Right
            + Padding.Left
            + Padding.Right
            + _tree.Padding.Left
            + _tree.Padding.Right
            + DesignMetrics.From96(4);
        var needed = content + chrome;
        Width = Math.Clamp(
            needed,
            DesignMetrics.EditorHeaderChunkStripMinWidth,
            DesignMetrics.EditorHeaderChunkStripMaxWidth);
    }

    private TreeViewItem CreateItem(
        WaveStructureNode node,
        int depth,
        int nameFieldWidth,
        int sizeFieldWidth)
    {
        var brushKey = node.IsInvalid
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
}
