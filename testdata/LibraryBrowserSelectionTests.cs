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
            Assert.Same(source, view.BoundItemsSource);
            Assert.Same(second, view.SelectedSession);
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
    public void Layout_HasExplorerThenList()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var view = new LibraryBrowserView();
            Assert.Equal(3, view.RootColumnCount);
            Assert.Equal(UiStrings.LibraryExplorerLabel, view.ExplorerPaneTitle);
            Assert.Equal(UiStrings.LibraryFavoritesLabel, view.FavoritesPaneTitle);
            Assert.Equal(UiStrings.LibraryPlaylistLabel, view.PlaylistPaneTitle);
            Assert.Equal(Dock.Right, view.PlaylistGroupDock);
            Assert.Equal(DesignMetrics.LibraryFavoritesSplitterHitHeight, view.FavoritesSplitter.Height);
            Assert.Equal(VerticalAlignment.Top, view.FavoritesSplitter.VerticalAlignment);
            Assert.Equal(Cursors.SizeNS, view.FavoritesSplitter.Cursor);
            Assert.Equal(DesignMetrics.LibrarySplitterHitThickness, view.ExplorerSplitter.Width);
            Assert.Equal(HorizontalAlignment.Left, view.ExplorerSplitter.HorizontalAlignment);
            Assert.Equal(Cursors.SizeWE, view.ExplorerSplitter.Cursor);
            Assert.False(view.ExplorerSplitter.Focusable);
            Assert.False(view.FavoritesSplitter.Focusable);
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
            Assert.Equal(
                LibraryBrowserView.LibrarySelectionFillAlpha,
                LibraryBrowserView.LibrarySelectionFillAlphaLight);
            Assert.True(
                LibraryBrowserView.LibrarySelectionFillAlphaLight
                < LibraryBrowserView.LibraryHoverFillAlpha);
            var navy = Color.FromRgb(0x1A, 0x90, 0xA8);
            var lightRgb = LibraryBrowserView.LibrarySelectionRgb(navy, UiTheme.Light);
            Assert.Equal(UiThemePalette.ColorFor(UiTheme.Light, "PlayerSelectionFillBrush"), lightRgb);
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
                UiStrings.LibraryMenuCatPitch,
                UiStrings.LibraryMenuCatTree,
                UiStrings.LibraryMenuCatOther,
            ],
            items.Select(item => (string)item.Header).ToArray());
            var playlist = items[0];
            var edit = items[1];
            var tree = items[3];
            var other = items[4];
            Assert.Equal("Enter", ((MenuItem)playlist.Items[0]).InputGestureText);
            Assert.Equal("Shift+Enter", ((MenuItem)playlist.Items[1]).InputGestureText);
            Assert.Equal("Ctrl+Enter", ((MenuItem)playlist.Items[2]).InputGestureText);
            Assert.Equal("Ctrl+Shift+Enter", ((MenuItem)playlist.Items[3]).InputGestureText);
            Assert.Equal("Ctrl+C", ((MenuItem)edit.Items[0]).InputGestureText);
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
                UiStrings.LibraryMenuCatPitch,
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
            Assert.Equal(
            [
                UiStrings.LibraryMenuColor,
                UiStrings.LibraryMenuCatPitch,
                UiStrings.LibraryMenuCatEdit,
                UiStrings.LibraryMenuCatOther,
            ],
            view.OpenContextMenuItems.Select(item => (string)item.Header).ToArray());
            var listPitch = view.OpenContextMenuItems[1];
            var listEdit = view.OpenContextMenuItems[2];
            var listOther = view.OpenContextMenuItems[3];
            Assert.Equal(MenuGestures.PreviousTrack, ((MenuItem)listPitch.Items[0]).InputGestureText);
            Assert.Equal(MenuGestures.NextTrack, ((MenuItem)listPitch.Items[1]).InputGestureText);
            Assert.Equal(MenuGestures.RestartTrack, ((MenuItem)listPitch.Items[2]).InputGestureText);
            Assert.Equal("Alt+P", ((MenuItem)listPitch.Items[3]).InputGestureText);
            Assert.Equal(MenuGestures.PitchUp, ((MenuItem)listPitch.Items[5]).InputGestureText);
            Assert.Equal(MenuGestures.PitchDown, ((MenuItem)listPitch.Items[6]).InputGestureText);
            Assert.Equal(MenuGestures.PitchReset, ((MenuItem)listPitch.Items[7]).InputGestureText);
            Assert.Equal("Ctrl+C", ((MenuItem)listEdit.Items[0]).InputGestureText);
            Assert.Equal(UiStrings.LibraryMenuClearFromPlaylist, (string)((MenuItem)listEdit.Items[6]).Header);
            Assert.True(string.IsNullOrEmpty(((MenuItem)listEdit.Items[6]).InputGestureText));
            Assert.Equal("V", ((MenuItem)listOther.Items[0]).InputGestureText);
            Assert.Equal("Ctrl+Shift+M", ((MenuItem)listOther.Items[1]).InputGestureText);
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
            // トラックが消える（報告されたバグ）。閉じてからアイドルまではガードを維持する。
            view.CloseKeyboardContextMenu();
            // 閉じた直後は HasOpenContextMenu が false。旧コードはこれだけを見ていたので、
            // 貫通クリックを弾けずにプレイヤーを抜けていた（＝バグ）。
            Assert.False(view.HasOpenContextMenu);
            // 貫通クリックは閉じたのと同じ入力の中（アイドル前）で届くので、ガードはまだ立っている。
            Assert.True(view.IsContextMenuDismissGuardActive);
            // plain-press 抑制もアイドルまで残す（閉じた直後の MouseUp で単一選択確定しない）。
            Assert.True(view.IsPlaylistPlainPressSuppressedAfterMenu);

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
            var first = Path.Combine(root, "a.txt");
            var second = Path.Combine(root, "b.txt");
            var third = Path.Combine(root, "c.txt");
            File.WriteAllText(first, "");
            File.WriteAllText(second, "");
            File.WriteAllText(third, "");
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
    public void LabelLibraryShuffle_IsEnglishWithoutShortcut()
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
    public void LabelLibraryAutoPlay_IsEnglishWithoutShortcut()
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
    public void LabelLibraryAutoPlayNext_IsEnglishWithoutShortcut()
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
    public void LabelLibraryAutoLoop_IsEnglishWithoutShortcut()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.Japanese);
            Assert.Equal("Auto Loop", UiStrings.LabelLibraryAutoLoop);
            Assert.Null(MenuAccessKeys.Read(UiStrings.LabelLibraryAutoLoop));
            Assert.DoesNotContain("Alt+", UiStrings.LabelLibraryAutoLoop, StringComparison.OrdinalIgnoreCase);
            UiStrings.SetLanguage(UiLanguage.English);
            Assert.Equal("Auto Loop", UiStrings.LabelLibraryAutoLoop);
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

        Application.Current!.Resources["AccentCyanBrush"] = new SolidColorBrush(Color.FromRgb(0x00, 0xF5, 0xFF));
        Application.Current.Resources["PrimaryForeBrush"] = new SolidColorBrush(Color.FromRgb(0xEB, 0xEB, 0xEB));
        Application.Current.Resources["MenuHighlightBackBrush"] = new SolidColorBrush(Color.FromRgb(0x37, 0x37, 0x3A));
        Application.Current.Resources["ColorPanelBackBrush"] = new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x2A));
        Application.Current.Resources["ChromeBorderBrush"] = new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x4E));
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
