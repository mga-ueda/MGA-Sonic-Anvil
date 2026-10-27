namespace MgaSonicAnvil.Video.Dxgi;

/// <summary>
/// スクラブ位置（Anchor）の周りにデコード済みフレームを溜めるスレッドセーフな窓。PTS 順に並べ、
/// 容量を超えたら Anchor から最も遠い端を追い出す（前後どちらにも戻れるように）。
/// 追い出したフレームは <see cref="Evicted"/> で通知し、呼び出し側がバッファをプールへ戻す。
/// 直前に <see cref="TryGetNearest"/> で渡したフレームは UI が使っている可能性があるので通知しない。
/// </summary>
internal sealed class DxgiFrameCache
{
    private readonly object _gate = new();
    private readonly List<DxgiDecodedFrame> _frames = [];
    private int _capacity;
    private long _anchor;
    private DxgiDecodedFrame? _lastServed;

    public DxgiFrameCache(int capacity = 32)
    {
        _capacity = Math.Max(1, capacity);
    }

    /// <summary>追い出されたフレーム（ワーカー側で Add を呼んだスレッドから、ロック外で呼ばれる）。</summary>
    public event Action<DxgiDecodedFrame>? Evicted;

    public int Capacity
    {
        get
        {
            lock (_gate)
            {
                return _capacity;
            }
        }

        set
        {
            List<DxgiDecodedFrame>? evicted = null;
            lock (_gate)
            {
                _capacity = Math.Max(1, value);
                Trim(ref evicted);
            }

            RaiseEvicted(evicted);
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _frames.Count;
            }
        }
    }

    /// <summary>追い出し判定の中心（最新の要求位置）。</summary>
    public long AnchorTicks
    {
        get
        {
            lock (_gate)
            {
                return _anchor;
            }
        }

        set
        {
            lock (_gate)
            {
                _anchor = value;
            }
        }
    }

    /// <summary>キャッシュ内の PTS 範囲。空なら (0, -1)。</summary>
    public (long Min, long Max) PtsRange
    {
        get
        {
            lock (_gate)
            {
                return _frames.Count == 0 ? (0, -1) : (_frames[0].PtsTicks, _frames[^1].PtsTicks);
            }
        }
    }

    /// <summary>最後のフレームの終端（Pts+Duration）。空なら -1。</summary>
    public long LastEndTicks
    {
        get
        {
            lock (_gate)
            {
                return _frames.Count == 0 ? -1 : _frames[^1].PtsTicks + _frames[^1].DurationTicks;
            }
        }
    }

    /// <summary>ticks 以上で最小の PTS。無ければ -1（「今の読み区間のうち、まだ残っている先頭」を知るため）。</summary>
    public long MinPtsAtOrAfter(long ticks)
    {
        lock (_gate)
        {
            var index = IndexOfFirstAtOrAfter(ticks);
            return index < _frames.Count ? _frames[index].PtsTicks : -1;
        }
    }

    /// <summary>同じ PTS があれば置き換える。容量超過は Anchor から遠い端を捨てる。</summary>
    public void Add(DxgiDecodedFrame frame)
    {
        List<DxgiDecodedFrame>? evicted = null;
        lock (_gate)
        {
            var index = IndexOfFirstAtOrAfter(frame.PtsTicks);
            if (index < _frames.Count && _frames[index].PtsTicks == frame.PtsTicks)
            {
                var old = _frames[index];
                _frames[index] = frame;
                if (!ReferenceEquals(old, frame))
                {
                    (evicted ??= []).Add(old);
                }
            }
            else
            {
                _frames.Insert(index, frame);
            }

            Trim(ref evicted);
        }

        RaiseEvicted(evicted);
    }

    /// <summary>この PTS を今入れたら（追い出されずに）残るか。遠い捨て読みのコピーを省くための見積り。</summary>
    public bool WouldKeep(long ptsTicks)
    {
        lock (_gate)
        {
            if (_frames.Count < _capacity)
            {
                return true;
            }

            var farthest = Math.Max(Math.Abs(_frames[0].PtsTicks - _anchor), Math.Abs(_frames[^1].PtsTicks - _anchor));
            return Math.Abs(ptsTicks - _anchor) < farthest;
        }
    }

    /// <summary>[Pts, Pts+Duration) に ticks を含むフレーム。</summary>
    public bool TryGetCovering(long ticks, out DxgiDecodedFrame? frame)
    {
        lock (_gate)
        {
            frame = FindCovering(ticks);
            return frame is not null;
        }
    }

    /// <summary>
    /// ticks を含むフレーム、無ければ最も近いもの（maxDistanceTicks 以内）。渡したフレームは追い出し通知の対象外になる。
    /// </summary>
    public bool TryGetNearest(long ticks, long maxDistanceTicks, out DxgiDecodedFrame? frame)
    {
        lock (_gate)
        {
            frame = FindCovering(ticks);
            if (frame is null && _frames.Count > 0)
            {
                var index = IndexOfFirstAtOrAfter(ticks);
                DxgiDecodedFrame? best = null;
                var bestDistance = long.MaxValue;
                for (var i = Math.Max(0, index - 1); i <= Math.Min(_frames.Count - 1, index); i++)
                {
                    var d = _frames[i].DistanceTo(ticks);
                    if (d < bestDistance)
                    {
                        best = _frames[i];
                        bestDistance = d;
                    }
                }

                if (best is not null && bestDistance <= maxDistanceTicks)
                {
                    frame = best;
                }
            }

            if (frame is not null)
            {
                _lastServed = frame;
            }

            return frame is not null;
        }
    }

    /// <summary>全部捨てる（通知しない。Close／Open で呼ぶ）。</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _frames.Clear();
            _lastServed = null;
        }
    }

    private DxgiDecodedFrame? FindCovering(long ticks)
    {
        if (_frames.Count == 0)
        {
            return null;
        }

        var index = IndexOfFirstAtOrAfter(ticks);
        if (index < _frames.Count && _frames[index].Covers(ticks))
        {
            return _frames[index];
        }

        if (index > 0 && _frames[index - 1].Covers(ticks))
        {
            return _frames[index - 1];
        }

        return null;
    }

    private int IndexOfFirstAtOrAfter(long ticks)
    {
        var lo = 0;
        var hi = _frames.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (_frames[mid].PtsTicks < ticks)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private void Trim(ref List<DxgiDecodedFrame>? evicted)
    {
        while (_frames.Count > _capacity)
        {
            var first = _frames[0];
            var last = _frames[^1];
            var dropFirst = Math.Abs(first.PtsTicks - _anchor) >= Math.Abs(last.PtsTicks - _anchor);
            var victim = dropFirst ? first : last;
            _frames.RemoveAt(dropFirst ? 0 : _frames.Count - 1);
            if (!ReferenceEquals(victim, _lastServed))
            {
                (evicted ??= []).Add(victim);
            }
        }
    }

    private void RaiseEvicted(List<DxgiDecodedFrame>? evicted)
    {
        if (evicted is null)
        {
            return;
        }

        var handler = Evicted;
        if (handler is null)
        {
            return;
        }

        foreach (var frame in evicted)
        {
            handler(frame);
        }
    }
}
