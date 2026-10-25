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
    private void ConfigureGrid()
    {
        AddGroupSpacerColumn();
        AddCheckColumn();
        AddNameColumn();
        AddColumn(LibraryFileColumn.Title, nameof(LibraryFileRow.Title));
        AddColumn(LibraryFileColumn.Album, nameof(LibraryFileRow.Album));
        AddColumn(LibraryFileColumn.Artist, nameof(LibraryFileRow.Artist));
        AddColumn(LibraryFileColumn.Composer, nameof(LibraryFileRow.Composer));
        AddColumn(
            LibraryFileColumn.Duration,
            nameof(LibraryFileRow.DurationText),
            right: true,
            trim: false);
        AddColumn(LibraryFileColumn.Track, nameof(LibraryFileRow.Track), right: true);
        AddColumn(LibraryFileColumn.Disc, nameof(LibraryFileRow.Disc), right: true);
        AddColumn(LibraryFileColumn.Year, nameof(LibraryFileRow.Year), right: true);
        AddColumn(LibraryFileColumn.Genre, nameof(LibraryFileRow.Genre));
        AddColumn(LibraryFileColumn.Date, nameof(LibraryFileRow.DateText));
        AddColumn(LibraryFileColumn.Comment, nameof(LibraryFileRow.Comment));
        AddColumn(LibraryFileColumn.ParentFolder, nameof(LibraryFileRow.ParentFolder));
        AddWaveformColumn();
        AddColumn(LibraryFileColumn.AlbumArtist, nameof(LibraryFileRow.AlbumArtist));
        AddColumn(LibraryFileColumn.Kind, nameof(LibraryFileRow.Kind));
        AddColumn(
            LibraryFileColumn.SampleRate,
            nameof(LibraryFileRow.SampleRateText),
            right: true,
            nonDefaultBinding: nameof(LibraryFileRow.SampleRateNonDefault));
        AddColumn(
            LibraryFileColumn.BitDepth,
            nameof(LibraryFileRow.BitDepthText),
            right: true,
            nonDefaultBinding: nameof(LibraryFileRow.BitDepthNonDefault));
        AddColumn(
            LibraryFileColumn.Channels,
            nameof(LibraryFileRow.ChannelsText),
            right: true,
            nonDefaultBinding: nameof(LibraryFileRow.ChannelsNonDefault));
        AddColumn(LibraryFileColumn.BitRate, nameof(LibraryFileRow.BitRateText), right: true);
        AddColumn(LibraryFileColumn.VideoSize, nameof(LibraryFileRow.VideoSizeText), right: true);
        AddColumn(LibraryFileColumn.FrameRate, nameof(LibraryFileRow.FrameRateText), right: true);
        AddColumn(LibraryFileColumn.VideoCodec, nameof(LibraryFileRow.VideoCodec));
        AddColumn(LibraryFileColumn.AudioCodec, nameof(LibraryFileRow.AudioCodec));
        AddColumn(
            LibraryFileColumn.Loudness,
            nameof(LibraryFileRow.LoudnessText),
            right: true,
            loudnessTone: true);
        AddColumn(LibraryFileColumn.Size, nameof(LibraryFileRow.SizeText), right: true);
        AddColumn(LibraryFileColumn.Folder, nameof(LibraryFileRow.Folder));
        AddColumn(LibraryFileColumn.Jacket, nameof(LibraryFileRow.JacketText));

        if (_columns.TryGetValue(LibraryFileColumn.Album, out var albumColumn))
        {
            albumColumn.SortDirection = System.ComponentModel.ListSortDirection.Ascending;
        }

        RefreshSortChrome();

        _grid.AutoGenerateColumns = false;
        _grid.IsReadOnly = true;
        ImeComposition.Disable(_grid);
        // 上段 0–9 は波形の割合ジャンプ。行の TextSearch に吸われてフォーカスがリストへ戻るのを防ぐ。
        _grid.IsTextSearchEnabled = false;
        _grid.CanUserAddRows = false;
        _grid.CanUserDeleteRows = false;
        _grid.CanUserReorderColumns = true;
        _grid.CanUserSortColumns = true;
        _grid.CanUserResizeRows = false;
        _grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        _grid.GridLinesVisibility = DataGridGridLinesVisibility.None;
        _grid.HorizontalGridLinesBrush = Brushes.Transparent;
        _grid.VerticalGridLinesBrush = Brushes.Transparent;
        _grid.SelectionUnit = DataGridSelectionUnit.FullRow;
        _grid.SelectionMode = DataGridSelectionMode.Extended;
        // 列再レイアウトで CurrentItem が先頭へ動いても、選択シアンを追従させない。
        _grid.IsSynchronizedWithCurrentItem = false;
        _grid.ClipboardCopyMode = DataGridClipboardCopyMode.None;
        BindLibraryCopyCommand(_grid, LibraryPane.List);
        // 1000 行超を全部実体化すると ↓ リピートと列幅再計算が止まる。グループ時も仮想化する。
        _grid.EnableRowVirtualization = true;
        VirtualizingPanel.SetIsVirtualizing(_grid, true);
        VirtualizingPanel.SetIsVirtualizingWhenGrouping(_grid, true);
        VirtualizingPanel.SetVirtualizationMode(_grid, VirtualizationMode.Standard);
        VirtualizingPanel.SetScrollUnit(_grid, ScrollUnit.Item);
        _grid.RowHeaderWidth = 0;
        _grid.MinRowHeight = 24;
        _grid.VerticalContentAlignment = VerticalAlignment.Center;
        _grid.BorderThickness = new Thickness(0);
        _grid.UseLayoutRounding = true;
        _grid.SnapsToDevicePixels = true;
        _grid.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        _grid.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _grid.Background = Brushes.Transparent;
        _grid.FontSize = LibraryListFontSize;
        _grid.SetResourceReference(ForegroundProperty, "PrimaryForeBrush");
        _grid.Sorting += Grid_Sorting;
        _grid.ColumnReordered += Grid_ColumnReordered;
        _grid.SelectionChanged += Grid_SelectionChanged;
        _grid.PreviewMouseLeftButtonDown += Grid_PreviewMouseLeftButtonDown;
        _grid.MouseDoubleClick += Grid_MouseDoubleClick;
        RebuildListContextMenu();

        var headerFactory = new FrameworkElementFactory(typeof(LibraryGroupHeader));
        headerFactory.SetValue(DockPanel.DockProperty, Dock.Top);

        var jacket = new FrameworkElementFactory(typeof(LibraryGroupJacketImage));
        jacket.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        jacket.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
        jacket.SetValue(Panel.ZIndexProperty, 1);
        jacket.SetValue(UIElement.IsHitTestVisibleProperty, false);

        var items = new FrameworkElementFactory(typeof(ItemsPresenter));
        items.SetValue(Panel.ZIndexProperty, 0);

        var stage = new FrameworkElementFactory(typeof(Grid));
        stage.SetValue(FrameworkElement.ClipToBoundsProperty, true);
        stage.AppendChild(items);
        stage.AppendChild(jacket);

        var body = new FrameworkElementFactory(typeof(DockPanel));
        body.SetValue(DockPanel.LastChildFillProperty, true);
        body.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 10));
        body.SetValue(FrameworkElement.ClipToBoundsProperty, false);
        body.AppendChild(headerFactory);
        body.AppendChild(stage);

        var template = new ControlTemplate(typeof(GroupItem)) { VisualTree = body };
        var container = new Style(typeof(GroupItem));
        container.Setters.Add(new Setter(Control.TemplateProperty, template));
        container.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        container.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));

        container.Setters.Add(new Setter(FrameworkElement.ClipToBoundsProperty, false));

        _grid.GroupStyle.Clear();
        _grid.GroupStyle.Add(new GroupStyle
        {
            ContainerStyle = container,
            HidesIfEmpty = true,
        });
        SyncGroupChrome();
    }

    /// <summary>グループ左のジャケット幅（余白込み）。先頭のスペーサ列と一致させる。</summary>
    internal static double GroupJacketColumnWidth =>
        DesignMetrics.LibraryGroupJacketSize + 16;

    internal DataGridHeadersVisibility HeadersVisibility => _grid.HeadersVisibility;

    internal Visibility GroupSpacerVisibility => _groupSpacer.Visibility;

    internal int FrozenColumnCount => _grid.FrozenColumnCount;

    /// <summary>
    /// グループ左のジャケット枠。実ジャケットが1件も無いときは出さない。
    /// </summary>
    private bool ShowsGroupJackets
    {
        get
        {
            if (_group == LibraryFileGroup.None)
            {
                return false;
            }

            foreach (var row in _rows)
            {
                if (row.HasArtwork)
                {
                    return true;
                }
            }

            return false;
        }
    }

}
