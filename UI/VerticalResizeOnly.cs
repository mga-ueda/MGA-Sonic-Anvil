using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MgaSonicAnvil.UI;

/// <summary>
/// MinWidth と MaxWidth が同じとき、左右と四隅の横リサイズ判定を外す。
/// Windows は幅が固定でも左右で ↔️ を出すため。
/// </summary>
internal static class VerticalResizeOnly
{
    private const int WmNcHitTest = 0x0084;
    internal const int HtBorder = 18;
    internal const int HtLeft = 10;
    internal const int HtRight = 11;
    internal const int HtTop = 12;
    internal const int HtTopLeft = 13;
    internal const int HtTopRight = 14;
    internal const int HtBottom = 15;
    internal const int HtBottomLeft = 16;
    internal const int HtBottomRight = 17;

    public static void LockWidth(Window window)
    {
        void Attach()
        {
            if (PresentationSource.FromVisual(window) is HwndSource source)
            {
                source.AddHook(delegate (IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
                {
                    return Filter(window, hwnd, msg, wParam, lParam, ref handled);
                });
            }
        }

        if (PresentationSource.FromVisual(window) is not null)
        {
            Attach();
        }
        else
        {
            window.SourceInitialized += (_, _) => Attach();
        }
    }

    internal static int RemapWidthLocked(int hit) => hit switch
    {
        HtLeft or HtRight => HtBorder,
        HtTopLeft or HtTopRight => HtTop,
        HtBottomLeft or HtBottomRight => HtBottom,
        _ => hit,
    };

    private static IntPtr Filter(
        Window window,
        IntPtr hwnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (msg != WmNcHitTest || window.MinWidth < window.MaxWidth)
        {
            return IntPtr.Zero;
        }

        var hit = DefWindowProc(hwnd, msg, wParam, lParam).ToInt32();
        handled = true;
        return new IntPtr(RemapWidthLocked(hit));
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
