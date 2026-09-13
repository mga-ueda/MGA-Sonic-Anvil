using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>
/// フッタ権利表記。本文はミュート色、GitHub / MIT License / LAME だけリンク。
/// </summary>
internal sealed class BrandLicenseLabel : TextBlock
{
    public static readonly DependencyProperty LinkTextProperty =
        DependencyProperty.Register(
            nameof(LinkText),
            typeof(string),
            typeof(BrandLicenseLabel),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.AffectsMeasure,
                OnContentChanged));

    public static readonly RoutedEvent LinkClickEvent =
        EventManager.RegisterRoutedEvent(
            nameof(LinkClick),
            RoutingStrategy.Bubble,
            typeof(EventHandler<BrandLicenseLinkClickEventArgs>),
            typeof(BrandLicenseLabel));

    private readonly List<Hyperlink> _links = [];
    private Hyperlink? _hoveredLink;
    private static Style? _linkStyle;

    public BrandLicenseLabel()
    {
        TextWrapping = TextWrapping.NoWrap;
        FontSize = DesignMetrics.BrandLicenseFontSize;
        Padding = new Thickness(0);
        Cursor = Cursors.Arrow;
        Focusable = false;
        Loaded += (_, _) => RebuildContent();
    }

    public event EventHandler<BrandLicenseLinkClickEventArgs> LinkClick
    {
        add => AddHandler(LinkClickEvent, value);
        remove => RemoveHandler(LinkClickEvent, value);
    }

    public string LinkText
    {
        get => (string)GetValue(LinkTextProperty);
        set => SetValue(LinkTextProperty, value);
    }

    public void ApplyColors() => RebuildContent();

    private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BrandLicenseLabel label)
        {
            label.RebuildContent();
        }
    }

    private void ApplyLineMetrics()
    {
        var spacing = FontFamily?.LineSpacing > 0 ? FontFamily.LineSpacing : 1.2;
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        LineHeight = Math.Max(1, FontSize * spacing);
    }

    private void RebuildContent()
    {
        Inlines.Clear();
        _links.Clear();
        _hoveredLink = null;
        ApplyLineMetrics();

        var text = LinkText;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var bodyBrush = ResolveBrush("ActionCopyrightForeBrush") ?? Foreground;
        Foreground = bodyBrush;
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                Inlines.Add(new LineBreak());
            }

            AppendLineWithLinks(lines[i], bodyBrush);
        }

        UpdateLinkBrushes();
    }

    private void AppendLineWithLinks(string line, Brush bodyBrush)
    {
        if (string.IsNullOrEmpty(line))
        {
            Inlines.Add(new Run(" ") { Foreground = bodyBrush });
            return;
        }

        var remaining = line;
        while (remaining.Length > 0)
        {
            if (!TryNextLink(remaining, out var at, out var id, out var length))
            {
                Inlines.Add(new Run(remaining) { Foreground = bodyBrush });
                break;
            }

            if (at > 0)
            {
                Inlines.Add(new Run(remaining[..at]) { Foreground = bodyBrush });
            }

            var link = new Hyperlink(new Run(remaining.Substring(at, length)))
            {
                Style = LinkStyle(),
                TextDecorations = null,
                Cursor = Cursors.Hand,
                Focusable = false,
                Tag = id,
            };
            var capturedId = id;
            link.Click += (_, _) =>
                RaiseEvent(new BrandLicenseLinkClickEventArgs(LinkClickEvent, this, capturedId));
            link.MouseEnter += (_, _) =>
            {
                _hoveredLink = link;
                UpdateLinkBrushes();
            };
            link.MouseLeave += (_, _) =>
            {
                if (ReferenceEquals(_hoveredLink, link))
                {
                    _hoveredLink = null;
                }

                UpdateLinkBrushes();
            };
            _links.Add(link);
            Inlines.Add(link);
            remaining = remaining[(at + length)..];
        }
    }

    private void UpdateLinkBrushes()
    {
        var fallback = Foreground;
        var normal = ResolveBrush("ActionLinkForeBrush");
        var hover = ResolveBrush("ActionLinkHoverForeBrush") ?? ResolveBrush("AccentCyanBrush");
        foreach (var link in _links)
        {
            link.Foreground = ForeForLink(
                ReferenceEquals(link, _hoveredLink),
                normal,
                hover,
                fallback);
        }
    }

    internal static Brush ForeForLink(bool hovered, Brush? normal, Brush? hover, Brush fallback)
    {
        var rest = normal ?? fallback;
        return hovered ? hover ?? rest : rest;
    }

    private static Style LinkStyle()
    {
        if (_linkStyle is not null)
        {
            return _linkStyle;
        }

        var style = new Style(typeof(Hyperlink));
        style.Setters.Add(new Setter(Inline.TextDecorationsProperty, null));
        style.Seal();
        _linkStyle = style;
        return style;
    }

    private static bool TryNextLink(string remaining, out int at, out string id, out int length)
    {
        at = -1;
        id = string.Empty;
        length = 0;
        Consider(remaining, UiStrings.CopyrightGitHub, "github", ref at, ref id, ref length);
        Consider(remaining, UiStrings.CopyrightMitLink, "mit", ref at, ref id, ref length);
        Consider(remaining, UiStrings.CopyrightLameLink, "lame", ref at, ref id, ref length);
        return at >= 0;
    }

    private static void Consider(
        string remaining,
        string token,
        string tokenId,
        ref int at,
        ref string id,
        ref int length)
    {
        var found = remaining.IndexOf(token, StringComparison.Ordinal);
        if (found < 0 || (at >= 0 && found > at))
        {
            return;
        }

        at = found;
        id = tokenId;
        length = token.Length;
    }

    private Brush? ResolveBrush(string key) => TryFindResource(key) as Brush;
}

internal sealed class BrandLicenseLinkClickEventArgs : RoutedEventArgs
{
    public BrandLicenseLinkClickEventArgs(RoutedEvent routedEvent, object source, string linkId)
        : base(routedEvent, source)
    {
        LinkId = linkId;
    }

    public string LinkId { get; }
}
