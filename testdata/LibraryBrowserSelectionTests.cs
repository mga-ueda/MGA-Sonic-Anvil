using System.Globalization;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibraryBrowserSelectionTests
{
    [Fact]
    public void SetSessions_SameList_KeepsItemsSourceAndSelection()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var first = Session("aaa.wav");
            var second = Session("bbb.wav");
            var view = new LibraryBrowserView();
            var activations = 0;
            view.SessionActivated += (_, _) => activations++;
            view.SetSessions([first, second], second, [second]);
            Flush();
            activations = 0;
            var source = view.BoundItemsSource;
            Assert.Same(second, view.SelectedSession);

            view.SetSessions([first, second], second, [second]);
            Flush();
            Assert.Same(source, view.BoundItemsSource);
            Assert.Same(second, view.SelectedSession);
            Assert.Equal(0, activations);
        });
    }

    [Fact]
    public void SetSessions_Empty_ThenAppend_DoesNotKeepOldRows()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var first = Session("aaa.wav");
            var second = Session("bbb.wav");
            var third = Session("ccc.wav");
            var view = new LibraryBrowserView();
            view.SetSessions([first, second], first, [first]);
            Flush();
            Assert.Equal(2, view.BoundRowCount);

            view.SetSessions([], null);
            Flush();
            Assert.Equal(0, view.BoundRowCount);

            view.AppendSession(third, select: true);
            Flush();
            Assert.Equal(1, view.BoundRowCount);
            Assert.Same(third, view.SelectedSession);
        });
    }

    [Fact]
    public void SetSessions_RemoveSome_GroupedFolder_KeepsSelectionAndSelectAll()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var keepA = Session(Path.Combine("Alpha", "01.wav"));
            var dropB = Session(Path.Combine("Alpha", "02.wav"));
            var keepC = Session(Path.Combine("Beta", "03.wav"));
            var dropD = Session(Path.Combine("Beta", "04.wav"));
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 420,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetGroup(LibraryFileGroup.Folder);
            view.SetSessions([keepA, dropB, keepC, dropD], dropB, [dropB, dropD]);
            Flush();
            view.UpdateLayout();
            Flush();
            view.FocusList();
            Flush();
            Assert.True(view.IsListKeyboardFocused);

            // RemoveLibrarySelectedFromList と同じ順：SetSessions の直後に FocusList
            //（Flush 前。FocusList が ensure 世代を進めてもキャレットが残ること）。
            view.SetSessions([keepA, keepC], keepC, [keepC]);
            view.FocusList();
            Flush();
            view.UpdateLayout();
            Flush();

            Assert.Same(keepC, view.SelectedSession);
            Assert.Equal([keepC], view.SelectedSessions);
            Assert.True(view.IsListKeyboardFocused);
            var grid = Assert.IsAssignableFrom<DataGrid>(view.FileGrid);
            Assert.NotNull(grid.SelectedItem);
            Assert.Same(keepC, (grid.SelectedItem as LibraryFileRow)?.Tag as DocumentSession);
            Assert.Same(keepC, (grid.CurrentItem as LibraryFileRow)?.Tag as DocumentSession);

            view.SelectAllRows();
            Flush();
            Assert.Equal(2, view.SelectedSessions.Length);
            Assert.Contains(keepA, view.SelectedSessions);
            Assert.Contains(keepC, view.SelectedSessions);

            view.SetSessions([keepA, keepC], keepA, [keepA]);
            Flush();
            view.FocusList();
            Flush();
            view.MoveSelectionToEdge(1, extend: true);
            Flush();
            view.UpdateLayout();
            Flush();
            Assert.Equal(2, view.SelectedSessions.Length);
            Assert.Contains(keepA, view.SelectedSessions);
            Assert.Contains(keepC, view.SelectedSessions);
            window.Close();
        });
    }

    [Fact]
    public void SetSessions_ClearLatterHalf_MoveSelectionStaysOnBoundRows()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var sessions = new DocumentSession[24];
            for (var i = 0; i < sessions.Length; i++)
            {
                var folder = i < 12 ? "Alpha" : "Beta";
                sessions[i] = Session(Path.Combine(folder, $"{i:00}.wav"));
            }

            var keep = sessions.Take(12).ToArray();
            var drop = sessions.Skip(12).ToArray();
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 360,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetGroup(LibraryFileGroup.Folder);
            view.SetSessions(sessions, drop[0], drop);
            Flush();
            view.UpdateLayout();
            Flush();
            view.FocusList();
            Flush();

            // 後半クリア直後に FocusList → ↑↓（実機の Delete 後操作）。
            view.SetSessions(keep, keep[^1], [keep[^1]]);
            view.FocusList();
            Flush();
            view.UpdateLayout();
            Flush();

            var grid = Assert.IsAssignableFrom<DataGrid>(view.FileGrid);
            AssertBoundSelection(view, grid, keep[^1]);

            view.MoveSelection(-1);
            Flush();
            view.UpdateLayout();
            Flush();
            AssertBoundSelection(view, grid, keep[^2]);

            view.MoveSelection(1);
            Flush();
            view.UpdateLayout();
            Flush();
            AssertBoundSelection(view, grid, keep[^1]);

            view.MoveSelection(1, wrap: false);
            Flush();
            AssertBoundSelection(view, grid, keep[^1]);

            view.MoveSelection(-3);
            Flush();
            view.UpdateLayout();
            Flush();
            AssertBoundSelection(view, grid, keep[^4]);
            window.Close();
        });
    }

    private static void AssertBoundSelection(
        LibraryBrowserView view,
        DataGrid grid,
        DocumentSession expected)
    {
        Assert.Same(expected, view.SelectedSession);
        Assert.Equal([expected], view.SelectedSessions);
        var selected = Assert.IsType<LibraryFileRow>(grid.SelectedItem);
        Assert.Same(expected, selected.Tag as DocumentSession);
        Assert.True(selected.IsPlaylistSelected);
        Assert.True(grid.Items.Contains(selected));
        var current = Assert.IsType<LibraryFileRow>(grid.CurrentItem);
        Assert.Same(expected, current.Tag as DocumentSession);
        Assert.Same(selected, current);
    }

    [Fact]
    public void AppendSessions_MultipleUnselected_ClearsDeferActivateSoGridClickWorks()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var first = Session("aaa.wav");
            var second = Session("bbb.wav");
            var third = Session("ccc.wav");
            var fourth = Session("ddd.wav");
            var extra = Session("eee.wav");
            var view = new LibraryBrowserView();
            DocumentSession? activated = null;
            view.SessionActivated += (_, session) => activated = session;
            view.SetSessions([first], first, [first]);
            Flush();
            Assert.False(view.IsActivateDeferred);

            // 先頭だけ選択、残りは非選択追加（フォルダ／複数ドロップの flush と同じ形）。
            view.AppendSessions([second, third, fourth], select: second);
            Flush();
            Assert.False(view.IsActivateDeferred);

            activated = null;
            view.SimulatePlaylistGridSelectForTests(fourth);
            Flush();
            Assert.Same(fourth, activated);
            Assert.Same(fourth, view.SelectedSession);

            // 全件非選択追加でも残留しない。
            view.AppendSessions([extra], select: null);
            Flush();
            Assert.False(view.IsActivateDeferred);

            // ソート位置への連続 Insert（placeByCurrentSort）も深さを落とす。
            view.InsertSessionByCurrentSort(Session("fff.wav"), select: false);
            view.InsertSessionByCurrentSort(Session("ggg.wav"), select: false);
            Flush();
            Assert.False(view.IsActivateDeferred);
        });
    }

    [Fact]
    public void UpdateSessionRow_Artwork_KeepsSelectionAndItemsSource()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var first = Session("aaa.wav");
            var second = Session("bbb.wav");
            var view = new LibraryBrowserView();
            var activations = 0;
            view.SessionActivated += (_, _) => activations++;
            view.SetSessions([first, second], second, [second]);
            Flush();
            activations = 0;
            var source = view.BoundItemsSource;
            second.Document.SetArtwork([1, 2, 3, 4]);
            view.UpdateSessionRow(second);
            Flush();
            // ソフト差し替えは ItemsSource を張り直さない（force／複数更新だけ全件載せ替え）。
            Assert.Same(source, view.BoundItemsSource);
            Assert.Same(second, view.SelectedSession);
            // Stabilize の Idle 解除で SessionActivated が1回来ることはある。
            Assert.True(activations <= 1);
        });
    }

    [Fact]
    public void RefreshLoudnessRows_DoesNotRaiseSessionActivated()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var first = Session("aaa.wav");
            var second = Session("bbb.wav");
            var view = new LibraryBrowserView();
            var activations = 0;
            view.SessionActivated += (_, _) => activations++;
            view.SetSessions([first, second], second, [second]);
            Flush();
            activations = 0;

            second.Document.ApplyTags(
                second.Document.Tags.WithFileLoudness(
                    new FileLoudness(-12f, -16f, -9f, 4f, -1f)));
            view.RefreshLoudnessRows([second]);
            Flush();

            Assert.Same(second, view.SelectedSession);
            // ラウドネス列だけの差し替えは再生やり直しの元になる SessionActivated を起こさない。
            Assert.Equal(0, activations);
        });
    }

    [Fact]
    public void UpdateSessionRow_KeepsPlaylistKeyboardFocus()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var first = Session("aaa.wav");
            var second = Session("bbb.wav");
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetSessions([first, second], second, [second]);
            Flush();
            view.UpdateLayout();
            Flush();
            view.FocusList();
            Flush();
            Assert.True(view.IsListKeyboardFocused);
            second.Document.SetArtwork([1, 2, 3, 4]);
            view.UpdateSessionRow(second);
            Flush();
            Assert.True(view.IsListKeyboardFocused);
            Assert.Same(second, view.SelectedSession);
            window.Close();
        });
    }

    [Fact]
    public void SelectSessionQuiet_KeepsPlaylistKeyboardFocus()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var first = Session("aaa.wav");
            var second = Session("bbb.wav");
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetSessions([first, second], first, [first]);
            Flush();
            view.UpdateLayout();
            Flush();
            view.FocusList();
            Flush();
            Assert.True(view.IsListKeyboardFocused);
            view.SelectSessionQuiet(second);
            Flush();
            Assert.True(view.IsListKeyboardFocused);
            Assert.Same(second, view.SelectedSession);
            window.Close();
        });
    }

    [Fact]
    public void GroupJacket_DoesNotStealHits()
    {
        RunSta(() =>
        {
            var jacket = new LibraryGroupJacketImage();
            Assert.False(jacket.IsHitTestVisible);
        });
    }

    [Fact]
    public void GroupJacket_MeasureHeight_IsFaceOnly_NotReflection()
    {
        RunSta(() =>
        {
            var size = DesignMetrics.LibraryGroupJacketSize;
            var bmp = System.Windows.Media.Imaging.BitmapSource.Create(
                1,
                1,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                new byte[] { 32, 32, 255, 255 },
                4);
            bmp.Freeze();

            var jacket = new LibraryGroupJacketImage();
            // Reload 経由だと Owner が要るので、子 Image に直接載せて測る。
            var face = FindDescendants<Image>(jacket).First();
            face.Source = bmp;
            var reflection = FindDescendants<LibraryJacketReflectionView>(jacket).First();
            reflection.Source = bmp;
            reflection.SyncFaceSize(size, size);

            jacket.Measure(new Size(size + 16, double.PositiveInfinity));
            Assert.Equal(size, jacket.DesiredSize.Height, 1);
            Assert.True(
                jacket.DesiredSize.Height + 1 < size + LibraryJacketReflection.GapDip
                    + (size * LibraryJacketReflection.HeightFactor),
                "layout height must exclude the reflection (shadow) band");
        });
    }

    [Fact]
    public void Layout_HasExplorerThenList()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var view = new LibraryBrowserView();
            Assert.Equal(5, view.RootColumnCount);
            Assert.Equal(UiStrings.LibraryExplorerLabel, view.ExplorerPaneTitle);
            Assert.Equal(UiStrings.LibraryFavoritesLabel, view.FavoritesPaneTitle);
            Assert.Equal(UiStrings.LibraryPlaylistLabel, view.PlaylistPaneTitle);
            Assert.Equal(UiStrings.LibraryHeaderStructureLabel, view.HeaderStructurePaneTitle);
            Assert.Equal(Dock.Right, view.PlaylistGroupDock);
            Assert.Equal(DesignMetrics.LibraryFavoritesSplitterHitHeight, view.FavoritesSplitter.Height);
            Assert.Equal(VerticalAlignment.Top, view.FavoritesSplitter.VerticalAlignment);
            Assert.Equal(Cursors.SizeNS, view.FavoritesSplitter.Cursor);
            Assert.Equal(DesignMetrics.LibrarySplitterHitThickness, view.ExplorerSplitter.Width);
            Assert.Equal(HorizontalAlignment.Left, view.ExplorerSplitter.HorizontalAlignment);
            Assert.Equal(Cursors.SizeWE, view.ExplorerSplitter.Cursor);
            Assert.Equal(DesignMetrics.LibrarySplitterHitThickness, view.StructureSplitter.Width);
            Assert.Equal(HorizontalAlignment.Left, view.StructureSplitter.HorizontalAlignment);
            Assert.Equal(Cursors.SizeWE, view.StructureSplitter.Cursor);
            Assert.False(view.ExplorerSplitter.Focusable);
            Assert.False(view.FavoritesSplitter.Focusable);
            Assert.False(view.StructureSplitter.Focusable);
        });
    }

    [Fact]
    public void GroupJacketColumnWidth_MatchesJacketPlusMargins()
    {
        Assert.Equal(DesignMetrics.LibraryGroupJacketSize + 16, LibraryBrowserView.GroupJacketColumnWidth);
        Assert.Equal(
            DesignMetrics.LibraryVisualJacketWidth * 9d / 16d,
            DesignMetrics.LibraryVisualJacketHeight);
        Assert.True(DesignMetrics.LibraryVisualJacketHeight < DesignMetrics.LibraryGroupJacketSize);
    }

    [Fact]
    public void SelectionAndHoverFills_AreTranslucent()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var selected = Assert.IsType<SolidColorBrush>(LibraryBrowserView.CyanSelectionBrush());
            var hover = Assert.IsType<SolidColorBrush>(LibraryBrowserView.GrayHoverBrush());
            Assert.Equal(
                LibraryBrowserView.LibrarySelectionFillAlphaFor(UiThemeService.Current),
                selected.Color.A);
            Assert.Equal(LibraryBrowserView.LibraryHoverFillAlpha, hover.Color.A);
            Assert.True(selected.Color.A < 255);
            Assert.True(hover.Color.A < 255);
            Assert.True(hover.Color.A > LibraryBrowserView.LibrarySelectionFillAlpha);
            Assert.True(hover.Color.A > 0x70);
            var navy = Color.FromRgb(0x1A, 0x90, 0xA8);
            Assert.Equal(
                UiThemePalette.ColorFor(UiTheme.Dark, "PlayerSelectionFillBrush"),
                LibraryBrowserView.LibrarySelectionRgb(navy, UiTheme.Dark));
        });
    }

    [Fact]
    public void ExplorerTypeahead_TThenTa_SelectsTaskNotTest()
    {
        RunSta(() =>
        {
            EnsureTheme();
            // ルート名に t を入れない（部分一致でルート自身がヒットしないように）。
            var root = Path.Combine(Path.GetTempPath(), "mga-lib-" + Guid.NewGuid().ToString("N"));
            var test = Path.Combine(root, "Test");
            var task = Path.Combine(root, "Task");
            Directory.CreateDirectory(test);
            Directory.CreateDirectory(task);
            Window? window = null;
            try
            {
                var view = new LibraryBrowserView();
                view.SetExplorerRoots([root]);
                window = new Window
                {
                    Content = view,
                    Width = 900,
                    Height = 480,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                };
                window.Show();
                Flush();
                var folder = view.ExplorerFirstFolder;
                Assert.NotNull(folder);
                folder.IsExpanded = true;
                Flush();

                Assert.True(view.TryExplorerTypeahead(Key.T, ModifierKeys.None));
                Assert.Equal("t", view.ExplorerTypeaheadQuery);
                Assert.True(view.TryGetSelectedExplorerFolder(out var afterT));
                var afterTName = Path.GetFileName(afterT);
                Assert.True(
                    afterTName.Equals("Test", StringComparison.OrdinalIgnoreCase)
                    || afterTName.Equals("Task", StringComparison.OrdinalIgnoreCase),
                    afterTName);

                Assert.True(view.TryExplorerTypeahead(Key.A, ModifierKeys.None));
                Assert.Equal("ta", view.ExplorerTypeaheadQuery);
                Assert.True(view.TryGetSelectedExplorerFolder(out var afterTa));
                Assert.Equal(Path.GetFullPath(task), Path.GetFullPath(afterTa));
            }
            finally
            {
                window?.Close();
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        });
    }

    [Fact]
    public void ExplorerTypeahead_M_SelectsMusicFolder()
    {
        RunSta(() =>
        {
            EnsureTheme();
            // ルート名に m を入れない（部分一致でルート自身がヒットしないように）。
            var root = Path.Combine(Path.GetTempPath(), "xqa-lib-" + Guid.NewGuid().ToString("N"));
            var music = Path.Combine(root, "Music");
            var other = Path.Combine(root, "Other");
            Directory.CreateDirectory(music);
            Directory.CreateDirectory(other);
            Window? window = null;
            try
            {
                var view = new LibraryBrowserView();
                view.SetExplorerRoots([root]);
                window = new Window
                {
                    Content = view,
                    Width = 900,
                    Height = 480,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                };
                window.Show();
                Flush();
                var folder = view.ExplorerFirstFolder;
                Assert.NotNull(folder);
                folder.IsExpanded = true;
                Flush();

                Assert.True(view.TryExplorerTypeahead(Key.M, ModifierKeys.None));
                Assert.Equal("m", view.ExplorerTypeaheadQuery);
                Assert.True(view.IsExplorerTypeaheadActive);
                Assert.True(view.TryGetSelectedExplorerFolder(out var selected));
                Assert.Equal(Path.GetFullPath(music), Path.GetFullPath(selected));

                Assert.True(view.ClearExplorerTypeahead());
                Assert.False(view.IsExplorerTypeaheadActive);
            }
            finally
            {
                window?.Close();
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        });
    }

    [Fact]
    public void ExplorerSpace_ExpandsThenMovesDown_AndClearsTypeahead()
    {
        RunSta(() =>
        {
            EnsureTheme();
            // ルート名に a／b を入れない。
            var root = Path.Combine(Path.GetTempPath(), "xqt-lib-" + Guid.NewGuid().ToString("N"));
            var alpha = Path.Combine(root, "Alpha");
            var beta = Path.Combine(root, "Beta");
            Directory.CreateDirectory(alpha);
            Directory.CreateDirectory(beta);
            Window? window = null;
            try
            {
                var view = new LibraryBrowserView();
                view.SetExplorerRoots([root]);
                window = new Window
                {
                    Content = view,
                    Width = 900,
                    Height = 480,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                };
                window.Show();
                Flush();
                var folder = view.ExplorerFirstFolder;
                Assert.NotNull(folder);
                folder.IsSelected = true;
                Flush();
                Assert.False(folder.IsExpanded);

                Assert.True(view.TryExplorerSpaceExpandOrMoveDown());
                Flush();
                Assert.True(folder.IsExpanded);
                Assert.True(view.TryGetSelectedExplorerFolder(out var stillRoot));
                Assert.Equal(Path.GetFullPath(root), Path.GetFullPath(stillRoot));

                // 展開済みルートで Space → 一つ下（Alpha）。
                Assert.True(view.TryExplorerSpaceExpandOrMoveDown());
                Assert.True(view.TryGetSelectedExplorerFolder(out var onAlpha));
                Assert.Equal(Path.GetFullPath(alpha), Path.GetFullPath(onAlpha));

                Assert.True(view.TryExplorerTypeahead(Key.B, ModifierKeys.None));
                Assert.True(view.IsExplorerTypeaheadActive);
                Assert.True(view.TryGetSelectedExplorerFolder(out var onBeta));
                Assert.Equal(Path.GetFullPath(beta), Path.GetFullPath(onBeta));

                Assert.True(view.TryExplorerSpaceExpandOrMoveDown());
                Assert.False(view.IsExplorerTypeaheadActive);
            }
            finally
            {
                window?.Close();
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        });
    }

    [Fact]
    public void ExplorerShiftSpace_CollapsesThenMovesUp()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var root = Path.Combine(Path.GetTempPath(), "xqt-lib-ss-" + Guid.NewGuid().ToString("N"));
            var alpha = Path.Combine(root, "Alpha");
            var beta = Path.Combine(root, "Beta");
            Directory.CreateDirectory(alpha);
            Directory.CreateDirectory(beta);
            Window? window = null;
            try
            {
                var view = new LibraryBrowserView();
                view.SetExplorerRoots([root]);
                window = new Window
                {
                    Content = view,
                    Width = 900,
                    Height = 480,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                };
                window.Show();
                Flush();
                var folder = view.ExplorerFirstFolder;
                Assert.NotNull(folder);
                folder.IsSelected = true;
                Flush();

                Assert.True(view.TryExplorerSpaceExpandOrMoveDown());
                Flush();
                Assert.True(folder.IsExpanded);

                // 展開済みで Shift+Space → 1段階閉じる。
                Assert.True(view.TryExplorerShiftSpaceCollapseOrMoveUp());
                Flush();
                Assert.False(folder.IsExpanded);
                Assert.True(view.TryGetSelectedExplorerFolder(out var stillRoot));
                Assert.Equal(Path.GetFullPath(root), Path.GetFullPath(stillRoot));

                // 閉じた状態で再度 Shift+Space → 上へ（ルートなのでそのまま）。
                Assert.True(view.TryExplorerShiftSpaceCollapseOrMoveUp());
                Assert.True(view.TryGetSelectedExplorerFolder(out var stillRoot2));
                Assert.Equal(Path.GetFullPath(root), Path.GetFullPath(stillRoot2));

                Assert.True(view.TryExplorerSpaceExpandOrMoveDown());
                Flush();
                Assert.True(view.TryExplorerSpaceExpandOrMoveDown());
                Assert.True(view.TryGetSelectedExplorerFolder(out var onAlpha));
                Assert.Equal(Path.GetFullPath(alpha), Path.GetFullPath(onAlpha));

                // 葉（閉じた子）で Shift+Space → 一つ上（ルート）。
                Assert.True(view.TryExplorerShiftSpaceCollapseOrMoveUp());
                Assert.True(view.TryGetSelectedExplorerFolder(out var backOnRoot));
                Assert.Equal(Path.GetFullPath(root), Path.GetFullPath(backOnRoot));
            }
            finally
            {
                window?.Close();
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        });
    }

    [Fact]
    public void FocusExplorerFolder_MovesKeyboardFocusFromPlaylist()
    {
        // 新規フォルダ作成後に選択だけツリーへ移り、Ctrl+V がプレイリストのまま
        // 「同じ場所」になるのを防ぐ。
        RunSta(() =>
        {
            EnsureTheme();
            var root = Path.Combine(Path.GetTempPath(), "mga-focus-folder-" + Guid.NewGuid().ToString("N"));
            var child = Path.Combine(root, "Child");
            Directory.CreateDirectory(child);
            Window? window = null;
            try
            {
                var view = new LibraryBrowserView();
                view.SetExplorerRoots([root]);
                window = new Window
                {
                    Content = view,
                    Width = 900,
                    Height = 480,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                };
                window.Show();
                Flush();
                view.FocusList();
                Flush();
                Assert.True(view.IsListKeyboardFocused);

                view.FocusExplorerFolder(child);
                Flush();
                Assert.True(view.TryGetSelectedExplorerFolder(out var selected));
                Assert.Equal(Path.GetFullPath(child), Path.GetFullPath(selected));
                Assert.True(view.IsExplorerFocused);
                Assert.False(view.IsListKeyboardFocused);
            }
            finally
            {
                window?.Close();
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        });
    }

    [Fact]
    public void RefreshExplorer_PicksUpNewFolder_KeepsSelection()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var root = Path.Combine(Path.GetTempPath(), "mga-tree-refresh-" + Guid.NewGuid().ToString("N"));
            var alpha = Path.Combine(root, "Alpha");
            Directory.CreateDirectory(alpha);
            Window? window = null;
            try
            {
                var view = new LibraryBrowserView();
                view.SetExplorerRoots([root]);
                window = new Window
                {
                    Content = view,
                    Width = 900,
                    Height = 480,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                };
                window.Show();
                Flush();
                var folder = view.ExplorerFirstFolder;
                Assert.NotNull(folder);
                folder.IsExpanded = true;
                Flush();
                view.SetExplorerFolder(alpha);
                Flush();
                Assert.True(view.TryGetSelectedExplorerFolder(out var selected));
                Assert.Equal(Path.GetFullPath(alpha), Path.GetFullPath(selected));

                var beta = Path.Combine(root, "Beta");
                Directory.CreateDirectory(beta);
                view.RefreshExplorer();
                Flush();

                Assert.True(view.TryGetSelectedExplorerFolder(out var after));
                Assert.Equal(Path.GetFullPath(alpha), Path.GetFullPath(after));
                var names = view.ExplorerFirstFolder!
                    .Items.OfType<TreeViewItem>()
                    .Where(item => item.Tag is string)
                    .Select(item => Path.GetFileName((string)item.Tag!))
                    .ToArray();
                Assert.Contains("Alpha", names, StringComparer.OrdinalIgnoreCase);
                Assert.Contains("Beta", names, StringComparer.OrdinalIgnoreCase);
            }
            finally
            {
                window?.Close();
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        });
    }

    [Fact]
    public void Explorer_CtrlMultiSelect_PressKeepsFoldersForCopy()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var root = Path.Combine(Path.GetTempPath(), "mga-tree-multi-" + Guid.NewGuid().ToString("N"));
            var firstPath = Path.Combine(root, "Alpha");
            var secondPath = Path.Combine(root, "Beta");
            Directory.CreateDirectory(firstPath);
            Directory.CreateDirectory(secondPath);
            Window? window = null;
            try
            {
                var view = new LibraryBrowserView();
                view.SetExplorerRoots([root]);
                window = new Window
                {
                    Content = view,
                    Width = 900,
                    Height = 480,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                };
                window.Show();
                Flush();
                var folder = view.ExplorerFirstFolder;
                Assert.NotNull(folder);
                folder.IsExpanded = true;
                Flush();
                var children = folder.Items.OfType<TreeViewItem>()
                    .Where(item => item.Tag is string)
                    .ToArray();
                Assert.True(children.Length >= 2);
                view.BeginExplorerPlainPress(children[0], control: false);
                view.BeginExplorerPlainPress(children[1], control: true);
                Flush();
                Assert.Equal(2, view.SelectedExplorerFolders.Length);
                Assert.True(view.ShouldHoldExplorerMultiSelect(children[0]));

                view.BeginExplorerPlainPress(children[0], control: false);
                Flush();
                Assert.Equal(2, view.SelectedExplorerFolders.Length);
                view.FocusExplorer();
                Flush();
                var copies = view.SelectedCopyPaths();
                Assert.Contains(Path.GetFullPath(firstPath), copies);
                Assert.Contains(Path.GetFullPath(secondPath), copies);
                Assert.Equal(2, copies.Length);

                view.AbandonExplorerPendingClick();
                view.CompleteExplorerPlainPress();
                Flush();
                Assert.Equal(2, view.SelectedExplorerFolders.Length);
            }
            finally
            {
                window?.Close();
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        });
    }

    [Fact]
    public void Explorer_ArrowNav_ScrollsOnlyWhenHeaderLeavesViewport()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var root = Path.Combine(Path.GetTempPath(), "mga-tree-arrow-scroll-" + Guid.NewGuid().ToString("N"));
            var anime = Path.Combine(root, "Anime");
            var nested = Path.Combine(anime, "Nested");
            Directory.CreateDirectory(nested);
            for (var i = 0; i < 50; i++)
            {
                Directory.CreateDirectory(Path.Combine(nested, $"Track{i:D2}"));
            }

            for (var i = 0; i < 30; i++)
            {
                Directory.CreateDirectory(Path.Combine(root, $"Other{i:D2}"));
            }

            Window? window = null;
            try
            {
                var view = new LibraryBrowserView();
                view.SetExplorerRoots([root]);
                window = new Window
                {
                    Content = view,
                    Width = 480,
                    Height = 360,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                };
                window.Show();
                Flush();
                var folder = view.ExplorerFirstFolder;
                Assert.NotNull(folder);
                folder!.IsExpanded = true;
                Flush();
                var animeItem = folder.Items.OfType<TreeViewItem>()
                    .First(item => item.Tag is string path
                        && path.EndsWith("Anime", StringComparison.OrdinalIgnoreCase));
                animeItem.IsExpanded = true;
                Flush();
                var nestedItem = animeItem.Items.OfType<TreeViewItem>()
                    .First(item => item.Tag is string);
                nestedItem.IsExpanded = true;
                Flush();
                view.UpdateLayout();
                window.UpdateLayout();
                Flush();

                var scroll = FindAncestorScroll(folder);
                Assert.NotNull(scroll);
                Assert.True(scroll!.ScrollableHeight > 8, "precondition: tree must overflow");

                // 1) ルート先頭 → 直下へ ↓。展開子が多くてもスクロールしない。
                scroll.ScrollToVerticalOffset(0);
                folder.IsSelected = true;
                view.BeginExplorerPlainPress(folder, control: false);
                Flush();
                view.EnsureExplorerItemHeaderVisible(folder);
                Flush();
                var offsetBeforeDown = scroll.VerticalOffset;
                view.MoveExplorerSelection(1, extend: false);
                Flush();
                Assert.True(animeItem.IsSelected);
                Assert.True(
                    Math.Abs(scroll.VerticalOffset - offsetBeforeDown) < 1.0,
                    $"expected no scroll on ↓ to visible child, before={offsetBeforeDown}, after={scroll.VerticalOffset}");

                // 2) 深い末尾から先頭ルートへ ↑。ルート見出しを最上段へ。
                var deepChildren = nestedItem.Items.OfType<TreeViewItem>()
                    .Where(item => item.Tag is string)
                    .ToArray();
                Assert.True(deepChildren.Length >= 2);
                deepChildren[^1].IsSelected = true;
                view.BeginExplorerPlainPress(deepChildren[^1], control: false);
                Flush();
                scroll.ScrollToVerticalOffset(scroll.ScrollableHeight);
                Flush();
                Assert.True(scroll.VerticalOffset > 4, "precondition: scrolled down");

                for (var i = 0; i < deepChildren.Length + 8; i++)
                {
                    view.MoveExplorerSelection(-1, extend: false);
                    Flush();
                }

                Assert.True(folder.IsSelected, "expected root selected");
                Assert.True(
                    scroll.VerticalOffset < 1.5,
                    $"expected scroll at top after ↑ to root, got {scroll.VerticalOffset}");
            }
            finally
            {
                window?.Close();
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        });
    }

    [Fact]
    public void Explorer_ShiftArrow_SelectsVisibleRange()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var root = Path.Combine(Path.GetTempPath(), "mga-tree-shift-" + Guid.NewGuid().ToString("N"));
            var firstPath = Path.Combine(root, "Alpha");
            var secondPath = Path.Combine(root, "Beta");
            var thirdPath = Path.Combine(root, "Gamma");
            Directory.CreateDirectory(firstPath);
            Directory.CreateDirectory(secondPath);
            Directory.CreateDirectory(thirdPath);
            Window? window = null;
            try
            {
                var view = new LibraryBrowserView();
                view.SetExplorerRoots([root]);
                window = new Window
                {
                    Content = view,
                    Width = 900,
                    Height = 480,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                };
                window.Show();
                Flush();
                var folder = view.ExplorerFirstFolder;
                Assert.NotNull(folder);
                folder.IsExpanded = true;
                Flush();
                var children = folder.Items.OfType<TreeViewItem>()
                    .Where(item => item.Tag is string)
                    .ToArray();
                Assert.True(children.Length >= 3);
                children[0].IsSelected = true;
                view.BeginExplorerPlainPress(children[0], control: false);
                Flush();
                view.MoveExplorerSelection(1, extend: true);
                Flush();
                Assert.Equal(2, view.SelectedExplorerFolders.Length);
                Assert.Contains(Path.GetFullPath(firstPath), view.SelectedExplorerFolders.Select(Path.GetFullPath));
                Assert.Contains(Path.GetFullPath(secondPath), view.SelectedExplorerFolders.Select(Path.GetFullPath));

                view.MoveExplorerSelection(1, extend: true);
                Flush();
                Assert.Equal(3, view.SelectedExplorerFolders.Length);
                Assert.Contains(Path.GetFullPath(thirdPath), view.SelectedExplorerFolders.Select(Path.GetFullPath));

                view.MoveExplorerSelection(-1, extend: true);
                Flush();
                Assert.Equal(2, view.SelectedExplorerFolders.Length);
                Assert.DoesNotContain(Path.GetFullPath(thirdPath), view.SelectedExplorerFolders.Select(Path.GetFullPath));

                view.MoveExplorerSelection(1, extend: false);
                Flush();
                var lastFolder = Assert.Single(view.SelectedExplorerFolders);
                Assert.Equal(Path.GetFullPath(thirdPath), Path.GetFullPath(lastFolder));

                children[0].IsSelected = true;
                view.BeginExplorerPlainPress(children[0], control: false);
                Flush();
                view.ApplyExplorerShiftClick(children[2]);
                Flush();
                Assert.Equal(3, view.SelectedExplorerFolders.Length);
                Assert.Contains(Path.GetFullPath(firstPath), view.SelectedExplorerFolders.Select(Path.GetFullPath));
                Assert.Contains(Path.GetFullPath(thirdPath), view.SelectedExplorerFolders.Select(Path.GetFullPath));
            }
            finally
            {
                window?.Close();
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        });
    }

    [Fact]
    public void ExplorerNestedFolders_UseSameHoverTemplate()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var root = Path.Combine(Path.GetTempPath(), "mga-tree-hover-" + Guid.NewGuid().ToString("N"));
            var childPath = Path.Combine(root, "sub");
            Directory.CreateDirectory(childPath);
            Window? window = null;
            try
            {
                var view = new LibraryBrowserView();
                view.SetExplorerRoots([root]);
                window = new Window
                {
                    Content = view,
                    Width = 900,
                    Height = 480,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                };
                window.Show();
                Flush();
                view.UpdateLayout();
                Flush();

                var folder = view.ExplorerFirstFolder;
                Assert.NotNull(folder);
                Assert.NotNull(folder.Style);
                folder.ApplyTemplate();
                Assert.IsType<Border>(folder.Template.FindName("Bd", folder));

                folder.IsExpanded = true;
                Flush();
                var nested = folder.Items.OfType<TreeViewItem>().FirstOrDefault(item => item.Tag is string);
                Assert.NotNull(nested);
                Assert.Same(folder.Style, nested.Style);
                nested.ApplyTemplate();
                Assert.IsType<Border>(nested.Template.FindName("Bd", nested));
            }
            finally
            {
                window?.Close();
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        });
    }

    [Fact]
    public void RefreshAppearance_ReappliesTreeHoverToExistingFolders()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var root = Path.Combine(Path.GetTempPath(), "mga-tree-restyle-" + Guid.NewGuid().ToString("N"));
            var childPath = Path.Combine(root, "sub");
            Directory.CreateDirectory(childPath);
            Window? window = null;
            try
            {
                var view = new LibraryBrowserView();
                view.SetExplorerRoots([root]);
                window = new Window
                {
                    Content = view,
                    Width = 900,
                    Height = 480,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                };
                window.Show();
                Flush();

                var folder = view.ExplorerFirstFolder;
                Assert.NotNull(folder);
                folder.IsExpanded = true;
                Flush();
                var nested = folder.Items.OfType<TreeViewItem>().FirstOrDefault(item => item.Tag is string);
                Assert.NotNull(nested);
                var before = folder.Style;

                view.RefreshAppearance();
                Flush();
                Assert.Same(view.ExplorerItemStyle, folder.Style);
                Assert.Same(view.ExplorerItemStyle, nested.Style);
                Assert.NotSame(before, folder.Style);

                var hover = Assert.IsType<SolidColorBrush>(LibraryBrowserView.GrayHoverBrush());
                var template = Assert.IsType<ControlTemplate>(folder.Template);
                var trigger = template.Triggers.OfType<Trigger>()
                    .First(item => item.SourceName == "Bd" && item.Property == UIElement.IsMouseOverProperty);
                var fill = Assert.IsType<SolidColorBrush>(trigger.Setters.OfType<Setter>().First().Value);
                Assert.Equal(hover.Color, fill.Color);
            }
            finally
            {
                window?.Close();
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        });
    }

    [Fact]
    public void ExplorerStarAndSlash_ExpandAndCollapseSubtree()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var root = Path.Combine(Path.GetTempPath(), "mga-tree-expand-" + Guid.NewGuid().ToString("N"));
            var nestedPath = Path.Combine(root, "sub");
            var deepPath = Path.Combine(nestedPath, "deep");
            Directory.CreateDirectory(deepPath);
            Window? window = null;
            try
            {
                var view = new LibraryBrowserView();
                view.SetExplorerRoots([root]);
                window = new Window
                {
                    Content = view,
                    Width = 900,
                    Height = 480,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                };
                window.Show();
                Flush();
                view.UpdateLayout();
                Flush();

                var folder = view.ExplorerFirstFolder;
                Assert.NotNull(folder);
                folder.IsSelected = true;
                Assert.True(view.ExpandSelectedExplorerSubtree());
                Flush();
                Assert.True(folder.IsExpanded);
                var nested = folder.Items.OfType<TreeViewItem>().FirstOrDefault(item => item.Tag is string);
                Assert.NotNull(nested);
                Assert.True(nested.IsExpanded);
                var deep = nested.Items.OfType<TreeViewItem>().FirstOrDefault(item => item.Tag is string);
                Assert.NotNull(deep);
                Assert.True(deep.IsExpanded);
                Assert.Contains(
                    Path.GetFullPath(nestedPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    view.ExplorerExpandedPaths,
                    StringComparer.OrdinalIgnoreCase);

                Assert.True(view.CollapseSelectedExplorerSubtree());
                Flush();
                Assert.False(folder.IsExpanded);
                Assert.False(nested.IsExpanded);
                Assert.False(deep.IsExpanded);

                view.SetExplorerExpanded([root, nestedPath, deepPath]);
                Flush();
                Assert.True(folder.IsExpanded);
                nested = folder.Items.OfType<TreeViewItem>().FirstOrDefault(item => item.Tag is string);
                Assert.NotNull(nested);
                Assert.True(nested.IsExpanded);
                deep = nested.Items.OfType<TreeViewItem>().FirstOrDefault(item => item.Tag is string);
                Assert.NotNull(deep);
                Assert.True(deep.IsExpanded);
            }
            finally
            {
                window?.Close();
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        });
    }

    [Fact]
    public void FocusPaneShortcuts_SelectTreeFavoritesAndList()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            if (string.IsNullOrWhiteSpace(music) || !Directory.Exists(music))
            {
                music = Path.GetTempPath();
            }

            var view = new LibraryBrowserView();
            view.SetExplorerRoots([music]);
            view.SetFavorites([music]);
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            Flush();
            view.UpdateLayout();
            Flush();

            view.FocusList();
            Flush();
            view.FocusExplorer();
            Flush();
            Assert.True(view.IsExplorerFocused);
            view.FocusFavorites();
            Flush();
            Assert.True(view.IsFavoritesFocused);
            view.FocusList();
            Flush();
            Assert.True(view.IsListKeyboardFocused);
            Assert.Equal(Visibility.Visible, view.ListFocusLineVisibility);
            Assert.Equal(Visibility.Collapsed, view.TreeFocusLineVisibility);
            Assert.Equal(LibraryPane.Explorer, LibraryPlayerMode.NextPane(view.ActivePane, reverse: false));
            view.CyclePaneFocus(reverse: false);
            Flush();
            Assert.True(view.IsExplorerFocused);
            Assert.Equal(Visibility.Visible, view.TreeFocusLineVisibility);
            Assert.Equal(Visibility.Collapsed, view.ListFocusLineVisibility);
            view.CyclePaneFocus(reverse: false);
            Flush();
            Assert.True(view.IsFavoritesFocused);
            Assert.Equal(Visibility.Visible, view.FavoritesFocusLineVisibility);
            view.CyclePaneFocus(reverse: false);
            Flush();
            Assert.True(view.IsListKeyboardFocused);
            view.CyclePaneFocus(reverse: true);
            Flush();
            Assert.True(view.IsFavoritesFocused);
            Assert.Equal(36, LibraryBrowserView.PaneFocusLineHeight);
            window.Close();
        });
    }

    [Fact]
    public void EmptyPaneClick_ActivatesPaneWithoutForcingFirstSelection()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            if (string.IsNullOrWhiteSpace(music) || !Directory.Exists(music))
            {
                music = Path.GetTempPath();
            }

            var view = new LibraryBrowserView();
            view.SetExplorerRoots([music]);
            view.SetFavorites([music]);
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            Flush();
            view.UpdateLayout();
            Flush();

            view.FocusList();
            Flush();
            Assert.True(view.IsListKeyboardFocused);

            view.ActivateExplorerPaneFromClick();
            Flush();
            Assert.True(view.IsExplorerFocused);
            Assert.Equal(Visibility.Visible, view.TreeFocusLineVisibility);
            Assert.Equal(Visibility.Collapsed, view.ListFocusLineVisibility);

            view.ActivateFavoritesPaneFromClick();
            Flush();
            Assert.True(view.IsFavoritesFocused);
            Assert.Equal(Visibility.Visible, view.FavoritesFocusLineVisibility);

            view.ActivatePlaylistPaneFromClick();
            Flush();
            Assert.True(view.IsListKeyboardFocused);
            Assert.Equal(Visibility.Visible, view.ListFocusLineVisibility);
            window.Close();
        });
    }

    [Fact]
    public void KeyboardContextMenu_OpensPaneMenuNotEditor()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            if (string.IsNullOrWhiteSpace(music) || !Directory.Exists(music))
            {
                music = Path.GetTempPath();
            }

            var view = new LibraryBrowserView();
            view.SetExplorerRoots([music]);
            view.SetFavorites([music]);
            view.SetSessions([Session("01 a.mp3")], null, []);
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            Flush();
            view.UpdateLayout();
            Flush();

            view.FocusExplorer();
            Flush();
            var replaced = 0;
            var appended = 0;
            view.ExplorerReplacePlaylistRequested += (_, _) => replaced++;
            view.ExplorerFolderOpened += (_, _) => appended++;
            Assert.True(view.TryOpenKeyboardContextMenu());
            var items = view.OpenContextMenuItems;
            Assert.Equal(
            [
                UiStrings.LibraryMenuCatPlaylist,
                UiStrings.LibraryMenuCatEdit,
                UiStrings.LibraryMenuCatTree,
                UiStrings.LibraryMenuCatOther,
            ],
            items.Select(item => (string)item.Header).ToArray());
            var playlist = items[0];
            var edit = items[1];
            var tree = items[2];
            var other = items[3];
            Assert.Equal("Enter", ((MenuItem)playlist.Items[0]).InputGestureText);
            Assert.Equal("Shift+Enter", ((MenuItem)playlist.Items[1]).InputGestureText);
            Assert.Equal("Ctrl+Enter", ((MenuItem)playlist.Items[2]).InputGestureText);
            Assert.Equal("Ctrl+Shift+Enter", ((MenuItem)playlist.Items[3]).InputGestureText);
            Assert.Equal("Ctrl+C", ((MenuItem)edit.Items[0]).InputGestureText);
            Assert.Equal("F7", ((MenuItem)edit.Items[3]).InputGestureText);
            Assert.Equal(UiStrings.LibraryMenuNewFolder, (string)((MenuItem)edit.Items[3]).Header);
            Assert.Equal("F2", ((MenuItem)edit.Items[4]).InputGestureText);
            Assert.Equal("Ctrl+Delete", ((MenuItem)edit.Items[5]).InputGestureText);
            Assert.Equal(MenuGestures.ExplorerExpand, ((MenuItem)tree.Items[0]).InputGestureText);
            Assert.Equal(MenuGestures.ExplorerCollapse, ((MenuItem)tree.Items[1]).InputGestureText);
            Assert.Equal("Alt+Up", ((MenuItem)tree.Items[4]).InputGestureText);
            Assert.Equal("Alt+Down", ((MenuItem)tree.Items[5]).InputGestureText);
            Assert.Equal("F5", ((MenuItem)other.Items[0]).InputGestureText);
            Assert.Equal("Ctrl+F5", ((MenuItem)other.Items[1]).InputGestureText);
            var rebuilt = 0;
            view.LibraryCatalogRebuildRequested += (_, _) => rebuilt++;
            ((MenuItem)other.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(1, rebuilt);
            ((MenuItem)playlist.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            ((MenuItem)playlist.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(1, replaced);
            Assert.Equal(1, appended);
            view.CloseKeyboardContextMenu();

            view.FocusFavorites();
            Flush();
            LibraryFavoritesActivateEventArgs? favoritesArgs = null;
            view.FavoritesActivated += (_, e) => favoritesArgs = e;
            Assert.True(view.TryOpenKeyboardContextMenu());
            Assert.Equal(
            [
                UiStrings.LibraryMenuCatPlaylist,
                UiStrings.LibraryMenuCatEdit,
                UiStrings.LibraryMenuCatOther,
            ],
            view.OpenContextMenuItems.Select(item => (string)item.Header).ToArray());
            var favoritePlaylist = view.OpenContextMenuItems[0];
            var favoriteEdit = view.OpenContextMenuItems[1];
            Assert.Equal("Enter", ((MenuItem)favoritePlaylist.Items[0]).InputGestureText);
            Assert.Equal("Shift+Enter", ((MenuItem)favoritePlaylist.Items[1]).InputGestureText);
            Assert.Equal("Ctrl+Enter", ((MenuItem)favoritePlaylist.Items[2]).InputGestureText);
            Assert.Equal("Ctrl+Shift+Enter", ((MenuItem)favoritePlaylist.Items[3]).InputGestureText);
            Assert.Equal("F2", ((MenuItem)favoriteEdit.Items[0]).InputGestureText);
            ((MenuItem)favoritePlaylist.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.True(favoritesArgs is { ClearPlaylist: true });
            favoritesArgs = null;
            ((MenuItem)favoritePlaylist.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.True(favoritesArgs is { ClearPlaylist: false });
            Assert.True(view.HasOpenContextMenu);
            view.CloseKeyboardContextMenu();
            Assert.False(view.HasOpenContextMenu);

            view.FocusList();
            Flush();
            Assert.True(view.IsListOrigin(view.FileGrid));
            Assert.True(view.TryOpenKeyboardContextMenu());
            // ウィンドウメニューと同じカテゴリ（先頭カラーのみプレイリスト固有）。実行不可もグレーアウトで残す。
            Assert.Equal(
            [
                UiStrings.LibraryMenuColor,
                UiStrings.WaveMenuCatHere,
                UiStrings.WaveMenuCatFile,
                UiStrings.WaveMenuCatEdit,
                UiStrings.WaveMenuCatFormat,
                UiStrings.WaveMenuCatExport,
                UiStrings.WaveMenuCatView,
                UiStrings.WaveMenuCatTimeline,
                UiStrings.WaveMenuCatTabs,
                UiStrings.WaveMenuCatPlayback,
                UiStrings.WaveMenuCatAudio,
                UiStrings.WaveMenuCatVideo,
                UiStrings.WaveMenuCatWwise,
                UiStrings.WaveMenuCatOther,
            ],
            view.OpenContextMenuItems.Select(item => (string)item.Header).ToArray());
            var listEdit = view.OpenContextMenuItems[3];
            var listFormat = view.OpenContextMenuItems[4];
            var listExport = view.OpenContextMenuItems[5];
            var listPlayback = view.OpenContextMenuItems[9];
            var listOther = view.OpenContextMenuItems[13];
            var listPlaylist = listPlayback.Items.OfType<MenuItem>()
                .First(item => (string)item.Header == UiStrings.WaveMenuCatPlaylist);
            Assert.Equal(MenuGestures.PreviousTrack, ((MenuItem)listPlaylist.Items[0]).InputGestureText);
            Assert.Equal(MenuGestures.NextTrack, ((MenuItem)listPlaylist.Items[1]).InputGestureText);
            Assert.Equal(MenuGestures.RestartTrack, ((MenuItem)listPlaylist.Items[2]).InputGestureText);
            Assert.Equal("Alt+Shift+P", ((MenuItem)listPlaylist.Items[4]).InputGestureText);
            Assert.Equal(MenuGestures.SelectByGroupUp, ((MenuItem)listPlaylist.Items[^3]).InputGestureText);
            Assert.Equal(MenuGestures.SelectByGroupDown, ((MenuItem)listPlaylist.Items[^2]).InputGestureText);
            Assert.Equal(MenuGestures.SelectSameColor, ((MenuItem)listPlaylist.Items[^1]).InputGestureText);
            Assert.Equal(UiStrings.WaveMenuSelectByGroupUp, (string)((MenuItem)listPlaylist.Items[^3]).Header);
            Assert.Equal(UiStrings.WaveMenuSelectByGroupDown, (string)((MenuItem)listPlaylist.Items[^2]).Header);
            Assert.Equal(UiStrings.WaveMenuSelectSameColor, (string)((MenuItem)listPlaylist.Items[^1]).Header);
            Assert.Equal(MenuGestures.PitchUp, listPlayback.Items.OfType<MenuItem>()
                .First(item => item.InputGestureText == MenuGestures.PitchUp).InputGestureText);
            // 編集末尾はシェル操作＋プレイリストからクリア。
            Assert.Equal(UiStrings.LibraryMenuClearFromPlaylist, (string)((MenuItem)listEdit.Items[^1]).Header);
            Assert.Equal(MenuGestures.Delete, ((MenuItem)listEdit.Items[^1]).InputGestureText);
            Assert.Equal("Ctrl+C", ((MenuItem)listEdit.Items[^7]).InputGestureText);
            // 破壊編集ショートカット（S／B／C 等）はプレイヤーでは表記なし。
            Assert.True(string.IsNullOrEmpty(((MenuItem)listFormat.Items[0]).InputGestureText));
            Assert.True(string.IsNullOrEmpty(((MenuItem)listFormat.Items[1]).InputGestureText));
            Assert.True(string.IsNullOrEmpty(((MenuItem)listFormat.Items[2]).InputGestureText));
            Assert.Equal("Ctrl+Shift+M", ((MenuItem)listExport.Items[1]).InputGestureText);
            Assert.Equal("Ctrl+Shift+X", ((MenuItem)listExport.Items[2]).InputGestureText);
            // コピー／エクスプローラーは例外でその他の先頭。
            Assert.Equal(UiStrings.LibraryMenuCopyFileName, (string)((MenuItem)listOther.Items[0]).Header);
            Assert.Equal(UiStrings.LibraryMenuCopyFilePath, (string)((MenuItem)listOther.Items[1]).Header);
            Assert.Equal(UiStrings.LibraryMenuOpenInExplorer, (string)((MenuItem)listOther.Items[2]).Header);
            Assert.IsType<Separator>(listOther.Items[3]);
            Assert.Equal(UiStrings.WaveMenuSettings, (string)((MenuItem)listOther.Items[4]).Header);
            // Wwise はプレイヤーでは実行不可でもカテゴリ自体は残してグレーアウト。
            Assert.False(view.OpenContextMenuItems[12].IsEnabled);
            Assert.Equal(UiStrings.WaveMenuCatWwise, (string)view.OpenContextMenuItems[12].Header);
            Assert.Equal(UiStrings.WaveMenuCatVideo, (string)view.OpenContextMenuItems[11].Header);
            WaveMenuCommand? waveCommand = null;
            view.PlaylistWaveMenuRequested += (_, command) => waveCommand = command;
            var sampleRateItem = (MenuItem)listFormat.Items[0];
            sampleRateItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(WaveMenuCommand.SampleRate, waveCommand);
            Assert.True(view.HasOpenContextMenu);
            view.CloseKeyboardContextMenu();
            window.Close();
        });
    }

    [Fact]
    public void ListContextMenu_DismissDoesNotCompletePendingSingleSelect()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var first = Session("01 a.wav");
            var second = Session("02 b.wav");
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetGroup(LibraryFileGroup.None);
            view.SetSessions([first, second], first, [first, second]);
            Flush();
            view.FocusList();
            Flush();
            var clicked = ((System.Collections.IEnumerable)view.BoundItemsSource!)
                .OfType<LibraryFileRow>()
                .First(row => ReferenceEquals(row.Tag, second));
            view.BeginPlaylistPlainPress(clicked, clickCount: 1);
            Assert.True(view.TryOpenKeyboardContextMenu());
            Assert.Equal(2, view.SelectedSessions.Length);
            // Opened で pending を捨てる。この時点で Complete しても複数選択のまま。
            view.CompletePlaylistPlainPress();
            Flush();
            Assert.Equal(2, view.SelectedSessions.Length);
            view.CloseKeyboardContextMenu();
            Flush();
            window.Close();
        });
    }

    [Fact]
    public void HeaderStructure_KeyboardContextMenu_OpensChunkMenuNotPlaylist()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var path = Path.Combine(
                Path.GetTempPath(),
                "mga-header-apps-" + Guid.NewGuid().ToString("N") + ".wav");
            var pcm = new byte[4];
            var fmt = new byte[]
            {
                1, 0, 1, 0, 0x44, 0xAC, 0, 0, 0x88, 0x58, 0x01, 0, 2, 0, 16, 0,
            };
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + pcm.Length);
                w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                w.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
                w.Write(fmt.Length);
                w.Write(fmt);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                w.Write(pcm.Length);
                w.Write(pcm);
                File.WriteAllBytes(path, ms.ToArray());
            }

            try
            {
                var session = new DocumentSession(AudioDocument.CreateDeferred(path));
                var view = new LibraryBrowserView();
                var window = new Window
                {
                    Content = view,
                    Width = 900,
                    Height = 480,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                };
                window.Show();
                view.SetSessions([session], session, [session]);
                Flush();
                view.ToggleHeaderStructureVisible(focusWhenShown: true);
                Flush();
                Assert.True(view.IsHeaderStructureFocused);
                Assert.NotEmpty(view.HeaderStructureTree.Items);

                Assert.True(view.TryOpenKeyboardContextMenu());
                Assert.True(view.HasOpenContextMenu);
                // メニュー表示中はフォーカスがメニュー側へ移る。プレイリストメニューではないことだけ見る。
                Assert.Equal(
                [
                    UiStrings.WaveMenuSelectAll,
                    UiStrings.WaveMenuCopy,
                    UiStrings.WaveChannelLayoutEditMenu,
                    UiStrings.WaveChunkDeleteMenu,
                ],
                view.OpenContextMenuItems.Select(item => (string)item.Header).ToArray());

                view.CloseKeyboardContextMenu();
                Assert.False(view.HasOpenContextMenu);
                window.Close();
            }
            finally
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                }
            }
        });
    }

    [Fact]
    public void ListContextMenu_DismissGuard_StaysActiveThroughCloseUntilIdle()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var first = Session("01 a.wav");
            var second = Session("02 b.wav");
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetGroup(LibraryFileGroup.None);
            view.SetSessions([first, second], first, [first, second]);
            Flush();
            view.FocusList();
            Flush();

            Assert.False(view.IsContextMenuDismissGuardActive);

            Assert.True(view.TryOpenKeyboardContextMenu());
            Assert.True(view.HasOpenContextMenu);
            Assert.True(view.IsContextMenuDismissGuardActive);

            // 外側クリックで閉じると、メニューを閉じたのと同じ入力の中で、下にある
            // トランスポートの「プレイヤー(F10)」ボタンへクリックが貫通する。閉じた直後は
            // HasOpenContextMenu が既に false なので、ガードが立っていないと貫通クリックで
            // ToggleLibraryMaximize → KeepOnlyLibrarySelectedSessions が走り、選択以外の
            // トラックが消える（報告されたバグ）。閉じてから左ボタン解放まではガードを維持する。
            view.CloseKeyboardContextMenu();
            // 閉じた直後は HasOpenContextMenu が false。旧コードはこれだけを見ていたので、
            // 貫通クリックを弾けずにプレイヤーを抜けていた（＝バグ）。
            Assert.False(view.HasOpenContextMenu);
            // 貫通クリックは閉じたのと同じ入力の中（アイドル前）で届くので、ガードはまだ立っている。
            Assert.True(view.IsContextMenuDismissGuardActive);
            // plain-press 抑制も dismiss 左クリック解放まで残す。
            Assert.True(view.IsPlaylistPlainPressSuppressedAfterMenu);

            window.Close();
        });
    }

    [Fact]
    public void ListContextMenu_DismissGuard_StaysActiveAfterIdleWhileLeftHeld()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var first = Session("01 a.wav");
            var second = Session("02 b.wav");
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetGroup(LibraryFileGroup.None);
            view.SetSessions([first, second], first, [first, second]);
            Flush();
            view.FocusList();
            Flush();

            // Idle が先に回っても、同じ dismiss の左ボタンが押されているあいだはガードを残す。
            // （キャプチャ解放の再入で Idle が MouseUp/Click より先に来る稀な経路の回帰。）
            view.DismissGuardLeftHeldForTests = () => true;
            Assert.True(view.TryOpenKeyboardContextMenu());
            view.CloseKeyboardContextMenu();
            Assert.False(view.HasOpenContextMenu);
            Flush();
            Assert.True(view.IsContextMenuDismissGuardActive);
            Assert.True(view.IsPlaylistPlainPressSuppressedAfterMenu);

            view.DismissGuardLeftHeldForTests = () => false;
            view.FlushContextMenuDismissGuardForTests();
            Assert.False(view.IsContextMenuDismissGuardActive);
            Assert.False(view.IsPlaylistPlainPressSuppressedAfterMenu);

            window.Close();
        });
    }

    [Fact]
    public void SelectedCopyPaths_Playlist_UsesExistingFiles()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var path = Path.Combine(Path.GetTempPath(), "mga-copy-" + Guid.NewGuid().ToString("N") + ".wav");
            File.WriteAllBytes(path, [0]);
            try
            {
                var session = new DocumentSession(AudioDocument.CreateDeferred(path));
                var view = new LibraryBrowserView();
                var window = new Window
                {
                    Content = view,
                    Width = 900,
                    Height = 480,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                };
                window.Show();
                view.SetGroup(LibraryFileGroup.None);
                view.SetSessions([session], session, [session]);
                Flush();
                view.FocusList();
                Flush();
                Assert.Equal([Path.GetFullPath(path)], view.SelectedCopyPaths());
                Assert.Equal([Path.GetFullPath(path)], view.SelectedRevealPaths());
                window.Close();
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void PlaylistCopyPathsFromRow_Unselected_UsesClickedFile()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var firstPath = Path.Combine(Path.GetTempPath(), "mga-pl-a-" + Guid.NewGuid().ToString("N") + ".wav");
            var secondPath = Path.Combine(Path.GetTempPath(), "mga-pl-b-" + Guid.NewGuid().ToString("N") + ".wav");
            File.WriteAllBytes(firstPath, [0]);
            File.WriteAllBytes(secondPath, [0]);
            try
            {
                var first = new DocumentSession(AudioDocument.CreateDeferred(firstPath));
                var second = new DocumentSession(AudioDocument.CreateDeferred(secondPath));
                var view = new LibraryBrowserView();
                var window = new Window
                {
                    Content = view,
                    Width = 900,
                    Height = 480,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                };
                window.Show();
                view.SetGroup(LibraryFileGroup.None);
                view.SetSessions([first, second], first, [first]);
                Flush();
                var clicked = ((System.Collections.IEnumerable)view.BoundItemsSource!)
                    .OfType<LibraryFileRow>()
                    .First(row => ReferenceEquals(row.Tag, second));
                Assert.Equal([Path.GetFullPath(secondPath)], view.PlaylistCopyPathsFromRow(clicked));
                window.Close();
            }
            finally
            {
                File.Delete(firstPath);
                File.Delete(secondPath);
            }
        });
    }

    [Fact]
    public void PlaylistCopyPathsFromRow_Selected_UsesAllSelected()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var firstPath = Path.Combine(Path.GetTempPath(), "mga-pl-c-" + Guid.NewGuid().ToString("N") + ".wav");
            var secondPath = Path.Combine(Path.GetTempPath(), "mga-pl-d-" + Guid.NewGuid().ToString("N") + ".wav");
            File.WriteAllBytes(firstPath, [0]);
            File.WriteAllBytes(secondPath, [0]);
            try
            {
                var first = new DocumentSession(AudioDocument.CreateDeferred(firstPath));
                var second = new DocumentSession(AudioDocument.CreateDeferred(secondPath));
                var view = new LibraryBrowserView();
                var window = new Window
                {
                    Content = view,
                    Width = 900,
                    Height = 480,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                };
                window.Show();
                view.SetGroup(LibraryFileGroup.None);
                view.SetSessions([first, second], first, [first, second]);
                Flush();
                var clicked = ((System.Collections.IEnumerable)view.BoundItemsSource!)
                    .OfType<LibraryFileRow>()
                    .First(row => ReferenceEquals(row.Tag, first));
                Assert.Equal(
                    [Path.GetFullPath(firstPath), Path.GetFullPath(secondPath)],
                    view.PlaylistCopyPathsFromRow(clicked));
                window.Close();
            }
            finally
            {
                File.Delete(firstPath);
                File.Delete(secondPath);
            }
        });
    }

    [Fact]
    public void Playlist_PressOnMultiSelect_KeepsSelectionUntilRelease()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var first = Session("01 a.mp3");
            var second = Session("02 b.mp3");
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetGroup(LibraryFileGroup.None);
            view.SetSessions([first, second], first, [first, second]);
            Flush();
            var clicked = ((System.Collections.IEnumerable)view.BoundItemsSource!)
                .OfType<LibraryFileRow>()
                .First(row => ReferenceEquals(row.Tag, second));
            Assert.True(view.ShouldHoldPlaylistMultiSelect(clicked, clickCount: 1));
            view.BeginPlaylistPlainPress(clicked, clickCount: 1);
            Flush();
            Assert.Equal(2, view.SelectedSessions.Length);
            Assert.Contains(first, view.SelectedSessions);
            Assert.Contains(second, view.SelectedSessions);

            view.CompletePlaylistPlainPress();
            Flush();
            Assert.Equal([second], view.SelectedSessions);
            window.Close();
        });
    }

    [Fact]
    public void Playlist_PressOnMultiSelect_DragKeepsSelection()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var first = Session("01 a.mp3");
            var second = Session("02 b.mp3");
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetGroup(LibraryFileGroup.None);
            view.SetSessions([first, second], first, [first, second]);
            Flush();
            var clicked = ((System.Collections.IEnumerable)view.BoundItemsSource!)
                .OfType<LibraryFileRow>()
                .First(row => ReferenceEquals(row.Tag, second));
            view.BeginPlaylistPlainPress(clicked, clickCount: 1);
            view.AbandonPlaylistPendingClick();
            view.CompletePlaylistPlainPress();
            Flush();
            Assert.Equal(2, view.SelectedSessions.Length);
            Assert.Contains(first, view.SelectedSessions);
            Assert.Contains(second, view.SelectedSessions);
            window.Close();
        });
    }

    [Fact]
    public void MissingRegisteredFolders_ShowInTreeAndFavorites()
    {
        RunSta(() =>
        {
            EnsureTheme();
            Application.Current!.Resources[LibraryBrowserView.MissingRegisteredPathForeBrushKey] =
                new SolidColorBrush(Color.FromRgb(0xFF, 0x6E, 0x6E));
            var existing = Path.Combine(Path.GetTempPath(), "mga-missing-ok-" + Guid.NewGuid().ToString("N"));
            var missing = Path.Combine(Path.GetTempPath(), "mga-missing-gone-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(existing);
            Window? window = null;
            try
            {
                var view = new LibraryBrowserView();
                view.SetExplorerRoots([existing, missing]);
                view.SetFavorites([existing, missing]);
                window = new Window
                {
                    Content = view,
                    Width = 900,
                    Height = 480,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                };
                window.Show();
                Flush();

                var paths = view.ExplorerVisibleFolderPaths
                    .Select(path => Path.GetFullPath(path).TrimEnd('\\', '/'))
                    .ToArray();
                Assert.Contains(Path.GetFullPath(existing).TrimEnd('\\', '/'), paths);
                Assert.Contains(Path.GetFullPath(missing).TrimEnd('\\', '/'), paths);

                var missingFull = Path.GetFullPath(missing).TrimEnd('\\', '/');
                var missingItem = Assert.Single(
                    FindDescendants<TreeViewItem>(view),
                    item => item.Tag is string tag
                        && Path.GetFullPath(tag).TrimEnd('\\', '/')
                            .Equals(missingFull, StringComparison.OrdinalIgnoreCase));
                var missingHeader = Assert.IsType<TextBlock>(missingItem.Header);
                var missingFore = Assert.IsType<SolidColorBrush>(missingHeader.Foreground);
                Assert.Equal(Color.FromRgb(0xFF, 0x6E, 0x6E), missingFore.Color);

                Assert.Equal(
                    [Path.GetFullPath(existing), Path.GetFullPath(missing)],
                    view.FavoritePaths.Select(Path.GetFullPath).ToArray());
                Assert.False(LibraryFavoriteRow.FromPath(existing).IsMissing);
                Assert.True(LibraryFavoriteRow.FromPath(missing).IsMissing);

                var nested = Path.Combine(existing, "Nested");
                Directory.CreateDirectory(nested);
                view.SetExplorerFolder(nested);
                Flush();
                var rootsChanged = 0;
                IReadOnlyList<string>? savedRoots = null;
                view.ExplorerRootsChanged += (_, paths) =>
                {
                    rootsChanged++;
                    savedRoots = paths;
                };
                view.AddSelectedExplorerToRoots();
                Assert.Equal(1, rootsChanged);
                Assert.NotNull(savedRoots);
                Assert.Contains(
                    Path.GetFullPath(nested).TrimEnd('\\', '/'),
                    savedRoots!.Select(path => Path.GetFullPath(path).TrimEnd('\\', '/')));

                // 欠損ルートの Enter／追加はイベントを出さない（クリアだけ走らせない）。
                view.SetExplorerRoots([existing, missing]);
                Flush();
                missingItem = Assert.Single(
                    FindDescendants<TreeViewItem>(view),
                    item => item.Tag is string tag
                        && Path.GetFullPath(tag).TrimEnd('\\', '/')
                            .Equals(missingFull, StringComparison.OrdinalIgnoreCase));
                missingItem.IsSelected = true;
                Flush();
                var opened = 0;
                var replaced = 0;
                view.ExplorerFolderOpened += (_, _) => opened++;
                view.ExplorerReplacePlaylistRequested += (_, _) => replaced++;
                view.OpenSelectedFolder();
                view.ReplacePlaylistFromSelection();
                Assert.Equal(0, opened);
                Assert.Equal(0, replaced);

                view.SetFavorites([missing]);
                Flush();
                view.FocusFavorites();
                Flush();
                LibraryFavoritesActivateEventArgs? favArgs = null;
                view.FavoritesActivated += (_, e) => favArgs = e;
                view.ActivateSelectedFavorites(clearPlaylist: true);
                Assert.Null(favArgs);
            }
            finally
            {
                window?.Close();
                if (Directory.Exists(existing))
                {
                    Directory.Delete(existing, recursive: true);
                }
            }
        });
    }

    [Fact]
    public void ExplorerRoots_MoveAndDropRegister()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var first = Path.Combine(Path.GetTempPath(), "mga-roots-a-" + Guid.NewGuid().ToString("N"));
            var second = Path.Combine(Path.GetTempPath(), "mga-roots-b-" + Guid.NewGuid().ToString("N"));
            var third = Path.Combine(Path.GetTempPath(), "mga-roots-c-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            Directory.CreateDirectory(third);
            Window? window = null;
            try
            {
                var view = new LibraryBrowserView();
                view.SetExplorerRoots([first, second]);
                window = new Window
                {
                    Content = view,
                    Width = 900,
                    Height = 480,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                };
                window.Show();
                Flush();

                var secondFull = Path.GetFullPath(second).TrimEnd('\\', '/');
                var secondItem = Assert.Single(
                    FindDescendants<TreeViewItem>(view),
                    item => item.Tag is string tag
                        && Path.GetFullPath(tag).TrimEnd('\\', '/')
                            .Equals(secondFull, StringComparison.OrdinalIgnoreCase));
                secondItem.IsSelected = true;
                Assert.True(secondItem.Focus());
                Flush();
                Assert.True(view.IsExplorerTreeFocused);

                var moved = 0;
                view.ExplorerRootsChanged += (_, _) => moved++;
                Assert.True(view.TryMoveSelectedExplorerRoots(-1));
                Assert.Equal(1, moved);
                Assert.Equal(
                    [
                        Path.GetFullPath(second).TrimEnd('\\', '/'),
                        Path.GetFullPath(first).TrimEnd('\\', '/'),
                    ],
                    view.ExplorerRoots.Select(path => Path.GetFullPath(path).TrimEnd('\\', '/')).ToArray());
                Flush();
                Assert.True(view.IsExplorerTreeFocused);

                Assert.False(view.TryMoveSelectedExplorerRoots(-1));

                Assert.True(view.TryReorderExplorerRoots([second], 2));
                Assert.Equal(
                    [
                        Path.GetFullPath(first).TrimEnd('\\', '/'),
                        Path.GetFullPath(second).TrimEnd('\\', '/'),
                    ],
                    view.ExplorerRoots.Select(path => Path.GetFullPath(path).TrimEnd('\\', '/')).ToArray());

                var file = Path.Combine(third, "skip.wav");
                File.WriteAllBytes(file, [0]);
                moved = 0;
                view.AddExplorerRootPaths([third, file]);
                Assert.Equal(1, moved);
                Assert.Contains(
                    Path.GetFullPath(third).TrimEnd('\\', '/'),
                    view.ExplorerRoots.Select(path => Path.GetFullPath(path).TrimEnd('\\', '/')));
            }
            finally
            {
                window?.Close();
                foreach (var path in new[] { first, second, third })
                {
                    if (Directory.Exists(path))
                    {
                        Directory.Delete(path, recursive: true);
                    }
                }
            }
        });
    }

    [Fact]
    public void Favorites_DoubleClick_AppendsWithoutClearing()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            if (string.IsNullOrWhiteSpace(music) || !Directory.Exists(music))
            {
                music = Path.GetTempPath();
            }

            var view = new LibraryBrowserView();
            view.SetFavorites([music]);
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            Flush();
            view.FocusFavorites();
            Flush();

            LibraryFavoritesActivateEventArgs? args = null;
            view.FavoritesActivated += (_, e) => args = e;
            view.ActivateSelectedFavorites(clearPlaylist: false);
            Assert.NotNull(args);
            Assert.False(args!.ClearPlaylist);
            Assert.Contains(Path.GetFullPath(music), args.Paths.Select(Path.GetFullPath));

            args = null;
            view.ActivateSelectedFavorites(clearPlaylist: true);
            Assert.NotNull(args);
            Assert.True(args!.ClearPlaylist);
            window.Close();
        });
    }

    [Fact]
    public void RequestListFocus_MovesFromExplorer()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var first = Session("01 a.mp3");
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetSessions([first], first, [first]);
            Flush();
            view.UpdateLayout();
            Flush();

            view.FocusExplorer();
            Flush();
            Assert.True(view.IsExplorerFocused);

            view.RequestListFocus();
            Flush();
            Assert.True(view.IsListKeyboardFocused);
            window.Close();
        });
    }

    [Fact]
    public void RequestListFocus_SurvivesBackgroundTreeRefocus()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var root = Path.Combine(Path.GetTempPath(), "mga-focus-idle-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Window? window = null;
            try
            {
                var first = Session("01 a.mp3");
                var view = new LibraryBrowserView();
                view.SetExplorerRoots([root]);
                window = new Window
                {
                    Content = view,
                    Width = 900,
                    Height = 480,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                };
                window.Show();
                view.SetSessions([first], first, [first]);
                Flush();
                view.UpdateLayout();
                Flush();

                view.FocusExplorer();
                Flush();
                Assert.True(view.IsExplorerTreeFocused);
                var treeItem = view.ExplorerFirstFolder;
                Assert.NotNull(treeItem);

                view.RequestListFocus();
                // フォルダ登録の続き（列幅・行の組み直し）が、一度移したフォーカスをツリーへ戻す。
                view.Dispatcher.BeginInvoke(
                    () => treeItem!.Focus(),
                    DispatcherPriority.Background);
                Flush();
                Assert.True(view.IsListKeyboardFocused);
                Assert.False(view.IsExplorerTreeFocused);

                // ApplicationIdle よりあとでも、選択中のツリー項目がフォーカスを取りに来る。
                treeItem!.Focus();
                Flush();
                Assert.True(view.IsListKeyboardFocused);
                Assert.False(view.IsExplorerTreeFocused);

                // 名前変更・フォルダ削除は FocusExplorer でツリーに戻し、そこへ留まる。
                view.FocusExplorer();
                Flush();
                Assert.True(view.IsExplorerTreeFocused);
                treeItem.Focus();
                Flush();
                Assert.True(view.IsExplorerTreeFocused);
                Assert.False(view.IsListKeyboardFocused);
            }
            finally
            {
                window?.Close();
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        });
    }

    [Fact]
    public void RequestListFocus_ReclaimsAfterPlaylistLosesFocus()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var root = Path.Combine(Path.GetTempPath(), "mga-focus-reclaim-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Window? window = null;
            try
            {
                var first = Session("01 a.mp3");
                var view = new LibraryBrowserView();
                view.SetExplorerRoots([root]);
                var other = new Button { Content = "other", Width = 80, Height = 24 };
                var host = new DockPanel();
                DockPanel.SetDock(other, Dock.Top);
                host.Children.Add(other);
                host.Children.Add(view);
                window = new Window
                {
                    Content = host,
                    Width = 900,
                    Height = 480,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                };
                window.Show();
                view.SetSessions([first], first, [first]);
                Flush();
                view.UpdateLayout();
                Flush();

                view.FocusExplorer();
                Flush();
                Assert.True(view.IsExplorerTreeFocused);

                // フォルダ Enter と同じ順。先頭の再生で RequestListFocus、その後に列の組み直し。
                view.RequestListFocus();
                view.FinishIncrementalSessionLoad();
                Flush();
                Assert.True(view.IsListKeyboardFocused);

                other.Focus();
                Flush();
                Assert.True(view.IsListKeyboardFocused);
                Assert.False(other.IsKeyboardFocusWithin);

                // F5・名前変更・フォルダ削除はホールドを外すので、ツリーから引き戻さない。
                view.FocusExplorer();
                Flush();
                Assert.True(view.IsExplorerTreeFocused);
                other.Focus();
                Flush();
                Assert.True(other.IsKeyboardFocusWithin);
                Assert.False(view.IsListKeyboardFocused);
            }
            finally
            {
                window?.Close();
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        });
    }

    [Fact]
    public void FocusExplorer_CancelsPendingPlaylistFocus()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var first = Session("01 a.mp3");
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetSessions([first], first, [first]);
            Flush();
            view.UpdateLayout();
            Flush();

            view.FocusExplorer();
            Flush();
            var ticket = view.RequestListFocus();
            view.FocusExplorer();
            view.RequestListFocusIfCurrent(ticket);
            Flush();
            Assert.True(view.IsExplorerTreeFocused);
            Assert.False(view.IsPlaylistGridFocused);
            window.Close();
        });
    }

    [Fact]
    public void RequestListFocusIfCurrent_RepeatsWhileTicketHolds()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var first = Session("01 a.mp3");
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetSessions([first], first, [first]);
            Flush();
            view.UpdateLayout();
            Flush();

            view.FocusExplorer();
            Flush();
            var ticket = view.RequestListFocus();
            view.RequestListFocusIfCurrent(ticket);
            Flush();
            Assert.True(view.IsListKeyboardFocused);
            window.Close();
        });
    }

    [Fact]
    public void Favorites_ShiftArrow_SelectsRange()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var root = Path.Combine(Path.GetTempPath(), "mga-fav-shift-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            // お気に入りはフォルダのみ登録（ファイル単体は Resolve で落ちる）。
            var first = Path.Combine(root, "a");
            var second = Path.Combine(root, "b");
            var third = Path.Combine(root, "c");
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            Directory.CreateDirectory(third);
            Window? window = null;
            try
            {
                var view = new LibraryBrowserView();
                view.SetFavorites([first, second, third]);
                window = new Window
                {
                    Content = view,
                    Width = 900,
                    Height = 480,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                };
                window.Show();
                Flush();
                view.FocusFavorites();
                Flush();
                view.MoveFavoritesSelection(1, extend: true);
                Flush();
                Assert.Equal(2, view.SelectedFavoritePaths.Length);
                Assert.Contains(Path.GetFullPath(first), view.SelectedFavoritePaths.Select(Path.GetFullPath));
                Assert.Contains(Path.GetFullPath(second), view.SelectedFavoritePaths.Select(Path.GetFullPath));

                view.MoveFavoritesSelection(1, extend: true);
                Flush();
                Assert.Equal(3, view.SelectedFavoritePaths.Length);

                view.MoveFavoritesSelection(-1, extend: true);
                Flush();
                Assert.Equal(2, view.SelectedFavoritePaths.Length);
                Assert.DoesNotContain(Path.GetFullPath(third), view.SelectedFavoritePaths.Select(Path.GetFullPath));

                view.MoveFavoritesSelection(1, extend: false);
                Flush();
                var last = Assert.Single(view.SelectedFavoritePaths);
                Assert.Equal(Path.GetFullPath(third), Path.GetFullPath(last));

                view.MoveFavoritesSelection(-2, extend: false);
                Flush();
                view.ApplyFavoritesShiftClick(2);
                Flush();
                Assert.Equal(3, view.SelectedFavoritePaths.Length);
                Assert.Contains(Path.GetFullPath(first), view.SelectedFavoritePaths.Select(Path.GetFullPath));
                Assert.Contains(Path.GetFullPath(third), view.SelectedFavoritePaths.Select(Path.GetFullPath));
            }
            finally
            {
                window?.Close();
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        });
    }

    [Fact]
    public void Playlist_ShiftArrow_ExtendsAndShrinksWithoutCollapsing()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var sessions = new DocumentSession[20];
            for (var i = 0; i < sessions.Length; i++)
            {
                sessions[i] = Session($"{i:00}.mp3");
            }

            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetGroup(LibraryFileGroup.None);
            view.SetSessions(sessions, sessions[0], [sessions[0]]);
            Flush();
            view.UpdateLayout();
            Flush();

            for (var i = 0; i < 15; i++)
            {
                view.MoveSelection(1, extend: true);
            }

            Flush();
            Assert.Equal(16, view.SelectedSessions.Length);
            Assert.Same(sessions[15], view.SelectedSession);

            for (var i = 0; i < 5; i++)
            {
                view.MoveSelection(-1, extend: true);
            }

            Flush();
            Assert.Equal(11, view.SelectedSessions.Length);
            Assert.Same(sessions[10], view.SelectedSession);
            Assert.Contains(sessions[0], view.SelectedSessions);
            Assert.Contains(sessions[10], view.SelectedSessions);
            Assert.DoesNotContain(sessions[11], view.SelectedSessions);
            window.Close();
        });
    }

    [Fact]
    public void Playlist_ShiftExtend_AfterQuietSync_KeepsBandWhenExtendingAgain()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var sessions = new DocumentSession[12];
            for (var i = 0; i < sessions.Length; i++)
            {
                sessions[i] = Session($"{i:00}.mp3");
            }

            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetGroup(LibraryFileGroup.None);
            view.SetSessions(sessions, sessions[0], [sessions[0]]);
            Flush();
            view.UpdateLayout();
            Flush();

            for (var i = 0; i < 5; i++)
            {
                view.MoveSelection(1, extend: true);
            }

            Flush();
            Assert.Equal(6, view.SelectedSessions.Length);
            Assert.Same(sessions[5], view.SelectedSession);

            // キー離し後のプレビュー／再生追従相当。帯は残すがアンカーをキャレットへ寄せがち。
            view.SelectSessionQuiet(sessions[5]);
            Flush();
            view.FlushPlaylistMoveDefer();
            Flush();
            Assert.Equal(6, view.SelectedSessions.Length);

            view.MoveSelection(1, extend: true);
            Flush();
            Assert.Equal(7, view.SelectedSessions.Length);
            Assert.Same(sessions[6], view.SelectedSession);
            Assert.Contains(sessions[0], view.SelectedSessions);
            Assert.Contains(sessions[6], view.SelectedSessions);
            window.Close();
        });
    }

    [Fact]
    public void Playlist_ShiftEndAndPage_ExtendsSelection()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var sessions = new DocumentSession[12];
            for (var i = 0; i < sessions.Length; i++)
            {
                sessions[i] = Session($"{i:00}.mp3");
            }

            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 360,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetGroup(LibraryFileGroup.None);
            view.SetSessions(sessions, sessions[2], [sessions[2]]);
            Flush();
            view.UpdateLayout();
            Flush();
            view.FocusList();
            Flush();

            view.MoveSelectionToEdge(1, extend: true);
            Flush();
            view.UpdateLayout();
            Flush();
            Assert.Equal(10, view.SelectedSessions.Length);
            Assert.Same(sessions[^1], view.SelectedSession);
            Assert.Contains(sessions[2], view.SelectedSessions);
            Assert.Contains(sessions[^1], view.SelectedSessions);
            Assert.DoesNotContain(sessions[0], view.SelectedSessions);

            view.SetSessions(sessions, sessions[0], [sessions[0]]);
            Flush();
            view.FocusList();
            Flush();
            view.MoveSelectionPage(1, extend: true);
            Flush();
            view.UpdateLayout();
            Flush();
            Assert.True(view.SelectedSessions.Length > 1);
            Assert.Contains(sessions[0], view.SelectedSessions);
            window.Close();
        });
    }

    [Fact]
    public void Playlist_ShiftAndCtrlSelect_KeepsMultipleRows()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var first = Session("01 a.mp3");
            var second = Session("02 b.mp3");
            var third = Session("03 c.mp3");
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetGroup(LibraryFileGroup.None);
            view.SetSessions([first, second, third], first, [first]);
            Flush();
            view.UpdateLayout();
            Flush();

            view.MoveSelection(1, extend: true);
            Flush();
            Assert.Equal(2, view.SelectedSessions.Length);
            Assert.Contains(first, view.SelectedSessions);
            Assert.Contains(second, view.SelectedSessions);

            view.SelectSessionQuiet(first);
            Flush();
            Assert.Equal(2, view.SelectedSessions.Length);

            view.ApplyPlaylistModifierClick(2, shift: false, control: true);
            Flush();
            Assert.Equal(3, view.SelectedSessions.Length);
            Assert.Contains(third, view.SelectedSessions);

            view.ApplyPlaylistModifierClick(1, shift: false, control: true);
            Flush();
            Assert.Equal(2, view.SelectedSessions.Length);
            Assert.DoesNotContain(second, view.SelectedSessions);

            view.SelectAllRows();
            Flush();
            Assert.Equal(3, view.SelectedSessions.Length);
            window.Close();
        });
    }

    [Fact]
    public void Playlist_SelectSameColorRows_SelectsMatchingMarkerOnly()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var previous = AppStorage.Settings.FileNameColorAssignments;
            var redA = Session("red-a.mp3");
            var redB = Session("red-b.mp3");
            var blue = Session("blue.mp3");
            var plain = Session("plain.mp3");
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            try
            {
                view.SetGroup(LibraryFileGroup.None);
                view.SetSessions([redA, redB, blue, plain], redA, [redA]);
                Flush();
                view.UpdateLayout();
                Flush();

                Assert.False(view.CanSelectSameColorRows());

                view.SetManualFileNameColor(
                    [redA.Document.SourcePath!, redB.Document.SourcePath!],
                    colorIndex: 3);
                view.SetManualFileNameColor([blue.Document.SourcePath!], colorIndex: 10);
                Flush();

                Assert.True(view.CanSelectSameColorRows());
                view.SelectSameColorRows();
                Flush();
                Assert.Equal(2, view.SelectedSessions.Length);
                Assert.Contains(redA, view.SelectedSessions);
                Assert.Contains(redB, view.SelectedSessions);
                Assert.DoesNotContain(blue, view.SelectedSessions);
                Assert.DoesNotContain(plain, view.SelectedSessions);

                view.SelectSessionQuiet(blue);
                Flush();
                view.SelectSameColorRows();
                Flush();
                Assert.Single(view.SelectedSessions);
                Assert.Same(blue, view.SelectedSessions[0]);
            }
            finally
            {
                AppStorage.Settings.FileNameColorAssignments = previous;
                AppStorage.Save();
                window.Close();
            }
        });
    }

    [Fact]
    public void Playlist_CheckSelectMode_SpaceTogglesAndRangeChecks()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var first = Session("01 a.mp3");
            var second = Session("02 b.mp3");
            var third = Session("03 c.mp3");
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetGroup(LibraryFileGroup.None);
            view.SetSessions([first, second, third], first, [first]);
            Flush();
            view.UpdateLayout();
            Flush();

            Assert.False(view.CheckSelectMode);
            view.ToggleCheckSelectMode();
            Flush();
            Assert.True(view.CheckSelectMode);
            // オン時、既に選んでいた first へチェックが載る。
            Assert.Equal([first], view.SelectedSessions);

            // Space でカーソル行のチェックを外す（トグル）。
            view.ApplyPlaylistSpaceCheck();
            Flush();
            Assert.Empty(view.SelectedSessions);

            view.ApplyPlaylistSpaceCheck();
            Flush();
            Assert.Equal([first], view.SelectedSessions);

            // Shift 範囲で 1〜3 を選び、Space ですべてチェック。
            view.MoveSelection(2, extend: true);
            Flush();
            view.ApplyPlaylistSpaceCheck();
            Flush();
            Assert.Equal(3, view.SelectedSessions.Length);

            // 範囲がすべてチェック済みのとき Space でまとめて外す。
            view.ApplyPlaylistSpaceCheck();
            Flush();
            Assert.Empty(view.SelectedSessions);

            view.ApplyPlaylistSpaceCheck();
            Flush();
            Assert.Equal(3, view.SelectedSessions.Length);
            Assert.Contains(first, view.SelectedSessions);
            Assert.Contains(second, view.SelectedSessions);
            Assert.Contains(third, view.SelectedSessions);

            // モードを切るとチェックは忘れ、チェック行は選択として残る。
            view.ToggleCheckSelectMode();
            Flush();
            Assert.False(view.CheckSelectMode);
            Assert.Equal(3, view.SelectedSessions.Length);
            Assert.Contains(first, view.SelectedSessions);
            Assert.Contains(second, view.SelectedSessions);
            Assert.Contains(third, view.SelectedSessions);

            // オンに戻すと、残っていた選択へチェックが載る（操作対象＝その3件）。
            view.ToggleCheckSelectMode();
            Flush();
            Assert.True(view.CheckSelectMode);
            Assert.Equal(3, view.SelectedSessions.Length);
            Assert.Contains(first, view.SelectedSessions);
            Assert.Contains(second, view.SelectedSessions);
            Assert.Contains(third, view.SelectedSessions);
            Assert.Equal(3, view.PlaylistBandSessions().Length);

            window.Close();
        });
    }

    [Fact]
    public void CheckSelectMode_On_SeedsChecksFromCurrentBandSelection()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var first = Session("01 a.wav");
            var second = Session("02 b.wav");
            var third = Session("03 c.wav");
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetGroup(LibraryFileGroup.None);
            view.SetSessions([first, second, third], first, [first, second]);
            Flush();
            view.UpdateLayout();
            Flush();
            Assert.False(view.CheckSelectMode);
            Assert.Equal([first, second], view.SelectedSessions);

            view.ToggleCheckSelectMode();
            Flush();
            Assert.True(view.CheckSelectMode);
            Assert.Equal([first, second], view.SelectedSessions);
            Assert.Equal([first, second], view.PlaylistBandSessions());
            Assert.DoesNotContain(third, view.SelectedSessions);
            window.Close();
        });
    }

    [Fact]
    public void InvertPlaylistSelection_InvertsBandWhenCheckModeOff()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var first = Session("01 a.wav");
            var second = Session("02 b.wav");
            var third = Session("03 c.wav");
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetGroup(LibraryFileGroup.None);
            view.SetSessions([first, second, third], first, [first]);
            Flush();
            view.UpdateLayout();
            Flush();
            Assert.False(view.CheckSelectMode);
            Assert.Equal([first], view.SelectedSessions);

            view.InvertPlaylistSelection();
            Flush();
            Assert.Equal(2, view.SelectedSessions.Length);
            Assert.DoesNotContain(first, view.SelectedSessions);
            Assert.Contains(second, view.SelectedSessions);
            Assert.Contains(third, view.SelectedSessions);
            // キャレットは末尾ではなく、元位置に近い選択行（second）。
            Assert.Same(second, view.SelectedSession);
            window.Close();
        });
    }

    [Fact]
    public void InvertPlaylistSelection_InvertsChecksWhenCheckModeOn()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var first = Session("01 a.wav");
            var second = Session("02 b.wav");
            var third = Session("03 c.wav");
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetGroup(LibraryFileGroup.None);
            view.SetSessions([first, second, third], first, [first, second]);
            Flush();
            view.UpdateLayout();
            Flush();
            view.ToggleCheckSelectMode();
            Flush();
            Assert.True(view.CheckSelectMode);
            Assert.Equal([first, second], view.SelectedSessions);

            view.InvertPlaylistSelection();
            Flush();
            Assert.Equal([third], view.SelectedSessions);
            Assert.Equal([first, second], view.PlaylistBandSessions());
            window.Close();
        });
    }

    [Fact]
    public void InvertPlaylistSelection_GroupedFolder_InvertsWholePlaylist()
    {
        RunSta(() =>
        {
            EnsureTheme();
            // 再帰読込みと同様に複数フォルダへ分かれる。
            // ヘッダ和集合で絞ったあと全体反転→クリアで残りを残す用途なのでグループ限定にしない。
            var alphaA = Session(Path.Combine("Alpha", "a.wav"));
            var alphaB = Session(Path.Combine("Alpha", "b.wav"));
            var betaA = Session(Path.Combine("Beta", "a.wav"));
            var betaB = Session(Path.Combine("Beta", "b.wav"));
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetGroup(LibraryFileGroup.Folder);
            view.SetSessions([alphaA, alphaB, betaA, betaB], alphaA, [alphaA]);
            Flush();
            view.UpdateLayout();
            Flush();
            Assert.Equal([alphaA], view.SelectedSessions);

            view.InvertPlaylistSelection();
            Flush();
            Assert.Equal(3, view.SelectedSessions.Length);
            Assert.DoesNotContain(alphaA, view.SelectedSessions);
            Assert.Contains(alphaB, view.SelectedSessions);
            Assert.Contains(betaA, view.SelectedSessions);
            Assert.Contains(betaB, view.SelectedSessions);
            window.Close();
        });
    }

    [Fact]
    public void InvertPlaylistSelection_CheckMode_EmptyChecksUsesBandThenInverts()
    {
        // 帯だけ2件・チェック空（ヘッダ絞り込み後の未同期に相当）→反転で残り1件が選ばれる。
        // 空チェックをそのまま反転すると全チェックになり、クリアで全件消える退行を防ぐ。
        RunSta(() =>
        {
            EnsureTheme();
            var keep = Session("01 keep.wav");
            var dropA = Session("02 drop-a.wav");
            var dropB = Session("03 drop-b.wav");
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetGroup(LibraryFileGroup.Folder);
            view.SetSessions([keep, dropA, dropB], dropA, [dropA, dropB]);
            Flush();
            view.UpdateLayout();
            Flush();
            view.ToggleCheckSelectMode();
            Flush();
            Assert.True(view.CheckSelectMode);
            Assert.Equal(2, view.SelectedSessions.Length);

            // 複数帯がすべてチェック済み → Space でチェックだけ外す（帯は残る）。
            view.ApplyPlaylistSpaceCheck();
            Flush();
            Assert.Equal(2, view.PlaylistBandSessions().Length);
            Assert.Equal(2, view.SelectedSessions.Length);

            view.InvertPlaylistSelection();
            Flush();
            Assert.Equal([keep], view.SelectedSessions);
            window.Close();
        });
    }

    [Fact]
    public void CheckSelectMode_MoveSelection_DoesNotSelectAllCheckedAsBand()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var first = Session("01 a.wav");
            var second = Session("02 b.wav");
            var third = Session("03 c.wav");
            var fourth = Session("04 d.wav");
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetGroup(LibraryFileGroup.None);
            view.SetSessions([first, second, third, fourth], first, [first]);
            Flush();
            view.UpdateLayout();
            Flush();
            view.ToggleCheckSelectMode();
            Flush();
            // オン時に帯選択だった first へチェックが載る。
            Assert.Equal([first], view.SelectedSessions);

            // third もチェック。キャレットは未チェックの fourth。
            view.MoveSelection(2);
            Flush();
            view.ApplyPlaylistSpaceCheck();
            Flush();
            view.MoveSelection(1);
            Flush();
            Assert.Equal([first, third], view.SelectedSessions);
            Assert.Equal([fourth], view.PlaylistBandSessions());

            // ↑ でチェック済み third へ。再生追従／行更新相当も挟む。
            view.MoveSelection(-1);
            Flush();
            view.SelectSessionQuiet(third);
            Flush();
            view.UpdateSessionRow(third);
            Flush();

            Assert.Equal([first, third], view.SelectedSessions);
            Assert.Equal([third], view.PlaylistBandSessions());
            Assert.Equal(1, view.PlaylistSelectedCount);
            Assert.Same(third, view.SelectedSession);

            view.MoveSelection(-1);
            Flush();
            view.SelectSessionQuiet(second);
            Flush();
            Assert.Equal([first, third], view.SelectedSessions);
            Assert.Equal([second], view.PlaylistBandSessions());
            window.Close();
        });
    }

    [Fact]
    public void NextPlaylistSession_WrapsToFirst()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var first = Session("01 a.mp3");
            var second = Session("02 b.mp3");
            var third = Session("03 c.mp3");
            var view = new LibraryBrowserView();
            view.SetSessions([first, second, third], first, [first]);

            var cursor = first;
            for (var i = 0; i < 3; i++)
            {
                var next = view.NextPlaylistSession(cursor);
                Assert.NotNull(next);
                Assert.NotSame(cursor, next);
                cursor = next;
            }

            Assert.Same(first, cursor);

            view.SetSessions([first], first, [first]);
            Assert.Same(first, view.NextPlaylistSession(first));
            Assert.Same(first, view.NextPlaylistSession(null));

            view.SetSessions([first, second, third], first, [first]);
            Assert.Same(third, view.PreviousPlaylistSession(first));
            Assert.Same(first, view.PreviousPlaylistSession(second));
            Assert.Same(second, view.PreviousPlaylistSession(third));
            Assert.Same(third, view.PreviousPlaylistSession(null));
            view.SetSessions([first], first, [first]);
            Assert.Same(first, view.PreviousPlaylistSession(first));
        });
    }

    [Fact]
    public void NextPlaylistSession_FollowsGroupedDisplayOrder()
    {
        RunSta(() =>
        {
            EnsureTheme();
            // Album ソートだと zebra / alpha / zebra の順。Artist グループだと
            // Alpha → Zebra の表示順になり、_items 順とは食い違う。
            var alphaOnZebra = Session("01.mp3");
            alphaOnZebra.Document.ApplyTags(new AudioFileTags
            {
                Probed = true,
                Artist = "Alpha",
                Album = "Zebra",
                Title = "A",
            });
            var zebraOnAlpha = Session("02.mp3");
            zebraOnAlpha.Document.ApplyTags(new AudioFileTags
            {
                Probed = true,
                Artist = "Zebra",
                Album = "Alpha",
                Title = "B",
            });
            var zebraOnZebra = Session("03.mp3");
            zebraOnZebra.Document.ApplyTags(new AudioFileTags
            {
                Probed = true,
                Artist = "Zebra",
                Album = "Zebra",
                Title = "C",
            });

            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 400,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetSessions(
                [alphaOnZebra, zebraOnAlpha, zebraOnZebra],
                alphaOnZebra,
                [alphaOnZebra]);
            view.SetGroup(LibraryFileGroup.Artist);
            Flush();

            Assert.Same(zebraOnAlpha, view.NextPlaylistSession(alphaOnZebra));
            Assert.Same(zebraOnZebra, view.NextPlaylistSession(zebraOnAlpha));
            Assert.Same(alphaOnZebra, view.NextPlaylistSession(zebraOnZebra));
            Assert.Same(zebraOnZebra, view.PreviousPlaylistSession(alphaOnZebra));

            view.MoveSelection(1);
            Flush();
            Assert.Same(zebraOnAlpha, view.SelectedSession);
            window.Close();
        });
    }

    [Fact]
    public void MoveSelection_FolderGroup_FollowsParentFolderName_EvenIfAppendedOutOfOrder()
    {
        RunSta(() =>
        {
            EnsureTheme();
            // 走査順が Zebra → Alpha でも、親フォルダグループ＋名前ソートなら Alpha が先。
            var zebraA = Session(@"D:\music\Zebra\aaa.wav");
            var alphaZ = Session(@"D:\music\Alpha\zzz.wav");
            var alphaM = Session(@"D:\music\Alpha\mmm.wav");

            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 400,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetSort(new LibraryPlaylistSort(LibraryFileColumn.Name, LibrarySortDirection.Ascending));
            view.SetGroup(LibraryFileGroup.Folder);
            view.AppendSessionsRaw([zebraA, alphaZ, alphaM], zebraA);
            Flush();

            // ロック前でも行送りは見出し名順（Alpha → Zebra）。グループ内は追加順のまま。
            Assert.Same(alphaZ, view.FirstPlaylistSession);
            Assert.Same(alphaM, view.NextPlaylistSession(alphaZ));
            Assert.Same(zebraA, view.NextPlaylistSession(alphaM));

            view.LockPlaylistOrder(alphaM, [alphaM]);
            view.UpdateSessionRows([zebraA, alphaZ, alphaM]);
            Flush();

            // ロック後は Alpha 内も名前順（mmm → zzz → aaa）。
            Assert.Same(alphaM, view.FirstPlaylistSession);
            view.MoveSelection(1, extend: true);
            Flush();
            Assert.Equal(2, view.SelectedSessions.Length);
            Assert.Contains(alphaM, view.SelectedSessions);
            Assert.Contains(alphaZ, view.SelectedSessions);
            Assert.DoesNotContain(zebraA, view.SelectedSessions);

            view.MoveSelection(1);
            Flush();
            Assert.Same(zebraA, view.SelectedSession);
            window.Close();
        });
    }

    [Fact]
    public void MoveSelectionByGroup_JumpsToNextGroupAndExtends()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var alphaA = Session("01.mp3");
            alphaA.Document.ApplyTags(new AudioFileTags
            {
                Probed = true,
                Artist = "Alpha",
                Album = "A",
                Title = "1",
            });
            var alphaB = Session("02.mp3");
            alphaB.Document.ApplyTags(new AudioFileTags
            {
                Probed = true,
                Artist = "Alpha",
                Album = "A",
                Title = "2",
            });
            var zebraA = Session("03.mp3");
            zebraA.Document.ApplyTags(new AudioFileTags
            {
                Probed = true,
                Artist = "Zebra",
                Album = "Z",
                Title = "3",
            });
            var zebraB = Session("04.mp3");
            zebraB.Document.ApplyTags(new AudioFileTags
            {
                Probed = true,
                Artist = "Zebra",
                Album = "Z",
                Title = "4",
            });

            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 400,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetSessions([alphaA, alphaB, zebraA, zebraB], alphaA, [alphaA]);
            view.SetGroup(LibraryFileGroup.Artist);
            Flush();

            view.MoveSelectionByGroup(1, extend: true);
            Flush();
            // 次グループ先頭は含めず、現グループ末尾まで。
            Assert.Same(alphaB, view.SelectedSession);
            Assert.Equal(2, view.SelectedSessions.Length);
            Assert.Contains(alphaA, view.SelectedSessions);
            Assert.Contains(alphaB, view.SelectedSessions);

            view.MoveSelectionByGroup(1, extend: true);
            Flush();
            // 既に端なので隣グループ末尾まで伸ばす。
            Assert.Same(zebraB, view.SelectedSession);
            Assert.Equal(4, view.SelectedSessions.Length);
            Assert.Contains(zebraA, view.SelectedSessions);
            Assert.Contains(zebraB, view.SelectedSessions);

            view.MoveSelectionByGroup(-1, extend: false);
            Flush();
            Assert.Same(alphaA, view.SelectedSession);
            Assert.Equal([alphaA], view.SelectedSessions);

            Assert.True(view.TryGetDisplayedNeighbors(
                alphaB,
                [alphaA, alphaB],
                out var next,
                out var prev));
            Assert.Same(zebraA, next);
            Assert.Null(prev);
            window.Close();
        });
    }

    [Fact]
    public void MoveSelection_WrapsAtEnds()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var first = Session("01 a.mp3");
            var second = Session("02 b.mp3");
            var third = Session("03 c.mp3");
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 400,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetSessions([first, second, third], first, [first]);
            Flush();

            view.MoveSelection(-1);
            Flush();
            Assert.Same(third, view.SelectedSession);

            view.MoveSelection(1);
            Flush();
            Assert.Same(first, view.SelectedSession);

            view.SetSessions([first, second, third], third, [third]);
            Flush();
            view.MoveSelection(1);
            Flush();
            Assert.Same(first, view.SelectedSession);

            // 長押し連続（wrap=false）は端で一旦止まる。
            view.SetSessions([first, second, third], third, [third]);
            Flush();
            view.MoveSelection(1, wrap: false);
            Flush();
            Assert.Same(third, view.SelectedSession);

            view.SetSessions([first, second, third], first, [first]);
            Flush();
            view.MoveSelection(-1, wrap: false);
            Flush();
            Assert.Same(first, view.SelectedSession);

            view.SetSessions([first, second, third], first, [first]);
            Flush();
            view.MoveSelection(-1, extend: true);
            Flush();
            Assert.Same(first, view.SelectedSession);
            window.Close();
        });
    }

    [Fact]
    public void NextPlaylistSession_ShufflePlaysEachOncePerCycle()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var tracks = Enumerable.Range(0, 5)
                .Select(i => Session($"{i:00} t.mp3"))
                .ToArray();
            var view = new LibraryBrowserView();
            view.SetSessions(tracks, tracks[0], [tracks[0]]);
            view.ShuffleEnabled = true;

            var seen = new HashSet<DocumentSession> { tracks[0] };
            var cursor = tracks[0];
            for (var i = 1; i < 5; i++)
            {
                var next = view.NextPlaylistSession(cursor);
                Assert.NotNull(next);
                Assert.True(seen.Add(next!));
                cursor = next!;
            }

            Assert.Equal(5, seen.Count);
            var again = view.NextPlaylistSession(cursor);
            Assert.NotNull(again);
            Assert.NotSame(cursor, again);
        });
    }

    [Fact]
    public void FormatOptionToggleToast_ShowsOnOff()
    {
        Assert.Equal("Silent Skip: ON", MainWindow.FormatOptionToggleToast("Silent Skip", enabled: true));
        Assert.Equal("Random: OFF", MainWindow.FormatOptionToggleToast("Random", enabled: false));
    }

    [Fact]
    public void FormatLayoutModeToast_IsAlwaysEnglish()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.Japanese);
            Assert.Equal("Player mode 1", MainWindow.FormatLayoutModeToast(WindowLayoutSlot.F9));
            Assert.Equal("Player mode 2", MainWindow.FormatLayoutModeToast(WindowLayoutSlot.F10));
            Assert.Equal("Editor mode 1", MainWindow.FormatLayoutModeToast(WindowLayoutSlot.F11));
            Assert.Equal("Editor mode 2", MainWindow.FormatLayoutModeToast(WindowLayoutSlot.F12));
            UiStrings.SetLanguage(UiLanguage.English);
            Assert.Equal("Player mode 1", MainWindow.FormatLayoutModeToast(WindowLayoutSlot.F9));
            Assert.Equal("Editor mode 2", MainWindow.FormatLayoutModeToast(WindowLayoutSlot.F12));
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }

    [Fact]
    public void FormatLayoutModeToast_ToggleFullscreen_AddsSecondLine()
    {
        Assert.Equal(
            "Player mode 1",
            MainWindow.FormatLayoutModeToast(
                WindowLayoutSlot.F9,
                LayoutFullscreenMode.On,
                fullscreenOn: true));
        Assert.Equal(
            "Editor mode 1\nFullscreen: ON",
            MainWindow.FormatLayoutModeToast(
                WindowLayoutSlot.F11,
                LayoutFullscreenMode.Toggle,
                fullscreenOn: true));
        Assert.Equal(
            "Editor mode 2\nFullscreen: OFF",
            MainWindow.FormatLayoutModeToast(
                WindowLayoutSlot.F12,
                LayoutFullscreenMode.Toggle,
                fullscreenOn: false));
    }

    [Fact]
    public void AcrylicToastHost_SplitLines_DropsEmptySecondLine()
    {
        AcrylicToastHost.SplitLines("Player mode 1\n", out var primary, out var secondary);
        Assert.Equal("Player mode 1", primary);
        Assert.Equal(string.Empty, secondary);

        AcrylicToastHost.SplitLines(
            "Editor mode 1\nFullscreen: ON",
            out primary,
            out secondary);
        Assert.Equal("Editor mode 1", primary);
        Assert.Equal("Fullscreen: ON", secondary);
    }

    [Fact]
    public void CheckSelectModeChanged_RaisesOnToggle_NotOnSilentSet()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var view = new LibraryBrowserView();
            var raised = 0;
            view.CheckSelectModeChanged += (_, _) => raised++;

            view.ToggleCheckSelectMode();
            Assert.True(view.CheckSelectMode);
            Assert.Equal(1, raised);

            view.SetCheckSelectMode(false, raise: false);
            Assert.False(view.CheckSelectMode);
            Assert.Equal(1, raised);

            view.SetCheckSelectMode(true);
            Assert.True(view.CheckSelectMode);
            Assert.Equal(2, raised);
        });
    }

    [Fact]
    public void ShuffleEnabled_ToggleWorksWhenPlaylistEmpty()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var view = new LibraryBrowserView();
            Assert.Equal(0, view.BoundRowCount);
            view.ToggleShuffle();
            Assert.True(view.ShuffleEnabled);
            view.ToggleShuffle();
            Assert.False(view.ShuffleEnabled);
        });
    }

    [Fact]
    public void ShuffleEnabled_DefaultsOff()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var view = new LibraryBrowserView();
            Assert.False(view.ShuffleEnabled);
        });
    }

    [Fact]
    public void LabelLibraryShuffle_IsEnglishWithoutAccessKey()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.Japanese);
            Assert.Equal("Random", UiStrings.LabelLibraryShuffle);
            Assert.Null(MenuAccessKeys.Read(UiStrings.LabelLibraryShuffle));
            Assert.DoesNotContain("Alt+", UiStrings.LabelLibraryShuffle, StringComparison.OrdinalIgnoreCase);
            UiStrings.SetLanguage(UiLanguage.English);
            Assert.Equal("Random", UiStrings.LabelLibraryShuffle);
            Assert.Null(MenuAccessKeys.Read(UiStrings.LabelLibraryShuffle));
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }

    [Fact]
    public void LibraryGroupChrome_LabelAndNoneStayEnglish_OptionsMatchColumnLabels()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.Japanese);
            Assert.Equal("Group", UiStrings.LibraryGroupLabel);
            Assert.Equal("None", UiStrings.LibraryGroupNone);
            Assert.Equal(UiStrings.LibraryColumnParentFolder, UiStrings.LibraryGroupFolder);
            Assert.Equal(UiStrings.LibraryColumnSampleRate, UiStrings.LibraryGroupSampleRate);
            Assert.Equal(UiStrings.LibraryColumnBitDepth, UiStrings.LibraryGroupBitDepth);
            Assert.Equal(UiStrings.LibraryColumnChannels, UiStrings.LibraryGroupChannels);
            Assert.Equal("親フォルダ", UiStrings.LibraryGroupFolder);
            Assert.Equal("レート", UiStrings.LibraryGroupSampleRate);
            Assert.Equal("ビット", UiStrings.LibraryGroupBitDepth);
            Assert.Equal("Ch", UiStrings.LibraryGroupChannels);
            UiStrings.SetLanguage(UiLanguage.English);
            Assert.Equal("Group", UiStrings.LibraryGroupLabel);
            Assert.Equal("None", UiStrings.LibraryGroupNone);
            Assert.Equal("Parent folder", UiStrings.LibraryGroupFolder);
            Assert.Equal("Rate", UiStrings.LibraryGroupSampleRate);
            Assert.Equal("Bits", UiStrings.LibraryGroupBitDepth);
            Assert.Equal("Ch", UiStrings.LibraryGroupChannels);
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }

    [Fact]
    public void AutoPlayEnabled_DefaultsOn()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var view = new LibraryBrowserView();
            Assert.True(view.AutoPlayEnabled);
        });
    }

    [Fact]
    public void AutoPlayEnabled_ToggleWorksWhenPlaylistEmpty()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var view = new LibraryBrowserView();
            Assert.Equal(0, view.BoundRowCount);
            view.ToggleAutoPlay();
            Assert.False(view.AutoPlayEnabled);
            view.ToggleAutoPlay();
            Assert.True(view.AutoPlayEnabled);
        });
    }

    [Fact]
    public void LabelLibraryAutoPlay_IsEnglishWithoutAccessKey()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.Japanese);
            Assert.Equal("Auto Play", UiStrings.LabelLibraryAutoPlay);
            Assert.Null(MenuAccessKeys.Read(UiStrings.LabelLibraryAutoPlay));
            Assert.DoesNotContain("Alt+", UiStrings.LabelLibraryAutoPlay, StringComparison.OrdinalIgnoreCase);
            UiStrings.SetLanguage(UiLanguage.English);
            Assert.Equal("Auto Play", UiStrings.LabelLibraryAutoPlay);
            Assert.Null(MenuAccessKeys.Read(UiStrings.LabelLibraryAutoPlay));
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }

    [Fact]
    public void AutoPlayNextEnabled_ToggleWorksWhenPlaylistEmpty()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var view = new LibraryBrowserView();
            Assert.Equal(0, view.BoundRowCount);
            view.ToggleAutoPlayNext();
            Assert.True(view.AutoPlayNextEnabled);
            view.ToggleAutoPlayNext();
            Assert.False(view.AutoPlayNextEnabled);
        });
    }

    [Fact]
    public void AutoPlayNextEnabled_DefaultsOff()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var view = new LibraryBrowserView();
            Assert.False(view.AutoPlayNextEnabled);
        });
    }

    [Fact]
    public void LabelLibraryAutoPlayNext_IsEnglishWithoutAccessKey()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.Japanese);
            Assert.Equal("Auto Next", UiStrings.LabelLibraryAutoPlayNext);
            Assert.Null(MenuAccessKeys.Read(UiStrings.LabelLibraryAutoPlayNext));
            Assert.DoesNotContain("Alt+", UiStrings.LabelLibraryAutoPlayNext, StringComparison.OrdinalIgnoreCase);
            UiStrings.SetLanguage(UiLanguage.English);
            Assert.Equal("Auto Next", UiStrings.LabelLibraryAutoPlayNext);
            Assert.Null(MenuAccessKeys.Read(UiStrings.LabelLibraryAutoPlayNext));
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }

    [Fact]
    public void AutoLoopEnabled_ToggleWorksWhenPlaylistEmpty()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var view = new LibraryBrowserView();
            Assert.Equal(0, view.BoundRowCount);
            view.ToggleAutoLoop();
            Assert.True(view.AutoLoopEnabled);
            view.ToggleAutoLoop();
            Assert.False(view.AutoLoopEnabled);
        });
    }

    [Fact]
    public void AutoLoopEnabled_DefaultsOff()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var view = new LibraryBrowserView();
            Assert.False(view.AutoLoopEnabled);
        });
    }

    [Fact]
    public void SelectAllRows_DefersHeaderStructureUnionUntilSettled()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var path1 = Path.Combine(
                Path.GetTempPath(),
                "mga-header-selall-1-" + Guid.NewGuid().ToString("N") + ".wav");
            var path2 = Path.Combine(
                Path.GetTempPath(),
                "mga-header-selall-2-" + Guid.NewGuid().ToString("N") + ".wav");
            WriteMinimalWav(path1);
            File.Copy(path1, path2, overwrite: true);
            try
            {
                var first = new DocumentSession(AudioDocument.CreateDeferred(path1));
                var second = new DocumentSession(AudioDocument.CreateDeferred(path2));
                var view = new LibraryBrowserView();
                var window = new Window
                {
                    Content = view,
                    Width = 900,
                    Height = 480,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                };
                window.Show();
                Flush();
                view.SetSessions([first, second], first, [first]);
                view.ToggleHeaderStructureVisible(focusWhenShown: false);
                Flush();
                Assert.True(view.HeaderStructureVisible);
                Assert.False(view.IsHeaderStructureRefreshDeferred);

                // 全選択のハイライトは即時。和集合解析だけ保留する。
                view.SelectAllRows();
                Assert.Equal(2, view.SelectedSessions.Length);
                Assert.True(view.IsHeaderStructureRefreshDeferred);

                view.FlushDeferredHeaderStructureRefresh();
                Assert.False(view.IsHeaderStructureRefreshDeferred);
                PumpUntil(
                    () => view.HeaderStructureLoadedPath is { } loaded
                        && loaded.Contains("\u0001union\u0001", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(3));
                Assert.Contains("\u0001union\u0001", view.HeaderStructureLoadedPath, StringComparison.Ordinal);
                Assert.NotEmpty(view.HeaderStructureTree.Items);
                window.Close();
            }
            finally
            {
                foreach (var path in new[] { path1, path2 })
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
        });
    }

    [Fact]
    public void HeaderStructureVisible_DefaultsOffAndToggles()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var path = Path.Combine(
                Path.GetTempPath(),
                "mga-header-off-" + Guid.NewGuid().ToString("N") + ".wav");
            var pcm = new byte[4];
            var fmt = new byte[]
            {
                1, 0, 1, 0, 0x44, 0xAC, 0, 0, 0x88, 0x58, 0x01, 0, 2, 0, 16, 0,
            };
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + pcm.Length);
                w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                w.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
                w.Write(fmt.Length);
                w.Write(fmt);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                w.Write(pcm.Length);
                w.Write(pcm);
                File.WriteAllBytes(path, ms.ToArray());
            }

            var session = new DocumentSession(AudioDocument.CreateDeferred(path));
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            try
            {
                window.Show();
                Flush();
                view.UpdateLayout();
                Flush();
                Assert.False(view.HeaderStructureVisible);
                Assert.Equal(Visibility.Collapsed, view.StructureSplitter.Visibility);

                // オフ中は選択してもヘッダ解析しない。
                view.SetSessions([session], session, [session]);
                Flush();
                Assert.Empty(view.HeaderStructureTree.Items);

                // オフのまま追加（選択あり／なし）しても解析しない。
                var path2 = Path.Combine(
                    Path.GetTempPath(),
                    "mga-header-off-add-" + Guid.NewGuid().ToString("N") + ".wav");
                var path3 = Path.Combine(
                    Path.GetTempPath(),
                    "mga-header-off-add2-" + Guid.NewGuid().ToString("N") + ".wav");
                File.Copy(path, path2, overwrite: true);
                File.Copy(path, path3, overwrite: true);
                try
                {
                    view.AppendSession(
                        new DocumentSession(AudioDocument.CreateDeferred(path2)),
                        select: false);
                    Flush();
                    Assert.Empty(view.HeaderStructureTree.Items);

                    view.AppendSession(
                        new DocumentSession(AudioDocument.CreateDeferred(path2)),
                        select: true);
                    Flush();
                    Assert.Empty(view.HeaderStructureTree.Items);

                    view.ToggleHeaderStructureVisible();
                    Flush();
                    Assert.True(view.HeaderStructureVisible);
                    Assert.Equal(Visibility.Visible, view.StructureSplitter.Visibility);
                    Assert.True(view.StructureColumnWidth > 1);
                    Assert.NotEmpty(view.HeaderStructureTree.Items);
                    Assert.True(view.IsHeaderStructureFocused);

                    view.ToggleHeaderStructureVisible();
                    Flush();
                    Assert.False(view.HeaderStructureVisible);
                    Assert.Equal(Visibility.Collapsed, view.StructureSplitter.Visibility);
                    Assert.Empty(view.HeaderStructureTree.Items);

                    // 一度オン→オフのあと、さらに追加しても解析しない。
                    view.AppendSession(
                        new DocumentSession(AudioDocument.CreateDeferred(path3)),
                        select: true);
                    Flush();
                    Assert.Empty(view.HeaderStructureTree.Items);

                    // 再表示後、ナビキー押し中は選択だけ進め、離してからヘッダを読む。
                    view.ToggleHeaderStructureVisible(focusWhenShown: false);
                    Flush();
                    Assert.True(view.HeaderStructureVisible);
                    Assert.False(view.IsHeaderStructureRefreshDeferred);
                    var beforePath = view.HeaderStructureLoadedPath;
                    Assert.False(string.IsNullOrWhiteSpace(beforePath));

                    var keyHeld = true;
                    view.PlaylistNavigationKeyHeldForTest = () => keyHeld;
                    try
                    {
                        // path3 は末尾選択なので ↑ で前の曲へ。
                        view.MoveSelection(-1, wrap: false);
                        Flush();
                        Assert.True(view.IsHeaderStructureRefreshDeferred);
                        Assert.Equal(beforePath, view.HeaderStructureLoadedPath, StringComparer.OrdinalIgnoreCase);

                        keyHeld = false;
                        view.FlushDeferredHeaderStructureRefresh();
                        Flush();
                        Assert.False(view.IsHeaderStructureRefreshDeferred);
                        Assert.NotEqual(
                            beforePath,
                            view.HeaderStructureLoadedPath,
                            StringComparer.OrdinalIgnoreCase);
                    }
                    finally
                    {
                        view.PlaylistNavigationKeyHeldForTest = null;
                    }
                }
                finally
                {
                    foreach (var p in new[] { path2, path3 })
                    {
                        try
                        {
                            File.Delete(p);
                        }
                        catch
                        {
                            // ignore
                        }
                    }
                }
            }
            finally
            {
                window.Close();
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    // ignore
                }
            }
        });
    }

    [Fact]
    public void HeaderStructureChunk_AppliesPlaylistSelectionOnKeyRelease()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var path1 = Path.Combine(
                Path.GetTempPath(),
                "mga-header-chunk-defer-1-" + Guid.NewGuid().ToString("N") + ".wav");
            var path2 = Path.Combine(
                Path.GetTempPath(),
                "mga-header-chunk-defer-2-" + Guid.NewGuid().ToString("N") + ".wav");
            WriteMinimalWav(path1);
            File.Copy(path1, path2, overwrite: true);
            try
            {
                var first = new DocumentSession(AudioDocument.CreateDeferred(path1));
                var second = new DocumentSession(AudioDocument.CreateDeferred(path2));
                var view = new LibraryBrowserView();
                var window = new Window
                {
                    Content = view,
                    Width = 900,
                    Height = 480,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                };
                window.Show();
                Flush();
                view.SetSessions([first, second], first, [first, second]);
                view.ToggleHeaderStructureVisible(focusWhenShown: true);
                Flush();
                PumpUntil(
                    () => view.HeaderStructureLoadedPath is { } loaded
                        && loaded.Contains("\u0001union\u0001", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(3));

                var items = EditorHeaderChunkStripView.FlattenVisibleTreeItems(
                    view.HeaderStructureTree.Items);
                Assert.True(items.Count >= 2);

                // クリック相当：即時反映。
                view.StructureNavigationKeyHeldForTest = () => false;
                items[0].IsSelected = true;
                Flush();
                Assert.False(view.IsStructureChunkApplyDeferred);
                var afterClick = view.SelectedSessions.Length;

                // ナビキー押し中はキャレットだけ進めて、プレイリスト同期は保留。
                view.StructureNavigationKeyHeldForTest = () => true;
                view.MoveHeaderStructureSelection(1, extend: false);
                Flush();
                Assert.True(view.IsStructureChunkApplyDeferred);
                Assert.Equal(afterClick, view.SelectedSessions.Length);

                // キー離しで確定。
                view.StructureNavigationKeyHeldForTest = () => false;
                view.FlushDeferredStructureChunkApply();
                Flush();
                Assert.False(view.IsStructureChunkApplyDeferred);
                Assert.True(view.SelectedSessions.Length >= 1);
                window.Close();
            }
            finally
            {
                foreach (var path in new[] { path1, path2 })
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
        });
    }

    [Fact]
    public void HeaderStructureUnionChunk_SelectsPlaylistWithoutStealingFocus()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var path1 = Path.Combine(
                Path.GetTempPath(),
                "mga-header-union-focus-1-" + Guid.NewGuid().ToString("N") + ".wav");
            var path2 = Path.Combine(
                Path.GetTempPath(),
                "mga-header-union-focus-2-" + Guid.NewGuid().ToString("N") + ".wav");
            WriteMinimalWav(path1);
            File.Copy(path1, path2, overwrite: true);
            try
            {
                var first = new DocumentSession(AudioDocument.CreateDeferred(path1));
                var second = new DocumentSession(AudioDocument.CreateDeferred(path2));
                var view = new LibraryBrowserView();
                var window = new Window
                {
                    Content = view,
                    Width = 900,
                    Height = 480,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                };
                window.Show();
                Flush();
                view.SetSessions([first, second], first, [first, second]);
                view.ToggleHeaderStructureVisible(focusWhenShown: true);
                Flush();
                PumpUntil(
                    () => view.HeaderStructureLoadedPath is { } loaded
                        && loaded.Contains("\u0001union\u0001", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(3));
                Assert.True(view.IsHeaderStructureFocused);

                var items = EditorHeaderChunkStripView.FlattenVisibleTreeItems(
                    view.HeaderStructureTree.Items);
                Assert.NotEmpty(items);
                var chunk = items[0];
                chunk.IsSelected = true;
                chunk.Focus();
                Flush();
                // ContextIdle のキャレット再 assert まで回す。
                Dispatcher.CurrentDispatcher.Invoke(
                    () => { },
                    DispatcherPriority.ContextIdle);
                Flush();

                Assert.True(view.IsHeaderStructureFocused);
                Assert.False(view.IsPlaylistGridFocused);
                Assert.True(view.SelectedSessions.Length >= 1);
                window.Close();
            }
            finally
            {
                foreach (var path in new[] { path1, path2 })
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
        });
    }

    [Fact]
    public void HeaderStructureUnionMultiSelect_SelectsUnionOfMemberPaths()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var dir = Path.Combine(Path.GetTempPath(), "mga-union-multi-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var plain = Path.Combine(dir, "plain.wav");
            var withBext = Path.Combine(dir, "bext.wav");
            var plain2 = Path.Combine(dir, "plain2.wav");
            try
            {
                WriteMinimalWav(plain);
                WriteWavWithExtraChunk(withBext, "bext", new byte[10]);
                WriteMinimalWav(plain2);

                var sessions = new[]
                {
                    new DocumentSession(AudioDocument.CreateDeferred(plain)),
                    new DocumentSession(AudioDocument.CreateDeferred(withBext)),
                    new DocumentSession(AudioDocument.CreateDeferred(plain2)),
                };
                var view = new LibraryBrowserView();
                var window = new Window
                {
                    Content = view,
                    Width = 900,
                    Height = 480,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                };
                window.Show();
                Flush();
                view.SetSessions(sessions, sessions[0], sessions);
                view.ToggleHeaderStructureVisible(focusWhenShown: true);
                Flush();
                PumpUntil(
                    () => view.HeaderStructureLoadedPath is { } loaded
                        && loaded.Contains("\u0001union\u0001", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(3));

                var items = EditorHeaderChunkStripView.FlattenVisibleTreeItems(
                    view.HeaderStructureTree.Items);
                TreeViewItem? FindChunk(string id) =>
                    items.FirstOrDefault(item => item.Tag is WaveStructureNode n && n.Id == id);

                var bext = FindChunk("bext");
                var fmt = FindChunk("fmt ");
                Assert.NotNull(bext);
                Assert.NotNull(fmt);

                // bext だけ → 1 件。
                view.BeginHeaderStructurePlainPress(bext, control: false);
                bext.IsSelected = true;
                Flush();
                view.FlushDeferredStructureChunkApply();
                Flush();
                Assert.Equal(1, view.PlaylistSelectedCount);
                Assert.Contains(sessions[1], view.SelectedSessions);

                // Shift で fmt まで → MemberPaths の和で 3 件全部。
                view.ApplyHeaderStructureShiftClick(fmt);
                Flush();
                view.FlushDeferredStructureChunkApply();
                Flush();
                Assert.Equal(3, view.HeaderStructureMultiSelectedCount);
                Assert.Equal(3, view.PlaylistSelectedCount);
                Assert.Equal(3, view.SelectedSessions.Length);

                // 一度 bext だけに戻してから Ctrl+A → 和集合全チャンク → 全ファイル。
                view.BeginHeaderStructurePlainPress(bext, control: false);
                bext.IsSelected = true;
                Flush();
                view.FlushDeferredStructureChunkApply();
                Flush();
                Assert.Equal(1, view.PlaylistSelectedCount);
                view.SelectAllHeaderStructureItems();
                Flush();
                view.FlushDeferredStructureChunkApply();
                Flush();
                Assert.True(view.HeaderStructureMultiSelectedCount >= 2);
                Assert.Equal(3, view.PlaylistSelectedCount);
                Assert.Equal(3, view.SelectedSessions.Length);
                window.Close();
            }
            finally
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        });
    }

    [Fact]
    public void HeaderStructureTree_SupportsSelectAllShiftRangeAndCtrlToggle()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var path = Path.Combine(
                Path.GetTempPath(),
                "mga-header-multi-" + Guid.NewGuid().ToString("N") + ".wav");
            var pcm = new byte[4];
            var fmt = new byte[]
            {
                1, 0, 1, 0, 0x44, 0xAC, 0, 0, 0x88, 0x58, 0x01, 0, 2, 0, 16, 0,
            };
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + pcm.Length);
                w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                w.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
                w.Write(fmt.Length);
                w.Write(fmt);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                w.Write(pcm.Length);
                w.Write(pcm);
                File.WriteAllBytes(path, ms.ToArray());
            }

            var session = new DocumentSession(AudioDocument.CreateDeferred(path));
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            try
            {
                window.Show();
                Flush();
                view.SetSessions([session], session, [session]);
                view.ToggleHeaderStructureVisible(focusWhenShown: true);
                Flush();
                PumpUntil(
                    () => view.HeaderStructureTree.Items.Count > 0,
                    TimeSpan.FromSeconds(3));

                var items = EditorHeaderChunkStripView
                    .FlattenVisibleTreeItems(view.HeaderStructureTree.Items);
                Assert.True(items.Count >= 2);

                view.BeginHeaderStructurePlainPress(items[0], control: false);
                items[0].IsSelected = true;
                Flush();
                Assert.Equal(1, view.HeaderStructureMultiSelectedCount);

                view.SelectAllHeaderStructureItems();
                Flush();
                Assert.Equal(items.Count, view.HeaderStructureMultiSelectedCount);
                var allCopy = view.BuildHeaderStructureCopyText();
                Assert.Contains('\t', allCopy);
                Assert.True(allCopy.Split(
                    ['\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries).Length >= 2);

                view.BeginHeaderStructurePlainPress(items[0], control: false);
                items[0].IsSelected = true;
                Flush();
                Assert.Equal(1, view.HeaderStructureMultiSelectedCount);

                view.BeginHeaderStructurePlainPress(items[1], control: true);
                Flush();
                Assert.Equal(2, view.HeaderStructureMultiSelectedCount);

                view.MoveHeaderStructureSelectionToEdge(1, extend: true);
                Flush();
                Assert.Equal(items.Count, view.HeaderStructureMultiSelectedCount);

                view.BeginHeaderStructurePlainPress(items[0], control: false);
                items[0].IsSelected = true;
                Flush();
                view.ApplyHeaderStructureShiftClick(items[^1]);
                Flush();
                Assert.Equal(items.Count, view.HeaderStructureMultiSelectedCount);

                view.MoveHeaderStructureSelection(1, extend: false);
                Flush();
                Assert.Equal(1, view.HeaderStructureMultiSelectedCount);
            }
            finally
            {
                window.Close();
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    // ignore
                }
            }
        });
    }

    [Fact]
    public void HeaderStructurePlainPress_WhilePreviousStillSelected_KeepsSoloCorners()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var path = Path.Combine(
                Path.GetTempPath(),
                "mga-header-corners-" + Guid.NewGuid().ToString("N") + ".wav");
            var pcm = new byte[4];
            var fmt = new byte[]
            {
                1, 0, 1, 0, 0x44, 0xAC, 0, 0, 0x88, 0x58, 0x01, 0, 2, 0, 16, 0,
            };
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + pcm.Length);
                w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                w.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
                w.Write(fmt.Length);
                w.Write(fmt);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                w.Write(pcm.Length);
                w.Write(pcm);
                File.WriteAllBytes(path, ms.ToArray());
            }

            var session = new DocumentSession(AudioDocument.CreateDeferred(path));
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            try
            {
                window.Show();
                Flush();
                view.SetSessions([session], session, [session]);
                view.ToggleHeaderStructureVisible(focusWhenShown: true);
                Flush();
                PumpUntil(
                    () => view.HeaderStructureTree.Items.Count > 0,
                    TimeSpan.FromSeconds(3));

                var items = EditorHeaderChunkStripView
                    .FlattenVisibleTreeItems(view.HeaderStructureTree.Items);
                Assert.True(items.Count >= 2);

                view.BeginHeaderStructurePlainPress(items[0], control: false);
                items[0].IsSelected = true;
                Flush();

                // 実クリック同様、前行がまだ IsSelected のまま次行を Preview する。
                view.BeginHeaderStructurePlainPress(items[1], control: false);
                Flush();
                Assert.Equal(
                    SelectionBandEdge.Solo,
                    (SelectionBandEdge)items[1].GetValue(LibraryBrowserView.SelectionBandEdgeProperty));

                items[0].IsSelected = false;
                items[1].IsSelected = true;
                Flush();
                Assert.Equal(
                    SelectionBandEdge.Solo,
                    (SelectionBandEdge)items[1].GetValue(LibraryBrowserView.SelectionBandEdgeProperty));
            }
            finally
            {
                window.Close();
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    // ignore
                }
            }
        });
    }

    [Fact]
    public void EditorHeaderChunkStrip_SupportsSelectAllShiftRangeAndCtrlToggle()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var path = Path.Combine(
                Path.GetTempPath(),
                "mga-editor-header-multi-" + Guid.NewGuid().ToString("N") + ".wav");
            var pcm = new byte[4];
            var fmt = new byte[]
            {
                1, 0, 1, 0, 0x44, 0xAC, 0, 0, 0x88, 0x58, 0x01, 0, 2, 0, 16, 0,
            };
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + pcm.Length);
                w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                w.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
                w.Write(fmt.Length);
                w.Write(fmt);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                w.Write(pcm.Length);
                w.Write(pcm);
                File.WriteAllBytes(path, ms.ToArray());
            }

            var strip = new EditorHeaderChunkStripView();
            var window = new Window
            {
                Content = strip,
                Width = 480,
                Height = 200,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            try
            {
                window.Show();
                Flush();
                strip.LoadFromPath(path);
                Flush();
                var visible = EditorHeaderChunkStripView.FlattenVisibleTreeItems(strip.Tree.Items);
                Assert.True(visible.Count >= 2);

                strip.SelectAllItems();
                Flush();
                Assert.Equal(visible.Count, strip.MultiSelectedCount);

                strip.BeginPlainPress(visible[0], control: false);
                visible[0].IsSelected = true;
                Flush();
                Assert.Equal(1, strip.MultiSelectedCount);

                strip.BeginPlainPress(visible[1], control: true);
                Flush();
                Assert.Equal(2, strip.MultiSelectedCount);

                strip.MoveSelectionToEdge(1, extend: true);
                Flush();
                Assert.Equal(visible.Count, strip.MultiSelectedCount);

                // アンカー（先頭）へ Shift+クリックすると範囲は1件に戻る。
                strip.ApplyShiftClick(visible[0]);
                Flush();
                Assert.Equal(1, strip.MultiSelectedCount);

                strip.ApplyShiftClick(visible[^1]);
                Flush();
                Assert.Equal(visible.Count, strip.MultiSelectedCount);

                strip.MoveSelection(1, extend: false);
                Flush();
                Assert.Equal(1, strip.MultiSelectedCount);

                var copy = strip.BuildCopyText();
                Assert.Contains('\t', copy);

                // メニューキー相当。ツリーの ContextMenu を直接開き、波形へ Focus しない。
                visible[0].Focus();
                Flush();
                Assert.True(strip.TryOpenKeyboardContextMenu());
                Assert.True(strip.Tree.ContextMenu is { IsOpen: true });
                strip.Tree.ContextMenu!.IsOpen = false;
            }
            finally
            {
                window.Close();
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    // ignore
                }
            }
        });
    }

    [Fact]
    public void HeaderStructure_PrefersWaveContainerPathOverSource()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var dir = Path.Combine(Path.GetTempPath(), "mga-hdr-work-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var source = Path.Combine(dir, "source.wav");
            var working = Path.Combine(dir, "working.wav");
            try
            {
                WriteMinimalWav(source);
                WriteWavWithExtraChunk(working, "bext", new byte[10]);

                var document = AudioDocument.CreateDeferred(source);
                document.SetWaveContainerPath(working, markDirty: true);
                var session = new DocumentSession(document);
                var view = new LibraryBrowserView();
                var window = new Window
                {
                    Content = view,
                    Width = 900,
                    Height = 480,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                };
                window.Show();
                Flush();
                view.SetSessions([session], session, [session]);
                view.ToggleHeaderStructureVisible(focusWhenShown: true);
                Flush();
                PumpUntil(
                    () => view.HeaderStructureLoadedPath is { } loaded
                        && string.Equals(loaded, working, StringComparison.OrdinalIgnoreCase),
                    TimeSpan.FromSeconds(3));

                Assert.Equal(working, view.HeaderStructureLoadedPath, StringComparer.OrdinalIgnoreCase);
                var items = EditorHeaderChunkStripView.FlattenVisibleTreeItems(
                    view.HeaderStructureTree.Items);
                Assert.Contains(
                    items,
                    item => item.Tag is WaveStructureNode n && n.Id == "bext");
                window.Close();
            }
            finally
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        });
    }

    [Fact]
    public void EditorHeaderChunkStrip_UnionSelectsMemberPathsAndBuildsDeletePlan()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var dir = Path.Combine(Path.GetTempPath(), "mga-editor-union-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var plain = Path.Combine(dir, "plain.wav");
            var withBext = Path.Combine(dir, "bext.wav");
            var plain2 = Path.Combine(dir, "plain2.wav");
            try
            {
                WriteMinimalWav(plain);
                WriteWavWithExtraChunk(withBext, "bext", new byte[10]);
                WriteMinimalWav(plain2);

                var strip = new EditorHeaderChunkStripView();
                IReadOnlyList<WaveStructureNode>? applied = null;
                strip.ApplyUnionChunkTabSelection = nodes => applied = nodes;
                var window = new Window
                {
                    Content = strip,
                    Width = 480,
                    Height = 240,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                };
                window.Show();
                Flush();

                strip.LoadUnionFromPaths([plain, withBext, plain2]);
                Flush();
                PumpUntil(
                    () => strip.LoadedPath is { } loaded
                        && loaded.Contains("\u0001union\u0001", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(3));
                Assert.True(strip.IsChunkUnion);

                var items = EditorHeaderChunkStripView.FlattenVisibleTreeItems(strip.Tree.Items);
                TreeViewItem? FindChunk(string id) =>
                    items.FirstOrDefault(item => item.Tag is WaveStructureNode n && n.Id == id);

                var bext = FindChunk("bext");
                var fmt = FindChunk("fmt ");
                Assert.NotNull(bext);
                Assert.NotNull(fmt);
                Assert.Single(((WaveStructureNode)bext.Tag).MemberPaths);
                Assert.Equal(3, ((WaveStructureNode)fmt.Tag).MemberPaths.Count);

                // bext だけ → MemberPaths 1 件をコールバックへ。
                strip.BeginPlainPress(bext, control: false);
                bext.IsSelected = true;
                Flush();
                strip.FlushDeferredChunkApply();
                Flush();
                Assert.NotNull(applied);
                Assert.Single(applied);
                Assert.Equal("bext", applied[0].Id);
                Assert.Contains(
                    withBext,
                    applied[0].MemberPaths,
                    StringComparer.OrdinalIgnoreCase);

                // Shift で fmt まで → 範囲内のマーク和（fmt を含むので全ファイル）。
                strip.ApplyShiftClick(fmt);
                Flush();
                strip.FlushDeferredChunkApply();
                Flush();
                Assert.NotNull(applied);
                Assert.True(applied.Count >= 2);
                Assert.Equal(applied.Count, strip.MultiSelectedCount);
                Assert.Contains(applied, n => n.Id == "fmt ");
                var unionPaths = applied
                    .SelectMany(n => n.MemberPaths)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                Assert.Equal(3, unionPaths.Length);

                // 削除プランは MemberPaths 経由で bext 所持ファイルのみ。
                var plan = WaveChunkDeleteUi.BuildPlan(
                    [bext],
                    singleFilePath: null);
                Assert.NotNull(plan);
                Assert.Contains(
                    plan.Value.PathToChunkIds.Keys,
                    k => string.Equals(k, withBext, StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(
                    plan.Value.PathToChunkIds.Keys,
                    k => string.Equals(k, plain, StringComparison.OrdinalIgnoreCase));

                window.Close();
            }
            finally
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        });
    }

    [Fact]
    public void LabelLibraryHeaderStructure_IsEnglishWithoutAccessKey()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.Japanese);
            Assert.Equal("Header Info", UiStrings.LabelLibraryHeaderStructure);
            Assert.Null(MenuAccessKeys.Read(UiStrings.LabelLibraryHeaderStructure));
            UiStrings.SetLanguage(UiLanguage.English);
            Assert.Equal("Header Info", UiStrings.LabelLibraryHeaderStructure);
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }

    [Fact]
    public void LabelLibraryAutoLoop_IsEnglishWithoutAccessKey()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.Japanese);
            Assert.Equal("Single Loop", UiStrings.LabelLibraryAutoLoop);
            Assert.Null(MenuAccessKeys.Read(UiStrings.LabelLibraryAutoLoop));
            Assert.DoesNotContain("Alt+", UiStrings.LabelLibraryAutoLoop, StringComparison.OrdinalIgnoreCase);
            UiStrings.SetLanguage(UiLanguage.English);
            Assert.Equal("Single Loop", UiStrings.LabelLibraryAutoLoop);
            Assert.Null(MenuAccessKeys.Read(UiStrings.LabelLibraryAutoLoop));
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }

    [Fact]
    public void ColumnHeader_MarksSortedColumn()
    {
        RunSta(() =>
        {
            EnsureTheme();
            if (!Application.Current!.Resources.Contains(typeof(TextBlock)))
            {
                Application.Current.Resources.Add(
                    typeof(TextBlock),
                    new Style(typeof(TextBlock))
                    {
                        Setters =
                        {
                            new Setter(TextBlock.FontFamilyProperty, new FontFamily("Yu Gothic UI")),
                            new Setter(TextBlock.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")),
                        },
                    });
            }

            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 1100,
                Height = 360,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            Flush();
            view.UpdateLayout();
            Flush();
            Assert.Equal(DataGridHeadersVisibility.None, view.HeadersVisibility);

            var session = Session("01 a.mp3");
            session.Document.ApplyTags(new AudioFileTags { Probed = true, Album = "Test Album" });
            view.SetSessions([session], null, []);
            Flush();
            view.UpdateLayout();
            Flush();
            var album = HeaderLabeled(view, UiStrings.LibraryColumnAlbum);
            AssertSortMarks(album, ascending: true);
            Assert.True(CountCyanInHeader(window, album) > 8, "marks vanished after load");

            view.SetArtwork(null);
            view.RefreshAppearance();
            Flush();
            view.UpdateLayout();
            Flush();
            album = HeaderLabeled(view, UiStrings.LibraryColumnAlbum);
            AssertSortMarks(album, ascending: true);
            Assert.True(CountCyanInHeader(window, album) > 8, "marks vanished after artwork refresh");

            var name = HeaderLabeled(view, UiStrings.LibraryColumnName);
            Assert.Equal(Visibility.Collapsed, NamedPath(name, "ArrowUp").Visibility);
            Assert.Equal(Visibility.Collapsed, NamedPath(name, "ArrowDown").Visibility);
            window.Close();
        });
    }

    [Fact]
    public void FocusList_SelectsFirstWhenNothingSelected()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var first = Session("01 a.mp3");
            var second = Session("02 b.mp3");
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetSessions([first, second], null, []);
            Flush();
            view.UpdateLayout();
            Flush();

            view.FocusExplorer();
            Flush();
            Assert.True(view.IsExplorerFocused);

            view.FocusList();
            Flush();
            Assert.True(view.IsListKeyboardFocused);
            window.Close();
        });
    }

    [Fact]
    public void FocusExplorer_SelectsTree()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            Flush();
            view.UpdateLayout();
            Flush();

            view.FocusExplorer();
            Flush();
            Assert.True(view.IsExplorerFocused);
            window.Close();
        });
    }

    [Fact]
    public void PlaylistWaveformCompletion_KeepsSelectionOnPlayingRow()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var sessions = new DocumentSession[40];
            for (var i = 0; i < sessions.Length; i++)
            {
                sessions[i] = Session($"{i:00} track.wav");
            }

            var first = sessions[0];
            var target = sessions[25];
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 1000,
                Height = 360,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            // 実機どおり：先頭選択でフォーカスを掴んでから↓で別行へ移し、その後に波形を埋める。
            view.SetSessions(sessions, first, [first]);
            Flush();
            view.UpdateLayout();
            Flush();

            view.FocusList();
            Flush();
            view.UpdateLayout();
            Flush();

            view.MoveSelection(25);
            Flush();
            view.UpdateLayout();
            Flush();

            var activations = 0;
            view.SessionActivated += (_, _) => activations++;
            Assert.Same(target, view.SelectedSession);

            // 列幅の再計算（FitColumns）＋各行波形の完了を交互に流す。
            var barCount = LibraryPlaylistWaveform.BarCount;
            foreach (var session in sessions)
            {
                var bars = new float[barCount];
                for (var i = 0; i < barCount; i++)
                {
                    bars[i] = 0.5f;
                }

                LibraryPlaylistWaveform.Set(session.Document.SourcePath!, barCount, bars);
                view.RecalculateColumnWidths();
                Flush();
                view.UpdateLayout();
                Flush();
            }

            Assert.Same(target, view.SelectedSession);
            var selectedSessions = view.SelectedSessions;
            Assert.Single(selectedSessions);
            Assert.Same(target, selectedSessions[0]);
            Assert.Equal(0, activations);

            // DataGrid の現在セルも選択行へ固定され続けていること。ここが先頭行へ戻ると
            // レイアウト／フォーカスのパスでシアンが1行目へ飛ぶ。
            var grid = Assert.IsAssignableFrom<DataGrid>(view.FileGrid);
            var currentSession = (grid.CurrentItem as LibraryFileRow)?.Tag as DocumentSession;
            Assert.Same(target, currentSession);
            window.Close();
        });
    }

    [Fact]
    public void MoveSelection_PinsDataGridCurrentItemToSelectedRow()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var first = Session("aaa.wav");
            var second = Session("bbb.wav");
            var third = Session("ccc.wav");
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 400,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetSessions([first, second, third], first, [first]);
            Flush();
            view.UpdateLayout();
            Flush();
            view.FocusList();
            Flush();

            view.MoveSelection(2);
            Flush();

            Assert.Same(third, view.SelectedSession);
            var grid = Assert.IsAssignableFrom<DataGrid>(view.FileGrid);
            var currentSession = (grid.CurrentItem as LibraryFileRow)?.Tag as DocumentSession;
            Assert.Same(third, currentSession);
            window.Close();
        });
    }

    [Fact]
    public void UpdateSessionRows_Force_KeepsMultiSelectionAndAllowsMove()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var first = Session("aaa.wav");
            var second = Session("bbb.wav");
            var third = Session("ccc.wav");
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 400,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetSessions([first, second, third], second, [first, second, third]);
            Flush();
            view.UpdateLayout();
            Flush();
            view.FocusList();
            Flush();
            Assert.Equal(3, view.PlaylistSelectedCount);

            // チャンク一括削除後の行差し替え相当。SameContent でも force で作り直す。
            view.UpdateSessionRows([first, second, third], force: true);
            Flush();

            Assert.Equal(3, view.SelectedSessions.Length);
            Assert.Contains(first, view.SelectedSessions);
            Assert.Contains(second, view.SelectedSessions);
            Assert.Contains(third, view.SelectedSessions);
            Assert.Same(second, view.SelectedSession);

            view.MoveSelection(1, wrap: false);
            Flush();
            Assert.Same(third, view.SelectedSession);
            Assert.Equal(1, view.PlaylistSelectedCount);
            window.Close();
        });
    }

    [Fact]
    public void RematerializeBoundPlaylistRows_AfterFormatConvert_AllowsSelectAllAndMove()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var first = Session("rate-a.wav");
            var second = Session("rate-b.wav");
            var third = Session("rate-c.wav");
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 400,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetSessions([first, second, third], second, [first, second, third]);
            Flush();
            view.FocusList();
            Flush();

            // サンプルレート／Ch／ビット変換後の一括載せ替え相当。
            view.RematerializeBoundPlaylistRows();
            Flush();

            Assert.Equal(3, view.SelectedSessions.Length);
            view.MoveSelection(1, wrap: false);
            Flush();
            Assert.Equal(1, view.PlaylistSelectedCount);
            view.SelectAllRows();
            Flush();
            Assert.Equal(3, view.PlaylistSelectedCount);
            Assert.Equal(3, view.BoundRowCount);
            view.MoveSelection(1, wrap: false);
            Flush();
            Assert.NotNull(view.SelectedSession);
            Assert.True(view.PlaylistSelectedCount >= 1);
            window.Close();
        });
    }

    [Fact]
    public void RefreshSessionsAfterHeaderRepairInPlace_SelectAllSelectsEveryRow()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var dir = Path.Combine(Path.GetTempPath(), "mga-chunk-reload-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var paths = new[]
                {
                    Path.Combine(dir, "a.wav"),
                    Path.Combine(dir, "b.wav"),
                    Path.Combine(dir, "c.wav"),
                };
                foreach (var path in paths)
                {
                    WriteMinimalWav(path);
                }

                var sessions = paths
                    .Select(path => new DocumentSession(AudioDocument.CreateDeferred(path)))
                    .ToArray();
                var view = new LibraryBrowserView();
                var window = new Window
                {
                    Content = view,
                    Width = 900,
                    Height = 400,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                };
                window.Show();
                view.SetSessions(sessions, sessions[1], sessions);
                Flush();
                view.FocusList();
                Flush();
                Assert.Equal(3, view.PlaylistSelectedCount);

                // チャンク削除相当: mtime だけ進め、行を全件作り直す。
                // （SetSessions 後に FileShare.Read が残ることがあり WriteAllBytes は使わない）
                foreach (var path in paths)
                {
                    File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
                }

                view.RefreshSessionsAfterHeaderRepairInPlace(paths);
                Flush();

                Assert.Equal(3, view.SelectedSessions.Length);
                Assert.Equal(3, view.BoundRowCount);

                // 帯を1件に落としてから Ctrl+A 相当（削除直後に全選択が途中で止まる回帰）。
                view.MoveSelection(1, wrap: false);
                Flush();
                Assert.Equal(1, view.PlaylistSelectedCount);
                view.SelectAllRows();
                Flush();
                Assert.Equal(3, view.PlaylistSelectedCount);
                Assert.Equal(3, view.SelectedSessions.Length);
                Assert.Equal(3, view.BoundRowCount);

                view.MoveSelection(1, wrap: false);
                Flush();
                Assert.NotNull(view.SelectedSession);
                Assert.True(view.PlaylistSelectedCount >= 1);
                window.Close();
            }
            finally
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        });
    }

    [Fact]
    public void MoveSelection_RapidDown_KeepsSelectionFromJumpingUp()
    {
        RunSta(() =>
        {
            EnsureTheme();
            // 画面に収まりきらない件数にして ScrollIntoView／仮想化を必ず踏ませる。
            var sessions = new DocumentSession[40];
            for (var i = 0; i < sessions.Length; i++)
            {
                sessions[i] = Session($"track-{i:D2}.wav");
            }

            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 280,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetSessions(sessions, sessions[0], [sessions[0]]);
            Flush();
            view.UpdateLayout();
            Flush();
            view.FocusList();
            Flush();

            // ↓連打相当。各移動のあとにレイアウトを挟み、古い EnsureRowVisible の
            // Loaded コールバックと SelectedItem の寄りを誘発する。
            for (var i = 0; i < 12; i++)
            {
                view.MoveSelection(1, wrap: false);
                view.UpdateLayout();
                Flush();
            }

            Assert.Same(sessions[12], view.SelectedSession);
            var grid = Assert.IsAssignableFrom<DataGrid>(view.FileGrid);
            Assert.Same(sessions[12], (grid.SelectedItem as LibraryFileRow)?.Tag as DocumentSession);
            Assert.Same(sessions[12], (grid.CurrentItem as LibraryFileRow)?.Tag as DocumentSession);
            window.Close();
        });
    }

    [Fact]
    public void MoveSelection_WhileNavKeyHeld_KeepsCyanWhenGridSelectionDrifts()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var sessions = new DocumentSession[40];
            for (var i = 0; i < sessions.Length; i++)
            {
                sessions[i] = Session($"hold-{i:D2}.wav");
            }

            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 280,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetSessions(sessions, sessions[0], [sessions[0]]);
            Flush();
            view.UpdateLayout();
            Flush();
            view.FocusList();
            Flush();

            // ↑↓ 押し続け中は ScrollIntoView 後も _deferActivate を残す。
            view.PlaylistNavigationKeyHeldForTest = () => true;
            for (var i = 0; i < 10; i++)
            {
                view.MoveSelection(1, wrap: false);
                view.UpdateLayout();
                Flush();
            }

            Assert.True(view.IsActivateDeferred);
            Assert.Same(sessions[10], view.SelectedSession);

            var grid = Assert.IsAssignableFrom<DataGrid>(view.FileGrid);
            var targetRow = Assert.IsType<LibraryFileRow>(grid.SelectedItem);
            var firstRow = Assert.IsType<LibraryFileRow>(grid.Items[0]);
            Assert.True(targetRow.IsPlaylistSelected);

            // 仮想化が SelectedItem を先頭へ寄せても、シアン帯フラグは奪われない。
            grid.SelectedItem = firstRow;
            Flush();
            Assert.True(targetRow.IsPlaylistSelected);
            Assert.False(firstRow.IsPlaylistSelected);
            Assert.Same(sessions[10], view.SelectedSession);

            view.PlaylistNavigationKeyHeldForTest = () => false;
            view.FlushPlaylistMoveDefer();
            Flush();
            Assert.False(view.IsActivateDeferred);
            Assert.Same(sessions[10], (grid.SelectedItem as LibraryFileRow)?.Tag as DocumentSession);
            Assert.True(targetRow.IsPlaylistSelected);
            window.Close();
        });
    }

    [Fact]
    public void ShouldRestoreFolderPlaySelection_KeepsUserMovedRow()
    {
        var first = Session("first.wav");
        var moved = Session("moved.wav");
        Assert.True(MainWindow.ShouldRestoreFolderPlaySelection(null, first));
        Assert.True(MainWindow.ShouldRestoreFolderPlaySelection(first, first));
        Assert.False(MainWindow.ShouldRestoreFolderPlaySelection(moved, first));
    }

    [Fact]
    public void ShouldSelectAppendedLibrarySession_ShiftEnterSelectsOnlyEmptyPlaylistHead()
    {
        Assert.True(MainWindow.ShouldSelectAppendedLibrarySession(playFirst: true, firstInBatch: true, playlistWasEmpty: true));
        Assert.True(MainWindow.ShouldSelectAppendedLibrarySession(playFirst: true, firstInBatch: true, playlistWasEmpty: false));
        Assert.False(MainWindow.ShouldSelectAppendedLibrarySession(playFirst: true, firstInBatch: false, playlistWasEmpty: true));
        Assert.True(MainWindow.ShouldSelectAppendedLibrarySession(playFirst: false, firstInBatch: true, playlistWasEmpty: true));
        Assert.False(MainWindow.ShouldSelectAppendedLibrarySession(playFirst: false, firstInBatch: true, playlistWasEmpty: false));
        Assert.False(MainWindow.ShouldSelectAppendedLibrarySession(playFirst: false, firstInBatch: false, playlistWasEmpty: true));
    }

    [Fact]
    public void ShouldPlayFolderFirstImmediately_AudioYes_VisualNo()
    {
        Assert.True(MainWindow.ShouldPlayFolderFirstImmediately(@"C:\music\a.wav"));
        Assert.True(MainWindow.ShouldPlayFolderFirstImmediately(@"C:\music\a.mp3"));
        Assert.True(MainWindow.ShouldPlayFolderFirstImmediately(@"C:\music\a.m4a"));
        Assert.False(MainWindow.ShouldPlayFolderFirstImmediately(@"C:\music\a.mp4"));
        Assert.False(MainWindow.ShouldPlayFolderFirstImmediately(@"C:\music\a.mov"));
        Assert.False(MainWindow.ShouldPlayFolderFirstImmediately(@"C:\music\a.pdf"));
        Assert.False(MainWindow.ShouldPlayFolderFirstImmediately(null));
        Assert.False(MainWindow.ShouldPlayFolderFirstImmediately(""));
        Assert.False(MainWindow.ShouldPlayFolderFirstImmediately("   "));
    }

    [Fact]
    public void AppendSessionsRaw_LockPlaylistOrder_ThenUpdateKeepsOrder()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var zebra = Session("zzz.mp3");
            zebra.Document.ApplyTags(new AudioFileTags
            {
                Probed = true,
                Album = "Zebra",
                Title = "Z",
            });
            var alpha = Session("aaa.mp3");
            alpha.Document.ApplyTags(new AudioFileTags
            {
                Probed = true,
                Album = "Alpha",
                Title = "A",
            });

            var view = new LibraryBrowserView();
            // 追加順は Zebra → Alpha。Album ソート確定後の冒頭は Alpha。
            view.AppendSessionsRaw([zebra, alpha], select: null);
            Flush();
            Assert.Same(zebra, view.FirstPlaylistSession);

            var head = view.LockPlaylistOrder();
            Flush();
            Assert.Same(alpha, head);
            Assert.Same(alpha, view.FirstPlaylistSession);
            Assert.Equal([alpha, zebra], view.PlaylistSessionsInDisplayOrder());

            // 後追いで Album が入れ替わっても、並びはロックしたまま。
            alpha.Document.ApplyTags(new AudioFileTags
            {
                Probed = true,
                Album = "Omega",
                Title = "A",
            });
            view.UpdateSessionRow(alpha);
            Flush();
            Assert.Equal([alpha, zebra], view.PlaylistSessionsInDisplayOrder());
            Assert.Same(alpha, view.FirstPlaylistSession);
        });
    }

    [Fact]
    public void SetSessions_PinsCollectionViewCurrentToSelection_NotFirstRow()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var first = Session("aaa.wav");
            var second = Session("bbb.wav");
            var third = Session("ccc.wav");
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 400,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetSessions([first, second, third], third, [third]);
            Flush();
            view.UpdateLayout();
            Flush();

            // CollectionView の CurrentItem が先頭行（＝WPF が IsSelected を寄せる足がかり）
            // ではなく、実際の選択行に固定されていること。
            var cv = Assert.IsAssignableFrom<System.ComponentModel.ICollectionView>(view.BoundItemsSource);
            var currentSession = (cv.CurrentItem as LibraryFileRow)?.Tag as DocumentSession;
            Assert.Same(third, currentSession);
            Assert.Same(third, view.SelectedSession);
            window.Close();
        });
    }

    [Fact]
    public void FitColumns_PacksToContent()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var first = Session("01 Meet The World.mp3");
            first.Document.ApplyTags(new AudioFileTags
            {
                Probed = true,
                Title = "Meet The World",
                Artist = "namco",
                Album = "King Street Sounds / Nite Grooves Tunes for RIDGE RACER 7",
            });
            var second = Session("02 Fire Thrower.mp3");
            second.Document.ApplyTags(new AudioFileTags
            {
                Probed = true,
                Title = "Fire Thrower",
                Artist = "namco",
                Album = "King Street Sounds / Nite Grooves Tunes for RIDGE RACER 7",
            });

            var view = new LibraryBrowserView();
            view.SetVisibleColumns(
            [
                LibraryFileColumn.Name,
                LibraryFileColumn.Title,
                LibraryFileColumn.Artist,
                LibraryFileColumn.Album,
            ]);
            var window = new Window
            {
                Content = view,
                Width = 1100,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetSessions([first, second], first, [first]);
            Flush();
            view.UpdateLayout();
            view.RecalculateColumnWidths();
            Flush();

            var artist = view.ColumnPixelWidth(LibraryFileColumn.Artist);
            var album = view.ColumnPixelWidth(LibraryFileColumn.Album);
            var name = view.ColumnPixelWidth(LibraryFileColumn.Name);
            Assert.True(artist > 24, $"artist width was {artist}");
            Assert.True(album > artist + 40, $"album {album} should be wider than artist {artist}");
            Assert.True(
                artist < 96,
                $"artist column should pack to short content, was {artist}");
            Assert.True(
                name < 180,
                $"file column should pack to short names, was {name}");
            Assert.True(
                LibraryBrowserView.LibraryColumnHeaderPadX * 2 + LibraryBrowserView.LibraryColumnSortPad < 48,
                "header chrome must stay tight so short columns do not pick up extra width");
            window.Close();
        });
    }

    [Fact]
    public void FitColumns_ReservesFileNameMarker()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var previous = AppStorage.Settings.FileNameColorRules;
            try
            {
                AppStorage.Settings.ApplyFileNameColorRules([]);
                var session = Session("bgm_bt_0000.wav");
                var view = new LibraryBrowserView();
                view.SetVisibleColumns([LibraryFileColumn.Name]);
                var window = new Window
                {
                    Content = view,
                    Width = 800,
                    Height = 320,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                };
                window.Show();
                view.SetSessions([session], session, [session]);
                Flush();
                view.UpdateLayout();
                view.RecalculateColumnWidths();
                var plain = view.ColumnPixelWidth(LibraryFileColumn.Name);

                AppStorage.Settings.ApplyFileNameColorRules(
                [
                    new FileNameColorRule { Pattern = "_bt_", ColorIndex = 10 },
                ]);
                view.RefreshFileNameMarkers();
                view.RecalculateColumnWidths();
                var marked = view.ColumnPixelWidth(LibraryFileColumn.Name);
                Assert.True(
                    marked >= plain + FileNameColorMarkerVisual.SnappedSlotWidth(1),
                    $"marked {marked} should clear the marker beyond plain {plain}");

                AppStorage.Settings.ApplyFileNameColorRules([]);
                view.RefreshFileNameMarkers();
                view.RecalculateColumnWidths();
                var cleared = view.ColumnPixelWidth(LibraryFileColumn.Name);
                Assert.True(
                    cleared <= plain + 0.5,
                    $"clearing markers should shrink back, plain {plain} cleared {cleared}");
                window.Close();
            }
            finally
            {
                AppStorage.Settings.ApplyFileNameColorRules(previous);
            }
        });
    }

    [Fact]
    public void FitColumns_MixedMarkers_UsesPerRowNeed()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var previous = AppStorage.Settings.FileNameColorRules;
            try
            {
                AppStorage.Settings.ApplyFileNameColorRules(
                [
                    new FileNameColorRule { Pattern = "_bt_", ColorIndex = 10 },
                ]);
                // 長い方はマーカー無し、短い方だけマーカー。列は「長い方」か「短い方+slot」の大きい方。
                var longPlain = Session("soundtrack_long_title_theme.wav");
                var shortMarked = Session("bgm_bt_00.wav");
                var view = new LibraryBrowserView();
                view.SetVisibleColumns([LibraryFileColumn.Name]);
                var window = new Window
                {
                    Content = view,
                    Width = 900,
                    Height = 320,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                };
                window.Show();

                view.SetSessions([longPlain], longPlain, [longPlain]);
                Flush();
                view.UpdateLayout();
                view.RecalculateColumnWidths();
                var longOnly = view.ColumnPixelWidth(LibraryFileColumn.Name);

                view.SetSessions([shortMarked], shortMarked, [shortMarked]);
                Flush();
                view.UpdateLayout();
                view.RecalculateColumnWidths();
                var shortOnly = view.ColumnPixelWidth(LibraryFileColumn.Name);

                view.SetSessions([longPlain, shortMarked], longPlain, [longPlain]);
                Flush();
                view.UpdateLayout();
                view.RecalculateColumnWidths();
                var mixed = view.ColumnPixelWidth(LibraryFileColumn.Name);

                Assert.True(longOnly > shortOnly, $"long {longOnly} should exceed short+marker {shortOnly}");
                Assert.True(
                    mixed >= longOnly - 0.5 && mixed <= longOnly + 0.5,
                    $"mixed {mixed} should follow the longer unmarked row ({longOnly}), not add a spare slot");
                window.Close();
            }
            finally
            {
                AppStorage.Settings.ApplyFileNameColorRules(previous);
            }
        });
    }

    [Fact]
    public void MeasurePlaylistText_DisplayNotNarrowerThanIdeal_ForJapanese()
    {
        RunSta(() =>
        {
            var face = new Typeface(
                new FontFamily("Yu Gothic UI"),
                FontStyles.Normal,
                FontWeights.Normal,
                FontStretches.Normal);
            const double size = LibraryBrowserView.LibraryListFontSize;
            const double dpi = 1;
            foreach (var text in new[]
                     {
                         "効果音_爆発ループ_01.wav",
                         "ＢＧＭ＿タイトルテーマ.wav",
                         "背景音楽_戦闘.aiff",
                     })
            {
                var ideal = new FormattedText(
                    text,
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    face,
                    size,
                    Brushes.Black,
                    dpi);
                var idealWidth = ideal.WidthIncludingTrailingWhitespace;
                if (ideal.OverhangLeading < 0)
                {
                    idealWidth -= ideal.OverhangLeading;
                }

                if (ideal.OverhangAfter > 0)
                {
                    idealWidth += ideal.OverhangAfter;
                }

                var display = LibraryBrowserView.MeasurePlaylistText(text, face, size, dpi);
                Assert.True(
                    display + 0.01 >= idealWidth,
                    $"Display ({display}) must not undercut Ideal ({idealWidth}) for '{text}'");
            }
        });
    }

    [Fact]
    public void FitColumns_JapaneseFileName_ClearsTextBlockNaturalWidth()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var previous = AppStorage.Settings.FileNameColorRules;
            try
            {
                AppStorage.Settings.ApplyFileNameColorRules([]);
                var session = Session("効果音_爆発ループ_01.wav");
                var view = new LibraryBrowserView();
                view.SetVisibleColumns([LibraryFileColumn.Name]);
                var window = new Window
                {
                    Content = view,
                    Width = 900,
                    Height = 320,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                };
                window.Show();
                view.SetSessions([session], session, [session]);
                Flush();
                view.UpdateLayout();
                view.RecalculateColumnWidths();
                Flush();
                view.UpdateLayout();

                var nameText = FindDescendants<TextBlock>(view.FileGrid)
                    .FirstOrDefault(block => block.Text == "効果音_爆発ループ_01.wav");
                Assert.NotNull(nameText);

                nameText!.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var natural = nameText.DesiredSize.Width;
                var column = view.ColumnPixelWidth(LibraryFileColumn.Name);
                var available = column - (LibraryBrowserView.LibraryColumnCellPadX * 2);
                Assert.True(
                    available + 0.5 >= natural,
                    $"Name column available {available} (col {column}) must clear Japanese text natural {natural}");

                // NameColumnRowWidth の計算とも一致すること（計測が Ideal に戻っていないか）。
                var face = new Typeface(
                    new FontFamily("Yu Gothic UI"),
                    FontStyles.Normal,
                    FontWeights.Normal,
                    FontStretches.Normal);
                var measured = LibraryBrowserView.MeasurePlaylistText(
                    "効果音_爆発ループ_01.wav",
                    face,
                    LibraryBrowserView.LibraryListFontSize,
                    1);
                var expected = FileNameColorMarkerVisual.NameColumnRowWidth(
                    measured,
                    hasMarker: false,
                    pixelsPerDip: 1,
                    LibraryBrowserView.LibraryColumnCellPadX * 2);
                Assert.True(
                    column + 0.5 >= Math.Ceiling(expected),
                    $"fitted {column} should cover NameColumnRowWidth {expected}");
                window.Close();
            }
            finally
            {
                AppStorage.Settings.ApplyFileNameColorRules(previous);
            }
        });
    }

    [Fact]
    public void FitColumns_JapaneseFileNameWithMarker_ClearsSlotAndGlyphs()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var previous = AppStorage.Settings.FileNameColorRules;
            try
            {
                AppStorage.Settings.ApplyFileNameColorRules(
                [
                    new FileNameColorRule { Pattern = "効果音", ColorIndex = 10 },
                ]);
                var session = Session("効果音_爆発ループ_01.wav");
                var view = new LibraryBrowserView();
                view.SetVisibleColumns([LibraryFileColumn.Name]);
                var window = new Window
                {
                    Content = view,
                    Width = 900,
                    Height = 320,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                };
                window.Show();
                view.SetSessions([session], session, [session]);
                Flush();
                view.UpdateLayout();
                view.RecalculateColumnWidths();
                Flush();
                view.UpdateLayout();

                var nameText = FindDescendants<TextBlock>(view.FileGrid)
                    .FirstOrDefault(block => block.Text == "効果音_爆発ループ_01.wav");
                Assert.NotNull(nameText);
                nameText!.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var natural = nameText.DesiredSize.Width;
                var column = view.ColumnPixelWidth(LibraryFileColumn.Name);
                var available = column
                    - (LibraryBrowserView.LibraryColumnCellPadX * 2)
                    - FileNameColorMarkerVisual.SnappedSlotWidth(1);
                Assert.True(
                    available + 0.5 >= natural,
                    $"marked Name available {available} (col {column}) must clear Japanese natural {natural}");
                window.Close();
            }
            finally
            {
                AppStorage.Settings.ApplyFileNameColorRules(previous);
            }
        });
    }

    [Fact]
    public void FitColumns_ReservesLoudnessWhenUnmeasured()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var empty = Session("unmeasured.wav");
            empty.Document.ApplyTags(new AudioFileTags
            {
                Probed = true,
                DurationSeconds = 1,
                SampleRate = 48000,
                Channels = 2,
                BitsPerSample = 24,
            });
            var measured = Session("measured.wav");
            measured.Document.ApplyTags(new AudioFileTags
            {
                Probed = true,
                DurationSeconds = 1,
                SampleRate = 48000,
                Channels = 2,
                BitsPerSample = 24,
                IntegratedLufs = -23.5f,
            });

            var view = new LibraryBrowserView();
            view.SetVisibleColumns([LibraryFileColumn.Name, LibraryFileColumn.Loudness]);
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 320,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();

            view.SetSessions([empty], empty, [empty]);
            Flush();
            view.UpdateLayout();
            view.RecalculateColumnWidths();
            var reserved = view.ColumnPixelWidth(LibraryFileColumn.Loudness);

            view.SetSessions([measured], measured, [measured]);
            Flush();
            view.UpdateLayout();
            view.RecalculateColumnWidths();
            var filled = view.ColumnPixelWidth(LibraryFileColumn.Loudness);

            Assert.True(
                reserved >= filled - 0.5,
                $"unmeasured reserve {reserved} should already clear measured {filled}");
            Assert.True(reserved > 40, $"loudness column should not stay header-only, was {reserved}");

            // Name 列はマーカー無しでも ellipsis 回避の 2px を含む（詰め込み過ぎない範囲）。
            var nameWidth = view.ColumnPixelWidth(LibraryFileColumn.Name);
            Assert.True(nameWidth > 24, $"name column should clear short file names, was {nameWidth}");
            Assert.True(nameWidth < 160, $"name column should stay packed, was {nameWidth}");
            window.Close();
        });
    }

    [Fact]
    public void EmptyPlaylist_HidesEveryColumnIncludingName()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var view = new LibraryBrowserView();
            foreach (var column in LibraryColumnFilter.All)
            {
                Assert.Equal(Visibility.Collapsed, view.ColumnVisibility(column));
            }

            Assert.Equal(DataGridHeadersVisibility.None, view.HeadersVisibility);
            Assert.Equal(Visibility.Collapsed, view.GroupSpacerVisibility);

            var wave = Session("kick.wav");
            view.SetSessions([wave], wave, [wave]);
            Assert.Equal(Visibility.Visible, view.ColumnVisibility(LibraryFileColumn.Name));
            Assert.Equal(DataGridHeadersVisibility.Column, view.HeadersVisibility);

            view.SetSessions([], null, []);
            foreach (var column in LibraryColumnFilter.All)
            {
                Assert.Equal(Visibility.Collapsed, view.ColumnVisibility(column));
            }

            Assert.Equal(DataGridHeadersVisibility.None, view.HeadersVisibility);
        });
    }

    [Fact]
    public void EmptyPlaylist_FadesColumnHeadersBeforeHiding()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 400,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            var wave = Session("kick.wav");
            view.SetSessions([wave], wave, [wave]);
            Flush();
            view.UpdateLayout();
            Flush();
            Assert.Equal(Visibility.Visible, view.ColumnVisibility(LibraryFileColumn.Name));
            Assert.Equal(DataGridHeadersVisibility.Column, view.HeadersVisibility);

            view.SetSessions([], null, []);
            Assert.True(view.ColumnChromeFadingOut);
            Assert.Equal(Visibility.Visible, view.ColumnVisibility(LibraryFileColumn.Name));
            Assert.Equal(DataGridHeadersVisibility.Column, view.HeadersVisibility);

            PumpUntil(
                () => !view.ColumnChromeFadingOut,
                TimeSpan.FromSeconds(LibraryPlayerMode.MeterFadeSeconds + 0.5));
            Assert.False(view.ColumnChromeFadingOut);
            foreach (var column in LibraryColumnFilter.All)
            {
                Assert.Equal(Visibility.Collapsed, view.ColumnVisibility(column));
            }

            Assert.Equal(DataGridHeadersVisibility.None, view.HeadersVisibility);
            window.Close();
        });
    }

    [Fact]
    public void EmptyPlaylist_Mp3Only_FadesWithoutShowingWaveColumns()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var mp3 = Session("song.mp3");
            mp3.Document.ApplyTags(new AudioFileTags { Probed = true, Title = "Song" });
            var view = new LibraryBrowserView();
            view.SetVisibleColumnPresets(
                LibraryColumnFilter.WaveDefaults,
                LibraryColumnFilter.Mp3Defaults);
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 400,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetSessions([mp3], mp3, [mp3]);
            Flush();
            view.UpdateLayout();
            Flush();
            Assert.Equal(Visibility.Visible, view.ColumnVisibility(LibraryFileColumn.Name));
            Assert.Equal(Visibility.Visible, view.ColumnVisibility(LibraryFileColumn.Title));
            Assert.Equal(Visibility.Collapsed, view.ColumnVisibility(LibraryFileColumn.SampleRate));
            Assert.Equal(Visibility.Collapsed, view.ColumnVisibility(LibraryFileColumn.BitDepth));
            Assert.Equal(Visibility.Collapsed, view.ColumnVisibility(LibraryFileColumn.Waveform));

            view.SetSessions([], null, []);
            Flush();
            Assert.True(view.ColumnChromeFadingOut);
            Assert.Equal(Visibility.Visible, view.ColumnVisibility(LibraryFileColumn.Name));
            Assert.Equal(Visibility.Visible, view.ColumnVisibility(LibraryFileColumn.Title));
            Assert.Equal(Visibility.Collapsed, view.ColumnVisibility(LibraryFileColumn.SampleRate));
            Assert.Equal(Visibility.Collapsed, view.ColumnVisibility(LibraryFileColumn.BitDepth));
            Assert.Equal(Visibility.Collapsed, view.ColumnVisibility(LibraryFileColumn.Waveform));
            Assert.Equal(LibraryColumnFilter.Mp3Defaults, view.Mp3ColumnOrder);

            view.AppendSession(mp3, select: true);
            Flush();
            Assert.False(view.ColumnChromeFadingOut);
            Assert.Equal(Visibility.Visible, view.ColumnVisibility(LibraryFileColumn.Title));
            Assert.Equal(Visibility.Collapsed, view.ColumnVisibility(LibraryFileColumn.SampleRate));
            Assert.Equal(Visibility.Collapsed, view.ColumnVisibility(LibraryFileColumn.Waveform));
            window.Close();
        });
    }

    [Fact]
    public void Mp3AfterWavePlaylist_SwitchesToMp3Columns()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var wave = Session("kick.wav");
            var mp3 = Session("song.mp3");
            mp3.Document.ApplyTags(new AudioFileTags { Probed = true, Title = "Song" });
            var view = new LibraryBrowserView();
            view.SetVisibleColumnPresets(
                LibraryColumnFilter.WaveDefaults,
                LibraryColumnFilter.Mp3Defaults);
            view.SetSessions([wave], wave, [wave]);
            Assert.Equal(Visibility.Visible, view.ColumnVisibility(LibraryFileColumn.Waveform));
            Assert.Equal(Visibility.Collapsed, view.ColumnVisibility(LibraryFileColumn.Title));

            view.SetSessions([mp3], mp3, [mp3]);
            Assert.Equal(Visibility.Visible, view.ColumnVisibility(LibraryFileColumn.Title));
            Assert.Equal(Visibility.Collapsed, view.ColumnVisibility(LibraryFileColumn.Waveform));
            Assert.Equal(Visibility.Collapsed, view.ColumnVisibility(LibraryFileColumn.SampleRate));
            Assert.Equal(LibraryColumnFilter.Mp3Defaults, view.Mp3ColumnOrder);
            Assert.Equal(LibraryColumnFilter.Mp3Defaults, view.VisibleColumnOrder);

            view.SetVisibleColumnPresets(
                LibraryColumnFilter.WaveDefaults,
                LibraryColumnFilter.Mp3Defaults);
            Assert.Equal(Visibility.Visible, view.ColumnVisibility(LibraryFileColumn.Title));
            Assert.Equal(Visibility.Collapsed, view.ColumnVisibility(LibraryFileColumn.Waveform));
        });
    }

    [Fact]
    public void Grouped_KeepsNativeHeadersAndJacketSpacer()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var view = new LibraryBrowserView();
            Assert.Equal(DataGridHeadersVisibility.None, view.HeadersVisibility);
            // 曲が無い／実ジャケットが無いときは枠を出さない。
            Assert.Equal(Visibility.Collapsed, view.GroupSpacerVisibility);
            Assert.Equal(0, view.FrozenColumnCount);

            var mp3 = Session("song.mp3");
            mp3.Document.ApplyTags(new AudioFileTags { Probed = true, Album = "A" });
            view.SetSessions([mp3], mp3, [mp3]);
            Assert.Equal(DataGridHeadersVisibility.Column, view.HeadersVisibility);
            Assert.Equal(Visibility.Collapsed, view.GroupSpacerVisibility);

            mp3.Document.SetArtwork(OnePixelPng);
            view.SetSessions([mp3], mp3, [mp3]);
            Assert.Equal(Visibility.Visible, view.GroupSpacerVisibility);
            Assert.Equal(1, view.FrozenColumnCount);

            view.SetGroup(LibraryFileGroup.None);
            Assert.Equal(DataGridHeadersVisibility.Column, view.HeadersVisibility);
            Assert.Equal(Visibility.Collapsed, view.GroupSpacerVisibility);
            Assert.Equal(0, view.FrozenColumnCount);
        });
    }

    [Fact]
    public void MixedPlaylist_WaveformColumn_RespectsFamilyPresetsPerRow()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var wave = Session("kick.wav");
            var mp3 = Session("song.mp3");
            mp3.Document.ApplyTags(new AudioFileTags { Probed = true, Title = "Song" });
            var view = new LibraryBrowserView();
            view.SetVisibleColumnPresets(
                LibraryColumnFilter.WaveDefaults,
                LibraryColumnFilter.Mp3Defaults);
            view.SetSessions([wave, mp3], wave, [wave, mp3]);
            Assert.Equal(Visibility.Visible, view.ColumnVisibility(LibraryFileColumn.Waveform));

            var waveRow = new LibraryFileRow { Name = "kick.wav", Kind = "WAVE", Tag = wave };
            var mp3Row = new LibraryFileRow { Name = "song.mp3", Kind = "MP3", Tag = mp3 };
            Assert.True(view.AllowsPlaylistWaveform(waveRow));
            Assert.False(view.AllowsPlaylistWaveform(mp3Row));
            Assert.False(LibraryPlaylistWaveform.AllowsRowDisplay(mp3Row));
            Assert.True(LibraryPlaylistWaveform.AllowsRowDisplay(waveRow));

            view.SetVisibleColumnPresets(
                LibraryColumnFilter.WaveDefaults,
                [.. LibraryColumnFilter.Mp3Defaults, LibraryFileColumn.Waveform]);
            Assert.True(view.AllowsPlaylistWaveform(mp3Row));
            Assert.True(LibraryPlaylistWaveform.AllowsRowDisplay(mp3Row));
        });
    }

    [Fact]
    public void Mp3Only_WaveformColumnOn_ShowsWaveform()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var mp3 = Session("song.mp3");
            mp3.Document.ApplyTags(new AudioFileTags { Probed = true, Title = "Song" });
            var view = new LibraryBrowserView();
            view.SetSessions([mp3], mp3, [mp3]);
            Assert.Equal(Visibility.Collapsed, view.ColumnVisibility(LibraryFileColumn.Waveform));

            view.SetColumnEnabledForTests(LibraryFileColumn.Waveform, enabled: true);
            Assert.Equal(Visibility.Visible, view.ColumnVisibility(LibraryFileColumn.Waveform));
            Assert.Contains(LibraryFileColumn.Waveform, view.Mp3ColumnOrder);
        });
    }

    [Fact]
    public void Mp3Only_WaveformInMp3Preset_ShowsWaveformColumn()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var mp3 = Session("song.mp3");
            mp3.Document.ApplyTags(new AudioFileTags { Probed = true, Title = "Song" });
            var view = new LibraryBrowserView();
            view.SetVisibleColumnPresets(
                LibraryColumnFilter.WaveDefaults,
                LibraryColumnFilter.Mp3Defaults);
            view.SetSessions([mp3], mp3, [mp3]);
            Assert.Equal(Visibility.Collapsed, view.ColumnVisibility(LibraryFileColumn.Waveform));

            view.SetVisibleColumnPresets(
                LibraryColumnFilter.WaveDefaults,
                [.. LibraryColumnFilter.Mp3Defaults, LibraryFileColumn.Waveform]);
            Assert.Equal(Visibility.Visible, view.ColumnVisibility(LibraryFileColumn.Waveform));
            Assert.Contains(LibraryFileColumn.Waveform, view.Mp3ColumnOrder);
        });
    }

    [Fact]
    public void Grouped_WaveOnly_HidesNoImageJacketFrame()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var wave = Session("kick.wav");
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 400,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetSessions([wave], wave, [wave]);
            Flush();
            view.UpdateLayout();
            Flush();

            Assert.Equal(Visibility.Collapsed, view.GroupSpacerVisibility);
            Assert.Equal(0, view.FrozenColumnCount);
            Assert.DoesNotContain(
                FindDescendants<LibraryGroupJacketImage>(view.FileGrid),
                image => image.Visibility == Visibility.Visible);
            window.Close();
        });
    }

    [Fact]
    public void Grouped_SelectionDoesNotTintJacketColumn()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var first = Session("aaa.mp3");
            first.Document.ApplyTags(new AudioFileTags { Probed = true, Album = "A", Title = "One" });
            first.Document.SetArtwork(OnePixelPng);
            var second = Session("bbb.mp3");
            second.Document.ApplyTags(new AudioFileTags { Probed = true, Album = "A", Title = "Two" });
            second.Document.SetArtwork(OnePixelPng);
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 400,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetSessions([first, second], second, [second]);
            Flush();
            view.UpdateLayout();
            Flush();

            var spacerCells = FindDescendants<DataGridCell>(view.FileGrid)
                .Where(cell => cell.Column?.DisplayIndex == 0 && cell.IsSelected)
                .ToList();
            Assert.NotEmpty(spacerCells);
            foreach (var cell in spacerCells)
            {
                Assert.Equal(Brushes.Transparent, cell.Background);
                Assert.Equal(new Thickness(0), cell.BorderThickness);
            }

            var selectedRows = FindDescendants<DataGridRow>(view.FileGrid)
                .Where(row => row.IsSelected)
                .ToList();
            Assert.NotEmpty(selectedRows);
            foreach (var row in selectedRows)
            {
                var band = AssertSelectedRowBand(row);
                Assert.Equal(LibraryBrowserView.GroupJacketColumnWidth, band.Margin.Left, 3);
            }

            window.Close();
        });
    }

    [Fact]
    public void Playlist_SelectedRowBand_FillsBehindTransparentCells()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var first = Session("aaa.wav");
            var second = Session("bbb.wav");
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 400,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetGroup(LibraryFileGroup.None);
            view.SetSessions([first, second], second, [second]);
            Flush();
            view.UpdateLayout();
            Flush();

            var selectedCells = FindDescendants<DataGridCell>(view.FileGrid)
                .Where(cell => cell.IsSelected)
                .ToList();
            Assert.True(selectedCells.Count >= 2);
            foreach (var cell in selectedCells)
            {
                Assert.Equal(Brushes.Transparent, cell.Background);
                Assert.Equal(new Thickness(0), cell.BorderThickness);
            }

            var selectedRows = FindDescendants<DataGridRow>(view.FileGrid)
                .Where(row => row.IsSelected)
                .ToList();
            Assert.Single(selectedRows);
            var band = AssertSelectedRowBand(selectedRows[0]);
            Assert.Equal(0, band.Margin.Left);
            var brush = Assert.IsType<SolidColorBrush>(band.Background);
            Assert.Equal(0x00, brush.Color.R);
            Assert.Equal(0xF5, brush.Color.G);
            Assert.Equal(0xFF, brush.Color.B);

            window.Close();
        });
    }

    [Fact]
    public void Grouped_JacketFollowsVerticalScroll()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var sessions = new DocumentSession[28];
            for (var i = 0; i < sessions.Length; i++)
            {
                var document = AudioDocument.CreateDeferred(
                    Path.Combine(Path.GetTempPath(), "sticky-album", $"track{i:D2}.mp3"));
                document.ApplyTags(new AudioFileTags
                {
                    Probed = true,
                    Title = $"Track {i:D2}",
                    Album = "Sticky Album",
                    Artist = "CAPCOM",
                });
                sessions[i] = new DocumentSession(document);
                document.SetArtwork(OnePixelPng);
            }

            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 360,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetSessions(sessions, sessions[0], [sessions[0]]);
            Flush();
            view.UpdateLayout();
            window.UpdateLayout();
            Flush();

            var jacket = FindDescendants<LibraryGroupJacketImage>(view.FileGrid).FirstOrDefault();
            Assert.NotNull(jacket);
            Assert.Equal(0, jacket!.StickyOffsetY, 1);

            view.MoveSelectionToEdge(1);
            Flush();
            view.UpdateLayout();
            Flush();

            jacket = FindDescendants<LibraryGroupJacketImage>(view.FileGrid).FirstOrDefault();
            Assert.NotNull(jacket);
            Assert.True(
                jacket!.StickyOffsetY > 24,
                $"jacket should follow scroll, offset was {jacket.StickyOffsetY}");
            window.Close();
        });
    }

    [Fact]
    public void Grouped_FirstGroupHeader_StaysVisible_AfterScrollIntoViewFromBottom()
    {
        RunSta(() =>
        {
            EnsureTheme();
            // ScrollIntoView(first row) with ScrollUnit.Item sets VerticalOffset=1 and
            // hides only the first group name under the column headers. Short lists do not reproduce.
            var sessions = new DocumentSession[60];
            for (var i = 0; i < sessions.Length; i++)
            {
                var folder = i < 8 ? "kagutsuchi" : $"folder{i / 3:D2}";
                var document = AudioDocument.CreateDeferred(
                    Path.Combine(Path.GetTempPath(), folder, $"t{i:D2}.mp4"));
                document.ApplyTags(new AudioFileTags
                {
                    Probed = true,
                    Title = $"T{i:D2}",
                    VideoWidth = 1920,
                    VideoHeight = 1080,
                    FrameRate = 30,
                    BitRateKbps = 8000,
                    VideoCodec = "H.264",
                    AudioCodec = "AAC",
                });
                sessions[i] = new DocumentSession(document);
            }

            var videoColumns = new[]
            {
                LibraryFileColumn.Name,
                LibraryFileColumn.VideoSize,
                LibraryFileColumn.FrameRate,
                LibraryFileColumn.BitRate,
                LibraryFileColumn.VideoCodec,
                LibraryFileColumn.AudioCodec,
                LibraryFileColumn.ParentFolder,
            };
            var view = new LibraryBrowserView();
            view.SetVisibleColumnPresets(videoColumns, videoColumns, videoColumns: videoColumns);
            view.SetGroup(LibraryFileGroup.Folder);
            var window = new Window
            {
                Content = view,
                Width = 1100,
                Height = 420,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetSessions(sessions, sessions[^1], [sessions[^1]]);
            Flush();
            view.UpdateLayout();
            window.UpdateLayout();
            Flush();

            var scroll = FindScroll(view.FileGrid);
            Assert.NotNull(scroll);
            scroll!.ScrollToVerticalOffset(scroll.ExtentHeight);
            Flush();
            Flush();
            Assert.True(scroll.VerticalOffset > 1, "precondition: list must be scrolled away from top");

            view.MoveSelectionToEdge(-1);
            Flush();
            Flush();
            Flush();
            view.UpdateLayout();

            var headers = FindDescendants<DataGridColumnHeadersPresenter>(view.FileGrid).First();
            Assert.True(
                headers.ActualHeight > 28,
                $"expected multi-line column headers, ActualHeight={headers.ActualHeight}");

            var header = FindDescendants<LibraryGroupHeader>(view.FileGrid)
                .FirstOrDefault(block => block.Text == "kagutsuchi");
            Assert.NotNull(header);
            Assert.True(header!.ActualHeight > 0, "first group header should be laid out");
            var headerTop = header.TransformToAncestor(view.FileGrid).Transform(new Point(0, 0)).Y;
            var viewportTop = view.RowsViewportTop();
            Assert.True(
                headerTop >= viewportTop - 1,
                $"first group header clipped under column headers: top={headerTop}, viewport={viewportTop}, offset={scroll.VerticalOffset}, headerHeight={header.ActualHeight}");
            window.Close();
        });
    }

    [Fact]
    public void Grouped_ScrollIntoView_Alone_HidesFirstGroupHeader()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var sessions = new DocumentSession[60];
            for (var i = 0; i < sessions.Length; i++)
            {
                var folder = i < 8 ? "kagutsuchi" : $"folder{i / 3:D2}";
                var document = AudioDocument.CreateDeferred(
                    Path.Combine(Path.GetTempPath(), folder, $"t{i:D2}.wav"));
                document.ApplyTags(new AudioFileTags { Probed = true, Title = $"T{i:D2}" });
                sessions[i] = new DocumentSession(document);
            }

            var view = new LibraryBrowserView();
            view.SetGroup(LibraryFileGroup.Folder);
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 420,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetSessions(sessions, sessions[^1], [sessions[^1]]);
            Flush();
            view.UpdateLayout();
            Flush();

            var grid = (DataGrid)view.FileGrid;
            var scroll = FindScroll(view.FileGrid)!;
            scroll.ScrollToVerticalOffset(scroll.ExtentHeight);
            Flush();
            Flush();

            var firstRow = grid.Items[0];
            grid.ScrollIntoView(firstRow);
            Flush();
            Flush();
            Flush();
            view.UpdateLayout();

            var header = FindDescendants<LibraryGroupHeader>(view.FileGrid)
                .FirstOrDefault(block => block.Text == "kagutsuchi");
            Assert.NotNull(header);
            var headerTop = header!.TransformToAncestor(view.FileGrid).Transform(new Point(0, 0)).Y;
            var viewportTop = view.RowsViewportTop();
            Assert.True(
                headerTop < viewportTop - 1,
                $"precondition: raw ScrollIntoView should hide first group header (top={headerTop}, viewport={viewportTop}, offset={scroll.VerticalOffset})");
            Assert.True(scroll.CanContentScroll);
            Assert.Equal(1, scroll.VerticalOffset, 3);
            window.Close();
        });
    }

    [Fact]
    public void Grouped_JacketFollowsScroll_WhenOpenedFromCollapsed()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var sessions = new DocumentSession[48];
            for (var i = 0; i < sessions.Length; i++)
            {
                var document = AudioDocument.CreateDeferred(
                    Path.Combine(Path.GetTempPath(), "sticky-album", $"track{i:D2}.mp3"));
                document.ApplyTags(new AudioFileTags
                {
                    Probed = true,
                    Title = $"Track {i:D2}",
                    Album = "Long Album",
                    Artist = "CAPCOM",
                });
                sessions[i] = new DocumentSession(document);
                document.SetArtwork(OnePixelPng);
            }

            // プレーヤー起動時と同じく、一覧は高さ 0 で畳んだまま載る。
            var host = new Grid();
            host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });
            var view = new LibraryBrowserView { Visibility = Visibility.Collapsed };
            Grid.SetRow(view, 0);
            host.Children.Add(view);
            var window = new Window
            {
                Content = host,
                Width = 900,
                Height = 640,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            Flush();
            host.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
            view.Visibility = Visibility.Visible;
            view.SetSessions(sessions, sessions[0], [sessions[0]]);
            Flush();
            view.UpdateLayout();
            window.UpdateLayout();
            Flush();

            var jacket = FindDescendants<LibraryGroupJacketImage>(view.FileGrid).First();
            Assert.Equal(0, jacket.StickyOffsetY, 1);
            var header = view.RowsViewportTop();

            var scroll = FindScroll(view.FileGrid);
            Assert.NotNull(scroll);
            scroll!.ScrollToVerticalOffset(16);
            Flush();
            view.UpdateLayout();
            Flush();

            jacket = FindDescendants<LibraryGroupJacketImage>(view.FileGrid).First();
            var followed = jacket.TransformToAncestor(view.FileGrid).Transform(new Point(0, 0)).Y;
            Assert.True(
                jacket.StickyOffsetY > 24,
                $"jacket should follow after the list was collapsed at startup, offset was {jacket.StickyOffsetY}");
            Assert.InRange(followed, header - 4, header + 8);

            scroll.ScrollToVerticalOffset(0);
            Flush();
            view.UpdateLayout();
            Flush();
            jacket = FindDescendants<LibraryGroupJacketImage>(view.FileGrid).First();
            Assert.Equal(0, jacket.StickyOffsetY, 1);
            window.Close();
        });
    }

    [Fact]
    public void SetVisibleColumns_RemembersDisplayOrderAndSavesOnReorder()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var view = new LibraryBrowserView();
            LibraryColumnPresets? saved = null;
            view.VisibleColumnPresetsChanged += (_, presets) => saved = presets;
            var order = new[]
            {
                LibraryFileColumn.Duration,
                LibraryFileColumn.Name,
                LibraryFileColumn.Title,
            };
            view.SetVisibleColumns(order);
            var active = LibraryColumnFilter.ResolveActive([], order, order, videoColumns: order);
            Assert.Equal(active, view.VisibleColumnOrder);
            Assert.Equal(0, view.GroupSpacerDisplayIndex);
            Assert.True(
                view.ColumnDisplayIndex(LibraryFileColumn.Name)
                < view.ColumnDisplayIndex(LibraryFileColumn.Duration));
            Assert.True(
                view.ColumnDisplayIndex(LibraryFileColumn.Duration)
                < view.ColumnDisplayIndex(LibraryFileColumn.Title));

            view.MoveVisibleColumnForTests(
                LibraryFileColumn.Duration,
                view.ColumnDisplayIndex(LibraryFileColumn.Title));
            Assert.NotNull(saved);
            Assert.Equal(saved!.Value.Wave, view.WaveColumnOrder);
            Assert.Equal(saved.Value.Mp3, view.Mp3ColumnOrder);
            Assert.Equal(saved.Value.Video, view.VideoColumnOrder);
            Assert.Equal(saved.Value.Mixed, view.MixedColumnOrder);
            Assert.True(saved.Value.PersistMixed);
            Assert.Equal(
                LibraryColumnFilter.ResolveActive(
                    [],
                    saved.Value.Wave,
                    saved.Value.Mp3,
                    saved.Value.Mixed,
                    saved.Value.Video),
                view.VisibleColumnOrder);
            Assert.Contains(LibraryFileColumn.Name, saved.Value.Mixed);
            Assert.Contains(LibraryFileColumn.Title, saved.Value.Mixed);
            Assert.Equal(LibraryFileColumn.Duration, saved.Value.Mixed[^1]);
            // 空／混在の並べ替えは混在プリセットだけ。Wave / MP3 / Video 設定は触らない。
            Assert.Equal(view.WaveColumnOrder, saved.Value.Wave);
            Assert.Equal(view.Mp3ColumnOrder, saved.Value.Mp3);
            Assert.Equal(view.VideoColumnOrder, saved.Value.Video);
            Assert.NotEqual(saved.Value.Mixed, saved.Value.Wave);

            view.SetVisibleColumnPresets(
                LibraryColumnFilter.WaveDefaults,
                LibraryColumnFilter.Mp3Defaults,
                saved.Value.Mixed,
                mixedCustomized: true,
                videoColumns: LibraryColumnFilter.VideoDefaults);
            Assert.Equal(
                LibraryColumnFilter.ResolveActive(
                    [],
                    LibraryColumnFilter.WaveDefaults,
                    LibraryColumnFilter.Mp3Defaults,
                    saved.Value.Mixed,
                    LibraryColumnFilter.VideoDefaults),
                view.VisibleColumnOrder);
            Assert.True(
                view.ColumnDisplayIndex(LibraryFileColumn.Title)
                < view.ColumnDisplayIndex(LibraryFileColumn.Duration));
        });
    }

    [Fact]
    public void ColumnHeaderContextMenu_TogglesColumnAndNotifies()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var view = new LibraryBrowserView();
            LibraryColumnPresets? saved = null;
            view.VisibleColumnPresetsChanged += (_, presets) => saved = presets;
            view.SetVisibleColumns(
                [
                    LibraryFileColumn.Name,
                    LibraryFileColumn.Title,
                    LibraryFileColumn.Duration,
                ]);

            var menu = view.ColumnHeaderContextMenuForTests;
            Assert.NotNull(menu);
            var toggleable = LibraryColumnFilter.All.Count(c => !LibraryColumnFilter.IsLocked(c));
            Assert.Equal(toggleable, menu!.Items.OfType<MenuItem>().Count());
            Assert.DoesNotContain(
                menu.Items.OfType<MenuItem>(),
                item => item.Tag is LibraryFileColumn.Name);

            var kind = menu.Items.OfType<MenuItem>()
                .First(item => item.Tag is LibraryFileColumn.Kind);
            Assert.False(kind.IsChecked);
            Assert.True(kind.IsEnabled);

            menu.IsOpen = true;
            Assert.True(view.HasOpenContextMenu);
            kind.IsChecked = true;
            kind.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.NotNull(saved);
            Assert.True(saved!.Value.PersistMixed);
            Assert.Contains(LibraryFileColumn.Kind, saved.Value.Mixed);
            Assert.Contains(LibraryFileColumn.Name, saved.Value.Mixed);
            Assert.Contains(LibraryFileColumn.Title, saved.Value.Mixed);
            Assert.Contains(LibraryFileColumn.Duration, saved.Value.Mixed);
            Assert.DoesNotContain(LibraryFileColumn.Kind, saved.Value.Wave);
            Assert.Equal(
                LibraryColumnFilter.ResolveActive(
                    [],
                    saved.Value.Wave,
                    saved.Value.Mp3,
                    saved.Value.Mixed,
                    saved.Value.Video),
                view.VisibleColumnOrder);

            var title = menu.Items.OfType<MenuItem>()
                .First(item => item.Tag is LibraryFileColumn.Title);
            title.IsChecked = false;
            title.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.DoesNotContain(LibraryFileColumn.Title, saved!.Value.Mixed);
            Assert.Contains(LibraryFileColumn.Kind, saved.Value.Mixed);
            view.CloseKeyboardContextMenu();
            Assert.False(view.HasOpenContextMenu);
        });
    }

    [Fact]
    public void Grouped_HorizontalScroll_KeepsHeaderAlignedWithCells()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var sessions = new DocumentSession[12];
            for (var i = 0; i < sessions.Length; i++)
            {
                var document = AudioDocument.CreateDeferred(
                    Path.Combine(Path.GetTempPath(), "album", $"track-{i:D2}-very-long-name.mp3"));
                document.ApplyTags(new AudioFileTags
                {
                    Probed = true,
                    Title = $"Very Long Title {i:D2} for horizontal scroll",
                    Artist = "CAPCOM",
                    Album = "Album",
                    Comment = Path.Combine(@"V:\very\long\folder\path\for\scroll", $"disc{i}"),
                });
                sessions[i] = new DocumentSession(document);
                document.SetArtwork(OnePixelPng);
            }

            var view = new LibraryBrowserView();
            view.SetVisibleColumns(LibraryColumnFilter.All);
            var window = new Window
            {
                Content = view,
                Width = 640,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetSessions(sessions, sessions[0], [sessions[0]]);
            Flush();
            view.UpdateLayout();
            window.UpdateLayout();
            Flush();

            AssertAligned(view, 0);
            if (FindScroll(view.FileGrid) is { } scroll)
            {
                scroll.ScrollToHorizontalOffset(140);
                Flush();
                view.UpdateLayout();
                Flush();
                Assert.True(scroll.HorizontalOffset > 1, "expected horizontal scroll to move");
            }

            AssertAligned(view, 2);
            window.Close();
        });
    }

    [Fact]
    public void Grouped_HorizontalScroll_PinsJacketAndGroupName()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var sessions = new DocumentSession[8];
            for (var i = 0; i < sessions.Length; i++)
            {
                var document = AudioDocument.CreateDeferred(
                    Path.Combine(Path.GetTempPath(), "album", $"track-{i:D2}-very-long-name.mp3"));
                document.ApplyTags(new AudioFileTags
                {
                    Probed = true,
                    Title = $"Very Long Title {i:D2} for horizontal scroll",
                    Artist = "CAPCOM",
                    Album = "King Street Sounds",
                    Comment = Path.Combine(@"V:\very\long\folder\path\for\scroll", $"disc{i}"),
                });
                sessions[i] = new DocumentSession(document);
                document.SetArtwork(OnePixelPng);
            }

            var view = new LibraryBrowserView();
            view.SetVisibleColumns(LibraryColumnFilter.All);
            var window = new Window
            {
                Content = view,
                Width = 640,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetSessions(sessions, sessions[0], [sessions[0]]);
            Flush();
            view.UpdateLayout();
            window.UpdateLayout();
            Flush();

            var jacket = FindDescendants<LibraryGroupJacketImage>(view.FileGrid).First();
            var header = FindDescendants<LibraryGroupHeader>(view.FileGrid).First();
            var jacketX = VisualX(jacket, view.FileGrid);
            var headerX = VisualX(header, view.FileGrid);
            var scroll = FindScroll(view.FileGrid);
            Assert.NotNull(scroll);
            scroll!.ScrollToHorizontalOffset(180);
            Flush();
            view.UpdateLayout();
            Flush();
            Assert.True(scroll.HorizontalOffset > 40, "expected horizontal scroll to move");
            Assert.InRange(VisualX(jacket, view.FileGrid), jacketX - 2, jacketX + 2);
            Assert.InRange(VisualX(header, view.FileGrid), headerX - 2, headerX + 2);
            window.Close();
        });
    }

    [Fact]
    public void Playlist_HorizontalKeyAndWheelHelpers_ScrollAndRevealBars()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var sessions = new DocumentSession[8];
            for (var i = 0; i < sessions.Length; i++)
            {
                var document = AudioDocument.CreateDeferred(
                    Path.Combine(Path.GetTempPath(), "album", $"track-{i:D2}-very-long-name.mp3"));
                document.ApplyTags(new AudioFileTags
                {
                    Probed = true,
                    Title = $"Very Long Title {i:D2} for horizontal scroll",
                    Artist = "CAPCOM",
                    Album = "Album",
                    Comment = Path.Combine(@"V:\very\long\folder\path\for\scroll", $"disc{i}"),
                });
                sessions[i] = new DocumentSession(document);
                document.SetArtwork(OnePixelPng);
            }

            var view = new LibraryBrowserView();
            view.SetVisibleColumns(LibraryColumnFilter.All);
            var window = new Window
            {
                Content = view,
                Width = 640,
                Height = 480,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetSessions(sessions, sessions[0], [sessions[0]]);
            Flush();
            view.UpdateLayout();
            window.UpdateLayout();
            Flush();

            Assert.True(view.CanScrollPlaylistHorizontally());
            var scroll = FindScroll(view.FileGrid);
            Assert.NotNull(scroll);
            Assert.True(
                view.TryScrollPlaylistHorizontal(DesignMetrics.LibraryPlaylistHorizontalKeyStep));
            Flush();
            Assert.True(scroll!.HorizontalOffset > 1);
            Assert.Contains(
                FindDescendants<ScrollBar>(view.FileGrid),
                bar => LibraryScrollReveal.GetRevealed(bar));

            var before = scroll.HorizontalOffset;
            var wheelDelta = LibraryPlayerMode.PlaylistHorizontalWheelDelta(
                -Mouse.MouseWheelDeltaForOneLine,
                DesignMetrics.LibraryPlaylistHorizontalWheelStep);
            Assert.True(view.TryScrollPlaylistHorizontal(wheelDelta));
            Flush();
            Assert.True(scroll.HorizontalOffset > before);

            window.Close();
        });
    }

    [Fact]
    public void ScrollPlaylistByRows_SurvivesLongDurationRowUpdate()
    {
        RunSta(() =>
        {
            EnsureTheme();
            // 画面に収まりきらない件数＋グループ化で、Alt スクロールと行差し替えが競合しうる状態にする。
            var sessions = new DocumentSession[48];
            for (var i = 0; i < sessions.Length; i++)
            {
                var folder = i < 24 ? "Alpha" : "Radio";
                var document = AudioDocument.CreateDeferred(
                    Path.Combine(Path.GetTempPath(), folder, $"track-{i:D2}.mp3"));
                document.ApplyTags(new AudioFileTags
                {
                    Probed = true,
                    Title = $"Track {i:D2}",
                    Artist = "K-ON",
                    Album = folder,
                    DurationSeconds = i == 30 ? 10 : 180,
                });
                sessions[i] = new DocumentSession(document);
            }

            var view = new LibraryBrowserView();
            view.SetVisibleColumns(LibraryColumnFilter.All);
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 420,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetSessions(sessions, sessions[0], [sessions[0]]);
            Flush();
            view.UpdateLayout();
            window.UpdateLayout();
            // BindRows 後の FitColumns（Background）まで消化してからスクロールする。
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Flush();

            var scroll = FindScroll(view.FileGrid);
            Assert.NotNull(scroll);
            Assert.True(scroll!.ScrollableHeight > 8, "precondition: playlist must be scrollable");

            view.ScrollPlaylistByRows(3);
            Flush();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Flush();
            // グループ化 Item スクロールが y=3 付近で view を変えて戻しても、Repin で残ること。
            Assert.True(
                scroll.VerticalOffset > 2.5,
                $"Alt scroll must stick near y=3 after layout repin, got {scroll.VerticalOffset}");

            view.ScrollPlaylistByRows(9);
            Flush();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
            Flush();
            // 選択行の BringIntoView／グループ見出し補正が走っても戻らないこと。
            view.FocusList();
            view.SelectSessionQuiet(sessions[0]);
            Flush();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
            Flush();
            var offset = scroll.VerticalOffset;
            Assert.True(offset > 4, $"Alt scroll must stick after FocusList/SelectSessionQuiet, got {offset}");

            // 付近の超長尺曲のタグが後から着いたときの行差し替え。
            sessions[30].Document.ApplyTags(new AudioFileTags
            {
                Probed = true,
                Title = "Track 30",
                Artist = "K-ON",
                Album = "Radio",
                DurationSeconds = 110 * 60 + 15.23,
            });
            view.UpdateSessionRow(sessions[30]);
            // 差し替えの遅延復元より後のユーザー操作が勝つこと。
            view.ScrollPlaylistByRows(3);
            Flush();
            var afterUser = scroll.VerticalOffset;
            Assert.True(afterUser > offset, $"expected further Alt scroll, before={offset}, after={afterUser}");
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
            Flush();

            Assert.Equal(afterUser, scroll.VerticalOffset, 1);
            Assert.Same(sessions[0], view.SelectedSession);

            view.RematerializeBoundPlaylistRows();
            Flush();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
            Flush();

            Assert.Equal(afterUser, scroll.VerticalOffset, 1);
            Assert.Same(sessions[0], view.SelectedSession);

            // 再帰読込末尾の並び確定相当。
            view.LockPlaylistOrder(sessions[0], [sessions[0]]);
            Flush();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
            Flush();
            Assert.Equal(afterUser, scroll.VerticalOffset, 1);

            // 追加フラッシュ相当。選択は据え置きで末尾へ足す。
            var extra = new DocumentSession(
                AudioDocument.CreateDeferred(
                    Path.Combine(Path.GetTempPath(), "Radio", "track-extra.mp3")));
            extra.Document.ApplyTags(new AudioFileTags
            {
                Probed = true,
                Title = "Extra",
                Artist = "K-ON",
                Album = "Radio",
                DurationSeconds = 200,
            });
            view.AppendSessionsRaw([extra], select: null);
            Flush();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
            Flush();
            Assert.Equal(afterUser, scroll.VerticalOffset, 1);

            window.Close();
        });
    }

    [Fact]
    public void Playlist_EndPad_AllowsLastRowAboveViewportBottom()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var sessions = new DocumentSession[36];
            for (var i = 0; i < sessions.Length; i++)
            {
                var document = AudioDocument.CreateDeferred(
                    Path.Combine(Path.GetTempPath(), "end-pad", $"track{i:D2}.mp3"));
                document.ApplyTags(new AudioFileTags
                {
                    Probed = true,
                    Title = $"Track {i:D2}",
                    Album = "End Pad Album",
                    Artist = "CAPCOM",
                });
                sessions[i] = new DocumentSession(document);
            }

            var view = new LibraryBrowserView();
            view.SetVisibleColumns(LibraryColumnFilter.All);
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 360,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetSessions(sessions, sessions[^1], [sessions[^1]]);
            Flush();
            view.UpdateLayout();
            window.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Flush();

            view.MoveSelectionToEdge(1);
            Flush();
            view.UpdateLayout();
            Flush();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
            Flush();

            var scroll = FindScroll(view.FileGrid);
            Assert.NotNull(scroll);
            var grid = Assert.IsType<DataGrid>(view.FileGrid);
            var row = grid.ItemContainerGenerator.ContainerFromItem(grid.SelectedItem)
                as FrameworkElement;
            Assert.NotNull(row);
            var bounds = row!.TransformToAncestor(grid)
                .TransformBounds(new Rect(row.RenderSize));
            Assert.True(
                bounds.Bottom <= view.RowsViewportBottom() + 1.5,
                $"last row should sit above viewport bottom: bottom={bounds.Bottom}, limit={view.RowsViewportBottom()}");
            window.Close();
        });
    }

    [Fact]
    public void WheelScroll_AfterAltScroll_DoesNotSnapBackToPinnedOffset()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var sessions = new DocumentSession[40];
            for (var i = 0; i < sessions.Length; i++)
            {
                var document = AudioDocument.CreateDeferred(
                    Path.Combine(Path.GetTempPath(), "wheel-after-alt", $"track{i:D2}.mp3"));
                document.ApplyTags(new AudioFileTags
                {
                    Probed = true,
                    Title = $"Track {i:D2}",
                    Album = "Wheel Album",
                    Artist = "CAPCOM",
                });
                sessions[i] = new DocumentSession(document);
            }

            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 360,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetSessions(sessions, sessions[0], [sessions[0]]);
            Flush();
            view.UpdateLayout();
            window.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Flush();

            var scroll = FindScroll(view.FileGrid);
            Assert.NotNull(scroll);
            Assert.True(scroll!.ScrollableHeight > 10, "precondition: playlist must be scrollable");

            // Alt スクロールで pin を立て、離し相当の Flush で Idle 後に pin を捨てる。
            view.PlaylistAltScrollKeyHeldForTest = () => true;
            view.ScrollPlaylistByRows(4);
            view.PlaylistAltScrollKeyHeldForTest = () => false;
            view.FlushPlaylistAltScrollChrome();
            Flush();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Flush();
            var afterAlt = scroll.VerticalOffset;
            Assert.True(afterAlt > 2.5, $"Alt scroll should move, got {afterAlt}");

            // ホイール相当のネイティブ縦スクロール。pin が残っていると Repin で戻される。
            scroll.ScrollToVerticalOffset(afterAlt + 6);
            Flush();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            Flush();

            Assert.True(
                scroll.VerticalOffset > afterAlt + 3,
                $"wheel after Alt must keep new offset, alt={afterAlt}, now={scroll.VerticalOffset}");
            view.PlaylistAltScrollKeyHeldForTest = null;
            window.Close();
        });
    }

    [Fact]
    public void MoveSelectionPage_DefersStickyChromeWhileNavKeyHeld()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var sessions = new DocumentSession[48];
            for (var i = 0; i < sessions.Length; i++)
            {
                var document = AudioDocument.CreateDeferred(
                    Path.Combine(Path.GetTempPath(), "page-nav-album", $"track{i:D2}.mp3"));
                document.ApplyTags(new AudioFileTags
                {
                    Probed = true,
                    Title = $"Track {i:D2}",
                    Album = "Page Nav Album",
                    Artist = "CAPCOM",
                });
                sessions[i] = new DocumentSession(document);
                document.SetArtwork(OnePixelPng);
            }

            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 360,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetSessions(sessions, sessions[0], [sessions[0]]);
            Flush();
            view.UpdateLayout();
            window.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Flush();

            var jacket = FindDescendants<LibraryGroupJacketImage>(view.FileGrid).FirstOrDefault();
            Assert.NotNull(jacket);
            Assert.Equal(0, jacket!.StickyOffsetY, 1);

            // PgDn 押し続け相当：移動は進めるが sticky はキー離しまで溜める。
            view.PlaylistNavigationKeyHeldForTest = () => true;
            view.MoveSelectionPage(1, extend: false, wrap: false);
            view.MoveSelectionPage(1, extend: false, wrap: false);
            Flush();
            view.UpdateLayout();
            Flush();

            Assert.NotSame(sessions[0], view.SelectedSession);
            Assert.True(
                view.IsPlaylistGroupViewportDirty || jacket.StickyOffsetY < 1,
                "sticky chrome should stay deferred while Page navigation key is held");

            view.PlaylistNavigationKeyHeldForTest = () => false;
            view.FlushPlaylistMoveDefer();
            Flush();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
            Flush();

            Assert.False(view.IsPlaylistGroupViewportDirty);
            jacket = FindDescendants<LibraryGroupJacketImage>(view.FileGrid).FirstOrDefault();
            Assert.NotNull(jacket);
            Assert.True(
                jacket!.StickyOffsetY > 24,
                $"jacket should catch up after Page key release, offset was {jacket.StickyOffsetY}");

            view.PlaylistNavigationKeyHeldForTest = null;
            window.Close();
        });
    }

    [Fact]
    public void MoveSelectionPage_MatchesExplorer_VisibleEdgeThenPage()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var sessions = new DocumentSession[64];
            for (var i = 0; i < sessions.Length; i++)
            {
                var document = AudioDocument.CreateDeferred(
                    Path.Combine(Path.GetTempPath(), "page-explorer", $"track{i:D2}.mp3"));
                document.ApplyTags(new AudioFileTags
                {
                    Probed = true,
                    Title = $"Track {i:D2}",
                    Album = "Page Explorer Album",
                    Artist = "CAPCOM",
                });
                sessions[i] = new DocumentSession(document);
            }

            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 360,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetSessions(sessions, sessions[0], [sessions[0]]);
            Flush();
            view.UpdateLayout();
            window.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Flush();

            var scroll = FindScroll(view.FileGrid);
            Assert.NotNull(scroll);
            Assert.True(scroll!.ScrollableHeight > 12, "precondition: playlist must be scrollable");

            // 表示内の中ほどへ（スクロールは発生しない想定）。
            view.MoveSelection(4, extend: false, wrap: false);
            Flush();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
            Flush();
            var mid = view.PlaylistCaretFileIndex();
            Assert.True(mid > 0, $"precondition: mid caret, got {mid}");
            var offsetBefore = scroll.VerticalOffset;

            view.ArmPlaylistPageNavLite();
            view.PlaylistNavigationKeyHeldForTest = () => true;
            // 1回目: いま見えている最下段へ。スクロールしない。
            view.MoveSelectionPage(1, extend: false, wrap: false);
            Flush();
            for (var i = 0; i < 4; i++)
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Flush();
            }

            var edge = view.PlaylistCaretFileIndex();
            Assert.True(edge > mid, $"first PageDown should move to visible bottom (mid={mid}, edge={edge})");
            Assert.Equal(offsetBefore, scroll.VerticalOffset, 1);

            // 2回目: 既に端なので1ページ送り。
            view.MoveSelectionPage(1, extend: false, wrap: false);
            Flush();
            for (var i = 0; i < 8; i++)
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Flush();
            }

            var paged = view.PlaylistCaretFileIndex();
            Assert.True(paged > edge, $"second PageDown should page beyond visible edge (edge={edge}, paged={paged})");
            Assert.True(
                scroll.VerticalOffset > offsetBefore + 0.5,
                $"second PageDown should scroll (before={offsetBefore}, after={scroll.VerticalOffset})");

            view.PlaylistNavigationKeyHeldForTest = () => false;
            view.FlushPlaylistMoveDefer();
            Flush();
            for (var i = 0; i < 8; i++)
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Flush();
            }

            view.UpdateLayout();
            Flush();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
            Flush();

            // 2回目以降も、新ページの可視最下段に乗っていること。
            var grid = Assert.IsType<DataGrid>(view.FileGrid);
            var row = grid.ItemContainerGenerator.ContainerFromItem(grid.SelectedItem)
                as FrameworkElement;
            Assert.NotNull(row);
            var bounds = row!.TransformToAncestor(grid)
                .TransformBounds(new Rect(row.RenderSize));
            var gapFromBottom = view.RowsViewportBottom() - bounds.Bottom;
            Assert.True(
                gapFromBottom < 40,
                $"paged selection should sit on visible bottom (gap={gapFromBottom}, bottom={bounds.Bottom}, viewportBottom={view.RowsViewportBottom()})");

            view.PlaylistNavigationKeyHeldForTest = null;
            window.Close();
        });
    }

    [Fact]
    public void ScrollPlaylistByRows_DefersStickyChromeUntilKeyRelease()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var sessions = new DocumentSession[36];
            for (var i = 0; i < sessions.Length; i++)
            {
                var document = AudioDocument.CreateDeferred(
                    Path.Combine(Path.GetTempPath(), "alt-scroll-album", $"track{i:D2}.mp3"));
                document.ApplyTags(new AudioFileTags
                {
                    Probed = true,
                    Title = $"Track {i:D2}",
                    Album = "Alt Scroll Album",
                    Artist = "CAPCOM",
                });
                sessions[i] = new DocumentSession(document);
                document.SetArtwork(OnePixelPng);
            }

            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 900,
                Height = 360,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
            };
            window.Show();
            view.SetSessions(sessions, sessions[0], [sessions[0]]);
            Flush();
            view.UpdateLayout();
            window.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Flush();

            var scroll = FindScroll(view.FileGrid);
            Assert.NotNull(scroll);
            Assert.True(scroll!.ScrollableHeight > 8, "precondition: playlist must be scrollable");

            var jacket = FindDescendants<LibraryGroupJacketImage>(view.FileGrid).FirstOrDefault();
            Assert.NotNull(jacket);
            Assert.Equal(0, jacket!.StickyOffsetY, 1);

            // 押し続け中は sticky を溜め、リピート差分は Input 優先度で合流する。
            view.PlaylistAltScrollKeyHeldForTest = () => true;
            view.ScrollPlaylistByRows(2, coalesceRepeat: false);
            view.ScrollPlaylistByRows(3, coalesceRepeat: true);
            view.ScrollPlaylistByRows(4, coalesceRepeat: true);
            Flush();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Input);
            Flush();

            Assert.True(view.IsPlaylistAltScrollChromeActive);
            Assert.True(
                scroll.VerticalOffset > 6.5,
                $"coalesced Alt scroll should land near y=9, got {scroll.VerticalOffset}");
            // 押し続け中は TransformToAncestor の sticky 更新を端折る。
            Assert.True(
                view.IsPlaylistGroupViewportDirty || jacket.StickyOffsetY < 1,
                "sticky chrome should stay deferred while Alt scroll key is held");

            view.PlaylistAltScrollKeyHeldForTest = () => false;
            view.FlushPlaylistAltScrollChrome();
            Flush();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
            Flush();

            Assert.False(view.IsPlaylistAltScrollChromeActive);
            Assert.False(view.IsPlaylistGroupViewportDirty);
            jacket = FindDescendants<LibraryGroupJacketImage>(view.FileGrid).FirstOrDefault();
            Assert.NotNull(jacket);
            Assert.True(
                jacket!.StickyOffsetY > 20,
                $"jacket should catch up after Alt scroll key release, offset was {jacket.StickyOffsetY}");
            Assert.Same(sessions[0], view.SelectedSession);

            view.PlaylistAltScrollKeyHeldForTest = null;
            window.Close();
        });
    }

    private static int CountCyanInHeader(Window window, DataGridColumnHeader header)
    {
        window.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(window);
        var origin = header.TransformToAncestor(window).Transform(new Point(0, 0));
        var copied = new System.Windows.Media.Imaging.FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
        copied.Freeze();
        var stride = width * 4;
        var pixels = new byte[stride * height];
        copied.CopyPixels(pixels, stride, 0);
        var x0 = Math.Clamp((int)origin.X, 0, width - 1);
        var y0 = Math.Clamp((int)origin.Y, 0, height - 1);
        var x1 = Math.Clamp((int)(origin.X + header.ActualWidth), 0, width);
        var y1 = Math.Clamp((int)(origin.Y + header.ActualHeight), 0, height);
        var cyan = 0;
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var i = y * stride + x * 4;
                if (pixels[i + 3] < 20)
                {
                    continue;
                }

                if (pixels[i] > 180 && pixels[i + 1] > 120 && pixels[i + 2] < 180)
                {
                    cyan++;
                }
            }
        }

        return cyan;
    }

    private static DataGridColumnHeader HeaderLabeled(LibraryBrowserView view, string label) =>
        FindDescendants<DataGridColumnHeader>(view.FileGrid)
            .First(h => FindDescendants<TextBlock>(h).Any(block => block.Text == label));

    private static System.Windows.Shapes.Path NamedPath(DependencyObject root, string name) =>
        FindDescendants<System.Windows.Shapes.Path>(root).First(path => path.Name == name);

    private static void AssertSortMarks(DataGridColumnHeader header, bool ascending)
    {
        Assert.Equal(
            ascending
                ? System.ComponentModel.ListSortDirection.Ascending
                : System.ComponentModel.ListSortDirection.Descending,
            header.SortDirection);
        var up = NamedPath(header, "ArrowUp");
        var down = NamedPath(header, "ArrowDown");
        Assert.Equal(Visibility.Visible, up.Visibility);
        Assert.Equal(Visibility.Visible, down.Visibility);
        Assert.True(up.ActualHeight is >= 4 and <= 7, $"up height {up.ActualHeight}");
        Assert.True(down.ActualHeight is >= 4 and <= 7, $"down height {down.ActualHeight}");
        var cyan = (SolidColorBrush)Application.Current!.Resources["AccentCyanBrush"];
        var active = ascending ? up : down;
        var idle = ascending ? down : up;
        Assert.Equal(cyan.Color, Assert.IsType<SolidColorBrush>(active.Fill).Color);
        Assert.Equal(1, active.Opacity);
        Assert.NotEqual(cyan.Color, Assert.IsType<SolidColorBrush>(idle.Fill).Color);
        var textRight = FindDescendants<TextBlock>(header)
            .Where(block => block.Text.Length > 0)
            .Select(block => block.TransformToAncestor(header).Transform(new Point(block.ActualWidth, 0)).X)
            .First();
        var markX = up.TransformToAncestor(header).Transform(new Point(0, 0)).X;
        Assert.InRange(markX, textRight - 4, textRight + 48);
        Assert.True(markX + up.ActualWidth < header.ActualWidth - 2, $"up arrow clipped at {markX + up.ActualWidth} of {header.ActualWidth}");
    }

    private static double VisualX(FrameworkElement element, FrameworkElement root) =>
        element.TransformToAncestor(root).Transform(new Point(0, 0)).X;

    private static void AssertAligned(LibraryBrowserView view, double tolerance)
    {
        var grid = view.FileGrid;
        var header = FindDescendants<DataGridColumnHeader>(grid)
            .FirstOrDefault(h => FindDescendants<TextBlock>(h).Any(block => block.Text == UiStrings.LibraryColumnName));
        Assert.NotNull(header);
        var cell = FindDescendants<DataGridCell>(grid)
            .FirstOrDefault(c => c.Column?.Header is LibrarySortHeader sort && sort.Label == UiStrings.LibraryColumnName);
        Assert.NotNull(cell);
        var headerX = header!.TransformToAncestor(grid).Transform(new Point(0, 0)).X;
        var cellX = cell!.TransformToAncestor(grid).Transform(new Point(0, 0)).X;
        Assert.InRange(cellX, headerX - tolerance, headerX + tolerance);
    }

    private static ScrollViewer? FindScroll(DependencyObject root)
    {
        foreach (var scroll in FindDescendants<ScrollViewer>(root))
        {
            return scroll;
        }

        return null;
    }

    private static ScrollViewer? FindAncestorScroll(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is ScrollViewer scroll)
            {
                return scroll;
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return null;
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindDescendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    private static Border AssertSelectedRowBand(DataGridRow row)
    {
        var named = FindDescendants<Border>(row)
            .FirstOrDefault(border => border.Name == LibraryBrowserView.PlaylistRowBandName);
        var presenter = FindDescendants<DataGridCellsPresenter>(row).FirstOrDefault();
        var band = named;
        if (band is null && presenter is not null)
        {
            var parent = VisualTreeHelper.GetParent(presenter);
            if (parent is Border parentBand)
            {
                band = parentBand;
            }
            else if (parent is not null)
            {
                band = FindDescendants<Border>(parent)
                    .FirstOrDefault(border => border.Name == LibraryBrowserView.PlaylistRowBandName);
            }
        }

        Assert.True(
            band is not null,
            presenter is null
                ? "DataGridCellsPresenter was not found."
                : $"Row band Border was not found. CellsPresenter parent is {VisualTreeHelper.GetParent(presenter)?.GetType().Name ?? "null"}.");
        var brush = Assert.IsType<SolidColorBrush>(band!.Background);
        Assert.Equal(
            LibraryBrowserView.LibrarySelectionFillAlphaFor(UiThemeService.Current),
            brush.Color.A);
        return band;
    }

    private static DocumentSession Session(string name) =>
        new(AudioDocument.CreateDeferred(Path.Combine(Path.GetTempPath(), name)));

    private static void WriteMinimalWav(string path)
    {
        var pcm = new byte[4];
        var fmt = new byte[]
        {
            1, 0, 1, 0, 0x44, 0xAC, 0, 0, 0x88, 0x58, 0x01, 0, 2, 0, 16, 0,
        };
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + pcm.Length);
        w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        w.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        w.Write(fmt.Length);
        w.Write(fmt);
        w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        w.Write(pcm.Length);
        w.Write(pcm);
        File.WriteAllBytes(path, ms.ToArray());
    }

    private static void WriteWavWithExtraChunk(string path, string fourCc, byte[] payload)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(fourCc.Length, 4);
        var pcm = new byte[4];
        var fmt = new byte[]
        {
            1, 0, 1, 0, 0x44, 0xAC, 0, 0, 0x88, 0x58, 0x01, 0, 2, 0, 16, 0,
        };
        var padded = payload.Length + (payload.Length & 1);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + pcm.Length + 8 + padded);
        w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        w.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        w.Write(fmt.Length);
        w.Write(fmt);
        w.Write(System.Text.Encoding.ASCII.GetBytes(fourCc));
        w.Write(payload.Length);
        w.Write(payload);
        if ((payload.Length & 1) != 0)
        {
            w.Write((byte)0);
        }

        w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        w.Write(pcm.Length);
        w.Write(pcm);
        File.WriteAllBytes(path, ms.ToArray());
    }

    private static readonly byte[] OnePixelPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54,
        0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00, 0x05, 0x00, 0x01,
        0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00,
        0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    ];

    private static void EnsureTheme()
    {
        if (Application.Current is null)
        {
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        }

        var resources = Application.Current!.Resources;
        // ヘッダ修復ボタン等で UiColors 全体が要る。キー追加後も古いマージのままにしない。
        if (resources["ClearButtonFillBrush"] is null || resources["ChromeBackBrush"] is null)
        {
            var assembly = Uri.EscapeDataString(typeof(LibraryBrowserView).Assembly.GetName().Name!);
            resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    $"pack://application:,,,/{assembly};component/Themes/UiColors.xaml",
                    UriKind.Absolute),
            });
        }

        resources["AccentCyanBrush"] = new SolidColorBrush(Color.FromRgb(0x00, 0xF5, 0xFF));
        resources["PrimaryForeBrush"] = new SolidColorBrush(Color.FromRgb(0xEB, 0xEB, 0xEB));
        resources["MenuHighlightBackBrush"] = new SolidColorBrush(Color.FromRgb(0x37, 0x37, 0x3A));
        resources["ColorPanelBackBrush"] = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x2A));
        resources["ChromeBorderBrush"] = new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x4E));
        // MonitorLatchButton のフィールド初期化（CreateOption のコンストラクタ）で参照される。
        resources["TransportBackBrush"] = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2E));
        resources["LatchToggleOnForeBrush"] = new SolidColorBrush(Colors.White);
        resources["LatchToggleOffForeBrush"] = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8));
        resources["LatchToggleOnBackBrush"] = new SolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0xED));
        resources["LatchToggleOffBackBrush"] = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x40));
        // RefreshAppearance が BUSY バッジ色を読む。
        resources["PlaylistBusyBadgeBackBrush"] =
            new SolidColorBrush(Color.FromRgb(0xBE, 0x32, 0x32));
    }

    private static void Flush() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void PumpUntil(Func<bool> done, TimeSpan timeout)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = timeout };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        var poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        poll.Tick += (_, _) =>
        {
            if (!done())
            {
                return;
            }

            poll.Stop();
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        poll.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
