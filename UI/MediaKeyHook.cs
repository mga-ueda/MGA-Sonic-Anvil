using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Threading;

namespace MgaSonicAnvil.UI;

/// <summary>
/// 再生・一時停止／停止／前後のメディアキーを、起動中はアプリの前面・背面に関係なく横取りする。
/// 低レベルフックで握りつぶすので、ブラウザ（YouTube）などほかのアプリには届かない。
/// フックは専用スレッドで回す。UI スレッドが詰まると OS がフックを外すため。
/// </summary>
internal sealed class MediaKeyHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint WmQuit = 0x0012;

    private const int VkMediaNextTrack = 0xB0;
    private const int VkMediaPrevTrack = 0xB1;
    private const int VkMediaStop = 0xB2;
    private const int VkMediaPlayPause = 0xB3;

    private readonly Dispatcher _dispatcher;
    private readonly Action<Key> _onKeyDown;
    private readonly HashSet<int> _down = new();
    private readonly ManualResetEventSlim _started = new();
    private readonly Thread _thread;
    private LowLevelKeyboardProc? _proc;
    private volatile uint _threadId;
    private volatile bool _disposed;

    private MediaKeyHook(Dispatcher dispatcher, Action<Key> onKeyDown)
    {
        _dispatcher = dispatcher;
        _onKeyDown = onKeyDown;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "MediaKeyHook",
        };
    }

    private bool IsInstalled { get; set; }

    /// <summary>フックを張れなかったら null（ウィンドウ内の PreviewKeyDown だけで扱う）。</summary>
    public static MediaKeyHook? TryStart(Dispatcher dispatcher, Action<Key> onKeyDown)
    {
        var hook = new MediaKeyHook(dispatcher, onKeyDown);
        hook._thread.Start();
        hook._started.Wait(TimeSpan.FromSeconds(2));
        if (hook.IsInstalled)
        {
            return hook;
        }

        hook.Dispose();
        return null;
    }

    public void Dispose()
    {
        _disposed = true;
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        _proc = HookProc;
        var handle = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(null), 0);
        IsInstalled = handle != IntPtr.Zero;
        _started.Set();
        if (!IsInstalled)
        {
            return;
        }

        try
        {
            // フックのコールバックは GetMessage の中で呼ばれる。窓は無いので配送はしない。
            while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
            {
            }
        }
        finally
        {
            UnhookWindowsHookEx(handle);
        }
    }

    private IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0 || _disposed)
        {
            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        var vk = Marshal.ReadInt32(lParam);
        var key = vk switch
        {
            VkMediaPlayPause => Key.MediaPlayPause,
            VkMediaStop => Key.MediaStop,
            VkMediaPrevTrack => Key.MediaPreviousTrack,
            VkMediaNextTrack => Key.MediaNextTrack,
            _ => Key.None,
        };
        if (key == Key.None)
        {
            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        var message = wParam.ToInt32();
        if (message is WmKeyDown or WmSysKeyDown)
        {
            // 押しっぱなしのオートリピートは 1 回扱い。
            if (_down.Add(vk))
            {
                _dispatcher.BeginInvoke(_onKeyDown, key);
            }
        }
        else if (message is WmKeyUp or WmSysKeyUp)
        {
            _down.Remove(vk);
        }

        // 次のフックにも前面アプリにも渡さない。
        return 1;
    }

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMsg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PtX;
        public int PtY;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out NativeMsg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
