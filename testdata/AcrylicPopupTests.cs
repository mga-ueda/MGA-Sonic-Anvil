using System.Runtime.ExceptionServices;
using System.Windows.Controls.Primitives;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class AcrylicPopupTests
{
    [Fact]
    public void Attach_CanSubscribeWithoutThrowing()
    {
        RunSta(() =>
        {
            var popup = new Popup();
            AcrylicPopup.Attach(popup);
            AcrylicPopup.Attach(popup);
            Assert.False(popup.IsOpen);
        });
    }

    [Fact]
    public void Install_CanRegisterClassHandlersWithoutThrowing()
    {
        RunSta(AcrylicPopup.Install);
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
