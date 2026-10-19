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
            var bytes = File.ReadAllBytes(path);
            using var probe = new MemoryStream(bytes, writable: false);
            var frame = BitmapDecoder.Create(
                probe,
                BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad).Frames[0];
            BitmapSource result = frame;
            if (frame.PixelWidth > DecodeWidth)
            {
                using var scaled = new MemoryStream(bytes, writable: false);
                var image = new BitmapImage();
                image.BeginInit();
                image.StreamSource = scaled;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = DecodeWidth;
                image.EndInit();
                result = image;
            }

            if (result.CanFreeze)
            {
                result.Freeze();
            }

            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException)
        {
            return null;
        }
    }
}
