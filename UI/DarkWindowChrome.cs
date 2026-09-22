using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

internal static class DarkWindowChrome
{
    private const int DwmwaTransitionsForcedisabled = 3;
    private const int DwmwaCloak = 13;
    private const int DwmwaUseImmersiveDarkModeBefore20 = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;
    private const int WmEraseBkgnd = 0x0014;

    public static void ApplyImmersiveDarkTitleBar(Window window)
    {
        UiScaleService.Attach(window);
        WindowIconHelper.Apply(window);

        void Apply()
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var useDarkMode = UiThemeService.Painted == UiTheme.Dark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref useDarkMode, sizeof(int)) != 0)
            {
                _ = DwmSetWindowAttribute(
                    hwnd,
                    DwmwaUseImmersiveDarkModeBefore20,
                    ref useDarkMode,
                    sizeof(int));
            }

            var caption = ToColorRef(Theme.Get("SurfaceBackBrush"));
            var border = ToColorRef(Theme.Get("ChromeBorderBrush"));
            var text = ToColorRef(Theme.Get("PrimaryForeBrush"));
            _ = DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref caption, sizeof(int));
            _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref border, sizeof(int));
            _ = DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref text, sizeof(int));
        }

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            Apply();
        }
        else
        {
            window.SourceInitialized += (_, _) => Apply();
        }
    }

    public static void DisableShowTransitions(Window window) =>
        SetBool(window, DwmwaTransitionsForcedisabled, true);

    public static bool TrySetCloaked(Window window, bool cloaked) =>
        SetBool(window, DwmwaCloak, cloaked);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window, object> EraseHooked = new();

    public static void SuppressEraseBackground(Window window)
    {
        if (EraseHooked.TryGetValue(window, out _))
        {
            return;
        }

        EraseHooked.Add(window, true);

        void Attach()
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero || HwndSource.FromHwnd(hwnd) is not { } source)
            {
                return;
            }

            source.AddHook(delegate (IntPtr hookHwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
            {
                return FilterErase(window, hookHwnd, msg, wParam, ref handled);
            });
        }

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            Attach();
        }
        else
        {
            window.SourceInitialized += (_, _) => Attach();
        }
    }

    private static bool SetBool(Window window, int attribute, bool value)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var raw = value ? 1 : 0;
        return DwmSetWindowAttribute(hwnd, attribute, ref raw, sizeof(int)) == 0;
    }

    private static IntPtr FilterErase(
        Window window,
        IntPtr hwnd,
        int msg,
        IntPtr hdc,
        ref bool handled)
    {
        if (msg != WmEraseBkgnd || hdc == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        if (window.Background is SolidColorBrush solid && GetClientRect(hwnd, out var rect))
        {
            var brush = CreateSolidBrush(ToColorRef(solid.Color));
            if (brush != IntPtr.Zero)
            {
                _ = FillRect(hdc, ref rect, brush);
                _ = DeleteObject(brush);
            }
        }

        handled = true;
        return new IntPtr(1);
    }

    private static int ToColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attr,
        ref int attrValue,
        int attrSize);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int FillRect(IntPtr hdc, ref NativeRect rect, IntPtr brush);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern IntPtr CreateSolidBrush(int color);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern bool DeleteObject(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
