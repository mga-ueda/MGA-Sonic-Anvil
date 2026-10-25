using System.Globalization;
using System.Threading;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;
using NAudio.Wave;

namespace MgaSonicAnvil.Audio;

internal sealed partial class PlaybackSampleProvider
{
    public void CopyMeterWindow(float[] left, float[] right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        var n = LevelMeterEngine.WindowFrames;
        if (left.Length < n || right.Length < n)
        {
            throw new ArgumentException("Meter window buffers must hold 1024 samples.");
        }

        lock (_monitorGate)
        {
            var count = _meterCount;
            var start = _meterWrite - count;
            if (start < 0)
            {
                start += n;
            }

            for (var i = 0; i < n; i++)
            {
                if (i < n - count)
                {
                    left[i] = 0;
                    right[i] = 0;
                    continue;
                }

                var src = (start + i - (n - count)) % n;
                left[i] = _meterL[src];
                right[i] = _meterR[src];
            }
        }
    }

    /// <summary>前回取得以降に出力した区間の Peak / RMS。新規サンプルがなければ false。</summary>
    public bool TakeMeterInterval(out float peakLeft, out float rmsLeft, out float peakRight, out float rmsRight)
    {
        Span<float> peaks = stackalloc float[ChannelLayout.MaxChannels];
        Span<float> rms = stackalloc float[ChannelLayout.MaxChannels];
        var ok = TakeMeterInterval(peaks, rms, out _);
        peakLeft = peaks[0];
        rmsLeft = rms[0];
        peakRight = peaks[1];
        rmsRight = rms[1];
        return ok;
    }

    public bool TakeMeterInterval(Span<float> peaks, Span<float> rms, out int channels)
    {
        lock (_monitorGate)
        {
            channels = Math.Max(1, _meterSourceChannels);
            var n = Math.Min(Math.Min(peaks.Length, rms.Length), channels);
            if (_intervalFrames <= 0)
            {
                peaks[..n].Clear();
                rms[..n].Clear();
                return false;
            }

            for (var ch = 0; ch < n; ch++)
            {
                peaks[ch] = _intervalPeak[ch];
                rms[ch] = LevelMeterEngine.SineReferencedAcRms(
                    _intervalSum[ch], _intervalSumSq[ch], _intervalFrames);
            }

            ResetMeterIntervalNoLock();
            return true;
        }
    }

    public void CopyMeterPlanar(float[] dest)
    {
        ArgumentNullException.ThrowIfNull(dest);
        var frames = LevelMeterEngine.WindowFrames;
        var stride = ChannelLayout.MaxChannels;
        if (dest.Length < frames * stride)
        {
            throw new ArgumentException("Planar meter buffer must hold 1024 frames.");
        }

        lock (_monitorGate)
        {
            var count = _meterCount;
            var start = _meterWrite - count;
            if (start < 0)
            {
                start += frames;
            }

            for (var i = 0; i < frames; i++)
            {
                var destAt = i * stride;
                if (i < frames - count)
                {
                    dest.AsSpan(destAt, stride).Clear();
                    continue;
                }

                var src = (start + i - (frames - count)) % frames;
                _meterPlanar.AsSpan(src * stride, stride).CopyTo(dest.AsSpan(destAt, stride));
            }
        }
    }

    public void CopyMeterPeaks(Span<float> peaks)
    {
        lock (_monitorGate)
        {
            var n = Math.Min(peaks.Length, Math.Max(1, _meterSourceChannels));
            var count = _meterCount;
            var window = LevelMeterEngine.WindowFrames;
            var start = _meterWrite - count;
            if (start < 0)
            {
                start += window;
            }

            for (var ch = 0; ch < n; ch++)
            {
                var peak = 0f;
                for (var i = 0; i < count; i++)
                {
                    var src = (start + i) % window;
                    var sample = Math.Abs(_meterPlanar[src * ChannelLayout.MaxChannels + ch]);
                    if (sample > peak)
                    {
                        peak = sample;
                    }
                }

                peaks[ch] = peak;
            }

            if (peaks.Length > n)
            {
                peaks[n..].Clear();
            }
        }
    }

    private void ResetMeterBuffers()
    {
        Array.Clear(_meterL);
        Array.Clear(_meterR);
        Array.Clear(_meterPlanar);
        _meterWrite = 0;
        _meterCount = 0;
    }

    private void ResetMeterIntervalNoLock()
    {
        Array.Clear(_intervalPeak);
        Array.Clear(_intervalSum);
        Array.Clear(_intervalSumSq);
        _intervalFrames = 0;
    }

    /// <summary>直近の出力サンプル（モノラルミックス）を destination の末尾詰めでコピーする。</summary>
    public int CopyRecentOutputSamples(float[] destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        lock (_monitorGate)
        {
            var available = (int)Math.Min(
                _monitorWriteCount,
                Math.Min(destination.Length, _monitorRing.Length));
            var start = _monitorWriteCount - available;
            for (var i = 0; i < available; i++)
            {
                destination[destination.Length - available + i] =
                    _monitorRing[(int)((start + i) % _monitorRing.Length)];
            }

            if (available < destination.Length)
            {
                Array.Clear(destination, 0, destination.Length - available);
            }

            return available;
        }
    }
}
