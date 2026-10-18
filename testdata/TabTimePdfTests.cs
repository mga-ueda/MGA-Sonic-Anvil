using System.IO;
using System.Text;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class TabTimePdfTests
{
    [Fact]
    public void WritePdf_WrapsJpegPages()
    {
        var jpeg = TabTimePdf.EncodeJpeg(CreateSwatch());
        using var stream = new MemoryStream();
        TabTimePdf.WritePdf(stream, [(jpeg, 8, 8)]);
        var bytes = stream.ToArray();
        Assert.Equal("%PDF-1.4", Encoding.ASCII.GetString(bytes, 0, 8));
        Assert.Contains("%%EOF", Encoding.ASCII.GetString(bytes[^32..]));
        Assert.True(bytes.AsSpan().IndexOf(new byte[] { 0xFF, 0xD8 }) >= 0);
    }

    [Fact]
    public void Write_RendersTablePdf()
    {
        using var stream = new MemoryStream();
        TabTimePdf.Write(
            stream,
            [TabTimeRow.Create(0, "キック.wav", 48000, 48000)],
            "タブの時間",
            "ファイル",
            "時間");
        var bytes = stream.ToArray();
        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF-1.4", Encoding.ASCII.GetString(bytes, 0, 8));
        Assert.Contains("%%EOF", Encoding.ASCII.GetString(bytes[^32..]));
    }

    private static System.Windows.Media.Imaging.WriteableBitmap CreateSwatch()
    {
        var bitmap = new System.Windows.Media.Imaging.WriteableBitmap(
            8,
            8,
            96,
            96,
            System.Windows.Media.PixelFormats.Pbgra32,
            null);
        bitmap.Freeze();
        return bitmap;
    }
}
