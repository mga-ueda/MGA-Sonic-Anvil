using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class BpmDisplayLockTests
{
    [Fact]
    public void Push_StableAround150_LocksAndIgnoresOneHundred()
    {
        var lock_ = new BpmDisplayLock();
        for (var i = 0; i < BpmDisplayLock.StableCount; i++)
        {
            lock_.Push(150f + (i % 2 == 0 ? -1f : 1f));
        }

        Assert.True(lock_.IsLocked);
        Assert.InRange(lock_.DisplayBpm!.Value, 145f, 155f);

        for (var i = 0; i < BpmDisplayLock.UnlockStreak - 1; i++)
        {
            lock_.Push(100f);
            Assert.True(lock_.IsLocked);
            Assert.InRange(lock_.DisplayBpm!.Value, 145f, 155f);
        }
    }

    [Fact]
    public void Push_ConsecutiveOutliers_Unlocks()
    {
        var lock_ = new BpmDisplayLock();
        for (var i = 0; i < BpmDisplayLock.StableCount; i++)
        {
            lock_.Push(150f);
        }

        Assert.True(lock_.IsLocked);

        for (var i = 0; i < BpmDisplayLock.UnlockStreak; i++)
        {
            lock_.Push(100f);
        }

        Assert.False(lock_.IsLocked);
    }

    [Fact]
    public void Push_HalfTempoWhileLocked_IsIgnored()
    {
        var lock_ = new BpmDisplayLock();
        for (var i = 0; i < BpmDisplayLock.StableCount; i++)
        {
            lock_.Push(120f);
        }

        Assert.True(lock_.IsLocked);
        lock_.Push(60f);
        Assert.True(lock_.IsLocked);
        Assert.InRange(lock_.DisplayBpm!.Value, 115f, 125f);
    }

    /// <summary>
    /// 一度 75 でロックすると、ネイティブが 150 を返しても FoldOctave で半テンポ側に畳まれる。
    /// （150→75 表示が固まる一因。クリック間隔とは別経路。）
    /// </summary>
    [Fact]
    public void Push_DoubleTempoWhileLockedAt75_IsFoldedBackToHalf()
    {
        var lock_ = new BpmDisplayLock();
        for (var i = 0; i < BpmDisplayLock.StableCount; i++)
        {
            lock_.Push(75f);
        }

        Assert.True(lock_.IsLocked);
        lock_.Push(150f);
        Assert.True(lock_.IsLocked);
        Assert.InRange(lock_.DisplayBpm!.Value, 70f, 80f);
    }

    [Fact]
    public void Reset_ClearsLock()
    {
        var lock_ = new BpmDisplayLock();
        for (var i = 0; i < BpmDisplayLock.StableCount; i++)
        {
            lock_.Push(128f);
        }

        Assert.True(lock_.IsLocked);
        lock_.Reset();
        Assert.False(lock_.IsLocked);
        Assert.Null(lock_.DisplayBpm);
    }
}
