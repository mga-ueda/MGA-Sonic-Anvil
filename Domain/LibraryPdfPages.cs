using System.IO;
using System.Runtime.InteropServices;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace MgaSonicAnvil.Domain;

/// <summary>プレイヤー背面表示用。Windows.Data.Pdf でページ数と描画。</summary>
internal static class LibraryPdfPages
{
    public static async Task<int> GetPageCountAsync(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path).AsTask().ConfigureAwait(false);
        var pdf = await PdfDocument.LoadFromFileAsync(file).AsTask().ConfigureAwait(false);
        return (int)pdf.PageCount;
    }

    public static async Task<byte[]?> RenderPngAsync(string path, int pageIndex, uint maxEdge = 1920)
    {
        if (string.IsNullOrWhiteSpace(path) || pageIndex < 0 || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path).AsTask().ConfigureAwait(false);
            var pdf = await PdfDocument.LoadFromFileAsync(file).AsTask().ConfigureAwait(false);
            if (pageIndex >= pdf.PageCount)
            {
                return null;
            }

            using var page = pdf.GetPage((uint)pageIndex);
            var options = new PdfPageRenderOptions();
            var width = page.Size.Width;
            var height = page.Size.Height;
            var scale = Math.Min(1, maxEdge / Math.Max(width, height));
            if (scale < 1 && scale > 0)
            {
                options.DestinationWidth = (uint)Math.Max(1, Math.Round(width * scale));
                options.DestinationHeight = (uint)Math.Max(1, Math.Round(height * scale));
            }

            using var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream, options).AsTask().ConfigureAwait(false);
            stream.Seek(0);
            var size = (int)stream.Size;
            var bytes = new byte[size];
            using var reader = new DataReader(stream);
            await reader.LoadAsync((uint)size).AsTask().ConfigureAwait(false);
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException
                                       or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
