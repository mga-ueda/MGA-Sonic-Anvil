using System.Runtime.InteropServices;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Audio;

/// <summary>Signalsmith Stretch によるピッチシフト。既定は長さ据え置き。</summary>
internal static class PitchShift
{
    public const int MinSemitones = -24;
    public const int MaxSemitones = 24;
    public const int OctaveSemitones = 12;

    public static int Snap(int semitones) => Math.Clamp(semitones, MinSemitones, MaxSemitones);

    public static bool IsNoOp(int semitones) => Snap(semitones) == 0;

    public static double Ratio(int semitones) => Math.Pow(2, Snap(semitones) / 12d);

    public static int NudgeStep(bool octave) => octave ? OctaveSemitones : 1;

    public static int DestFrameCount(int sourceFrames, int semitones, bool timeStretch)
    {
        sourceFrames = Math.Max(0, sourceFrames);
        if (timeStretch || IsNoOp(semitones) || sourceFrames == 0)
        {
            return sourceFrames;
        }

        return Math.Max(1, (int)Math.Round(sourceFrames / Ratio(semitones)));
    }

    public static float[] Apply(
        float[] interleaved,
        int channels,
        int sampleRate,
        int semitones,
        bool timeStretch = true,
        IProgress<double>? progress = null)
    {
        channels = Math.Max(1, channels);
        sampleRate = Math.Max(1, sampleRate);
        semitones = Snap(semitones);
        if (interleaved.Length < channels)
        {
            progress?.Report(1);
            return (float[])interleaved.Clone();
        }

        if (IsNoOp(semitones))
        {
            progress?.Report(1);
            return (float[])interleaved.Clone();
        }

        var frames = interleaved.Length / channels;
        if (timeStretch)
        {
            return StretchExact(interleaved, frames, frames, channels, sampleRate, semitones, progress);
        }

        var destFrames = DestFrameCount(frames, semitones, timeStretch: false);
        return FormatConvert.ResampleToFrameCount(interleaved, channels, sampleRate, destFrames, progress);
    }

    internal static float[] StretchExact(
        float[] interleaved,
        int inputFrames,
        int outputFrames,
        int channels,
        int sampleRate,
        float semitones,
        IProgress<double>? progress,
        string? failedMessage = null)
    {
        var dest = new float[outputFrames * channels];
        SignalsmithStretchNative.ProgressCallback? callback = progress is null
            ? null
            : (_, value) => progress.Report(value);
        var ok = SignalsmithStretchNative.PitchExact(
            interleaved,
            inputFrames,
            dest,
            outputFrames,
            channels,
            sampleRate,
            semitones,
            callback,
            IntPtr.Zero);
        GC.KeepAlive(callback);
        if (ok == 0)
        {
            throw new InvalidOperationException(failedMessage ?? UiStrings.ErrorPitchShiftFailed);
        }

        return dest;
    }
}

internal static class SignalsmithStretchNative
{
    private const string Dll = "SignalsmithStretch";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ProgressCallback(IntPtr user, float progress);

    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ss_pitch_exact_interleaved")]
    public static extern int PitchExact(
        float[] input,
        int inputFrames,
        float[] output,
        int outputFrames,
        int channels,
        int sampleRate,
        float semitones,
        ProgressCallback? progress,
        IntPtr user);

    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ss_rt_create")]
    public static extern IntPtr RtCreate();

    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ss_rt_destroy")]
    public static extern void RtDestroy(IntPtr handle);

    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ss_rt_set_semitones")]
    public static extern void RtSetSemitones(IntPtr handle, float semitones);

    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ss_rt_reset")]
    public static extern void RtReset(IntPtr handle);

    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ss_rt_process")]
    public static extern int RtProcess(
        IntPtr handle,
        float[] interleaved,
        int frameOffset,
        int frames,
        int channels,
        int sampleRate);
}

/// <summary>
/// 再生出力に掛ける時間長据え置きのピッチ。半音 0 のときはバッファを触らない。
/// </summary>
internal sealed class PlaybackPitchShift : IDisposable
{
    private IntPtr _handle;
    private bool _disposed;

    public void SetSemitones(int semitones)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Ensure();
        SignalsmithStretchNative.RtSetSemitones(_handle, semitones);
    }

    /// <summary>未使用ならネイティブを作らない。トラック切替の 0 戻し用。</summary>
    public void ClearSemitones()
    {
        if (_disposed || _handle == IntPtr.Zero)
        {
            return;
        }

        SignalsmithStretchNative.RtSetSemitones(_handle, 0);
        SignalsmithStretchNative.RtReset(_handle);
    }

    public void ResetEngine()
    {
        if (_disposed || _handle == IntPtr.Zero)
        {
            return;
        }

        SignalsmithStretchNative.RtReset(_handle);
    }

    public void Process(float[] interleaved, int frameOffset, int frames, int channels, int sampleRate, int semitones)
    {
        if (_disposed || semitones == 0 || frames < 1 || channels < 1)
        {
            return;
        }

        Ensure();
        SignalsmithStretchNative.RtSetSemitones(_handle, semitones);
        SignalsmithStretchNative.RtProcess(_handle, interleaved, frameOffset, frames, channels, sampleRate);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_handle != IntPtr.Zero)
        {
            SignalsmithStretchNative.RtDestroy(_handle);
            _handle = IntPtr.Zero;
        }
    }

    private void Ensure()
    {
        if (_handle == IntPtr.Zero)
        {
            _handle = SignalsmithStretchNative.RtCreate();
        }
    }
}
