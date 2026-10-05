using System.Buffers.Binary;
using System.IO;
using System.Text;
using MgaSonicAnvil.Audio;
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

    private static string WriteTemp(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-vcodec-" + Guid.NewGuid().ToString("N") + ".mov");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] BuildMovie(string videoFourcc)
    {
        var stsd = Concat(
            U32(0),
            U32(1),
            Concat(U32(16), Ascii(videoFourcc), new byte[8]));
        var stbl = Box("stbl", Box("stsd", stsd));
        var minf = Box("minf", stbl);
        var mdia = Box("mdia", minf);
        var trak = Box("trak", mdia);
        var moov = Box("moov", trak);
        var mdat = Box("mdat", "payload"u8.ToArray());
        return Concat(Box("ftyp", "qt  "u8.ToArray()), mdat, moov);
    }

    private static byte[] Box(string type, byte[] payload)
    {
        var size = 8 + payload.Length;
        return Concat(U32(size), Ascii(type), payload);
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
