using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Umpk.Auth.Internal;

/// <summary>Thin HTTP helper over the injected <see cref="HttpMessageHandler"/>. Owns one <see cref="HttpClient"/> that does not dispose the shared handler. All auth and session HTTP flows through here so a single scripted fake handler covers every path. Response bodies are parsed with <see cref="JsonDocument"/> (reflection-free); dynamic external payloads never bind to a source-gen contract.</summary>
internal sealed class AuthHttpClient : IDisposable
{
    private readonly HttpClient _http;

    public AuthHttpClient(IHttpMessageHandlerFactory factory)
    {
        _http = new HttpClient(factory.CreateHandler(), disposeHandler: false);
    }

    public async Task<AuthHttpResponse> PostFormAsync(Uri url, string formBody, string userAgent, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(formBody, Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        request.Headers.UserAgent.ParseAdd(userAgent);
        return await SendAsync(request, ct).ConfigureAwait(false);
    }

    public async Task<AuthHttpResponse> PostJsonAsync(
        Uri url, string jsonBody, string? bearerToken, IReadOnlyDictionary<string, string>? headers, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        ApplyBearer(request, bearerToken);
        ApplyHeaders(request, headers);
        return await SendAsync(request, ct).ConfigureAwait(false);
    }

    public async Task<AuthHttpResponse> GetAsync(
        Uri url, string? bearerToken, IReadOnlyDictionary<string, string>? headers, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        ApplyBearer(request, bearerToken);
        ApplyHeaders(request, headers);
        return await SendAsync(request, ct).ConfigureAwait(false);
    }

    private async Task<AuthHttpResponse> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return new AuthHttpResponse((int)response.StatusCode, body);
    }

    private static void ApplyBearer(HttpRequestMessage request, string? bearerToken)
    {
        if (!string.IsNullOrEmpty(bearerToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

    }

    private static void ApplyHeaders(HttpRequestMessage request, IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null)
            return;

        foreach (KeyValuePair<string, string> header in headers)
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);

    }

    public void Dispose() => _http.Dispose();
}

/// <summary>An HTTP status code plus the response body text.</summary>
internal readonly record struct AuthHttpResponse(int StatusCode, string Body)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;

    /// <summary>Parses the body as a JSON document. Caller disposes.</summary>
    public JsonDocument ParseJson() => JsonDocument.Parse(string.IsNullOrEmpty(Body) ? "{}" : Body);
}
