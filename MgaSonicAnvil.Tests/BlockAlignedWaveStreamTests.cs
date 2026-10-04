using System.IO;
using System.Text;
using MgaSonicAnvil.Audio;
using NAudio.Wave;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class BlockAlignedWaveStreamTests
{
    [Fact]
    public void ShortReadsOnTwentyFourBitStereo_KeepTailPeak()
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-24bit-short-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WritePcm24Wave(path, sampleRate: 48000, frames: 4800, fmtExtraBytes: 0, junkBytes: 0, tailPeak: 0.91f);

            using var file = File.OpenRead(path);
            using var shortReads = new MaxReadStream(file, maxRead: 4);
            using var raw = new WaveFileReader(shortReads);
            using var aligned = new BlockAlignedWaveStream(raw);
            var provider = aligned.ToSampleProvider();
            var samples = ReadAll(provider, channels: 2);
            var frames = samples.Length / 2;
            Assert.True(frames >= 4700, $"frames={frames}");
            Assert.InRange(samples[^2], 0.8f, 1f);
            Assert.InRange(samples[^1], 0.8f, 1f);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void AdobeStyleFmt18AndJunk_OpensAndKeepsTail()
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-24bit-fmt18-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WritePcm24Wave(path, sampleRate: 48000, frames: 2400, fmtExtraBytes: 2, junkBytes: 458, tailPeak: 0.88f);

            using var stream = AudioCodec.OpenPlaybackStream(path);
            Assert.Equal(48000, stream.WaveFormat.SampleRate);
            Assert.Equal(2, stream.WaveFormat.Channels);
            Assert.Equal(24, stream.WaveFormat.BitsPerSample);
            Assert.Equal(6, stream.WaveFormat.BlockAlign);

            var peaks = PeakPyramid.BuildPlayerDisplayFromPath(path);
            Assert.False(peaks.IsEmpty);
            Assert.Equal(2400, peaks.FrameCount);
            Assert.Equal(2400, peaks.FilledFrames);

            var mins = new float[64];
            var maxs = new float[64];
            Assert.Equal(64, peaks.ReadRange(0, 2400, 64, 0, mins, maxs));
            Assert.True(maxs[^1] >= 0.8f, $"tailMax={maxs[^1]}");
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void StandardSixteenByteFmt24_KeepsTailThroughPlayerPeaks()
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-24bit-fmt16-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WritePcm24Wave(path, sampleRate: 48000, frames: 2400, fmtExtraBytes: 0, junkBytes: 0, tailPeak: 0.93f);
            var peaks = PeakPyramid.BuildPlayerDisplayFromPath(path);
            var mins = new float[32];
            var maxs = new float[32];
            Assert.Equal(32, peaks.ReadRange(0, peaks.FrameCount, 32, 0, mins, maxs));
            Assert.True(maxs[^1] >= 0.85f, $"tailMax={maxs[^1]}");
        }
        finally
        {
            TryDelete(path);
        }
    }

    private static float[] ReadAll(ISampleProvider provider, int channels)
    {
        var acc = new List<float>();
        var chunk = new float[channels * 256];
        while (true)
        {
            var n = provider.Read(chunk, 0, chunk.Length);
            if (n <= 0)
            {
                break;
            }

            for (var i = 0; i < n; i++)
            {
                acc.Add(chunk[i]);
            }
        }

        return [.. acc];
    }

    /// <summary>Sound Forge / Audition の 24-bit PCM（fmt 18 + JUNK あり）。</summary>
    private static void WritePcm24Wave(
        string path,
        int sampleRate,
        int frames,
        int fmtExtraBytes,
        int junkBytes,
        float tailPeak)
    {
        const int channels = 2;
        const int bits = 24;
        var block = channels * (bits / 8);
        var dataBytes = frames * block;
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        var riffSizePos = stream.Position;
        writer.Write(0);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));

        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16 + Math.Max(0, fmtExtraBytes));
        writer.Write((ushort)1);
        writer.Write((ushort)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * block);
        writer.Write((ushort)block);
        writer.Write((ushort)bits);
        for (var i = 0; i < fmtExtraBytes; i++)
        {
            writer.Write((byte)0);
        }

        if (junkBytes > 0)
        {
            writer.Write(Encoding.ASCII.GetBytes("JUNK"));
            writer.Write(junkBytes);
            writer.Write(new byte[junkBytes]);
        }

        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataBytes);
        for (var frame = 0; frame < frames; frame++)
        {
            var amp = frame >= frames - 8 ? tailPeak : 0.05f;
            WritePcm24(writer, amp);
            WritePcm24(writer, amp);
        }

        var end = stream.Position;
        stream.Position = riffSizePos;
        writer.Write((int)(end - 8));
    }

    private static void WritePcm24(BinaryWriter writer, float sample)
    {
        var v = (int)Math.Round(Math.Clamp(sample, -1f, 1f) * 8388607f);
        writer.Write((byte)v);
        writer.Write((byte)(v >> 8));
        writer.Write((byte)(v >> 16));
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }

    private sealed class MaxReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly int _maxRead;

        public MaxReadStream(Stream inner, int maxRead)
        {
            _inner = inner;
            _maxRead = Math.Max(1, maxRead);
        }

        public override bool CanRead => true;

        public override bool CanSeek => _inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, Math.Min(count, _maxRead));

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
