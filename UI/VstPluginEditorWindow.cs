using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.UI;

/// <summary>
/// VST3 IPlugView ホスト。クライアント領域＝プラグイン getSize（ピクセル）。
/// resizeView → 窓サイズ変更 → onSize。ユーザーリサイズも canResize / checkSizeConstraint 経由。
/// </summary>
internal sealed class VstPluginEditorWindow : Window
{
    private readonly Vst3EditorSession _session;
    private readonly VstHwndHost _host;
    private bool _closing;
    private bool _applyingSize;
    private bool _syncingFromUser;
    private DispatcherTimer? _sizePollTimer;
    private int _sizePollRemaining;

    public VstPluginEditorWindow(Window? owner, Vst3EditorSession session)
    {
        _session = session;
        Title = session.Title;
        Owner = owner;
        ShowInTaskbar = false;
        WindowStartupLocation = owner is null
            ? WindowStartupLocation.CenterScreen
            : WindowStartupLocation.CenterOwner;
        Background = Brushes.Black;
        SizeToContent = SizeToContent.Manual;
        ResizeMode = ResizeMode.CanResize;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        WindowStyle = WindowStyle.ToolWindow;
        MinWidth = 64;
        MinHeight = 64;

        var size = session.GetSize();
        var w = Math.Max(1, size.Width > 0 ? size.Width : 400);
        var h = Math.Max(1, size.Height > 0 ? size.Height : 300);

        _host = new VstHwndHost(session, w, h);
        Content = _host;

        session.ResizeFromPlugin += OnPluginResize;
        SourceInitialized += (_, _) => ApplyPluginPixelSize(w, h, notifyPlugin: false);
        SizeChanged += OnHostSizeChanged;
        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            StopSizePoll();
            if (_closing)
            {
                return;
            }

            DetachPluginNow();
        };
    }

    public int SlotIndex { get; init; } = -1;

    /// <summary>
    /// false のとき Closed で音声サスペンドを戻さない（差し替え破棄中のデッドロック防止）。
    /// </summary>
    public bool ResumeAudioOnClose { get; set; } = true;

    /// <summary>
    /// Window.Close より先に IPlugView を外す。HWND 破棄と removed() の競合を避ける。
    /// </summary>
    public void DetachPluginNow()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        StopSizePoll();
        try
        {
            _session.ResizeFromPlugin -= OnPluginResize;
        }
        catch
        {
        }

        try
        {
            _session.Dispose();
        }
        catch
        {
        }
    }

    /// <summary>
    /// 差し替え用。UI スレッドで removed()/Dispose/DestroyWindow しない。
    /// IPlugView teardown はプロセッサ Dispose に任せる。
    /// </summary>
    public void OrphanCloseForReplace()
    {
        ResumeAudioOnClose = false;
        StopSizePoll();
        if (!_closing)
        {
            _closing = true;
            try
            {
                _session.ResizeFromPlugin -= OnPluginResize;
            }
            catch
            {
            }
        }

        try
        {
            Hide();
            ShowInTaskbar = false;
        }
        catch
        {
        }
    }

    /// <summary>アプリ終了時。プラグイン UI を確実に破棄する（Owner 無し窓が残らないように）。</summary>
    public void ForceCloseForAppExit()
    {
        ResumeAudioOnClose = false;
        try
        {
            DetachPluginNow();
        }
        catch
        {
        }

        try
        {
            Close();
        }
        catch
        {
            try
            {
                Hide();
            }
            catch
            {
            }
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        Dispatcher.BeginInvoke(() =>
        {
            if (_closing)
            {
                return;
            }

            SyncFromPluginSize(notifyPlugin: true);
            StartSizePoll();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Altiverb 等は attach 直後の getSize が小さく、後から resizeView / getSize が変わる。</summary>
    private void StartSizePoll()
    {
        StopSizePoll();
        _sizePollRemaining = 12;
        _sizePollTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        _sizePollTimer.Tick += (_, _) =>
        {
            if (_closing || _sizePollRemaining-- <= 0)
            {
                StopSizePoll();
                return;
            }

            SyncFromPluginSize(notifyPlugin: true);
        };
        _sizePollTimer.Start();
    }

    private void StopSizePoll()
    {
        if (_sizePollTimer is null)
        {
            return;
        }

        _sizePollTimer.Stop();
        _sizePollTimer = null;
    }

    private void SyncFromPluginSize(bool notifyPlugin)
    {
        try
        {
            var after = _session.GetSize();
            if (after.Width > 0 && after.Height > 0)
            {
                ApplyPluginPixelSize(after.Width, after.Height, notifyPlugin);
            }
        }
        catch
        {
        }
    }

    private void OnPluginResize(Vst3HostNative.ViewRect rect)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_closing)
            {
                return;
            }

            // VST3: resizeView 受理後はホストが窓を変え、続けて onSize を呼ぶ
            ApplyPluginPixelSize(rect.Width, rect.Height, notifyPlugin: true);
        });
    }

    private void OnHostSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_closing || _applyingSize || _syncingFromUser)
        {
            return;
        }

        if (!IsLoaded || !IsVisible)
        {
            return;
        }

        // プラグイン起因の適用中以外＝ユーザー／システムによる枠変更
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !GetClientRect(hwnd, out var client))
        {
            return;
        }

        var pixelW = Math.Max(1, client.Right - client.Left);
        var pixelH = Math.Max(1, client.Bottom - client.Top);
        if (!_session.CanResize())
        {
            // リサイズ不可ならプラグイン希望サイズへ戻す
            SyncFromPluginSize(notifyPlugin: false);
            return;
        }

        var desired = new Vst3HostNative.ViewRect
        {
            Left = 0,
            Top = 0,
            Right = pixelW,
            Bottom = pixelH,
        };
        var constrained = _session.CheckSizeConstraint(desired) ?? desired;
        _syncingFromUser = true;
        try
        {
            ApplyPluginPixelSize(constrained.Width, constrained.Height, notifyPlugin: true);
        }
        finally
        {
            _syncingFromUser = false;
        }
    }

    private void ApplyPluginPixelSize(int pixelW, int pixelH, bool notifyPlugin)
    {
        if (_applyingSize)
        {
            return;
        }

        pixelW = Math.Max(1, pixelW);
        pixelH = Math.Max(1, pixelH);
        if (pixelW < 32 && pixelH < 32)
        {
            return;
        }

        _applyingSize = true;
        try
        {
            var dipScale = GetDipScale();
            _host.Width = pixelW / dipScale;
            _host.Height = pixelH / dipScale;
            _host.ResizeTo(pixelW, pixelH);
            FitWindowToClientPixels(pixelW, pixelH);

            if (notifyPlugin)
            {
                _session.NotifySize(new Vst3HostNative.ViewRect
                {
                    Left = 0,
                    Top = 0,
                    Right = pixelW,
                    Bottom = pixelH,
                });
            }
        }
        finally
        {
            _applyingSize = false;
        }
    }

    private double GetDipScale()
    {
        try
        {
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget is { } ct)
            {
                return Math.Max(0.01, ct.TransformToDevice.M11);
            }
        }
        catch
        {
        }

        return 1.0;
    }

    private void FitWindowToClientPixels(int clientW, int clientH)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var scale = GetDipScale();
        if (hwnd == IntPtr.Zero)
        {
            Width = clientW / scale;
            Height = clientH / scale;
            return;
        }

        if (!GetClientRect(hwnd, out var client) || !GetWindowRect(hwnd, out var window))
        {
            return;
        }

        var chromeW = (window.Right - window.Left) - (client.Right - client.Left);
        var chromeH = (window.Bottom - window.Top) - (client.Bottom - client.Top);
        var targetW = Math.Max(1, clientW + chromeW);
        var targetH = Math.Max(1, clientH + chromeH);

        Width = targetW / scale;
        Height = targetH / scale;

        SetWindowPos(
            hwnd,
            IntPtr.Zero,
            window.Left,
            window.Top,
            targetW,
            targetH,
            SwpNoZOrder | SwpNoActivate | SwpNoCopyBits);
    }

    private sealed class VstHwndHost : HwndHost
    {
        private readonly Vst3EditorSession _session;
        private IntPtr _child;
        private int _pixelW;
        private int _pixelH;

        public VstHwndHost(Vst3EditorSession session, int pixelW, int pixelH)
        {
            _session = session;
            _pixelW = Math.Max(1, pixelW);
            _pixelH = Math.Max(1, pixelH);
            // 初期はピクセル＝DIP 近似。親が ApplyPluginPixelSize で DPI 補正する。
            Width = _pixelW;
            Height = _pixelH;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Stretch;
        }

        public void ResizeTo(int pixelW, int pixelH)
        {
            _pixelW = Math.Max(1, pixelW);
            _pixelH = Math.Max(1, pixelH);
            if (_child != IntPtr.Zero)
            {
                MoveWindow(_child, 0, 0, _pixelW, _pixelH, true);
            }
        }

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            _child = CreateWindowEx(
                0,
                "Static",
                "",
                WsChild | WsVisible | WsClipChildren | WsClipSiblings,
                0,
                0,
                _pixelW,
                _pixelH,
                hwndParent.Handle,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            _session.Attach(_child);

            var size = _session.GetSize();
            if (size.Width > 0 && size.Height > 0)
            {
                _pixelW = size.Width;
                _pixelH = size.Height;
                MoveWindow(_child, 0, 0, _pixelW, _pixelH, true);
            }

            var rect = new Vst3HostNative.ViewRect
            {
                Left = 0,
                Top = 0,
                Right = _pixelW,
                Bottom = _pixelH,
            };
            _session.NotifySize(rect);
            _session.RequestResize(rect);
            return new HandleRef(this, _child);
        }

        protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmEraseBkgnd)
            {
                if (GetClientRect(hwnd, out var rc))
                {
                    var hdc = wParam;
                    var brush = CreateSolidBrush(0x000000);
                    FillRect(hdc, ref rc, brush);
                    DeleteObject(brush);
                }

                handled = true;
                return (IntPtr)1;
            }

            if (msg == WmSize && _child != IntPtr.Zero)
            {
                var w = lParam.ToInt32() & 0xFFFF;
                var h = (lParam.ToInt32() >> 16) & 0xFFFF;
                if (w > 0 && h > 0)
                {
                    MoveWindow(_child, 0, 0, w, h, true);
                }
            }

            return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            if (hwnd.Handle != IntPtr.Zero)
            {
                DestroyWindow(hwnd.Handle);
            }

            _child = IntPtr.Zero;
        }

        private const int WsChild = 0x40000000;
        private const int WsVisible = 0x10000000;
        private const int WsClipChildren = 0x02000000;
        private const int WsClipSiblings = 0x04000000;
        private const int WmEraseBkgnd = 0x0014;
        private const int WmSize = 0x0005;

        [DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
            int dwExStyle,
            string lpClassName,
            string lpWindowName,
            int dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32", SetLastError = true)]
        private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32", SetLastError = true)]
        private static extern bool GetClientRect(IntPtr hWnd, out NativeRect lpRect);

        [DllImport("gdi32", SetLastError = true)]
        private static extern IntPtr CreateSolidBrush(int colorRef);

        [DllImport("gdi32", SetLastError = true)]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32", SetLastError = true)]
        private static extern int FillRect(IntPtr hDC, ref NativeRect lprc, IntPtr hbr);
    }

    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoCopyBits = 0x0100;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);
}
