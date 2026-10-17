using System.Collections.Concurrent;
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

    private const int SincFracBins = 1024;
    private const int ParallelFrameThreshold = 4096;

    /// <summary>
    /// 通過帯域は低い方のナイキストの 90% まで平坦、阻止域は 100 dB。
    /// Sound Forge の既定（補間精度 2 ＋アンチエイリアス）が「ハイエンド」としている帯に合わせる。
    /// 精度 4 の最長カーネルまでは伸ばさない。
    /// </summary>
    private const double PassbandFraction = 0.90;

    private const double StopbandAttenuationDb = 100;
    private const int MinHalfWidth = 16;
    private const int MaxHalfWidth = 512;
    private const double KaiserBeta = 0.1102 * (StopbandAttenuationDb - 8.7);
    private static readonly double KaiserI0Beta = BesselI0(KaiserBeta);
    private static readonly ConcurrentDictionary<(int Source, int Dest), double[]> KernelCache = new();

    /// <summary>どのレート比でも足りる、カーネルが参照する最大余白。</summary>
    public static int ResampleEdgePad => MaxHalfWidth;

    /// <summary>そのレート比のカーネルが端で参照する余白。</summary>
    public static int ResampleMargin(int sourceRate, int destRate) =>
        HalfWidth(Math.Max(1, sourceRate), Math.Max(1, destRate));

    public static bool ShouldResampleForDevice(int sourceRate, int deviceRate) =>
        sourceRate > 0 && deviceRate > 0 && sourceRate != deviceRate;

    /// <summary>
    /// 低い方のナイキストで帯域制限してから間引く／補間する。
    /// アンチエイリアスは常に入る。ホールドだと階段波の高調波が出て、8 kHz がキンキンする。
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
        var table = KernelTable(sourceRate, destRate);
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
        var table = KernelTable(sampleRate, destRate);
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

    /// <summary>
    /// ビットを落とす。POW-r #1（Dithering）と同じ構成。
    /// 1 LSB の三角ディザをチャンネルごとにナイキストへ変調し、量子化誤差は可聴帯ではほぼ平坦でナイキスト付近だけ持ち上がる FIR で戻す。
    /// 種は固定なので、同じ入力はいつも同じ結果になる。
    /// </summary>
    public static float[] Quantize(float[] samples, int bits, int channels = 1)
    {
        if (bits >= 24 || samples.Length == 0)
        {
            return samples;
        }

        bits = Math.Clamp(bits, 4, 24);
        channels = Math.Max(1, channels);
        var levels = (double)Math.Max(1, (1 << (bits - 1)) - 1);
        // 直流利得 0。可聴域は ±2 dB 以内、ナイキストで約 +2 dB。
        const double feedback1 = 0.15;
        const double feedback2 = -0.15;
        var error1 = new double[channels];
        var error2 = new double[channels];
        var nyquistSign = new int[channels];
        var random = new Random[channels];
        for (var ch = 0; ch < channels; ch++)
        {
            nyquistSign[ch] = 1;
            random[ch] = new Random(0x504F5752 + ch);
        }

        var dest = new float[samples.Length];
        for (var i = 0; i < samples.Length; i++)
        {
            var ch = i % channels;
            var rng = random[ch];
            var triangular = (rng.NextDouble() - 0.5) + (rng.NextDouble() - 0.5);
            var dither = triangular * nyquistSign[ch] / levels;
            nyquistSign[ch] = -nyquistSign[ch];

            var feedback = (feedback1 * error1[ch]) + (feedback2 * error2[ch]);
            var shaped = samples[i] + dither - feedback;
            var quantized = Math.Clamp(Math.Round(shaped * levels), -levels, levels);
            var y = quantized / levels;
            var roundingError = y - shaped;
            error2[ch] = error1[ch];
            error1[ch] = roundingError;
            dest[i] = (float)y;
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

        var table = KernelTable(sourceRate, destRate);
        var tapCount = TapCount(table);
        var half = tapCount / 2;
        var center = (int)Math.Floor(frame);
        var leftIndex = center - half;
        var sumL = 0d;
        var sumR = 0d;
        var wsum = 0d;
        KernelRows(frame - center, tapCount, out var row0, out var row1, out var blend);
        for (var t = 0; t < tapCount; t++)
        {
            var kernel = table[row0 + t] + (table[row1 + t] - table[row0 + t]) * blend;
            wsum += kernel;
            var index = leftIndex + t;
            if (unchecked((uint)index) >= (uint)frameCount)
            {
                continue;
            }

            ChannelMix.Downmix(
                interleaved,
                index * channels,
                channels,
                out var tapL,
                out var tapR);
            sumL += tapL * kernel;
            sumR += tapR * kernel;
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

        var table = KernelTable(sourceRate, destRate);
        for (var ch = 0; ch < n; ch++)
        {
            dest[ch] = SampleSincTable(interleaved, channels, ch, frame, frameCount, table);
        }
    }

    /// <summary>遷移帯の中央。通過域の端から、低い方のナイキストまでを阻止域にする。</summary>
    private static double LowpassCutoff(int sourceRate, int destRate)
    {
        var nyquist = 0.5 * Math.Min(1d, destRate / (double)Math.Max(1, sourceRate));
        return nyquist * (PassbandFraction + 1d) * 0.5;
    }

    private static int HalfWidth(int sourceRate, int destRate)
    {
        var ratio = Math.Min(1d, destRate / (double)Math.Max(1, sourceRate));
        var transition = Math.Max(1e-4, (1d - PassbandFraction) * 0.5 * ratio);
        var length = (int)Math.Ceiling((StopbandAttenuationDb - 8d) / (2.285 * 2d * Math.PI * transition)) + 1;
        if ((length & 1) == 0)
        {
            length++;
        }

        return Math.Clamp(length / 2, MinHalfWidth, MaxHalfWidth);
    }

    private static double[] KernelTable(int sourceRate, int destRate) =>
        KernelCache.GetOrAdd((sourceRate, destRate), static key =>
            BuildSincKernelTable(LowpassCutoff(key.Source, key.Dest), HalfWidth(key.Source, key.Dest)));

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

    private static double[] BuildSincKernelTable(double cutoff, int halfWidth)
    {
        var tapCount = halfWidth * 2 + 1;
        var table = new double[(SincFracBins + 1) * tapCount];
        for (var bin = 0; bin <= SincFracBins; bin++)
        {
            var frac = bin / (double)SincFracBins;
            var row = bin * tapCount;
            for (var t = 0; t < tapCount; t++)
            {
                table[row + t] = SincKernel((t - halfWidth) - frac, cutoff, halfWidth);
            }
        }

        return table;
    }

    private static int TapCount(double[] table) => table.Length / (SincFracBins + 1);

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

        var tapCount = TapCount(table);
        var half = tapCount / 2;
        var center = (int)Math.Floor(frame);
        KernelRows(frame - center, tapCount, out var row0, out var row1, out var blend);
        var left = center - half;
        var sum = 0d;
        var wsum = 0d;
        if (left >= 0 && tapCount <= frameCount && left <= frameCount - tapCount)
        {
            var index = left * channels + channel;
            for (var t = 0; t < tapCount; t++)
            {
                var kernel = table[row0 + t] + (table[row1 + t] - table[row0 + t]) * blend;
                sum += src[index] * kernel;
                wsum += kernel;
                index += channels;
            }
        }
        else
        {
            for (var t = 0; t < tapCount; t++)
            {
                var kernel = table[row0 + t] + (table[row1 + t] - table[row0 + t]) * blend;
                wsum += kernel;
                var index = left + t;
                if (unchecked((uint)index) < (uint)frameCount)
                {
                    sum += src[index * channels + channel] * kernel;
                }
            }
        }

        return (float)(Math.Abs(wsum) > 1e-8 ? sum / wsum : sum);
    }

    private static void KernelRows(double frac, int tapCount, out int row0, out int row1, out double blend)
    {
        var scaled = frac * SincFracBins;
        var bin = (int)scaled;
        if (bin >= SincFracBins)
        {
            bin = SincFracBins - 1;
            scaled = SincFracBins;
        }

        if (bin < 0)
        {
            bin = 0;
            scaled = 0;
        }

        blend = scaled - bin;
        row0 = bin * tapCount;
        row1 = row0 + tapCount;
    }

    private static double SincKernel(double offset, double cutoff, int halfWidth)
    {
        if (halfWidth < 1 || Math.Abs(offset) > halfWidth)
        {
            return 0;
        }

        var radial = offset / halfWidth;
        var window = BesselI0(KaiserBeta * Math.Sqrt(1d - radial * radial)) / KaiserI0Beta;
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

    private static double BesselI0(double x)
    {
        var sum = 1d;
        var term = 1d;
        var half = x * 0.5;
        for (var k = 1; k < 40; k++)
        {
            term *= half / k;
            term *= half / k;
            sum += term;
            if (term < 1e-16 * sum)
            {
                break;
            }
        }

        return sum;
    }
}
