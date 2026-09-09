using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MgaSonicAnvil.UI;

/// <summary>
/// 二重起動や引数起動で、Windows のフォアグラウンド制限下でも既存窓を前面へ出す。
/// </summary>
internal static class ForegroundActivation
{
    private const int SwShow = 5;
    private const uint LsfwUnlock = 2;
    private const uint FlashwAll = 3;
    private const uint FlashwTimerNoFg = 12;

    public static bool TryAllowProcess(int processId)
    {
        try
        {
            return AllowSetForegroundWindow(processId);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryAllowAny() => TryAllowProcess(-1);

    public static void BringToFront(Window window)
    {
        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        if (hwnd == IntPtr.Zero)
        {
            window.Activate();
            return;
        }

        ShowWindow(hwnd, SwShow);
        LockSetForegroundWindow(LsfwUnlock);

        if (!SetForegroundWindow(hwnd))
        {
            TryAttachAndForeground(hwnd);
        }

        window.Activate();
        var keepTop = window.Topmost;
        window.Topmost = true;
        window.Topmost = keepTop;

        if (GetForegroundWindow() != hwnd)
        {
            SetForegroundWindow(hwnd);
            window.Activate();
        }

        if (GetForegroundWindow() != hwnd)
        {
            Flash(hwnd);
        }
    }

    private static void TryAttachAndForeground(IntPtr hwnd)
    {
        var foreground = GetForegroundWindow();
        if (foreground == hwnd || foreground == IntPtr.Zero)
        {
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
            return;
        }

        var currentThread = GetCurrentThreadId();
        var foregroundThread = GetWindowThreadProcessId(foreground, IntPtr.Zero);
        if (foregroundThread == 0 || foregroundThread == currentThread)
        {
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
            return;
        }

        var attached = AttachThreadInput(foregroundThread, currentThread, true);
        try
        {
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(foregroundThread, currentThread, false);
            }
        }
    }

    private static void Flash(IntPtr hwnd)
    {
        var info = new FlashInfo
        {
            Size = (uint)Marshal.SizeOf<FlashInfo>(),
            Hwnd = hwnd,
            Flags = FlashwAll | FlashwTimerNoFg,
            Count = 5,
            Timeout = 0,
        };
        FlashWindowEx(ref info);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Hwnd;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool LockSetForegroundWindow(uint lockCode);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, IntPtr processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, bool attachInput);

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FlashInfo info);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
