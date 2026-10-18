using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibrarySearchTests
{
    [Fact]
    public void PlaylistSearch_FiltersRowsAndSelectsFirstHit()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var hit = Session("bgm_loop.wav");
            var miss = Session("se_hit.wav");
            var view = new LibraryBrowserView();
            view.SetSessions([hit, miss], hit, [hit]);
            Flush();
            Assert.Equal(2, view.BoundRowCount);

            view.ApplyPlaylistSearchSync("loop");
            Flush();
            Assert.Equal(1, view.BoundRowCount);
            Assert.Same(hit, view.SelectedSession);

            view.ApplyPlaylistSearchSync("beatles|loop");
            Flush();
            Assert.Equal(1, view.BoundRowCount);

            view.ApplyPlaylistSearchSync("");
            Flush();
            Assert.Equal(2, view.BoundRowCount);
        });
    }

    [Fact]
    public void PlaylistSearch_DoesNotFilterUntilCommit()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var hit = Session("bgm_loop.wav");
            var miss = Session("se_hit.wav");
            var view = new LibraryBrowserView();
            view.SetSessions([hit, miss], hit, [hit]);
            Flush();
            Assert.Equal(2, view.BoundRowCount);

            view.PlaylistSearchBox.Text = "loop";
            Flush();
            Assert.Equal(2, view.BoundRowCount);

            view.ApplyPlaylistSearchSync("loop");
            Flush();
            Assert.Equal(1, view.BoundRowCount);
        });
    }

    [Fact]
    public void PlaylistSearch_AndAcrossColumns_UsesNameAndFolder()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var hit = Session(@"C:\Music\Beatles\yesterday.wav");
            var miss = Session(@"C:\Music\Beatles\help.wav");
            var view = new LibraryBrowserView();
            view.SetSessions([hit, miss], hit, [hit]);
            Flush();

            view.ApplyPlaylistSearchSync("beatles yesterday");
            Flush();
            Assert.Equal(1, view.BoundRowCount);
            Assert.Same(hit, view.SelectedSession);
        });
    }

    [Fact]
    public void ExplorerSearch_ShowsOnlyFoldersWithMatchingFiles()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var root = Path.Combine(Path.GetTempPath(), "mga-lib-search-" + Guid.NewGuid().ToString("N"));
            var albumA = Path.Combine(root, "AlbumA");
            var albumB = Path.Combine(root, "AlbumB");
            var nested = Path.Combine(albumA, "CD1");
            Directory.CreateDirectory(nested);
            Directory.CreateDirectory(albumB);
            File.WriteAllBytes(Path.Combine(albumA, "bgm_loop.wav"), [0]);
            File.WriteAllBytes(Path.Combine(nested, "voice.wav"), [0]);
            File.WriteAllBytes(Path.Combine(albumB, "se_hit.wav"), [0]);
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

                var before = view.ExplorerVisibleFolderPaths.ToArray();
                view.ExplorerSearchBox.Text = "loop";
                Flush();
                Assert.Equal(before, view.ExplorerVisibleFolderPaths.ToArray());

                view.ApplyExplorerSearchSync("loop");
                Flush();
                var paths = view.ExplorerVisibleFolderPaths
                    .Select(path => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                Assert.Contains(Path.GetFullPath(root).TrimEnd('\\', '/'), paths);
                Assert.Contains(Path.GetFullPath(albumA).TrimEnd('\\', '/'), paths);
                Assert.DoesNotContain(Path.GetFullPath(albumB).TrimEnd('\\', '/'), paths);
                Assert.DoesNotContain(Path.GetFullPath(nested).TrimEnd('\\', '/'), paths);

                view.ApplyExplorerSearchSync("");
                Flush();
                var restored = view.ExplorerVisibleFolderPaths;
                Assert.Contains(
                    Path.GetFullPath(root).TrimEnd('\\', '/'),
                    restored.Select(path => path.TrimEnd('\\', '/')));

                view.ApplyExplorerSearchSync("AlbumB");
                Flush();
                var byFolder = view.ExplorerVisibleFolderPaths
                    .Select(path => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                Assert.Contains(Path.GetFullPath(root).TrimEnd('\\', '/'), byFolder);
                Assert.Contains(Path.GetFullPath(albumB).TrimEnd('\\', '/'), byFolder);
                Assert.DoesNotContain(Path.GetFullPath(albumA).TrimEnd('\\', '/'), byFolder);

                view.ApplyExplorerSearchSync("loop");
                Flush();
                var walk = view.SnapshotExplorerPlaylistWalk();
                var registered = LibrarySearchQuery.CollectPlaylistFiles([root], walk.Groups, walk.Visible)
                    .Select(Path.GetFileName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                Assert.Contains("bgm_loop.wav", registered);
                Assert.DoesNotContain("voice.wav", registered);
                Assert.DoesNotContain("se_hit.wav", registered);
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
    public void PlaylistSearch_CommitReturnsFocusToList()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var hit = Session("bgm_loop.wav");
            var miss = Session("se_hit.wav");
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
            view.SetSessions([hit, miss], hit, [hit]);
            Flush();

            view.FocusPlaylistSearch();
            Flush();
            Assert.True(view.IsPlaylistSearchFocused);
            view.PlaylistSearchBox.Text = "loop";
            view.CommitSearchFocus();
            Flush();
            Assert.Equal(1, view.BoundRowCount);
            Assert.False(view.IsPlaylistSearchFocused);
            Assert.True(view.IsPlaylistGridFocused);
            Assert.True(view.IsListKeyboardFocused);
            window.Close();
        });
    }

    [Fact]
    public void ExplorerSearch_CommitReturnsFocusToTree()
    {
        RunSta(() =>
        {
            EnsureTheme();
            var root = Path.Combine(Path.GetTempPath(), "mga-lib-focus-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            File.WriteAllBytes(Path.Combine(root, "bgm_loop.wav"), [0]);
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

                view.FocusExplorerSearch();
                Flush();
                Assert.True(view.IsExplorerSearchFocused);
                view.ExplorerSearchBox.Text = "loop";
                view.CommitSearchFocus();
                Flush();
                Assert.False(view.IsExplorerSearchFocused);
                Assert.True(view.IsExplorerTreeFocused);
                Assert.True(view.IsExplorerFocused);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SearchEditors_MatchPlayerComboChrome(bool light)
    {
        RunSta(() =>
        {
            var theme = light ? UiTheme.Light : UiTheme.Dark;
            EnsurePlayerChrome();
            UiThemePalette.Apply(theme);
            var view = new LibraryBrowserView();
            var window = new Window
            {
                Content = view,
                Width = 960,
                Height = 420,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
                Background = (Brush)Application.Current!.FindResource("SurfaceBackBrush"),
            };
            window.Show();
            Flush();
            view.UpdateLayout();
            window.UpdateLayout();
            Flush();

            AssertSearchMatchesCombo(view.ExplorerSearchBox, view.ExplorerSearchHint, view.GroupCombo);
            AssertSearchMatchesCombo(view.PlaylistSearchBox, view.PlaylistSearchHint, view.GroupCombo);
            Assert.Equal(
                view.PlaylistSearchBox.TranslatePoint(new Point(0, 0), view).Y,
                view.ExplorerSearchBox.TranslatePoint(new Point(0, 0), view).Y,
                1);
            window.Close();
        });
    }

    private static DocumentSession Session(string pathOrName)
    {
        var path = pathOrName.Contains(Path.DirectorySeparatorChar) || pathOrName.Contains(Path.AltDirectorySeparatorChar)
            ? pathOrName
            : Path.Combine(Path.GetTempPath(), pathOrName);
        return new DocumentSession(AudioDocument.CreateDeferred(path));
    }

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
        Application.Current.Resources["DialogInputBackBrush"] = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x22));
        Application.Current.Resources["MutedForeBrush"] = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA0));
    }

    private static void EnsurePlayerChrome()
    {
        if (Application.Current is null)
        {
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        }

        var resources = Application.Current!.Resources;
        if (resources["LibrarySearchBoxStyle"] is Style)
        {
            return;
        }

        var assembly = Uri.EscapeDataString(typeof(LibraryBrowserView).Assembly.GetName().Name!);
        resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/{assembly};component/Themes/UiColors.xaml", UriKind.Absolute),
        });
        resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/{assembly};component/Themes/Controls.xaml", UriKind.Absolute),
        });
    }

    private static void AssertSearchMatchesCombo(TextBox box, TextBlock hint, ComboBox combo)
    {
        box.ApplyTemplate();
        combo.ApplyTemplate();
        Assert.Equal(DesignMetrics.AudioInputHeight, box.Height);
        Assert.Equal(DesignMetrics.AudioInputHeight, combo.Height);
        Assert.Equal(combo.ActualHeight, box.ActualHeight, 1);
        Assert.Same(Application.Current!.FindResource("LibrarySearchBoxStyle"), box.Style);
        Assert.Same(Application.Current.FindResource("LibraryComboBoxStyle"), combo.Style);

        var searchFill = Assert.IsType<Border>(box.Template.FindName("Fill", box));
        var comboFill = Assert.IsType<Border>(combo.Template.FindName("Fill", combo));
        var searchBrush = Assert.IsType<SolidColorBrush>(searchFill.Background);
        var comboBrush = Assert.IsType<SolidColorBrush>(comboFill.Background);
        Assert.Equal(comboBrush.Color, searchBrush.Color);
        Assert.Equal(
            ((SolidColorBrush)Application.Current.FindResource("PlayerComboFillBrush")).Color,
            searchBrush.Color);
        Assert.Equal(
            ((SolidColorBrush)Application.Current.FindResource("AccentCyanBrush")).Color,
            Assert.IsType<SolidColorBrush>(box.CaretBrush).Color);
        Assert.Equal(
            ((SolidColorBrush)Application.Current.FindResource("PrimaryForeBrush")).Color,
            Assert.IsType<SolidColorBrush>(box.Foreground).Color);
        Assert.Equal(
            ((SolidColorBrush)Application.Current.FindResource("MutedForeBrush")).Color,
            Assert.IsType<SolidColorBrush>(hint.Foreground).Color);
        Assert.Equal(Visibility.Visible, hint.Visibility);
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
