using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MgaSonicAnvil.UI;

/// <summary>DWM Acrylic HWND の角丸。任意半径は取れず、OS の二段階だけ。</summary>
internal enum AcrylicCornerStyle
{
    None,
    RoundSmall,
    Round,
}

/// <summary>
/// ウィンドウへすりガラスを適用する（MGA After Drive と同じ ACCENT Acrylic。
/// DWM 呼び出し方針は WinBlur 由来 — THIRD_PARTY_NOTICES.md の WinBlur 項を参照）。
/// ティントは色設定のブラシ（#AARRGGBB）。ダイアログ／メニューは <c>AcrylicDialogTintBrush</c>、
/// トーストは <c>AcrylicToastTintBrush</c>。DwmExtendFrameIntoClientArea は呼ばないこと。
/// </summary>
internal static class AcrylicBackdrop
{
    internal const string TintBrushKey = "AcrylicDialogTintBrush";

    internal const string ToastTintBrushKey = "AcrylicToastTintBrush";

    /// <summary>ダイアログ用 XAML 既定と同じ黒ティント 0xC0。</summary>
    internal const byte DefaultTintAlpha = 0xC0;

    /// <summary>トースト用 XAML 既定と同じ黒ティント 0x80。</summary>
    internal const byte DefaultToastTintAlpha = 0x80;

    private static readonly object TrackGate = new();
    private static readonly List<WeakReference<Window>> Tracked = [];

    private const int GwlExStyle = -20;
    private const int WsExLayered = 0x00080000;
    private const int WsExTopmost = 0x00000008;
    private const int WcaAccentPolicy = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmsbtNone = 1;
    private const int DwmwcpDonotround = 1;
    private const int DwmwcpRound = 2;
    private const int DwmwcpRoundsmall = 3;
    private static readonly IntPtr HwndNotopmost = new(-2);
    private const uint SwpNomove = 0x0002;
    private const uint SwpNosize = 0x0001;
    private const uint SwpNoactivate = 0x0010;
    private const uint SwpNoownerzorder = 0x0200;

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
            var already = false;
            foreach (var weak in Tracked)
            {
                if (weak.TryGetTarget(out var existing) && ReferenceEquals(existing, window))
                {
                    already = true;
                    break;
                }
            }

            if (!already)
            {
                Tracked.Add(new WeakReference<Window>(window));
            }
        }

        window.Closed -= OnTrackedClosed;
        window.Closed += OnTrackedClosed;
    }

    /// <summary>色設定のライブ更新後に、開いているすりガラス窓とトーストへティントを載せ直す。</summary>
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
            // 閉じ済み窓へ EnsureHandle すると InvalidOperationException になるため、既存 HWND のみ載せ直す。
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                continue;
            }

            Apply(hwnd, TintBrushKey);
        }

        AcrylicGlassPopup.RefreshOpenTints();
    }

    /// <summary>
    /// WPF Window へ Acrylic を適用する。背景ブラシは呼び出し側で Transparent にしておくこと。
    /// </summary>
    public static void Apply(Window window) => Apply(window, TintBrushKey);

    public static void Apply(Window window, string tintBrushKey)
    {
        ArgumentNullException.ThrowIfNull(window);

        IntPtr hwnd;
        try
        {
            hwnd = new WindowInteropHelper(window).EnsureHandle();
        }
        catch (InvalidOperationException)
        {
            // Close 後（または Closing 中）の EnsureHandle は不可。
            return;
        }

        var source = HwndSource.FromHwnd(hwnd);
        if (source?.CompositionTarget is not null)
        {
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
        }

        Apply(hwnd, tintBrushKey);
    }

    /// <summary>ContextMenu / Popup 子など、Visual の HWND へ Acrylic を載せる。</summary>
    public static bool TryApplyToVisual(Visual? visual) =>
        TryApplyToVisual(visual, TintBrushKey, roundCorners: true);

    public static bool TryApplyToVisual(Visual? visual, string tintBrushKey, bool roundCorners) =>
        TryApplyTintToVisual(visual, ResolveTintColor(tintBrushKey), roundCorners);

    /// <summary>任意色ティントで Visual の HWND へ Acrylic を載せる（トーストのαフェード用）。</summary>
    public static bool TryApplyTintToVisual(Visual? visual, Color tint, bool roundCorners) =>
        TryApplyTintToVisual(
            visual,
            tint,
            roundCorners ? AcrylicCornerStyle.Round : AcrylicCornerStyle.None);

    public static bool TryApplyTintToVisual(Visual? visual, Color tint, AcrylicCornerStyle corners)
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

        ApplyTint(source.Handle, tint, corners);
        return true;
    }

    public static void Apply(IntPtr hwnd) => Apply(hwnd, TintBrushKey);

    public static void Apply(IntPtr hwnd, string tintBrushKey) =>
        ApplyTint(hwnd, ResolveTintColor(tintBrushKey), roundCorners: true);

    /// <param name="roundCorners">
    /// false なら DWM 角丸なし。true は通常の Round（トースト等）。
    /// タイムコードは <see cref="ApplyTint(IntPtr, Color, AcrylicCornerStyle)"/> で設定値に近いサイズを選ぶ。
    /// </param>
    public static void ApplyTint(IntPtr hwnd, Color tint, bool roundCorners) =>
        ApplyTint(hwnd, tint, roundCorners ? AcrylicCornerStyle.Round : AcrylicCornerStyle.None);

    /// <summary>
    /// Acrylic の角丸。SetWindowRgn では Acrylic 塗りが切れないので DWM の好みだけを使う。
    /// </summary>
    public static void ApplyTint(IntPtr hwnd, Color tint, AcrylicCornerStyle corners)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var darkMode = TintLuma(tint) < 0.55 ? 1 : 0;
            _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));

            var backdrop = DwmsbtNone;
            _ = DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int));

            // 以前のリージョン切り抜きが残ると DWM 角丸が効かないことがある。
            _ = SetWindowRgn(hwnd, IntPtr.Zero, true);

            var corner = corners switch
            {
                AcrylicCornerStyle.Round => DwmwcpRound,
                AcrylicCornerStyle.RoundSmall => DwmwcpRoundsmall,
                _ => DwmwcpDonotround,
            };
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

    /// <summary>
    /// 設定の角丸（dip）を DWM の二段階に落とす。
    /// Acrylic の塗りは任意半径にできないため、設定値そのものではなく区分だけ反映する。
    /// 0＝角なし、〜6＝Small（既定 5）、7以上＝Round。
    /// </summary>
    public static AcrylicCornerStyle CornerStyleForControlRadius(double radiusDip)
    {
        if (double.IsNaN(radiusDip) || radiusDip <= 0)
        {
            return AcrylicCornerStyle.None;
        }

        return radiusDip <= 6 ? AcrylicCornerStyle.RoundSmall : AcrylicCornerStyle.Round;
    }

    /// <summary>
    /// DWM 角に合わせた Border 用の目安半径（dip）。塗りと枠の食い違いを抑える。
    /// </summary>
    public static double BorderRadiusForCornerStyle(AcrylicCornerStyle style) =>
        style switch
        {
            AcrylicCornerStyle.RoundSmall => 4,
            AcrylicCornerStyle.Round => 8,
            _ => 0,
        };

    internal static Color ResolveTintColor() => ResolveTintColor(TintBrushKey);

    internal static Color ResolveTintColor(string tintBrushKey)
    {
        try
        {
            return Theme.Get(string.IsNullOrEmpty(tintBrushKey) ? TintBrushKey : tintBrushKey);
        }
        catch (InvalidOperationException)
        {
            var alpha = string.Equals(tintBrushKey, ToastTintBrushKey, StringComparison.Ordinal)
                ? DefaultToastTintAlpha
                : DefaultTintAlpha;
            return Color.FromArgb(alpha, 0, 0, 0);
        }
    }

    /// <summary>
    /// ACCENT GradientColor 用 ABGR。
    /// Acrylic はアルファ 0 と 255 をティント色として無視するため、1 と 254 に丸める。
    /// </summary>
    internal static int ToAccentGradient(Color tint)
    {
        var alpha = tint.A;
        if (alpha == 0)
        {
            alpha = 1;
        }
        else if (alpha == 255)
        {
            alpha = 254;
        }

        return alpha << 24
            | tint.B << 16
            | tint.G << 8
            | tint.R;
    }

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

    /// <summary>
    /// WPF Popup 既定の WS_EX_TOPMOST を外し、他アプリの手前に残らないようにする。
    /// オーナー窓の上には所有関係で留まる。
    /// </summary>
    public static void ClearPopupTopmost(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var exStyle = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
            if ((exStyle & WsExTopmost) != 0)
            {
                _ = SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(exStyle & ~WsExTopmost));
            }

            _ = SetWindowPos(
                hwnd,
                HwndNotopmost,
                0,
                0,
                0,
                0,
                SwpNomove | SwpNosize | SwpNoactivate | SwpNoownerzorder);
        }
        catch (Exception ex) when (
            ex is ExternalException
                or InvalidOperationException
                or NotSupportedException)
        {
        }
    }


    private static void OnTrackedClosed(object? sender, EventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        window.Closed -= OnTrackedClosed;
        lock (TrackGate)
        {
            for (var i = Tracked.Count - 1; i >= 0; i--)
            {
                if (!Tracked[i].TryGetTarget(out var existing) || ReferenceEquals(existing, window))
                {
                    Tracked.RemoveAt(i);
                }
            }
        }
    }

    private static void PruneTracked()
    {
        for (var i = Tracked.Count - 1; i >= 0; i--)
        {
            // 未 Show の窓は Handle==0 / IsLoaded==false なので、閉じ判定は Closed で外す。
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);
}
