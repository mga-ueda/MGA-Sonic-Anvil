using System.Text;

namespace MgaSonicAnvil.Audio;

/// <summary>
/// 差し替え＋UI オープンをアプリと同じ二段階で診断。
/// 用法: アプリ.exe --vst-replace-smoke "A.vst3" "B.vst3"
/// </summary>
internal static class Vst3ReplaceSmoke
{
    public const string CliSwitch = "--vst-replace-smoke";

    public static int Run(string pathA, string pathB)
    {
        var logPath = Path.Combine(Path.GetTempPath(), "mga-vst-replace-smoke.log");
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
            Log("A=" + pathA);
            Log("B=" + pathB);
            using var engine = new VstInsertEngine();
            engine.Reconfigure(48000, 256, 2);
            var owner = new object();
            var chain = new VstInsertChain();

            // --- アプリと同じ: ExclusiveLoad →（差し替えなら unload）→ load → TryOpenEditor ---
            engine.BeginExclusiveLoad();
            engine.WaitForProcessUsersAsync(2000).GetAwaiter().GetResult();
            try
            {
                chain.Slots[0].PluginPath = pathA;
                chain.Slots[0].DisplayName = NameOf(pathA);
                Log("Apply A...");
                engine.Apply(owner, chain, playerBypass: false);
                var liveA = engine.TryGetLive(0);
                if (liveA is null)
                {
                    Log("FAIL liveA null create=" + (Vst3ProcessorInstance.LastCreateFailure ?? "?"));
                    return Finish(2);
                }

                if (liveA.Faulted)
                {
                    Log("FAIL liveA Faulted create=" + (Vst3ProcessorInstance.LastCreateFailure ?? "?"));
                    return Finish(2);
                }

                Log("ok Apply A name=" + liveA.DisplayName);

                var stop = 0;
                var processThread = new Thread(() =>
                {
                    var buf = new float[512];
                    while (Interlocked.CompareExchange(ref stop, 0, 0) == 0)
                    {
                        Array.Fill(buf, 0.01f);
                        engine.Process(buf, 0, buf.Length, 2);
                        Thread.Sleep(1);
                    }
                })
                {
                    IsBackground = true,
                };
                // ExclusiveLoad 中は Process は no-op。再開後に回す。
                Log("OpenEditor A (during exclusive)...");
                liveA.ResetEditor();
                var sessionA = TryOpenWithRetry(liveA, Log);
                if (sessionA is null)
                {
                    Log("FAIL OpenEditor A reason=" + (Vst3ProcessorInstance.LastEditorFailure ?? "?"));
                    return Finish(3);
                }

                Log("ok OpenEditor A — detach like DetachAndCloseVstEditor");
                sessionA.Dispose();
                liveA.ResetEditor();

                // 第1段 unload
                Log("Unload A...");
                chain.ClearSlot(0);
                engine.Apply(owner, chain, playerBypass: false);
                if (engine.TryGetLive(0) is not null)
                {
                    Log("FAIL live still present after unload");
                    return Finish(4);
                }

                Log("ok Unload A");
                Thread.Sleep(120);

                // 第2段 load B
                chain.Slots[0].PluginPath = pathB;
                chain.Slots[0].DisplayName = NameOf(pathB);
                chain.Slots[0].ClassId = null;
                chain.Slots[0].StateBlob = null;
                chain.Slots[0].Bypassed = false;
                chain.Slots[0].Faulted = false;
                Log("Apply B...");
                engine.Apply(owner, chain, playerBypass: false);
                var liveB = engine.TryGetLive(0);
                if (liveB is null)
                {
                    Log("FAIL liveB null create=" + (Vst3ProcessorInstance.LastCreateFailure ?? "?"));
                    return Finish(5);
                }

                if (liveB.Faulted)
                {
                    Log("FAIL liveB Faulted create=" + (Vst3ProcessorInstance.LastCreateFailure ?? "?"));
                    return Finish(5);
                }

                Log("ok Apply B name=" + liveB.DisplayName);

                Log("OpenEditor B (during exclusive, app path)...");
                liveB.ResetEditor();
                var sessionB = TryOpenWithRetry(liveB, Log);
                if (sessionB is null)
                {
                    Log("FAIL OpenEditor B reason=" + (Vst3ProcessorInstance.LastEditorFailure ?? "?"));
                    return Finish(6);
                }

                Log("ok OpenEditor B title=" + sessionB.Title);
                var size = sessionB.GetSize();
                Log("size " + size.Width + "x" + size.Height);
                sessionB.Dispose();
                Interlocked.Exchange(ref stop, 1);
            }
            finally
            {
                engine.EndExclusiveLoad();
                engine.SetSlotSuspended(0, false);
            }

            // 再開後も Process / 再オープン
            var buf = new float[512];
            Array.Fill(buf, 0.01f);
            engine.Process(buf, 0, buf.Length, 2);
            Log("ok Process after EndExclusiveLoad");

            var liveAgain = engine.TryGetLive(0);
            if (liveAgain is null)
            {
                Log("FAIL live gone after exclusive end");
                return Finish(7);
            }

            Log("Re-open editor after exclusive end...");
            liveAgain.ResetEditor();
            var sessionC = TryOpenWithRetry(liveAgain, Log);
            if (sessionC is null)
            {
                Log("FAIL re-open reason=" + (Vst3ProcessorInstance.LastEditorFailure ?? "?"));
                return Finish(8);
            }

            Log("ok re-open title=" + sessionC.Title);
            sessionC.Dispose();
            Log("PASS replace-smoke");
            return Finish(0);
        }
        catch (Exception ex)
        {
            Log("FAIL exception " + ex);
            return Finish(9);
        }

        int Finish(int code)
        {
            try
            {
                File.WriteAllLines(logPath, lines, Encoding.UTF8);
                Log("log=" + logPath);
            }
            catch
            {
            }

            return code;
        }
    }

    private static Vst3EditorSession? TryOpenWithRetry(IVstSlotProcessor live, Action<string> log)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0)
            {
                Thread.Sleep(40);
                live.ResetEditor();
            }

            try
            {
                var session = live.TryOpenEditor();
                if (session is not null)
                {
                    log("  attempt " + attempt + " ok");
                    return session;
                }

                log("  attempt " + attempt + " null reason=" + (Vst3ProcessorInstance.LastEditorFailure ?? "?"));
            }
            catch (Exception ex)
            {
                log("  attempt " + attempt + " threw " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        return null;
    }

    private static string NameOf(string path) =>
        Path.GetFileNameWithoutExtension(Path.TrimEndingDirectorySeparator(path));
}
