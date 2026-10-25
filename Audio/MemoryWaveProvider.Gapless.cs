using System.Globalization;
using System.Threading;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;
using NAudio.Wave;

namespace MgaSonicAnvil.Audio;

internal sealed partial class PlaybackSampleProvider
{
    /// <summary>
    /// 次の曲を音声スレッドでつなぐ。所有権は成功時に移る。通常速・非ループのストリーム再生だけ。
    /// </summary>
    public bool TryArmGapless(AudioStreamSource source, AudioDocument document)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(document);
        lock (_gate)
        {
            if (_stream is null)
            {
                return false;
            }

            ClearGaplessNoLock();
            var window = Mp3Gapless.ResolveWindow(document, source.FrameCount, playRange: null);
            var start = Mp3Gapless.ClampStart(0, window, source.FrameCount);
            if (start > 0)
            {
                source.SeekFrame(start, prebufferTimeoutMs: 0);
            }

            _gaplessStream = source;
            _gaplessDocument = document;
            _gaplessArmed = true;
            return true;
        }
    }

    public void ClearGaplessNext()
    {
        lock (_gate)
        {
            ClearGaplessNoLock();
        }
    }

    /// <summary>
    /// 再生用ストリームとギャップレス先読みを閉じる。停止後もファイルを掴み続けない。
    /// </summary>
    public void ReleaseStreamSource()
    {
        lock (_gate)
        {
            ClearGaplessNoLock();
            NoteGaplessAdvancedNoLock(null);
            DisposeStreamNoLock();
        }
    }

    public bool TryTakeGaplessAdvance(out AudioDocument? document)
    {
        if (_gaplessAdvancePending == 0)
        {
            document = null;
            return false;
        }

        lock (_gate)
        {
            document = _gaplessAdvanced;
            NoteGaplessAdvancedNoLock(null);
            return document is not null;
        }
    }

    private void NoteGaplessAdvancedNoLock(AudioDocument? document)
    {
        _gaplessAdvanced = document;
        _gaplessAdvancePending = document is null ? 0 : 1;
    }

    private void ClearGaplessNoLock()
    {
        _gaplessStream?.Dispose();
        _gaplessStream = null;
        _gaplessDocument = null;
        _gaplessArmed = false;
    }

    /// <summary>現在のストリーム終端で、先読みした次の曲へデバイスを止めずに切り替える。</summary>
    private bool TryConsumeGaplessNoLock()
    {
        if (_gaplessStream is null || _gaplessDocument is null || _loop || _scrubbing)
        {
            return false;
        }

        if (_seekFadePhase != 0)
        {
            return false;
        }

        if (Math.Abs(_playbackSpeed - 1d) > 1e-6)
        {
            return false;
        }

        var next = _gaplessStream;
        var document = _gaplessDocument;
        _gaplessStream = null;
        _gaplessDocument = null;
        _gaplessArmed = false;
        var outChannels = _outputChannels;
        var waveChannels = WaveFormat.Channels;
        var waveRate = WaveFormat.SampleRate;

        DisposeStreamNoLock(synchronous: false);
        _stream = next;
        _boundDocument = document;
        _samples = [];
        _channels = Math.Max(1, next.Channels);
        _sourceRate = Math.Max(1, next.SampleRate);
        var frames = Math.Max(0, next.FrameCount);
        var sampleCount = frames * (long)_channels;
        _usedSamples = sampleCount > int.MaxValue ? int.MaxValue : (int)sampleCount;
        if (_streamFrame.Length < _channels)
        {
            _streamFrame = new float[_channels];
        }

        if (_streamCached.Length < _channels)
        {
            _streamCached = new float[_channels];
        }

        InvalidateStreamCursorCacheNoLock();
        InvalidateStreamRecentNoLock();
        ClearStreamReverseBuf();
        var window = Mp3Gapless.ResolveWindow(document, frames, playRange: null);
        var start = Mp3Gapless.ClampStart(0, window, frames);
        if (start != next.Frame)
        {
            next.SeekFrame(start, prebufferTimeoutMs: 0);
        }

        _sourceFrame = start;
        _cursor = checked((int)start * Math.Max(1, _channels));
        _frameGain = null;
        _loopFadePreview = null;
        _silenceOnly = false;
        _flushFadeRemaining = 0;
        _flushFadeTotal = 0;
        ClearSeekFadeNoLock();
        _exitPlaying = false;
        _shuttlePrimed = false;
        _playExitLayer = false;
        ApplyOutputConfig();
        if (_outputChannels != outChannels
            || WaveFormat.Channels != waveChannels
            || WaveFormat.SampleRate != waveRate)
        {
            Ended = true;
            return false;
        }

        ApplyPlayWindowNoLock(window, loop: false);
        Ended = false;
        ResetMeterBuffers();
        lock (_monitorGate)
        {
            _meterSourceChannels = Math.Max(1, _channels);
            ResetMeterIntervalNoLock();
            ResetLoudnessNoLock();
        }

        NoteGaplessAdvancedNoLock(document);
        ForgetPlaybackPitchNoLock();
        return true;
    }

    /// <summary>実音が尽きたときだけ次曲へ進む。見積もり長さまでの無音は挟まない。</summary>
    private bool TryHandoffGapless(double playEndFrame) =>
        StreamExhausted(playEndFrame) && TryConsumeGaplessNoLock();

    private bool StreamExhausted(double playEndFrame)
    {
        if (_playbackSpeed < 0)
        {
            return false;
        }

        if (_stream is null)
        {
            return _sourceFrame + 1 >= playEndFrame;
        }

        if (_stream.IsDrained)
        {
            return true;
        }

        var srcCh = Math.Max(1, _channels);
        return _stream.Frame >= FrameCountOrStreamEnd(srcCh) || _sourceFrame + 1 >= playEndFrame;
    }

    private void ResumeStreamAfterReverseNoLock()
    {
        if (_stream is null)
        {
            return;
        }

        var end = Math.Max(1, FrameCountOrStreamEnd(Math.Max(1, _channels)));
        var frame = (long)Math.Clamp(Math.Floor(_sourceFrame), 0, end - 1);
        _stream.SeekFrame(frame, prebufferTimeoutMs: 0);
        InvalidateStreamCursorCacheNoLock();
    }
}
