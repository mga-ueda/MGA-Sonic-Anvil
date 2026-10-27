using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MgaSonicAnvil.UI;

/// <summary>
/// ウィンドウへすりガラスを適用する（MGA After Drive と同じ ACCENT Acrylic）。
/// 透け具合は <see cref="TintAlpha"/>。DwmExtendFrameIntoClientArea は呼ばないこと。
/// </summary>
internal static class AcrylicBackdrop
{
    /// <summary>ABGR 黒ティントのアルファ。低いほど透ける。</summary>
    internal const byte TintAlpha = 0x40;

    private const int GwlExStyle = -20;
    private const int WsExLayered = 0x00080000;
    private const int WcaAccentPolicy = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmsbtNone = 1;
    private const int DwmwcpRound = 2;

    private enum AccentState
    {
        AccentEnableAcrylicBlurBehind = 4,
    }

    /// <summary>
    /// WPF Window へ Acrylic を適用する。背景ブラシは呼び出し側で Transparent にしておくこと。
    /// </summary>
    public static void Apply(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var source = HwndSource.FromHwnd(hwnd);
        if (source?.CompositionTarget is not null)
        {
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
        }

        Apply(hwnd);
    }

    public static void Apply(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var darkMode = 1;
            _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));

            var backdrop = DwmsbtNone;
            _ = DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int));

            var corner = DwmwcpRound;
            _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corner, sizeof(int));

            ClearLayeredStyle(hwnd);
            ApplyAccent(hwnd, AccentState.AccentEnableAcrylicBlurBehind, TintAlpha);
        }
        catch (Exception ex) when (
            ex is ExternalException
                or InvalidOperationException
                or NotSupportedException)
        {
        }
    }

    private static void ApplyAccent(IntPtr hwnd, AccentState state, byte alpha)
    {
        var policy = new AccentPolicy
        {
            AccentState = state,
            AccentFlags = 0,
            GradientColor = Math.Max((byte)1, alpha) << 24,
            AnimationId = 0,
        };

        var size = Marshal.SizeOf<AccentPolicy>();
        var policyPtr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, policyPtr, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WcaAccentPolicy,
                Data = policyPtr,
                SizeOfData = size,
            };
            _ = SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(policyPtr);
        }
    }

    public static void ClearLayeredStyle(IntPtr hwnd)
    {
        var exStyle = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        if ((exStyle & WsExLayered) == 0)
        {
            return;
        }

        _ = SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(exStyle & ~WsExLayered));
    }

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, nIndex)
            : new IntPtr(GetWindowLong32(hWnd, nIndex));

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong) =>
        IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
            : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(
        IntPtr hwnd,
        ref WindowCompositionAttributeData data);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
