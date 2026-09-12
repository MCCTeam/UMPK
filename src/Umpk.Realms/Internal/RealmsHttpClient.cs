using System.Net.Http.Headers;
using Umpk.Auth;

namespace Umpk.Realms.Internal;

/// <summary>Thin HTTP helper over the injected <see cref="HttpMessageHandler"/>, mirroring Umpk.Auth's internal client (which is not visible here). Owns one <see cref="HttpClient"/> that does not dispose the shared handler. Every Realms request carries the session cookie and the vanilla-client user agent so a single scripted fake handler can cover every path in tests.</summary>
internal sealed class RealmsHttpClient : IDisposable
{
    // Realms expects this Java client user agent.
    private const string RealmsUserAgent = "Java/1.6.0_27";

    private readonly HttpClient _http;
    private readonly string _cookieHeader;

    public RealmsHttpClient(IHttpMessageHandlerFactory factory, string cookieHeader)
    {
        _http = new HttpClient(factory.CreateHandler(), disposeHandler: false);
        _cookieHeader = cookieHeader;
    }

    public async Task<RealmsHttpResponse> GetAsync(Uri url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        return await SendAsync(request, ct).ConfigureAwait(false);
    }

    public async Task<RealmsHttpResponse> PostAsync(Uri url, string? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (body is not null)
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        return await SendAsync(request, ct).ConfigureAwait(false);
    }

    private async Task<RealmsHttpResponse> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd(RealmsUserAgent);
        request.Headers.TryAddWithoutValidation("Cookie", _cookieHeader);
        request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
        request.Headers.TryAddWithoutValidation("Pragma", "no-cache");

        using HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        string responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return new RealmsHttpResponse((int)response.StatusCode, responseBody);
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>An HTTP status code plus the response body text.</summary>
internal readonly record struct RealmsHttpResponse(int StatusCode, string Body)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;
}
