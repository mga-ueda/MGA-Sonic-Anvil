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
    private void ConfigureGroupCombo()
    {
        _groupCombo.DisplayMemberPath = nameof(GroupOption.Label);
        _groupCombo.SelectedValuePath = nameof(GroupOption.Group);
    }

    private void FillGroupOptions()
    {
        var selected = _group;
        _groupCombo.ItemsSource = new GroupOption[]
        {
            new(LibraryFileGroup.None, UiStrings.LibraryGroupNone),
            new(LibraryFileGroup.Title, UiStrings.LibraryGroupTitle),
            new(LibraryFileGroup.Artist, UiStrings.LibraryGroupArtist),
            new(LibraryFileGroup.Album, UiStrings.LibraryGroupAlbum),
            new(LibraryFileGroup.Genre, UiStrings.LibraryGroupGenre),
            new(LibraryFileGroup.Year, UiStrings.LibraryGroupYear),
            new(LibraryFileGroup.Kind, UiStrings.LibraryGroupKind),
            new(LibraryFileGroup.SampleRate, UiStrings.LibraryGroupSampleRate),
            new(LibraryFileGroup.BitDepth, UiStrings.LibraryGroupBitDepth),
            new(LibraryFileGroup.Channels, UiStrings.LibraryGroupChannels),
            new(LibraryFileGroup.Folder, UiStrings.LibraryGroupFolder),
            new(LibraryFileGroup.Color, UiStrings.LibraryGroupColor),
        };
        _groupCombo.SelectedValue = selected;
    }

    public void SetVisibleColumnPresets(
        IEnumerable<LibraryFileColumn> waveColumns,
        IEnumerable<LibraryFileColumn> mp3Columns,
        IEnumerable<LibraryFileColumn>? mixedColumns = null,
        bool mixedCustomized = false,
        IEnumerable<LibraryFileColumn>? videoColumns = null)
    {
        _waveColumns = LibraryColumnFilter.ResolveWave(
            LibraryColumnFilter.Serialize(waveColumns, LibraryColumnFilter.WaveDefaults));
        _mp3Columns = LibraryColumnFilter.ResolveMp3(
            LibraryColumnFilter.Serialize(mp3Columns, LibraryColumnFilter.Mp3Defaults));
        _videoColumns = LibraryColumnFilter.ResolveVideo(
            videoColumns is null
                ? null
                : LibraryColumnFilter.Serialize(videoColumns, LibraryColumnFilter.VideoDefaults));
        _mixedColumnsCustomized = mixedCustomized && mixedColumns is not null;
        _mixedColumns = _mixedColumnsCustomized
            ? LibraryColumnFilter.ResolveMixed(
                LibraryColumnFilter.Serialize(mixedColumns!, LibraryColumnFilter.MixedDefaults),
                _waveColumns,
                _mp3Columns,
                _videoColumns)
            : LibraryColumnFilter.Union(_waveColumns, _mp3Columns, _videoColumns);
        ApplyActiveColumnVisibility(notify: false, forceLayout: true);
    }

    /// <summary>テスト用。Wave / MP3 / Video プリセットへ同じ列を入れる。</summary>
    public void SetVisibleColumns(IEnumerable<LibraryFileColumn> columns)
    {
        var ordered = LibraryColumnFilter.ResolveWave(
            LibraryColumnFilter.Serialize(columns, LibraryColumnFilter.WaveDefaults));
        SetVisibleColumnPresets(ordered, ordered, videoColumns: ordered);
    }

    internal IReadOnlyList<LibraryFileColumn> WaveColumnOrder => _waveColumns;

    internal IReadOnlyList<LibraryFileColumn> Mp3ColumnOrder => _mp3Columns;

    internal IReadOnlyList<LibraryFileColumn> VideoColumnOrder => _videoColumns;

    internal IReadOnlyList<LibraryFileColumn> MixedColumnOrder => _mixedColumns;

    public void SetGroup(LibraryFileGroup group)
    {
        if (_group == group)
        {
            if (!Equals(_groupCombo.SelectedValue, group))
            {
                _groupCombo.SelectedValue = group;
            }

            return;
        }

        _group = group;
        _groupCombo.SelectedValue = group;
        SyncGroupChrome();
        if (_grid.ItemsSource is null)
        {
            return;
        }

        var active = SelectedSession;
        var selected = SelectedSessions;
        LibraryFileList.ApplyGroupKeys(_rows, _group);
        BindRows(active, selected);
        InvalidateGroupJackets();
        _ = EnsureGroupArtworkAsync();
    }

    private void ApplyColumnVisibility(IReadOnlyList<LibraryFileColumn> visible, bool notify)
    {
        var kind = CurrentPlaylistColumnKind();
        LibraryColumnFilter.ApplyVisibleChange(
            kind,
            _waveColumns,
            _mp3Columns,
            _videoColumns,
            _mixedColumns,
            visible,
            out var wave,
            out var mp3,
            out var video,
            out var mixed);
        _waveColumns = wave;
        _mp3Columns = mp3;
        _videoColumns = video;
        _mixedColumns = mixed;
        // Wave / MP3 / Video を変えたら混在は和集合へ戻す。混在を弄ったときだけユーザー設定になる。
        _mixedColumnsCustomized = kind == LibraryPlaylistColumnKind.Mixed;

        ApplyActiveColumnVisibility(notify);
    }

    private void ApplyActiveColumnVisibility(bool notify, bool forceLayout = false)
    {
        if (forceLayout || !KeepsColumnLayoutWhileEmpty())
        {
            _visibleColumns = LibraryColumnFilter.ResolveActive(
                _rows,
                _waveColumns,
                _mp3Columns,
                _mixedColumns,
                _videoColumns);
            ApplyColumnDisplayOrder(_visibleColumns);
        }

        ApplyEffectiveColumnVisibility();
        EnforceFadingColumnSnapshotIfEmpty();

        if (notify)
        {
            VisibleColumnPresetsChanged?.Invoke(
                this,
                new LibraryColumnPresets(
                    _waveColumns,
                    _mp3Columns,
                    _videoColumns,
                    _mixedColumns,
                    PersistMixed: _mixedColumnsCustomized));
        }

        RequestFitColumns();
        SyncGroupChrome();
        if (!_columnChromeFadingOut)
        {
            RefreshPlaylistWaveformSize(reschedule: true);
            SchedulePlaylistLoudness();
        }
    }

    /// <summary>
    /// 設定でオンの列のうち、プレイリストに値が無い列は隠す。値が付けば出す。
    /// 空のプレイリストは列をすべて隠す。ソートは曲が入るまで残す。
    /// 表示中から空にしたときはメーターと同じ 1 秒でフェードアウトする。
    /// ソート中の列が隠れたらソート無しにする。
    /// </summary>
    private void ApplyEffectiveColumnVisibility()
    {
        if (_rows.Count > 0)
        {
            _playlistColumnKind = LibraryColumnFilter.ClassifyPlaylistColumns(_rows);
            _visibleColumns = LibraryColumnFilter.ResolveActive(
                _rows,
                _waveColumns,
                _mp3Columns,
                _mixedColumns,
                _videoColumns);
            var enabled = new HashSet<LibraryFileColumn>(_visibleColumns);
            var used = CurrentUsedColumns();
            if (_sortColumn is { } sort
                && !LibraryColumnFilter.IsEffectivelyVisible(sort, enabled, used))
            {
                ClearSort(resort: false);
                NotifySortChanged();
            }

            CancelColumnChromeFade(resetOpacity: true);
            foreach (var pair in _columns)
            {
                pair.Value.Visibility = LibraryColumnFilter.IsEffectivelyVisible(pair.Key, enabled, used)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            return;
        }

        if (_columnChromeFadingOut)
        {
            EnforceFadingColumnSnapshot();
            return;
        }

        if (!AnyPlaylistColumnShown())
        {
            CancelColumnChromeFade(resetOpacity: true);
            return;
        }

        if (BeginColumnChromeFadeOut())
        {
            return;
        }

        CollapseAllPlaylistColumns();
    }

    private LibraryPlaylistColumnKind CurrentPlaylistColumnKind() =>
        _rows.Count == 0
            ? _playlistColumnKind
            : LibraryColumnFilter.ClassifyPlaylistColumns(_rows);

    private void BeginColumnOrderCommitGate() => _columnOrderCommitGate++;

    private void EndColumnOrderCommitGate()
    {
        if (_columnOrderCommitGate > 0)
        {
            _columnOrderCommitGate--;
        }
    }

    private bool SuppressesColumnOrderCommit() =>
        _columnOrderBusy
        || _columnOrderCommitGate > 0
        || _columnChromeFadingOut
        || _fadingColumnSnapshot is not null;

    private void PrepareEmptyPlaylistColumnFade()
    {
        if (_columnChromeFadingOut || !AnyPlaylistColumnShown())
        {
            return;
        }

        BeginColumnChromeFadeOut();
    }

    private bool KeepsColumnLayoutWhileEmpty() =>
        _rows.Count == 0
        && (AnyPlaylistColumnShown() || _columnChromeFadingOut || _fadingColumnSnapshot is not null);

    private bool AnyPlaylistColumnShown()
    {
        foreach (var pair in _columns)
        {
            if (pair.Value.Visibility == Visibility.Visible)
            {
                return true;
            }
        }

        return false;
    }

    private bool BeginColumnChromeFadeOut()
    {
        if (!IsLoaded)
        {
            return false;
        }

        if (FindDescendant<DataGridColumnHeadersPresenter>(_grid) is not { } headers)
        {
            UpdateLayout();
            headers = FindDescendant<DataGridColumnHeadersPresenter>(_grid);
            if (headers is null)
            {
                return false;
            }
        }

        _columnChromeFadingOut = true;
        CaptureFadingColumnSnapshot();
        var ticket = ++_columnChromeFadeTicket;
        var from = headers.Opacity;
        headers.BeginAnimation(UIElement.OpacityProperty, null);
        headers.Opacity = from;
        if (from <= 0.001)
        {
            FinishColumnChromeFadeOut(ticket);
            return true;
        }

        var anim = LibraryPlayerMode.CreateMeterFade(from, 0);
        anim.Completed += (_, _) => FinishColumnChromeFadeOut(ticket);
        headers.BeginAnimation(UIElement.OpacityProperty, anim);
        return true;
    }

    private void FinishColumnChromeFadeOut(int ticket)
    {
        if (ticket != _columnChromeFadeTicket)
        {
            return;
        }

        _columnChromeFadingOut = false;
        _fadingColumnSnapshot = null;
        if (_rows.Count > 0)
        {
            ResetColumnHeaderOpacity();
            ApplyEffectiveColumnVisibility();
            SyncGroupChrome();
            RequestFitColumns();
            return;
        }

        CollapseAllPlaylistColumns();
        ResetColumnHeaderOpacity();
        SyncGroupChrome();
        RequestFitColumns();
    }

    private void CaptureFadingColumnSnapshot()
    {
        var snapshot = new HashSet<LibraryFileColumn>();
        foreach (var pair in _columns)
        {
            if (pair.Value.Visibility == Visibility.Visible)
            {
                snapshot.Add(pair.Key);
            }
        }

        _fadingColumnSnapshot = snapshot;
    }

    private void EnforceFadingColumnSnapshotIfEmpty()
    {
        if (_rows.Count == 0)
        {
            EnforceFadingColumnSnapshot();
        }
    }

    private void EnforceFadingColumnSnapshot()
    {
        if (_fadingColumnSnapshot is not { Count: > 0 } snapshot)
        {
            return;
        }

        foreach (var pair in _columns)
        {
            pair.Value.Visibility = snapshot.Contains(pair.Key)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void CollapseAllPlaylistColumns()
    {
        foreach (var pair in _columns)
        {
            pair.Value.Visibility = Visibility.Collapsed;
        }
    }

    private void CancelColumnChromeFade(bool resetOpacity)
    {
        _columnChromeFadeTicket++;
        _columnChromeFadingOut = false;
        _fadingColumnSnapshot = null;
        if (resetOpacity)
        {
            ResetColumnHeaderOpacity();
        }
    }

    private void ResetColumnHeaderOpacity()
    {
        if (FindDescendant<DataGridColumnHeadersPresenter>(_grid) is not { } headers)
        {
            return;
        }

        headers.BeginAnimation(UIElement.OpacityProperty, null);
        headers.Opacity = 1;
    }

    private HashSet<LibraryFileColumn>? CurrentUsedColumns() =>
        LibraryColumnFilter.UsedColumns(_rows);

    private void ClearSort(bool resort)
    {
        if (_sortColumn is null)
        {
            return;
        }

        _sortColumn = null;
        foreach (var item in _columns)
        {
            item.Value.SortDirection = null;
        }

        RefreshSortChrome();
        if (!resort)
        {
            return;
        }

        var active = SelectedSession;
        var selected = SelectedSessions;
        BindRows(active, selected);
    }

    private IReadOnlyList<LibraryFileRow> SortRows(IReadOnlyList<LibraryFileRow> rows) =>
        _sortColumn is { } column
            ? LibraryFileList.Sort(rows, column, _sortDirection)
            : rows;

    private void RefreshEffectiveColumns()
    {
        BeginColumnOrderCommitGate();
        try
        {
            if (!KeepsColumnLayoutWhileEmpty())
            {
                var next = LibraryColumnFilter.ResolveActive(
                    _rows,
                    _waveColumns,
                    _mp3Columns,
                    _mixedColumns,
                    _videoColumns);
                if (!next.SequenceEqual(_visibleColumns))
                {
                    _visibleColumns = next;
                    ApplyColumnDisplayOrder(_visibleColumns);
                }
            }

            ApplyEffectiveColumnVisibility();
            EnforceFadingColumnSnapshotIfEmpty();
            SyncGroupChrome();
            RequestFitColumns();
        }
        finally
        {
            EndColumnOrderCommitGate();
        }
    }

    private void ApplyColumnDisplayOrder(IReadOnlyList<LibraryFileColumn> visible)
    {
        if (_columns.Count == 0)
        {
            return;
        }

        _columnOrderBusy = true;
        try
        {
            var ordered = new List<DataGridColumn>(_grid.Columns.Count);
            var grouped = _group != LibraryFileGroup.None;
            if (grouped)
            {
                ordered.Add(_groupSpacer);
            }

            if (_checkColumn is not null)
            {
                ordered.Add(_checkColumn);
            }

            var seen = new HashSet<LibraryFileColumn>();
            foreach (var column in visible)
            {
                if (_columns.TryGetValue(column, out var gridColumn) && seen.Add(column))
                {
                    ordered.Add(gridColumn);
                }
            }

            foreach (var column in LibraryColumnFilter.All)
            {
                if (!seen.Contains(column) && _columns.TryGetValue(column, out var gridColumn))
                {
                    ordered.Add(gridColumn);
                }
            }

            if (!grouped)
            {
                ordered.Add(_groupSpacer);
            }

            for (var i = 0; i < ordered.Count; i++)
            {
                if (ordered[i].DisplayIndex != i)
                {
                    ordered[i].DisplayIndex = i;
                }
            }
        }
        finally
        {
            _columnOrderBusy = false;
        }
    }

    private LibraryFileColumn[] ReadVisibleColumnOrder()
    {
        var visible = new List<(int Index, LibraryFileColumn Column)>(_columns.Count);
        foreach (var pair in _columns)
        {
            if (pair.Value.Visibility != Visibility.Visible
                && (_rows.Count > 0 || Array.IndexOf(_visibleColumns, pair.Key) < 0))
            {
                continue;
            }

            visible.Add((pair.Value.DisplayIndex, pair.Key));
        }

        visible.Sort(static (left, right) => left.Index.CompareTo(right.Index));
        var result = new LibraryFileColumn[visible.Count];
        for (var i = 0; i < visible.Count; i++)
        {
            result[i] = visible[i].Column;
        }

        if (result.Length == 0 || Array.IndexOf(result, LibraryFileColumn.Name) < 0)
        {
            return [.. _visibleColumns];
        }

        return result;
    }

    private void Grid_ColumnReordered(object? sender, DataGridColumnEventArgs e)
    {
        if (_columnOrderBusy)
        {
            return;
        }

        Dispatcher.BeginInvoke(CommitColumnOrder, DispatcherPriority.Background);
    }

    internal void CommitColumnOrder()
    {
        if (SuppressesColumnOrderCommit())
        {
            return;
        }

        var next = ReadVisibleColumnOrder();
        ApplyColumnDisplayOrder(next);
        if (next.SequenceEqual(_visibleColumns))
        {
            return;
        }

        ApplyColumnVisibility(next, notify: true);
    }

    internal void SetColumnEnabledForTests(LibraryFileColumn column, bool enabled)
    {
        var selected = new HashSet<LibraryFileColumn>(_visibleColumns) { LibraryFileColumn.Name };
        if (enabled)
        {
            selected.Add(column);
        }
        else
        {
            selected.Remove(column);
        }

        ApplyColumnVisibility(LibraryColumnFilter.Merge(_visibleColumns, selected), notify: true);
    }

    internal void MoveVisibleColumnForTests(LibraryFileColumn column, int displayIndex)
    {
        if (!_columns.TryGetValue(column, out var gridColumn))
        {
            return;
        }

        gridColumn.DisplayIndex = displayIndex;
        CommitColumnOrder();
    }

    private static string ColumnHeader(LibraryFileColumn column) =>
        UiStrings.LibraryColumnHeader(column);

    private void AddGroupSpacerColumn()
    {
        var jacket = new FrameworkElementFactory(typeof(LibraryRowJacketImage));
        jacket.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        jacket.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        var cell = new DataTemplate(typeof(LibraryFileRow)) { VisualTree = jacket };
        _groupSpacer = new DataGridTemplateColumn
        {
            Header = string.Empty,
            CellTemplate = cell,
            Width = new DataGridLength(GroupJacketColumnWidth),
            MinWidth = GroupJacketColumnWidth,
            MaxWidth = GroupJacketColumnWidth,
            IsReadOnly = true,
            CanUserSort = false,
            CanUserResize = false,
            CanUserReorder = false,
        };
        ApplyGroupSpacerCellStyle();
        _grid.Columns.Add(_groupSpacer);
    }

    private void AddCheckColumn()
    {
        var box = new FrameworkElementFactory(typeof(CheckBox));
        box.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        box.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        box.SetValue(UIElement.FocusableProperty, false);
        box.SetValue(FrameworkElement.StyleProperty, new DynamicResourceExtension("DarkCheckBoxStyle"));
        box.SetBinding(
            ToggleButton.IsCheckedProperty,
            new Binding(nameof(LibraryFileRow.IsChecked)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        var cell = new DataTemplate(typeof(LibraryFileRow)) { VisualTree = box };
        _checkColumn = new DataGridTemplateColumn
        {
            Header = string.Empty,
            CellTemplate = cell,
            Width = new DataGridLength(28),
            MinWidth = 28,
            MaxWidth = 28,
            IsReadOnly = false,
            CanUserSort = false,
            CanUserResize = false,
            CanUserReorder = false,
            Visibility = Visibility.Collapsed,
        };
        _checkColumn.CellStyle = CreatePlaylistCellStyle();
        _grid.Columns.Add(_checkColumn);
    }

    private void ApplyGroupSpacerCellStyle()
    {
        if (_groupSpacer is null)
        {
            return;
        }

        _groupSpacer.CellStyle = CreatePlaylistCellStyle();
    }

    private void AddNameColumn()
    {
        var marker = new FrameworkElementFactory(typeof(Border));
        marker.Name = "NameMarker";
        marker.SetValue(FrameworkElement.WidthProperty, FileNameColorMarkerVisual.Width);
        marker.SetValue(FrameworkElement.HeightProperty, FileNameColorMarkerVisual.Height);
        marker.SetValue(Border.CornerRadiusProperty, FileNameColorMarkerVisual.Corner);
        marker.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        marker.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, FileNameColorMarkerVisual.Gap, 0));
        marker.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        marker.SetValue(DockPanel.DockProperty, Dock.Left);
        marker.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        marker.SetBinding(
            Border.BackgroundProperty,
            new Binding(nameof(LibraryFileRow.FileNameMarkerIndex))
            {
                Converter = FileNameMarkerBrushConverter.Instance,
            });

        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.Name = "NameText";
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(LibraryFileRow.Name)));
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        text.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        text.SetValue(TextBlock.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush"));

        var panel = new FrameworkElementFactory(typeof(DockPanel));
        panel.SetValue(
            FrameworkElement.MarginProperty,
            new Thickness(LibraryColumnCellPadX, 0, LibraryColumnCellPadX, 0));
        panel.AppendChild(marker);
        panel.AppendChild(text);

        var cellTemplate = new DataTemplate(typeof(LibraryFileRow)) { VisualTree = panel };
        var showMarker = new DataTrigger
        {
            Binding = new Binding(nameof(LibraryFileRow.HasFileNameMarker)),
            Value = true,
        };
        showMarker.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "NameMarker"));
        cellTemplate.Triggers.Add(showMarker);
        var cutDim = new DataTrigger
        {
            Binding = new Binding(nameof(LibraryFileRow.CutPending)),
            Value = true,
        };
        cutDim.Setters.Add(new Setter(
            TextBlock.ForegroundProperty,
            new DynamicResourceExtension("MutedForeBrush"),
            "NameText"));
        cellTemplate.Triggers.Add(cutDim);

        var gridColumn = new DataGridTemplateColumn
        {
            CellTemplate = cellTemplate,
            MinWidth = 0,
            Width = DataGridLength.SizeToCells,
            IsReadOnly = true,
            CanUserSort = true,
            SortMemberPath = nameof(LibraryFileRow.Name),
        };
        var sortHeader = new LibrarySortHeader(ColumnHeader(LibraryFileColumn.Name));
        _sortHeaders[LibraryFileColumn.Name] = sortHeader;
        gridColumn.Header = sortHeader;
        _columns[LibraryFileColumn.Name] = gridColumn;
        _grid.Columns.Add(gridColumn);
    }

    private void AddColumn(
        LibraryFileColumn column,
        string binding,
        bool right = false,
        string? nonDefaultBinding = null,
        bool trim = true,
        bool loudnessTone = false)
    {
        var text = new Style(typeof(TextBlock));
        text.Setters.Add(new Setter(
            TextBlock.TextTrimmingProperty,
            trim ? TextTrimming.CharacterEllipsis : TextTrimming.None));
        text.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(LibraryColumnCellPadX, 0, LibraryColumnCellPadX, 0)));
        text.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        text.Setters.Add(new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));
        var cutDim = new DataTrigger
        {
            Binding = new Binding(nameof(LibraryFileRow.CutPending)),
            Value = true,
        };
        cutDim.Setters.Add(new Setter(
            TextBlock.ForegroundProperty,
            new DynamicResourceExtension("MutedForeBrush")));
        text.Triggers.Add(cutDim);
        if (right)
        {
            text.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
        }

        if (nonDefaultBinding is not null)
        {
            // 既定 Wave フォーマットと違うレート／ビット／Ch だけ着色（行全体は塗らない）。色は色設定。
            var mismatch = new DataTrigger
            {
                Binding = new Binding(nonDefaultBinding),
                Value = true,
            };
            mismatch.Setters.Add(new Setter(
                TextBlock.ForegroundProperty,
                new DynamicResourceExtension(LibraryPlayerMode.FormatMismatchForeBrushKey)));
            text.Triggers.Add(mismatch);
        }

        if (loudnessTone)
        {
            // メーターと同じ3色。ターゲット以下が青、+3 LU までが黄、それより上が赤。
            AddLoudnessToneTrigger(text, LoudnessTraffic.Safe, "LoudnessSafeBrush");
            AddLoudnessToneTrigger(text, LoudnessTraffic.Caution, "LoudnessCautionBrush");
            AddLoudnessToneTrigger(text, LoudnessTraffic.Danger, "LoudnessDangerBrush");
        }

        var gridColumn = new DataGridTextColumn
        {
            Binding = new Binding(binding),
            MinWidth = 0,
            Width = DataGridLength.SizeToCells,
            IsReadOnly = true,
            ElementStyle = text,
            CanUserSort = true,
            SortMemberPath = binding,
        };
        var sortHeader = new LibrarySortHeader(ColumnHeader(column));
        _sortHeaders[column] = sortHeader;
        gridColumn.Header = sortHeader;
        _columns[column] = gridColumn;
        _grid.Columns.Add(gridColumn);
    }

    private static void AddLoudnessToneTrigger(Style text, LoudnessTraffic tone, string brushKey)
    {
        var trigger = new DataTrigger
        {
            Binding = new Binding(nameof(LibraryFileRow.LoudnessTone)),
            Value = tone,
        };
        trigger.Setters.Add(new Setter(
            TextBlock.ForegroundProperty,
            new DynamicResourceExtension(brushKey)));
        text.Triggers.Add(trigger);
    }

    private void AddWaveformColumn()
    {
        var width = LibraryPlaylistWaveform.ColumnWidth;
        var factory = new FrameworkElementFactory(typeof(LibraryPlaylistWaveformCell));
        factory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        factory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        var cellTemplate = new DataTemplate(typeof(LibraryFileRow)) { VisualTree = factory };
        var gridColumn = new DataGridTemplateColumn
        {
            CellTemplate = cellTemplate,
            MinWidth = width,
            MaxWidth = width,
            Width = new DataGridLength(width),
            IsReadOnly = true,
            CanUserSort = false,
            CanUserResize = false,
        };
        var sortHeader = new LibrarySortHeader(ColumnHeader(LibraryFileColumn.Waveform));
        _sortHeaders[LibraryFileColumn.Waveform] = sortHeader;
        gridColumn.Header = sortHeader;
        _columns[LibraryFileColumn.Waveform] = gridColumn;
        _grid.Columns.Add(gridColumn);
    }

    private void RequestFitColumns()
    {
        if (!IsLoaded || _fitColumnsQueued || _columnChromeFadingOut)
        {
            return;
        }

        _fitColumnsQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _fitColumnsQueued = false;
            if (_columnChromeFadingOut)
            {
                return;
            }

            FitColumns();
        }, DispatcherPriority.Background);
    }

    private void FitColumns()
    {
        if (_columnChromeFadingOut)
        {
            return;
        }

        var fontSize = _grid.FontSize > 0 ? _grid.FontSize : LibraryListFontSize;
        var cellPad = LibraryColumnCellPadX * 2;
        var headerPad = LibraryColumnHeaderPadX * 2;
        var family = _grid.FontFamily ?? new FontFamily("Yu Gothic UI");
        var typeface = new Typeface(family, _grid.FontStyle, _grid.FontWeight, _grid.FontStretch);
        var headerFace = new Typeface(family, _grid.FontStyle, FontWeights.SemiBold, _grid.FontStretch);
        var dpi = 1d;
        if (_grid.IsLoaded)
        {
            try
            {
                dpi = VisualTreeHelper.GetDpi(_grid).PixelsPerDip;
            }
            catch (InvalidOperationException)
            {
            }
        }

        var cache = new Dictionary<string, double>(StringComparer.Ordinal);
        double Measure(string text, Typeface face)
        {
            if (text.Length == 0)
            {
                return 0;
            }

            var key = ReferenceEquals(face, headerFace) ? text + "\u0001" : text;
            if (!cache.TryGetValue(key, out var width))
            {
                // TextBlock は App 既定の Display。Ideal だと日本語グリフが狭く出て CharacterEllipsis する。
                width = MeasurePlaylistText(text, face, fontSize, dpi);
                cache[key] = width;
            }

            return width;
        }

        foreach (var (kind, column) in _columns)
        {
            if (column.Visibility != Visibility.Visible)
            {
                continue;
            }

            if (kind == LibraryFileColumn.Waveform)
            {
                SetColumnPixelWidth(column, LibraryPlaylistWaveform.ColumnWidth);
                continue;
            }

            // 見出しは改行あり。最も長い行の幅で列を決める。
            var headerWidth = 0d;
            foreach (var line in ColumnHeader(kind).Split('\n'))
            {
                headerWidth = Math.Max(headerWidth, Measure(line, headerFace));
            }

            var max = headerWidth + headerPad;
            if (kind == _sortColumn)
            {
                max += LibraryColumnSortPad;
            }

            foreach (var row in _items)
            {
                var text = CellText(row, kind);
                if (text.Length == 0)
                {
                    continue;
                }

                var width = kind == LibraryFileColumn.Name
                    ? FileNameColorMarkerVisual.NameColumnRowWidth(
                        Measure(text, typeface),
                        row.HasFileNameMarker,
                        dpi,
                        cellPad)
                    : Measure(text, typeface) + cellPad;
                if (kind is LibraryFileColumn.Duration or LibraryFileColumn.Loudness)
                {
                    // Duration: ホバー再スナップで末尾ミリ秒が欠けないよう余裕を残す。
                    // Loudness: CharacterEllipsis で実測値が見切れないよう同様に 2px。
                    width += 2;
                }

                if (width > max)
                {
                    max = width;
                }
            }

            if (kind == LibraryFileColumn.Loudness)
            {
                // 未計測は LoudnessText が空。RefreshLoudnessRows は選択を壊さないよう
                // FitColumns を呼ばないので、ここで計測後の表示幅を予約する。
                foreach (var sample in UiStrings.LoudnessColumnWidthSamples())
                {
                    max = Math.Max(max, Measure(sample, typeface) + cellPad + 2);
                }
            }

            var fitted = Math.Clamp(
                Math.Ceiling(max),
                1,
                DesignMetrics.LibraryColumnMaxWidth);
            SetColumnPixelWidth(column, fitted);
        }
    }

    /// <summary>
    /// プレイリストセルと同じ TextFormattingMode.Display で文字幅を測る。
    /// Ideal（FormattedText 既定）だと CJK が狭く出ることがある。
    /// </summary>
    internal static double MeasurePlaylistText(
        string text,
        Typeface typeface,
        double fontSize,
        double pixelsPerDip)
    {
        if (text.Length == 0 || fontSize <= 0)
        {
            return 0;
        }

        var dpi = Math.Max(1e-6, pixelsPerDip);
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.Black,
            numberSubstitution: null,
            TextFormattingMode.Display,
            dpi);
        var width = formatted.WidthIncludingTrailingWhitespace;
        if (formatted.OverhangLeading < 0)
        {
            width -= formatted.OverhangLeading;
        }

        if (formatted.OverhangAfter > 0)
        {
            width += formatted.OverhangAfter;
        }

        return width;
    }

    /// <summary>
    /// 幅が同じなら触らない。MinWidth=0 経由の再レイアウトは無駄な列再計算を招くため避ける。
    /// </summary>
    private static bool SetColumnPixelWidth(DataGridColumn column, double width)
    {
        width = Math.Max(0, width);
        if (column.Width.IsAbsolute
            && Math.Abs(column.Width.Value - width) < 0.5
            && Math.Abs(column.MinWidth - width) < 0.5
            && (double.IsPositiveInfinity(column.MaxWidth) || Math.Abs(column.MaxWidth - width) < 0.5))
        {
            return false;
        }

        column.MinWidth = 0;
        column.MaxWidth = width;
        column.Width = new DataGridLength(width, DataGridLengthUnitType.Pixel);
        column.MinWidth = width;
        return true;
    }

    private static string CellText(LibraryFileRow row, LibraryFileColumn column) =>
        LibraryFileList.CellText(row, column);

    private void SetColumnHeader(LibraryFileColumn column, string header)
    {
        if (_sortHeaders.TryGetValue(column, out var sortHeader))
        {
            sortHeader.Label = header;
        }
    }

    /// <summary>
    /// ItemsSource の差し替えで DataGrid が SortDirection を落としても、見出し側の ▼▲ は残す。
    /// </summary>
    private void RefreshSortChrome()
    {
        ApplySortChrome();
        var ticket = ++_sortChromeTicket;
        Dispatcher.BeginInvoke(
            () =>
            {
                if (ticket == _sortChromeTicket)
                {
                    ApplySortChrome();
                }
            },
            DispatcherPriority.ContextIdle);
    }

    private void ApplySortChrome()
    {
        var ascending = _sortDirection == LibrarySortDirection.Ascending;
        var direction = ascending
            ? ListSortDirection.Ascending
            : ListSortDirection.Descending;
        foreach (var pair in _sortHeaders)
        {
            pair.Value.ShowSort(_sortColumn is { } sort && pair.Key == sort, ascending);
        }

        foreach (var item in _columns)
        {
            var next = _sortColumn is { } sort && item.Key == sort
                ? (ListSortDirection?)direction
                : null;
            if (!Equals(item.Value.SortDirection, next))
            {
                item.Value.SortDirection = next;
            }
        }
    }

    private void Grid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        var column = ColumnFromGrid(e.Column);
        if (_sortColumn == column)
        {
            _sortDirection = _sortDirection == LibrarySortDirection.Ascending
                ? LibrarySortDirection.Descending
                : LibrarySortDirection.Ascending;
        }
        else
        {
            _sortColumn = column;
            _sortDirection = column is LibraryFileColumn.Duration
                or LibraryFileColumn.SampleRate
                or LibraryFileColumn.BitDepth
                or LibraryFileColumn.Channels
                or LibraryFileColumn.BitRate
                or LibraryFileColumn.Size
                or LibraryFileColumn.Date
                or LibraryFileColumn.Track
                or LibraryFileColumn.Disc
                or LibraryFileColumn.Year
                or LibraryFileColumn.Jacket
                ? LibrarySortDirection.Descending
                : LibrarySortDirection.Ascending;
        }

        ApplySortDirectionToColumns();

        RefreshSortChrome();
        var active = SelectedSession;
        var selected = SelectedSessions;
        _rows = SortRows(_rows);
        LibraryFileList.ApplyGroupKeys(_rows, _group);
        BindRows(active, selected);
        NotifySortChanged();
    }

    private void NotifySortChanged() =>
        SortChanged?.Invoke(this, new LibraryPlaylistSort(_sortColumn, _sortDirection));

    private void ApplySortDirectionToColumns()
    {
        foreach (var item in _columns)
        {
            item.Value.SortDirection = item.Key == _sortColumn
                ? _sortDirection == LibrarySortDirection.Ascending
                    ? System.ComponentModel.ListSortDirection.Ascending
                    : System.ComponentModel.ListSortDirection.Descending
                : null;
        }
    }

    private LibraryFileColumn ColumnFromGrid(DataGridColumn column)
    {
        foreach (var pair in _columns)
        {
            if (ReferenceEquals(pair.Value, column))
            {
                return pair.Key;
            }
        }

        return LibraryFileColumn.Name;
    }

    private void GroupCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_groupCombo.SelectedValue is not LibraryFileGroup group || group == _group)
        {
            return;
        }

        _group = group;
        var active = SelectedSession;
        var selected = SelectedSessions;
        LibraryFileList.ApplyGroupKeys(_rows, _group);
        SyncGroupChrome();
        BindRows(active, selected);
        InvalidateGroupJackets();
        _ = EnsureGroupArtworkAsync();
        GroupChanged?.Invoke(this, group);
    }

    /// <summary>保存済みの列ソート。起動時に使い、変更は通知しない。</summary>
    public void SetSort(LibraryPlaylistSort sort)
    {
        if (_sortColumn == sort.Column
            && (sort.Column is null || _sortDirection == sort.Direction))
        {
            return;
        }

        _sortColumn = sort.Column;
        if (sort.Column is not null)
        {
            _sortDirection = sort.Direction;
        }

        ApplySortDirectionToColumns();
        RefreshSortChrome();
        if (_rows.Count == 0)
        {
            return;
        }

        var active = SelectedSession;
        var selected = SelectedSessions;
        _rows = SortRows(_rows);
        LibraryFileList.ApplyGroupKeys(_rows, _group);
        BindRows(active, selected);
    }

    private void ShuffleLatch_Click(object sender, RoutedEventArgs e)
    {
        if (_shuffleBusy)
        {
            return;
        }

        SetShuffleEnabled(!_shuffleEnabled);
    }

    private void AutoPlayLatch_Click(object sender, RoutedEventArgs e)
    {
        if (_autoPlayBusy)
        {
            return;
        }

        SetAutoPlayEnabled(!_autoPlayEnabled, raise: true);
    }

    private void CheckSelectLatch_Click(object sender, RoutedEventArgs e)
    {
        if (_checkSelectBusy)
        {
            return;
        }

        SetCheckSelectMode(!_checkSelectMode);
    }

    private void AutoPlayNextLatch_Click(object sender, RoutedEventArgs e)
    {
        if (_autoPlayNextBusy)
        {
            return;
        }

        SetAutoPlayNextEnabled(!_autoPlayNextEnabled);
    }

    private bool TryPatchBoundRows(IReadOnlyList<LibraryFileRow> rows)
    {
        if (_playlistSearchGroups.Count > 0
            || _grid.ItemsSource is null
            || _items.Count != rows.Count)
        {
            return false;
        }

        for (var i = 0; i < rows.Count; i++)
        {
            if (!ReferenceEquals(_items[i].Tag, rows[i].Tag))
            {
                return false;
            }
        }

        var keepFocus = _restoreListFocus || _grid.IsKeyboardFocusWithin;
        for (var i = 0; i < rows.Count; i++)
        {
            if (LibraryFileList.SameContent(_items[i], rows[i]))
            {
                continue;
            }

            var previous = _items[i];
            var wasSelected = previous.IsPlaylistSelected;
            var wasCurrent = ReferenceEquals(_grid.SelectedItem, previous)
                || ReferenceEquals(_playlistCaretRow, previous);
            _playlistSelectedRows.Remove(previous);
            previous.IsPlaylistSelected = false;
            _items[i] = rows[i];
            if (wasSelected)
            {
                SetPlaylistRowSelected(_items[i], true);
            }

            if (wasCurrent)
            {
                SyncPlaylistCaretToGrid(_items[i]);
            }
        }

        _rows = [.. _items];
        if (keepFocus)
        {
            EnsureListFocused();
        }

        return true;
    }

    private sealed record GroupOption(LibraryFileGroup Group, string Label);
}
