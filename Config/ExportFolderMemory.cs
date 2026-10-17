using System.IO;

namespace MgaSonicAnvil.Config;

/// <summary>開く／保存・書き出しダイアログの初期フォルダ。settings.json の LastOpenFolder と LastExportFolder に残す。</summary>
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

    /// <summary>前回のバウンス保存先。フォルダが残っているときだけディレクトリとファイル名を返す。</summary>
    public static bool TryResolveBounceStart(string? lastBouncePath, out string directory, out string fileName)
    {
        directory = string.Empty;
        fileName = string.Empty;
        if (!TryNormalize(Path.GetDirectoryName(lastBouncePath), out directory))
        {
            return false;
        }

        fileName = Path.GetFileName(lastBouncePath ?? string.Empty);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            directory = string.Empty;
            return false;
        }

        return true;
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
