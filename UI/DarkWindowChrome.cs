using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

internal static class DarkWindowChrome
{
    private const int DwmwaUseImmersiveDarkModeBefore20 = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    public static void ApplyImmersiveDarkTitleBar(Window window)
    {
        WindowIconHelper.Apply(window);
        if (window is MainWindow)
        {
            window.Title = AppVersion.FormTitle;
        }

        void Apply()
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            var useDarkMode = 1;
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

    private static int ToColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attr,
        ref int attrValue,
        int attrSize);
}
