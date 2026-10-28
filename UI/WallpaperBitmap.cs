using System.IO;
using System.Windows.Media.Imaging;

namespace MgaSonicAnvil.UI;

/// <summary>設定で選んだ壁紙。読めないパスは無しとして扱う。</summary>
internal static class WallpaperBitmap
{
    /// <summary>これより広い画像は幅をここまで落としてデコードする。</summary>
    internal const int DecodeWidth = 1920;

    /// <summary>
    /// 空欄、または実在するファイルのときだけプレビューする。入力途中のパスでは今の画像を維持する。
    /// </summary>
    internal static bool TryPreviewPath(string? text, out string path)
    {
        path = (text ?? string.Empty).Trim().Trim('"');
        return path.Length == 0 || File.Exists(path);
    }

    public static BitmapSource? TryLoad(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            // メタデータだけ先に見て、巨大画像のフルデコードを避ける。
            var pixelWidth = ProbePixelWidth(path);
            using var stream = File.OpenRead(path);
            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = stream;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (pixelWidth > DecodeWidth)
            {
                image.DecodePixelWidth = DecodeWidth;
            }

            image.EndInit();
            if (image.CanFreeze)
            {
                image.Freeze();
            }

            return image;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or FileFormatException
            or ArgumentException
            or OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>DelayCreation で寸法だけ取る。失敗したら上限超え扱いで縮小デコードへ。</summary>
    private static int ProbePixelWidth(string path)
    {
        try
        {
            using var probe = File.OpenRead(path);
            var frame = BitmapDecoder.Create(
                probe,
                BitmapCreateOptions.DelayCreation,
                BitmapCacheOption.None).Frames[0];
            return frame.PixelWidth;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or FileFormatException
            or ArgumentException
            or OutOfMemoryException)
        {
            return DecodeWidth + 1;
        }
    }
}
