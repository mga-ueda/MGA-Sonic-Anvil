using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>全タブのファイル名と時間。セル範囲コピーと CSV / PDF 書き出し。</summary>
internal sealed class TabTimeTableWindow : Window
{
    private readonly DataGrid _grid = new();
    private readonly DataGridTextColumn _fileColumn = new();
    private readonly DataGridTextColumn _timeColumn = new();
    private readonly RoundedButton _copyButton = new();
    private readonly RoundedButton _csvButton = new();
    private readonly RoundedButton _pdfButton = new();
    private readonly RoundedButton _closeButton = new();
    private readonly MenuItem _copyMenuItem = new();
    private IReadOnlyList<TabTimeRow> _rows = [];

    public TabTimeTableWindow()
    {
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = DesignMetrics.From96(640);
        Height = DesignMetrics.From96(440);
        MinWidth = DesignMetrics.From96(520);
        MinHeight = DesignMetrics.From96(320);
        SetResourceReference(BackgroundProperty, "WindowBackBrush");
        SetResourceReference(ForegroundProperty, "PrimaryForeBrush");
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;

        ConfigureGrid();
        ConfigureButtons();
        Content = BuildRoot();

        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, OnCopy, CanCopy));
        PreviewKeyDown += OnPreviewCopy;
        WindowPaintReveal.Attach(this, activateOnReveal: true);
        AppDialogKeys.PrepareActionButton(_copyButton, isDefault: true);
        AppDialogKeys.PrepareActionButton(_csvButton);
        AppDialogKeys.PrepareActionButton(_pdfButton);
        AppDialogKeys.PrepareActionButton(_closeButton, isCancel: true);
        AppDialogKeys.Attach(this, Close);
        ApplyLocalizedText();
        ApplyButtonLooks();
        ApplyGridStyles();
    }

    public void SetRows(IReadOnlyList<TabTimeRow> rows)
    {
        _rows = rows ?? [];
        _grid.ItemsSource = _rows;
        _grid.SelectedCells.Clear();
    }

    public void ApplyLocalizedText()
    {
        Title = UiStrings.TabTimeWindowTitle;
        _fileColumn.Header = UiStrings.TabTimeColumnFile;
        _timeColumn.Header = UiStrings.TabTimeColumnTime;
        _copyButton.Content = UiStrings.TabTimeCopy;
        _csvButton.Content = UiStrings.TabTimeSaveCsv;
        _pdfButton.Content = UiStrings.TabTimeSavePdf;
        _closeButton.Content = UiStrings.ButtonClose;
        _copyMenuItem.Header = UiStrings.MenuCopy;
        TipService.Set(_copyButton, UiStrings.TipTabTimeCopy);
        TipService.Set(_csvButton, UiStrings.TipTabTimeCsv);
        TipService.Set(_pdfButton, UiStrings.TipTabTimePdf);
        TipService.Set(_grid, UiStrings.TipTabTimeCopy);
    }

    public void RefreshAppearance()
    {
        DarkWindowChrome.ApplyImmersiveDarkTitleBar(this);
        ApplyButtonLooks();
        ApplyGridStyles();
    }

    private void ConfigureGrid()
    {
        _fileColumn.Binding = new Binding(nameof(TabTimeRow.FileName));
        _fileColumn.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
        _fileColumn.IsReadOnly = true;
        _timeColumn.Binding = new Binding(nameof(TabTimeRow.Duration));
        _timeColumn.Width = new DataGridLength(1, DataGridLengthUnitType.Auto);
        _timeColumn.MinWidth = DesignMetrics.From96(108);
        _timeColumn.IsReadOnly = true;

        _grid.AutoGenerateColumns = false;
        _grid.IsReadOnly = true;
        _grid.CanUserAddRows = false;
        _grid.CanUserDeleteRows = false;
        _grid.CanUserReorderColumns = false;
        _grid.CanUserSortColumns = false;
        _grid.CanUserResizeRows = false;
        _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        _grid.GridLinesVisibility = DataGridGridLinesVisibility.All;
        _grid.SelectionUnit = DataGridSelectionUnit.Cell;
        _grid.SelectionMode = DataGridSelectionMode.Extended;
        _grid.ClipboardCopyMode = DataGridClipboardCopyMode.None;
        _grid.EnableRowVirtualization = false;
        _grid.EnableColumnVirtualization = false;
        _grid.RowHeaderWidth = 0;
        _grid.MinRowHeight = 28;
        _grid.BorderThickness = new Thickness(1);
        _grid.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _grid.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _grid.Columns.Add(_fileColumn);
        _grid.Columns.Add(_timeColumn);
        _grid.SetResourceReference(Control.BorderBrushProperty, "ChromeBorderBrush");
        _grid.SetResourceReference(Control.BackgroundProperty, "SurfaceBackBrush");
        _grid.SetResourceReference(Control.ForegroundProperty, "PrimaryForeBrush");
        _grid.SetResourceReference(DataGrid.HorizontalGridLinesBrushProperty, "ChromeBorderBrush");
        _grid.SetResourceReference(DataGrid.VerticalGridLinesBrushProperty, "ChromeBorderBrush");

        var timeText = new Style(typeof(TextBlock));
        timeText.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
        timeText.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(8, 0, 8, 0)));
        _timeColumn.ElementStyle = timeText;

        var fileText = new Style(typeof(TextBlock));
        fileText.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        fileText.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(8, 0, 8, 0)));
        _fileColumn.ElementStyle = fileText;

        _copyMenuItem.Command = ApplicationCommands.Copy;
        _copyMenuItem.CommandTarget = _grid;
        _grid.ContextMenu = new ContextMenu { Items = { _copyMenuItem } };
        _grid.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, OnCopy, CanCopy));
    }

    private void ConfigureButtons()
    {
        StyleButton(_copyButton, first: true);
        StyleButton(_csvButton);
        StyleButton(_pdfButton);
        StyleButton(_closeButton);
        _copyButton.Click += (_, _) => CopySelection();
        _csvButton.Click += (_, _) => SaveCsv();
        _pdfButton.Click += (_, _) => SavePdf();
        _closeButton.Click += (_, _) => Close();
    }

    private Border BuildRoot()
    {
        var buttons = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 16, 0, 0) };
        DockPanel.SetDock(_closeButton, Dock.Right);
        buttons.Children.Add(_closeButton);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        actions.Children.Add(_copyButton);
        actions.Children.Add(_csvButton);
        actions.Children.Add(_pdfButton);
        buttons.Children.Add(actions);

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(_grid);
        return new Border { Padding = DesignMetrics.AudioPad, Child = root };
    }

    private void CanCopy(object sender, CanExecuteRoutedEventArgs e)
    {
        e.CanExecute = _rows.Count > 0;
        e.Handled = true;
    }

    private void OnCopy(object sender, ExecutedRoutedEventArgs e)
    {
        CopySelection();
        e.Handled = true;
    }

    private void OnPreviewCopy(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key != Key.C || Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        CopySelection();
        e.Handled = true;
    }

    private void CopySelection()
    {
        var text = TabTimeList.FormatCopy(_rows, ReadSelection());
        if (text.Length == 0)
        {
            return;
        }

        SystemClipboard.TrySetText(text, this);
    }

    internal TabTimeSelection? ReadSelection()
    {
        if (_grid.SelectedCells.Count == 0)
        {
            return null;
        }

        var rowStart = int.MaxValue;
        var rowEnd = int.MinValue;
        var colStart = int.MaxValue;
        var colEnd = int.MinValue;
        foreach (var cell in _grid.SelectedCells)
        {
            if (cell.Item is not TabTimeRow row)
            {
                continue;
            }

            var column = ReferenceEquals(cell.Column, _timeColumn) ? 1 : 0;
            rowStart = Math.Min(rowStart, row.Index);
            rowEnd = Math.Max(rowEnd, row.Index);
            colStart = Math.Min(colStart, column);
            colEnd = Math.Max(colEnd, column);
        }

        return rowStart == int.MaxValue
            ? null
            : new TabTimeSelection(rowStart, rowEnd, colStart, colEnd);
    }

    private void SaveCsv()
    {
        var dialog = new SaveFileDialog
        {
            Filter = UiStrings.FilterTabTimeCsv,
            Title = UiStrings.TabTimeSaveCsvTitle,
            FileName = UiStrings.TabTimeFileNameCsv,
            AddExtension = true,
            DefaultExt = ".csv",
        };
        SuggestExportDirectory(dialog);
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            using var stream = File.Create(dialog.FileName);
            TabTimeList.WriteCsv(stream, _rows);
            RememberExportFolder(Path.GetDirectoryName(dialog.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowSaveFailed(ex);
        }
    }

    private void SavePdf()
    {
        var dialog = new SaveFileDialog
        {
            Filter = UiStrings.FilterTabTimePdf,
            Title = UiStrings.TabTimeSavePdfTitle,
            FileName = UiStrings.TabTimeFileNamePdf,
            AddExtension = true,
            DefaultExt = ".pdf",
        };
        SuggestExportDirectory(dialog);
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            using var stream = File.Create(dialog.FileName);
            TabTimePdf.Write(
                stream,
                _rows,
                UiStrings.TabTimeWindowTitle,
                UiStrings.TabTimeColumnFile,
                UiStrings.TabTimeColumnTime);
            RememberExportFolder(Path.GetDirectoryName(dialog.FileName));
        }
        catch (Exception ex)
        {
            ShowSaveFailed(ex);
        }
    }

    private void ShowSaveFailed(Exception ex)
    {
        OwnerCenteredMessageBox.Show(
            this,
            $"{UiStrings.ErrorSaveFailed}\n{ex.Message}",
            UiStrings.AppName,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private static void SuggestExportDirectory(SaveFileDialog dialog)
    {
        var folder = ExportFolderMemory.Resolve(AppStorage.Settings.LastExportFolder, null);
        if (folder.Length > 0)
        {
            dialog.InitialDirectory = folder;
        }
    }

    private static void RememberExportFolder(string? folder)
    {
        if (!ExportFolderMemory.TryNormalize(folder, out var path))
        {
            return;
        }

        AppStorage.Settings.LastExportFolder = path;
        AppStorage.Save();
    }

    private void ApplyButtonLooks()
    {
        ActionButtonLooks.ApplyAccent(_copyButton);
        ActionButtonLooks.ApplyClear(_csvButton);
        ActionButtonLooks.ApplyClear(_pdfButton);
        ActionButtonLooks.ApplyClear(_closeButton);
    }

    private void ApplyGridStyles()
    {
        var accent = Theme.Get("AccentCyanBrush");
        var selected = new SolidColorBrush(Color.FromArgb(80, accent.R, accent.G, accent.B));
        selected.Freeze();

        var headerStyle = new Style(typeof(DataGridColumnHeader));
        headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("ColorPanelBackBrush")));
        headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));
        headerStyle.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("ChromeBorderBrush")));
        headerStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
        headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
        headerStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        headerStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        _grid.ColumnHeaderStyle = headerStyle;

        var cellStyle = new Style(typeof(DataGridCell));
        cellStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        cellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        cellStyle.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        cellStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));
        var selectedTrigger = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, selected));
        selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));
        cellStyle.Triggers.Add(selectedTrigger);
        _grid.CellStyle = cellStyle;

        var rowStyle = new Style(typeof(DataGridRow));
        rowStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        rowStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));
        var rowSelected = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
        rowSelected.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        rowStyle.Triggers.Add(rowSelected);
        _grid.RowStyle = rowStyle;
        _grid.RowBackground = Brushes.Transparent;
        _grid.AlternatingRowBackground = Brushes.Transparent;
        _grid.AlternationCount = 0;
    }

    private static void StyleButton(RoundedButton button, bool first = false)
    {
        button.MinWidth = DesignMetrics.ConfirmSaveButtonWidth;
        button.Height = DesignMetrics.AudioDialogButtonHeight;
        button.Margin = new Thickness(first ? 0 : 8, 0, 0, 0);
    }
}
