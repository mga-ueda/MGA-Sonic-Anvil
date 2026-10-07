using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class TextBoxContextMenuTests
{
    [Fact]
    public void DarkTextBoxStyle_ClipboardCommandsOnly()
    {
        RunSta(() =>
        {
            EnsureControls();
            var box = new TextBox
            {
                Style = (Style)Application.Current!.FindResource("DarkTextBoxStyle"),
            };
            var menu = Assert.IsType<ContextMenu>(box.ContextMenu);
            Assert.Equal(
                new RoutedUICommand[]
                {
                    ApplicationCommands.Cut,
                    ApplicationCommands.Copy,
                    ApplicationCommands.Paste,
                },
                menu.Items.OfType<MenuItem>().Select(item => item.Command));
            Assert.Equal(3, menu.Items.Count);
        });
    }

    private static void EnsureControls()
    {
        if (Application.Current is null)
        {
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        }

        var resources = Application.Current!.Resources;
        if (resources["DarkTextBoxStyle"] is Style)
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
