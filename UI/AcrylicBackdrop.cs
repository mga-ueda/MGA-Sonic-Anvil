using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MgaSonicAnvil.UI;

/// <summary>
/// ウィンドウへすりガラスを適用する（MGA After Drive と同じ ACCENT Acrylic。
/// DWM 呼び出し方針は WinBlur 由来 — THIRD_PARTY_NOTICES.md の WinBlur 項を参照）。
/// ティントは色設定の <c>AcrylicDialogTintBrush</c>（#AARRGGBB）。DwmExtendFrameIntoClientArea は呼ばないこと。
/// </summary>
internal static class AcrylicBackdrop
{
    internal const string TintBrushKey = "AcrylicDialogTintBrush";

    /// <summary>XAML 既定と同じ黒ティント 0xC0。</summary>
    internal const byte DefaultTintAlpha = 0xC0;

    private static readonly object TrackGate = new();
    private static readonly List<WeakReference<Window>> Tracked = [];

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

    public static void Track(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        lock (TrackGate)
        {
            PruneTracked();
            foreach (var weak in Tracked)
            {
                if (weak.TryGetTarget(out var existing) && ReferenceEquals(existing, window))
                {
                    return;
                }
            }

            Tracked.Add(new WeakReference<Window>(window));
        }
    }

    /// <summary>色設定のライブ更新後に、開いているすりガラス窓へティントを載せ直す。</summary>
    public static void RefreshTracked()
    {
        Window[] live;
        lock (TrackGate)
        {
            PruneTracked();
            live = Tracked
                .Select(weak => weak.TryGetTarget(out var window) ? window : null)
                .Where(window => window is not null)
                .Cast<Window>()
                .ToArray();
        }

        foreach (var window in live)
        {
            if (window.IsLoaded)
            {
                Apply(window);
            }
        }
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

    /// <summary>ContextMenu / Popup 子など、Visual の HWND へ Acrylic を載せる。</summary>
    public static bool TryApplyToVisual(Visual? visual)
    {
        if (visual is null)
        {
            return false;
        }

        if (PresentationSource.FromVisual(visual) is not HwndSource source
            || source.Handle == IntPtr.Zero)
        {
            return false;
        }

        if (source.CompositionTarget is not null)
        {
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
        }

        Apply(source.Handle);
        return true;
    }

    public static void Apply(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var tint = ResolveTintColor();
            var darkMode = TintLuma(tint) < 0.55 ? 1 : 0;
            _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));

            var backdrop = DwmsbtNone;
            _ = DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int));

            var corner = DwmwcpRound;
            _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corner, sizeof(int));

            ClearLayeredStyle(hwnd);
            ApplyAccent(hwnd, AccentState.AccentEnableAcrylicBlurBehind, tint);
        }
        catch (Exception ex) when (
            ex is ExternalException
                or InvalidOperationException
                or NotSupportedException)
        {
        }
    }

    internal static Color ResolveTintColor()
    {
        try
        {
            return Theme.Get(TintBrushKey);
        }
        catch (InvalidOperationException)
        {
            return Color.FromArgb(DefaultTintAlpha, 0, 0, 0);
        }
    }

    /// <summary>ACCENT GradientColor 用 ABGR。</summary>
    internal static int ToAccentGradient(Color tint) =>
        Math.Max((byte)1, tint.A) << 24
        | tint.B << 16
        | tint.G << 8
        | tint.R;

    private static double TintLuma(Color tint) =>
        (0.2126 * tint.R + 0.7152 * tint.G + 0.0722 * tint.B) / 255d;

    private static void ApplyAccent(IntPtr hwnd, AccentState state, Color tint)
    {
        var policy = new AccentPolicy
        {
            AccentState = state,
            AccentFlags = 0,
            GradientColor = ToAccentGradient(tint),
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

    private static void PruneTracked()
    {
        for (var i = Tracked.Count - 1; i >= 0; i--)
        {
            if (!Tracked[i].TryGetTarget(out _))
            {
                Tracked.RemoveAt(i);
            }
        }
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
