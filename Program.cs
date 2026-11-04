using System.Text;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.UI;

namespace MgaSonicAnvil;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // F5「デバッガなし」: preLaunchTask が exe を起動済み。dll ホストは即終了
        // （Cursor の noDebug でもデバッガが残り Altiverb が読めないため）。
        if (string.Equals(
                Environment.GetEnvironmentVariable("MGA_F5_HANDOFF_EXE"),
                "1",
                StringComparison.Ordinal))
        {
            return 0;
        }

        // VST3 工場問い合わせ用の子プロセス入口（WPF / 二重起動の前に抜ける）。
        if (args.Length >= 2
            && string.Equals(args[0], Vst3FactoryProbe.CliSwitch, StringComparison.Ordinal))
        {
            return Vst3FactoryProbe.RunCli(args[1]);
        }

        // 同権限のシェルからの DnD / WM_DROPFILES 用（UIPI メッセージフィルタ）。
        // 通常 Explorer → 管理者アプリの OLE ドロップは OS が遮断し、回避不可。
        UipiFileDrop.AllowProcessWide();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var files = LaunchFiles.Collect(args);
        if (!SingleInstance.TryAcquireOrTakeOver())
        {
            SingleInstance.RequestActivate(files);
            return 0;
        }

        try
        {
            LaunchFiles.SetStartup(files);
            AppStorage.Initialize();
            LibraryCatalog.Initialize();
            SpectrogramCache.DeleteLeftoverTempFiles();
            LameEncoder.DeleteLeftoverTemps();
            VideoProxy.PruneExpired(
                AppStorage.Settings.ResolvedVideoProxyRetentionDays(),
                DateTime.UtcNow);
            Domain.UiStrings.SetLanguage(Domain.UiStrings.ParseLanguage(AppStorage.Settings.UiLanguage));
            var app = new App();
            app.InitializeComponent();
            app.Run();
            return 0;
        }
        finally
        {
            SingleInstance.Release();
        }
    }
}
