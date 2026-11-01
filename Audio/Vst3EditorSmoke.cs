using System.Runtime.InteropServices;
using System.Text;

namespace MgaSonicAnvil.Audio;

/// <summary>
/// 実 VST3 の createView / attached をコンソールで診断する。
/// 用法: アプリ.exe --vst-editor-smoke "C:\...\Plugin.vst3" [classIdBase64]
/// </summary>
internal static class Vst3EditorSmoke
{
    public const string CliSwitch = "--vst-editor-smoke";

    public static int Run(string pluginPath, string? classId = null)
    {
        var logPath = Path.Combine(Path.GetTempPath(), "mga-vst-editor-smoke.log");
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
            Log("classId=" + (classId ?? "(null)"));

            if (!File.Exists(pluginPath) && !Directory.Exists(pluginPath))
            {
                Log("FAIL missing path");
                return Finish(2);
            }

            var slot = new VstInsertSlot
            {
                PluginPath = pluginPath,
                ClassId = classId,
                DisplayName = Path.GetFileNameWithoutExtension(Path.TrimEndingDirectorySeparator(pluginPath)),
            };

            if (!Vst3FactoryProbe.TryResolveBinary(pluginPath, out var binary))
            {
                Log("FAIL TryResolveBinary");
                return Finish(3);
            }

            Log("binary=" + binary);
            var module = Vst3Module.TryAcquire(pluginPath);
            if (module is null)
            {
                Log("FAIL Vst3Module.TryAcquire");
                return Finish(3);
            }

            try
            {
                Log("classes=" + module.Classes.Count);
                foreach (var c in module.Classes.Take(12))
                {
                    Log("  class " + c.Name + " | " + c.SubCategories + " | "
                        + Vst3HostNative.ClassIdToBase64(c.Cid));
                }

                if (!module.TryResolveClassId(classId, slot.DisplayName, out var cid, out var info))
                {
                    Log("FAIL TryResolveClassId");
                    return Finish(3);
                }

                Log("resolved " + info.Name + " cid=" + Vst3HostNative.ClassIdToBase64(cid));
            }
            finally
            {
                module.Dispose();
            }

            Vst3ProcessorInstance? instance = null;
            try
            {
                instance = Vst3ProcessorInstance.TryCreate(slot, 48000, 256, 2);
            }
            catch (Exception ex)
            {
                Log("FAIL TryCreate threw " + ex.GetType().Name + ": " + ex.Message);
                return Finish(3);
            }

            if (instance is null)
            {
                Log("FAIL TryCreate returned null reason=" + (Vst3ProcessorInstance.LastCreateFailure ?? "?"));
                return Finish(3);
            }

            Log("ok TryCreate name=" + instance.DisplayName + " cid=" + (instance.ClassId ?? ""));
            if (instance.Faulted)
            {
                Log("FAIL instance Faulted after create");
                instance.Dispose();
                return Finish(4);
            }

            var buffer = new float[512];
            Array.Fill(buffer, 0.02f);
            try
            {
                var processed = instance.Process(buffer, 0, buffer.Length, 2);
                Log(processed ? "ok Process" : "FAIL Process returned false");
            }
            catch (Exception ex)
            {
                Log("FAIL Process threw " + ex.GetType().Name + ": " + ex.Message);
            }

            Vst3EditorSession? session = null;
            try
            {
                session = instance.TryOpenEditor();
            }
            catch (Exception ex)
            {
                Log("FAIL TryOpenEditor threw " + ex.GetType().Name + ": " + ex.Message);
                instance.Dispose();
                return Finish(5);
            }

            if (session is null)
            {
                Log("FAIL TryOpenEditor reason=" + (Vst3ProcessorInstance.LastEditorFailure ?? "?"));
                instance.Dispose();
                return Finish(5);
            }

            Log("ok TryOpenEditor title=" + session.Title);
            var sizeBefore = session.GetSize();
            Log("size-before-attach " + sizeBefore.Width + "x" + sizeBefore.Height);

            var hwnd = CreateMessageWindow(Math.Max(120, sizeBefore.Width), Math.Max(80, sizeBefore.Height));
            if (hwnd == IntPtr.Zero)
            {
                Log("FAIL CreateWindow err=" + Marshal.GetLastWin32Error());
                session.Dispose();
                instance.Dispose();
                return Finish(6);
            }

            try
            {
                session.Attach(hwnd);
                var sizeAfter = session.GetSize();
                Log("ok Attach size-after " + sizeAfter.Width + "x" + sizeAfter.Height);
                session.NotifySize(new Vst3HostNative.ViewRect
                {
                    Left = 0,
                    Top = 0,
                    Right = Math.Max(120, sizeAfter.Width),
                    Bottom = Math.Max(80, sizeAfter.Height),
                });
                Log("PASS editor-smoke");
                return Finish(0);
            }
            catch (Exception ex)
            {
                Log("FAIL Attach threw " + ex.GetType().Name + ": " + ex.Message);
                return Finish(7);
            }
            finally
            {
                try
                {
                    session.Dispose();
                }
                catch
                {
                }

                DestroyWindow(hwnd);
                instance.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log("FAIL unhandled " + ex);
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

    private static IntPtr CreateMessageWindow(int width, int height)
    {
        return CreateWindowEx(
            0,
            "STATIC",
            "vst-smoke",
            WsPopup | WsVisible,
            0,
            0,
            width,
            height,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
    }

    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsVisible = 0x10000000;

    [DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int InitFn(IntPtr self, IntPtr context);
}
