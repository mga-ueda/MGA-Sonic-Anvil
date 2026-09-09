using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Audio;

/// <summary>タブ書き出しの出力パス。未保存でも現在の編集内容を書く。</summary>
internal static class AudioExport
{
    public static string SuggestBaseName(string? sourcePath, string displayName)
    {
        if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            var fromFile = Path.GetFileNameWithoutExtension(sourcePath);
            if (!string.IsNullOrWhiteSpace(fromFile))
            {
                return SanitizeBaseName(fromFile);
            }
        }

        return SanitizeBaseName(displayName);
    }

    public static string UniqueInDirectory(
        string directory,
        string baseName,
        string extension,
        ISet<string> reserved)
    {
        var ext = NormalizeExtension(extension);
        var safe = SanitizeBaseName(baseName);
        var n = 1;
        while (true)
        {
            var name = n == 1 ? safe + ext : $"{safe} {n}{ext}";
            var path = Path.GetFullPath(Path.Combine(directory, name));
            if (reserved.Add(path))
            {
                return path;
            }

            n++;
        }
    }

    public static string SanitizeBaseName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "untitled";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
            {
                chars[i] = '_';
            }
        }

        var cleaned = new string(chars).Trim().Trim('_');
        return cleaned.Length == 0 ? "untitled" : cleaned;
    }

    /// <summary>設定の Auto。CPU コア数の 1/4（最低 1）。</summary>
    public const int AutoParallelism = 0;

    public static int AutoWorkers(int processorCount) =>
        Math.Max(1, Math.Max(1, processorCount) / 4);

    public static int MaxWorkers(int processorCount) =>
        Math.Max(1, Math.Max(1, processorCount) / 2);

    public static int ResolveWorkers(int setting, int processorCount)
    {
        var max = MaxWorkers(processorCount);
        return setting <= AutoParallelism
            ? AutoWorkers(processorCount)
            : Math.Clamp(setting, 1, max);
    }

    public static int WorkerCount(int jobCount, int setting) =>
        WorkerCount(jobCount, setting, Environment.ProcessorCount);

    public static int WorkerCount(int jobCount, int setting, int processorCount)
    {
        if (jobCount <= 1)
        {
            return 1;
        }

        return Math.Min(jobCount, ResolveWorkers(setting, processorCount));
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return ".wav";
        }

        return extension[0] == '.' ? extension : "." + extension;
    }

    /// <summary>上書き確認に出す名前。多すぎるときは先頭だけ残す。</summary>
    public const int OverwritePreviewLimit = 8;

    public static string FormatOverwritePreview(
        IReadOnlyList<string> names,
        int limit = OverwritePreviewLimit)
    {
        if (names.Count == 0)
        {
            return string.Empty;
        }

        var take = Math.Min(Math.Max(1, limit), names.Count);
        var lines = new string[names.Count > take ? take + 1 : take];
        for (var i = 0; i < take; i++)
        {
            lines[i] = names[i];
        }

        if (names.Count > take)
        {
            lines[take] = UiStrings.OverwriteMoreFiles(names.Count - take, names.Count);
        }

        return string.Join(Environment.NewLine, lines);
    }
}
