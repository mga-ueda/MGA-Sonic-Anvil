namespace MgaSonicAnvil.Audio;

/// <summary>
/// TimeCaster の VideoAudioScrubVoice と同じスクラブ。
/// 再生デコーダは触らず、位置の短いグレインを出す。
/// グレインは端テーパー＋ループ再生で途切れさせず、差し替えはクロスフェード。
/// 静止時はホールド後すばやくフェードアウトし、グレインが 1 周し終わる前に
/// 無音へ達する（ループの繰り返しが「ダダダ」と聞こえないように）。
/// </summary>
internal sealed class MemoryScrubVoice
{
    internal const double GrainSeconds = 0.08;
    internal const double MinMoveSeconds = 0.005;
    internal const double XfadeSeconds = 0.02;
    internal const double EdgeTaperSeconds = 0.005;
    internal const double MaxLeadSeconds = 0.03;
    internal const double HoldSeconds = 0.05;
    internal const double ReleaseSeconds = 0.025;

    private readonly object _gate = new();
    private float[] _samples = [];
    private int _channels = 2;
    private int _sampleRate = 48000;
    private long _frameCount;
    private float[]? _grain;
    private float[]? _oldGrain;
    private float[]? _pending;
    private int _index;
    private int _oldIndex;
    private int _xfadeLeft;
    private int _xfadeTotal;
    private int _taperFrames = 1;
    private int _samplesSinceSwap;
    private float _holdEnv;
    private double _pendingSeconds;
    private double _grainSeconds = double.NaN;
    private double _lastCaptureSeconds = double.NaN;
    private bool _opened;
    private int _soloMask;
    private float[] _soloScratch = [];

    public int SampleRate => Math.Max(1, _sampleRate);

    public void SetSoloMask(int mask)
    {
        lock (_gate)
        {
            _soloMask = mask;
        }
    }

    public void Bind(AudioDocument document)
    {
        lock (_gate)
        {
            _samples = document.Interleaved;
            _channels = Math.Max(1, document.Channels);
            _sampleRate = Math.Max(1, document.SampleRate);
            _frameCount = document.FrameCount;
            _opened = true;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _pending = null;
            _grain = null;
            _oldGrain = null;
            _index = 0;
            _oldIndex = 0;
            _xfadeLeft = 0;
            _samplesSinceSwap = 0;
            _holdEnv = 0;
            _grainSeconds = double.NaN;
            _lastCaptureSeconds = double.NaN;
        }
    }

    public void Capture(long frame)
    {
        lock (_gate)
        {
            if (!_opened)
            {
                return;
            }

            var seconds = frame / (double)_sampleRate;
            if (!double.IsNaN(_lastCaptureSeconds)
                && Math.Abs(seconds - _lastCaptureSeconds) < MinMoveSeconds)
            {
                return;
            }

            _lastCaptureSeconds = seconds;
            var rate = _sampleRate;
            var frames = Math.Max(64, (int)Math.Round(GrainSeconds * rate));
            var taper = Math.Clamp((int)Math.Round(EdgeTaperSeconds * rate), 1, frames / 2);
            var local = new float[frames * 2];
            FillGrain(local, frames, frame);
            ApplyEdgeTaper(local, frames, taper);
            _pending = local;
            _pendingSeconds = seconds;
            _taperFrames = taper;
        }
    }

    public void Read(float[] dest, int offset, int frames, float volume)
    {
        lock (_gate)
        {
            if (_pending is not null && _xfadeLeft <= 0)
            {
                var rate0 = Math.Max(1, _sampleRate);
                var contentSeconds = _grain is null || double.IsNaN(_grainSeconds)
                    ? double.NaN
                    : _grainSeconds + (double)_index / rate0;
                _oldGrain = _grain;
                _oldIndex = _index;
                _grain = _pending;
                _pending = null;
                _grainSeconds = _pendingSeconds;
                _index = 0;
                if (!double.IsNaN(contentSeconds))
                {
                    var aligned = (int)Math.Round((contentSeconds - _grainSeconds) * rate0);
                    var loopFrames = Math.Max(1, _grain.Length / 2 - _taperFrames);
                    var maxLead = Math.Min(loopFrames - 1, (int)Math.Round(MaxLeadSeconds * rate0));
                    if (aligned > maxLead)
                    {
                        aligned = maxLead;
                    }

                    if (aligned > 0)
                    {
                        _index = aligned;
                    }
                }

                _samplesSinceSwap = 0;
                _xfadeTotal = Math.Max(1, (int)Math.Round(XfadeSeconds * rate0));
                _xfadeLeft = _oldGrain is null ? 0 : _xfadeTotal;
            }

            if (_grain is null || _grain.Length < 2)
            {
                for (var i = 0; i < frames * 2; i++)
                {
                    dest[offset + i] = 0f;
                }

                _holdEnv = 0;
                return;
            }

            var rate = Math.Max(1, _sampleRate);
            var holdFrames = (int)(HoldSeconds * rate);
            var envStep = 1f / Math.Max(1, (int)(ReleaseSeconds * rate));
            var taper = _taperFrames;

            for (var i = 0; i < frames; i++)
            {
                ReadOne(_grain, ref _index, taper, out var left, out var right);
                if (_xfadeLeft > 0 && _oldGrain is not null)
                {
                    ReadOne(_oldGrain, ref _oldIndex, taper, out var oldL, out var oldR);
                    var t = 1f - (_xfadeLeft / (float)_xfadeTotal);
                    left = oldL + (left - oldL) * t;
                    right = oldR + (right - oldR) * t;
                    _xfadeLeft--;
                    if (_xfadeLeft <= 0)
                    {
                        _oldGrain = null;
                    }
                }

                var target = _samplesSinceSwap > holdFrames ? 0f : 1f;
                _holdEnv = target > _holdEnv
                    ? Math.Min(target, _holdEnv + envStep)
                    : Math.Max(target, _holdEnv - envStep);
                _samplesSinceSwap++;

                dest[offset + i * 2] = left * volume * _holdEnv;
                dest[offset + i * 2 + 1] = right * volume * _holdEnv;
            }
        }
    }

    internal static void SoftClip(float[] buffer, int offset, int count)
    {
        const float knee = 0.85f;
        for (var i = 0; i < count; i++)
        {
            var v = buffer[offset + i];
            var a = Math.Abs(v);
            if (a <= knee)
            {
                continue;
            }

            var y = knee + (1f - knee) * MathF.Tanh((a - knee) / (1f - knee));
            buffer[offset + i] = v < 0 ? -y : y;
        }
    }

    internal static void ApplyEdgeTaper(float[] stereo, int frames, int taperFrames)
    {
        var n = Math.Clamp(taperFrames, 1, frames / 2);
        for (var i = 0; i < n; i++)
        {
            var w = 0.5f * (1f - MathF.Cos(MathF.PI * i / n));
            stereo[i * 2] *= w;
            stereo[i * 2 + 1] *= w;
            var j = frames - 1 - i;
            stereo[j * 2] *= w;
            stereo[j * 2 + 1] *= w;
        }
    }

    private void FillGrain(float[] dest, int frames, long startFrame)
    {
        var src = _samples;
        var channels = _channels;
        var count = _frameCount;
        for (var i = 0; i < frames; i++)
        {
            var frame = startFrame + i;
            if (frame < 0 || frame >= count || src.Length == 0)
            {
                dest[i * 2] = 0f;
                dest[i * 2 + 1] = 0f;
                continue;
            }

            var offset = checked((int)frame * channels);
            if (offset < 0 || offset + channels > src.Length)
            {
                dest[i * 2] = 0f;
                dest[i * 2 + 1] = 0f;
                continue;
            }

            if (_soloMask != 0)
            {
                ChannelMix.Downmix(
                    ApplySolo(src.AsSpan(offset, channels)),
                    out dest[i * 2],
                    out dest[i * 2 + 1]);
                continue;
            }

            ChannelMix.Downmix(src, offset, channels, out var left, out var right);
            dest[i * 2] = left;
            dest[i * 2 + 1] = right;
        }
    }

    private ReadOnlySpan<float> ApplySolo(ReadOnlySpan<float> source)
    {
        if (_soloMask == 0)
        {
            return source;
        }

        if (_soloScratch.Length < source.Length)
        {
            _soloScratch = new float[source.Length];
        }

        var dest = _soloScratch.AsSpan(0, source.Length);
        dest.Clear();
        for (var i = 0; i < source.Length; i++)
        {
            if (ChannelSolo.Contains(_soloMask, i))
            {
                dest[i] = source[i];
            }
        }

        return dest;
    }

    private static void ReadOne(float[] grain, ref int index, int taperFrames, out float left, out float right)
    {
        var frames = grain.Length / 2;
        var loopFrames = Math.Max(1, frames - taperFrames);
        if (index >= loopFrames)
        {
            index = 0;
        }

        left = grain[index * 2];
        right = grain[index * 2 + 1];
        var tail = loopFrames + index;
        if (index < taperFrames && tail < frames)
        {
            left += grain[tail * 2];
            right += grain[tail * 2 + 1];
        }

        index++;
    }
}
