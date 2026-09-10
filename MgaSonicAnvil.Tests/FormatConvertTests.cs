using System.IO;
using System.Text;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Editing;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class FormatConvertTests
{
    [Fact]
    public void Resample_ScalesLengthAndKeepsDuration()
    {
        var source = MakeSine(frames: 480, sampleRate: 48000);
        var dest = FormatConvert.Resample(source, channels: 2, sourceRate: 48000, destRate: 44100);
        Assert.Equal(2 * (int)Math.Round(480 * 44100 / 48000d), dest.Length);
    }

    [Fact]
    public void Resample_ReportsProgressFromZeroToOne()
    {
        var reports = new List<double>();
        var source = MakeSine(frames: 480, sampleRate: 48000);
        FormatConvert.Resample(
            source,
            channels: 2,
            sourceRate: 48000,
            destRate: 44100,
            new CollectProgress(reports));
        Assert.Contains(0d, reports);
        Assert.Contains(1d, reports);
        Assert.True(reports[0] <= reports[^1]);
    }

    [Fact]
    public void Resample_DownsampleRemovesContentAboveNewNyquist()
    {
        var source = MakeSine(frames: 48000, sampleRate: 48000, frequency: 6000);
        var dest = FormatConvert.Resample(source, channels: 2, sourceRate: 48000, destRate: 8000);
        Assert.True(Rms(dest) < Rms(source) * 0.08);
    }

    [Fact]
    public void Resample_KeepsContentBelowNewNyquist()
    {
        var source = MakeSine(frames: 48000, sampleRate: 48000, frequency: 1000);
        var dest = FormatConvert.Resample(source, channels: 2, sourceRate: 48000, destRate: 8000);
        Assert.True(Rms(dest) > Rms(source) * 0.7);
    }

    [Fact]
    public void ConvertSampleRate_RemapsMarkersAndMarksDirty()
    {
        var document = MakeDocument(frames: 480, sampleRate: 48000);
        document.TryAddMarker(240);
        document.SetSampleLoop(new WaveSelection(120, 360), markDirty: false);
        document.SetRegion(new WaveSelection(80, 200), markDirty: false);
        document.MarkSaved("a.wav", AudioFileKind.Wave);

        var command = ProcessEdits.ConvertSampleRate(document, 24000);
        Assert.NotNull(command);
        var history = new EditHistory();
        history.Do(document, command);

        Assert.Equal(24000, document.SampleRate);
        Assert.Equal(240, document.FrameCount);
        Assert.Equal(120, document.Markers[0].Frame);
        Assert.Equal(new WaveSelection(60, 180), document.SampleLoop);
        Assert.Equal(new WaveSelection(40, 100), document.Region);
        Assert.True(document.IsDirty);
        Assert.True(document.SampleRateEdited);
        Assert.True(document.FormatEdited);
        Assert.False(document.BitDepthEdited);
        Assert.False(document.ChannelsEdited);
        Assert.Equal(
            AudioDocument.EstimateFileBytes(document.CommittedFileBytes, 480, 2, 16, 240, 2, 16),
            document.EstimatedFileBytes);

        Assert.True(history.Undo(document));
        Assert.Equal(48000, document.SampleRate);
        Assert.False(document.SampleRateEdited);
        Assert.Equal(480, document.FrameCount);
        Assert.Equal(240, document.Markers[0].Frame);
    }

    [Fact]
    public void SaveWave_KeepsConvertedEightKhzInFmtChunk()
    {
        var document = MakeDocument(frames: 480, sampleRate: 48000);
        new EditHistory().Do(document, ProcessEdits.ConvertSampleRate(document, 8000)!);
        Assert.Equal(8000, document.SampleRate);

        var path = Path.Combine(Path.GetTempPath(), $"sonic-anvil-8k-{Guid.NewGuid():N}.wav");
        try
        {
            AudioCodec.SaveWave(document, path);
            var fmt = ReadFmt(path);
            Assert.Equal(16, fmt.ChunkSize);
            Assert.Equal(1, fmt.FormatTag);
            Assert.Equal(2, fmt.Channels);
            Assert.Equal(8000, fmt.SampleRate);
            Assert.Equal(16, fmt.BitsPerSample);
            Assert.Equal(4, fmt.BlockAlign);
            Assert.Equal(32000, fmt.ByteRate);

            var loaded = AudioCodec.Load(path);
            Assert.Equal(8000, loaded.SampleRate);
            Assert.Equal(document.FrameCount, loaded.FrameCount);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void ConvertSampleRate_TwentyFourBitToEightKhzShrinksFrames()
    {
        var document = MakeDocument(frames: 4800, sampleRate: 48000, bits: 24);
        new EditHistory().Do(document, ProcessEdits.ConvertSampleRate(document, 8000)!);
        Assert.Equal(8000, document.SampleRate);
        Assert.Equal(24, document.BitsPerSample);
        Assert.Equal(800, document.FrameCount);

        var path = Path.Combine(Path.GetTempPath(), $"sonic-anvil-8k24-{Guid.NewGuid():N}.wav");
        try
        {
            AudioCodec.SaveWave(document, path);
            var fmt = ReadFmt(path);
            Assert.Equal(40, fmt.ChunkSize);
            Assert.Equal(0xFFFE, fmt.FormatTag);
            Assert.Equal(8000, fmt.SampleRate);
            Assert.Equal(24, fmt.BitsPerSample);
            Assert.Equal(6, fmt.BlockAlign);
            Assert.Equal(48000, fmt.ByteRate);

            var loaded = AudioCodec.Load(path);
            Assert.Equal(8000, loaded.SampleRate);
            Assert.Equal(24, loaded.BitsPerSample);
            Assert.Equal(800, loaded.FrameCount);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void SaveWave_DeletesStaleSoundForgePeakCache()
    {
        var document = MakeDocument(frames: 80, sampleRate: 8000);
        var path = Path.Combine(Path.GetTempPath(), $"sonic-anvil-8k-sfk-{Guid.NewGuid():N}.wav");
        var sfk = Path.ChangeExtension(path, ".sfk");
        try
        {
            File.WriteAllText(sfk, "stale");
            AudioCodec.SaveWave(document, path);
            Assert.False(File.Exists(sfk));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            if (File.Exists(sfk))
            {
                File.Delete(sfk);
            }
        }
    }

    [Fact]
    public void SaveWave_EightKhzLoopWritesNanosecondPeriod()
    {
        var document = MakeDocument(frames: 80, sampleRate: 8000);
        document.SetSampleLoop(new WaveSelection(8, 40), markDirty: false);
        var path = Path.Combine(Path.GetTempPath(), $"sonic-anvil-8k-loop-{Guid.NewGuid():N}.wav");
        try
        {
            AudioCodec.SaveWave(document, path);
            var fmt = ReadFmt(path);
            Assert.Equal(16, fmt.ChunkSize);
            Assert.Equal(8000, fmt.SampleRate);
            Assert.Equal(125000u, ReadSmplPeriod(path));

            var loaded = AudioCodec.Load(path);
            Assert.Equal(8000, loaded.SampleRate);
            Assert.Equal(new WaveSelection(8, 40), loaded.SampleLoop);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void ConvertSampleRate_SecondConvertUsesOriginalRate()
    {
        var source = MakeSine(frames: 4800, sampleRate: 48000, frequency: 400);
        var document = new AudioDocument((float[])source.Clone(), 48000, 2, 16, AudioFileKind.Wave, null);
        var history = new EditHistory();
        history.Do(document, ProcessEdits.ConvertSampleRate(document, 24000)!);
        history.Do(document, ProcessEdits.ConvertSampleRate(document, 12000)!);

        var direct = FormatConvert.Resample(source, 2, 48000, 12000);
        Assert.Equal(direct.Length, document.Interleaved.Length);
        var err = 0d;
        for (var i = 0; i < direct.Length; i++)
        {
            var d = document.Interleaved[i] - direct[i];
            err += d * d;
        }

        Assert.True(Math.Sqrt(err / direct.Length) < 1e-6);
    }

    [Fact]
    public void ConvertBitDepth_SecondLoweringUsesOriginalPrecision()
    {
        var document = MakeDocument(frames: 8, sampleRate: 48000, bits: 24);
        document.Interleaved[0] = 0.1234567f;
        document.CaptureFormatOrigin();
        var history = new EditHistory();
        history.Do(document, ProcessEdits.ConvertBitDepth(document, 16)!);
        history.Do(document, ProcessEdits.ConvertBitDepth(document, 8)!);
        Assert.Equal(FormatConvert.Quantize([0.1234567f], 8)[0], document.Interleaved[0]);
    }

    [Fact]
    public void ConvertSampleRate_SameRateIsNoOp()
    {
        var document = MakeDocument(frames: 100, sampleRate: 48000);
        Assert.Null(ProcessEdits.ConvertSampleRate(document, 48000));
    }

    [Fact]
    public void ConvertBitDepth_QuantizesWhenLowering()
    {
        var document = MakeDocument(frames: 8, sampleRate: 48000, bits: 24);
        document.Interleaved[0] = 0.1234567f;
        var command = ProcessEdits.ConvertBitDepth(document, 16);
        Assert.NotNull(command);
        new EditHistory().Do(document, command);
        Assert.Equal(16, document.BitsPerSample);
        Assert.Equal(FormatConvert.Quantize([0.1234567f], 16)[0], document.Interleaved[0]);
        Assert.True(document.IsDirty);
        Assert.True(document.BitDepthEdited);
        Assert.False(document.SampleRateEdited);
        document.MarkSaved("b.wav", AudioFileKind.Wave);
        Assert.False(document.BitDepthEdited);
    }

    [Fact]
    public void ConvertBitDepth_SupportsEightBit()
    {
        var document = MakeDocument(frames: 8, sampleRate: 48000, bits: 16);
        document.Interleaved[0] = 0.37f;
        var command = ProcessEdits.ConvertBitDepth(document, 8);
        Assert.NotNull(command);
        new EditHistory().Do(document, command);
        Assert.Equal(8, document.BitsPerSample);
        Assert.Equal(FormatConvert.Quantize([0.37f], 8)[0], document.Interleaved[0]);
    }

    [Fact]
    public void SaveWave_WritesEightBitUnsignedPcm()
    {
        var document = MakeDocument(frames: 32, sampleRate: 8000, bits: 8);
        document.Interleaved[0] = 1f;
        document.Interleaved[1] = -1f;
        var path = Path.Combine(Path.GetTempPath(), $"sonic-anvil-8bit-{Guid.NewGuid():N}.wav");
        try
        {
            AudioCodec.SaveWave(document, path);
            var fmt = ReadFmt(path);
            Assert.Equal(16, fmt.ChunkSize);
            Assert.Equal(1, fmt.FormatTag);
            Assert.Equal(8, fmt.BitsPerSample);
            Assert.Equal(2, fmt.BlockAlign);
            Assert.Equal(16000, fmt.ByteRate);

            var loaded = AudioCodec.Load(path);
            Assert.Equal(8, loaded.BitsPerSample);
            Assert.Equal(8000, loaded.SampleRate);
            Assert.Equal(32, loaded.FrameCount);
            Assert.True(loaded.Interleaved[0] > 0.9f);
            Assert.True(loaded.Interleaved[1] < -0.9f);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void ConvertBitDepth_SupportsFourBit()
    {
        var document = MakeDocument(frames: 8, sampleRate: 48000, bits: 16);
        document.Interleaved[0] = 0.37f;
        var command = ProcessEdits.ConvertBitDepth(document, 4);
        Assert.NotNull(command);
        new EditHistory().Do(document, command);
        Assert.Equal(4, document.BitsPerSample);
        Assert.Equal(FormatConvert.Quantize([0.37f], 4)[0], document.Interleaved[0]);
        Assert.Equal(3f / 7f, document.Interleaved[0], 5);
    }

    [Fact]
    public void Playback_HoldsSourceSamplesOnDeviceClock()
    {
        Assert.True(FormatConvert.ShouldHoldForDevice(8000, 48000));
        Assert.True(FormatConvert.ShouldResampleForDevice(8000, 48000));
        Assert.False(FormatConvert.ShouldHoldForDevice(48000, 48000));
        Assert.False(FormatConvert.ShouldResampleForDevice(48000, 48000));

        var samples = new float[8];
        samples[0] = 1f;
        samples[1] = 1f;
        samples[2] = -1f;
        samples[3] = -1f;
        var document = new AudioDocument(samples, 8000, 2, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(48000);
        provider.Bind(document, 0, null, loop: false);

        var buffer = new float[24];
        Assert.Equal(24, provider.Read(buffer, 0, buffer.Length));
        var left = Enumerable.Range(0, 12).Select(i => buffer[i * 2]).ToArray();
        Assert.All(left, sample => Assert.True(sample is 1f or -1f));
        Assert.Equal(1f, left[0]);
        Assert.Equal(-1f, left[^1]);
        Assert.Equal(2, left.Distinct().Count());
    }

    [Fact]
    public void Playback_ConvertedMonoPlaysOnBothChannels()
    {
        var document = MakeDocument(frames: 4, sampleRate: 48000);
        document.Interleaved[0] = 0.8f;
        document.Interleaved[1] = -0.2f;
        new EditHistory().Do(document, ProcessEdits.ConvertChannels(document, 1)!);

        var provider = new PlaybackSampleProvider();
        provider.Bind(document, 0, null, loop: false);
        Assert.Equal(2, provider.WaveFormat.Channels);

        var buffer = new float[8];
        Assert.Equal(8, provider.Read(buffer, 0, buffer.Length));
        var expected = 0.5f * (0.8f + -0.2f);
        Assert.Equal(expected, buffer[0], 5);
        Assert.Equal(expected, buffer[1], 5);
    }

    [Fact]
    public void ConvertChannels_StereoToMono()
    {
        var document = MakeDocument(frames: 4, sampleRate: 48000);
        document.Interleaved[0] = 1f;
        document.Interleaved[1] = -1f;
        var command = ProcessEdits.ConvertChannels(document, 1);
        Assert.NotNull(command);
        new EditHistory().Do(document, command);
        Assert.Equal(1, document.Channels);
        Assert.Equal(4, document.FrameCount);
        Assert.Equal(0f, document.Interleaved[0], 5);
        Assert.True(document.ChannelsEdited);
        Assert.False(document.SampleRateEdited);
        Assert.False(document.BitDepthEdited);
    }

    [Fact]
    public void ConvertFormat_ClearsSelection()
    {
        var document = MakeDocument(frames: 480, sampleRate: 48000);
        document.Selection = new WaveSelection(10, 80);
        new EditHistory().Do(document, ProcessEdits.ConvertSampleRate(document, 24000)!);
        Assert.True(document.Selection.IsEmpty);

        document.Selection = new WaveSelection(0, 40);
        new EditHistory().Do(document, ProcessEdits.ConvertBitDepth(document, 24)!);
        Assert.True(document.Selection.IsEmpty);

        document.Selection = new WaveSelection(0, 20);
        new EditHistory().Do(document, ProcessEdits.ConvertChannels(document, 1)!);
        Assert.True(document.Selection.IsEmpty);
    }

    [Fact]
    public void EstimateFileBytesFor_ScalesWithHighlightedRate()
    {
        var document = MakeDocument(frames: 48000, sampleRate: 48000);
        var half = document.EstimateFileBytesFor(24000, document.BitsPerSample, document.Channels);
        Assert.Equal(document.CommittedFileBytes / 2, half);
        Assert.Equal(document.CommittedFileBytes, document.EstimateFileBytesFor(48000, 16, 2));
    }

    [Fact]
    public void EstimateFileBytes_KeepsHeaderAndScalesPayload()
    {
        var committedPcm = AudioDocument.EstimatePcmPayloadBytes(48000, 2, 16);
        var destPcm = AudioDocument.EstimatePcmPayloadBytes(24000, 1, 24);
        var header = 128;
        Assert.Equal(
            destPcm + header,
            AudioDocument.EstimateFileBytes(committedPcm + header, 48000, 2, 16, 24000, 1, 24));
    }

    [Fact]
    public void EstimateFileBytes_KeepsCompressedSizeWhenUnchanged()
    {
        var pcm = AudioDocument.EstimatePcmPayloadBytes(48000, 2, 16);
        var compressed = Math.Max(1, pcm / 10);
        Assert.Equal(
            compressed,
            AudioDocument.EstimateFileBytes(compressed, 48000, 2, 16, 48000, 2, 16));
    }

    [Fact]
    public void EstimateFileBytes_ScalesCompressedSizeWithPayload()
    {
        var pcm = AudioDocument.EstimatePcmPayloadBytes(48000, 2, 16);
        var compressed = Math.Max(1, pcm / 10);
        Assert.Equal(
            compressed / 2,
            AudioDocument.EstimateFileBytes(compressed, 48000, 2, 16, 24000, 2, 16));
    }

    [Fact]
    public void Mp3Load_DoesNotMarkFileSizeEdited()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mga-mp3-size-{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(path, new byte[1234]);
            var document = new AudioDocument(new float[48000 * 2], 48000, 2, 16, AudioFileKind.Mp3, path);
            Assert.Equal(1234, document.CommittedFileBytes);
            Assert.Equal(1234, document.EstimatedFileBytes);
            Assert.Equal(1234, document.EstimateFileBytesFor(48000, 16, 2));
            Assert.False(document.FileSizeEdited);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void CustomRate_Bounds()
    {
        Assert.True(FormatConvert.IsValidSampleRate(44100));
        Assert.True(FormatConvert.IsValidSampleRate(12345));
        Assert.False(FormatConvert.IsValidSampleRate(999));
        Assert.False(FormatConvert.IsValidSampleRate(400000));
    }

    [Theory]
    [InlineData(false, false, 1)]
    [InlineData(true, false, 10)]
    [InlineData(false, true, 100)]
    [InlineData(true, true, 1000)]
    public void SampleRateNudgeStep_UsesModifiers(bool shift, bool control, int expected)
    {
        Assert.Equal(expected, FormatConvert.SampleRateNudgeStep(shift, control));
    }

    [Fact]
    public void ApplySampleRateNudge_ClampsToValidRange()
    {
        Assert.Equal(48001, FormatConvert.ApplySampleRateNudge(48000, 1, 1));
        Assert.Equal(47990, FormatConvert.ApplySampleRateNudge(48000, -1, 10));
        Assert.Equal(FormatConvert.MinSampleRate, FormatConvert.ApplySampleRateNudge(1005, -1, 10));
        Assert.Equal(FormatConvert.MaxSampleRate, FormatConvert.ApplySampleRateNudge(383500, 1, 1000));
    }

    private static (int ChunkSize, ushort FormatTag, ushort Channels, int SampleRate, int ByteRate, ushort BlockAlign, ushort BitsPerSample) ReadFmt(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(reader.ReadBytes(4)));
        _ = reader.ReadUInt32();
        Assert.Equal("WAVE", Encoding.ASCII.GetString(reader.ReadBytes(4)));
        while (stream.Position + 8 <= stream.Length)
        {
            var id = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var size = reader.ReadInt32();
            if (id == "fmt ")
            {
                var formatTag = reader.ReadUInt16();
                var channels = reader.ReadUInt16();
                var sampleRate = reader.ReadInt32();
                var byteRate = reader.ReadInt32();
                var blockAlign = reader.ReadUInt16();
                var bits = reader.ReadUInt16();
                return (size, formatTag, channels, sampleRate, byteRate, blockAlign, bits);
            }

            stream.Position += size + (size & 1);
        }

        throw new InvalidDataException("fmt chunk missing");
    }

    private static uint ReadSmplPeriod(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(reader.ReadBytes(4)));
        _ = reader.ReadUInt32();
        Assert.Equal("WAVE", Encoding.ASCII.GetString(reader.ReadBytes(4)));
        while (stream.Position + 8 <= stream.Length)
        {
            var id = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var size = reader.ReadInt32();
            if (id == "smpl" && size >= 12)
            {
                _ = reader.ReadUInt32();
                _ = reader.ReadUInt32();
                return reader.ReadUInt32();
            }

            stream.Position += size + (size & 1);
        }

        throw new InvalidDataException("smpl chunk missing");
    }

    private static AudioDocument MakeDocument(int frames, int sampleRate, int bits = 16)
    {
        return new AudioDocument(new float[frames * 2], sampleRate, 2, bits, AudioFileKind.Wave, null);
    }

    private static float[] MakeSine(int frames, int sampleRate, double frequency = 440)
    {
        var samples = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var value = (float)Math.Sin(2 * Math.PI * frequency * i / sampleRate);
            samples[i * 2] = value;
            samples[i * 2 + 1] = value;
        }

        return samples;
    }

    private static double Rms(float[] samples)
    {
        var sum = 0d;
        foreach (var value in samples)
        {
            sum += value * (double)value;
        }

        return Math.Sqrt(sum / Math.Max(1, samples.Length));
    }

    private sealed class CollectProgress(List<double> values) : IProgress<double>
    {
        public void Report(double value) => values.Add(value);
    }
}
