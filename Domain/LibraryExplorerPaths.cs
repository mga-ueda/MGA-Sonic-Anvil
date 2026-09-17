using System.IO;

namespace MgaSonicAnvil.Domain;

/// <summary>F10 左のフォルダツリー。空の記憶はマイミュージック。</summary>
internal static class LibraryExplorerPaths
{
    public static string DefaultFolder()
    {
        var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        if (!string.IsNullOrWhiteSpace(music) && Directory.Exists(music))
        {
            return Path.GetFullPath(music);
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(profile) ? string.Empty : Path.GetFullPath(profile);
    }

    public static string Resolve(string? saved)
    {
        if (!string.IsNullOrWhiteSpace(saved))
        {
            try
            {
                var full = Path.GetFullPath(saved.Trim());
                if (Directory.Exists(full))
                {
                    return full;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
            }
        }

        return DefaultFolder();
    }
}
