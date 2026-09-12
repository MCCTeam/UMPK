using System.Collections.Concurrent;
using System.Net;
using Umpk.Auth;

namespace Umpk.Realms.Tests.Fakes;

/// <summary>A scripted <see cref="HttpMessageHandler"/> for Realms tests. Requests are matched by method and an absolute-URI substring; each matcher yields a canned status and body. Every request is recorded with its Cookie and User-Agent headers so tests can assert the Realms session cookie was sent. Unmatched requests fail loudly. Mirrors the Umpk.Auth.Tests scripted handler but captures cookies instead of the bearer token (Realms authenticates by cookie, not Authorization).</summary>
public sealed class ScriptedRealmsHandler : HttpMessageHandler, IHttpMessageHandlerFactory
{
    private readonly List<Route> _routes = [];
    private readonly ConcurrentQueue<RecordedRealmsRequest> _requests = new();

    public IReadOnlyList<RecordedRealmsRequest> Requests => _requests.ToArray();

    /// <summary>Registers a matcher that returns <paramref name="body"/> with <paramref name="status"/>.</summary>
    public ScriptedRealmsHandler On(HttpMethod method, string uriContains, HttpStatusCode status, string body)
    {
        _routes.Add(new Route(method, uriContains, status, body));
        return this;
    }

    public HttpMessageHandler CreateHandler() => this;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string uri = request.RequestUri!.AbsoluteUri;
        string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        string? cookie = request.Headers.TryGetValues("Cookie", out IEnumerable<string>? cookies) ? string.Join("; ", cookies) : null;
        string? userAgent = request.Headers.UserAgent.Count > 0 ? request.Headers.UserAgent.ToString() : null;
        _requests.Enqueue(new RecordedRealmsRequest(request.Method, request.RequestUri!, body, cookie, userAgent));

        foreach (Route route in _routes)
            if (route.Method == request.Method && uri.Contains(route.UriContains, StringComparison.Ordinal))
                return new HttpResponseMessage(route.Status)
                {
                    Content = new StringContent(route.Body),
                };

        throw new InvalidOperationException($"No scripted route for {request.Method} {uri}");
    }

    private sealed record Route(HttpMethod Method, string UriContains, HttpStatusCode Status, string Body);
}

/// <summary>A captured request: method, URI, body text, Cookie header, and User-Agent header.</summary>
public sealed record RecordedRealmsRequest(HttpMethod Method, Uri Uri, string Body, string? Cookie, string? UserAgent);
