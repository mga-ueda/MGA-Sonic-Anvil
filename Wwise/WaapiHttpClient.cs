using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Wwise;

/// <summary>
/// WAAPI の HTTP エンドポイントへ JSON-RPC 風の呼び出しを行う薄いクライアント。
/// </summary>
internal sealed class WaapiHttpClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _url;

    public WaapiHttpClient(string url, TimeSpan timeout)
    {
        _url = url.Trim();
        _http = new HttpClient
        {
            Timeout = timeout,
        };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<JsonElement> CallAsync(
        string uri,
        object? args = null,
        object? options = null,
        CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["uri"] = uri,
            ["args"] = args ?? new Dictionary<string, object>(),
            ["options"] = options ?? new Dictionary<string, object>(),
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(_url, content, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var snippet = body.Length > 240 ? body[..240] + "…" : body;
            throw new WaapiException(
                $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}. {snippet}".Trim());
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            throw new WaapiException(UiStrings.ErrEmptyWaapiResponse);
        }

        using var document = JsonDocument.Parse(body);
        if (document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("message", out var message)
            && document.RootElement.TryGetProperty("uri", out _))
        {
            throw new WaapiException(
                message.ValueKind == JsonValueKind.String
                    ? message.GetString() ?? body
                    : body);
        }

        return document.RootElement.Clone();
    }

    public void Dispose() => _http.Dispose();
}

internal sealed class WaapiException : Exception
{
    public WaapiException(string message)
        : base(message)
    {
    }
}
