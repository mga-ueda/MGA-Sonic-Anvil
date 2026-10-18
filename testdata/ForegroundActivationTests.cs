using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Windows;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class ForegroundActivationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PulseTopmost_AlwaysChangesTwiceAndRestores(bool keepTop)
    {
        RunSta(() =>
        {
            var window = new Window { Topmost = keepTop };
            var changes = 0;
            var descriptor = DependencyPropertyDescriptor.FromProperty(
                Window.TopmostProperty,
                typeof(Window));
            void OnChanged(object? _, EventArgs __) => changes++;
            descriptor.AddValueChanged(window, OnChanged);
            try
            {
                ForegroundActivation.PulseTopmost(window);
            }
            finally
            {
                descriptor.RemoveValueChanged(window, OnChanged);
            }

            Assert.Equal(2, changes);
            Assert.Equal(keepTop, window.Topmost);
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
