using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace MgaSonicAnvil.UI;

internal static class WindowIconHelper
{
    private static BitmapFrame? _cached;

    public static void Apply(Window window)
    {
        if (GetIcon() is { } icon)
        {
            window.Icon = icon;
        }
    }

    public static BitmapFrame? GetIcon()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        try
        {
            var assembly = typeof(WindowIconHelper).Assembly;
            using var stream = assembly.GetManifestResourceStream("MgaSonicAnvil.Branding.MgaSonicAnvil.ico");
            if (stream is null)
            {
                return null;
            }

            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            copy.Position = 0;
            var decoder = BitmapDecoder.Create(copy, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames
                .OrderByDescending(f => f.PixelWidth * f.PixelHeight)
                .FirstOrDefault();
            if (frame is null)
            {
                return null;
            }

            frame.Freeze();
            _cached = frame;
            return _cached;
        }
        catch
        {
            return null;
        }
    }
}
