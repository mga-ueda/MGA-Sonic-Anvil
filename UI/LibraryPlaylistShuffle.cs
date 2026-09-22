namespace MgaSonicAnvil.UI;

/// <summary>
/// プレイヤーのランダム再生順。一巡するまで同じ index を出さず、
/// 常に次の一周分を先に積んでおく（境界で Next を複数回呼んでも同じ次曲が返る）。
/// </summary>
internal sealed class LibraryPlaylistShuffle
{
    private readonly List<int> _order = [];
    private int _at = -1;
    private int _trackCount;

    public void Clear()
    {
        _order.Clear();
        _at = -1;
        _trackCount = 0;
    }

    public int First(int count, Func<int, int>? nextRandom = null)
    {
        if (count <= 0)
        {
            return -1;
        }

        Reset(count, nextRandom);
        _at = 0;
        return _order[0];
    }

    public int Last(int count, Func<int, int>? nextRandom = null)
    {
        if (count <= 0)
        {
            return -1;
        }

        Reset(count, nextRandom);
        _at = count - 1;
        return _order[_at];
    }

    public int Next(int count, int current, Func<int, int>? nextRandom = null)
    {
        if (count <= 0)
        {
            return -1;
        }

        if (count == 1)
        {
            return 0;
        }

        if (_trackCount != count)
        {
            Reset(count, nextRandom, preferStart: current);
        }

        SyncToCurrent(current, nextRandom);
        EnsureBuffered(nextRandom);
        _at++;
        TrimConsumed();
        return _order[_at];
    }

    public int Previous(int count, int current, Func<int, int>? nextRandom = null)
    {
        if (count <= 0)
        {
            return -1;
        }

        if (count == 1)
        {
            return 0;
        }

        if (_trackCount != count)
        {
            Reset(count, nextRandom, preferStart: current);
        }

        SyncToCurrent(current, nextRandom);
        if (_at <= 0)
        {
            _at = count - 1;
        }
        else
        {
            _at--;
        }

        return _order[_at];
    }

    private void SyncToCurrent(int current, Func<int, int>? nextRandom)
    {
        if (_order.Count == 0)
        {
            Reset(_trackCount, nextRandom, preferStart: current);
            _at = 0;
            return;
        }

        if (_at >= 0 && _at < _order.Count && _order[_at] == current)
        {
            return;
        }

        // 先読みでポインタが進んでいても、いま鳴っている曲は直後の後方から拾う。
        var from = _at < 0 ? 0 : Math.Min(_order.Count - 1, _at);
        var back = Math.Max(0, from - _trackCount);
        for (var i = from; i >= back; i--)
        {
            if (_order[i] == current)
            {
                _at = i;
                return;
            }
        }

        for (var i = from + 1; i < _order.Count; i++)
        {
            if (_order[i] == current)
            {
                _at = i;
                return;
            }
        }

        Reset(_trackCount, nextRandom, preferStart: current);
        _at = 0;
    }

    /// <summary>現在位置の先に、少なくとも一周分を残す。</summary>
    private void EnsureBuffered(Func<int, int>? nextRandom)
    {
        while (_order.Count - _at - 1 < _trackCount)
        {
            var avoid = _order.Count > 0 ? _order[^1] : (int?)null;
            AppendCycle(nextRandom, avoidStart: avoid);
        }
    }

    private void TrimConsumed()
    {
        // いま鳴っている一周は残す。境界で Next が二度呼ばれても同じ次曲を返すため。
        if (_trackCount <= 0 || _at < _trackCount * 2)
        {
            return;
        }

        _order.RemoveRange(0, _trackCount);
        _at -= _trackCount;
    }

    private void Reset(int count, Func<int, int>? nextRandom, int? preferStart = null)
    {
        _order.Clear();
        _trackCount = count;
        _at = -1;
        AppendCycle(nextRandom, preferStart: preferStart);
        // 二周目を先に作り、一周目の末尾→二周目先頭の境界でもたつかないようにする。
        if (count > 1)
        {
            AppendCycle(nextRandom, avoidStart: _order[^1]);
        }
    }

    private void AppendCycle(Func<int, int>? nextRandom, int? preferStart = null, int? avoidStart = null)
    {
        var cycle = new int[_trackCount];
        for (var i = 0; i < _trackCount; i++)
        {
            cycle[i] = i;
        }

        Shuffle(cycle, nextRandom);
        if (preferStart is { } prefer && prefer >= 0 && prefer < _trackCount)
        {
            MoveToFront(cycle, prefer);
        }
        else if (avoidStart is { } avoid && avoid >= 0 && avoid < _trackCount && cycle[0] == avoid)
        {
            var swap = 1 + NextRandom(nextRandom, _trackCount - 1);
            (cycle[0], cycle[swap]) = (cycle[swap], cycle[0]);
        }

        _order.AddRange(cycle);
    }

    private static void MoveToFront(int[] order, int value)
    {
        var i = Array.IndexOf(order, value);
        if (i <= 0)
        {
            return;
        }

        var held = order[i];
        Array.Copy(order, 0, order, 1, i);
        order[0] = held;
    }

    private static void Shuffle(int[] order, Func<int, int>? nextRandom)
    {
        for (var i = order.Length - 1; i > 0; i--)
        {
            var j = NextRandom(nextRandom, i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
    }

    private static int NextRandom(Func<int, int>? nextRandom, int maxExclusive) =>
        nextRandom?.Invoke(maxExclusive) ?? Random.Shared.Next(maxExclusive);
}
