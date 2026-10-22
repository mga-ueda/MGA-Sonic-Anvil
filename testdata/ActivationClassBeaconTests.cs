using System.Runtime.InteropServices;
using System.Windows;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class ActivationClassBeaconTests
{
    [Fact]
    public void WindowClassName_IsStableAsciiToken()
    {
        Assert.Equal("MGASonicAnvil", ActivationClassBeacon.WindowClassName);
        Assert.DoesNotContain(' ', ActivationClassBeacon.WindowClassName);
    }

    [Fact]
    public void Start_CreatesFindableWindow_WithFixedClassAndProductTitle()
    {
        RunSta(() =>
        {
            var host = new Window
            {
                Width = 100,
                Height = 100,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow,
                Opacity = 0,
            };
            try
            {
                host.Show();
                ActivationClassBeacon.Start(host);

                var hwnd = FindWindow(ActivationClassBeacon.WindowClassName, null);
                Assert.NotEqual(IntPtr.Zero, hwnd);
                Assert.Equal(hwnd, ActivationClassBeacon.Handle);

                var titled = FindWindow(
                    ActivationClassBeacon.WindowClassName,
                    AppVersion.ProductName);
                Assert.Equal(hwnd, titled);

                ActivationClassBeacon.Stop();
                Assert.Equal(IntPtr.Zero, ActivationClassBeacon.Handle);
                Assert.Equal(IntPtr.Zero, FindWindow(ActivationClassBeacon.WindowClassName, null));
            }
            finally
            {
                ActivationClassBeacon.Stop();
                host.Close();
            }
        });
    }

    private static void RunSta(Action body)
    {
        Exception? fault = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                fault = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (fault is not null)
        {
            throw fault;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);
}
