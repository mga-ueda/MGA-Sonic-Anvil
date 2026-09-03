using System.Text;
using MgaSonicAnvil.Config;

namespace MgaSonicAnvil;

static class Program
{
    [STAThread]
    static void Main()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        AppStorage.Initialize();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
