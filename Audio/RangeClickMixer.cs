namespace MgaSonicAnvil.Audio;

/// <summary>
/// ソースフレームのトリガでクリック波形を発火し、出力フレームごとに 1 サンプル進める。
/// 拍子が分かれば小節頭は High、それ以外は Low。
/// </summary>
internal sealed class RangeClickMixer
{
    /// <summary>本体より少し下げて、拍の位置だけが分かるようにする。</summary>
    internal const double OutputGainDb = -20;
    internal static readonly float OutputGainLinear = (float)Math.Pow(10, OutputGainDb / 20d);

    private float[] _nativeLow = [];
    private float[] _nativeHigh = [];
    private int _nativeLowRate = 1;
    private int _nativeHighRate = 1;
    private float[] _playLow = [];
    private float[] _playHigh = [];
    private int _deviceRate = 48000;
    private long[] _triggers = [];
    private int _groupSize;
    private int _voice = -1;
    private bool _high;
    private long _lastSource = long.MinValue;
    private bool _enabled;

    public bool Enabled => _enabled;

    public bool HasSample => _playLow.Length > 0;

    public bool HasHighSample => _playHigh.Length > 0;

    public void SetSample(float[] mono, int sampleRate) =>
        SetSamples(mono, mono, sampleRate);

    public void SetSamples(float[] low, float[] high, int sampleRate)
    {
        SetSamples(low, sampleRate, high, sampleRate);
    }

    public void SetSamples(float[] low, int lowRate, float[] high, int highRate)
    {
        _nativeLow = low ?? [];
        _nativeHigh = high ?? [];
        _nativeLowRate = Math.Max(1, lowRate);
        _nativeHighRate = Math.Max(1, highRate);
        RebuildPlay();
        ResetVoice();
    }

    public void SetLowSample(float[] mono, int sampleRate)
    {
        _nativeLow = mono ?? [];
        _nativeLowRate = Math.Max(1, sampleRate);
        _playLow = Resample(_nativeLow, _nativeLowRate);
        RefreshEnabled();
        ResetVoice();
    }

    public void SetHighSample(float[] mono, int sampleRate)
    {
        _nativeHigh = mono ?? [];
        _nativeHighRate = Math.Max(1, sampleRate);
        _playHigh = Resample(_nativeHigh, _nativeHighRate);
        RefreshEnabled();
        ResetVoice();
    }

    public void SetDeviceRate(int sampleRate)
    {
        var rate = Math.Clamp(sampleRate, 1000, 384000);
        if (rate == _deviceRate)
        {
            return;
        }

        _deviceRate = rate;
        RebuildPlay();
    }

    public void SetTriggers(ReadOnlySpan<long> frames, int groupSize = 0)
    {
        _triggers = UniqueSorted(frames);
        _groupSize = groupSize < 0 ? 0 : groupSize;
        RefreshEnabled();
        ResetVoice();
    }

    public void ResetVoice()
    {
        _voice = -1;
        _high = false;
        _lastSource = long.MinValue;
    }

    /// <summary>出力 1 フレーム分。トリガを跨いだら波形先頭から重ね直す。</summary>
    public float Advance(long sourceFrame)
    {
        if (!_enabled)
        {
            _lastSource = sourceFrame;
            return 0f;
        }

        MaybeTrigger(sourceFrame);
        _lastSource = sourceFrame;
        var play = PlayVoice();
        if (_voice < 0 || play.Length == 0)
        {
            return 0f;
        }

        var sample = play[_voice++] * OutputGainLinear;
        if (_voice >= play.Length)
        {
            _voice = -1;
        }

        return sample;
    }

    private float[] PlayVoice()
    {
        if (_high && _playHigh.Length > 0)
        {
            return _playHigh;
        }

        return _playLow;
    }

    private void MaybeTrigger(long sourceFrame)
    {
        if (_lastSource == long.MinValue || sourceFrame < _lastSource)
        {
            var at = TriggerIndexAt(sourceFrame);
            if (at >= 0)
            {
                StartTrigger(at);
            }

            return;
        }

        if (sourceFrame == _lastSource)
        {
            return;
        }

        var index = LastTriggerIndexIn(_lastSource + 1, sourceFrame);
        if (index >= 0)
        {
            StartTrigger(index);
        }
    }

    private void StartTrigger(int index)
    {
        _high = RangeClickMeter.IsDownbeat(index, _groupSize);
        _voice = 0;
    }

    private int TriggerIndexAt(long frame)
    {
        var i = Array.BinarySearch(_triggers, frame);
        return i >= 0 ? i : -1;
    }

    private int LastTriggerIndexIn(long lo, long hi)
    {
        var i = Array.BinarySearch(_triggers, hi);
        if (i < 0)
        {
            i = ~i - 1;
        }

        return i >= 0 && _triggers[i] >= lo ? i : -1;
    }

    private void RebuildPlay()
    {
        _playLow = Resample(_nativeLow, _nativeLowRate);
        _playHigh = Resample(_nativeHigh, _nativeHighRate);
        RefreshEnabled();
    }

    private float[] Resample(float[] native, int nativeRate)
    {
        if (native.Length == 0)
        {
            return [];
        }

        return nativeRate == _deviceRate
            ? native
            : FormatConvert.Resample(native, channels: 1, nativeRate, _deviceRate);
    }

    private void RefreshEnabled() =>
        _enabled = (_playLow.Length > 0 || _playHigh.Length > 0) && _triggers.Length > 0;

    private static long[] UniqueSorted(ReadOnlySpan<long> frames)
    {
        if (frames.Length == 0)
        {
            return [];
        }

        var copy = frames.ToArray();
        Array.Sort(copy);
        var n = 1;
        for (var i = 1; i < copy.Length; i++)
        {
            if (copy[i] != copy[n - 1])
            {
                copy[n++] = copy[i];
            }
        }

        return copy.AsSpan(0, n).ToArray();
    }
}
