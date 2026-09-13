namespace MgaSonicAnvil.Audio;

/// <summary>
/// 録音の Silent Skip。先頭の無音は捨て、しきい値を下回った時点から無音を書く。
/// pad まで書いたら止め、音が戻ったときには挟まない。末尾の長い無音は捨てる。
/// 一度閉じたあとは、しきい値より少し大きい音になるまで再開しない。
/// 再生には使わない。
/// </summary>
internal sealed class SilentSkipRecordGate
{
    private const float ResumeRatio = 2f;

    private int _channels = 1;
    private int _padFrames;
    private float _floor;
    private bool _enabled;
    private bool _hasWritten;
    private bool _closed;
    private int _silentWritten;
    private readonly List<RecordedSpan> _spans = [];
    private long _writtenFrames;
    private long _audioStart;
    private bool _hasAudio;
    private bool _inPad;
    private long _padStart;
    private bool _padCommitted;

    public bool HasWritten => _hasWritten;

    public void Reset(bool hasWritten)
    {
        _hasWritten = hasWritten;
        _closed = false;
        _silentWritten = 0;
        _spans.Clear();
        _writtenFrames = 0;
        _audioStart = 0;
        _hasAudio = false;
        _inPad = false;
        _padStart = 0;
        _padCommitted = false;
    }

    public void Configure(bool enabled, float thresholdLinear, int padFrames, int channels)
    {
        _enabled = enabled;
        _floor = thresholdLinear;
        _padFrames = Math.Max(0, padFrames);
        _channels = Math.Max(1, channels);
        if (_silentWritten > _padFrames)
        {
            _silentWritten = _padFrames;
        }
    }

    /// <summary>1 フレームを処理し、dest に書いたサンプル数を返す。</summary>
    public int ProcessFrame(ReadOnlySpan<float> frame, Span<float> dest)
    {
        if (frame.Length < _channels)
        {
            return 0;
        }

        if (!_enabled)
        {
            if (dest.Length < _channels)
            {
                return 0;
            }

            frame[.._channels].CopyTo(dest);
            _hasWritten = true;
            _closed = false;
            _silentWritten = 0;
            NoteAudioFrame();
            return _channels;
        }

        if (IsSilent(frame[.._channels]))
        {
            if (!_hasWritten)
            {
                return 0;
            }

            if (_padFrames <= 0 || _silentWritten >= _padFrames)
            {
                _closed = true;
                return 0;
            }

            if (dest.Length < _channels)
            {
                return 0;
            }

            dest[.._channels].Clear();
            _silentWritten++;
            if (_silentWritten >= _padFrames)
            {
                _closed = true;
            }

            NotePadFrame();
            return _channels;
        }

        if (dest.Length < _channels)
        {
            return 0;
        }

        frame[.._channels].CopyTo(dest);
        _hasWritten = true;
        _closed = false;
        _silentWritten = 0;
        NoteAudioFrame();
        return _channels;
    }

    /// <summary>停止時。すでに書いた長い末尾の pad を捨てるサンプル数。</summary>
    public int CloseTake()
    {
        if (!_enabled || _padFrames <= 0 || _silentWritten < _padFrames)
        {
            return 0;
        }

        var discard = _padFrames * _channels;
        _silentWritten = 0;
        _writtenFrames = Math.Max(0, _writtenFrames - _padFrames);
        if (_padCommitted
            && _spans.Count > 0
            && _spans[^1].Silent
            && _spans[^1].EndFrame - _spans[^1].StartFrame == _padFrames)
        {
            _spans.RemoveAt(_spans.Count - 1);
        }

        _inPad = false;
        _padCommitted = false;
        return discard;
    }

    /// <summary>pad を挟み切った区間。短い谷は可聴に含める。末尾の捨てた pad は含まない。</summary>
    public RecordedSpan[] SnapshotWrittenSpans()
    {
        if (_hasAudio && _writtenFrames > _audioStart)
        {
            var copy = new RecordedSpan[_spans.Count + 1];
            _spans.CopyTo(copy);
            copy[^1] = new RecordedSpan(_audioStart, _writtenFrames, Silent: false);
            return copy;
        }

        return [.. _spans];
    }

    private void NoteAudioFrame()
    {
        if (_inPad)
        {
            _inPad = false;
            _padCommitted = false;
        }

        if (!_hasAudio)
        {
            _audioStart = _writtenFrames;
            _hasAudio = true;
        }

        _writtenFrames++;
    }

    private void NotePadFrame()
    {
        if (!_inPad)
        {
            _inPad = true;
            _padStart = _writtenFrames;
            _padCommitted = false;
        }

        _writtenFrames++;
        if (_padCommitted || _padFrames <= 0 || _writtenFrames - _padStart < _padFrames)
        {
            return;
        }

        if (_hasAudio && _padStart > _audioStart)
        {
            _spans.Add(new RecordedSpan(_audioStart, _padStart, Silent: false));
        }

        _spans.Add(new RecordedSpan(_padStart, _writtenFrames, Silent: true));
        _hasAudio = false;
        _padCommitted = true;
    }

    private bool IsSilent(ReadOnlySpan<float> frame)
    {
        var floor = _closed || _silentWritten > 0
            ? Math.Min(1f, _floor * ResumeRatio)
            : _floor;
        return SilentSkip.IsSpanSilent(frame, floor);
    }
}
