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

        // VST3 エディタ（createView / attached）診断。WPF 前に抜ける。
        if (args.Length >= 2
            && string.Equals(args[0], Vst3EditorSmoke.CliSwitch, StringComparison.Ordinal))
        {
            var classId = args.Length >= 3 ? args[2] : null;
            return Vst3EditorSmoke.Run(args[1], classId);
        }

        // 差し替えハング診断。
        if (args.Length >= 3
            && string.Equals(args[0], Vst3ReplaceSmoke.CliSwitch, StringComparison.Ordinal))
        {
            return Vst3ReplaceSmoke.Run(args[1], args[2]);
        }

        // 実 WPF ウィンドウで UI 表示まで確認。
        if (args.Length >= 2
            && string.Equals(args[0], Vst3WpfUiSmoke.CliSwitch, StringComparison.Ordinal))
        {
            return Vst3WpfUiSmoke.Run(args[1]);
        }

        // WaveShell スキャン時展開の件数確認。
        if (args.Length >= 2
            && string.Equals(args[0], "--vst-waveshell-expand", StringComparison.Ordinal))
        {
            var info = new Vst3PluginInfo(
                System.IO.Path.GetFileNameWithoutExtension(args[1]),
                args[1],
                DateTimeOffset.UtcNow);
            var meta = new Vst3PluginMetadata("WaveShell", "Waves", "Fx", string.Empty, null);
            var expanded = Vst3PluginMetadata.ExpandWaveShellAtScan(info, meta);
            var msg = "count=" + expanded.Count + Environment.NewLine;
            foreach (var e in expanded.Take(8))
            {
                msg += e.Metadata.Name + " | " + e.Metadata.ClassId + Environment.NewLine;
            }

            var bytes = Encoding.UTF8.GetBytes(msg);
            using var stdout = Console.OpenStandardOutput();
            stdout.Write(bytes, 0, bytes.Length);
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mga-waveshell-expand.log"),
                msg);
            return expanded.Count > 0 ? 0 : 2;
        }

        // 実 MainWindow（Debug）で VST 差し替え／削除後挿入を自動検証。
        // --vst-app-replace-smoke soft alti [audio]
        // --vst-app-clear-then-smoke soft alti [audio]  … Soft 削除 → Altiverb（ユーザー手順）
        // --vst-app-altiverb-only-smoke alti [audio]
        if (args.Length >= 2
            && (string.Equals(args[0], "--vst-app-replace-smoke", StringComparison.OrdinalIgnoreCase)
                || string.Equals(args[0], "--vst-app-clear-then-smoke", StringComparison.OrdinalIgnoreCase)
                || string.Equals(args[0], "--vst-app-altiverb-only-smoke", StringComparison.OrdinalIgnoreCase)))
        {
            var mode = args[0] switch
            {
                var s when s.Contains("clear-then", StringComparison.OrdinalIgnoreCase) => "clear-then",
                var s when s.Contains("altiverb-only", StringComparison.OrdinalIgnoreCase) => "altiverb-only",
                _ => "replace",
            };
            string soft;
            string alti;
            string audio;
            if (mode == "altiverb-only")
            {
                soft = "";
                alti = args[1];
                audio = args.Length >= 3
                    ? args[2]
                    : System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Click", "Low.wav");
            }
            else
            {
                soft = args[1];
                alti = args[2];
                audio = args.Length >= 4
                    ? args[3]
                    : System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Click", "Low.wav");
            }

            MainWindow.PendingVstAppReplaceSmoke = new MainWindow.VstAppReplaceSmokeRequest(soft, alti, audio, mode);
            args = [audio];
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
