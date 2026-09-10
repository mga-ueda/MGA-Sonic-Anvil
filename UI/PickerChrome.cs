using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MgaSonicAnvil.UI;

/// <summary>S / B / C / I / O / V / P / T のポップアップ。幅と行組みだけ共通にする。</summary>
internal static class PickerChrome
{
    private static readonly DependencyPropertyKey? IsHighlightedKey =
        typeof(MenuItem)
            .GetField("IsHighlightedPropertyKey", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetValue(null) as DependencyPropertyKey;

    private static readonly DependencyPropertyDescriptor? IsHighlightedDescriptor =
        DependencyPropertyDescriptor.FromProperty(MenuItem.IsHighlightedProperty, typeof(MenuItem));

    public const double PanelWidth = 248;
    public const double LabelWidth = 72;
    public const double BoxMinWidth = 96;
    public const int CustomRateChars = 7;

    public static string Numbered(int index, string label) => $"{index}  {label}";

    public static StackPanel Panel(double? width = null)
    {
        var root = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
        if (width is > 0)
        {
            root.Width = width.Value;
        }

        KeyboardNavigation.SetTabNavigation(root, KeyboardNavigationMode.Cycle);
        return root;
    }

    public static TextBlock Title(string text) =>
        new()
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        };

    public static TextBlock Caption(string text) =>
        new()
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.None,
            TextWrapping = TextWrapping.NoWrap,
        };

    public static TextBlock Unit(string text) =>
        new()
        {
            Text = text,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

    public static StackPanel TitleField(string caption, TextBox box, string unit)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 4),
        };
        row.Children.Add(new TextBlock
        {
            Text = caption,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        row.Children.Add(box);
        row.Children.Add(Unit(unit));
        return row;
    }

    public static TextBox ValueBox(double width = BoxMinWidth)
    {
        var box = new TextBox
        {
            Width = width,
            MinWidth = width,
            MaxWidth = width,
            TextAlignment = TextAlignment.Right,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Consolas"),
            MinHeight = 22,
            IsTabStop = true,
        };
        TryStyle(box, "DarkTextBoxStyle");
        return box;
    }

    public static TextBox ValueBoxChars(int chars) => ValueBox(CharBoxWidth(chars));

    public static double CharBoxWidth(int chars)
    {
        var text = new string('0', Math.Max(1, chars));
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            12,
            Brushes.Black,
            1.0);
        return Math.Ceiling(formatted.WidthIncludingTrailingWhitespace) + 6 + 6 + 1 + 1 + 4;
    }

    public static TextBlock MonoValue(bool emphasize = false) =>
        new()
        {
            FontFamily = new FontFamily("Consolas"),
            FontWeight = emphasize ? FontWeights.SemiBold : FontWeights.Normal,
            TextAlignment = TextAlignment.Right,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };

    public static Grid Form(params (string Label, FrameworkElement Field, string? Unit)[] rows)
    {
        var fieldWidth = 0d;
        foreach (var row in rows)
        {
            fieldWidth = Math.Max(fieldWidth, ReadWidth(row.Field));
        }

        fieldWidth = Math.Ceiling(Math.Max(fieldWidth, 1));

        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
        Grid.SetIsSharedSizeScope(grid, true);
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
            SharedSizeGroup = "PickerLabel",
        });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fieldWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        for (var i = 0; i < rows.Length; i++)
        {
            var (caption, field, unit) = rows[i];
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = Caption(caption);
            label.Margin = new Thickness(0, 0, 8, 0);
            ApplyFieldWidth(field, fieldWidth);
            field.Margin = new Thickness(0, 1, 0, 1);
            Grid.SetRow(label, i);
            Grid.SetColumn(label, 0);
            Grid.SetRow(field, i);
            Grid.SetColumn(field, 1);
            grid.Children.Add(label);
            grid.Children.Add(field);
            if (unit is not null)
            {
                var unitText = Unit(unit);
                Grid.SetRow(unitText, i);
                Grid.SetColumn(unitText, 2);
                grid.Children.Add(unitText);
            }
        }

        return grid;
    }

    public static CheckBox Option(string content)
    {
        var box = new CheckBox
        {
            Content = content,
            Focusable = true,
            IsTabStop = true,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0),
        };
        TryStyle(box, "DarkCheckBoxStyle");
        return box;
    }

    public static double LabelColumnWidth(params string[] labels)
    {
        var width = 0d;
        foreach (var label in labels)
        {
            width = Math.Max(width, MeasureUiText(label));
        }

        return Math.Ceiling(width);
    }

    public static StackPanel FieldRow(
        string caption,
        FrameworkElement field,
        string? unit = null,
        double? labelMinWidth = null)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 1, 0, 1),
        };
        var label = Caption(caption);
        label.MinWidth = Math.Ceiling(Math.Max(labelMinWidth ?? 0, MeasureUiText(caption)));
        label.Margin = new Thickness(0, 0, 8, 0);
        row.Children.Add(label);
        row.Children.Add(field);
        if (unit is not null)
        {
            row.Children.Add(Unit(unit));
        }

        return row;
    }

    public static StackPanel MetricRow(
        string caption,
        TextBlock before,
        TextBlock after,
        double? labelMinWidth = null)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 1, 0, 1),
        };
        var label = Caption(caption);
        label.Margin = new Thickness(0, 0, 8, 0);
        label.MinWidth = Math.Ceiling(Math.Max(labelMinWidth ?? 0, MeasureUiText(caption)));
        var arrow = new TextBlock
        {
            Text = "→",
            Margin = new Thickness(6, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Muted(),
        };
        row.Children.Add(label);
        row.Children.Add(before);
        row.Children.Add(arrow);
        row.Children.Add(after);
        return row;
    }

    public static MenuItem FormHost(FrameworkElement header)
    {
        header.HorizontalAlignment = HorizontalAlignment.Left;
        var item = new MenuItem
        {
            Header = header,
            Icon = null,
            StaysOpenOnClick = true,
            Focusable = false,
            MinHeight = 0,
        };
        QuietHighlight(item);
        return item;
    }

    public static void FitFormMenu(ContextMenu menu)
    {
        Grid.SetIsSharedSizeScope(menu, false);
        menu.MinWidth = 0;
        var widest = 0d;
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            item.Icon = null;
            item.Padding = new Thickness(12, 8, 12, 8);
            item.MinHeight = 0;
            item.HorizontalAlignment = HorizontalAlignment.Left;
            if (item.Header is FrameworkElement header)
            {
                header.HorizontalAlignment = HorizontalAlignment.Left;
            }

            widest = Math.Max(widest, MeasureFormRow(item));
        }

        var width = Math.Ceiling(widest) + 8;
        menu.Width = width;
        menu.MinWidth = width;
        menu.MaxWidth = width;
    }

    public static double MeasureFormRow(MenuItem item) =>
        item.Padding.Left + item.Padding.Right + MeasureHeader(item.Header) + 2;

    public static Border Gutter() =>
        new()
        {
            Width = 10,
            Height = 10,
            BorderThickness = new Thickness(1),
            BorderBrush = Brushes.Transparent,
            Background = Brushes.Transparent,
        };

    public static void FitListMenu(ContextMenu menu)
    {
        Grid.SetIsSharedSizeScope(menu, false);
        menu.MinWidth = 0;
        var widest = 0d;
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            item.Padding = new Thickness(12, 4, 12, 4);
            widest = Math.Max(widest, MeasureMenuRow(item));
        }

        var width = Math.Ceiling(widest) + 4;
        menu.Width = width;
        menu.MinWidth = width;
        menu.MaxWidth = width;
    }

    public static double MeasureMenuRow(MenuItem item)
    {
        var icon = item.Icon is null ? 0 : 22 + 8;
        return item.Padding.Left + item.Padding.Right + icon + MeasureHeader(item.Header) + 2;
    }

    public static double MeasureUiText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            WpfControlHelpers.UiTypeface,
            12,
            Brushes.Black,
            1.0);
        return formatted.WidthIncludingTrailingWhitespace
            + Math.Max(0, -formatted.OverhangLeading)
            + Math.Max(0, formatted.OverhangAfter);
    }

    private static double MeasureHeader(object? header) => header switch
    {
        string text => MeasureUiText(text),
        TextBlock block => MeasureSizedText(block.Text, block.Width, block.MinWidth),
        TextBox box => AssignedWidth(box.Width, box.MinWidth),
        CheckBox box => MeasureCheckBox(box),
        Grid grid => MeasureGrid(grid),
        StackPanel { Orientation: Orientation.Vertical } stack => MaxChildWidth(stack),
        Panel panel => SumChildWidth(panel),
        FrameworkElement element => AssignedWidth(element.Width, element.MinWidth),
        _ => 0,
    };

    private static double MeasureSizedText(string? text, double width, double minWidth) =>
        Math.Max(MeasureUiText(text ?? string.Empty), AssignedWidth(width, minWidth));

    private static double AssignedWidth(double width, double minWidth)
    {
        var assigned = 0d;
        if (!double.IsNaN(width) && width > 0)
        {
            assigned = width;
        }

        if (!double.IsNaN(minWidth) && minWidth > assigned)
        {
            assigned = minWidth;
        }

        return assigned;
    }

    private static double MeasureCheckBox(CheckBox box)
    {
        var label = box.Content as string ?? (box.Content as TextBlock)?.Text ?? string.Empty;
        var pad = box.Padding.Left + box.Padding.Right;
        if (pad <= 0)
        {
            pad = 6;
        }

        return 15 + pad + MeasureUiText(label) + box.Margin.Left + box.Margin.Right;
    }

    private static double MeasureGrid(Grid grid)
    {
        var rows = Math.Max(1, grid.RowDefinitions.Count);
        var widths = new double[rows];
        foreach (var child in grid.Children.OfType<FrameworkElement>())
        {
            var row = Math.Clamp(Grid.GetRow(child), 0, rows - 1);
            widths[row] += MeasureHeader(child) + child.Margin.Left + child.Margin.Right;
        }

        return widths.Max();
    }

    private static double MaxChildWidth(Panel panel)
    {
        var width = 0d;
        foreach (var child in panel.Children.OfType<FrameworkElement>())
        {
            width = Math.Max(width, MeasureHeader(child) + child.Margin.Left + child.Margin.Right);
        }

        return width;
    }

    private static double SumChildWidth(Panel panel)
    {
        var width = 0d;
        foreach (var child in panel.Children.OfType<FrameworkElement>())
        {
            width += MeasureHeader(child) + child.Margin.Left + child.Margin.Right;
        }

        return width;
    }

    private static double ReadWidth(FrameworkElement field) => field switch
    {
        TextBox or TextBlock when field.Width > 0 => field.Width,
        Panel panel => SumChildWidth(panel),
        _ => field.Width > 0 ? field.Width : field.MinWidth,
    };

    private static void ApplyFieldWidth(FrameworkElement field, double width)
    {
        field.Width = width;
        field.MinWidth = width;
        field.MaxWidth = width;
        field.HorizontalAlignment = HorizontalAlignment.Stretch;
        if (field is TextBlock block)
        {
            block.TextAlignment = TextAlignment.Right;
        }
    }

    private static void QuietHighlight(MenuItem item)
    {
        if (IsHighlightedKey is null || IsHighlightedDescriptor is null)
        {
            return;
        }

        IsHighlightedDescriptor.AddValueChanged(item, (_, _) =>
        {
            if (item.IsHighlighted)
            {
                item.SetValue(IsHighlightedKey, false);
            }
        });
    }

    private static Brush Muted() =>
        WpfControlHelpers.FrozenBrush(Theme.Get("MutedForeBrush"));

    private static void TryStyle(FrameworkElement element, string key)
    {
        if (Application.Current?.TryFindResource(key) is Style style)
        {
            element.Style = style;
        }
    }
}
