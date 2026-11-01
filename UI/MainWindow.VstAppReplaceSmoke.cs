using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;

namespace MgaSonicAnvil.UI;

/// <summary>
/// 実 MainWindow でユーザー手順を自動実行する。
/// mode: replace = Soft→差し替え / clear-then = Soft→削除→Altiverb / altiverb-only = Altiverb のみ
/// </summary>
public partial class MainWindow
{
    internal static VstAppReplaceSmokeRequest? PendingVstAppReplaceSmoke;

    /// <summary>スモーク中は MessageBox で UI を止めない。</summary>
    internal static bool VstAppReplaceSmokeActive;

    internal sealed record VstAppReplaceSmokeRequest(
        string PluginA,
        string PluginB,
        string AudioPath,
        string Mode);

    private void MaybeStartVstAppReplaceSmoke()
    {
        if (PendingVstAppReplaceSmoke is null)
        {
            return;
        }

        ContentRendered += VstAppReplaceSmoke_ContentRendered;
    }

    private async void VstAppReplaceSmoke_ContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= VstAppReplaceSmoke_ContentRendered;
        var req = PendingVstAppReplaceSmoke;
        PendingVstAppReplaceSmoke = null;
        if (req is null)
        {
            return;
        }

        var logPath = Path.Combine(Path.GetTempPath(), "mga-vst-app-replace-smoke.log");
        var lines = new List<string>();
        void Log(string line)
        {
            lines.Add(line);
            try
            {
                File.AppendAllText(logPath, line + Environment.NewLine, Encoding.UTF8);
                var bytes = Encoding.UTF8.GetBytes(line + Environment.NewLine);
                using var stdout = Console.OpenStandardOutput();
                stdout.Write(bytes, 0, bytes.Length);
                stdout.Flush();
            }
            catch
            {
            }
        }

        var exit = 2;
        var hangTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(90) };
        hangTimer.Tick += (_, _) =>
        {
            hangTimer.Stop();
            Log("FAIL HANG-WATCHDOG 90s");
            try
            {
                File.WriteAllLines(logPath, lines, Encoding.UTF8);
            }
            catch
            {
            }

            Application.Current?.Shutdown(8);
        };
        hangTimer.Start();

        try
        {
            Log("=== vst-app-smoke mode=" + req.Mode + " ===");
            Log("exe=" + (Environment.ProcessPath ?? "?"));
            Log("exeWrite=" + File.GetLastWriteTime(Environment.ProcessPath ?? "").ToString("o"));
            Log("A=" + req.PluginA);
            Log("B=" + req.PluginB);
            Log("audio=" + req.AudioPath);
            Log("time=" + DateTimeOffset.Now.ToString("o"));
            Log("debugger managed=" + System.Diagnostics.Debugger.IsAttached
                + " native=" + IsDebuggerPresent()
                + " likely=" + Vst3DebuggerLoadGuard.IsDebuggerPresentLikely);

            VstAppReplaceSmokeActive = true;

            if (IsLibraryMaximized)
            {
                try
                {
                    SetWaveformMaximizeMode(WaveformMaximizeMode.Off);
                }
                catch (Exception ex)
                {
                    Log("WARN maximize leave: " + ex.Message);
                }
            }

            for (var i = 0; i < 30 && _activeSession is null; i++)
            {
                await Task.Delay(100);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            }

            if (_activeSession is null)
            {
                Log("OpenPath audio...");
                OpenPath(req.AudioPath);
                for (var i = 0; i < 80 && _activeSession is null; i++)
                {
                    await Task.Delay(100);
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                }
            }

            if (_activeSession is null)
            {
                Log("FAIL no active session");
                exit = 3;
                return;
            }

            if (IsLibraryMaximized)
            {
                SetWaveformMaximizeMode(WaveformMaximizeMode.Off);
                await Task.Delay(200);
            }

            Log("ok session=" + _activeSession.DisplayName + " vstUi=" + IsVstInsertUiEnabled());
            SyncVstInsertRackUi();
            await Task.Delay(300);

            var mode = req.Mode;
            if (string.Equals(mode, "altiverb-only", StringComparison.OrdinalIgnoreCase))
            {
                exit = await RunAltiverbOnlyAsync(req, Log) ? 0 : 6;
                return;
            }

            if (string.Equals(mode, "clear-then", StringComparison.OrdinalIgnoreCase))
            {
                exit = await RunClearThenAltiverbAsync(req, Log) ? 0 : 6;
                return;
            }

            // default: replace
            exit = await RunReplaceAsync(req, Log) ? 0 : 6;
        }
        catch (Exception ex)
        {
            Log("FAIL exception " + ex);
            exit = 9;
        }
        finally
        {
            hangTimer.Stop();
            VstAppReplaceSmokeActive = false;
            try
            {
                File.WriteAllLines(logPath, lines, Encoding.UTF8);
                Log("log=" + logPath);
            }
            catch
            {
            }

            await Task.Delay(400);
            Application.Current?.Shutdown(exit);
        }
    }

    /// <summary>ユーザー手順: Soft Limit 挿入 → 削除 → Altiverb 挿入（再生なし）。</summary>
    private async Task<bool> RunClearThenAltiverbAsync(VstAppReplaceSmokeRequest req, Action<string> log)
    {
        var entryA = PickerEntryFor(req.PluginA, "Fx");
        log("1) Assign Soft Limit...");
        if (!await AssignWithTimeoutAsync(0, entryA, 30000, log))
        {
            return false;
        }

        log("after Soft live=" + (_player.VstInserts.TryGetLive(0)?.DisplayName ?? "null"));
        _ = await WaitForVstEditorAsync("Soft", 15000, log);

        log("2) Clear Soft Limit (same as UI remove)...");
        _activeSession!.VstInserts.ClearSlot(0);
        VstInsertRack.Refresh();
        var clearTask = SyncVstInsertEngineAsync();
        if (!ReferenceEquals(await Task.WhenAny(clearTask, Task.Delay(30000)), clearTask))
        {
            log("FAIL Clear/Sync timed out 30s (HANG)");
            return false;
        }

        await clearTask;
        log("after clear live=" + (_player.VstInserts.TryGetLive(0)?.DisplayName ?? "null"));
        await Task.Delay(500);

        var entryB = PickerEntryFor(req.PluginB, "Fx|Reverb");
        log("3) Assign Altiverb on empty slot...");
        if (!await AssignWithTimeoutAsync(0, entryB, 45000, log))
        {
            return false;
        }

        var live = _player.VstInserts.TryGetLive(0);
        log("after Altiverb live=" + (live?.DisplayName ?? "null")
            + " createFail=" + (Vst3ProcessorInstance.LastCreateFailure ?? "-"));

        if (live is null)
        {
            log("FAIL Altiverb not loaded");
            return false;
        }

        if (!await WaitForVstEditorAsync("Altiverb", 20000, log))
        {
            log("FAIL Altiverb editor not shown");
            return false;
        }

        log("PASS clear-then Soft delete -> Altiverb with UI");
        return true;
    }

    private async Task<bool> RunAltiverbOnlyAsync(VstAppReplaceSmokeRequest req, Action<string> log)
    {
        var entryB = PickerEntryFor(req.PluginB, "Fx|Reverb");
        log("Assign Altiverb only...");
        if (!await AssignWithTimeoutAsync(0, entryB, 45000, log))
        {
            return false;
        }

        var live = _player.VstInserts.TryGetLive(0);
        log("after Altiverb live=" + (live?.DisplayName ?? "null")
            + " createFail=" + (Vst3ProcessorInstance.LastCreateFailure ?? "-"));
        if (live is null)
        {
            log("FAIL Altiverb not loaded");
            return false;
        }

        if (!await WaitForVstEditorAsync("Altiverb", 20000, log))
        {
            log("FAIL Altiverb editor not shown");
            return false;
        }

        log("PASS altiverb-only with UI");
        return true;
    }

    private async Task<bool> RunReplaceAsync(VstAppReplaceSmokeRequest req, Action<string> log)
    {
        var entryA = PickerEntryFor(req.PluginA, "Fx");
        log("Assign Soft Limit...");
        if (!await AssignWithTimeoutAsync(0, entryA, 30000, log))
        {
            return false;
        }

        if (!await WaitForVstEditorAsync("Soft", 20000, log))
        {
            log("FAIL Soft Limit editor not shown");
            return false;
        }

        await Task.Delay(1000);
        var entryB = PickerEntryFor(req.PluginB, "Fx|Reverb");
        log("Assign Altiverb replace...");
        if (!await AssignWithTimeoutAsync(0, entryB, 45000, log))
        {
            return false;
        }

        var live = _player.VstInserts.TryGetLive(0);
        log("after replace live=" + (live?.DisplayName ?? "null"));
        if (live is null)
        {
            return false;
        }

        if (!await WaitForVstEditorAsync("Altiverb", 20000, log))
        {
            return false;
        }

        log("PASS Soft Limit -> Altiverb replace with UI");
        return true;
    }

    private async Task<bool> AssignWithTimeoutAsync(
        int slot,
        VstPickerEntry entry,
        int timeoutMs,
        Action<string> log)
    {
        var task = AssignVstPluginAsync(slot, entry);
        if (!ReferenceEquals(await Task.WhenAny(task, Task.Delay(timeoutMs)), task))
        {
            log("FAIL Assign " + entry.Name + " timed out " + timeoutMs + "ms (HANG)");
            return false;
        }

        await task;
        return true;
    }

    [DllImport("kernel32")]
    private static extern bool IsDebuggerPresent();

    private static VstPickerEntry PickerEntryFor(string pluginPath, string category)
    {
        try
        {
            var hit = Vst3PluginList.Load().Plugins.FirstOrDefault(p =>
                p.IsRecognized && string.Equals(p.Path, pluginPath, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
            {
                return VstPickerEntry.From(hit);
            }
        }
        catch
        {
        }

        return new VstPickerEntry(
            Path.GetFileNameWithoutExtension(Path.TrimEndingDirectorySeparator(pluginPath)),
            pluginPath,
            Vendor: "",
            Category: category,
            ClassId: null);
    }

    private async Task<bool> WaitForVstEditorAsync(string titleContains, int timeoutMs, Action<string> log)
    {
        var start = Environment.TickCount64;
        while (Environment.TickCount64 - start < timeoutMs)
        {
            foreach (var w in _vstEditors.Values)
            {
                if (w.IsVisible
                    && w.Title.Contains(titleContains, StringComparison.OrdinalIgnoreCase)
                    && w.ActualWidth > 40
                    && w.ActualHeight > 40)
                {
                    log("found editor title=" + w.Title + " size=" + w.ActualWidth.ToString("0") + "x" + w.ActualHeight.ToString("0"));
                    return true;
                }
            }

            await Task.Delay(100);
            await Dispatcher.Yield(DispatcherPriority.Background);
        }

        return false;
    }
}
