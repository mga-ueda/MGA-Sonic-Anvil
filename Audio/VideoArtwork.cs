using System.IO;
using System.Runtime.InteropServices;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace MgaSonicAnvil.Audio;

/// <summary>MOV / MP4 の一覧用ジャケット。エクスプローラーと同じサムネイル。ファイルへは書かない。</summary>
internal static class VideoArtwork
{
    internal const uint Edge = 256;

    public static bool TryRead(string path, out byte[] artwork)
    {
        artwork = [];
        try
        {
            var bytes = TryReadAsync(path).ConfigureAwait(false).GetAwaiter().GetResult();
            if (bytes is not { Length: > 0 })
            {
                return false;
            }

            artwork = bytes;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>シェルが返すファイルアイコンは実フレームではない。ffmpeg へ回す。</summary>
    internal static bool IsUsableShellThumbnail(ThumbnailType type, ulong size) =>
        type == ThumbnailType.Image && size > 0;

    public static async Task<byte[]?> TryReadAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path).AsTask().ConfigureAwait(false);
            using var thumb = await file.GetThumbnailAsync(
                    ThumbnailMode.SingleItem,
                    Edge,
                    ThumbnailOptions.ResizeThumbnail)
                .AsTask()
                .ConfigureAwait(false);
            if (thumb is not null && IsUsableShellThumbnail(thumb.Type, thumb.Size))
            {
                var size = (int)thumb.Size;
                var bytes = new byte[size];
                using var reader = new DataReader(thumb);
                await reader.LoadAsync((uint)size).AsTask().ConfigureAwait(false);
                reader.ReadBytes(bytes);
                if (bytes.Length > 0)
                {
                    return bytes;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException
                                       or ArgumentException or InvalidOperationException or FileNotFoundException
                                       or NotSupportedException)
        {
        }
        catch (Exception)
        {
            // WinRT は System.Exception を投げることがある。アイコン扱いと同じく ffmpeg へ。
        }

        // 共有ドライブや ProRes はシェルがアイコンだけ返す。ffmpeg があれば先頭フレームを使う。
        return VideoProxy.TryExtractStillJpeg(path, (int)Edge, out var jpeg) ? jpeg : null;
    }
}
