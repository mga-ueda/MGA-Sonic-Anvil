using System.Text;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;

namespace MgaSonicAnvil;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var files = LaunchFiles.Collect(args);
        if (!SingleInstance.TryAcquireOrTakeOver())
        {
            SingleInstance.RequestActivate(files);
            return;
        }

        try
        {
            LaunchFiles.SetStartup(files);
            AppStorage.Initialize();
            SpectrogramCache.DeleteLeftoverTempFiles();
            LameEncoder.DeleteLeftoverTemps();
            VideoProxy.PruneExpired(
                AppStorage.Settings.ResolvedVideoProxyRetentionDays(),
                DateTime.UtcNow);
            Domain.UiStrings.SetLanguage(Domain.UiStrings.ParseLanguage(AppStorage.Settings.UiLanguage));
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
