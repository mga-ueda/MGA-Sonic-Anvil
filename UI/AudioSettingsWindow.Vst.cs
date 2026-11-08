using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>
/// 設定の VST タブ。一覧の取得は「スキャン」ボタンだけ（開いた時・タブ選択時には走らせない）。
/// </summary>
internal partial class AudioSettingsWindow
{
    public string[] SelectedVst3SearchPaths { get; private set; } = [];

    /// <summary>Cubase / Nuendo の vst3plugins.xml を使うか。既定オフ。</summary>
    public bool SelectedVst3UseSteinbergHostCache { get; private set; }

    private CancellationTokenSource? _vstScanCts;
    private bool _vstScanning;

    /// <summary>スキャン中にダイアログ本体（タブと OK／キャンセル）を覆うすりガラス。</summary>
    private readonly BusyGlassOverlay _busyGlass = new();

    /// <summary>いま表示している表。振り分けを変えたらその場で vst-plugins.json へ保存する（OK／キャンセルに関係なく残す）。</summary>
    private Vst3PluginListDocument _vstDocument = new();

    // const を直接 if に置くと Debug/Release の片方で CS0162 になるため、プロパティ越しに読む。
    private static bool VstTabEnabled => VstFeature.IsEnabled;

    /// <summary>コンストラクタから。機能オフ時はタブごと外す。</summary>
    private void InitializeVstTab(
        IEnumerable<string>? vst3SearchPaths,
        bool useSteinbergHostCache)
    {
        SelectedVst3SearchPaths = Vst3SearchPathList.Resolve(vst3SearchPaths);
        SelectedVst3UseSteinbergHostCache = useSteinbergHostCache;
        if (!VstTabEnabled)
        {
            // Collapsed ではなく Items から外す（CycleSettingsTab／SlidingTabAccent は SelectedIndex 基準）。
            SettingsTabs.Items.Remove(VstTab);
            return;
        }

        ConfigureVstGrids();
        ApplyVstButtons();
        ApplyVstListChrome();
        AppDialogKeys.PrepareActionButton(Vst3SearchPathAddButton);
        AppDialogKeys.PrepareActionButton(Vst3SearchPathRemoveButton);
        AppDialogKeys.PrepareActionButton(Vst3SearchPathUpButton);
        AppDialogKeys.PrepareActionButton(Vst3SearchPathDownButton);
        AppDialogKeys.PrepareActionButton(VstScanButton);
        AppDialogKeys.PrepareActionButton(VstClearButton);
        AppDialogKeys.PrepareActionButton(VstPluginAddButton);
        AppDialogKeys.PrepareActionButton(VstPluginRemoveButton);
        FillVst3SearchPaths();
        VstUseSteinbergHostCacheBox.IsChecked = SelectedVst3UseSteinbergHostCache;
        // 保存済みの表を出すだけ。スキャンはしない。
        ShowVstPluginList(Vst3PluginList.Load());
        // SettingsBody は 2 行（タブ／OK・キャンセル）なので両方を覆う。
        Grid.SetRowSpan(_busyGlass, SettingsBody.RowDefinitions.Count);
        SettingsBody.SizeChanged += (_, _) =>
        {
            if (_busyGlass.IsShowingBusy)
            {
                _busyGlass.SyncBounds(GetVstBusyGlassCoverBounds());
            }
        };
        Closing += (_, _) => HideVstBusyGlass(fade: false);
        Closed += (_, _) => _vstScanCts?.Cancel();
    }

    private Rect GetVstBusyGlassCoverBounds() =>
        new(0, 0, Math.Max(0, SettingsBody.ActualWidth), Math.Max(0, SettingsBody.ActualHeight));

    /// <summary>
    /// フロストの元絵は SettingsBody 自身から取る（覆う範囲と座標系をそろえるため。SettingsChrome は Padding ぶんずれる）。
    /// 取り込みの時点ではすりガラスはまだ非表示なので自分は写らない。
    /// </summary>
    private void ShowVstBusyGlass()
    {
        SettingsBody.UpdateLayout();
        _busyGlass.ShowOverlay(SettingsBody, SettingsBody, GetVstBusyGlassCoverBounds(), UiStrings.OverlayVstScan);
    }

    private void HideVstBusyGlass(bool fade)
    {
        if (_busyGlass.Visibility != Visibility.Visible)
        {
            return;
        }

        if (fade)
        {
            _busyGlass.BeginFadeOut();
        }
        else
        {
            _busyGlass.HideOverlay();
        }
    }

    private void ReportVstScanProgress(Vst3ScanProgress progress)
    {
        if (!_vstScanning)
        {
            return;
        }

        _busyGlass.SetProgress(progress.Fraction);
        _busyGlass.SetMessage(progress.Total > 0
            ? UiStrings.OverlayVstScanProgress(progress.Current, progress.Total)
            : UiStrings.OverlayVstScan);
    }

    private void ApplyVstButtons()
    {
        ActionButtonLooks.ApplyClear(Vst3SearchPathAddButton);
        ActionButtonLooks.ApplyClear(Vst3SearchPathRemoveButton);
        ActionButtonLooks.ApplyClear(Vst3SearchPathUpButton);
        ActionButtonLooks.ApplyClear(Vst3SearchPathDownButton);
        ActionButtonLooks.ApplyClear(VstScanButton);
        ActionButtonLooks.ApplyClear(VstClearButton);
        ActionButtonLooks.ApplyClear(VstPluginAddButton);
        ActionButtonLooks.ApplyClear(VstPluginRemoveButton);
    }

    private void ApplyVstListChrome()
    {
        ApplyPathListChrome(Vst3SearchPathsList);
        ApplyLibraryExplorerRootTemplate(Vst3SearchPathsList);
        ApplyVstGridStyles(VstExcludedGrid);
        ApplyVstGridStyles(VstRecognizedGrid);
    }

    /// <summary>テーマ変更時。タブを外していれば何もしない。</summary>
    private void RefreshVstChrome()
    {
        if (!VstTabEnabled)
        {
            return;
        }

        ApplyVstButtons();
        ApplyVstListChrome();
    }

    /// <summary>OK 時。タブが無い（Release）ときは開いた時の値をそのまま返す。</summary>
    private void CaptureVst3SearchPaths()
    {
        if (!VstTabEnabled)
        {
            return;
        }

        SelectedVst3SearchPaths = Vst3SearchPathList.Resolve(ReadVst3SearchPathsFromList());
        SelectedVst3UseSteinbergHostCache = VstUseSteinbergHostCacheBox.IsChecked == true;
    }

    private string[] ReadVst3SearchPathsFromList() =>
        Vst3SearchPathsList.Items
            .OfType<LibraryExplorerRootItem>()
            .Select(item => item.Path)
            .ToArray();

    private void FillVst3SearchPaths()
    {
        Vst3SearchPathsList.Items.Clear();
        foreach (var path in SelectedVst3SearchPaths)
        {
            Vst3SearchPathsList.Items.Add(new LibraryExplorerRootItem(path));
        }

        RefreshVst3SearchPathButtons();
    }

    private void ShowVstPluginList(Vst3PluginListDocument document)
    {
        _vstDocument = document;
        var excluded = document.Plugins.Where(p => !p.IsRecognized).Select(p => new VstPluginRow(p)).ToList();
        var recognized = document.Plugins.Where(p => p.IsRecognized).Select(p => new VstPluginRow(p)).ToList();
        SetVstRows(VstExcludedGrid, excluded);
        SetVstRows(VstRecognizedGrid, recognized);
        VstExcludedHeader.Text = UiStrings.LabelVstExcluded(excluded.Count);
        VstRecognizedHeader.Text = UiStrings.LabelVstRecognized(recognized.Count);

        VstStatusText.Text = document.ScannedAtUtc is { } scannedAt
            ? UiStrings.LabelVstScanStatus(
                document.Plugins.Length,
                recognized.Count,
                excluded.Count(row => row.Entry.Recognition == Vst3RecognitionKind.Failed),
                scannedAt)
            : UiStrings.LabelVstNotScanned;
        VstClearButton.IsEnabled = !_vstScanning && HasVstDocument;
        RefreshVstPluginButtons();
    }

    /// <summary>ItemsSource を差し替えると列見出しの並べ替えが外れるので掛け直す。</summary>
    private static void SetVstRows(DataGrid grid, List<VstPluginRow> rows)
    {
        var sorts = grid.Items.SortDescriptions.ToArray();
        grid.ItemsSource = rows;
        foreach (var sort in sorts)
        {
            grid.Items.SortDescriptions.Add(sort);
        }

        foreach (var column in grid.Columns)
        {
            var sort = sorts.FirstOrDefault(s => s.PropertyName == column.SortMemberPath);
            column.SortDirection = sort.PropertyName is null ? null : sort.Direction;
        }
    }

    private bool HasVstDocument => _vstDocument.ScannedAtUtc is not null || _vstDocument.Plugins.Length > 0;

    private void RefreshVstPluginButtons()
    {
        VstPluginAddButton.IsEnabled = !_vstScanning && VstExcludedGrid.SelectedItems.Count > 0;
        VstPluginRemoveButton.IsEnabled = !_vstScanning && VstRecognizedGrid.SelectedItems.Count > 0;
    }

    private void VstPluginGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RefreshVstPluginButtons();

    private void VstPluginAddButton_Click(object sender, RoutedEventArgs e) =>
        MoveVstPlugins(VstExcludedGrid, recognized: true);

    private void VstPluginRemoveButton_Click(object sender, RoutedEventArgs e) =>
        MoveVstPlugins(VstRecognizedGrid, recognized: false);

    private void VstExcludedGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IsOnDataGridRow(e.OriginalSource as DependencyObject, VstExcludedGrid))
        {
            MoveVstPlugins(VstExcludedGrid, recognized: true);
            e.Handled = true;
        }
    }

    private void VstRecognizedGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IsOnDataGridRow(e.OriginalSource as DependencyObject, VstRecognizedGrid))
        {
            MoveVstPlugins(VstRecognizedGrid, recognized: false);
            e.Handled = true;
        }
    }

    /// <summary>見出しやスクロールバーのダブルクリックでは動かさない。</summary>
    private static bool IsOnDataGridRow(DependencyObject? source, DataGrid grid)
    {
        for (var current = source; current is not null && !ReferenceEquals(current, grid);
             current = current is Visual or System.Windows.Media.Media3D.Visual3D
                 ? VisualTreeHelper.GetParent(current)
                 : LogicalTreeHelper.GetParent(current))
        {
            if (current is DataGridRow)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 選んだ行を反対側へ移してすぐ保存する。連続操作のため、元の表の同じ位置付近を選び直す。
    /// </summary>
    private void MoveVstPlugins(DataGrid source, bool recognized)
    {
        if (_vstScanning)
        {
            return;
        }

        var rows = source.SelectedItems.OfType<VstPluginRow>().ToArray();
        if (rows.Length == 0)
        {
            return;
        }

        var resumeIndex = rows.Select(row => source.Items.IndexOf(row)).Where(i => i >= 0).DefaultIfEmpty(0).Min();
        if (Vst3PluginList.SetRecognized(_vstDocument, rows.Select(row => row.Path), recognized) == 0)
        {
            return;
        }

        var saved = Vst3PluginList.Save(_vstDocument);
        ShowVstPluginList(_vstDocument);
        SelectNearestVstRow(source, resumeIndex);
        if (!saved)
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.ErrorVstListSaveFailed,
                UiStrings.DialogSettingsTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static void SelectNearestVstRow(DataGrid grid, int preferredIndex)
    {
        grid.UnselectAll();
        if (grid.Items.Count == 0)
        {
            grid.Focus();
            return;
        }

        var pick = grid.Items[Math.Clamp(preferredIndex, 0, grid.Items.Count - 1)];
        grid.SelectedItem = pick;
        grid.ScrollIntoView(pick);
        // ItemsSource 差し替え直後はコンテナ未生成のため、フォーカスはレイアウト後に当てる。
        grid.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new Action(() =>
            {
                if (grid.ItemContainerGenerator.ContainerFromItem(pick) is DataGridRow row)
                {
                    row.Focus();
                }
                else
                {
                    grid.Focus();
                }
            }));
    }

    private async void VstScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vstScanning)
        {
            return;
        }

        var roots = ReadVst3SearchPathsFromList();
        // スキャンは今のチェック状態を使う（OK 前でも試せる）。保存は OK 時。
        var useHostCache = VstUseSteinbergHostCacheBox.IsChecked == true;
        // 手動で認識させないものを引き継ぐ。振り分けは変更のたびに保存済みなので、表示中の表が最新。
        var previous = _vstDocument;
        _vstScanCts?.Dispose();
        _vstScanCts = new CancellationTokenSource();
        var token = _vstScanCts.Token;
        SetVstScanning(true);
        ShowVstBusyGlass();
        var progress = new Progress<Vst3ScanProgress>(ReportVstScanProgress);
        Vst3PluginListDocument document;
        bool saved;
        try
        {
            (document, saved) = await Task.Run(
                () =>
                {
                    var found = Vst3PluginScanner.ScanWithMetadata(
                        roots,
                        progress,
                        token,
                        useHostCache);
                    var doc = Vst3PluginList.MergeScan(previous, found, DateTimeOffset.UtcNow);
                    return (doc, Vst3PluginList.Save(doc));
                },
                token);
        }
        catch (OperationCanceledException)
        {
            if (IsLoaded)
            {
                SetVstScanning(false);
                HideVstBusyGlass(fade: true);
            }

            return;
        }
        catch (Exception ex)
        {
            if (IsLoaded)
            {
                SetVstScanning(false);
                HideVstBusyGlass(fade: true);
                OwnerCenteredMessageBox.Show(
                    this,
                    UiStrings.ErrorVstScanFailed(ex.Message),
                    UiStrings.DialogSettingsTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            return;
        }

        if (!IsLoaded || token.IsCancellationRequested)
        {
            return;
        }

        SetVstScanning(false);
        ShowVstPluginList(document);
        _busyGlass.SetProgress(1);
        HideVstBusyGlass(fade: true);
        var scanned = UiStrings.InfoVstScanned(
            document.Plugins.Length,
            document.Plugins.Count(p => p.Recognition == Vst3RecognitionKind.Failed),
            document.Plugins.Count(p => p.Recognition == Vst3RecognitionKind.InstrumentExcluded),
            document.Plugins.Count(p => p.Recognition == Vst3RecognitionKind.AraExcluded));
        OwnerCenteredMessageBox.Show(
            this,
            saved
                ? scanned
                : scanned + "\n" + UiStrings.ErrorVstListSaveFailed,
            UiStrings.DialogSettingsTitle,
            MessageBoxButton.OK,
            saved ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void SetVstScanning(bool scanning)
    {
        _vstScanning = scanning;
        VstScanButton.IsEnabled = !scanning;
        VstScanButton.Content = scanning ? UiStrings.ButtonVstScanning : UiStrings.ButtonVstScan;
        VstClearButton.IsEnabled = !scanning && HasVstDocument;
        RefreshVstPluginButtons();
        Vst3SearchPathAddButton.IsEnabled = !scanning;
        VstUseSteinbergHostCacheBox.IsEnabled = !scanning;
        if (scanning)
        {
            Vst3SearchPathRemoveButton.IsEnabled = false;
            Vst3SearchPathUpButton.IsEnabled = false;
            Vst3SearchPathDownButton.IsEnabled = false;
        }
        else
        {
            RefreshVst3SearchPathButtons();
        }
    }

    private void VstClearButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vstScanning)
        {
            return;
        }

        var answer = OwnerCenteredMessageBox.Show(
            this,
            UiStrings.ConfirmVstClear,
            UiStrings.DialogSettingsTitle,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        Vst3PluginList.Clear();
        ShowVstPluginList(new Vst3PluginListDocument());
        OwnerCenteredMessageBox.Show(
            this,
            UiStrings.InfoVstCleared,
            UiStrings.DialogSettingsTitle,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void Vst3SearchPathAddButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = UiStrings.TitleVst3SearchPathAdd,
        };
        if (dialog.ShowDialog(this) != true
            || !PathUtil.TryGetFullPathTrimmedSeparators(dialog.FolderName, out var full))
        {
            return;
        }

        foreach (var existing in Vst3SearchPathsList.Items.OfType<LibraryExplorerRootItem>())
        {
            if (existing.Path.Equals(full, StringComparison.OrdinalIgnoreCase))
            {
                Vst3SearchPathsList.SelectedItem = existing;
                return;
            }
        }

        var item = new LibraryExplorerRootItem(full);
        Vst3SearchPathsList.Items.Add(item);
        Vst3SearchPathsList.SelectedItem = item;
    }

    private void Vst3SearchPathRemoveButton_Click(object sender, RoutedEventArgs e)
    {
        // 空にしてよい（空はスキャナ既定のフォルダ）。
        foreach (var item in Vst3SearchPathsList.SelectedItems.OfType<LibraryExplorerRootItem>().ToArray())
        {
            Vst3SearchPathsList.Items.Remove(item);
        }

        RefreshVst3SearchPathButtons();
    }

    private void Vst3SearchPathUpButton_Click(object sender, RoutedEventArgs e) =>
        MoveVst3SearchPaths(-1);

    private void Vst3SearchPathDownButton_Click(object sender, RoutedEventArgs e) =>
        MoveVst3SearchPaths(1);

    private void Vst3SearchPathsList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RefreshVst3SearchPathButtons();

    private void MoveVst3SearchPaths(int direction)
    {
        var items = Vst3SearchPathsList.Items.OfType<LibraryExplorerRootItem>().ToList();
        var selected = Vst3SearchPathsList.SelectedItems.OfType<LibraryExplorerRootItem>().ToHashSet();
        if (selected.Count == 0)
        {
            return;
        }

        LibraryExplorerPaths.MoveSelected(items, selected, direction);
        Vst3SearchPathsList.Items.Clear();
        foreach (var item in items)
        {
            Vst3SearchPathsList.Items.Add(item);
        }

        Vst3SearchPathsList.SelectedItems.Clear();
        foreach (var item in selected)
        {
            Vst3SearchPathsList.SelectedItems.Add(item);
        }

        if (Vst3SearchPathsList.SelectedItem is LibraryExplorerRootItem focus)
        {
            Vst3SearchPathsList.ScrollIntoView(focus);
        }

        RefreshVst3SearchPathButtons();
    }

    private void RefreshVst3SearchPathButtons()
    {
        if (_vstScanning)
        {
            return;
        }

        var items = Vst3SearchPathsList.Items.OfType<LibraryExplorerRootItem>().ToList();
        var selected = Vst3SearchPathsList.SelectedItems.OfType<LibraryExplorerRootItem>().ToHashSet();
        var min = int.MaxValue;
        var max = -1;
        for (var i = 0; i < items.Count; i++)
        {
            if (!selected.Contains(items[i]))
            {
                continue;
            }

            min = Math.Min(min, i);
            max = Math.Max(max, i);
        }

        Vst3SearchPathRemoveButton.IsEnabled = selected.Count > 0;
        Vst3SearchPathUpButton.IsEnabled = selected.Count > 0 && min > 0;
        Vst3SearchPathDownButton.IsEnabled = selected.Count > 0 && max >= 0 && max < items.Count - 1;
    }

    private void ConfigureVstGrids()
    {
        ConfigureVstGrid(VstExcludedGrid, withStatus: true);
        ConfigureVstGrid(VstRecognizedGrid, withStatus: false);
    }

    private static void ConfigureVstGrid(DataGrid grid, bool withStatus)
    {
        grid.AutoGenerateColumns = false;
        grid.IsReadOnly = true;
        grid.CanUserAddRows = false;
        grid.CanUserDeleteRows = false;
        grid.CanUserReorderColumns = false;
        grid.CanUserSortColumns = true;
        grid.CanUserResizeRows = false;
        grid.CanUserResizeColumns = true;
        grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        grid.GridLinesVisibility = DataGridGridLinesVisibility.Horizontal;
        grid.SelectionUnit = DataGridSelectionUnit.FullRow;
        grid.SelectionMode = DataGridSelectionMode.Extended;
        grid.ClipboardCopyMode = DataGridClipboardCopyMode.None;
        grid.RowHeaderWidth = 0;
        grid.MinRowHeight = 24;
        grid.BorderThickness = new Thickness(1);
        grid.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        grid.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        grid.HorizontalAlignment = HorizontalAlignment.Stretch;
        grid.VerticalAlignment = VerticalAlignment.Stretch;
        grid.SetResourceReference(Control.BorderBrushProperty, "ChromeBorderBrush");
        grid.SetResourceReference(Control.BackgroundProperty, "DialogInputBackBrush");
        grid.SetResourceReference(Control.ForegroundProperty, "PrimaryForeBrush");
        grid.SetResourceReference(DataGrid.HorizontalGridLinesBrushProperty, "ChromeBorderBrush");
        grid.SetResourceReference(DataGrid.VerticalGridLinesBrushProperty, "ChromeBorderBrush");

        grid.Columns.Clear();
        grid.Columns.Add(VstTextColumn(UiStrings.ColumnVstName, nameof(VstPluginRow.Name), star: true, minWidth: 96));
        grid.Columns.Add(VstTextColumn(UiStrings.ColumnVstVendor, nameof(VstPluginRow.Vendor), star: false, minWidth: 64));
        grid.Columns.Add(VstTextColumn(UiStrings.ColumnVstCategory, nameof(VstPluginRow.Category), star: false, minWidth: 64));
        if (withStatus)
        {
            var status = VstTextColumn(UiStrings.ColumnVstStatus, nameof(VstPluginRow.StatusLabel), star: false, minWidth: 48);
            // 認識失敗は欠落パスと同じ赤、手動は薄色で見分ける。
            var style = new Style(typeof(TextBlock), status.ElementStyle);
            style.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension("MutedForeBrush")));
            var failed = new DataTrigger { Binding = new Binding(nameof(VstPluginRow.IsFailed)), Value = true };
            failed.Setters.Add(new Setter(
                TextBlock.ForegroundProperty,
                new DynamicResourceExtension(LibraryBrowserView.MissingRegisteredPathForeBrushKey)));
            style.Triggers.Add(failed);
            status.ElementStyle = style;
            grid.Columns.Add(status);
        }
    }

    private static DataGridTextColumn VstTextColumn(string header, string path, bool star, double minWidth)
    {
        var text = new Style(typeof(TextBlock));
        text.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        text.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
        text.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(6, 0, 6, 0)));
        return new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(path),
            SortMemberPath = path,
            IsReadOnly = true,
            Width = star
                ? new DataGridLength(1, DataGridLengthUnitType.Star)
                : new DataGridLength(1, DataGridLengthUnitType.Auto),
            MinWidth = minWidth,
            ElementStyle = text,
        };
    }

    /// <summary>TabTimeTableWindow.ApplyGridStyles と同じ見た目。テーマ変更時にも呼ぶ。</summary>
    private static void ApplyVstGridStyles(DataGrid grid)
    {
        var accent = Theme.Get("AccentCyanBrush");
        var selected = new SolidColorBrush(Color.FromArgb(80, accent.R, accent.G, accent.B));
        selected.Freeze();

        var headerStyle = new Style(typeof(DataGridColumnHeader));
        headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("ColorPanelBackBrush")));
        headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));
        headerStyle.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("ChromeBorderBrush")));
        headerStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
        headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 4, 6, 4)));
        headerStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        headerStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        grid.ColumnHeaderStyle = headerStyle;

        var cellStyle = new Style(typeof(DataGridCell));
        cellStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        cellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        cellStyle.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        cellStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));
        var selectedTrigger = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, selected));
        selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));
        cellStyle.Triggers.Add(selectedTrigger);
        grid.CellStyle = cellStyle;

        var rowStyle = new Style(typeof(DataGridRow));
        rowStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        rowStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));
        rowStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(nameof(VstPluginRow.ToolTipText))));
        var rowSelected = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
        rowSelected.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        rowStyle.Triggers.Add(rowSelected);
        grid.RowStyle = rowStyle;
        grid.RowBackground = Brushes.Transparent;
        grid.AlternatingRowBackground = Brushes.Transparent;
        grid.AlternationCount = 0;
        DesignMetrics.BindControlCornerClip(grid);
    }

    /// <summary>表の 1 行。実体は <see cref="Vst3PluginEntry"/>（振り分けを変えたら表ごと作り直す）。</summary>
    private sealed class VstPluginRow(Vst3PluginEntry entry)
    {
        public Vst3PluginEntry Entry { get; } = entry;

        public string Name => Entry.Name;

        public string Path => Entry.Path;

        public string Vendor => Entry.Vendor;

        /// <summary>Fx|Reverb → Fx / Reverb。</summary>
        public string Category => string.Join(
            " / ",
            Entry.Category.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        public bool IsFailed => Entry.Recognition is Vst3RecognitionKind.Failed
            or Vst3RecognitionKind.InstrumentExcluded
            or Vst3RecognitionKind.AraExcluded;

        public string StatusLabel => Entry.Recognition switch
        {
            Vst3RecognitionKind.Failed => UiStrings.LabelVstStatusFailed,
            Vst3RecognitionKind.InstrumentExcluded => UiStrings.LabelVstStatusInstrument,
            Vst3RecognitionKind.AraExcluded => UiStrings.LabelVstStatusAra,
            Vst3RecognitionKind.ManualExcluded => UiStrings.LabelVstStatusManual,
            _ => string.Empty,
        };

        public string ToolTipText
        {
            get
            {
                var lines = new List<string> { Entry.Path };
                if (!string.IsNullOrWhiteSpace(Entry.Version))
                {
                    lines.Add(Entry.Version);
                }

                switch (Entry.Recognition)
                {
                    case Vst3RecognitionKind.Failed:
                        lines.Add(UiStrings.TooltipVstFailed(UiStrings.LabelVstFailureReason(Entry.FailureReason)));
                        break;
                    case Vst3RecognitionKind.InstrumentExcluded:
                        lines.Add(UiStrings.TooltipVstInstrument);
                        break;
                    case Vst3RecognitionKind.AraExcluded:
                        lines.Add(UiStrings.TooltipVstAra);
                        break;
                    case Vst3RecognitionKind.ManualExcluded:
                        lines.Add(UiStrings.TooltipVstManual);
                        break;
                }

                return string.Join("\n", lines);
            }
        }

        public override string ToString() => Name;
    }
}
