using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
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
    public void Layout_HasExplorerThenList()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var view = new LibraryBrowserView();
            Assert.Equal(3, view.RootColumnCount);
        });
    }

    [Fact]
    public void GroupJacketColumnWidth_MatchesJacketPlusMargins()
    {
        Assert.Equal(DesignMetrics.LibraryGroupJacketSize + 16, LibraryBrowserView.GroupJacketColumnWidth);
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
                UiStrings.LibraryMenuReplacePlaylist,
                UiStrings.LibraryMenuAppendPlaylist,
                UiStrings.LibraryMenuAddToFavorites,
            ],
            items.Select(item => (string)item.Header).ToArray());
            Assert.Equal("Enter", items[0].InputGestureText);
            Assert.Equal("Shift+Enter", items[1].InputGestureText);
            items[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            items[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(1, replaced);
            Assert.Equal(1, appended);
            view.CloseKeyboardContextMenu();

            view.FocusFavorites();
            Flush();
            Assert.True(view.TryOpenKeyboardContextMenu());
            Assert.Equal(UiStrings.LibraryMenuRemoveFromFavorites, view.OpenContextMenuHeader);
            view.CloseKeyboardContextMenu();

            view.FocusList();
            Flush();
            Assert.True(view.TryOpenKeyboardContextMenu());
            Assert.Equal(UiStrings.LibraryMenuClearFromPlaylist, view.OpenContextMenuHeader);
            view.CloseKeyboardContextMenu();
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
        });
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

            var album = HeaderLabeled(view, UiStrings.LibraryColumnAlbum);
            AssertSortMarks(album, ascending: true);
            Assert.True(CountCyanInHeader(window, album) > 8);

            view.SetSessions([Session("01 a.mp3")], null, []);
            Flush();
            view.UpdateLayout();
            Flush();
            album = HeaderLabeled(view, UiStrings.LibraryColumnAlbum);
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
                artist < 120,
                $"artist column should pack to short content, was {artist}");
            Assert.True(
                name < 220,
                $"file column should pack to short names, was {name}");
            window.Close();
        });
    }

    [Fact]
    public void Grouped_KeepsNativeHeadersAndJacketSpacer()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var view = new LibraryBrowserView();
            Assert.Equal(DataGridHeadersVisibility.Column, view.HeadersVisibility);
            Assert.Equal(Visibility.Visible, view.GroupSpacerVisibility);
            Assert.Equal(1, view.FrozenColumnCount);

            view.SetGroup(LibraryFileGroup.None);
            Assert.Equal(DataGridHeadersVisibility.Column, view.HeadersVisibility);
            Assert.Equal(Visibility.Collapsed, view.GroupSpacerVisibility);
            Assert.Equal(0, view.FrozenColumnCount);
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
            var second = Session("bbb.mp3");
            second.Document.ApplyTags(new AudioFileTags { Probed = true, Album = "A", Title = "Two" });
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
            }

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

    private static DocumentSession Session(string name) =>
        new(AudioDocument.CreateDeferred(Path.Combine(Path.GetTempPath(), name)));

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
