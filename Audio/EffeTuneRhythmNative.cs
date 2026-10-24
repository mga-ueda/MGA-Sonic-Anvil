using System.Runtime.InteropServices;

namespace MgaSonicAnvil.Audio;

/// <summary>
/// EffeTune Rhythm Analyzer DSP（MIT）の薄い P/Invoke。BPM 解析と拍間隔／クリック。
/// </summary>
internal static class EffeTuneRhythmNative
{
    private const string Dll = "EffeTuneRhythm";

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "er_create")]
    public static extern IntPtr Create();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "er_destroy")]
    public static extern void Destroy(IntPtr handle);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "er_prepare")]
    public static extern int Prepare(IntPtr handle, double sampleRate);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "er_reset")]
    public static extern void Reset(IntPtr handle);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "er_set_range")]
    public static extern void SetRange(IntPtr handle, double minimumBpm, double maximumBpm);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "er_set_click")]
    public static extern void SetClick(IntPtr handle, int enabled);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "er_process_mono")]
    public static extern void ProcessMono(IntPtr handle, float[] samples, int count);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "er_process_interleaved")]
    public static extern void ProcessInterleaved(IntPtr handle, float[] interleaved, int frames, int channels);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "er_get_bpm")]
    public static extern int GetBpm(IntPtr handle, out float bpm, out float confidence);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "er_get_ioi_bpm")]
    public static extern int GetIoiBpm(IntPtr handle, out float bpm);
}

/// <summary>
/// EffeTune Rhythm Analyzer。再生スレッドから interleaved を渡し、UI は拍間隔 BPM を読む。
/// </summary>
internal sealed class EffeTuneBpmEstimator : IDisposable
{
    public const double MinBpm = 40;
    public const double MaxBpm = 240;

    private readonly object _gate = new();
    private IntPtr _handle;
    private int _sampleRate;
    private bool _ready;
    private bool _clickEnabled;
    private float? _bpm;
    private float? _analysisBpm;
    private float _confidence;

    /// <summary>表示用。拍間隔（IOI）から算出した BPM。</summary>
    public float? Bpm
    {
        get
        {
            lock (_gate)
            {
                return _bpm;
            }
        }
    }

    /// <summary>EffeTune の period_/tempogram（半／倍テンポのことがある）。解析参照用。</summary>
    public float? AnalysisBpm
    {
        get
        {
            lock (_gate)
            {
                return _analysisBpm;
            }
        }
    }

    public float Confidence
    {
        get
        {
            lock (_gate)
            {
                return _confidence;
            }
        }
    }

    public EffeTuneBpmEstimator()
    {
        _handle = EffeTuneRhythmNative.Create();
        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("EffeTuneRhythm failed to create.");
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _bpm = null;
            _analysisBpm = null;
            _confidence = 0;
            if (_handle != IntPtr.Zero && _ready)
            {
                EffeTuneRhythmNative.Reset(_handle);
                EffeTuneRhythmNative.SetClick(_handle, _clickEnabled ? 1 : 0);
            }
        }
    }

    public void SetClickEnabled(bool enabled)
    {
        lock (_gate)
        {
            _clickEnabled = enabled;
            if (_handle != IntPtr.Zero && _ready)
            {
                EffeTuneRhythmNative.SetClick(_handle, enabled ? 1 : 0);
            }
        }
    }

    /// <summary>
    /// 再生バッファを解析し、クリック有効時は ch0/1 にクリックを加算する。
    /// 表示 BPM は拍間隔から更新する（EffeTune の半テンポ値は使わない）。
    /// </summary>
    public void ProcessInterleaved(float[] buffer, int offset, int sampleCount, int channels, int sampleRate)
    {
        if (_handle == IntPtr.Zero || buffer is null || sampleCount <= 0 || channels < 1)
        {
            return;
        }

        var frames = sampleCount / channels;
        if (frames <= 0)
        {
            return;
        }

        lock (_gate)
        {
            EnsurePreparedNoLock(sampleRate);
            if (!_ready)
            {
                _bpm = null;
                _analysisBpm = null;
                _confidence = 0;
                return;
            }

            if (offset == 0 && sampleCount == buffer.Length)
            {
                EffeTuneRhythmNative.ProcessInterleaved(_handle, buffer, frames, channels);
            }
            else
            {
                var scratch = new float[sampleCount];
                Buffer.BlockCopy(buffer, offset * sizeof(float), scratch, 0, sampleCount * sizeof(float));
                EffeTuneRhythmNative.ProcessInterleaved(_handle, scratch, frames, channels);
                Buffer.BlockCopy(scratch, 0, buffer, offset * sizeof(float), sampleCount * sizeof(float));
            }

            RefreshBpmNoLock();
        }
    }

    /// <summary>UI／テスト用。解析のみ（クリックは混ぜない）。拍間隔 BPM は更新する。</summary>
    public void Process(ReadOnlySpan<float> samples, int sampleRate)
    {
        if (_handle == IntPtr.Zero || samples.IsEmpty || sampleRate < 8000)
        {
            return;
        }

        lock (_gate)
        {
            EnsurePreparedNoLock(sampleRate);
            if (!_ready)
            {
                _bpm = null;
                _analysisBpm = null;
                _confidence = 0;
                return;
            }

            var copy = samples.ToArray();
            EffeTuneRhythmNative.ProcessMono(_handle, copy, copy.Length);
            RefreshBpmNoLock();
        }
    }

    private void RefreshBpmNoLock()
    {
        if (EffeTuneRhythmNative.GetBpm(_handle, out var analysis, out var confidence) != 0
            && float.IsFinite(analysis)
            && analysis >= (float)MinBpm
            && analysis <= (float)MaxBpm)
        {
            _analysisBpm = analysis;
            _confidence = confidence;
        }

        // 表示はクリック／拍の間隔のみ（60000 / ms）。解析の半テンポは使わない。
        if (EffeTuneRhythmNative.GetIoiBpm(_handle, out var ioi) != 0
            && float.IsFinite(ioi)
            && ioi >= (float)MinBpm
            && ioi <= (float)MaxBpm)
        {
            _bpm = ioi;
        }
    }

    private void EnsurePreparedNoLock(int sampleRate)
    {
        if (_ready && _sampleRate == sampleRate)
        {
            return;
        }

        _sampleRate = sampleRate;
        _ready = EffeTuneRhythmNative.Prepare(_handle, sampleRate) != 0;
        if (_ready)
        {
            EffeTuneRhythmNative.SetRange(_handle, MinBpm, MaxBpm);
            EffeTuneRhythmNative.SetClick(_handle, _clickEnabled ? 1 : 0);
        }

        _bpm = null;
        _analysisBpm = null;
        _confidence = 0;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_handle == IntPtr.Zero)
            {
                return;
            }

            EffeTuneRhythmNative.Destroy(_handle);
            _handle = IntPtr.Zero;
            _ready = false;
        }

        GC.SuppressFinalize(this);
    }

    ~EffeTuneBpmEstimator()
    {
        if (_handle != IntPtr.Zero)
        {
            EffeTuneRhythmNative.Destroy(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
