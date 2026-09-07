using System.Net.Http;
using System.Text.Json;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Wwise;

/// <summary>
/// WAAPI 接続確認と、Wwise 上の現在選択（オブジェクト作成先）の取得。
/// </summary>
internal static class WaapiStartupProbe
{
    private static readonly object SelectedReturnOptions = new Dictionary<string, object>
    {
        ["return"] = new[] { "id", "type", "path" },
    };

    public static async Task<WaapiProbeResult> RunAsync(
        WaapiSettings settings,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new WaapiHttpClient(settings.Url, TimeSpan.FromMilliseconds(settings.TimeoutMs));
            var info = await WaapiCoreCalls.GetInfoAsync(client, cancellationToken)
                .ConfigureAwait(false);

            var projectText = string.Empty;
            var projectName = string.Empty;
            var projectFilePath = string.Empty;
            try
            {
                var project = await WaapiCoreCalls.GetProjectInfoAsync(client, cancellationToken)
                    .ConfigureAwait(false);
                projectText = FormatProject(project);
                if (WaapiJson.TryGetString(project, "name", out var name))
                {
                    projectName = name;
                }

                projectFilePath = WaapiJson.ReadProjectFilePath(project);
                if (projectFilePath.Length == 0)
                {
                    projectFilePath = await TryReadProjectFilePathFromObjectGetAsync(client, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch
            {
                projectText = UiStrings.StatusNoProject;
            }

            var (selectedPath, selectedType) = await ReadSelectionAsync(client, cancellationToken)
                .ConfigureAwait(false);

            return new WaapiProbeResult
            {
                Ok = true,
                WwiseVersion = FormatWwiseVersion(info),
                Project = projectText,
                ProjectName = projectName,
                ProjectFilePath = projectFilePath,
                SelectedPath = selectedPath,
                SelectedType = selectedType,
            };
        }
        catch (TaskCanceledException)
        {
            return Fail(UiStrings.LogWaapiTimeout);
        }
        catch (HttpRequestException ex)
        {
            return Fail(UiStrings.LogWaapiConnectFailed, ex.Message);
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    public static async Task<(string Path, string Type)> RefreshSelectionAsync(
        WaapiSettings settings,
        CancellationToken cancellationToken = default)
    {
        using var client = new WaapiHttpClient(settings.Url, TimeSpan.FromMilliseconds(settings.TimeoutMs));
        return await ReadSelectionAsync(client, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(string Path, string Type)> ReadSelectionAsync(
        WaapiHttpClient client,
        CancellationToken cancellationToken)
    {
        var selected = await client.CallAsync(
                WaapiUris.UiGetSelectedObjects,
                options: SelectedReturnOptions,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (selected.ValueKind != JsonValueKind.Object
            || !selected.TryGetProperty("objects", out var objects)
            || objects.ValueKind != JsonValueKind.Array
            || objects.GetArrayLength() == 0)
        {
            return (string.Empty, string.Empty);
        }

        var first = objects[0];
        WaapiJson.TryGetString(first, "path", out var path);
        WaapiJson.TryGetString(first, "type", out var type);
        return (path, type);
    }

    private static WaapiProbeResult Fail(string message, string detail = "") =>
        new()
        {
            Ok = false,
            Message = message,
            Detail = detail,
        };

    private static string FormatWwiseVersion(JsonElement info)
    {
        var displayName = WaapiJson.TryGetString(info, "displayName", out var name) ? name : UiStrings.LabelWwise;
        if (info.TryGetProperty("version", out var version))
        {
            if (WaapiJson.TryGetString(version, "displayName", out var versionName))
            {
                return $"{displayName} {versionName}";
            }

            if (TryGetInt(version, "year", out var year)
                && TryGetInt(version, "major", out var major)
                && TryGetInt(version, "minor", out var minor))
            {
                var built = TryGetInt(version, "build", out var build)
                    ? $"{year}.{major}.{minor}.{build}"
                    : $"{year}.{major}.{minor}";
                return $"{displayName} {built}";
            }
        }

        return displayName;
    }

    private static bool TryGetInt(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value))
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.String
            && int.TryParse(property.GetString(), out value))
        {
            return true;
        }

        return false;
    }

    private static async Task<string> TryReadProjectFilePathFromObjectGetAsync(
        WaapiHttpClient client,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await client.CallAsync(
                    WaapiUris.CoreObjectGet,
                    new Dictionary<string, object?> { ["waql"] = "$ from type Project" },
                    new Dictionary<string, object?> { ["return"] = new[] { "name", "filePath" } },
                    cancellationToken)
                .ConfigureAwait(false);

            if (!result.TryGetProperty("return", out var objects)
                || objects.ValueKind != JsonValueKind.Array
                || objects.GetArrayLength() == 0)
            {
                return string.Empty;
            }

            return WaapiJson.ReadProjectFilePath(objects[0]);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FormatProject(JsonElement project)
    {
        var name = WaapiJson.TryGetString(project, "name", out var n) ? n : UiStrings.LabelUnnamedProject;
        var path = WaapiJson.ReadProjectFilePath(project);
        return path.Length > 0 ? $"{name} ({path})" : name;
    }
}
