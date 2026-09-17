using System.IO;
using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class Id3ArtworkTests
{
    private static readonly byte[] OnePixelPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54,
        0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00, 0x05, 0x00, 0x01,
        0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00,
        0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    ];

    [Fact]
    public void LooksLikeImage_DetectsPngAndJpegMagic()
    {
        Assert.True(Id3Artwork.LooksLikeImage(OnePixelPng));
        Assert.True(Id3Artwork.LooksLikeImage([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.False(Id3Artwork.LooksLikeImage([0xFF, 0xFB, 0x90, 0x00]));
        Assert.False(Id3Artwork.LooksLikeImage([]));
    }

    [Fact]
    public void IsImagePath_MatchesCommonExtensions()
    {
        Assert.True(Id3Artwork.IsImagePath(@"C:\art.PNG"));
        Assert.True(Id3Artwork.IsImagePath("cover.jpeg"));
        Assert.False(Id3Artwork.IsImagePath("song.mp3"));
        Assert.False(Id3Artwork.IsImagePath(null));
    }

    [Fact]
    public void WriteThenRead_RoundtripsPngOnBareMpegBytes()
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-id3-" + Guid.NewGuid().ToString("N") + ".mp3");
        try
        {
            File.WriteAllBytes(path, [0xFF, 0xFB, 0x90, 0x00, 0x00, 0x00, 0x00, 0x00]);
            Assert.True(Id3Artwork.TryWrite(path, OnePixelPng));
            Assert.True(Id3Artwork.TryRead(path, out var read));
            Assert.Equal(OnePixelPng, read);

            var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01 };
            Assert.True(Id3Artwork.TryWrite(path, jpeg));
            Assert.True(Id3Artwork.TryRead(path, out read));
            Assert.Equal(jpeg, read);

            Assert.True(Id3Artwork.TryWrite(path, null));
            Assert.False(Id3Artwork.TryRead(path, out _));
            var leftover = File.ReadAllBytes(path);
            Assert.Equal(0xFF, leftover[0]);
            Assert.Equal(0xFB, leftover[1]);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void AudioDocument_SetArtwork_CopiesAndClears()
    {
        var document = new AudioDocument(new float[480], 48000, 1, 16, AudioFileKind.Mp3, "a.mp3");
        Assert.False(document.HasArtwork);
        document.SetArtwork(OnePixelPng);
        Assert.True(document.HasArtwork);
        Assert.Equal(OnePixelPng, document.Artwork);
        document.Artwork![0] = 0;
        document.SetArtwork(OnePixelPng);
        Assert.Equal(0x89, document.Artwork![0]);
        document.SetArtwork(null);
        Assert.False(document.HasArtwork);
        Assert.Null(document.Artwork);
    }

    [Fact]
    public void TryReadTag_WithoutPictureData_SkipsApicPayloadButKeepsFlag()
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-id3-skip-" + Guid.NewGuid().ToString("N") + ".mp3");
        try
        {
            // 大きめの偽 PNG（マジックだけ正しければ TryWrite が通る）を埋め込む。
            var fat = new byte[256 * 1024];
            Random.Shared.NextBytes(fat);
            OnePixelPng.AsSpan().CopyTo(fat);
            File.WriteAllBytes(path, [0xFF, 0xFB, 0x90, 0x00, 0x00, 0x00, 0x00, 0x00]);
            Assert.True(Id3Artwork.TryWrite(path, fat));

            using (var stream = File.OpenRead(path))
            {
                Assert.True(Id3Artwork.TryReadTag(stream, out _, out var light, includePictureData: false));
                var apicIndex = light.FindIndex(f => f.Id is "APIC" or "PIC");
                Assert.True(apicIndex >= 0);
                Assert.Empty(light[apicIndex].Data);
            }

            Assert.True(AudioTagProbe.TryRead(path, out var tags));
            Assert.True(tags.HasArtwork);

            Assert.True(Id3Artwork.TryRead(path, out var art));
            Assert.Equal(fat, art);
        }
        finally
        {
            TryDelete(path);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}
