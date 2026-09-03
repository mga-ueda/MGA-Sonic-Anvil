using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Audio;

internal enum AudioFileKind
{
    Wave,
    Aiff,
    Mp3,
}

internal sealed class AudioDocument
{
    public AudioDocument(
        float[] interleaved,
        int sampleRate,
        int channels,
        int bitsPerSample,
        AudioFileKind sourceKind,
        string? sourcePath)
    {
        if (channels < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(channels));
        }

        if (sampleRate < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        Interleaved = interleaved;
        SampleRate = sampleRate;
        Channels = channels;
        BitsPerSample = bitsPerSample;
        SourceKind = sourceKind;
        SourcePath = sourcePath;
        Peaks = PeakPyramid.Build(interleaved, channels);
        RefreshFileBytes();
    }

    public float[] Interleaved { get; private set; }

    public int SampleRate { get; }

    public int Channels { get; }

    public int BitsPerSample { get; }

    public AudioFileKind SourceKind { get; private set; }

    public string? SourcePath { get; set; }

    public PeakPyramid Peaks { get; private set; }

    public bool IsDirty { get; private set; }

    public long FileBytes { get; private set; }

    public long FrameCount => Interleaved.Length / Channels;

    public double DurationSeconds => SampleRate <= 0 ? 0 : FrameCount / (double)SampleRate;

    public WaveSelection Selection { get; set; }

    /// <summary>マーカーを使わないサンプルループ範囲。未設定は Empty。</summary>
    public WaveSelection SampleLoop { get; set; }

    public long CursorFrame { get; set; }

    private readonly List<long> _markerFrames = [];
    private readonly Dictionary<long, string> _markerComments = [];
    private WaveMarker[] _markers = [];

    public IReadOnlyList<WaveMarker> Markers => _markers;

    public void MarkSaved(string path, AudioFileKind kind)
    {
        SourcePath = path;
        SourceKind = kind;
        IsDirty = false;
        RefreshFileBytes();
    }

    public void MarkUnsaved(string? sourcePath)
    {
        SourcePath = sourcePath;
        IsDirty = true;
    }

    public void ReplaceRange(long startFrame, float[] samples)
    {
        var start = checked((int)startFrame * Channels);
        if (start < 0 || start + samples.Length > Interleaved.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(startFrame));
        }

        Array.Copy(samples, 0, Interleaved, start, samples.Length);
        RebuildPeaks();
        IsDirty = true;
    }

    public float[] CopyRange(long startFrame, long frameCount)
    {
        var start = checked((int)startFrame * Channels);
        var length = checked((int)frameCount * Channels);
        var copy = new float[length];
        Array.Copy(Interleaved, start, copy, 0, length);
        return copy;
    }

    public void DeleteRange(long startFrame, long frameCount)
    {
        var start = checked((int)startFrame * Channels);
        var length = checked((int)frameCount * Channels);
        var next = new float[Interleaved.Length - length];
        Array.Copy(Interleaved, 0, next, 0, start);
        Array.Copy(Interleaved, start + length, next, start, Interleaved.Length - start - length);
        Interleaved = next;
        RebuildPeaks();
        IsDirty = true;
    }

    public void InsertRange(long startFrame, float[] samples)
    {
        var start = checked((int)startFrame * Channels);
        var next = new float[Interleaved.Length + samples.Length];
        Array.Copy(Interleaved, 0, next, 0, start);
        Array.Copy(samples, 0, next, start, samples.Length);
        Array.Copy(Interleaved, start, next, start + samples.Length, Interleaved.Length - start);
        Interleaved = next;
        RebuildPeaks();
        IsDirty = true;
    }

    public void ClampCursor()
    {
        CursorFrame = Math.Clamp(CursorFrame, 0, Math.Max(0, FrameCount));
        Selection = Selection.Clamp(FrameCount);
        SampleLoop = SampleLoop.Clamp(FrameCount);
        ClampMarkers();
    }

    public bool HasMarkerAt(long frame)
    {
        return _markerFrames.BinarySearch(frame) >= 0;
    }

    public bool TryGetRoleSpan(MarkerRole role, long playhead, out WaveSelection range)
    {
        range = WaveSelection.Empty;
        if (role == MarkerRole.None || FrameCount <= 0 || _markers.Length == 0)
        {
            return false;
        }

        WaveSelection? containing = null;
        WaveSelection? next = null;
        WaveSelection? first = null;
        for (var i = 0; i < _markers.Length; i++)
        {
            if (MarkerRoles.FromComment(_markers[i].Comment) != role)
            {
                continue;
            }

            var start = _markers[i].Frame;
            var end = i + 1 < _markers.Length ? _markers[i + 1].Frame : FrameCount;
            if (end <= start)
            {
                continue;
            }

            var span = new WaveSelection(start, end);
            first ??= span;
            if (playhead >= start && playhead < end)
            {
                containing = span;
                break;
            }

            if (next is null && start > playhead)
            {
                next = span;
            }
        }

        range = containing ?? next ?? first ?? WaveSelection.Empty;
        return !range.IsEmpty;
    }

    public WaveSelection MarkerSpanAt(long frame)
    {
        if (FrameCount <= 0)
        {
            return WaveSelection.Empty;
        }

        if (_markerFrames.Count == 0)
        {
            return new WaveSelection(0, FrameCount);
        }

        var index = _markerFrames.BinarySearch(frame);
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

        var start = left < 0 ? 0 : _markerFrames[left];
        var end = right >= _markerFrames.Count ? FrameCount : _markerFrames[right];
        return end <= start ? new WaveSelection(0, FrameCount) : new WaveSelection(start, end);
    }

    /// <summary>
    /// Double-click span: marker (and sample-loop) boundaries; expand to sample loop when inside it.
    /// </summary>
    public WaveSelection DoubleClickSpanAt(long frame)
    {
        if (FrameCount <= 0)
        {
            return WaveSelection.Empty;
        }

        var loop = SampleLoop;
        if (!loop.IsEmpty && frame >= loop.StartFrame && frame < loop.EndFrame)
        {
            return loop;
        }

        var cues = CueFrames();
        if (cues.Count == 0)
        {
            return new WaveSelection(0, FrameCount);
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
        var end = right >= cues.Count ? FrameCount : cues[right];
        var range = end <= start ? new WaveSelection(0, FrameCount) : new WaveSelection(start, end);
        if (!loop.IsEmpty
            && !range.IsEmpty
            && range.StartFrame >= loop.StartFrame
            && range.EndFrame <= loop.EndFrame)
        {
            return loop;
        }

        return range;
    }

    public long AdjacentMarkerFrame(long from, int direction)
    {
        return AdjacentInSorted(CueFrames(), from, direction);
    }

    public void SetSampleLoop(WaveSelection range)
    {
        SampleLoop = range.IsEmpty ? WaveSelection.Empty : range.Clamp(FrameCount);
    }

    public void ApplyDeleteToSampleLoop(long startFrame, long frameCount)
    {
        if (SampleLoop.IsEmpty || frameCount <= 0)
        {
            return;
        }

        var delEnd = startFrame + frameCount;
        var start = ShiftFrameThroughDelete(SampleLoop.StartFrame, startFrame, delEnd, frameCount, inclusiveEnd: false);
        var end = ShiftFrameThroughDelete(SampleLoop.EndFrame, startFrame, delEnd, frameCount, inclusiveEnd: true);
        SampleLoop = new WaveSelection(start, end).Clamp(FrameCount);
    }

    private List<long> CueFrames()
    {
        var loop = SampleLoop;
        if (loop.IsEmpty)
        {
            return _markerFrames;
        }

        var points = new List<long>(_markerFrames.Count + 2);
        points.AddRange(_markerFrames);
        var start = ClampMarkerFrame(loop.StartFrame);
        var end = ClampMarkerFrame(loop.EndFrame);
        if (_markerFrames.BinarySearch(start) < 0)
        {
            points.Add(start);
        }

        if (end != start && _markerFrames.BinarySearch(end) < 0)
        {
            points.Add(end);
        }

        if (points.Count != _markerFrames.Count)
        {
            points.Sort();
        }

        return points;
    }

    private long AdjacentInSorted(List<long> frames, long from, int direction)
    {
        if (frames.Count == 0)
        {
            return direction < 0 ? 0 : FrameCount;
        }

        var index = frames.BinarySearch(from);
        if (direction < 0)
        {
            if (index >= 0)
            {
                return index > 0 ? frames[index - 1] : 0;
            }

            var insert = ~index;
            return insert > 0 ? frames[insert - 1] : 0;
        }

        if (index >= 0)
        {
            return index + 1 < frames.Count ? frames[index + 1] : FrameCount;
        }

        var next = ~index;
        return next < frames.Count ? frames[next] : FrameCount;
    }

    private static long ShiftFrameThroughDelete(
        long frame,
        long deleteStart,
        long deleteEnd,
        long deletedCount,
        bool inclusiveEnd)
    {
        if (inclusiveEnd)
        {
            if (frame <= deleteStart)
            {
                return frame;
            }

            return frame <= deleteEnd ? deleteStart : frame - deletedCount;
        }

        if (frame < deleteStart)
        {
            return frame;
        }

        return frame < deleteEnd ? deleteStart : frame - deletedCount;
    }

    public bool TryAddMarker(long frame)
    {
        var next = ClampMarkerFrame(frame);
        if (HasMarkerAt(next))
        {
            return false;
        }

        _markerFrames.Add(next);
        CommitMarkers();
        IsDirty = true;
        return true;
    }

    public bool TryRemoveMarkers(IReadOnlyList<long> frames)
    {
        if (frames is null || frames.Count == 0)
        {
            return false;
        }

        var removed = false;
        foreach (var frame in frames)
        {
            var index = _markerFrames.BinarySearch(frame);
            if (index < 0)
            {
                continue;
            }

            _markerFrames.RemoveAt(index);
            _markerComments.Remove(frame);
            removed = true;
        }

        if (!removed)
        {
            return false;
        }

        CommitMarkers();
        IsDirty = true;
        return true;
    }

    public bool TryMoveMarkers(IReadOnlyList<long> frames, long delta, out long appliedDelta, bool markDirty = true)
    {
        appliedDelta = 0;
        if (frames is null || frames.Count == 0 || delta == 0)
        {
            return false;
        }

        var moving = new List<long>(frames.Count);
        foreach (var frame in frames)
        {
            if (HasMarkerAt(frame) && !moving.Contains(frame))
            {
                moving.Add(frame);
            }
        }

        if (moving.Count == 0)
        {
            return false;
        }

        moving.Sort();
        var occupied = new HashSet<long>();
        foreach (var frame in _markerFrames)
        {
            if (moving.BinarySearch(frame) < 0)
            {
                occupied.Add(frame);
            }
        }

        var applied = MarkerMoves.ResolveGroupDelta(moving, delta, occupied, Math.Max(0, FrameCount));
        if (applied == 0)
        {
            return false;
        }

        var payload = new (long From, long To, string Comment)[moving.Count];
        for (var i = 0; i < moving.Count; i++)
        {
            payload[i] = (moving[i], moving[i] + applied, MarkerCommentAt(moving[i]));
        }

        foreach (var item in payload)
        {
            var index = _markerFrames.BinarySearch(item.From);
            if (index >= 0)
            {
                _markerFrames.RemoveAt(index);
            }

            _markerComments.Remove(item.From);
        }

        foreach (var item in payload)
        {
            _markerFrames.Add(item.To);
            if (item.Comment.Length > 0)
            {
                _markerComments[item.To] = item.Comment;
            }
        }

        CommitMarkers();
        if (markDirty)
        {
            IsDirty = true;
        }

        appliedDelta = applied;
        return true;
    }

    public string MarkerCommentAt(long frame) =>
        _markerComments.TryGetValue(frame, out var comment) ? comment : string.Empty;

    public bool TrySetMarkerComment(long frame, string? comment)
    {
        if (!HasMarkerAt(frame))
        {
            return false;
        }

        var trimmed = MarkerRoles.Normalize(comment);
        if (MarkerCommentAt(frame) == trimmed)
        {
            return false;
        }

        if (trimmed.Length == 0)
        {
            _markerComments.Remove(frame);
        }
        else
        {
            _markerComments[frame] = trimmed;
        }

        CommitMarkers();
        IsDirty = true;
        return true;
    }

    public long[] SnapshotMarkerFrames() => _markerFrames.ToArray();

    public MarkerSnapshot[] SnapshotMarkers()
    {
        var snapshot = new MarkerSnapshot[_markerFrames.Count];
        for (var i = 0; i < _markerFrames.Count; i++)
        {
            var frame = _markerFrames[i];
            snapshot[i] = new MarkerSnapshot(frame, MarkerCommentAt(frame));
        }

        return snapshot;
    }

    public void ReplaceMarkerFrames(IReadOnlyList<long> frames, bool markDirty = true)
    {
        var snapshots = new MarkerSnapshot[frames?.Count ?? 0];
        for (var i = 0; i < snapshots.Length; i++)
        {
            snapshots[i] = new MarkerSnapshot(frames![i], string.Empty);
        }

        ReplaceMarkers(snapshots, markDirty);
    }

    public void ReplaceMarkers(IReadOnlyList<MarkerSnapshot> markers, bool markDirty = true)
    {
        _markerFrames.Clear();
        _markerComments.Clear();
        if (markers is not null)
        {
            foreach (var marker in markers)
            {
                var frame = ClampMarkerFrame(marker.Frame);
                _markerFrames.Add(frame);
                if (!string.IsNullOrWhiteSpace(marker.Comment))
                {
                    _markerComments[frame] = MarkerRoles.Normalize(marker.Comment);
                }
            }
        }

        CommitMarkers();
        if (markDirty)
        {
            IsDirty = true;
        }
    }

    public void ApplyDeleteToMarkers(long startFrame, long frameCount)
    {
        if (frameCount <= 0)
        {
            return;
        }

        var end = startFrame + frameCount;
        var nextFrames = new List<long>();
        var nextComments = new Dictionary<long, string>();
        foreach (var frame in _markerFrames)
        {
            if (frame >= startFrame && frame < end)
            {
                continue;
            }

            var dest = frame >= end ? frame - frameCount : frame;
            nextFrames.Add(dest);
            if (_markerComments.TryGetValue(frame, out var comment))
            {
                nextComments[dest] = comment;
            }
        }

        _markerFrames.Clear();
        _markerFrames.AddRange(nextFrames);
        _markerComments.Clear();
        foreach (var pair in nextComments)
        {
            _markerComments[pair.Key] = pair.Value;
        }

        CommitMarkers();
    }

    private void ClampMarkers()
    {
        var max = Math.Max(0, FrameCount);
        var changed = false;
        var moved = new Dictionary<long, string>();
        for (var i = 0; i < _markerFrames.Count; i++)
        {
            var clamped = Math.Clamp(_markerFrames[i], 0, max);
            if (clamped == _markerFrames[i])
            {
                continue;
            }

            if (_markerComments.Remove(_markerFrames[i], out var comment))
            {
                moved[clamped] = comment;
            }

            _markerFrames[i] = clamped;
            changed = true;
        }

        foreach (var pair in moved)
        {
            _markerComments[pair.Key] = pair.Value;
        }

        if (changed)
        {
            CommitMarkers();
        }
    }

    private long ClampMarkerFrame(long frame) => Math.Clamp(frame, 0, Math.Max(0, FrameCount));

    /// <summary>
    /// 先頭からの順番が番号であり、内部 ID も同じ数字にする。
    /// </summary>
    private void CommitMarkers()
    {
        if (_markerFrames.Count > 1)
        {
            _markerFrames.Sort();
            var write = 1;
            for (var i = 1; i < _markerFrames.Count; i++)
            {
                if (_markerFrames[i] == _markerFrames[write - 1])
                {
                    continue;
                }

                _markerFrames[write] = _markerFrames[i];
                write++;
            }

            if (write < _markerFrames.Count)
            {
                _markerFrames.RemoveRange(write, _markerFrames.Count - write);
            }
        }

        foreach (var frame in _markerComments.Keys.ToArray())
        {
            if (_markerFrames.BinarySearch(frame) < 0)
            {
                _markerComments.Remove(frame);
            }
        }

        var next = new WaveMarker[_markerFrames.Count];
        for (var i = 0; i < _markerFrames.Count; i++)
        {
            next[i] = new WaveMarker(i + 1, _markerFrames[i], MarkerCommentAt(_markerFrames[i]));
        }

        _markers = next;
    }

    private void RebuildPeaks() => Peaks = PeakPyramid.Build(Interleaved, Channels);

    public void RefreshFileBytes()
    {
        if (!string.IsNullOrWhiteSpace(SourcePath))
        {
            try
            {
                var info = new FileInfo(SourcePath);
                if (info.Exists)
                {
                    FileBytes = info.Length;
                    return;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        var bytesPerSample = Math.Max(1, (BitsPerSample + 7) / 8);
        FileBytes = FrameCount * (long)Channels * bytesPerSample;
    }
}

internal readonly record struct WaveMarker(int Id, long Frame, string Comment)
{
    public WaveMarker(int id, long frame)
        : this(id, frame, string.Empty)
    {
    }
}

internal readonly record struct MarkerSnapshot(long Frame, string Comment);

internal readonly record struct WaveSelection(long StartFrame, long EndFrame)
{
    public static WaveSelection Empty { get; } = new(0, 0);

    public bool IsEmpty => EndFrame <= StartFrame;

    public long Length => Math.Max(0, EndFrame - StartFrame);

    public WaveSelection Clamp(long frameCount)
    {
        if (frameCount <= 0 || IsEmpty)
        {
            return Empty;
        }

        var start = Math.Clamp(StartFrame, 0, frameCount);
        var end = Math.Clamp(EndFrame, 0, frameCount);
        return end <= start ? Empty : new WaveSelection(start, end);
    }

    public static WaveSelection FromPoints(long a, long b)
    {
        return a <= b ? new WaveSelection(a, b) : new WaveSelection(b, a);
    }
}
