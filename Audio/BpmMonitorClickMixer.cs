namespace MgaSonicAnvil.Audio;

/// <summary>
/// BPM モニター用クリック。EffeTune 合成音は出力に出さず、拍位置だけ借りて Low.wav（−20 dB）を鳴らす。
/// </summary>
internal sealed class BpmMonitorClickMixer
{
    private float[] _native = [];
    private int _nativeRate = 1;
    private float[] _play = [];
    private int _deviceRate = 48000;
    private int _voice = -1;

    public void EnsureSample()
    {
        if (_native.Length > 0)
        {
            return;
        }

        var (samples, rate) = RangeClickSample.LoadLow();
        _native = samples ?? [];
        _nativeRate = Math.Max(1, rate);
        RebuildPlay();
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

    public void Reset()
    {
        _voice = -1;
    }

    /// <summary>
    /// 出力バッファへ Low.wav を足す。<paramref name="onsetFrames"/> はバッファ内の拍開始フレーム。
    /// 前バッファから続く再生も継続する。
    /// </summary>
    public void MixBuffer(
        float[] buffer,
        int offset,
        int channels,
        int frames,
        List<int> onsetFrames,
        float dimLinear)
    {
        if (frames <= 0 || channels < 1 || buffer is null)
        {
            return;
        }

        EnsureSample();
        if (_play.Length == 0)
        {
            return;
        }

        var onsetIndex = 0;
        var dim = dimLinear;
        for (var frame = 0; frame < frames; frame++)
        {
            while (onsetIndex < onsetFrames.Count && onsetFrames[onsetIndex] <= frame)
            {
                if (onsetFrames[onsetIndex] == frame)
                {
                    _voice = 0;
                }

                onsetIndex++;
            }

            if (_voice < 0 || _voice >= _play.Length)
            {
                _voice = -1;
                continue;
            }

            var sample = _play[_voice++] * RangeClickMixer.OutputGainLinear * dim;
            var dest = offset + frame * channels;
            for (var ch = 0; ch < channels; ch++)
            {
                buffer[dest + ch] += sample;
            }

            if (_voice >= _play.Length)
            {
                _voice = -1;
            }
        }
    }

    /// <summary>
    /// 合成クリック付き scratch と元バッファの差分から、拍開始フレームを拾う。
    /// </summary>
    public static void CollectOnsetsFromSynthDiff(
        float[] original,
        int originalOffset,
        float[] scratchWithSynth,
        int frames,
        int channels,
        List<int> into)
    {
        into.Clear();
        if (frames <= 0 || channels < 1)
        {
            return;
        }

        // 合成クリック振幅は約 0.3。アタック立ち上がりを拍頭とする。
        const float onset = 0.02f;
        const float quiet = 0.004f;
        var prev = 0f;
        var armed = true;
        for (var frame = 0; frame < frames; frame++)
        {
            var a = original[originalOffset + frame * channels];
            var b = scratchWithSynth[frame * channels];
            var d = Math.Abs(b - a);
            if (armed && d > onset && prev < quiet)
            {
                into.Add(frame);
                armed = false;
            }

            if (d < quiet)
            {
                armed = true;
            }

            prev = d;
        }
    }

    private void RebuildPlay()
    {
        if (_native.Length == 0)
        {
            _play = [];
            return;
        }

        if (_nativeRate == _deviceRate)
        {
            _play = _native;
            return;
        }

        var ratio = (double)_deviceRate / _nativeRate;
        var length = Math.Max(1, (int)Math.Round(_native.Length * ratio));
        var play = new float[length];
        for (var i = 0; i < length; i++)
        {
            var src = i / ratio;
            var i0 = (int)src;
            var i1 = Math.Min(i0 + 1, _native.Length - 1);
            var t = src - i0;
            play[i] = (float)(_native[i0] * (1 - t) + _native[i1] * t);
        }

        _play = play;
    }
}
