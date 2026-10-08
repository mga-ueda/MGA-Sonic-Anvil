using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace MgaSonicAnvil.UI;

/// <summary>
/// ウィンドウを描画が終わるまで DWM で隠し、白い未描画フレームを出さない。
/// </summary>
internal static class WindowPaintReveal
{
    private static readonly ConditionalWeakTable<Window, State> States = new();

    private sealed class State
    {
        public bool Pending = true;
        public bool Attached;
        public bool ActivateOnReveal;
        public bool AutoReveal = true;
        public bool PaintNotified;
        public bool PaintCallbackInvoked;
        public Action? OnPainted;
    }

    public static bool IsPending(Window window) =>
        States.TryGetValue(window, out var state) && state.Pending;

    /// <param name="autoReveal">
    /// false のとき描画準備後もクロークしたまま <paramref name="onPainted"/> だけ呼ぶ。
    /// 呼び出し側が <see cref="Reveal"/> するまで画面に出さない。
    /// </param>
    public static void Attach(
        Window window,
        Action? onPainted = null,
        bool activateOnReveal = false,
        bool autoReveal = true)
    {
        ArgumentNullException.ThrowIfNull(window);
        var state = States.GetValue(window, _ => new State());
        if (onPainted is not null)
        {
            state.OnPainted = onPainted;
        }

        state.ActivateOnReveal = activateOnReveal;
        state.AutoReveal = autoReveal;
        if (state.Attached)
        {
            return;
        }

        state.Attached = true;
        window.SourceInitialized += (_, _) => Prepare(window);
        window.Loaded += OnLoaded;
        window.ContentRendered += OnContentRendered;
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            Prepare(window);
        }
    }

    /// <summary>Show / ShowDialog の直前。ハンドルを作り、表示前に隠す。</summary>
    public static void PrepareBeforeShow(Window window)
    {
        Attach(window);
        _ = new WindowInteropHelper(window).EnsureHandle();
        Prepare(window);
    }

    public static void ShowWhenPainted(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.IsVisible)
        {
            window.Show();
            return;
        }

        PrepareBeforeShow(window);
        window.Show();
    }

    public static bool? ShowDialogWhenPainted(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        PrepareBeforeShow(window);
        return window.ShowDialog();
    }

    public static void Prepare(Window window)
    {
        DarkWindowChrome.DisableShowTransitions(window);
        DarkWindowChrome.SuppressEraseBackground(window);
        DarkWindowChrome.TrySetCloaked(window, true);
        DarkWindowChrome.ApplyImmersiveDarkTitleBar(window);
    }

    public static void Reveal(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!States.TryGetValue(window, out var state) || !state.Pending)
        {
            if (window.Opacity < 1)
            {
                window.Opacity = 1;
            }

            return;
        }

        state.Pending = false;
        if (window.Opacity < 1)
        {
            window.Opacity = 1;
        }

        DarkWindowChrome.TrySetCloaked(window, false);
        if (state.ActivateOnReveal)
        {
            window.Activate();
        }

        InvokePainted(state);
    }

    private static void NotifyPainted(Window window)
    {
        if (!States.TryGetValue(window, out var state) || state.PaintNotified)
        {
            return;
        }

        state.PaintNotified = true;
        if (state.AutoReveal)
        {
            Reveal(window);
            return;
        }

        InvokePainted(state);
    }

    private static void InvokePainted(State state)
    {
        if (state.PaintCallbackInvoked)
        {
            return;
        }

        state.PaintCallbackInvoked = true;
        state.OnPainted?.Invoke();
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var window = (Window)sender;
        window.Loaded -= OnLoaded;
        window.UpdateLayout();
        window.Dispatcher.BeginInvoke(
            () => window.Dispatcher.BeginInvoke(
                () => NotifyPainted(window),
                DispatcherPriority.ContextIdle),
            DispatcherPriority.Render);
    }

    private static void OnContentRendered(object? sender, EventArgs e)
    {
        var window = (Window)sender!;
        window.ContentRendered -= OnContentRendered;
        window.UpdateLayout();
        window.Dispatcher.BeginInvoke(
            () => NotifyPainted(window),
            DispatcherPriority.ContextIdle);
    }
}
