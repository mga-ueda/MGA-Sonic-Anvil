using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Audio;

internal enum AudioFileKind
{
    Wave,
    Aiff,
    Mp3,
    M4a,
}

internal sealed partial class AudioDocument
{
    public AudioDocument(
        float[] interleaved,
        int sampleRate,
        int channels,
        int bitsPerSample,
        AudioFileKind sourceKind,
        string? sourcePath,
        bool buildPeaks = true)
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
        ChannelMask = 0;
        SourceKind = sourceKind;
        SourcePath = sourcePath;
        Peaks = buildPeaks
            ? PeakPyramid.Build(interleaved, channels)
            : PeakPyramid.Empty;
        RefreshFileBytes();
        CommitFormat();
        CaptureFormatOrigin();
    }

    /// <summary>
    /// プレイヤーモードのリスト登録用。PCM は読まず、パスとファイルサイズだけ持つ。
    /// </summary>
    public static AudioDocument CreateDeferred(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var document = new AudioDocument(
            [],
            48000,
            1,
            16,
            AudioCodec.DetectKind(path),
            path)
        {
            IsDeferredLoad = true,
        };
        return document;
    }

    /// <summary>
    /// プレイヤーのストリーム再生用。PCM は持たず、長さとフォーマットだけ確定する。
    /// </summary>
    public void ActivateStreamPlayback(int sampleRate, int channels, int bitsPerSample, long frameCount)
    {
        if (sampleRate < 1 || channels < 1 || frameCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount));
        }

        SampleRate = sampleRate;
        Channels = channels;
        BitsPerSample = bitsPerSample <= 0 ? 16 : bitsPerSample;
        Interleaved = [];
        var samples = frameCount * (long)channels;
        _liveSampleCount = samples > int.MaxValue ? int.MaxValue : (int)samples;
        Peaks = PeakPyramid.Empty;
        IsDeferredLoad = false;
        IsStreamPlayback = true;
        RefreshFileBytes();
        CommitFormat();
        CaptureFormatOrigin();
    }

    public float[] Interleaved { get; private set; }

    public int SampleRate { get; private set; }

    public int Channels { get; private set; }

    public int BitsPerSample { get; private set; }

    /// <summary>元ファイルの dwChannelMask。0 は未指定。推測して埋めない。</summary>
    public int ChannelMask { get; private set; }

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

    /// <summary>MP3 / M4A は cue/smpl を持てないのでリージョンとサンプルループは置けない。</summary>
    public bool AllowsRegionsAndLoops =>
        SourceKind is not (AudioFileKind.Mp3 or AudioFileKind.M4a);

    public AudioFileKind SourceKind { get; private set; }

    /// <summary>作業コピー復元などで種類だけ戻す。保存済みパスは変えない。</summary>
    public void RestoreSourceKind(AudioFileKind kind) => SourceKind = kind;

    public string? SourcePath { get; set; }

    /// <summary>MP3 の ID3 APIC / M4A の covr。WAVE 等は持たない（表示はプレースホルダ）。</summary>
    public byte[]? Artwork { get; private set; }

    public bool HasArtwork => Artwork is { Length: > 0 };

    /// <summary>PCM を読まずに取ったタグ。未走査は <see cref="AudioFileTags.Unprobed"/>。</summary>
    public AudioFileTags Tags { get; private set; } = AudioFileTags.Unprobed;

    public void ApplyTags(AudioFileTags tags) => Tags = tags;

    public void SetArtwork(byte[]? bytes)
    {
        Artwork = bytes is { Length: > 0 } ? bytes.ToArray() : null;
    }

    public PeakPyramid Peaks { get; private set; }

    public bool IsDirty { get; private set; }

    /// <summary>F10 でリストへ載せただけで、まだ AudioCodec.Load していない。</summary>
    public bool IsDeferredLoad { get; private set; }

    /// <summary>プレイヤーでストリーム再生中。Interleaved は空で FrameCount だけ持つ。</summary>
    public bool IsStreamPlayback { get; private set; }

    /// <summary>サンプル内容が変わった回数。マーカー等のメタだけでは増えない。</summary>
    public int SampleRevision { get; private set; }

    /// <summary>未保存の録音タブ。保存すると続きは録れない。</summary>
    public bool CanContinueRecording { get; set; }

    public long FileBytes { get; private set; }

    /// <summary>MP3 / M4A のビットレート。WAVE / AIFF は 0。タグが無ければファイルサイズから平均する。</summary>
    public int CompressedBitRateKbps
    {
        get
        {
            if (SourceKind is not (AudioFileKind.Mp3 or AudioFileKind.M4a))
            {
                return 0;
            }

            if (Tags.BitRateKbps > 0)
            {
                return Tags.BitRateKbps;
            }

            if (FileBytes <= 0 || DurationSeconds <= 0)
            {
                return 0;
            }

            return Math.Max(1, (int)Math.Round(FileBytes * 8d / DurationSeconds / 1000d));
        }
    }

    public DateTime? FileLastWriteTime { get; private set; }

    public long FrameCount => Channels <= 0 ? 0 : SampleCount / Channels;

    internal int SampleCount => _liveSampleCount >= 0 ? _liveSampleCount : Interleaved.Length;

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

    private int _liveSampleCount = -1;

    private readonly List<WaveRegion> _regions = [];

    /// <summary>
    /// 名前なしのリージョン範囲。複数可。サンプルループと重複可。
    /// 名前付きの実体は <see cref="SnapshotRegions"/>。
    /// </summary>
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
        CanContinueRecording = false;
        RefreshFileBytes();
        CommitFormat();
    }

    public void MarkUnsaved(string? sourcePath)
    {
        SourcePath = sourcePath;
        IsDirty = true;
    }

    public void SetDirty(bool dirty) => IsDirty = dirty;

    public void ReplaceRange(long startFrame, float[] samples)
    {
        DiscardLiveCapacity();
        var start = checked((int)startFrame * Channels);
        if (start < 0 || start + samples.Length > Interleaved.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(startFrame));
        }

        Array.Copy(samples, 0, Interleaved, start, samples.Length);
        RebuildPeaks();
        NoteSamplesChanged();
        CaptureFormatOrigin();
    }

    public void SpliceRange(long startFrame, long oldFrameCount, float[] samples)
    {
        DiscardLiveCapacity();
        var start = checked((int)startFrame * Channels);
        var oldLength = checked((int)oldFrameCount * Channels);
        if (start < 0 || oldLength < 0 || start + oldLength > Interleaved.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(startFrame));
        }

        var next = new float[Interleaved.Length - oldLength + samples.Length];
        Array.Copy(Interleaved, 0, next, 0, start);
        Array.Copy(samples, 0, next, start, samples.Length);
        Array.Copy(
            Interleaved,
            start + oldLength,
            next,
            start + samples.Length,
            Interleaved.Length - start - oldLength);
        Interleaved = next;
        RebuildPeaks();
        NoteSamplesChanged();
        CaptureFormatOrigin();
    }

    public static long MapFrameThroughRangeStretch(
        long frame,
        long rangeStart,
        long oldLength,
        long newLength)
    {
        var rangeEnd = rangeStart + Math.Max(0, oldLength);
        if (frame < rangeStart)
        {
            return frame;
        }

        if (frame >= rangeEnd)
        {
            return frame + (newLength - oldLength);
        }

        if (oldLength <= 0)
        {
            return rangeStart;
        }

        return rangeStart + (long)Math.Round((frame - rangeStart) * (double)newLength / oldLength);
    }

    public float[] CopyRange(long startFrame, long frameCount)
    {
        var start = checked((int)startFrame * Channels);
        var length = checked((int)frameCount * Channels);
        if (start < 0 || length < 0 || start + length > SampleCount)
        {
            throw new ArgumentOutOfRangeException(nameof(startFrame));
        }

        var copy = new float[length];
        Array.Copy(Interleaved, start, copy, 0, length);
        return copy;
    }

    public void DeleteRange(long startFrame, long frameCount)
    {
        DiscardLiveCapacity();
        var start = checked((int)startFrame * Channels);
        var length = checked((int)frameCount * Channels);
        var next = new float[Interleaved.Length - length];
        Array.Copy(Interleaved, 0, next, 0, start);
        Array.Copy(Interleaved, start + length, next, start, Interleaved.Length - start - length);
        Interleaved = next;
        RebuildPeaks();
        NoteSamplesChanged();
        CaptureFormatOrigin();
    }

    public void InsertRange(long startFrame, float[] samples)
    {
        DiscardLiveCapacity();
        var start = checked((int)startFrame * Channels);
        var next = new float[Interleaved.Length + samples.Length];
        Array.Copy(Interleaved, 0, next, 0, start);
        Array.Copy(samples, 0, next, start, samples.Length);
        Array.Copy(Interleaved, start, next, start + samples.Length, Interleaved.Length - start);
        Interleaved = next;
        RebuildPeaks();
        NoteSamplesChanged();
        CaptureFormatOrigin();
    }

    public void ReplaceAudio(
        float[] interleaved,
        int sampleRate,
        int channels,
        int bitsPerSample,
        bool rebuildPeaks = true)
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

        var channelsChanged = channels != Channels;
        _liveSampleCount = -1;
        Interleaved = interleaved;
        SampleRate = sampleRate;
        Channels = channels;
        BitsPerSample = bitsPerSample;
        if (channelsChanged)
        {
            ChannelMask = 0;
        }

        if (rebuildPeaks)
        {
            RebuildPeaks();
        }

        RefreshFileBytes();
        NoteSamplesChanged();
    }

    /// <summary>startFrame 以降を tail で置き換える。長さは prefix + tail。録音のライブ上書き用。</summary>
    public void WriteLiveFrom(long startFrame, ReadOnlySpan<float> tail)
    {
        var start = checked((int)startFrame * Channels);
        if (start < 0 || start > SampleCount)
        {
            throw new ArgumentOutOfRangeException(nameof(startFrame));
        }

        var needed = checked(start + tail.Length);
        EnsureLiveCapacity(needed, start);
        if (!tail.IsEmpty)
        {
            tail.CopyTo(Interleaved.AsSpan(start, tail.Length));
        }

        _liveSampleCount = needed;
        NoteSamplesChanged();
        RefreshFileBytes();
    }

    public void AppendLiveSamples(ReadOnlySpan<float> extra)
    {
        if (extra.IsEmpty)
        {
            return;
        }

        var used = SampleCount;
        var needed = checked(used + extra.Length);
        EnsureLiveCapacity(needed, used);
        extra.CopyTo(Interleaved.AsSpan(used, extra.Length));
        _liveSampleCount = needed;
        NoteSamplesChanged();
        RefreshFileBytes();
    }

    public void RefreshPeaks() => RebuildPeaks();

    internal void ReplacePeaks(PeakPyramid peaks)
    {
        ArgumentNullException.ThrowIfNull(peaks);
        Peaks = peaks;
    }

    public void CommitLiveSamples(bool rebuildPeaks)
    {
        DiscardLiveCapacity();
        if (rebuildPeaks)
        {
            RebuildPeaks();
        }
    }

    public void SetChannelMask(int mask) => ChannelMask = mask;

    public void ClampCursor()
    {
        CursorFrame = Math.Clamp(CursorFrame, 0, Math.Max(0, FrameCount));
        Selection = Selection.Clamp(FrameCount);
        SampleLoop = SampleLoop.Clamp(FrameCount);
        SetRegions(_regions, markDirty: false);
        ClampMarkers();
    }

    private void NoteSamplesChanged()
    {
        unchecked
        {
            SampleRevision++;
        }

        IsDirty = true;
    }

    private void RebuildPeaks() => Peaks = PeakPyramid.Build(Interleaved, Channels, SampleCount);

    private void EnsureLiveCapacity(int needed, int keep)
    {
        if (Interleaved.Length >= needed)
        {
            if (_liveSampleCount < 0)
            {
                _liveSampleCount = keep;
            }

            return;
        }

        var cap = Math.Max(Interleaved.Length, 1);
        if (cap < 8192)
        {
            cap = 8192;
        }

        while (cap < needed)
        {
            if (cap > int.MaxValue / 2)
            {
                cap = needed;
                break;
            }

            cap *= 2;
        }

        var next = new float[cap];
        if (keep > 0)
        {
            Array.Copy(Interleaved, next, keep);
        }

        Interleaved = next;
        if (_liveSampleCount < 0)
        {
            _liveSampleCount = keep;
        }
    }

    private void DiscardLiveCapacity()
    {
        if (_liveSampleCount < 0)
        {
            return;
        }

        if (_liveSampleCount != Interleaved.Length)
        {
            var exact = new float[_liveSampleCount];
            if (_liveSampleCount > 0)
            {
                Array.Copy(Interleaved, exact, _liveSampleCount);
            }

            Interleaved = exact;
        }

        _liveSampleCount = -1;
    }

    /// <summary>作業内容のコピー。パスは持たない（未保存の複製用）。</summary>
    public AudioDocument CopyWorking()
    {
        var count = Math.Max(0, SampleCount);
        var samples = new float[count];
        if (count > 0)
        {
            Array.Copy(Interleaved, samples, count);
        }

        var copy = new AudioDocument(
            samples,
            SampleRate,
            Channels,
            BitsPerSample,
            SourceKind,
            sourcePath: null);
        copy.SetChannelMask(ChannelMask);
        copy.ReplaceMarkers(SnapshotMarkers(), markDirty: false, normalizeComments: false);
        copy.SetRegions(SnapshotRegions(), markDirty: false);
        copy.SetSampleLoop(SampleLoop, markDirty: false);
        copy.Selection = Selection;
        copy.CursorFrame = CursorFrame;
        copy.SetDirty(true);
        return copy;
    }

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
