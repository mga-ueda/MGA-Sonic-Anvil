using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>
/// WPF 本体のクラス名（HwndWrapper[…;;GUID]）は起動ごとに変わるため、
/// 外部のショートカット起動ソフト向けに、固定クラス名の検出用 HWND を別途持つ。
/// 検出窓が前面化されたら、本窓（MainWindow）へ転送する。
/// </summary>
internal static class ActivationClassBeacon
{
    /// <summary>FindWindow / Spy++ 等で使う固定クラス名（空白なし）。</summary>
    public const string WindowClassName = "MGASonicAnvil";

    private const int ErrorClassAlreadyExists = 1410;
    private const int WmActivate = 0x0006;
    private const int WmSetFocus = 0x0007;
    private const int WmClose = 0x0010;
    private const int WmSysCommand = 0x0112;
    private const int WaInactive = 0;
    private const int ScClose = 0xF060;
    private const int ScRestore = 0xF120;
    private const int SwShowNoActivate = 4;
    private const uint WsPopup = 0x80000000;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExLayered = 0x00080000;
    private const uint WsExTransparent = 0x00000020;
    private const uint LwaAlpha = 0x00000002;

    private static readonly object Gate = new();
    private static WndProc? _wndProc;
    private static IntPtr _hwnd;
    private static bool _registeredClass;
    private static Window? _mainWindow;
    private static bool _redirecting;

    internal static IntPtr Handle
    {
        get
        {
            lock (Gate)
            {
                return _hwnd;
            }
        }
    }

    public static void Start(Window mainWindow)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);
        lock (Gate)
        {
            _mainWindow = mainWindow;
            if (_hwnd != IntPtr.Zero)
            {
                return;
            }

            _wndProc ??= WndProcCore;
            var module = GetModuleHandle(null);
            var wc = new WndClassEx
            {
                Size = (uint)Marshal.SizeOf<WndClassEx>(),
                WndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                Instance = module,
                ClassName = WindowClassName,
            };

            var atom = RegisterClassEx(ref wc);
            if (atom == 0)
            {
                var err = Marshal.GetLastWin32Error();
                if (err != ErrorClassAlreadyExists)
                {
                    return;
                }
            }
            else
            {
                _registeredClass = true;
            }

            // タスクバーに出さず、透過・クリック透過の 1x1 を画面外に置く。
            // IsWindowVisible は true のままなので、EnumWindows 系の検出ソフトでも拾いやすい。
            // WS_EX_NOACTIVATE は付けない。外部ソフトの SetForegroundWindow が
            // WM_ACTIVATE を届けにくくなることがある。
            var hwnd = CreateWindowEx(
                WsExToolWindow | WsExLayered | WsExTransparent,
                WindowClassName,
                AppVersion.ProductName,
                WsPopup,
                -32000,
                -32000,
                1,
                1,
                IntPtr.Zero,
                IntPtr.Zero,
                module,
                IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            SetLayeredWindowAttributes(hwnd, 0, 0, LwaAlpha);
            ShowWindow(hwnd, SwShowNoActivate);
            _hwnd = hwnd;
        }
    }

    public static void Stop()
    {
        lock (Gate)
        {
            var hwnd = _hwnd;
            _hwnd = IntPtr.Zero;
            _mainWindow = null;
            if (hwnd != IntPtr.Zero)
            {
                DestroyWindow(hwnd);
            }

            if (_registeredClass)
            {
                UnregisterClass(WindowClassName, GetModuleHandle(null));
                _registeredClass = false;
            }
        }
    }

    private static IntPtr WndProcCore(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case WmActivate:
                if ((wParam.ToInt32() & 0xFFFF) != WaInactive)
                {
                    RequestActivateMain();
                }

                return IntPtr.Zero;

            case WmSetFocus:
                RequestActivateMain();
                return IntPtr.Zero;

            case WmClose:
                // 外部ソフトの「閉じる」で検出窓だけ消えないようにする。
                return IntPtr.Zero;

            case WmSysCommand:
            {
                var command = wParam.ToInt32() & 0xFFF0;
                if (command == ScClose)
                {
                    return IntPtr.Zero;
                }

                if (command == ScRestore)
                {
                    RequestActivateMain();
                    return IntPtr.Zero;
                }

                break;
            }
        }

        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private static void RequestActivateMain()
    {
        Window? window;
        lock (Gate)
        {
            window = _mainWindow;
            if (window is null || _redirecting)
            {
                return;
            }

            _redirecting = true;
        }

        void Run()
        {
            try
            {
                ActivateMain(window);
            }
            finally
            {
                lock (Gate)
                {
                    _redirecting = false;
                }
            }
        }

        if (window.Dispatcher.CheckAccess())
        {
            Run();
        }
        else
        {
            window.Dispatcher.BeginInvoke((Action)Run, DispatcherPriority.Send);
        }
    }

    private static void ActivateMain(Window window)
    {
        try
        {
            if (window.Opacity < 1 || WindowPaintReveal.IsPending(window))
            {
                WindowPaintReveal.Reveal(window);
            }

            ForegroundActivation.BringToFront(window);
        }
        catch
        {
            // 終了直後など。検出用なので握りつぶす。
        }
    }

    private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public IntPtr WndProc;
        public int ClsExtra;
        public int WndExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr IconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WndClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClass(string className, IntPtr instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(
        IntPtr hwnd,
        uint key,
        byte alpha,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
