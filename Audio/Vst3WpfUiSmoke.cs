using System.Text;
using System.Windows;
using System.Windows.Threading;
using MgaSonicAnvil.UI;

namespace MgaSonicAnvil.Audio;

/// <summary>
/// 実 WPF ウィンドウで Altiverb 等の UI が出るまで確認する。
/// 用法: アプリ.exe --vst-wpf-ui-smoke "C:\...\Plugin.vst3"
/// </summary>
internal static class Vst3WpfUiSmoke
{
    public const string CliSwitch = "--vst-wpf-ui-smoke";

    public static int Run(string pluginPath)
    {
        var logPath = Path.Combine(Path.GetTempPath(), "mga-vst-wpf-ui-smoke.log");
        var lines = new List<string>();
        void Log(string line)
        {
            lines.Add(line);
            try
            {
                var bytes = Encoding.UTF8.GetBytes(line + Environment.NewLine);
                using var stdout = Console.OpenStandardOutput();
                stdout.Write(bytes, 0, bytes.Length);
                stdout.Flush();
            }
            catch
            {
            }
        }

        try
        {
            Log("path=" + pluginPath);
            Log("exe=" + Environment.ProcessPath);
            Log("time=" + DateTimeOffset.Now.ToString("o"));

            var app = new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown,
            };

            var exitCode = 2;
            app.Startup += (_, _) =>
            {
                _ = app.Dispatcher.BeginInvoke(async () =>
                {
                    try
                    {
                        exitCode = await RunCoreAsync(pluginPath, Log);
                    }
                    catch (Exception ex)
                    {
                        Log("FAIL startup " + ex);
                        exitCode = 9;
                    }
                    finally
                    {
                        try
                        {
                            File.WriteAllLines(logPath, lines, Encoding.UTF8);
                            Log("log=" + logPath);
                        }
                        catch
                        {
                        }

                        app.Shutdown(exitCode);
                    }
                });
            };

            return app.Run();
        }
        catch (Exception ex)
        {
            Log("FAIL " + ex);
            try
            {
                File.WriteAllLines(logPath, lines, Encoding.UTF8);
            }
            catch
            {
            }

            return 9;
        }
    }

    private static async Task<int> RunCoreAsync(string pluginPath, Action<string> log)
    {
        using var engine = new VstInsertEngine();
        engine.Reconfigure(48000, 256, 2);
        var owner = new object();
        var chain = new VstInsertChain();
        chain.Slots[0].PluginPath = pluginPath;
        chain.Slots[0].DisplayName = Path.GetFileNameWithoutExtension(
            Path.TrimEndingDirectorySeparator(pluginPath));

        // 再生中差し替えを模す
        var stopAudio = 0;
        var audioThread = new Thread(() =>
        {
            var buf = new float[512];
            while (Interlocked.CompareExchange(ref stopAudio, 0, 0) == 0)
            {
                Array.Fill(buf, 0.01f);
                try
                {
                    engine.Process(buf, 0, buf.Length, 2);
                }
                catch
                {
                }

                Thread.Sleep(1);
            }
        })
        {
            IsBackground = true,
            Name = "wpf-ui-smoke-audio",
        };
        audioThread.Start();
        log("audio thread started");

        // アプリ差し替え相当: exclusive → unload 空 → load → WPF Show
        var occupied = new VstInsertChain();
        occupied.Slots[0].PluginPath = @"C:\Program Files\Common Files\VST3\Soft Limit.vst3";
        occupied.Slots[0].DisplayName = "Soft Limit";
        log("Apply Soft Limit first...");
        engine.Apply(owner, occupied, playerBypass: false);
        var liveA = engine.TryGetLive(0);
        if (liveA is null)
        {
            log("WARN Soft Limit not created (optional), continuing");
        }
        else
        {
            log("ok Soft Limit live, opening editor while audio runs");
            await Task.Delay(100);
            var sessA = liveA.TryOpenEditor();
            if (sessA is not null)
            {
                var winA = new VstPluginEditorWindow(null, sessA) { Title = "smoke-A" };
                winA.Show();
                await Task.Delay(400);

                log("BeginExclusiveLoad + wait (app path)");
                engine.BeginExclusiveLoad();
                await engine.WaitForProcessUsersAsync(2000);
                try
                {
                    // アプリ差し替えと同じ: Process 停止後、UI STA で removed → Soft Limit 同期 Dispose → Altiverb create
                    log("DetachPluginNow Soft Limit on UI STA...");
                    winA.ResumeAudioOnClose = false;
                    winA.DetachPluginNow();
                    winA.Close();
                    await Task.Delay(30);

                    occupied.ClearSlot(0);
                    log("Unload Soft Limit Apply (sync dispose under exclusive)...");
                    engine.Apply(owner, occupied, playerBypass: false);
                    log("ok Soft Limit disposed on UI STA");
                    await Task.Delay(50);

                    log("Apply Altiverb on same UI STA...");
                    engine.Apply(owner, chain, playerBypass: false);
                }
                finally
                {
                    engine.EndExclusiveLoad();
                }
            }
            else
            {
                log("WARN Soft Limit editor null reason=" + (Vst3ProcessorInstance.LastEditorFailure ?? "?"));
                engine.BeginExclusiveLoad();
                await engine.WaitForProcessUsersAsync(2000);
                try
                {
                    occupied.ClearSlot(0);
                    engine.Apply(owner, occupied, playerBypass: false);
                    engine.Apply(owner, chain, playerBypass: false);
                }
                finally
                {
                    engine.EndExclusiveLoad();
                }
            }
        }

        if (liveA is null)
        {
            engine.BeginExclusiveLoad();
            await engine.WaitForProcessUsersAsync(2000);
            try
            {
                log("Apply target (no Soft Limit)...");
                engine.Apply(owner, chain, playerBypass: false);
            }
            finally
            {
                engine.EndExclusiveLoad();
            }
        }

        Interlocked.Exchange(ref stopAudio, 1);
        if (!audioThread.Join(3000))
        {
            log("WARN audio thread join timeout");
        }
        else
        {
            log("ok audio thread stopped");
        }

        var live = engine.TryGetLive(0);
        if (live is null || live.Faulted)
        {
            log("FAIL live null/faulted create=" + (Vst3ProcessorInstance.LastCreateFailure ?? "?"));
            return 3;
        }

        log("ok live=" + live.DisplayName);
        live.ResetEditor();
        var session = live.TryOpenEditor();
        if (session is null)
        {
            log("FAIL TryOpenEditor reason=" + (Vst3ProcessorInstance.LastEditorFailure ?? "?"));
            return 4;
        }

        log("ok TryOpenEditor size=" + session.GetSize().Width + "x" + session.GetSize().Height);

        VstPluginEditorWindow? window = null;
        try
        {
            window = new VstPluginEditorWindow(null, session)
            {
                Title = "smoke-" + live.DisplayName,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Topmost = true,
            };
            window.Show();
            window.Activate();
            log("Show() called IsVisible=" + window.IsVisible);

            // アタッチ・レイアウト待ち
            for (var i = 0; i < 40; i++)
            {
                await Task.Delay(100);
                await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                if (window.IsVisible && window.ActualWidth > 50 && window.ActualHeight > 50)
                {
                    break;
                }
            }

            var w = window.ActualWidth;
            var h = window.ActualHeight;
            var visible = window.IsVisible;
            log("after-wait visible=" + visible + " actual=" + w.ToString("0") + "x" + h.ToString("0"));

            if (!visible || w < 50 || h < 50)
            {
                log("FAIL window not really shown");
                return 5;
            }

            // さらに 1 秒表示してユーザー／自動確認用に残す
            await Task.Delay(1000);
            log("PASS wpf-ui-smoke title=" + window.Title + " size=" + w.ToString("0") + "x" + h.ToString("0"));
            return 0;
        }
        catch (Exception ex)
        {
            log("FAIL Show/attach " + ex);
            return 6;
        }
        finally
        {
            try
            {
                window?.Close();
            }
            catch
            {
            }
        }
    }
}
