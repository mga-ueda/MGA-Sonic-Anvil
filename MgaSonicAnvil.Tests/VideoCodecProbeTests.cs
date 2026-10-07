using System.Buffers.Binary;
using System.IO;
using System.Text;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class VideoCodecProbeTests
{
    [Fact]
    public void DirectPlay_JpegAndMjpegOnly()
    {
        Assert.True(VideoCodecProbe.IsDirectPlayFamily("jpeg"));
        Assert.True(VideoCodecProbe.IsDirectPlayFamily("mjpa"));
        Assert.True(VideoCodecProbe.IsDirectPlayFamily("MJPG"));
        Assert.False(VideoCodecProbe.IsDirectPlayFamily("apcn"));
        Assert.False(VideoCodecProbe.IsDirectPlayFamily("avc1"));
        Assert.False(VideoCodecProbe.IsDirectPlayFamily("hvc1"));
    }

    [Fact]
    public void ProResPresentationMov_NeedsProxyWhenPresent()
    {
        var path = @"V:\共有ドライブ\HAL Internship\市原由稀\20261001\01_就職プレゼンテーション作品\04_MA作品.mov";
        if (!File.Exists(path))
        {
            return;
        }

        Assert.True(VideoCodecProbe.TryReadVideoFourCcs(path, out var tags));
        Assert.Contains("apcn", tags, StringComparer.OrdinalIgnoreCase);
        Assert.False(VideoCodecProbe.CanPlayWithoutProxy(path));
    }

    [Fact]
    public void ReadsStsdFourcc_PhotoJpegAndProRes()
    {
        var jpeg = WriteTemp(BuildMovie("jpeg"));
        var prores = WriteTemp(BuildMovie("apcn"));
        try
        {
            Assert.True(VideoCodecProbe.TryReadVideoFourCcs(jpeg, out var jpegTags));
            Assert.Contains("jpeg", jpegTags, StringComparer.Ordinal);
            Assert.True(VideoCodecProbe.CanPlayWithoutProxy(jpeg));

            Assert.True(VideoCodecProbe.TryReadVideoFourCcs(prores, out var proresTags));
            Assert.Contains("apcn", proresTags, StringComparer.Ordinal);
            Assert.False(VideoCodecProbe.CanPlayWithoutProxy(prores));
        }
        finally
        {
            File.Delete(jpeg);
            File.Delete(prores);
        }
    }

    [Fact]
    public void ReadsMvhdDuration_WithoutAudioTrack()
    {
        var path = WriteTemp(BuildMovie("avc1", timescale: 600, duration: 22200));
        try
        {
            Assert.True(VideoCodecProbe.TryRead(path, out var codecs, out var seconds));
            Assert.Contains("avc1", codecs, StringComparer.Ordinal);
            Assert.Equal(37, seconds, 3);

            Assert.True(AudioTagProbe.TryRead(path, out var tags));
            Assert.Equal(37, tags.DurationSeconds, 3);
            Assert.Equal(0, tags.SampleRate);
            Assert.Equal(0, tags.Channels);

            var document = AudioDocument.CreateDeferred(path);
            AudioTagProbe.Ensure(document);
            document.ActivateStreamPlayback(48000, 2, 16, 48000);
            var row = LibraryBrowserView.CreateRow(new DocumentSession(document));
            Assert.Equal(37, row.DurationSeconds, 3);
            Assert.Equal(UiStrings.FormatDuration(37), row.DurationText);
            Assert.Equal(string.Empty, row.SampleRateText);
            Assert.Equal(string.Empty, row.BitDepthText);
            Assert.Equal(string.Empty, row.ChannelsText);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void JpegPlusIpcm_IsDirectPlayAndReadsAudio()
    {
        var path = WriteTemp(BuildMovieWithIpcm("jpeg", includePcmC: true));
        try
        {
            Assert.True(VideoCodecProbe.TryRead(
                path,
                out var codecs,
                out _,
                out var rate,
                out var channels,
                out var bits));
            Assert.Contains("jpeg", codecs, StringComparer.Ordinal);
            Assert.DoesNotContain("ipcm", codecs, StringComparer.OrdinalIgnoreCase);
            Assert.True(VideoCodecProbe.CanPlayWithoutProxy(path));
            Assert.Equal(48000, rate);
            Assert.Equal(2, channels);
            Assert.Equal(24, bits);

            Assert.True(AudioTagProbe.TryRead(path, out var tags));
            Assert.Equal(48000, tags.SampleRate);
            Assert.Equal(2, tags.Channels);
            Assert.Equal(24, tags.BitsPerSample);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ContestIpcmMp4_ReadsAudioWhenPresent()
    {
        var path = @"V:\共有ドライブ\HAL Internship\バイヤージョシュ\20261001\効果音\コンテスト\コンテスト作品 .mp4";
        if (!File.Exists(path))
        {
            return;
        }

        Assert.True(VideoCodecProbe.TryRead(
            path,
            out var codecs,
            out var seconds,
            out var rate,
            out var channels,
            out var bits));
        Assert.Contains("avc1", codecs, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("ipcm", codecs, StringComparer.OrdinalIgnoreCase);
        Assert.InRange(seconds, 90, 96);
        Assert.Equal(48000, rate);
        Assert.Equal(2, channels);
        Assert.Equal(24, bits);

        var document = AudioDocument.CreateDeferred(path);
        document.ApplyTags(new AudioFileTags { Probed = true, DurationSeconds = seconds });
        Assert.Equal(0, document.Tags.SampleRate);
        Assert.True(AudioTagProbe.TryRead(path, out var fresh) && fresh.SampleRate > 0);
        document.ApplyTags(fresh);
        Assert.Equal(48000, document.Tags.SampleRate);
        Assert.True(AudioCodec.TryActivateStreamPlayback(document));
        Assert.Equal(48000, document.SampleRate);
        Assert.Equal(2, document.Channels);
        Assert.True(document.FrameCount > 48000);
    }

    private static string WriteTemp(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-vcodec-" + Guid.NewGuid().ToString("N") + ".mov");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] BuildMovie(string videoFourcc, uint timescale = 0, uint duration = 0)
    {
        var stsd = Concat(
            U32(0),
            U32(1),
            Concat(U32(16), Ascii(videoFourcc), new byte[8]));
        var stbl = Box("stbl", Box("stsd", stsd));
        var minf = Box("minf", stbl);
        var mdia = Box("mdia", minf);
        var trak = Box("trak", mdia);
        var moovBody = timescale > 0 && duration > 0
            ? Concat(MovieHeader(timescale, duration), trak)
            : trak;
        var moov = Box("moov", moovBody);
        var mdat = Box("mdat", "payload"u8.ToArray());
        return Concat(Box("ftyp", "qt  "u8.ToArray()), mdat, moov);
    }

    private static byte[] BuildMovieWithIpcm(string videoFourcc, bool includePcmC)
    {
        var videoStsd = Concat(
            U32(0),
            U32(1),
            Concat(U32(16), Ascii(videoFourcc), new byte[8]));
        var videoTrak = Box("trak", Box("mdia", Box("minf", Box("stbl", Box("stsd", videoStsd)))));

        var rate = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(rate, 48000u << 16);
        byte[] pcmC = includePcmC
            ? Concat(U32(14), Ascii("pcmC"), U32(0), new byte[] { 1, 24 })
            : [];
        var ipcmBody = Concat(
            Ascii("ipcm"),
            new byte[6],
            U16(1),
            new byte[8],
            U16(2),
            U16(16),
            new byte[4],
            rate,
            pcmC);
        var ipcmEntry = Concat(U32(8 + ipcmBody.Length), ipcmBody);
        var audioStsd = Concat(U32(0), U32(1), ipcmEntry);
        var audioTrak = Box("trak", Box("mdia", Box("minf", Box("stbl", Box("stsd", audioStsd)))));

        var moov = Box("moov", Concat(videoTrak, audioTrak));
        var mdat = Box("mdat", "payload"u8.ToArray());
        return Concat(Box("ftyp", "qt  "u8.ToArray()), mdat, moov);
    }

    private static byte[] MovieHeader(uint timescale, uint duration)
    {
        var body = new byte[20];
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(12), timescale);
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(16), duration);
        return Box("mvhd", body);
    }

    private static byte[] Box(string type, byte[] payload)
    {
        var size = 8 + payload.Length;
        return Concat(U32(size), Ascii(type), payload);
    }

    private static byte[] U16(int value)
    {
        var buffer = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)value);
        return buffer;
    }

    private static byte[] U32(int value)
    {
        var buffer = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint)value);
        return buffer;
    }

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text.PadRight(4)[..4]);

    private static byte[] Concat(params byte[][] parts)
    {
        var length = 0;
        foreach (var part in parts)
        {
            length += part.Length;
        }

        var dest = new byte[length];
        var at = 0;
        foreach (var part in parts)
        {
            Buffer.BlockCopy(part, 0, dest, at, part.Length);
            at += part.Length;
        }

        return dest;
    }
}
