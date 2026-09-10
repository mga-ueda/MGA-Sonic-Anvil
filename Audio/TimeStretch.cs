using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Audio;

/// <summary>ピッチ据え置きの長さ変更。Signalsmith Stretch の exact。</summary>
internal static class TimeStretch
{
    public const double MinPercent = 10;
    public const double MaxPercent = 1000;

    public static bool IsNoOp(int sourceFrames, int destFrames) =>
        sourceFrames > 0 && destFrames == sourceFrames;

    public static double SnapPercent(double percent) =>
        Math.Clamp(Math.Round(percent, 1, MidpointRounding.AwayFromZero), MinPercent, MaxPercent);

    public static double PercentOf(int sourceFrames, int destFrames)
    {
        sourceFrames = Math.Max(0, sourceFrames);
        destFrames = Math.Max(0, destFrames);
        if (sourceFrames == 0)
        {
            return 100;
        }

        return SnapPercent(destFrames * 100d / sourceFrames);
    }

    public static int DestFrameCount(int sourceFrames, double percent) =>
        DestFrameCountFromRatio(sourceFrames, SnapPercent(percent) / 100d);

    public static int DestFrameCountFromRatio(int sourceFrames, double ratio)
    {
        sourceFrames = Math.Max(0, sourceFrames);
        if (sourceFrames == 0)
        {
            return 0;
        }

        ratio = Math.Clamp(ratio, MinPercent / 100d, MaxPercent / 100d);
        var dest = (int)Math.Round(sourceFrames * ratio, MidpointRounding.AwayFromZero);
        return Math.Clamp(dest, 1, MaxDestFrames(sourceFrames));
    }

    public static int ClampDestFrames(int sourceFrames, int destFrames)
    {
        sourceFrames = Math.Max(0, sourceFrames);
        if (sourceFrames == 0)
        {
            return 0;
        }

        return Math.Clamp(destFrames, DestFrameCount(sourceFrames, MinPercent), MaxDestFrames(sourceFrames));
    }

    public static int MaxDestFrames(int sourceFrames) =>
        Math.Max(1, (int)Math.Round(Math.Max(0, sourceFrames) * MaxPercent / 100d, MidpointRounding.AwayFromZero));

    public static double PercentNudgeStep(bool shift, bool control) =>
        control && shift ? 25 : control ? 10 : shift ? 1 : 0.1;

    public static float[] Apply(
        float[] interleaved,
        int channels,
        int sampleRate,
        int destFrames,
        IProgress<double>? progress = null)
    {
        channels = Math.Max(1, channels);
        sampleRate = Math.Max(1, sampleRate);
        if (interleaved.Length < channels)
        {
            progress?.Report(1);
            return (float[])interleaved.Clone();
        }

        var frames = interleaved.Length / channels;
        destFrames = ClampDestFrames(frames, destFrames);
        if (IsNoOp(frames, destFrames))
        {
            progress?.Report(1);
            return (float[])interleaved.Clone();
        }

        return PitchShift.StretchExact(
            interleaved,
            frames,
            destFrames,
            channels,
            sampleRate,
            0,
            progress,
            UiStrings.ErrorTimeStretchFailed);
    }
}
