using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MediaColor = System.Windows.Media.Color;

namespace MgaSonicAnvil.UI;

/// <summary>
/// 色設定パネル。開いたままメイン画面を見ながら変更できる。
/// アルファは XAML 既定を維持し、パネルでは RGB（#RRGGBB）のみ編集する。
/// </summary>
internal partial class ColorDevPanelWindow : Window
{
    private readonly Dictionary<string, ColorRow> _rows = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ColorGroup> _groups = [];
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private bool _applyingOwnChange;
    private string? _selectedKey;

    public event EventHandler? ColorsChanged;

    public ColorDevPanelWindow()
    {
        InitializeComponent();
        ApplyWindowTitle();
        WindowPaintReveal.Attach(this, activateOnReveal: true);
        Closed += (_, _) =>
        {
            FlushSave();
            WindowPlacement.CaptureColorPanel(this, AppStorage.Settings);
            AppStorage.Save();
        };
        _saveTimer.Tick += (_, _) => FlushSave();
        Picker.ColorChanged += (_, _) => ApplyLive(Picker.Color);
        Picker.ColorCommitted += (_, _) => FlushSave();
        BuildRows();
        RefreshRows();
        ApplyFilter();
        SelectFirstVisible();
        ApplyButtonLooks();
        ApplyTips();
        UpdateSearchHint();
        AppDialogKeys.PrepareActionButton(ImportButton);
        AppDialogKeys.PrepareActionButton(ExportButton);
        AppDialogKeys.PrepareActionButton(ResetButton);
        AppDialogKeys.PrepareActionButton(ResetThisButton);
        AppDialogKeys.PrepareActionButton(CloseButton, isCancel: true);
        AppDialogKeys.Attach(this, Close);
    }

    /// <summary>初回は一覧とピッカーの描画後に表示する。既に出ている場合は前面へ。</summary>
    public void Present()
    {
        if (IsVisible)
        {
            Show();
            Activate();
            return;
        }

        WindowPaintReveal.ShowWhenPainted(this);
    }

    public void ApplyLocalizedText()
    {
        ApplyWindowTitle();
        SearchHint.Text = UiStrings.ColorDevSearch;
        EmptyHint.Text = UiStrings.ColorDevNoMatches;
        PickerHint.Text = UiStrings.ColorDevPickHint;
        ResetThisButton.Content = UiStrings.ColorDevResetThis;
        ImportButton.Content = UiStrings.ColorDevImport;
        ExportButton.Content = UiStrings.ColorDevExport;
        ResetButton.Content = UiStrings.ColorDevResetToDefaults;
        CloseButton.Content = UiStrings.ColorDevClose;
        Picker.ApplyLocalizedText();
        var selected = _selectedKey;
        BuildRows();
        RefreshRows();
        ApplyButtonLooks();
        ApplyTips();
        ApplyFilter();
        if (selected is not null && _rows.ContainsKey(selected))
        {
            SelectKey(selected, scrollIntoView: false);
        }
    }

    public void RefreshAppearance()
    {
        ApplyWindowTitle();
        RefreshRows();
        ApplyButtonLooks();
        Picker.RefreshChrome();
        if (!_applyingOwnChange)
        {
            SyncPickerFromSelection();
        }
    }

    private void ApplyWindowTitle() =>
        Title = UiStrings.ColorDevTitleFor(UiThemeService.Painted);

    public void RefreshRows()
    {
        var offset = Scroll.VerticalOffset;
        RefreshRowsCore();
        Scroll.ScrollToVerticalOffset(offset);
    }

    private void ApplyButtonLooks()
    {
        ActionButtonLooks.ApplyClear(ResetThisButton);
        ActionButtonLooks.ApplyClear(ImportButton);
        ActionButtonLooks.ApplyClear(ExportButton);
        ActionButtonLooks.ApplyClear(ResetButton);
        ActionButtonLooks.ApplyAccent(CloseButton);
    }

    private void ApplyTips()
    {
        TipService.Set(ResetThisButton, UiStrings.ColorDevResetThis);
        TipService.Set(ImportButton, UiStrings.ColorDevImport);
        TipService.Set(ExportButton, UiStrings.ColorDevExport);
        TipService.Set(ResetButton, UiStrings.ColorDevResetToDefaults);
        TipService.Set(CloseButton, UiStrings.ColorDevClose);
        foreach (var row in _rows.Values)
        {
            var tip = ColorDevCatalog.Caption(row.GroupTitle, row.Label);
            TipService.Set(row.Host, tip);
            TipService.Set(row.Name, tip);
            TipService.Set(row.Swatch, tip);
        }
    }

    private void BuildRows()
    {
        ListPanel.Children.Clear();
        _rows.Clear();
        _groups.Clear();

        ColorGroup? current = null;
        foreach (var entry in ColorDevCatalog.Sort(UiColors.Entries))
        {
            var groupTitle = ColorDevCatalog.GroupTitleOf(entry.Key);
            if (current is null || !string.Equals(current.Title, groupTitle, StringComparison.CurrentCulture))
            {
                var header = new TextBlock
                {
                    Text = groupTitle,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("MutedForeBrush"),
                    Margin = new Thickness(4, current is null ? 2 : 12, 4, 4),
                };
                current = new ColorGroup(groupTitle, header);
                _groups.Add(current);
                ListPanel.Children.Add(header);
            }

            var row = CreateRow(entry, groupTitle);
            current.Rows.Add(row);
            _rows[entry.Key] = row;
            ListPanel.Children.Add(row.Host);
        }
    }

    private ColorRow CreateRow(UiColorEntry entry, string groupTitle)
    {
        var host = new Border
        {
            Padding = new Thickness(4, 3, 6, 3),
            Margin = new Thickness(0, 0, 0, 1),
            CornerRadius = new CornerRadius(6),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var accent = new Border
        {
            Width = 3,
            Height = 22,
            CornerRadius = new CornerRadius(1.5),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Hidden,
        };
        var swatch = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var name = new TextBlock
        {
            Text = entry.Label,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var hex = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            Margin = new Thickness(8, 0, 0, 0),
        };
        grid.Children.Add(accent);
        grid.Children.Add(swatch);
        grid.Children.Add(name);
        grid.Children.Add(hex);
        Grid.SetColumn(swatch, 2);
        Grid.SetColumn(name, 4);
        Grid.SetColumn(hex, 5);
        host.Child = grid;

        var row = new ColorRow(entry.Key, groupTitle, host, accent, swatch, name, hex);
        host.MouseLeftButtonUp += (_, _) => SelectKey(entry.Key, scrollIntoView: false);
        host.MouseEnter += (_, _) => PaintRow(row, hover: true);
        host.MouseLeave += (_, _) => PaintRow(row, hover: false);
        return row;
    }

    private void RefreshRowsCore()
    {
        foreach (var entry in UiColors.Entries)
        {
            if (_rows.TryGetValue(entry.Key, out var row))
            {
                PaintColor(row, entry.Get());
            }
        }

        PaintSelection();
    }

    private static void PaintColor(ColorRow row, MediaColor color)
    {
        var rgb = MediaColor.FromRgb(color.R, color.G, color.B);
        row.Swatch.Background = UiColors.Brush(rgb);
        row.Swatch.BorderBrush = WpfControlHelpers.FrozenBrush(Theme.Get("ChromeBorderBrush"));
        row.Hex.Text = UiColors.FormatColor(color);
        row.Hex.Foreground = WpfControlHelpers.FrozenBrush(Theme.Get("MutedForeBrush"));
        row.Name.Foreground = WpfControlHelpers.FrozenBrush(Theme.Get("PrimaryForeBrush"));
    }

    private void PaintRow(ColorRow row, bool hover)
    {
        var selected = string.Equals(row.Key, _selectedKey, StringComparison.OrdinalIgnoreCase);
        row.Host.Background = selected
            ? WpfControlHelpers.FrozenBrush(Theme.Get("TransportHoverBackBrush"))
            : hover
                ? WpfControlHelpers.FrozenBrush(Theme.Get("TransportHoverBackBrush"))
                : Brushes.Transparent;
        row.Accent.Background = WpfControlHelpers.FrozenBrush(Theme.Get("AccentCyanBrush"));
        row.Accent.Visibility = selected ? Visibility.Visible : Visibility.Hidden;
    }

    private void PaintSelection()
    {
        foreach (var row in _rows.Values)
        {
            PaintRow(row, hover: false);
        }
    }

    private void SelectKey(string key, bool scrollIntoView)
    {
        if (!_rows.TryGetValue(key, out var row))
        {
            return;
        }

        _selectedKey = key;
        SelectedName.Text = row.Label;
        SelectedName.Visibility = Visibility.Visible;
        Picker.Visibility = Visibility.Visible;
        PickerHint.Visibility = Visibility.Collapsed;
        ResetThisButton.IsEnabled = true;
        PaintSelection();
        SyncPickerFromSelection();
        if (scrollIntoView)
        {
            row.Host.BringIntoView();
        }

        Scroll.Focus();
    }

    private void SelectFirstVisible()
    {
        var first = VisibleRows().FirstOrDefault();
        if (first is null)
        {
            _selectedKey = null;
            SelectedName.Visibility = Visibility.Collapsed;
            Picker.Visibility = Visibility.Collapsed;
            PickerHint.Visibility = Visibility.Visible;
            ResetThisButton.IsEnabled = false;
            PaintSelection();
            return;
        }

        SelectKey(first.Key, scrollIntoView: false);
    }

    private void SyncPickerFromSelection()
    {
        if (_selectedKey is null)
        {
            return;
        }

        var entry = FindEntry(_selectedKey);
        if (entry is null)
        {
            return;
        }

        var color = entry.Get();
        Picker.SetColor(MediaColor.FromRgb(color.R, color.G, color.B));
    }

    private void ApplyLive(MediaColor rgb)
    {
        if (_selectedKey is null)
        {
            return;
        }

        var entry = FindEntry(_selectedKey);
        if (entry is null)
        {
            return;
        }

        var alpha = UiColors.GetDefaultAlpha(_selectedKey);
        _applyingOwnChange = true;
        try
        {
            entry.Set(MediaColor.FromArgb(alpha, rgb.R, rgb.G, rgb.B));
            if (_rows.TryGetValue(_selectedKey, out var row))
            {
                PaintColor(row, entry.Get());
            }

            ColorsChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _applyingOwnChange = false;
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void FlushSave()
    {
        _saveTimer.Stop();
        UiColors.Save();
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text;
        var any = false;
        foreach (var group in _groups)
        {
            var visibleInGroup = false;
            foreach (var row in group.Rows)
            {
                var hex = row.Hex.Text;
                var show = ColorDevCatalog.Matches(row.Label, row.Key, hex, row.GroupTitle, query);
                row.Host.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                visibleInGroup |= show;
                any |= show;
            }

            group.Header.Visibility = visibleInGroup ? Visibility.Visible : Visibility.Collapsed;
        }

        EmptyHint.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        Scroll.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        if (_selectedKey is null || !_rows.TryGetValue(_selectedKey, out var selected)
            || selected.Host.Visibility != Visibility.Visible)
        {
            SelectFirstVisible();
        }
    }

    private IEnumerable<ColorRow> VisibleRows() =>
        _groups.SelectMany(group => group.Rows)
            .Where(row => row.Host.Visibility == Visibility.Visible);

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSearchHint();
        if (Picker is null || ListPanel is null)
        {
            return;
        }

        ApplyFilter();
    }

    private void SearchBox_FocusChanged(object sender, KeyboardFocusChangedEventArgs e) =>
        UpdateSearchHint();

    private void UpdateSearchHint()
    {
        if (SearchHint is null)
        {
            return;
        }

        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) && !SearchBox.IsKeyboardFocused
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void Scroll_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down) || SearchBox.IsKeyboardFocusWithin)
        {
            return;
        }

        var visible = VisibleRows().ToList();
        if (visible.Count == 0)
        {
            return;
        }

        var index = visible.FindIndex(row =>
            string.Equals(row.Key, _selectedKey, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            index = 0;
        }
        else if (e.Key == Key.Down)
        {
            index = Math.Min(visible.Count - 1, index + 1);
        }
        else
        {
            index = Math.Max(0, index - 1);
        }

        SelectKey(visible[index].Key, scrollIntoView: true);
        e.Handled = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        FlushSave();
        var dialog = new SaveFileDialog
        {
            Filter = UiStrings.FilterColorScheme,
            Title = UiStrings.ColorDevExportTitle,
            FileName = AppVersion.ProductName + " colors.json",
            AddExtension = true,
            DefaultExt = ".json",
        };
        SuggestColorSchemeDirectory(dialog);
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, UiColorScheme.Write(UiColors.CaptureScheme()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.ColorDevExportFailed,
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = UiStrings.FilterColorScheme,
            Title = UiStrings.ColorDevImportTitle,
            CheckFileExists = true,
        };
        SuggestColorSchemeDirectory(dialog);
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            if (!UiColorScheme.TryRead(File.ReadAllText(dialog.FileName), out var scheme))
            {
                OwnerCenteredMessageBox.Show(
                    this,
                    UiStrings.ColorDevImportFailed,
                    UiStrings.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            UiColors.ApplyScheme(scheme);
            RefreshAppearance();
            FlushSave();
            ColorsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.ColorDevImportFailed,
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static void SuggestColorSchemeDirectory(FileDialog dialog)
    {
        var lastDir = Path.GetDirectoryName(AppStorage.Settings.LastDocumentPath);
        if (!string.IsNullOrWhiteSpace(lastDir) && Directory.Exists(lastDir))
        {
            dialog.InitialDirectory = lastDir;
        }
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        UiColors.ResetToDefaults();
        RefreshRows();
        SyncPickerFromSelection();
        FlushSave();
        ColorsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ResetThisButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedKey is null)
        {
            return;
        }

        FindEntry(_selectedKey)?.Set(UiColors.DefaultFor(UiThemeService.Painted, _selectedKey));
        RefreshRows();
        SyncPickerFromSelection();
        FlushSave();
        ColorsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static UiColorEntry? FindEntry(string key) =>
        UiColors.Entries.FirstOrDefault(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase));

    private sealed class ColorGroup
    {
        public ColorGroup(string title, TextBlock header)
        {
            Title = title;
            Header = header;
        }

        public string Title { get; }

        public TextBlock Header { get; }

        public List<ColorRow> Rows { get; } = [];
    }

    private sealed class ColorRow
    {
        public ColorRow(
            string key,
            string groupTitle,
            Border host,
            Border accent,
            Border swatch,
            TextBlock name,
            TextBlock hex)
        {
            Key = key;
            GroupTitle = groupTitle;
            Host = host;
            Accent = accent;
            Swatch = swatch;
            Name = name;
            Hex = hex;
        }

        public string Key { get; }

        public string GroupTitle { get; }

        public Border Host { get; }

        public Border Accent { get; }

        public Border Swatch { get; }

        public TextBlock Name { get; }

        public TextBlock Hex { get; }

        public string Label => Name.Text;
    }
}
