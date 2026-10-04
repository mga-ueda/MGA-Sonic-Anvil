using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.UI;

namespace MgaSonicAnvil;

/// <summary>同一ユーザーセッションでプロセスを1つに制限し、二重起動時は既存窓を前面へ出す。</summary>
internal static class SingleInstance
{
    private const string QueueMutexName = @"Local\MGA.SonicAnvil.OpenQueue";
    private const string QueueFileName = "open-queue.txt";

    private static readonly SingleInstanceState State = new(@"Local\MGA.SonicAnvil");

    private static string QueuePath => Path.Combine(AppStorage.RootDirectory, QueueFileName);

    /// <summary>即時取得。取れなければ終了中の持ち主を待って引き継ぐ。</summary>
    public static bool TryAcquireOrTakeOver() => State.TryAcquireOrTakeOver();

    public static void RequestActivate(IReadOnlyList<string>? paths = null)
    {
        if (paths is { Count: > 0 })
        {
            EnqueuePaths(paths);
        }

        State.RequestActivate();
    }

    public static void MarkExiting() => State.MarkExiting();

    public static void NotifyActivated() => State.NotifyActivated();

    public static void StartWatch(Action onActivate) => State.StartWatch(onActivate);

    /// <summary>設定保存後。mutex を先に渡し、洗い流しは裏で続ける。</summary>
    public static void ReleaseForHandoff() => State.ReleaseForHandoff();

    public static void Release() => State.Release();

    public static string[] TakePendingPaths()
    {
        string[] lines = [];
        WithQueue(() =>
        {
            if (!File.Exists(QueuePath))
            {
                return;
            }

            try
            {
                lines = File.ReadAllLines(QueuePath, Encoding.UTF8);
                File.Delete(QueuePath);
            }
            catch
            {
                lines = [];
            }
        });

        return LaunchFiles.Collect(lines);
    }

    private static void EnqueuePaths(IReadOnlyList<string> paths)
    {
        WithQueue(() =>
        {
            Directory.CreateDirectory(AppStorage.RootDirectory);
            File.AppendAllLines(QueuePath, paths, Encoding.UTF8);
        });
    }

    private static void WithQueue(Action action)
    {
        using var mutex = new Mutex(initiallyOwned: false, QueueMutexName);
        try
        {
            mutex.WaitOne();
        }
        catch (AbandonedMutexException)
        {
            // 前回が異常終了してもキュー操作は続ける。
        }

        try
        {
            action();
        }
        finally
        {
            try
            {
                mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // 所有していない場合は無視。
            }
        }
    }
}

/// <summary>名前つき IPC。テストでは接頭辞を変えて衝突を避ける。</summary>
internal sealed class SingleInstanceState : IDisposable
{
    private static readonly TimeSpan ActivateAckTimeout = TimeSpan.FromMilliseconds(1500);

    /// <summary>セッション保存＋ ASIO 洗い流し上限を覆う。</summary>
    public static readonly TimeSpan TakeOverWait = TimeSpan.FromMinutes(2);

    private readonly string _mutexName;
    private readonly string _activateEventName;
    private readonly string _activateAckEventName;
    private readonly string _exitingEventName;
    private readonly string _ownerPidMapName;

    private Mutex? _mutex;
    private EventWaitHandle? _activate;
    private EventWaitHandle? _activateAck;
    private EventWaitHandle? _exiting;
    private EventWaitHandle? _stop;
    private MemoryMappedFile? _ownerPid;
    private Thread? _watchThread;
    private bool _watching;

    public SingleInstanceState(string namePrefix)
    {
        _mutexName = namePrefix + ".SingleInstance";
        _activateEventName = namePrefix + ".Activate";
        _activateAckEventName = namePrefix + ".ActivateAck";
        _exitingEventName = namePrefix + ".Exiting";
        _ownerPidMapName = namePrefix + ".OwnerPid";
    }

    public bool TryAcquireOrTakeOver(TimeSpan? wait = null)
    {
        if (TryAcquire(TimeSpan.Zero))
        {
            return true;
        }

        if (!IsOwnerExiting())
        {
            return false;
        }

        return TryAcquire(wait ?? TakeOverWait);
    }

    public bool TryAcquire(TimeSpan wait)
    {
        if (_mutex is not null)
        {
            return true;
        }

        var mutex = new Mutex(initiallyOwned: false, _mutexName);
        try
        {
            if (!mutex.WaitOne(wait, exitContext: false))
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
        _ownerPid = SharedProcessId.Publish(_ownerPidMapName, Environment.ProcessId);
        EnsureActivateEvents();
        EnsureExitingEvent();
        try
        {
            _exiting?.Reset();
        }
        catch (ObjectDisposedException)
        {
        }

        return true;
    }

    public bool IsOwnerExiting()
    {
        try
        {
            using var exiting = EventWaitHandle.OpenExisting(_exitingEventName);
            return exiting.WaitOne(TimeSpan.Zero);
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
    }

    public void MarkExiting()
    {
        EnsureExitingEvent();
        try
        {
            _exiting?.Set();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void RequestActivate()
    {
        GrantForegroundToOwner();

        var signaled = false;
        try
        {
            using var ev = EventWaitHandle.OpenExisting(_activateEventName);
            ev.Set();
            signaled = true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // 既存プロセスの待ち受け前なら、その起動中の窓が出る。
        }

        if (!signaled)
        {
            return;
        }

        try
        {
            using var ack = EventWaitHandle.OpenExisting(_activateAckEventName);
            ack.WaitOne(ActivateAckTimeout);
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
    }

    public void NotifyActivated()
    {
        try
        {
            _activateAck?.Set();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void StartWatch(Action onActivate)
    {
        if (_watching)
        {
            return;
        }

        EnsureActivateEvents();
        _stop = new EventWaitHandle(false, EventResetMode.ManualReset);
        var activate = _activate!;
        var stop = _stop;
        var thread = new Thread(() =>
        {
            try
            {
                var handles = new WaitHandle[] { activate, stop };
                while (WaitHandle.WaitAny(handles) == 0)
                {
                    onActivate();
                }
            }
            catch (ObjectDisposedException)
            {
            }
        })
        {
            IsBackground = true,
            Name = "SingleInstanceActivate",
        };
        _watchThread = thread;
        _watching = true;
        thread.Start();
    }

    public void ReleaseForHandoff() => DisposeIpc(releaseMutex: true, disposeExiting: false);

    public void Release() => DisposeIpc(releaseMutex: true, disposeExiting: true);

    public void Dispose() => Release();

    private void DisposeIpc(bool releaseMutex, bool disposeExiting)
    {
        StopWatch();
        try
        {
            _activate?.Dispose();
            _activateAck?.Dispose();
            _stop?.Dispose();
            _ownerPid?.Dispose();
            _activate = null;
            _activateAck = null;
            _stop = null;
            _ownerPid = null;
            if (disposeExiting)
            {
                _exiting?.Dispose();
                _exiting = null;
            }

            if (releaseMutex)
            {
                try
                {
                    _mutex?.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                }

                _mutex?.Dispose();
                _mutex = null;
            }
        }
        catch (ApplicationException)
        {
            _mutex?.Dispose();
            _mutex = null;
        }
    }

    private void StopWatch()
    {
        try
        {
            _stop?.Set();
        }
        catch (ObjectDisposedException)
        {
        }

        var thread = _watchThread;
        _watchThread = null;
        if (thread is { IsAlive: true } && thread != Thread.CurrentThread)
        {
            thread.Join(TimeSpan.FromSeconds(1));
        }

        _watching = false;
    }

    private void EnsureActivateEvents()
    {
        _activate ??= new EventWaitHandle(false, EventResetMode.AutoReset, _activateEventName);
        _activateAck ??= new EventWaitHandle(false, EventResetMode.AutoReset, _activateAckEventName);
    }

    private void EnsureExitingEvent() =>
        _exiting ??= new EventWaitHandle(false, EventResetMode.ManualReset, _exitingEventName);

    private void GrantForegroundToOwner()
    {
        if (SharedProcessId.TryRead(_ownerPidMapName, out var pid) && ForegroundActivation.TryAllowProcess(pid))
        {
            return;
        }

        ForegroundActivation.TryAllowAny();
    }
}

/// <summary>既存プロセスの PID を名前付きメモリで共有する。</summary>
internal static class SharedProcessId
{
    public static MemoryMappedFile? Publish(string mapName, int processId)
    {
        try
        {
            var map = MemoryMappedFile.CreateOrOpen(mapName, sizeof(int));
            using var view = map.CreateViewAccessor(0, sizeof(int));
            view.Write(0, processId);
            return map;
        }
        catch
        {
            return null;
        }
    }

    public static bool TryRead(string mapName, out int processId)
    {
        processId = 0;
        try
        {
            using var map = MemoryMappedFile.OpenExisting(mapName);
            using var view = map.CreateViewAccessor(0, sizeof(int));
            processId = view.ReadInt32(0);
            return processId > 0;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
