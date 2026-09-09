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
    private const string MutexName = @"Local\MGA.SonicAnvil.SingleInstance";
    private const string ActivateEventName = @"Local\MGA.SonicAnvil.Activate";
    private const string ActivateAckEventName = @"Local\MGA.SonicAnvil.ActivateAck";
    private const string OwnerPidMapName = @"Local\MGA.SonicAnvil.OwnerPid";
    private const string QueueMutexName = @"Local\MGA.SonicAnvil.OpenQueue";
    private const string QueueFileName = "open-queue.txt";
    private static readonly TimeSpan ActivateAckTimeout = TimeSpan.FromMilliseconds(1500);

    private static Mutex? _mutex;
    private static EventWaitHandle? _activate;
    private static EventWaitHandle? _activateAck;
    private static EventWaitHandle? _stop;
    private static MemoryMappedFile? _ownerPid;
    private static bool _watching;

    private static string QueuePath => Path.Combine(AppStorage.RootDirectory, QueueFileName);

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
        _ownerPid = SharedProcessId.Publish(OwnerPidMapName, Environment.ProcessId);
        EnsureActivateEvents();
        return true;
    }

    public static void RequestActivate(IReadOnlyList<string>? paths = null)
    {
        if (paths is { Count: > 0 })
        {
            EnqueuePaths(paths);
        }

        GrantForegroundToOwner();

        var signaled = false;
        try
        {
            using var ev = EventWaitHandle.OpenExisting(ActivateEventName);
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

        // 権限譲渡中に既存プロセスが SetForegroundWindow できるよう、ACK まで残る。
        try
        {
            using var ack = EventWaitHandle.OpenExisting(ActivateAckEventName);
            ack.WaitOne(ActivateAckTimeout);
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
    }

    public static void NotifyActivated()
    {
        try
        {
            _activateAck?.Set();
        }
        catch (ObjectDisposedException)
        {
        }
    }

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

    public static void StartWatch(Action onActivate)
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
        _watching = true;
        thread.Start();
    }

    public static void Release()
    {
        try
        {
            _stop?.Set();
            _activate?.Dispose();
            _activateAck?.Dispose();
            _stop?.Dispose();
            _ownerPid?.Dispose();
            _activate = null;
            _activateAck = null;
            _stop = null;
            _ownerPid = null;
            _watching = false;
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

    private static void EnsureActivateEvents()
    {
        _activate ??= new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        _activateAck ??= new EventWaitHandle(false, EventResetMode.AutoReset, ActivateAckEventName);
    }

    private static void GrantForegroundToOwner()
    {
        if (SharedProcessId.TryRead(OwnerPidMapName, out var pid) && ForegroundActivation.TryAllowProcess(pid))
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
