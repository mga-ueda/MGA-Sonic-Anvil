using System.Text.Json;

namespace MgaSonicAnvil.Wwise;

internal static class WaapiCoreCalls
{
    public static Task<JsonElement> GetInfoAsync(
        WaapiHttpClient client,
        CancellationToken cancellationToken = default) =>
        client.CallAsync(WaapiUris.CoreGetInfo, cancellationToken: cancellationToken);

    public static Task<JsonElement> GetProjectInfoAsync(
        WaapiHttpClient client,
        CancellationToken cancellationToken = default) =>
        client.CallAsync(WaapiUris.CoreGetProjectInfo, cancellationToken: cancellationToken);

    public static Task<JsonElement> BringToForegroundAsync(
        WaapiHttpClient client,
        CancellationToken cancellationToken = default) =>
        client.CallAsync(WaapiUris.UiBringToForeground, cancellationToken: cancellationToken);
}
