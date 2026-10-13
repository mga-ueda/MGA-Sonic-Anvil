using System.Text;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.UI;

namespace MgaSonicAnvil;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // 同権限のシェルからの DnD / WM_DROPFILES 用（UIPI メッセージフィルタ）。
        // 通常 Explorer → 管理者アプリの OLE ドロップは OS が遮断し、回避不可。
        UipiFileDrop.AllowProcessWide();
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
