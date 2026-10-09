using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class AudioSettingsExplorerRootsTests
{
    [Fact]
    public void MissingExplorerRoot_ShowsErrorForeground()
    {
        RunSta(() =>
        {
            EnsureTheme();
            Application.Current!.Resources[LibraryBrowserView.MissingRegisteredPathForeBrushKey] =
                new SolidColorBrush(Color.FromRgb(0xFF, 0x6E, 0x6E));

            var existing = Path.Combine(Path.GetTempPath(), "mga-settings-root-ok-" + Guid.NewGuid().ToString("N"));
            var missing = Path.Combine(Path.GetTempPath(), "mga-settings-root-gone-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(existing);
            Window? window = null;
            try
            {
                var list = new ListBox
                {
                    Width = 400,
                    Height = 200,
                };
                AudioSettingsWindow.ApplyLibraryExplorerRootTemplate(list);
                list.Items.Add(new AudioSettingsWindow.LibraryExplorerRootItem(existing));
                list.Items.Add(new AudioSettingsWindow.LibraryExplorerRootItem(missing));

                window = new Window
                {
                    Content = list,
                    Width = 440,
                    Height = 260,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow,
                };
                window.Show();
                Flush();
                list.UpdateLayout();
                Flush();

                var missingFull = Path.GetFullPath(missing).TrimEnd('\\', '/');
                var existingFull = Path.GetFullPath(existing).TrimEnd('\\', '/');
                var texts = FindDescendants<TextBlock>(list)
                    .Where(block => !string.IsNullOrWhiteSpace(block.Text))
                    .ToArray();
                var missingBlock = Assert.Single(
                    texts,
                    block => Path.GetFullPath(block.Text).TrimEnd('\\', '/')
                        .Equals(missingFull, StringComparison.OrdinalIgnoreCase));
                var existingBlock = Assert.Single(
                    texts,
                    block => Path.GetFullPath(block.Text).TrimEnd('\\', '/')
                        .Equals(existingFull, StringComparison.OrdinalIgnoreCase));

                var missingFore = Assert.IsType<SolidColorBrush>(missingBlock.Foreground);
                Assert.Equal(Color.FromRgb(0xFF, 0x6E, 0x6E), missingFore.Color);

                var existingFore = Assert.IsType<SolidColorBrush>(existingBlock.Foreground);
                Assert.NotEqual(Color.FromRgb(0xFF, 0x6E, 0x6E), existingFore.Color);
                Assert.True(LibraryExplorerPaths.IsMissing(missing));
                Assert.False(LibraryExplorerPaths.IsMissing(existing));
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

    private static void EnsureTheme()
    {
        if (Application.Current is null)
        {
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        }

        Application.Current!.Resources["PrimaryForeBrush"] = new SolidColorBrush(Color.FromRgb(0xEB, 0xEB, 0xEB));
        Application.Current.Resources[LibraryBrowserView.MissingRegisteredPathForeBrushKey] =
            new SolidColorBrush(Color.FromRgb(0xFF, 0x6E, 0x6E));
    }

    private static void Flush() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

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
