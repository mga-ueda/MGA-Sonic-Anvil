using System.Threading.Tasks;
using MgaSonicAnvil.Domain;

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

    public static int SampleRateNudgeStep(bool shift, bool control) =>
        control && shift ? 1000 : control ? 100 : shift ? 10 : 1;

    public static int ApplySampleRateNudge(int current, int direction, int step)
    {
        var clamped = Math.Clamp(current, MinSampleRate, MaxSampleRate);
        if (direction == 0 || step <= 0)
        {
            return clamped;
        }

        var next = current + (long)step * Math.Sign(direction);
        if (next > MaxSampleRate)
        {
            return MaxSampleRate;
        }

        if (next < MinSampleRate)
        {
            return MinSampleRate;
        }

        return (int)next;
    }

    public static bool IsValidBitDepth(int bits) =>
        Array.IndexOf(BitDepths, bits) >= 0;

    private const int SincHalfWidth = 24;
    private const int SincTapCount = SincHalfWidth * 2 + 1;
    private const int SincFracBins = 1024;
    private const int ParallelFrameThreshold = 4096;
    private const double SincTransition = 0.91;

    /// <summary>Resample の端でカーネルが参照する余白（プレビュー切り出し用）。</summary>
    public static int ResampleEdgePad => SincHalfWidth;

    public static bool ShouldResampleForDevice(int sourceRate, int deviceRate) =>
        sourceRate > 0 && deviceRate > 0 && sourceRate != deviceRate;

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
        var table = BuildSincKernelTable(LowpassCutoff(sourceRate, destRate));
        var reportEvery = Math.Max(1, destFrames / 100);
        progress?.Report(0);

        if (destFrames < ParallelFrameThreshold)
        {
            for (var i = 0; i < destFrames; i++)
            {
                WriteResampledFrame(dest, interleaved, channels, srcFrames, i, step, table);
                if (progress is not null && ((i + 1) % reportEvery == 0 || i + 1 == destFrames))
                {
                    progress.Report((i + 1) / (double)destFrames);
                }
            }

            return dest;
        }

        var done = 0;
        Parallel.For(0, destFrames, i =>
        {
            WriteResampledFrame(dest, interleaved, channels, srcFrames, i, step, table);
            if (progress is null)
            {
                return;
            }

            var n = Interlocked.Increment(ref done);
            if (n % reportEvery == 0 || n == destFrames)
            {
                progress.Report(n / (double)destFrames);
            }
        });
        progress?.Report(1);
        return dest;
    }

    public static float[] ResampleToFrameCount(
        float[] interleaved,
        int channels,
        int sampleRate,
        int destFrames,
        IProgress<double>? progress = null)
    {
        channels = Math.Max(1, channels);
        sampleRate = Math.Max(1, sampleRate);
        var srcFrames = interleaved.Length / channels;
        destFrames = Math.Max(1, destFrames);
        if (srcFrames <= 0 || interleaved.Length < channels)
        {
            progress?.Report(1);
            return (float[])interleaved.Clone();
        }

        if (srcFrames == destFrames)
        {
            progress?.Report(1);
            return (float[])interleaved.Clone();
        }

        var destRate = Math.Max(1, (int)Math.Round(sampleRate * (double)destFrames / srcFrames));
        var dest = new float[destFrames * channels];
        var step = srcFrames / (double)destFrames;
        var table = BuildSincKernelTable(LowpassCutoff(sampleRate, destRate));
        var reportEvery = Math.Max(1, destFrames / 100);
        progress?.Report(0);
        if (destFrames < ParallelFrameThreshold)
        {
            for (var i = 0; i < destFrames; i++)
            {
                WriteResampledFrame(dest, interleaved, channels, srcFrames, i, step, table);
                if (progress is not null && ((i + 1) % reportEvery == 0 || i + 1 == destFrames))
                {
                    progress.Report((i + 1) / (double)destFrames);
                }
            }

            return dest;
        }

        var done = 0;
        Parallel.For(0, destFrames, i =>
        {
            WriteResampledFrame(dest, interleaved, channels, srcFrames, i, step, table);
            if (progress is null)
            {
                return;
            }

            var n = Interlocked.Increment(ref done);
            if (n % reportEvery == 0 || n == destFrames)
            {
                progress.Report(n / (double)destFrames);
            }
        });
        progress?.Report(1);
        return dest;
    }

    public static float[] CopyFrameRange(float[] interleaved, int channels, long startFrame, long endFrame)
    {
        channels = Math.Max(1, channels);
        var frames = interleaved.Length / channels;
        var start = (int)Math.Clamp(startFrame, 0, frames);
        var end = (int)Math.Clamp(endFrame, start, frames);
        if (start == 0 && end == frames)
        {
            return interleaved;
        }

        var count = end - start;
        if (count <= 0)
        {
            return [];
        }

        var dest = new float[count * channels];
        Array.Copy(interleaved, start * channels, dest, 0, dest.Length);
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

    /// <summary>
    /// ソースの全チャンネルを帯域制限補間で 1 フレーム分求める（再生のレート合わせ用）。
    /// ホールドだと折り返しイメージが乗り、1 kHz のファイルでも 22 kHz まで鳴ってしまう。
    /// </summary>
    public static void ResampleFrameBandlimited(
        float[] interleaved,
        int channels,
        double frame,
        int frameCount,
        int sourceRate,
        int destRate,
        Span<float> dest)
    {
        channels = Math.Max(1, channels);
        var n = Math.Min(channels, dest.Length);
        if (frameCount <= 0 || interleaved.Length < channels || n <= 0)
        {
            dest.Clear();
            return;
        }

        var cutoff = LowpassCutoff(sourceRate, destRate);
        var center = (int)Math.Floor(frame);
        Span<double> sums = stackalloc double[ChannelLayout.MaxChannels];
        sums = sums[..n];
        sums.Clear();
        var wsum = 0d;
        for (var tap = -SincHalfWidth; tap <= SincHalfWidth; tap++)
        {
            var kernel = SincKernel(center + tap - frame, cutoff, tap);
            var index = ClampIndex(center + tap, frameCount) * channels;
            for (var ch = 0; ch < n; ch++)
            {
                sums[ch] += interleaved[index + ch] * kernel;
            }

            wsum += kernel;
        }

        var scale = Math.Abs(wsum) > 1e-8 ? 1d / wsum : 1d;
        for (var ch = 0; ch < n; ch++)
        {
            dest[ch] = (float)(sums[ch] * scale);
        }
    }

    private static double LowpassCutoff(int sourceRate, int destRate) =>
        0.5 * Math.Min(1d, destRate / (double)Math.Max(1, sourceRate)) * SincTransition;

    private static void WriteResampledFrame(
        float[] dest,
        float[] src,
        int channels,
        int srcFrames,
        int destFrame,
        double step,
        double[] table)
    {
        var srcFrame = destFrame * step;
        var destOffset = destFrame * channels;
        for (var ch = 0; ch < channels; ch++)
        {
            dest[destOffset + ch] = SampleSincTable(src, channels, ch, srcFrame, srcFrames, table);
        }
    }

    private static double[] BuildSincKernelTable(double cutoff)
    {
        var table = new double[(SincFracBins + 1) * SincTapCount];
        for (var bin = 0; bin <= SincFracBins; bin++)
        {
            var frac = bin / (double)SincFracBins;
            var row = bin * SincTapCount;
            for (var t = 0; t < SincTapCount; t++)
            {
                var tap = t - SincHalfWidth;
                table[row + t] = SincKernel(tap - frac, cutoff, tap);
            }
        }

        return table;
    }

    private static float SampleSincTable(
        float[] src,
        int channels,
        int channel,
        double frame,
        int frameCount,
        double[] table)
    {
        if (frameCount <= 1)
        {
            return src[channel];
        }

        var center = (int)Math.Floor(frame);
        var frac = frame - center;
        var scaled = frac * SincFracBins;
        var bin = (int)scaled;
        if (bin >= SincFracBins)
        {
            bin = SincFracBins - 1;
            scaled = SincFracBins;
        }

        var blend = scaled - bin;
        var row0 = bin * SincTapCount;
        var row1 = row0 + SincTapCount;
        var left = center - SincHalfWidth;
        var sum = 0d;
        var wsum = 0d;
        if (unchecked((uint)left) <= (uint)(frameCount - SincTapCount))
        {
            var index = left * channels + channel;
            for (var t = 0; t < SincTapCount; t++)
            {
                var kernel = table[row0 + t] + (table[row1 + t] - table[row0 + t]) * blend;
                sum += src[index] * kernel;
                wsum += kernel;
                index += channels;
            }
        }
        else
        {
            for (var t = 0; t < SincTapCount; t++)
            {
                var kernel = table[row0 + t] + (table[row1 + t] - table[row0 + t]) * blend;
                sum += src[ClampIndex(left + t, frameCount) * channels + channel] * kernel;
                wsum += kernel;
            }
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

    private static int ClampIndex(int index, int frameCount) =>
        Math.Clamp(index, 0, frameCount - 1);
}
