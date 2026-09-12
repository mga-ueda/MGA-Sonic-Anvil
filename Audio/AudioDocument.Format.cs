using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Audio;

internal sealed partial class AudioDocument
{
    public void CommitFormat()
    {
        _committedSampleRate = SampleRate;
        _committedBitsPerSample = BitsPerSample;
        _committedChannels = Channels;
        _committedFrameCount = FrameCount;
        _committedFileBytes = FileBytes;
    }

    public static long EstimatePcmPayloadBytes(long frames, int channels, int bitsPerSample)
    {
        var bytesPerSample = Math.Max(1, (bitsPerSample + 7) / 8);
        return Math.Max(0, frames) * (long)Math.Max(0, channels) * bytesPerSample;
    }

    public static long EstimateFileBytes(
        long committedFileBytes,
        long committedFrames,
        int committedChannels,
        int committedBitsPerSample,
        long frames,
        int channels,
        int bitsPerSample)
    {
        var committedPcm = EstimatePcmPayloadBytes(committedFrames, committedChannels, committedBitsPerSample);
        var currentPcm = EstimatePcmPayloadBytes(frames, channels, bitsPerSample);
        if (committedPcm <= 0)
        {
            return currentPcm;
        }

        var leftover = committedFileBytes - committedPcm;
        if (leftover >= 0)
        {
            return currentPcm + leftover;
        }

        // MP3 など、実ファイルが PCM より小さいとき。未変更なら実サイズのまま。
        if (currentPcm == committedPcm)
        {
            return committedFileBytes;
        }

        return Math.Max(0, (long)Math.Round(committedFileBytes * (currentPcm / (double)committedPcm)));
    }

    public long EstimateFrameCountForRate(int destRate)
    {
        if (destRate == SampleRate || destRate < 1 || SampleRate < 1)
        {
            return FrameCount;
        }

        return Math.Max(0, (long)Math.Round(FrameCount * (double)destRate / SampleRate));
    }

    public long EstimateFileBytesFor(int sampleRate, int bitsPerSample, int channels) =>
        EstimateFileBytes(
            _committedFileBytes,
            _committedFrameCount,
            _committedChannels,
            _committedBitsPerSample,
            EstimateFrameCountForRate(sampleRate),
            channels,
            bitsPerSample);

    public float[] FormatOriginSamples => _formatOriginSamples;

    public int FormatOriginSampleRate => _formatOriginSampleRate;

    public int FormatOriginChannels => _formatOriginChannels;

    public int FormatOriginBitsPerSample => _formatOriginBits;

    public long FormatOriginFrameCount =>
        _formatOriginChannels <= 0 ? 0 : _formatOriginSamples.Length / _formatOriginChannels;

    /// <summary>フェード等の中身の編集後。以降のレート／ビット変換の起点になる。</summary>
    public void CaptureFormatOrigin()
    {
        _formatOriginSamples = Interleaved;
        _formatOriginSampleRate = SampleRate;
        _formatOriginChannels = Channels;
        _formatOriginBits = BitsPerSample;
    }

    public void SetFormatOrigin(float[] samples, int sampleRate, int channels, int bitsPerSample)
    {
        _formatOriginSamples = samples;
        _formatOriginSampleRate = sampleRate;
        _formatOriginChannels = channels;
        _formatOriginBits = bitsPerSample;
    }

    /// <summary>直近の中身編集（または読み込み）時点の PCM から、指定フォーマットを作る。</summary>
    public float[] MaterializeFormat(
        int destRate,
        int destBits,
        int destChannels,
        IProgress<double>? progress = null)
    {
        var samples = _formatOriginSamples;
        var rate = Math.Max(1, _formatOriginSampleRate);
        var channels = Math.Max(1, _formatOriginChannels);
        destChannels = Math.Max(1, destChannels);
        destRate = Math.Max(1, destRate);
        if (channels != destChannels)
        {
            samples = FormatConvert.Remix(samples, channels, destChannels);
            channels = destChannels;
        }

        if (rate != destRate)
        {
            samples = FormatConvert.Resample(samples, channels, rate, destRate, progress);
        }

        if (destBits < _formatOriginBits)
        {
            samples = FormatConvert.Quantize(samples, destBits);
        }

        return samples;
    }

}
