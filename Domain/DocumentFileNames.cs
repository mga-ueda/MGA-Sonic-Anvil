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

    /// <summary>クリップボード用。複数は CRLF 区切り。無題は表示名。</summary>
    public static string ClipboardFileNames(IReadOnlyList<(string? SourcePath, string DisplayName)> items)
    {
        if (items.Count == 0)
        {
            return string.Empty;
        }

        var lines = new List<string>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var name = NameForEdit(items[i].SourcePath, items[i].DisplayName);
            if (name.Length > 0)
            {
                lines.Add(name);
            }
        }

        return lines.Count == 0 ? string.Empty : string.Join("\r\n", lines);
    }

    /// <summary>パス一覧からファイル名／フォルダ名だけ。複数は CRLF。</summary>
    public static string ClipboardFileNames(IEnumerable<string?> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var lines = new List<string>();
        foreach (var path in paths)
        {
            var name = FileNameForClipboard(path);
            if (name.Length > 0)
            {
                lines.Add(name);
            }
        }

        return lines.Count == 0 ? string.Empty : string.Join("\r\n", lines);
    }

    /// <summary>フルパス。無い／無効なパスは落とす。複数は CRLF。</summary>
    public static string ClipboardFullPaths(IEnumerable<string?> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var lines = new List<string>();
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            try
            {
                lines.Add(Path.GetFullPath(path));
            }
            catch (Exception)
            {
                lines.Add(path);
            }
        }

        return lines.Count == 0 ? string.Empty : string.Join("\r\n", lines);
    }

    public static string FileNameForClipboard(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (trimmed.Length == 0)
        {
            return path;
        }

        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }
}
