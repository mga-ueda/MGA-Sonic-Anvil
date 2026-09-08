using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MgaSonicAnvil.Domain;
using MediaColor = System.Windows.Media.Color;

namespace MgaSonicAnvil.UI;

/// <summary>
/// 開発者向け色調整パネル。開いたままメイン画面を見ながら変更できる。
/// アルファは XAML 既定を維持し、パネルでは RGB（#RRGGBB）のみ編集する。
/// </summary>
internal partial class ColorDevPanelWindow : Window
{
    private readonly Dictionary<string, Border> _swatches = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBox> _hexInputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBlock> _nameLabels = new(StringComparer.OrdinalIgnoreCase);
    private bool _suppressHexEvents;

    public event EventHandler? ColorsChanged;

    public ColorDevPanelWindow()
    {
        InitializeComponent();
        Title = UiStrings.ColorDevTitle;
        SourceInitialized += (_, _) => DarkWindowChrome.ApplyImmersiveDarkTitleBar(this);
        BuildRows();
        RefreshRows();
    }

    public void ApplyLocalizedText()
    {
        Title = UiStrings.ColorDevTitle;
        ResetButton.Content = UiStrings.ColorDevResetToDefaults;
        CloseButton.Content = UiStrings.ColorDevClose;
        foreach (var entry in UiColors.Entries)
        {
            if (_nameLabels.TryGetValue(entry.Key, out var label))
            {
                label.Text = entry.Label;
            }
        }
    }

    private void BuildRows()
    {
        ListPanel.Children.Clear();
        _swatches.Clear();
        _hexInputs.Clear();
        _nameLabels.Clear();

        var hexWidth = MeasureHexEditorWidth(12);

        foreach (var entry in UiColors.Entries)
        {
            var row = new Grid { Height = 34, Margin = new Thickness(0, 0, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto,
                SharedSizeGroup = "ColorDevName",
            });
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto,
                SharedSizeGroup = "ColorDevSwatch",
            });
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto,
                SharedSizeGroup = "ColorDevHex",
            });

            var nameLabel = new TextBlock
            {
                Text = entry.Label,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("PrimaryForeBrush"),
            };
            _nameLabels[entry.Key] = nameLabel;

            var swatch = new Border
            {
                Width = 40,
                Height = 22,
                Margin = new Thickness(0, 4, 4, 4),
                VerticalAlignment = VerticalAlignment.Center,
                BorderBrush = (Brush)FindResource("ChromeBorderBrush"),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Tag = entry.Key,
            };
            swatch.MouseLeftButtonUp += (_, _) => PickColor(entry.Key);

            var hex = new TextBox
            {
                Width = hexWidth,
                MinWidth = hexWidth,
                MaxLength = 7,
                Margin = new Thickness(0, 2, 0, 2),
                Padding = new Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Background = (Brush)FindResource("ColorPanelInputBackBrush"),
                Foreground = (Brush)FindResource("PrimaryForeBrush"),
                BorderBrush = (Brush)FindResource("ChromeBorderBrush"),
                BorderThickness = new Thickness(1),
                Tag = entry.Key,
            };
            hex.LostFocus += Hex_LostFocus;
            hex.KeyDown += Hex_KeyDown;

            row.Children.Add(nameLabel);
            row.Children.Add(swatch);
            row.Children.Add(hex);
            Grid.SetColumn(swatch, 1);
            Grid.SetColumn(hex, 2);

            _swatches[entry.Key] = swatch;
            _hexInputs[entry.Key] = hex;
            ListPanel.Children.Add(row);
        }
    }

    private double MeasureHexEditorWidth(double fontSize)
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (dpi < 0.01)
        {
            dpi = 1d;
        }

        var formatted = new FormattedText(
            "#RRGGBB",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            WpfControlHelpers.MonoTypeface,
            fontSize,
            Brushes.Black,
            dpi);
        var textWidth = Math.Max(formatted.WidthIncludingTrailingWhitespace, fontSize * 0.7 * 7);
        return Math.Ceiling(textWidth) + 6 + 6 + 2 + 10;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        UiColors.ResetToDefaults();
        ApplyColorChange();
    }

    public void RefreshRows()
    {
        var offset = Scroll.VerticalOffset;
        RefreshRowsCore();
        Scroll.ScrollToVerticalOffset(offset);
    }

    private void Hex_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox hex || hex.Tag is not string key)
        {
            return;
        }

        e.Handled = true;
        ApplyHexText(key, hex.Text);
    }

    private void Hex_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressHexEvents || sender is not TextBox hex || hex.Tag is not string key)
        {
            return;
        }

        ApplyHexText(key, hex.Text);
    }

    private void ApplyHexText(string key, string text)
    {
        var entry = FindEntry(key);
        if (entry is null)
        {
            return;
        }

        var current = entry.Get();
        var expected = UiColors.FormatColor(current);
        if (string.Equals(text.Trim(), expected, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!UiColors.TryParseColor(text, out var parsed))
        {
            _suppressHexEvents = true;
            try
            {
                if (_hexInputs.TryGetValue(key, out var hex))
                {
                    hex.Text = expected;
                }
            }
            finally
            {
                _suppressHexEvents = false;
            }

            return;
        }

        var alpha = UiColors.GetDefaultAlpha(key);
        entry.Set(MediaColor.FromArgb(alpha, parsed.R, parsed.G, parsed.B));
        ApplyColorChange();
    }

    private void PickColor(string key)
    {
        var entry = FindEntry(key);
        if (entry is null)
        {
            return;
        }

        var current = entry.Get();
        var picker = new WpfRgbColorPickerWindow(Owner, MediaColor.FromRgb(current.R, current.G, current.B));
        if (picker.ShowDialog() != true)
        {
            return;
        }

        var alpha = UiColors.GetDefaultAlpha(key);
        entry.Set(MediaColor.FromArgb(alpha, picker.SelectedColor.R, picker.SelectedColor.G, picker.SelectedColor.B));
        ApplyColorChange();
    }

    private void ApplyColorChange()
    {
        var offset = Scroll.VerticalOffset;
        RefreshRowsCore();
        UiColors.Save();
        ColorsChanged?.Invoke(this, EventArgs.Empty);
        Dispatcher.BeginInvoke(
            () => Scroll.ScrollToVerticalOffset(offset),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static UiColorEntry? FindEntry(string key) =>
        UiColors.Entries.FirstOrDefault(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase));

    private void RefreshRowsCore()
    {
        _suppressHexEvents = true;
        try
        {
            foreach (var entry in UiColors.Entries)
            {
                var color = entry.Get();
                if (_swatches.TryGetValue(entry.Key, out var swatch))
                {
                    swatch.Background = UiColors.Brush(MediaColor.FromRgb(color.R, color.G, color.B));
                }

                if (_hexInputs.TryGetValue(entry.Key, out var hex))
                {
                    hex.Text = UiColors.FormatColor(color);
                }
            }
        }
        finally
        {
            _suppressHexEvents = false;
        }
    }

    private sealed class WpfRgbColorPickerWindow : Window
    {
        private readonly TextBox _rBox;
        private readonly TextBox _gBox;
        private readonly TextBox _bBox;
        private readonly Border _preview;

        public MediaColor SelectedColor { get; private set; }

        public WpfRgbColorPickerWindow(Window? owner, MediaColor initial)
        {
            Owner = owner;
            WindowStartupLocation = owner is null
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.CenterOwner;
            Title = UiStrings.ColorDevTitle;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStyle = WindowStyle.ToolWindow;
            Background = (Brush)Application.Current.FindResource("ColorPanelBackBrush");
            Foreground = (Brush)Application.Current.FindResource("PrimaryForeBrush");
            SourceInitialized += (_, _) => DarkWindowChrome.ApplyImmersiveDarkTitleBar(this);
            SelectedColor = initial;

            var root = new StackPanel { Margin = new Thickness(16), MinWidth = 240 };
            _preview = new Border
            {
                Height = 36,
                Margin = new Thickness(0, 0, 0, 12),
                BorderBrush = (Brush)Application.Current.FindResource("ChromeBorderBrush"),
                BorderThickness = new Thickness(1),
                Background = UiColors.Brush(initial),
            };

            _rBox = CreateChannelBox(initial.R);
            _gBox = CreateChannelBox(initial.G);
            _bBox = CreateChannelBox(initial.B);
            root.Children.Add(_preview);
            root.Children.Add(CreateChannelRow("R", _rBox));
            root.Children.Add(CreateChannelRow("G", _gBox));
            root.Children.Add(CreateChannelRow("B", _bBox));

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0),
            };
            var ok = new Button { Content = UiStrings.ButtonOk, Width = 72, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancel = new Button { Content = UiStrings.ButtonCancel, Width = 72, IsCancel = true };
            ok.Click += (_, _) =>
            {
                if (TryReadColor(out var color))
                {
                    SelectedColor = color;
                    DialogResult = true;
                    Close();
                }
            };
            cancel.Click += (_, _) =>
            {
                DialogResult = false;
                Close();
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            Content = root;

            _rBox.TextChanged += (_, _) => UpdatePreviewFromFields();
            _gBox.TextChanged += (_, _) => UpdatePreviewFromFields();
            _bBox.TextChanged += (_, _) => UpdatePreviewFromFields();
        }

        private static TextBox CreateChannelBox(byte value) => new()
        {
            Width = 56,
            Height = 28,
            MaxLength = 3,
            Padding = new Thickness(4, 1, 4, 1),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Text = value.ToString(CultureInfo.InvariantCulture),
        };

        private static StackPanel CreateChannelRow(string label, TextBox box)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            row.Children.Add(new TextBlock
            {
                Text = label,
                Width = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            });
            row.Children.Add(box);
            return row;
        }

        private void UpdatePreviewFromFields()
        {
            if (TryReadColor(out var color))
            {
                _preview.Background = UiColors.Brush(color);
            }
        }

        private bool TryReadColor(out MediaColor color)
        {
            color = default;
            if (!byte.TryParse(_rBox.Text, out var r)
                || !byte.TryParse(_gBox.Text, out var g)
                || !byte.TryParse(_bBox.Text, out var b))
            {
                return false;
            }

            color = MediaColor.FromRgb(r, g, b);
            return true;
        }
    }
}
