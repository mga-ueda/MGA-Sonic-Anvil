using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>GitHub Releases から最新版を取得し、ローカル版と比較する。</summary>
internal static class GitHubUpdateChecker
{
    private static readonly HttpClient Http = CreateClient();

    public readonly record struct UpdateInfo(
        string RemoteSemVer,
        string ReleaseUrl,
        bool IsPrerelease);

    /// <summary>
    /// 下書き以外のリリースから、ローカルより新しい最新を返す。
    /// 失敗・差分なしは null（呼び出し側は黙って続行）。
    /// </summary>
    public static async Task<UpdateInfo?> TryGetNewerReleaseAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, AppVersion.ReleasesApiUrl);
        using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        UpdateInfo? best = null;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean())
            {
                continue;
            }

            if (!release.TryGetProperty("tag_name", out var tagElement))
            {
                continue;
            }

            var semVer = AppVersion.NormalizeTag(tagElement.GetString());
            if (!AppVersion.IsRemoteNewer(semVer))
            {
                continue;
            }

            if (best is { } existing
                && AppVersion.CompareSemVer(semVer, existing.RemoteSemVer) <= 0)
            {
                continue;
            }

            var htmlUrl = release.TryGetProperty("html_url", out var urlElement)
                ? urlElement.GetString()
                : null;
            var prerelease = release.TryGetProperty("prerelease", out var preElement)
                && preElement.GetBoolean();

            best = new UpdateInfo(
                semVer,
                string.IsNullOrWhiteSpace(htmlUrl) ? AppVersion.RepositoryUrl : htmlUrl.Trim(),
                prerelease);
        }

        return best;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8),
        };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("MGA-Sonic-Anvil", AppVersion.Current));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
