namespace MgaSonicAnvil.Domain;

internal enum LibraryIoFailureKind
{
    NotFound,
    AccessDenied,
    DiskFull,
    Other,
}

/// <summary>
/// プレイヤーのコピー／移動／削除で使うパス判定。ディスク操作そのものは UI 側。
/// </summary>
internal static class LibraryFileTransfer
{
    public static string NormalizePrefix(string path)
    {
        var full = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (full.Length == 2 && full[1] == ':')
        {
            full += Path.DirectorySeparatorChar;
        }

        return full;
    }

    public static bool SameFullPath(string left, string right)
    {
        if (!PathUtil.TryNormalizePrefix(left, out var a)
            || !PathUtil.TryNormalizePrefix(right, out var b))
        {
            return false;
        }

        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCaseOnlyChange(string left, string right)
    {
        if (!PathUtil.TryGetFullPath(left, out var a) || !PathUtil.TryGetFullPath(right, out var b))
        {
            return false;
        }

        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(a, b, StringComparison.Ordinal);
    }

    /// <summary>path が folder そのもの、またはその配下か。</summary>
    public static bool IsSameOrUnder(string path, string folder)
    {
        if (!PathUtil.TryNormalizePrefix(path, out var full)
            || !PathUtil.TryNormalizePrefix(folder, out var root))
        {
            return false;
        }

        if (full.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryMapPath(string path, string oldPrefix, string newPrefix, out string mapped)
    {
        mapped = path;
        if (!PathUtil.TryGetFullPath(path, out var full)
            || !PathUtil.TryNormalizePrefix(oldPrefix, out var oldRoot)
            || !PathUtil.TryNormalizePrefix(newPrefix, out var newRoot))
        {
            return false;
        }

        var comparable = NormalizePrefix(full);
        if (comparable.Equals(oldRoot, StringComparison.OrdinalIgnoreCase))
        {
            mapped = newRoot;
            return true;
        }

        var prefix = oldRoot.EndsWith(Path.DirectorySeparatorChar)
            ? oldRoot
            : oldRoot + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var tail = full[prefix.Length..];
        var head = newRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        mapped = head + Path.DirectorySeparatorChar + tail;
        return true;
    }

    /// <summary>既存のファイル複製と同じく「名前 2」「名前 3」。</summary>
    public static string UniquePath(string directory, string fileName, bool directoryName)
    {
        var stem = directoryName ? fileName : Path.GetFileNameWithoutExtension(fileName);
        var ext = directoryName ? string.Empty : Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(stem))
        {
            stem = fileName;
            ext = string.Empty;
        }

        for (var n = 2; n < 10000; n++)
        {
            var candidate = Path.Combine(directory, stem + " " + n + ext);
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException();
    }

    public static bool TryAcceptName(
        string typed,
        bool directoryName,
        string currentName,
        out string name,
        out DocumentFileNameError error)
    {
        name = typed.Trim();
        if (name.Length == 0)
        {
            error = DocumentFileNameError.Empty;
            return false;
        }

        if (name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            error = DocumentFileNameError.Invalid;
            return false;
        }

        if (!directoryName && string.IsNullOrEmpty(Path.GetExtension(name)))
        {
            var ext = Path.GetExtension(currentName);
            if (!string.IsNullOrEmpty(ext))
            {
                name += ext;
            }
        }

        error = DocumentFileNameError.None;
        return true;
    }

    public static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    public static LibraryIoFailureKind Classify(Exception exception)
    {
        if (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return LibraryIoFailureKind.NotFound;
        }

        if (exception is UnauthorizedAccessException)
        {
            return LibraryIoFailureKind.AccessDenied;
        }

        if (exception is IOException)
        {
            var code = exception.HResult & 0xFFFF;
            if (code is 2 or 3 or 53 or 64 or 67)
            {
                return LibraryIoFailureKind.NotFound;
            }

            if (code is 5 or 19 or 395)
            {
                return LibraryIoFailureKind.AccessDenied;
            }

            if (code is 39 or 112 or 1295)
            {
                return LibraryIoFailureKind.DiskFull;
            }
        }

        return LibraryIoFailureKind.Other;
    }

    public static string Describe(Exception exception)
    {
        var summary = Classify(exception) switch
        {
            LibraryIoFailureKind.NotFound => UiStrings.ErrorLibraryNotFound,
            LibraryIoFailureKind.AccessDenied => UiStrings.ErrorLibraryAccess,
            LibraryIoFailureKind.DiskFull => UiStrings.ErrorLibraryDiskFull,
            _ => UiStrings.ErrorLibraryFileOp,
        };
        var detail = exception.Message?.Trim() ?? string.Empty;
        if (detail.Length == 0
            || detail.Equals(summary, StringComparison.Ordinal)
            || detail.Equals(summary.TrimEnd('。', '.'), StringComparison.Ordinal))
        {
            return summary;
        }

        return summary + Environment.NewLine + detail;
    }
}
