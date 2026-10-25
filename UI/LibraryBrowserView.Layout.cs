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
    private void BuildLayout()
    {
        var treeWidth = DesignMetrics.LibraryExplorerWidth;
        _sidePanesRememberedWidth = treeWidth;
        _treeColumn.Width = new GridLength(treeWidth);
        _treeColumn.MinWidth = DesignMetrics.LibraryExplorerMinWidth;
        _treeColumn.MaxWidth = DesignMetrics.LibraryExplorerMaxWidth;
        _treeSplitterColumn.Width = new GridLength(DesignMetrics.LibraryExplorerSplitterWidth);
        _root.ColumnDefinitions.Add(_treeColumn);
        _root.ColumnDefinitions.Add(_treeSplitterColumn);
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        ApplyExplorerStyle();
        ApplyFavoritesStyle();
        ImeComposition.Disable(_folderTree);
        ImeComposition.Disable(_favoritesList);
        // ルート並び替え／外部フォルダ登録。ツリー本体に AllowDrop。
        _folderTree.AllowDrop = true;
        _folderTree.DragOver += FolderTree_DragOver;
        _folderTree.Drop += FolderTree_Drop;
        _folderTree.SelectedItemChanged += FolderTree_SelectedItemChanged;
        _folderTree.PreviewMouseLeftButtonDown += FolderTree_PreviewMouseLeftButtonDown;
        _folderTree.PreviewMouseMove += FolderTree_PreviewMouseMove;
        _folderTree.PreviewMouseLeftButtonUp += FolderTree_PreviewMouseLeftButtonUp;
        _folderTree.PreviewMouseRightButtonDown += FolderTree_PreviewMouseRightButtonDown;
        _folderTree.PreviewKeyDown += FolderTree_PreviewKeyDown;
        _folderTree.PreviewTextInput += FolderTree_PreviewTextInput;
        // 上段 0–9 は割合ジャンプ。ツリーのインクリメンタル検索には使わない。
        _folderTree.IsTextSearchEnabled = false;
        BindLibraryCopyCommand(_folderTree, LibraryPane.Explorer);
        _folderTree.CommandBindings.Add(new CommandBinding(ApplicationCommands.Delete, (_, e) => e.Handled = true));
        BuildExplorerRoots();
        RebuildExplorerContextMenu();

        ApplyPaneTitleStyle(_explorerLabel);
        ApplyPaneTitleStyle(_favoritesLabel);
        ApplyPaneTitleStyle(_playlistLabel, new Thickness(0, 0, 8, 0));
        _favoritesList.ItemsSource = _favorites;
        _favoritesList.ItemTemplate = FavoriteRowTemplate();
        _favoritesList.SelectionMode = SelectionMode.Extended;
        _favoritesList.BorderThickness = new Thickness(0);
        _favoritesList.Background = Brushes.Transparent;
        _favoritesList.Padding = new Thickness(4, 2, 4, 6);
        _favoritesList.FontSize = 11;
        _favoritesList.SetResourceReference(ForegroundProperty, "PrimaryForeBrush");
        // 上段 0–9 は割合ジャンプ。名前の先頭一致ジャンプはしない。
        _favoritesList.IsTextSearchEnabled = false;
        _favoritesList.PreviewKeyDown += FavoritesList_PreviewKeyDown;
        _favoritesList.SelectionChanged += FavoritesList_SelectionChanged;
        _favoritesList.PreviewMouseLeftButtonDown += FavoritesList_PreviewMouseLeftButtonDown;
        _favoritesList.PreviewMouseMove += FavoritesList_PreviewMouseMove;
        _favoritesList.PreviewMouseLeftButtonUp += (_, _) => _favoritesDragStart = null;
        BindLibraryCopyCommand(_favoritesList, LibraryPane.Favorites);
        _favoritesList.AllowDrop = true;
        _favoritesList.PreviewDragOver += FavoritesList_PreviewDragOver;
        _favoritesList.Drop += FavoritesList_Drop;
        RebuildFavoritesContextMenu();

        var treePane = new Grid();
        _explorerTreeRow.MinHeight = DesignMetrics.LibraryFavoritesMinHeight;
        _favoritesRow.MinHeight = DesignMetrics.LibraryFavoritesMinHeight;
        treePane.RowDefinitions.Add(_explorerTreeRow);
        treePane.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(DesignMetrics.LibraryExplorerSplitterWidth),
        });
        treePane.RowDefinitions.Add(_favoritesRow);
        treePane.Background = Brushes.Transparent;
        treePane.ClipToBounds = false;

        var explorerPane = new DockPanel { Background = Brushes.Transparent };
        var explorerHeader = new DockPanel { LastChildFill = true };
        var explorerSearch = CreateSearchEditor(_explorerSearchBox, _explorerSearchHint);
        explorerSearch.Width = DesignMetrics.From96(120);
        explorerSearch.MinWidth = DesignMetrics.From96(72);
        explorerSearch.Margin = new Thickness(0, 6, 8, 6);
        explorerSearch.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(explorerSearch, Dock.Right);
        explorerHeader.Children.Add(explorerSearch);
        _explorerLabel.VerticalAlignment = VerticalAlignment.Center;
        explorerHeader.Children.Add(_explorerLabel);
        DockPanel.SetDock(explorerHeader, Dock.Top);
        explorerPane.Children.Add(explorerHeader);
        var treeBody = new Grid { Background = Brushes.Transparent };
        treeBody.Children.Add(_folderTree);
        var treeOverlay = new Grid
        {
            Background = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        BuildExplorerTypeaheadToast(treeBody);
        treeOverlay.Children.Add(_explorerTypeaheadToast);
        Panel.SetZIndex(_explorerTypeaheadToast, 20);
        var treeStack = new Grid { Background = Brushes.Transparent };
        treeStack.Children.Add(treeBody);
        treeStack.Children.Add(treeOverlay);
        explorerPane.Children.Add(treeStack);
        var treeHost = new Grid { Background = Brushes.Transparent };
        // 見出し上へのドロップもルート登録として受け取る。
        treeHost.AllowDrop = true;
        treeHost.DragOver += FolderTree_DragOver;
        treeHost.Drop += FolderTree_Drop;
        _explorerDropHost = treeHost;
        treeHost.Children.Add(explorerPane);
        treeHost.Children.Add(_treeFocusLine);
        Grid.SetRow(treeHost, 0);
        treePane.Children.Add(treeHost);

        var favoritesPane = new DockPanel();
        DockPanel.SetDock(_favoritesLabel, Dock.Top);
        favoritesPane.Children.Add(_favoritesLabel);
        favoritesPane.Children.Add(_favoritesList);
        var favoritesHost = new Grid { Background = Brushes.Transparent };
        favoritesHost.Children.Add(favoritesPane);
        favoritesHost.Children.Add(_favoritesFocusLine);
        Grid.SetRow(favoritesHost, 2);
        treePane.Children.Add(favoritesHost);

        var favoritesSplitter = _favoritesSplitter;
        favoritesSplitter.Height = DesignMetrics.LibraryFavoritesSplitterHitHeight;
        favoritesSplitter.HorizontalAlignment = HorizontalAlignment.Stretch;
        favoritesSplitter.VerticalAlignment = VerticalAlignment.Top;
        favoritesSplitter.ResizeBehavior = GridResizeBehavior.PreviousAndNext;
        favoritesSplitter.ResizeDirection = GridResizeDirection.Rows;
        favoritesSplitter.Cursor = Cursors.SizeNS;
        favoritesSplitter.SnapsToDevicePixels = true;
        ApplyLibrarySplitterChrome(favoritesSplitter);
        favoritesSplitter.DragCompleted += FavoritesSplitter_DragCompleted;
        Grid.SetRow(favoritesSplitter, 1);
        Panel.SetZIndex(favoritesSplitter, 8);
        treePane.Children.Add(favoritesSplitter);

        Grid.SetColumn(treePane, 0);
        _root.Children.Add(treePane);

        _groupLabel.VerticalAlignment = VerticalAlignment.Center;
        _groupLabel.Margin = new Thickness(0, 0, 8, 0);
        _groupLabel.FontSize = 11;
        _groupLabel.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryForeBrush");
        _groupCombo.MinWidth = DesignMetrics.From96(140);
        _groupCombo.Height = DesignMetrics.AudioInputHeight;
        _groupCombo.FontSize = 11;
        _groupCombo.VerticalContentAlignment = VerticalAlignment.Center;
        _groupCombo.SetResourceReference(StyleProperty, "LibraryComboBoxStyle");
        _groupCombo.SelectionChanged += GroupCombo_SelectionChanged;

        _playlistGroupHost.VerticalAlignment = VerticalAlignment.Center;
        _playlistGroupHost.Children.Add(_groupLabel);
        _playlistGroupHost.Children.Add(_groupCombo);

        var bar = new DockPanel { Margin = new Thickness(8, 6, 8, 6) };
        DockPanel.SetDock(_playlistGroupHost, Dock.Right);
        bar.Children.Add(_playlistGroupHost);
        var playlistSearch = CreateSearchEditor(_playlistSearchBox, _playlistSearchHint);
        playlistSearch.Width = DesignMetrics.From96(180);
        playlistSearch.MinWidth = DesignMetrics.From96(96);
        playlistSearch.Margin = new Thickness(8, 0, 8, 0);
        playlistSearch.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(playlistSearch, Dock.Right);
        bar.Children.Add(playlistSearch);

        _shuffleCheck.Style = TryFindResource("DarkCheckBoxStyle") as Style;
        _shuffleCheck.Content = UiStrings.LabelLibraryShuffle;
        _shuffleCheck.IsChecked = false;
        _shuffleCheck.Focusable = false;
        _shuffleCheck.VerticalAlignment = VerticalAlignment.Center;
        _shuffleCheck.FontSize = 11;
        _shuffleCheck.Margin = new Thickness(8, 0, 0, 0);
        _shuffleCheck.SetResourceReference(Control.ForegroundProperty, "PrimaryForeBrush");
        _shuffleCheck.Checked += ShuffleCheck_Changed;
        _shuffleCheck.Unchecked += ShuffleCheck_Changed;
        DockPanel.SetDock(_shuffleCheck, Dock.Right);
        bar.Children.Add(_shuffleCheck);

        // Dock.Right は先に足したものが右。Shuffle → AutoNext → AutoPlay で
        // 見た目は Auto Play | Auto Next | Random。Auto Loop はステータスバーへ。
        _autoPlayNextCheck.Style = TryFindResource("DarkCheckBoxStyle") as Style;
        _autoPlayNextCheck.Content = UiStrings.LabelLibraryAutoPlayNext;
        _autoPlayNextCheck.IsChecked = false;
        _autoPlayNextCheck.Focusable = false;
        _autoPlayNextCheck.VerticalAlignment = VerticalAlignment.Center;
        _autoPlayNextCheck.FontSize = 11;
        _autoPlayNextCheck.Margin = new Thickness(8, 0, 0, 0);
        _autoPlayNextCheck.SetResourceReference(Control.ForegroundProperty, "PrimaryForeBrush");
        _autoPlayNextCheck.Checked += AutoPlayNextCheck_Changed;
        _autoPlayNextCheck.Unchecked += AutoPlayNextCheck_Changed;
        DockPanel.SetDock(_autoPlayNextCheck, Dock.Right);
        bar.Children.Add(_autoPlayNextCheck);

        _autoPlayCheck.Style = TryFindResource("DarkCheckBoxStyle") as Style;
        _autoPlayCheck.Content = UiStrings.LabelLibraryAutoPlay;
        _autoPlayCheck.IsChecked = true;
        _autoPlayCheck.Focusable = false;
        _autoPlayCheck.VerticalAlignment = VerticalAlignment.Center;
        _autoPlayCheck.FontSize = 11;
        _autoPlayCheck.Margin = new Thickness(8, 0, 0, 0);
        _autoPlayCheck.SetResourceReference(Control.ForegroundProperty, "PrimaryForeBrush");
        _autoPlayCheck.Checked += AutoPlayCheck_Changed;
        _autoPlayCheck.Unchecked += AutoPlayCheck_Changed;
        DockPanel.SetDock(_autoPlayCheck, Dock.Right);
        bar.Children.Add(_autoPlayCheck);

        _playlistLabel.VerticalAlignment = VerticalAlignment.Center;
        _playlistLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        bar.Children.Add(_playlistLabel);

        var list = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        list.Children.Add(bar);
        list.Children.Add(_grid);

        var drift = new TransformGroup();
        drift.Children.Add(_glowScale);
        drift.Children.Add(_glowTranslate);
        _glowHost.RenderTransform = drift;
        _glowHost.RenderTransformOrigin = new Point(0.5, 0.5);
        _glowHost.CacheMode = CreateGlowBitmapCache();
        _glowHost.IsHitTestVisible = false;
        _glowHost.SnapsToDevicePixels = false;
        _glowHost.UseLayoutRounding = false;
        _glowHost.Margin = new Thickness(-GlowDriftBleed);
        _glowHost.Visibility = Visibility.Collapsed;
        _glowHost.Children.Add(_glowFrom);
        _glowHost.Children.Add(_glowTo);

        _veil.IsHitTestVisible = false;
        _veil.Visibility = Visibility.Collapsed;
        ApplyGlowVeil();

        var listPane = new Grid { Background = Brushes.Transparent };
        listPane.Children.Add(list);
        listPane.Children.Add(_listFocusLine);
        Grid.SetColumn(listPane, 2);
        _root.Children.Add(listPane);

        var splitter = _explorerSplitter;
        splitter.Width = DesignMetrics.LibrarySplitterHitThickness;
        splitter.HorizontalAlignment = HorizontalAlignment.Left;
        splitter.VerticalAlignment = VerticalAlignment.Stretch;
        splitter.ResizeBehavior = GridResizeBehavior.PreviousAndNext;
        splitter.ResizeDirection = GridResizeDirection.Columns;
        splitter.Cursor = Cursors.SizeWE;
        splitter.SnapsToDevicePixels = true;
        ApplyLibrarySplitterChrome(splitter);
        splitter.DragCompleted += ExplorerSplitter_DragCompleted;
        Grid.SetColumn(splitter, 1);
        Panel.SetZIndex(splitter, 8);
        _root.Children.Add(splitter);
        BuildTransferToast();

        Grid.SetColumnSpan(_glowHost, _root.ColumnDefinitions.Count);
        Grid.SetColumnSpan(_veil, _root.ColumnDefinitions.Count);
        _root.Children.Insert(0, _veil);
        _root.Children.Insert(0, _glowHost);
        _root.ClipToBounds = false;
        _root.SetResourceReference(Panel.BackgroundProperty, "SurfaceBackBrush");

        _host.ClipToBounds = true;
        _host.Children.Add(_root);
        Content = _host;
        SetResourceReference(BackgroundProperty, "SurfaceBackBrush");
        ApplyLibraryScrollBarStyle();
        LibraryScrollReveal.Attach(_folderTree);
        LibraryScrollReveal.Attach(_favoritesList);
        LibraryScrollReveal.Attach(_grid);
        LibrarySplitterReveal.Attach(_root, _explorerSplitter, _favoritesSplitter, _treeColumn, _explorerTreeRow);
    }

}
