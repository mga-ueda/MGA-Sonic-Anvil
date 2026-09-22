using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using MgaSonicAnvil.Config;

namespace MgaSonicAnvil.UI;

/// <summary>ウィンドウ内容とポップアップに表示倍率を載せ、位置・最小サイズを合わせる。</summary>
internal static class UiScaleService
{
    private static readonly ConditionalWeakTable<Window, WindowScaleState> States = new();
    private static Transform _published = Transform.Identity;
    private static bool _started;

    public static event EventHandler? Changed;

    public static double Factor => AppStorage.Settings.ResolvedUiScale();

    public static void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        PublishTransform();
        if (Application.Current is { } app)
        {
            app.Exit += (_, _) => _started = false;
        }
    }

    public static void ApplyFromSettings()
    {
        PublishTransform();
        if (Application.Current is null)
        {
            return;
        }

        foreach (Window window in Application.Current.Windows)
        {
            Attach(window, resize: true);
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void Attach(Window window) => Attach(window, resize: false);

    internal static int ToStoredExtent(double scaled, double scale) =>
        (int)Math.Round(scaled / Math.Max(1e-6, scale));

    internal static double FromStoredExtent(double unscaled, double scale) =>
        unscaled * Math.Max(1e-6, scale);

    internal static double ScaleExtent(double value, double previousFactor, double factor)
    {
        if (previousFactor <= 0 || factor <= 0)
        {
            return value;
        }

        return value * (factor / previousFactor);
    }

    /// <summary>
    /// 表示倍率の基準となる最小幅を差し替え、現在の倍率でウィンドウへ反映する。
    /// F9 ミニマムなどモードで下限が変わるときに使う。
    /// </summary>
    internal static void SetUnscaledMinWidth(Window window, double unscaledMinWidth)
    {
        var state = States.GetOrCreateValue(window);
        state.MinWidth = unscaledMinWidth;
        var factor = state.AppliedFactor > 0 ? state.AppliedFactor : Factor;
        var scaled = ScaleFinite(unscaledMinWidth, factor);
        if (!double.IsNaN(scaled))
        {
            window.MinWidth = scaled;
        }
    }

    /// <summary>表示倍率の基準となる最小高さを差し替え、現在の倍率でウィンドウへ反映する。</summary>
    internal static void SetUnscaledMinHeight(Window window, double unscaledMinHeight)
    {
        var state = States.GetOrCreateValue(window);
        state.MinHeight = unscaledMinHeight;
        var factor = state.AppliedFactor > 0 ? state.AppliedFactor : Factor;
        var scaled = ScaleFinite(unscaledMinHeight, factor);
        if (!double.IsNaN(scaled))
        {
            window.MinHeight = scaled;
        }
    }

    private static void Attach(Window window, bool resize)
    {
        PublishTransform();
        // 表示倍率はメインウィンドウの中身だけに掛ける。
        // メニュー・ツールチップ・設定などの独自ウィンドウは OS の DPI のまま等倍。
        if (window is not MainWindow)
        {
            return;
        }

        var factor = Factor;
        var state = States.GetOrCreateValue(window);
        if (state.AppliedFactor <= 0)
        {
            state.MinWidth = window.MinWidth;
            state.MinHeight = window.MinHeight;
            state.MaxWidth = window.MaxWidth;
            state.MaxHeight = window.MaxHeight;
        }

        if (window.Content is FrameworkElement content)
        {
            content.LayoutTransform = _published;
        }

        if (window.SizeToContent == SizeToContent.Manual)
        {
            var first = state.AppliedFactor <= 0;
            var previous = first ? 1 : state.AppliedFactor;
            if (first || resize)
            {
                ApplyBounds(window, state, previous, factor, resize && !first);
            }
        }

        state.AppliedFactor = factor;
    }

    private static void ApplyBounds(
        Window window,
        WindowScaleState state,
        double previousFactor,
        double factor,
        bool resize)
    {
        var minWidth = ScaleFinite(state.MinWidth, factor);
        var minHeight = ScaleFinite(state.MinHeight, factor);
        if (!double.IsNaN(minWidth))
        {
            window.MinWidth = minWidth;
        }

        if (!double.IsNaN(minHeight))
        {
            window.MinHeight = minHeight;
        }

        var maxWidth = ScaleFinite(state.MaxWidth, factor);
        var maxHeight = ScaleFinite(state.MaxHeight, factor);
        if (!double.IsNaN(maxWidth) && !double.IsPositiveInfinity(state.MaxWidth))
        {
            window.MaxWidth = maxWidth;
        }

        if (!double.IsNaN(maxHeight) && !double.IsPositiveInfinity(state.MaxHeight))
        {
            window.MaxHeight = maxHeight;
        }

        if (resize && window.WindowState == WindowState.Normal
            && window is not MainWindow { IsWaveformMaximized: true })
        {
            window.Width = ScaleExtent(window.Width, previousFactor, factor);
            window.Height = ScaleExtent(window.Height, previousFactor, factor);
            KeepOnWorkArea(window);
        }

        if (window is MainWindow { IsWaveformMaximized: true } main)
        {
            main.RefreshWaveformFullscreenFrame();
        }
    }

    private static void KeepOnWorkArea(Window window)
    {
        var work = SystemParameters.WorkArea;
        if (window.Width > work.Width && window.MinWidth <= work.Width)
        {
            window.Width = work.Width;
        }

        if (window.Height > work.Height && window.MinHeight <= work.Height)
        {
            window.Height = work.Height;
        }

        if (window.Left + window.Width > work.Right)
        {
            window.Left = Math.Max(work.X, work.Right - window.Width);
        }

        if (window.Top + window.Height > work.Bottom)
        {
            window.Top = Math.Max(work.Y, work.Bottom - window.Height);
        }

        if (window.Left < work.Left)
        {
            window.Left = work.Left;
        }

        if (window.Top < work.Top)
        {
            window.Top = work.Top;
        }
    }

    private static double ScaleFinite(double value, double factor)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return value;
        }

        return value * factor;
    }

    /// <summary>
    /// メインウィンドウ内でも等倍で見せたい要素（編集履歴などのフローティング表示）用の逆変換。
    /// ルートの LayoutTransform を打ち消す。
    /// </summary>
    internal static Transform CreateCounterTransform()
    {
        var factor = Factor;
        if (factor <= 1.0001)
        {
            return Transform.Identity;
        }

        var transform = new ScaleTransform(1d / factor, 1d / factor);
        transform.Freeze();
        return transform;
    }

    /// <summary>
    /// 凍結した変換。未凍結の Freezable を複数の LayoutTransform に渡すと起動時に落ちる。
    /// </summary>
    internal static Transform CreatePublishedTransform(double factor)
    {
        if (factor <= 1.0001)
        {
            return Transform.Identity;
        }

        var transform = new ScaleTransform(factor, factor);
        transform.Freeze();
        return transform;
    }

    private static void PublishTransform() =>
        _published = CreatePublishedTransform(Factor);

    private sealed class WindowScaleState
    {
        public double MinWidth;
        public double MinHeight;
        public double MaxWidth;
        public double MaxHeight;
        public double AppliedFactor;
    }
}
