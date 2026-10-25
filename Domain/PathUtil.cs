namespace MgaSonicAnvil.Domain;

/// <summary>
/// <see cref="Path.GetFullPath"/> が投げる形式例外を bool に畳む。
/// 呼び出し側で同じ catch フィルタを繰り返さないための共通入口。
/// </summary>
internal static class PathUtil
{
    /// <summary>
    /// 空白のみは失敗。成功時は <see cref="Path.GetFullPath"/> の結果（末尾区切りはそのまま）。
    /// </summary>
    public static bool TryGetFullPath(string? path, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(path);
            return fullPath.Length > 0;
        }
        catch (Exception ex) when (IsPathFormatException(ex))
        {
            fullPath = string.Empty;
            return false;
        }
    }

    /// <summary>入力を Trim してからフルパス化。</summary>
    public static bool TryGetFullPathTrimmed(string? path, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return TryGetFullPath(path.Trim(), out fullPath);
    }

    /// <summary>
    /// フルパス化し、末尾のディレクトリ区切りを除く（ドライブ根 <c>C:</c> は <c>C:\</c> に戻す）。
    /// <see cref="LibraryFileTransfer.NormalizePrefix"/> と同じ正規化。
    /// </summary>
    public static bool TryNormalizePrefix(string? path, out string fullPath)
    {
        fullPath = string.Empty;
        if (!TryGetFullPath(path, out var full))
        {
            return false;
        }

        fullPath = TrimDirectorySeparators(full);
        if (fullPath.Length == 2 && fullPath[1] == ':')
        {
            fullPath += Path.DirectorySeparatorChar;
        }

        return true;
    }

    /// <summary>入力 Trim ＋ <see cref="TryNormalizePrefix"/>（ドライブ根補正あり）。</summary>
    public static bool TryNormalizePrefixTrimmed(string? path, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return TryNormalizePrefix(path.Trim(), out fullPath);
    }

    /// <summary>
    /// フルパス化し末尾区切りだけ除く（ドライブ根は <c>C:</c> のままにしうる）。
    /// エクスプローラールート登録など、従来 <c>GetFullPath</c>＋<c>TrimEnd</c> だけだった経路向け。
    /// </summary>
    public static bool TryGetFullPathTrimmedSeparators(string? path, out string fullPath)
    {
        fullPath = string.Empty;
        if (!TryGetFullPathTrimmed(path, out var full))
        {
            return false;
        }

        fullPath = TrimDirectorySeparators(full);
        return true;
    }

    public static string TrimDirectorySeparators(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public static bool IsPathFormatException(Exception ex) =>
        ex is ArgumentException or NotSupportedException or PathTooLongException;
}
