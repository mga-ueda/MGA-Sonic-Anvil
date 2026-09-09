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
        CommitFormat();
        CaptureFormatOrigin();
    }

    public float[] Interleaved { get; private set; }

    public int SampleRate { get; private set; }

    public int Channels { get; private set; }

    public int BitsPerSample { get; private set; }

    public bool SampleRateEdited => SampleRate != _committedSampleRate;

    public bool BitDepthEdited => BitsPerSample != _committedBitsPerSample;

    public bool ChannelsEdited => Channels != _committedChannels;

    public bool FormatEdited => SampleRateEdited || BitDepthEdited || ChannelsEdited;

    public bool FileSizeEdited => EstimatedFileBytes != CommittedFileBytes;

    public int CommittedSampleRate => _committedSampleRate;

    public int CommittedBitsPerSample => _committedBitsPerSample;

    public int CommittedChannels => _committedChannels;

    public long CommittedFileBytes => _committedFileBytes;

    public long EstimatedFileBytes =>
        EstimateFileBytes(
            _committedFileBytes,
            _committedFrameCount,
            _committedChannels,
            _committedBitsPerSample,
            FrameCount,
            Channels,
            BitsPerSample);

    /// <summary>MP3 は cue/smpl を持てないのでリージョンとサンプルループは置けない。</summary>
    public bool AllowsRegionsAndLoops => SourceKind != AudioFileKind.Mp3;

    public AudioFileKind SourceKind { get; private set; }

    public string? SourcePath { get; set; }

    public PeakPyramid Peaks { get; private set; }

    public bool IsDirty { get; private set; }

    public long FileBytes { get; private set; }

    public DateTime? FileLastWriteTime { get; private set; }

    public long FrameCount => Interleaved.Length / Channels;

    public double DurationSeconds => SampleRate <= 0 ? 0 : FrameCount / (double)SampleRate;

    public WaveSelection Selection { get; set; }

    /// <summary>マーカーを使わないサンプルループ範囲。未設定は Empty。</summary>
    public WaveSelection SampleLoop { get; set; }

    private int _committedSampleRate;
    private int _committedBitsPerSample;
    private int _committedChannels;
    private long _committedFrameCount;
    private long _committedFileBytes;
    private float[] _formatOriginSamples = [];
    private int _formatOriginSampleRate;
    private int _formatOriginChannels;
    private int _formatOriginBits;

    private readonly List<WaveRegion> _regions = [];

    /// <summary>マーカーを使わないリージョン範囲。複数可。サンプルループと重複可。</summary>
    public IReadOnlyList<WaveSelection> Regions
    {
        get
        {
            if (_regions.Count == 0)
            {
                return [];
            }

            var ranges = new WaveSelection[_regions.Count];
            for (var i = 0; i < _regions.Count; i++)
            {
                ranges[i] = _regions[i].Range;
            }

            return ranges;
        }
    }

    /// <summary>先頭のリージョン。未設定は Empty。</summary>
    public WaveSelection Region => _regions.Count == 0 ? WaveSelection.Empty : _regions[0].Range;

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
        CommitFormat();
    }

    public void CommitFormat()
    {
        _committedSampleRate = SampleRate;
        _committedBitsPerSample = BitsPerSample;
        _committedChannels = Channels;
        _committedFrameCount = FrameCount;
        _committedFileBytes = FileBytes;
    }

    public static long EstimatePcmPayloadBytes(long frames, int channels, int bitsPerSample)
    {
        var bytesPerSample = Math.Max(1, (bitsPerSample + 7) / 8);
        return Math.Max(0, frames) * (long)Math.Max(0, channels) * bytesPerSample;
    }

    public static long EstimateFileBytes(
        long committedFileBytes,
        long committedFrames,
        int committedChannels,
        int committedBitsPerSample,
        long frames,
        int channels,
        int bitsPerSample)
    {
        var committedPcm = EstimatePcmPayloadBytes(committedFrames, committedChannels, committedBitsPerSample);
        var currentPcm = EstimatePcmPayloadBytes(frames, channels, bitsPerSample);
        if (committedPcm <= 0)
        {
            return currentPcm;
        }

        return currentPcm + Math.Max(0, committedFileBytes - committedPcm);
    }

    public long EstimateFrameCountForRate(int destRate)
    {
        if (destRate == SampleRate || destRate < 1 || SampleRate < 1)
        {
            return FrameCount;
        }

        return Math.Max(0, (long)Math.Round(FrameCount * (double)destRate / SampleRate));
    }

    public long EstimateFileBytesFor(int sampleRate, int bitsPerSample, int channels) =>
        EstimateFileBytes(
            _committedFileBytes,
            _committedFrameCount,
            _committedChannels,
            _committedBitsPerSample,
            EstimateFrameCountForRate(sampleRate),
            channels,
            bitsPerSample);

    public void MarkUnsaved(string? sourcePath)
    {
        SourcePath = sourcePath;
        IsDirty = true;
    }

    public void SetDirty(bool dirty) => IsDirty = dirty;

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
        CaptureFormatOrigin();
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
        CaptureFormatOrigin();
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
        CaptureFormatOrigin();
    }

    public void ReplaceAudio(
        float[] interleaved,
        int sampleRate,
        int channels,
        int bitsPerSample)
    {
        if (channels < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(channels));
        }

        if (sampleRate < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        if (bitsPerSample < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(bitsPerSample));
        }

        Interleaved = interleaved;
        SampleRate = sampleRate;
        Channels = channels;
        BitsPerSample = bitsPerSample;
        RebuildPeaks();
        RefreshFileBytes();
        IsDirty = true;
    }

    public float[] FormatOriginSamples => _formatOriginSamples;

    public int FormatOriginSampleRate => _formatOriginSampleRate;

    public int FormatOriginChannels => _formatOriginChannels;

    public int FormatOriginBitsPerSample => _formatOriginBits;

    public long FormatOriginFrameCount =>
        _formatOriginChannels <= 0 ? 0 : _formatOriginSamples.Length / _formatOriginChannels;

    /// <summary>フェード等の中身の編集後。以降のレート／ビット変換の起点になる。</summary>
    public void CaptureFormatOrigin()
    {
        _formatOriginSamples = Interleaved;
        _formatOriginSampleRate = SampleRate;
        _formatOriginChannels = Channels;
        _formatOriginBits = BitsPerSample;
    }

    public void SetFormatOrigin(float[] samples, int sampleRate, int channels, int bitsPerSample)
    {
        _formatOriginSamples = samples;
        _formatOriginSampleRate = sampleRate;
        _formatOriginChannels = channels;
        _formatOriginBits = bitsPerSample;
    }

    /// <summary>直近の中身編集（または読み込み）時点の PCM から、指定フォーマットを作る。</summary>
    public float[] MaterializeFormat(
        int destRate,
        int destBits,
        int destChannels,
        IProgress<double>? progress = null)
    {
        var samples = _formatOriginSamples;
        var rate = Math.Max(1, _formatOriginSampleRate);
        var channels = Math.Max(1, _formatOriginChannels);
        destChannels = Math.Max(1, destChannels);
        destRate = Math.Max(1, destRate);
        if (channels != destChannels)
        {
            samples = FormatConvert.Remix(samples, channels, destChannels);
            channels = destChannels;
        }

        if (rate != destRate)
        {
            samples = FormatConvert.Resample(samples, channels, rate, destRate, progress);
        }

        if (destBits < _formatOriginBits)
        {
            samples = FormatConvert.Quantize(samples, destBits);
        }

        return samples;
    }

    public void ClampCursor()
    {
        CursorFrame = Math.Clamp(CursorFrame, 0, Math.Max(0, FrameCount));
        Selection = Selection.Clamp(FrameCount);
        SampleLoop = SampleLoop.Clamp(FrameCount);
        SetRegions(_regions, markDirty: false);
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

        if (TryGetRegionAt(frame, out var region))
        {
            return region;
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

        if (TryGetRegionContaining(range, out var enclosing))
        {
            return enclosing;
        }

        return range;
    }

    public long AdjacentMarkerFrame(long from, int direction)
    {
        return AdjacentInSorted(CueFrames(), from, direction);
    }

    public void SetSampleLoop(WaveSelection range, bool markDirty = true)
    {
        var next = range.IsEmpty ? WaveSelection.Empty : range.Clamp(FrameCount);
        if (!AllowsRegionsAndLoops && !next.IsEmpty)
        {
            return;
        }

        if (next == SampleLoop)
        {
            return;
        }

        SampleLoop = next;
        if (markDirty)
        {
            IsDirty = true;
        }
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

    public void ApplyInsertToSampleLoop(long startFrame, long frameCount)
    {
        if (SampleLoop.IsEmpty || frameCount <= 0)
        {
            return;
        }

        var start = SampleLoop.StartFrame >= startFrame
            ? SampleLoop.StartFrame + frameCount
            : SampleLoop.StartFrame;
        var end = SampleLoop.EndFrame > startFrame
            ? SampleLoop.EndFrame + frameCount
            : SampleLoop.EndFrame;
        SampleLoop = new WaveSelection(start, end).Clamp(FrameCount);
    }

    public WaveRegion[] SnapshotRegions() => _regions.Count == 0 ? [] : [.. _regions];

    public string RegionName(WaveSelection range)
    {
        foreach (var region in _regions)
        {
            if (region.Range == range)
            {
                return region.Name;
            }
        }

        return string.Empty;
    }

    public bool TrySetRegionName(WaveSelection range, string? name)
    {
        for (var i = 0; i < _regions.Count; i++)
        {
            if (_regions[i].Range != range)
            {
                continue;
            }

            var next = MarkerRoles.Normalize(name);
            if (_regions[i].Name == next)
            {
                return false;
            }

            _regions[i] = _regions[i] with { Name = next };
            IsDirty = true;
            return true;
        }

        return false;
    }

    /// <summary>開始時刻順。マーカー番号の続きから振る。無い範囲は 0。</summary>
    public int RegionNumber(WaveSelection range)
    {
        for (var i = 0; i < _regions.Count; i++)
        {
            if (_regions[i].Range == range)
            {
                return _markers.Length + i + 1;
            }
        }

        return 0;
    }

    public void SetRegion(WaveSelection range, bool markDirty = true) =>
        SetRegions(range.IsEmpty ? [] : [range], markDirty);

    public void SetRegions(IReadOnlyList<WaveSelection> ranges, bool markDirty = true)
    {
        if (ranges is null)
        {
            SetRegions(Array.Empty<WaveRegion>(), markDirty);
            return;
        }

        var named = new WaveRegion[ranges.Count];
        for (var i = 0; i < ranges.Count; i++)
        {
            named[i] = new WaveRegion(ranges[i], RegionName(ranges[i]));
        }

        SetRegions(named, markDirty);
    }

    public void SetRegions(IReadOnlyList<WaveRegion> regions, bool markDirty = true)
    {
        var next = NormalizeRegions(regions);
        if (!AllowsRegionsAndLoops && next.Count > 0)
        {
            return;
        }

        if (SameRegions(_regions, next))
        {
            return;
        }

        _regions.Clear();
        _regions.AddRange(next);
        if (markDirty)
        {
            IsDirty = true;
        }
    }

    public bool TryGetRegionAt(long frame, out WaveSelection region)
    {
        region = WaveSelection.Empty;
        var found = false;
        foreach (var item in _regions)
        {
            if (!item.Range.ContainsFrame(frame))
            {
                continue;
            }

            if (!found || item.Range.Length < region.Length)
            {
                region = item.Range;
                found = true;
            }
        }

        return found;
    }

    public bool TryGetRegionContaining(WaveSelection range, out WaveSelection region)
    {
        region = WaveSelection.Empty;
        if (range.IsEmpty)
        {
            return false;
        }

        var found = false;
        foreach (var item in _regions)
        {
            if (range.StartFrame < item.StartFrame || range.EndFrame > item.EndFrame)
            {
                continue;
            }

            if (!found || item.Range.Length < region.Length)
            {
                region = item.Range;
                found = true;
            }
        }

        return found;
    }

    public void ApplyDeleteToRegion(long startFrame, long frameCount)
    {
        if (_regions.Count == 0 || frameCount <= 0)
        {
            return;
        }

        var delEnd = startFrame + frameCount;
        var next = new List<WaveRegion>(_regions.Count);
        foreach (var item in _regions)
        {
            var start = ShiftFrameThroughDelete(item.StartFrame, startFrame, delEnd, frameCount, inclusiveEnd: false);
            var end = ShiftFrameThroughDelete(item.EndFrame, startFrame, delEnd, frameCount, inclusiveEnd: true);
            var shifted = new WaveSelection(start, end).Clamp(FrameCount);
            if (!shifted.IsEmpty)
            {
                next.Add(new WaveRegion(shifted, item.Name));
            }
        }

        SetRegions(next, markDirty: false);
    }

    public void ApplyInsertToRegion(long startFrame, long frameCount)
    {
        if (_regions.Count == 0 || frameCount <= 0)
        {
            return;
        }

        var next = new List<WaveRegion>(_regions.Count);
        foreach (var item in _regions)
        {
            var start = item.StartFrame >= startFrame
                ? item.StartFrame + frameCount
                : item.StartFrame;
            var end = item.EndFrame > startFrame
                ? item.EndFrame + frameCount
                : item.EndFrame;
            var shifted = new WaveSelection(start, end).Clamp(FrameCount);
            if (!shifted.IsEmpty)
            {
                next.Add(new WaveRegion(shifted, item.Name));
            }
        }

        SetRegions(next, markDirty: false);
    }

    private List<WaveRegion> NormalizeRegions(IEnumerable<WaveRegion> regions)
    {
        var list = new List<WaveRegion>();
        foreach (var region in regions)
        {
            var next = region.Clamp(FrameCount);
            if (next.IsEmpty)
            {
                continue;
            }

            var exists = false;
            foreach (var item in list)
            {
                if (item.Range == next.Range)
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
            {
                list.Add(next);
            }
        }

        list.Sort(static (a, b) =>
        {
            var byStart = a.StartFrame.CompareTo(b.StartFrame);
            return byStart != 0 ? byStart : a.EndFrame.CompareTo(b.EndFrame);
        });
        return list;
    }

    private static bool SameRegions(IReadOnlyList<WaveRegion> left, IReadOnlyList<WaveRegion> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    private List<long> CueFrames()
    {
        var extras = new List<long>(4);
        AddRangeBounds(extras, SampleLoop);
        foreach (var region in _regions)
        {
            AddRangeBounds(extras, region.Range);
        }
        if (extras.Count == 0)
        {
            return _markerFrames;
        }

        var points = new List<long>(_markerFrames.Count + extras.Count);
        points.AddRange(_markerFrames);
        foreach (var frame in extras)
        {
            if (_markerFrames.BinarySearch(frame) < 0 && !points.Contains(frame))
            {
                points.Add(frame);
            }
        }

        if (points.Count != _markerFrames.Count)
        {
            points.Sort();
        }

        return points;
    }

    private void AddRangeBounds(List<long> frames, WaveSelection range)
    {
        if (range.IsEmpty)
        {
            return;
        }

        frames.Add(ClampMarkerFrame(range.StartFrame));
        var end = ClampMarkerFrame(range.EndFrame);
        if (end != frames[^1])
        {
            frames.Add(end);
        }
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

    public bool TryMoveRegions(IReadOnlyList<WaveSelection> origins, long delta, out long appliedDelta, bool markDirty = true)
    {
        appliedDelta = 0;
        if (origins is null || origins.Count == 0)
        {
            return false;
        }

        var moves = new List<RangeEdgeMove>(origins.Count);
        foreach (var origin in origins)
        {
            if (!origin.IsEmpty)
            {
                moves.Add(RangeEdgeMove.Translate(origin));
            }
        }

        return TryMoveRegionEdges(moves, delta, out appliedDelta, markDirty);
    }

    public bool TryMoveRegionEdges(IReadOnlyList<RangeEdgeMove> moves, long delta, out long appliedDelta, bool markDirty = true)
    {
        appliedDelta = 0;
        if (moves is null || moves.Count == 0 || delta == 0)
        {
            return false;
        }

        var byRange = new Dictionary<WaveSelection, RangeEdgeMove>();
        foreach (var move in moves)
        {
            if (move.IsEmpty || !HasRegionRange(move.Range))
            {
                continue;
            }

            if (byRange.TryGetValue(move.Range, out var existing))
            {
                byRange[move.Range] = existing with
                {
                    Start = existing.Start || move.Start,
                    End = existing.End || move.End,
                };
            }
            else
            {
                byRange[move.Range] = move;
            }
        }

        if (byRange.Count == 0)
        {
            return false;
        }

        var valid = byRange.Values.ToList();
        appliedDelta = TimelineMoves.ClampEdgeMoves(valid, delta, FrameCount);
        if (appliedDelta == 0)
        {
            return false;
        }

        var next = new List<WaveRegion>(_regions.Count);
        foreach (var region in _regions)
        {
            next.Add(byRange.TryGetValue(region.Range, out var move)
                ? region.WithRange(TimelineMoves.ShiftEdges(region.Range, move.Start, move.End, appliedDelta))
                : region);
        }

        SetRegions(next, markDirty);
        return true;
    }

    private bool HasRegionRange(WaveSelection range)
    {
        foreach (var region in _regions)
        {
            if (region.Range == range)
            {
                return true;
            }
        }

        return false;
    }

    public bool TryMoveSampleLoop(long delta, out long appliedDelta, bool markDirty = true) =>
        TryMoveSampleLoopEdges(start: true, end: true, delta, out appliedDelta, markDirty);

    public bool TryMoveSampleLoopEdges(bool start, bool end, long delta, out long appliedDelta, bool markDirty = true)
    {
        appliedDelta = 0;
        if (SampleLoop.IsEmpty || delta == 0 || (!start && !end))
        {
            return false;
        }

        var move = new RangeEdgeMove(SampleLoop, start, end);
        appliedDelta = TimelineMoves.ClampEdgeMoves([move], delta, FrameCount);
        if (appliedDelta == 0)
        {
            return false;
        }

        SetSampleLoop(TimelineMoves.ShiftEdges(SampleLoop, start, end, appliedDelta), markDirty);
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

    public MarkerSnapshot[] SnapshotMarkersInRange(WaveSelection range)
    {
        range = range.Clamp(FrameCount);
        if (range.IsEmpty || _markerFrames.Count == 0)
        {
            return [];
        }

        var copied = new List<MarkerSnapshot>();
        foreach (var frame in _markerFrames)
        {
            if (frame < range.StartFrame || frame >= range.EndFrame)
            {
                continue;
            }

            copied.Add(new MarkerSnapshot(frame - range.StartFrame, MarkerCommentAt(frame)));
        }

        return copied.ToArray();
    }

    public WaveRegion[] SnapshotExactRegions(WaveSelection range)
    {
        range = range.Clamp(FrameCount);
        if (range.IsEmpty)
        {
            return [];
        }

        foreach (var region in _regions)
        {
            if (region.Range == range)
            {
                return [new WaveRegion(new WaveSelection(0, range.Length), region.Name)];
            }
        }

        return [];
    }

    public void ApplyPastedRegions(long insertFrame, IReadOnlyList<WaveRegion> relative)
    {
        if (relative is null || relative.Count == 0)
        {
            return;
        }

        var next = new List<WaveRegion>(_regions);
        var added = false;
        foreach (var region in relative)
        {
            var dest = new WaveSelection(
                insertFrame + region.StartFrame,
                insertFrame + region.EndFrame).Clamp(FrameCount);
            if (dest.IsEmpty || HasRegionRange(dest))
            {
                continue;
            }

            next.Add(new WaveRegion(dest, region.Name));
            added = true;
        }

        if (!added)
        {
            return;
        }

        SetRegions(next, markDirty: true);
    }

    public void ApplyPastedMarkers(long insertFrame, IReadOnlyList<MarkerSnapshot> relative)
    {
        if (relative is null || relative.Count == 0)
        {
            return;
        }

        var added = false;
        foreach (var marker in relative)
        {
            var dest = ClampMarkerFrame(insertFrame + marker.Frame);
            if (HasMarkerAt(dest))
            {
                continue;
            }

            _markerFrames.Add(dest);
            if (!string.IsNullOrWhiteSpace(marker.Comment))
            {
                _markerComments[dest] = MarkerRoles.Normalize(marker.Comment);
            }

            added = true;
        }

        if (!added)
        {
            return;
        }

        CommitMarkers();
        IsDirty = true;
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

    public void ApplyInsertToMarkers(long startFrame, long frameCount)
    {
        if (frameCount <= 0)
        {
            return;
        }

        var nextFrames = new List<long>(_markerFrames.Count);
        var nextComments = new Dictionary<long, string>();
        foreach (var frame in _markerFrames)
        {
            var dest = frame >= startFrame ? frame + frameCount : frame;
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
                    FileLastWriteTime = info.LastWriteTime;
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
        FileLastWriteTime = null;
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

internal readonly record struct WaveRegion(long StartFrame, long EndFrame, string Name)
{
    public WaveRegion(WaveSelection range, string? name = null)
        : this(range.StartFrame, range.EndFrame, name ?? string.Empty)
    {
    }

    public static WaveRegion Empty { get; } = new(0, 0, string.Empty);

    public WaveSelection Range => new(StartFrame, EndFrame);

    public bool IsEmpty => EndFrame <= StartFrame;

    public WaveRegion Clamp(long frameCount)
    {
        var range = Range.Clamp(frameCount);
        return range.IsEmpty ? Empty : new WaveRegion(range, Name);
    }

    public WaveRegion WithRange(WaveSelection range) =>
        range.IsEmpty ? Empty : new WaveRegion(range, Name);
}

internal readonly record struct WaveSelection(long StartFrame, long EndFrame)
{
    public static WaveSelection Empty { get; } = new(0, 0);

    public bool IsEmpty => EndFrame <= StartFrame;

    public bool ContainsFrame(long frame) => !IsEmpty && frame >= StartFrame && frame < EndFrame;

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
