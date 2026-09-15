using System.IO;

namespace MgaSonicAnvil.Config;

/// <summary>開く／書き出しダイアログの初期フォルダ。settings.json の LastOpenFolder と LastExportFolder に残す。</summary>
internal static class ExportFolderMemory
{
    public static string Resolve(string? lastFolder, string? filePath)
    {
        if (TryNormalize(lastFolder, out var last))
        {
            return last;
        }

        return TryNormalize(Path.GetDirectoryName(filePath), out var fromFile)
            ? fromFile
            : string.Empty;
    }

    public static bool TryNormalize(string? folder, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return false;
        }

        path = Path.GetFullPath(folder);
        return true;
    }
}
