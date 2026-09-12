using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Audio;

internal enum AudioFileKind
{
    Wave,
    Aiff,
    Mp3,
}

internal sealed partial class AudioDocument
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
        ChannelMask = 0;
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

    public void SpliceRange(long startFrame, long oldFrameCount, float[] samples)
    {
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
        IsDirty = true;
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

        var channelsChanged = channels != Channels;
        Interleaved = interleaved;
        SampleRate = sampleRate;
        Channels = channels;
        BitsPerSample = bitsPerSample;
        if (channelsChanged)
        {
            ChannelMask = 0;
        }

        RebuildPeaks();
        RefreshFileBytes();
        IsDirty = true;
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
