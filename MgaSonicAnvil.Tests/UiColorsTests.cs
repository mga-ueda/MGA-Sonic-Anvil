using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class UiColorsTests
{
    [Fact]
    public void TryWrite_MutatesTheSameBrush()
    {
        RunSta(() =>
        {
            var brush = new SolidColorBrush(Colors.Red);
            var dictionary = new ResourceDictionary { ["TestBrush"] = brush };
            Assert.True(UiColors.TryWrite(dictionary, "TestBrush", Colors.Blue));
            Assert.Same(brush, dictionary["TestBrush"]);
            Assert.Equal(Colors.Blue, brush.Color);
        });
    }

    [Fact]
    public void TryWrite_FollowsMergedDictionary()
    {
        RunSta(() =>
        {
            var brush = new SolidColorBrush(Color.FromRgb(0xB6, 0xB6, 0xB6));
            var merged = new ResourceDictionary { ["WaveFillBrush"] = brush };
            var root = new ResourceDictionary();
            root.MergedDictionaries.Add(merged);
            Assert.True(UiColors.TryWrite(root, "WaveFillBrush", Color.FromRgb(0x00, 0xF5, 0xFF)));
            Assert.Equal(Color.FromRgb(0x00, 0xF5, 0xFF), brush.Color);
            Assert.Same(brush, root["WaveFillBrush"]);
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
