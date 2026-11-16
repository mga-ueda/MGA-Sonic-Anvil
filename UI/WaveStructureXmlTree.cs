using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Xml.Linq;

namespace MgaSonicAnvil.UI;

/// <summary>
/// プレイヤー情報列と同じ XML 色分けツリー。エディタの波形右詳細でも共有する。
/// </summary>
internal static class WaveStructureXmlTree
{
    private static readonly FontFamily MonoFont = new("Consolas");

    // プレイヤー情報列と同じブラシ対応。
    private const string XmlTagBrushKey = "AccentCyanBrush";
    private const string XmlPunctBrushKey = "MutedForeBrush";
    private const string XmlAttrNameBrushKey = "LoudnessCautionBrush";
    private const string XmlAttrValueBrushKey = "PlayerAbInBrush";
    private const string XmlTextBrushKey = "PrimaryForeBrush";
    private const string XmlCommentBrushKey = "CrossfadePreBrush";
    private const string XmlPiBrushKey = "SampleLoopTimelineBrush";

    private static double ChildIndent => DesignMetrics.From96(28);

    private static double FontSize => LibraryBrowserView.LibraryListFontSize;

    private static double ExpanderSize => FontSize + 1;

    /// <summary>XML を色分けツリーへ。失敗時は false（呼び出し側は平文へ）。</summary>
    public static bool TryPopulate(TreeView tree, string xml)
    {
        ArgumentNullException.ThrowIfNull(tree);
        try
        {
            var document = XDocument.Parse(xml, LoadOptions.None);
            tree.Items.Clear();

            if (document.Declaration is not null)
            {
                var declData = $"version=\"{document.Declaration.Version}\""
                    + (document.Declaration.Encoding is { Length: > 0 } enc
                        ? $" encoding=\"{enc}\""
                        : string.Empty);
                tree.Items.Add(CreatePiLeafItem(tree, "xml", declData, depth: 0));
            }

            foreach (var node in document.Nodes())
            {
                if (CreateTreeItem(tree, node, depth: 0) is { } item)
                {
                    tree.Items.Add(item);
                }
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void EnsureTreeChrome(TreeView tree)
    {
        if (tree.Tag as string == "WaveStructureXmlTree")
        {
            return;
        }

        tree.Tag = "WaveStructureXmlTree";
        tree.Background = Brushes.Transparent;
        tree.BorderThickness = new Thickness(0);
        // プレイヤー情報列の XML ツリーと同じガター。
        tree.Padding = new Thickness(4, 4, 4, 6);
        tree.FontFamily = MonoFont;
        tree.FontSize = FontSize;
        tree.FontWeight = FontWeights.Normal;
        ScrollViewer.SetHorizontalScrollBarVisibility(tree, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(tree, ScrollBarVisibility.Auto);
        tree.SetResourceReference(Control.ForegroundProperty, "PrimaryForeBrush");
        VirtualizingPanel.SetIsVirtualizing(tree, false);
        TextOptions.SetTextFormattingMode(tree, TextFormattingMode.Display);

        var hover = LibraryBrowserView.GrayHoverBrush();
        var selected = LibraryBrowserView.CyanSelectionBrush();
        var fore = tree.TryFindResource("PrimaryForeBrush") as Brush ?? Brushes.WhiteSmoke;
        LibraryBrowserView.ApplyNavSelectionResources(tree, selected, fore);

        var itemStyle = new Style(typeof(TreeViewItem));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2, 0, 4, 0)));
        itemStyle.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        itemStyle.Setters.Add(new Setter(UIElement.ClipToBoundsProperty, false));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        itemStyle.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
        itemStyle.Setters.Add(new Setter(Control.FontFamilyProperty, MonoFont));
        itemStyle.Setters.Add(new Setter(Control.FontSizeProperty, FontSize));
        itemStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Normal));
        itemStyle.Setters.Add(new Setter(
            Control.TemplateProperty,
            LibraryBrowserView.StructureTreeNavTemplate(
                hover,
                selected,
                childIndent: 0,
                stretchRow: true,
                expanderSize: ExpanderSize,
                expanderGlyphSize: 9)));
        tree.ItemContainerStyle = itemStyle;
        tree.Resources[typeof(TreeViewItem)] = itemStyle;
    }

    private static TreeViewItem? CreateTreeItem(TreeView tree, XNode node, int depth) =>
        node switch
        {
            XElement element => CreateElementItem(tree, element, depth),
            XText text =>
                string.IsNullOrWhiteSpace(text.Value)
                    ? null
                    : CreateLeafItem(tree, [XmlRun(text.Value.Trim(), XmlTextBrushKey)], depth),
            XComment comment => CreateLeafItem(
                tree,
                [XmlRun($"<!-- {comment.Value.Trim()} -->", XmlCommentBrushKey)],
                depth),
            XProcessingInstruction pi => CreatePiLeafItem(tree, pi.Target, pi.Data, depth),
            _ => null,
        };

    private static TreeViewItem CreateElementItem(TreeView tree, XElement element, int depth)
    {
        var name = element.Name.LocalName;
        if (!element.HasElements && !element.HasAttributes)
        {
            var text = element.Value.Trim();
            return text.Length == 0
                ? CreateLeafItem(tree, BuildEmptyElementInlines(name), depth)
                : CreateLeafItem(tree, BuildInlineElementInlines(name, text), depth);
        }

        var item = CreateLeafItem(tree, BuildOpenTagInlines(name), depth);
        var childDepth = depth + 1;
        foreach (var attr in element.Attributes())
        {
            item.Items.Add(
                CreateLeafItem(tree, BuildAttributeInlines(attr.Name.LocalName, attr.Value), childDepth));
        }

        foreach (var child in element.Nodes())
        {
            if (child is XText && element.HasElements)
            {
                var value = ((XText)child).Value.Trim();
                if (value.Length == 0)
                {
                    continue;
                }
            }

            if (CreateTreeItem(tree, child, childDepth) is { } childItem)
            {
                item.Items.Add(childItem);
            }
        }

        item.IsExpanded = true;
        return item;
    }

    private static TreeViewItem CreatePiLeafItem(TreeView tree, string target, string data, int depth) =>
        CreateLeafItem(
            tree,
            [
                XmlRun("<?", XmlPunctBrushKey),
                XmlRun(target, XmlPiBrushKey),
                XmlRun(string.IsNullOrEmpty(data) ? string.Empty : " " + data, XmlPiBrushKey),
                XmlRun("?>", XmlPunctBrushKey),
            ],
            depth);

    private static Inline[] BuildOpenTagInlines(string name) =>
    [
        XmlRun("<", XmlPunctBrushKey),
        XmlRun(name, XmlTagBrushKey),
        XmlRun(">", XmlPunctBrushKey),
    ];

    private static Inline[] BuildEmptyElementInlines(string name) =>
    [
        XmlRun("<", XmlPunctBrushKey),
        XmlRun(name, XmlTagBrushKey),
        XmlRun("/>", XmlPunctBrushKey),
    ];

    private static Inline[] BuildInlineElementInlines(string name, string text) =>
    [
        XmlRun("<", XmlPunctBrushKey),
        XmlRun(name, XmlTagBrushKey),
        XmlRun(">", XmlPunctBrushKey),
        XmlRun(text, XmlTextBrushKey),
        XmlRun("</", XmlPunctBrushKey),
        XmlRun(name, XmlTagBrushKey),
        XmlRun(">", XmlPunctBrushKey),
    ];

    private static Inline[] BuildAttributeInlines(string name, string value) =>
    [
        XmlRun(name, XmlAttrNameBrushKey),
        XmlRun("=", XmlPunctBrushKey),
        XmlRun("\"", XmlPunctBrushKey),
        XmlRun(value, XmlAttrValueBrushKey),
        XmlRun("\"", XmlPunctBrushKey),
    ];

    private static Run XmlRun(string text, string brushKey)
    {
        var run = new Run(text);
        run.SetResourceReference(TextElement.ForegroundProperty, brushKey);
        return run;
    }

    private static TreeViewItem CreateLeafItem(TreeView tree, IEnumerable<Inline> inlines, int depth)
    {
        var item = new TreeViewItem();
        if (tree.ItemContainerStyle is { } style)
        {
            item.Style = style;
            item.ItemContainerStyle = style;
        }

        var header = new TextBlock
        {
            FontFamily = MonoFont,
            FontSize = FontSize,
            FontWeight = FontWeights.Normal,
            FontStyle = FontStyles.Normal,
            FontStretch = FontStretches.Normal,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.None,
            Margin = new Thickness(0),
            Padding = new Thickness(0),
        };
        foreach (var inline in inlines)
        {
            header.Inlines.Add(inline);
        }

        TextOptions.SetTextFormattingMode(header, TextFormattingMode.Display);
        TextBlock.SetLineStackingStrategy(header, LineStackingStrategy.BlockLineHeight);
        TextBlock.SetLineHeight(header, FontSize);
        item.Header = header;
        item.FontWeight = FontWeights.Normal;
        item.IsExpanded = false;
        item.Padding = new Thickness(2 + (depth * ChildIndent), 0, 4, 0);
        return item;
    }
}
