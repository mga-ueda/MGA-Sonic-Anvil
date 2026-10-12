namespace MgaSonicAnvil.Domain;

/// <summary>
/// F10 プレイヤー専用の一時キュー（音声波形／動画本再生／F8）。
/// A-B／マーカーは別ファイル再生で忘れる。Esc は A-B のみ解除。
/// 同じ音声をエディタへ戻すとき、マーカーは既存とマージ、揃った A-B はサンプルループにする（即保存はしない）。
/// </summary>
internal sealed class PlayerScratchCues
{
    private readonly List<MarkerSnapshot> _markers = [];
    private long? _abIn;
    private long? _abOut;

    public long? AbIn => _abIn;

    public long? AbOut => _abOut;

    public IReadOnlyList<MarkerSnapshot> Markers => _markers;

    public bool HasAbPoint => _abIn is not null || _abOut is not null;

    public bool HasMarkers => _markers.Count > 0;

    public bool HasAny => HasAbPoint || HasMarkers;

    public void ClearAll()
    {
        _abIn = null;
        _abOut = null;
        _markers.Clear();
    }

    public void ClearAb()
    {
        _abIn = null;
        _abOut = null;
    }

    public void ReplaceAb(long? abIn, long? abOut)
    {
        _abIn = abIn;
        _abOut = abOut;
    }

    public void ClearMarkers() => _markers.Clear();

    /// <summary>
    /// I キー。既にあれば更新。Out より後ろ／同じなら拒否（前後入れ替え防止）。
    /// </summary>
    public static bool TryResolveAbIn(long frame, long? currentIn, long? currentOut, out long nextIn)
    {
        nextIn = frame;
        if (frame < 0)
        {
            return false;
        }

        if (currentOut is long outFrame && frame >= outFrame)
        {
            nextIn = currentIn ?? frame;
            return false;
        }

        return true;
    }

    /// <summary>
    /// O キー。既にあれば更新。In より前／同じなら拒否（前後入れ替え防止）。
    /// </summary>
    public static bool TryResolveAbOut(long frame, long? currentIn, long? currentOut, out long nextOut)
    {
        nextOut = frame;
        if (frame < 0)
        {
            return false;
        }

        if (currentIn is long inFrame && frame <= inFrame)
        {
            nextOut = currentOut ?? frame;
            return false;
        }

        return true;
    }

    public bool TrySetAbIn(long frame)
    {
        if (!TryResolveAbIn(frame, _abIn, _abOut, out var next))
        {
            return false;
        }

        _abIn = next;
        return true;
    }

    public bool TrySetAbOut(long frame)
    {
        if (!TryResolveAbOut(frame, _abIn, _abOut, out var next))
        {
            return false;
        }

        _abOut = next;
        return true;
    }

    /// <summary>両方揃っていて長さがあるときだけ無限リピート範囲。</summary>
    public static WaveSelection LoopRange(long? abIn, long? abOut)
    {
        if (abIn is not long start || abOut is not long end || end <= start)
        {
            return WaveSelection.Empty;
        }

        return new WaveSelection(start, end);
    }

    public WaveSelection LoopRange() => LoopRange(_abIn, _abOut);

    /// <summary>
    /// エディタの DoubleClickSpanAt 相当。A-B 内ならその範囲、それ以外はマーカー間（端は 0／末尾）。
    /// </summary>
    public WaveSelection DoubleClickSpanAt(long frame, long frameCount)
    {
        if (frameCount <= 0)
        {
            return WaveSelection.Empty;
        }

        var loop = LoopRange();
        if (!loop.IsEmpty && frame >= loop.StartFrame && frame < loop.EndFrame)
        {
            return loop;
        }

        var cues = CueFrames();
        if (cues.Count == 0)
        {
            return new WaveSelection(0, frameCount);
        }

        var index = cues.BinarySearch(frame);
        int left;
        int right;
        if (index >= 0)
        {
            left = index;
            right = index + 1;
        }
        else
        {
            right = ~index;
            left = right - 1;
        }

        var start = left < 0 ? 0 : cues[left];
        var end = right >= cues.Count ? frameCount : cues[right];
        var range = end <= start ? new WaveSelection(0, frameCount) : new WaveSelection(start, end);
        if (!loop.IsEmpty
            && !range.IsEmpty
            && range.StartFrame >= loop.StartFrame
            && range.EndFrame <= loop.EndFrame)
        {
            return loop;
        }

        return range;
    }

    private List<long> CueFrames()
    {
        var cues = new List<long>(_markers.Count + 2);
        foreach (var marker in _markers)
        {
            cues.Add(marker.Frame);
        }

        var loop = LoopRange();
        if (!loop.IsEmpty)
        {
            cues.Add(loop.StartFrame);
            cues.Add(loop.EndFrame);
        }

        if (cues.Count == 0)
        {
            return cues;
        }

        cues.Sort();
        var unique = new List<long>(cues.Count);
        long? prev = null;
        foreach (var frame in cues)
        {
            if (prev is long same && same == frame)
            {
                continue;
            }

            unique.Add(frame);
            prev = frame;
        }

        return unique;
    }

    public bool HasMarkerAt(long frame)
    {
        for (var i = 0; i < _markers.Count; i++)
        {
            if (_markers[i].Frame == frame)
            {
                return true;
            }
        }

        return false;
    }

    public bool TryAddMarker(long frame, long frameCount)
    {
        if (frameCount <= 0)
        {
            return false;
        }

        frame = Math.Clamp(frame, 0, frameCount);
        if (HasMarkerAt(frame))
        {
            return false;
        }

        var insert = 0;
        while (insert < _markers.Count && _markers[insert].Frame < frame)
        {
            insert++;
        }

        _markers.Insert(insert, new MarkerSnapshot(frame, string.Empty));
        return true;
    }

    public bool TryRemoveMarkers(IReadOnlyList<long> frames)
    {
        if (frames.Count == 0 || _markers.Count == 0)
        {
            return false;
        }

        var remove = new HashSet<long>(frames);
        var before = _markers.Count;
        _markers.RemoveAll(marker => remove.Contains(marker.Frame));
        return _markers.Count != before;
    }

    public MarkerSnapshot[] SnapshotMarkers() => _markers.ToArray();

    public void ReplaceMarkers(IReadOnlyList<MarkerSnapshot> markers)
    {
        _markers.Clear();
        if (markers.Count == 0)
        {
            return;
        }

        var sorted = markers.OrderBy(marker => marker.Frame).ToArray();
        long? prev = null;
        foreach (var marker in sorted)
        {
            if (prev is long same && same == marker.Frame)
            {
                continue;
            }

            _markers.Add(marker);
            prev = marker.Frame;
        }
    }

    public bool TryMoveMarkers(
        IReadOnlyList<long> frames,
        long delta,
        long frameCount,
        out long appliedDelta)
    {
        appliedDelta = 0;
        if (delta == 0 || frames.Count == 0 || frameCount <= 0 || _markers.Count == 0)
        {
            return false;
        }

        var moving = frames.OrderBy(frame => frame).ToArray();
        var occupied = new HashSet<long>();
        foreach (var marker in _markers)
        {
            if (Array.BinarySearch(moving, marker.Frame) < 0)
            {
                occupied.Add(marker.Frame);
            }
        }

        appliedDelta = MarkerMoves.ResolveGroupDelta(moving, delta, occupied, frameCount);
        if (appliedDelta == 0)
        {
            return false;
        }

        var comments = new Dictionary<long, string>();
        foreach (var marker in _markers)
        {
            comments[marker.Frame] = marker.Comment;
        }

        var next = new List<MarkerSnapshot>(_markers.Count);
        var moved = new HashSet<long>();
        foreach (var frame in moving)
        {
            if (!comments.TryGetValue(frame, out var comment))
            {
                continue;
            }

            var dest = frame + appliedDelta;
            next.Add(new MarkerSnapshot(dest, comment));
            moved.Add(frame);
        }

        foreach (var marker in _markers)
        {
            if (!moved.Contains(marker.Frame))
            {
                next.Add(marker);
            }
        }

        ReplaceMarkers(next);
        return true;
    }

    public bool TrySetMarkerComment(long frame, string comment)
    {
        for (var i = 0; i < _markers.Count; i++)
        {
            if (_markers[i].Frame != frame)
            {
                continue;
            }

            _markers[i] = new MarkerSnapshot(frame, comment ?? string.Empty);
            return true;
        }

        return false;
    }

    public WaveMarker[] ToWaveMarkers()
    {
        if (_markers.Count == 0)
        {
            return [];
        }

        var result = new WaveMarker[_markers.Count];
        for (var i = 0; i < _markers.Count; i++)
        {
            result[i] = new WaveMarker(i + 1, _markers[i].Frame, _markers[i].Comment);
        }

        return result;
    }

    /// <summary>
    /// 既存マーカーと捨てマーカーをフレームで結合。同じフレームは既存コメントを優先し、
    /// 既存が空なら捨て側のコメントを採る。
    /// </summary>
    public static MarkerSnapshot[] MergeMarkers(
        IReadOnlyList<MarkerSnapshot> existing,
        IReadOnlyList<MarkerSnapshot> scratch)
    {
        if (scratch.Count == 0)
        {
            return existing.Count == 0 ? [] : existing.ToArray();
        }

        if (existing.Count == 0)
        {
            return scratch.OrderBy(marker => marker.Frame).ToArray();
        }

        var map = new SortedDictionary<long, string>();
        foreach (var marker in existing)
        {
            map[marker.Frame] = marker.Comment ?? string.Empty;
        }

        foreach (var marker in scratch)
        {
            if (map.TryGetValue(marker.Frame, out var comment))
            {
                if (string.IsNullOrEmpty(comment) && !string.IsNullOrEmpty(marker.Comment))
                {
                    map[marker.Frame] = marker.Comment;
                }
            }
            else
            {
                map[marker.Frame] = marker.Comment ?? string.Empty;
            }
        }

        var merged = new MarkerSnapshot[map.Count];
        var i = 0;
        foreach (var pair in map)
        {
            merged[i++] = new MarkerSnapshot(pair.Key, pair.Value);
        }

        return merged;
    }

    public long AdjacentMarkerFrame(long playhead, int direction)
    {
        if (_markers.Count == 0 || direction == 0)
        {
            return playhead;
        }

        if (direction > 0)
        {
            for (var i = 0; i < _markers.Count; i++)
            {
                if (_markers[i].Frame > playhead)
                {
                    return _markers[i].Frame;
                }
            }

            return _markers[0].Frame;
        }

        for (var i = _markers.Count - 1; i >= 0; i--)
        {
            if (_markers[i].Frame < playhead)
            {
                return _markers[i].Frame;
            }
        }

        return _markers[^1].Frame;
    }
}
