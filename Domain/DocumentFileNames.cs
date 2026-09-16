namespace MgaSonicAnvil.Domain;

internal enum DocumentFileNameError
{
    None,
    Empty,
    Invalid,
}

/// <summary>タブのファイル名編集。入力から同じフォルダの新しいパスを組む。</summary>
internal static class DocumentFileNames
{
    public static string NameForEdit(string? sourcePath, string displayName)
    {
        if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            var name = Path.GetFileName(sourcePath);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        return displayName;
    }

    /// <summary>拡張子の直前までを選択する（Windows の名前変更と同じ）。</summary>
    public static int StemSelectLength(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return 0;
        }

        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext) || ext.Length >= fileName.Length)
        {
            return fileName.Length;
        }

        return fileName.Length - ext.Length;
    }

    public static bool TryBuildRenamePath(
        string? sourcePath,
        string typedName,
        string? fallbackDirectory,
        string fallbackExtension,
        out string destination,
        out DocumentFileNameError error)
    {
        destination = string.Empty;
        var name = typedName.Trim();
        if (name.Length == 0)
        {
            error = DocumentFileNameError.Empty;
            return false;
        }

        if (name is "." or ".."
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            error = DocumentFileNameError.Invalid;
            return false;
        }

        var directory = !string.IsNullOrWhiteSpace(sourcePath)
            ? Path.GetDirectoryName(sourcePath)
            : fallbackDirectory;
        if (string.IsNullOrWhiteSpace(directory))
        {
            error = DocumentFileNameError.Invalid;
            return false;
        }

        if (string.IsNullOrEmpty(Path.GetExtension(name)))
        {
            var ext = !string.IsNullOrWhiteSpace(sourcePath)
                ? Path.GetExtension(sourcePath)
                : fallbackExtension;
            if (!string.IsNullOrEmpty(ext))
            {
                name += ext[0] == '.' ? ext : "." + ext;
            }
        }

        try
        {
            destination = Path.GetFullPath(Path.Combine(directory, name));
        }
        catch (Exception)
        {
            error = DocumentFileNameError.Invalid;
            destination = string.Empty;
            return false;
        }

        error = DocumentFileNameError.None;
        return true;
    }

    public static bool IsSamePath(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool IsCaseOnlyChange(string sourcePath, string destination) =>
        IsSamePath(sourcePath, destination)
        && !string.Equals(
            Path.GetFileName(sourcePath),
            Path.GetFileName(destination),
            StringComparison.Ordinal);
}
