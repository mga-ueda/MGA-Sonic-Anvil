using System.Text.Json;

namespace MgaSonicAnvil.Wwise;

internal static class WaapiObjectUtil
{
    private static readonly string[] ReturnFieldsIdPath = ["id", "path"];

    public static async Task<bool> ExistsAsync(
        WaapiSettings settings,
        string objectPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectPath))
        {
            return false;
        }

        using var client = new WaapiHttpClient(
            settings.Url,
            TimeSpan.FromMilliseconds(settings.TimeoutMs));

        var escaped = objectPath.Replace("\"", "\\\"", StringComparison.Ordinal);
        try
        {
            var result = await client.CallAsync(
                    WaapiUris.CoreObjectGet,
                    new Dictionary<string, object?> { ["waql"] = $"$ \"{escaped}\"" },
                    new Dictionary<string, object?> { ["return"] = ReturnFieldsIdPath },
                    cancellationToken)
                .ConfigureAwait(false);

            return result.TryGetProperty("return", out var arr)
                && arr.ValueKind == JsonValueKind.Array
                && arr.GetArrayLength() > 0;
        }
        catch (WaapiException ex) when (IsObjectNotFound(ex.Message))
        {
            return false;
        }
    }

    private static bool IsObjectNotFound(string message) =>
        message.Contains("Object not found", StringComparison.OrdinalIgnoreCase)
        || message.Contains("invalid_query", StringComparison.OrdinalIgnoreCase)
        || message.Contains(WaapiUris.QueryInvalidQuery, StringComparison.OrdinalIgnoreCase);
}
