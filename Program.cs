using System.Text;
using MgaSonicAnvil.Config;

namespace MgaSonicAnvil;

static class Program
{
    [STAThread]
    static void Main()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        if (!SingleInstance.TryAcquire())
        {
            SingleInstance.RequestActivate();
            return;
        }

        try
        {
            AppStorage.Initialize();
            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
        finally
        {
            SingleInstance.Release();
        }
    }
}
