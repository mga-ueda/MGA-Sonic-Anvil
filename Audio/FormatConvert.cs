namespace MgaSonicAnvil.Audio;

internal static class FormatConvert
{
    public static readonly int[] SampleRates =
        [8000, 11025, 16000, 22050, 32000, 44100, 48000, 96000];

    public static readonly int[] BitDepths = [4, 8, 16, 24];

    public static readonly int[] ChannelCounts = [1, 2];

    public const int MinSampleRate = 1000;
    public const int MaxSampleRate = 384000;

    public static bool IsValidSampleRate(int rate) =>
        rate >= MinSampleRate && rate <= MaxSampleRate;

    public static bool IsValidBitDepth(int bits) =>
        Array.IndexOf(BitDepths, bits) >= 0;

    private const int SincHalfWidth = 24;
    private const double SincTransition = 0.91;

    public static bool ShouldResampleForDevice(int sourceRate, int deviceRate) =>
        sourceRate > 0 && deviceRate > 0 && sourceRate != deviceRate;

    public static bool ShouldHoldForDevice(int sourceRate, int deviceRate) =>
        ShouldResampleForDevice(sourceRate, deviceRate);

    /// <summary>
    /// 新しいナイキストで帯域制限してから間引く／補間する。
    /// ホールドだと階段波の高調波が出て、8 kHz がキンキンする。
    /// </summary>
    public static float[] Resample(
        float[] interleaved,
        int channels,
        int sourceRate,
        int destRate,
        IProgress<double>? progress = null)
    {
        channels = Math.Max(1, channels);
        if (sourceRate < 1 || destRate < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(destRate));
        }

        if (sourceRate == destRate || interleaved.Length < channels)
        {
            progress?.Report(1);
            return interleaved;
        }

        var srcFrames = interleaved.Length / channels;
        var destFrames = Math.Max(1, (int)Math.Round(srcFrames * (double)destRate / sourceRate));
        var dest = new float[destFrames * channels];
        var step = sourceRate / (double)destRate;
        var cutoff = LowpassCutoff(sourceRate, destRate);
        var reportEvery = Math.Max(1, destFrames / 100);
        progress?.Report(0);
        for (var i = 0; i < destFrames; i++)
        {
            var srcFrame = i * step;
            var destOffset = i * channels;
            for (var ch = 0; ch < channels; ch++)
            {
                dest[destOffset + ch] = SampleSinc(interleaved, channels, ch, srcFrame, srcFrames, cutoff);
            }

            if (progress is not null && ((i + 1) % reportEvery == 0 || i + 1 == destFrames))
            {
                progress.Report((i + 1) / (double)destFrames);
            }
        }

        return dest;
    }

    public static float[] Quantize(float[] samples, int bits)
    {
        if (bits >= 24)
        {
            return samples;
        }

        bits = Math.Clamp(bits, 4, 24);
        var levels = Math.Max(1, (1 << (bits - 1)) - 1);
        var dest = new float[samples.Length];
        for (var i = 0; i < samples.Length; i++)
        {
            var quantized = Math.Clamp(Math.Round(samples[i] * levels), -levels, levels);
            dest[i] = (float)(quantized / levels);
        }

        return dest;
    }

    public static float[] Remix(float[] interleaved, int sourceChannels, int destChannels)
    {
        sourceChannels = Math.Max(1, sourceChannels);
        destChannels = Math.Max(1, destChannels);
        if (destChannels == sourceChannels)
        {
            return interleaved;
        }

        var frames = interleaved.Length / sourceChannels;
        var dest = new float[frames * destChannels];
        for (var frame = 0; frame < frames; frame++)
        {
            ChannelMix.Downmix(interleaved, frame * sourceChannels, sourceChannels, out var left, out var right);
            if (destChannels == 1)
            {
                dest[frame] = 0.5f * (left + right);
                continue;
            }

            dest[frame * destChannels] = left;
            dest[frame * destChannels + 1] = right;
        }

        return dest;
    }

    public static long ScaleFrame(long frame, int sourceRate, int destRate, long destFrameCount)
    {
        if (sourceRate == destRate || sourceRate < 1)
        {
            return Math.Clamp(frame, 0, Math.Max(0, destFrameCount));
        }

        var scaled = (long)Math.Round(frame * (double)destRate / sourceRate);
        return Math.Clamp(scaled, 0, Math.Max(0, destFrameCount));
    }

    public static WaveSelection ScaleSelection(
        WaveSelection range,
        int sourceRate,
        int destRate,
        long destFrameCount)
    {
        if (range.IsEmpty)
        {
            return WaveSelection.Empty;
        }

        var start = ScaleFrame(range.StartFrame, sourceRate, destRate, destFrameCount);
        var end = ScaleFrame(range.EndFrame, sourceRate, destRate, destFrameCount);
        return new WaveSelection(start, end).Clamp(destFrameCount);
    }

    public static MarkerSnapshot[] ScaleMarkers(
        IReadOnlyList<MarkerSnapshot> markers,
        int sourceRate,
        int destRate,
        long destFrameCount)
    {
        if (markers.Count == 0)
        {
            return [];
        }

        var scaled = new MarkerSnapshot[markers.Count];
        for (var i = 0; i < markers.Count; i++)
        {
            scaled[i] = new MarkerSnapshot(
                ScaleFrame(markers[i].Frame, sourceRate, destRate, destFrameCount),
                markers[i].Comment);
        }

        return scaled;
    }

    public static void DownmixBandlimited(
        float[] interleaved,
        int channels,
        double frame,
        int frameCount,
        int sourceRate,
        int destRate,
        out float left,
        out float right)
    {
        if (frameCount <= 0 || interleaved.Length < channels)
        {
            left = right = 0;
            return;
        }

        var cutoff = LowpassCutoff(sourceRate, destRate);
        var center = (int)Math.Floor(frame);
        var sumL = 0d;
        var sumR = 0d;
        var wsum = 0d;
        for (var tap = -SincHalfWidth; tap <= SincHalfWidth; tap++)
        {
            var kernel = SincKernel(center + tap - frame, cutoff, tap);
            ChannelMix.Downmix(
                interleaved,
                ClampIndex(center + tap, frameCount) * channels,
                channels,
                out var tapL,
                out var tapR);
            sumL += tapL * kernel;
            sumR += tapR * kernel;
            wsum += kernel;
        }

        if (Math.Abs(wsum) > 1e-8)
        {
            sumL /= wsum;
            sumR /= wsum;
        }

        left = (float)sumL;
        right = (float)sumR;
    }

    public static void DownmixHeld(
        float[] interleaved,
        int channels,
        double frame,
        int frameCount,
        out float left,
        out float right)
    {
        if (frameCount <= 0 || interleaved.Length < channels)
        {
            left = right = 0;
            return;
        }

        ChannelMix.Downmix(
            interleaved,
            ClampIndex((int)Math.Floor(frame), frameCount) * channels,
            channels,
            out left,
            out right);
    }

    public static void DownmixInterpolated(
        float[] interleaved,
        int channels,
        double frame,
        int frameCount,
        out float left,
        out float right)
    {
        if (frameCount <= 0 || interleaved.Length < channels)
        {
            left = right = 0;
            return;
        }

        var i = (int)Math.Floor(frame);
        var t = (float)(frame - i);
        if (t <= 1e-8f || frameCount == 1)
        {
            ChannelMix.Downmix(interleaved, ClampIndex(i, frameCount) * channels, channels, out left, out right);
            return;
        }

        ChannelMix.Downmix(interleaved, ClampIndex(i - 1, frameCount) * channels, channels, out var p0l, out var p0r);
        ChannelMix.Downmix(interleaved, ClampIndex(i, frameCount) * channels, channels, out var p1l, out var p1r);
        ChannelMix.Downmix(interleaved, ClampIndex(i + 1, frameCount) * channels, channels, out var p2l, out var p2r);
        ChannelMix.Downmix(interleaved, ClampIndex(i + 2, frameCount) * channels, channels, out var p3l, out var p3r);
        left = Hermite(p0l, p1l, p2l, p3l, t);
        right = Hermite(p0r, p1r, p2r, p3r, t);
    }

    private static double LowpassCutoff(int sourceRate, int destRate) =>
        0.5 * Math.Min(1d, destRate / (double)Math.Max(1, sourceRate)) * SincTransition;

    private static float SampleSinc(
        float[] src,
        int channels,
        int channel,
        double frame,
        int frameCount,
        double cutoff)
    {
        if (frameCount <= 1)
        {
            return src[channel];
        }

        var center = (int)Math.Floor(frame);
        var sum = 0d;
        var wsum = 0d;
        for (var tap = -SincHalfWidth; tap <= SincHalfWidth; tap++)
        {
            var kernel = SincKernel(center + tap - frame, cutoff, tap);
            sum += src[ClampIndex(center + tap, frameCount) * channels + channel] * kernel;
            wsum += kernel;
        }

        return (float)(Math.Abs(wsum) > 1e-8 ? sum / wsum : sum);
    }

    private static double SincKernel(double offset, double cutoff, int tap)
    {
        var window = 0.5 * (1d + Math.Cos(Math.PI * tap / SincHalfWidth));
        return 2d * cutoff * Sinc(2d * cutoff * offset) * window;
    }

    private static double Sinc(double x)
    {
        if (Math.Abs(x) < 1e-8)
        {
            return 1d;
        }

        var pix = Math.PI * x;
        return Math.Sin(pix) / pix;
    }

    private static float SampleHeld(float[] src, int channels, int channel, double frame, int frameCount)
    {
        if (frameCount <= 1)
        {
            return src[channel];
        }

        return src[ClampIndex((int)Math.Floor(frame), frameCount) * channels + channel];
    }

    private static float Hermite(float p0, float p1, float p2, float p3, float t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        return 0.5f * (
            2f * p1
            + (-p0 + p2) * t
            + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
            + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    private static int ClampIndex(int index, int frameCount) =>
        Math.Clamp(index, 0, frameCount - 1);
}
