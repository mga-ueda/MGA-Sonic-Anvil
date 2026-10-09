using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MgaSonicAnvil.UI;

/// <summary>
/// F8／引数起動の動画ミニで、枠ドラッグ中もクライアントの縦横比を映像に合わせる（WM_SIZING）。
/// </summary>
internal static class VideoLaunchAspectResize
{
    private const int WmSizing = 0x0214;
    private const int WmszLeft = 1;
    private const int WmszRight = 2;
    private const int WmszTop = 3;
    private const int WmszTopLeft = 4;
    private const int WmszTopRight = 5;
    private const int WmszBottom = 6;
    private const int WmszBottomLeft = 7;
    private const int WmszBottomRight = 8;

    public readonly struct Constraints
    {
        public Constraints(
            double clientAspect,
            int nonClientWidthPx,
            int nonClientHeightPx,
            int minClientWidthPx,
            int minClientHeightPx,
            int maxClientWidthPx,
            int maxClientHeightPx)
        {
            ClientAspect = clientAspect;
            NonClientWidthPx = Math.Max(0, nonClientWidthPx);
            NonClientHeightPx = Math.Max(0, nonClientHeightPx);
            MinClientWidthPx = Math.Max(1, minClientWidthPx);
            MinClientHeightPx = Math.Max(1, minClientHeightPx);
            MaxClientWidthPx = Math.Max(MinClientWidthPx, maxClientWidthPx);
            MaxClientHeightPx = Math.Max(MinClientHeightPx, maxClientHeightPx);
        }

        public double ClientAspect { get; }
        public int NonClientWidthPx { get; }
        public int NonClientHeightPx { get; }
        public int MinClientWidthPx { get; }
        public int MinClientHeightPx { get; }
        public int MaxClientWidthPx { get; }
        public int MaxClientHeightPx { get; }
    }

    public static void Attach(Window window, Func<Constraints?> constraintsProvider)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(constraintsProvider);

        void Hook()
        {
            if (PresentationSource.FromVisual(window) is not HwndSource source)
            {
                return;
            }

            source.AddHook(delegate (IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
            {
                return Filter(constraintsProvider, msg, wParam, lParam, ref handled);
            });
        }

        if (PresentationSource.FromVisual(window) is not null)
        {
            Hook();
        }
        else
        {
            window.SourceInitialized += (_, _) => Hook();
        }
    }

    /// <summary>WM_SIZING の矩形をクライアント縦横比に合わせて書き換える。</summary>
    public static void ConstrainRect(
        ref int left,
        ref int top,
        ref int right,
        ref int bottom,
        int edge,
        Constraints constraints)
    {
        if (constraints.ClientAspect <= 0
            || double.IsNaN(constraints.ClientAspect)
            || double.IsInfinity(constraints.ClientAspect))
        {
            return;
        }

        var outerW = Math.Max(1, right - left);
        var outerH = Math.Max(1, bottom - top);
        var clientW = Math.Max(1, outerW - constraints.NonClientWidthPx);
        var clientH = Math.Max(1, outerH - constraints.NonClientHeightPx);

        var widthDriven = edge is WmszLeft or WmszRight or WmszTopLeft or WmszTopRight
            or WmszBottomLeft or WmszBottomRight;
        // 四隅はドラッグ量の大きい方を優先。
        if (edge is WmszTopLeft or WmszTopRight or WmszBottomLeft or WmszBottomRight)
        {
            var byWidthH = clientW / constraints.ClientAspect;
            var byHeightW = clientH * constraints.ClientAspect;
            widthDriven = Math.Abs(byWidthH - clientH) <= Math.Abs(byHeightW - clientW);
        }
        else if (edge is WmszTop or WmszBottom)
        {
            widthDriven = false;
        }

        if (widthDriven)
        {
            clientW = Math.Clamp(clientW, constraints.MinClientWidthPx, constraints.MaxClientWidthPx);
            clientH = (int)Math.Round(clientW / constraints.ClientAspect);
            clientH = Math.Clamp(clientH, constraints.MinClientHeightPx, constraints.MaxClientHeightPx);
            clientW = (int)Math.Round(clientH * constraints.ClientAspect);
            clientW = Math.Clamp(clientW, constraints.MinClientWidthPx, constraints.MaxClientWidthPx);
        }
        else
        {
            clientH = Math.Clamp(clientH, constraints.MinClientHeightPx, constraints.MaxClientHeightPx);
            clientW = (int)Math.Round(clientH * constraints.ClientAspect);
            clientW = Math.Clamp(clientW, constraints.MinClientWidthPx, constraints.MaxClientWidthPx);
            clientH = (int)Math.Round(clientW / constraints.ClientAspect);
            clientH = Math.Clamp(clientH, constraints.MinClientHeightPx, constraints.MaxClientHeightPx);
        }

        var newOuterW = clientW + constraints.NonClientWidthPx;
        var newOuterH = clientH + constraints.NonClientHeightPx;
        ApplyEdge(ref left, ref top, ref right, ref bottom, edge, newOuterW, newOuterH);
    }

    internal static void ApplyEdge(
        ref int left,
        ref int top,
        ref int right,
        ref int bottom,
        int edge,
        int outerWidth,
        int outerHeight)
    {
        switch (edge)
        {
            case WmszLeft:
            case WmszTopLeft:
            case WmszBottomLeft:
                left = right - outerWidth;
                break;
            default:
                right = left + outerWidth;
                break;
        }

        switch (edge)
        {
            case WmszTop:
            case WmszTopLeft:
            case WmszTopRight:
                top = bottom - outerHeight;
                break;
            default:
                bottom = top + outerHeight;
                break;
        }
    }

    private static IntPtr Filter(
        Func<Constraints?> constraintsProvider,
        int msg,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (msg != WmSizing || lParam == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var constraints = constraintsProvider();
        if (constraints is null)
        {
            return IntPtr.Zero;
        }

        var rect = Marshal.PtrToStructure<NativeRect>(lParam);
        var left = rect.Left;
        var top = rect.Top;
        var right = rect.Right;
        var bottom = rect.Bottom;
        ConstrainRect(
            ref left,
            ref top,
            ref right,
            ref bottom,
            wParam.ToInt32(),
            constraints.Value);
        rect.Left = left;
        rect.Top = top;
        rect.Right = right;
        rect.Bottom = bottom;
        Marshal.StructureToPtr(rect, lParam, fDeleteOld: false);
        handled = true;
        return IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
