using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class WaapiStatusBarTests
{
    [Fact]
    public void TryClearBadgeCanvas_DoesNotWipeWhenUnmeasured()
    {
        RunSta(() =>
        {
            var canvas = new Canvas();
            canvas.Children.Add(new TextBlock { Text = "CONNECT" });
            Assert.False(WaapiStatusBar.TryClearBadgeCanvas(canvas));
            Assert.Single(canvas.Children);
        });
    }

    [Fact]
    public void TryClearBadgeCanvas_ClearsWhenArranged()
    {
        RunSta(() =>
        {
            var canvas = new Canvas();
            canvas.Measure(new Size(40, 18));
            canvas.Arrange(new Rect(0, 0, 40, 18));
            canvas.Children.Add(new TextBlock { Text = "CONNECT" });
            Assert.True(WaapiStatusBar.TryClearBadgeCanvas(canvas));
            Assert.Empty(canvas.Children);
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
