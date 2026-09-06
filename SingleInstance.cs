using System.Threading;

namespace MgaSonicAnvil;

/// <summary>同一ユーザーセッションでプロセスを1つに制限し、二重起動時は既存窓を前面へ出す。</summary>
internal static class SingleInstance
{
    private const string MutexName = @"Local\MGA.SonicAnvil.SingleInstance";
    private const string ActivateEventName = @"Local\MGA.SonicAnvil.Activate";

    private static Mutex? _mutex;
    private static EventWaitHandle? _activate;
    private static EventWaitHandle? _stop;

    public static bool TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: false, MutexName);
        try
        {
            if (!mutex.WaitOne(TimeSpan.Zero, exitContext: false))
            {
                mutex.Dispose();
                return false;
            }
        }
        catch (AbandonedMutexException)
        {
            // 前回プロセスが異常終了していても、こちらが所有権を得る。
        }

        _mutex = mutex;
        return true;
    }

    public static void RequestActivate()
    {
        try
        {
            using var ev = EventWaitHandle.OpenExisting(ActivateEventName);
            ev.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // 既存プロセスの待ち受け前なら、その起動中の窓が出る。
        }
    }

    public static void StartWatch(Action onActivate)
    {
        if (_activate is not null)
        {
            return;
        }

        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        _stop = new EventWaitHandle(false, EventResetMode.ManualReset);
        var activate = _activate;
        var stop = _stop;
        var thread = new Thread(() =>
        {
            var handles = new WaitHandle[] { activate, stop };
            while (WaitHandle.WaitAny(handles) == 0)
            {
                onActivate();
            }
        })
        {
            IsBackground = true,
            Name = "SingleInstanceActivate",
        };
        thread.Start();
    }

    public static void Release()
    {
        try
        {
            _stop?.Set();
            _activate?.Dispose();
            _stop?.Dispose();
            _activate = null;
            _stop = null;
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            _mutex = null;
        }
        catch (ApplicationException)
        {
            _mutex?.Dispose();
            _mutex = null;
        }
    }
}
