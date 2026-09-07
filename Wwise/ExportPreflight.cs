using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Wwise;

internal sealed class ExportPreflightResult
{
    public required bool CanExport { get; init; }
    public required string Reason { get; init; }
    public string OutputDirectory { get; init; } = string.Empty;
    public string TargetPath { get; init; } = string.Empty;
    public string ProjectFilePath { get; init; } = string.Empty;
    public string OriginalsRoot { get; init; } = string.Empty;
}

/// <summary>
/// 書き出し先と Wwise 接続／選択の整合を検証する。
/// 書き出し先は接続中プロジェクトの Originals 配下である必要がある。
/// </summary>
internal static class ExportPreflight
{
    public static ExportPreflightResult Evaluate(
        string? outputDirectory,
        WaapiProbeResult? waapi,
        bool hasDocument,
        bool keepTarget = false,
        string? keptTargetPath = null,
        string? fallbackProjectFilePath = null)
    {
        if (!hasDocument)
        {
            return Fail(UiStrings.PreflightNoDocument);
        }

        var directory = outputDirectory?.Trim() ?? string.Empty;
        if (directory.Length == 0)
        {
            return Fail(UiStrings.PreflightNoOutputDir);
        }

        string fullDirectory;
        try
        {
            fullDirectory = Path.GetFullPath(directory);
        }
        catch (Exception ex)
        {
            return Fail(UiStrings.PreflightBadOutputPath(ex.Message), directory);
        }

        if (!Directory.Exists(fullDirectory))
        {
            return Fail(UiStrings.PreflightOutputMissing, fullDirectory);
        }

        if (waapi is null || !waapi.Ok)
        {
            return Fail(UiStrings.PreflightWaapiDisconnected, fullDirectory);
        }

        string targetPath;
        if (keepTarget)
        {
            targetPath = keptTargetPath?.Trim() ?? string.Empty;
            if (targetPath.Length == 0)
            {
                return Fail(
                    UiStrings.PreflightKeepTargetNoPath,
                    fullDirectory,
                    projectFilePath: ResolveProjectFilePath(waapi, keepTarget, fallbackProjectFilePath));
            }
        }
        else
        {
            if (!waapi.HasSelection)
            {
                return Fail(
                    UiStrings.PreflightNoSelection,
                    fullDirectory,
                    projectFilePath: waapi.ProjectFilePath,
                    targetPath: string.Empty);
            }

            targetPath = waapi.SelectedPath;
        }

        var projectFilePath = ResolveProjectFilePath(waapi, keepTarget, fallbackProjectFilePath);
        if (projectFilePath.Length == 0)
        {
            return Fail(
                UiStrings.PreflightNoProjectPath,
                fullDirectory,
                targetPath: targetPath);
        }

        string originalsRoot;
        try
        {
            var projectRoot = Path.GetDirectoryName(Path.GetFullPath(projectFilePath));
            if (string.IsNullOrEmpty(projectRoot))
            {
                return Fail(
                    UiStrings.PreflightNoProjectRoot,
                    fullDirectory,
                    projectFilePath: projectFilePath,
                    targetPath: targetPath);
            }

            originalsRoot = Path.GetFullPath(Path.Combine(projectRoot, "Originals"));
        }
        catch (Exception ex)
        {
            return Fail(
                UiStrings.PreflightOriginalsResolveFailed(ex.Message),
                fullDirectory,
                projectFilePath: projectFilePath,
                targetPath: targetPath);
        }

        if (!IsUnderDirectory(fullDirectory, originalsRoot))
        {
            return Fail(
                UiStrings.PreflightNotUnderOriginals,
                fullDirectory,
                projectFilePath: projectFilePath,
                originalsRoot: originalsRoot,
                targetPath: targetPath);
        }

        return new ExportPreflightResult
        {
            CanExport = true,
            Reason = keepTarget
                ? UiStrings.PreflightOkKeepTarget(targetPath)
                : UiStrings.PreflightOk,
            OutputDirectory = fullDirectory,
            TargetPath = targetPath,
            ProjectFilePath = Path.GetFullPath(projectFilePath),
            OriginalsRoot = originalsRoot,
        };
    }

    public static string ResolveOriginalsRoot(string? projectFilePath)
    {
        var path = projectFilePath?.Trim() ?? string.Empty;
        if (path.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            var projectRoot = Path.GetDirectoryName(Path.GetFullPath(path));
            return string.IsNullOrEmpty(projectRoot)
                ? string.Empty
                : Path.GetFullPath(Path.Combine(projectRoot, "Originals"));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ResolveProjectFilePath(
        WaapiProbeResult waapi,
        bool keepTarget,
        string? fallbackProjectFilePath)
    {
        var live = waapi.ProjectFilePath.Trim();
        if (live.Length > 0)
        {
            return live;
        }

        if (!keepTarget)
        {
            return string.Empty;
        }

        return fallbackProjectFilePath?.Trim() ?? string.Empty;
    }

    public static bool IsUnderDirectory(string candidate, string root)
    {
        string fullCandidate;
        string fullRoot;
        try
        {
            fullCandidate = Path.GetFullPath(candidate)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            fullRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return false;
        }

        if (string.Equals(fullCandidate, fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var relative = Path.GetRelativePath(fullRoot, fullCandidate);
        if (string.IsNullOrEmpty(relative)
            || relative == "."
            || Path.IsPathRooted(relative))
        {
            return false;
        }

        return !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
            && relative != "..";
    }

    private static ExportPreflightResult Fail(
        string reason,
        string outputDirectory = "",
        string projectFilePath = "",
        string originalsRoot = "",
        string targetPath = "") =>
        new()
        {
            CanExport = false,
            Reason = reason,
            OutputDirectory = outputDirectory,
            ProjectFilePath = projectFilePath,
            OriginalsRoot = originalsRoot,
            TargetPath = targetPath,
        };
}
