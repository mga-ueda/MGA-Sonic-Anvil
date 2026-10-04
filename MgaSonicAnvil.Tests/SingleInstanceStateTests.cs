using System;
using System.Threading;
using MgaSonicAnvil;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class SingleInstanceStateTests
{
    [Fact]
    public void SecondAcquire_FailsWhileFirstHolds()
    {
        var prefix = UniquePrefix();
        using var owner = OwnerThread.Start(prefix);
        using var second = new SingleInstanceState(prefix);
        Assert.False(second.TryAcquire(TimeSpan.Zero));
        Assert.False(second.TryAcquireOrTakeOver(TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public void TakeOver_WaitsWhileExitingThenAcquiresAfterHandoff()
    {
        var prefix = UniquePrefix();
        using var owner = OwnerThread.Start(prefix, markExiting: true);
        using var second = new SingleInstanceState(prefix);

        var handed = false;
        var waiter = new Thread(() =>
        {
            Thread.Sleep(80);
            owner.Handoff();
            handed = true;
        })
        {
            IsBackground = true,
        };
        waiter.Start();

        Assert.True(second.TryAcquireOrTakeOver(TimeSpan.FromSeconds(5)));
        waiter.Join(TimeSpan.FromSeconds(5));
        Assert.True(handed);
        Assert.False(second.IsOwnerExiting());
    }

    [Fact]
    public void TakeOver_DoesNotActivateWhenOwnerIsExitingAndStillHeld()
    {
        var prefix = UniquePrefix();
        using var owner = OwnerThread.Start(prefix, markExiting: true);
        using var second = new SingleInstanceState(prefix);
        Assert.True(second.IsOwnerExiting());
        Assert.False(second.TryAcquireOrTakeOver(TimeSpan.FromMilliseconds(30)));
        Assert.True(owner.Acquired);
    }

    [Fact]
    public void ReleaseForHandoff_AllowsImmediateAcquire()
    {
        var prefix = UniquePrefix();
        using var owner = OwnerThread.Start(prefix, markExiting: true);
        using var second = new SingleInstanceState(prefix);
        owner.Handoff();
        Assert.True(second.TryAcquire(TimeSpan.Zero));
    }

    private static string UniquePrefix() =>
        @"Local\MGA.SonicAnvil.Test." + Guid.NewGuid().ToString("N");

    /// <summary>名前つき Mutex は同一スレッドで再入するので、所有者は別スレッドに置く。</summary>
    private sealed class OwnerThread : IDisposable
    {
        private readonly SingleInstanceState _state;
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _ready = new();
        private readonly ManualResetEventSlim _handoff = new();
        private readonly ManualResetEventSlim _handed = new();
        private readonly ManualResetEventSlim _stop = new();

        private OwnerThread(string prefix, bool markExiting)
        {
            _state = new SingleInstanceState(prefix);
            _thread = new Thread(() =>
            {
                Acquired = _state.TryAcquire(TimeSpan.Zero);
                if (markExiting)
                {
                    _state.MarkExiting();
                }

                _ready.Set();
                var waiters = new[] { _handoff.WaitHandle, _stop.WaitHandle };
                while (WaitHandle.WaitAny(waiters) == 0)
                {
                    _state.ReleaseForHandoff();
                    _handed.Set();
                    _handoff.Reset();
                }

                _state.Release();
            })
            {
                IsBackground = true,
            };
            _thread.Start();
            if (!_ready.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("Owner thread did not acquire.");
            }
        }

        public bool Acquired { get; private set; }

        public static OwnerThread Start(string prefix, bool markExiting = false) =>
            new(prefix, markExiting);

        public void Handoff()
        {
            _handoff.Set();
            if (!_handed.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("Owner thread did not hand off.");
            }
        }

        public void Dispose()
        {
            _stop.Set();
            _thread.Join(TimeSpan.FromSeconds(5));
            _ready.Dispose();
            _handoff.Dispose();
            _handed.Dispose();
            _stop.Dispose();
        }
    }
}
