using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using Umpk.Auth;

namespace Umpk.Auth.Tests.Fakes;

/// <summary>A scripted <see cref="HttpMessageHandler"/> for tests. Requests are matched by method and an absolute-URI substring; each matcher yields a canned response and may capture the request body. Unmatched requests fail the test loudly. Every request is recorded for assertions on URLs and bodies.</summary>
public sealed class ScriptedHttpHandler : HttpMessageHandler, IHttpMessageHandlerFactory
{
    private readonly List<Route> _routes = [];
    private readonly ConcurrentQueue<RecordedRequest> _requests = new();

    public IReadOnlyList<RecordedRequest> Requests => _requests.ToArray();

    /// <summary>Registers a matcher that returns <paramref name="body"/> with <paramref name="status"/>.</summary>
    public ScriptedHttpHandler On(HttpMethod method, string uriContains, HttpStatusCode status, string body)
    {
        _routes.Add(new Route(method, uriContains, _ => new CannedResponse(status, body)));
        return this;
    }

    /// <summary>Registers a matcher whose response is produced from the (already-recorded) request.</summary>
    public ScriptedHttpHandler On(HttpMethod method, string uriContains, Func<RecordedRequest, CannedResponse> responder)
    {
        _routes.Add(new Route(method, uriContains, responder));
        return this;
    }

    /// <summary>Registers a matcher that returns a sequence of responses on successive calls, the last repeating. Useful for device-code polling (pending, pending, success).</summary>
    public ScriptedHttpHandler OnSequence(HttpMethod method, string uriContains, params CannedResponse[] responses)
    {
        int index = -1;
        _routes.Add(new Route(method, uriContains, _ =>
        {
            int i = Interlocked.Increment(ref index);
            return responses[Math.Min(i, responses.Length - 1)];
        }));
        return this;
    }

    public HttpMessageHandler CreateHandler() => this;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string uri = request.RequestUri!.AbsoluteUri;
        string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        string? auth = request.Headers.Authorization?.Parameter;
        var recorded = new RecordedRequest(request.Method, request.RequestUri!, body, auth);
        _requests.Enqueue(recorded);

        foreach (Route route in _routes)
            if (route.Method == request.Method && uri.Contains(route.UriContains, StringComparison.Ordinal))
            {
                CannedResponse canned = route.Responder(recorded);
                return new HttpResponseMessage(canned.Status)
                {
                    Content = new StringContent(canned.Body),
                };
            }

        throw new InvalidOperationException($"No scripted route for {request.Method} {uri}");
    }

    private sealed record Route(HttpMethod Method, string UriContains, Func<RecordedRequest, CannedResponse> Responder);
}

/// <summary>A captured request: method, URI, body text, and bearer token (if any).</summary>
public sealed record RecordedRequest(HttpMethod Method, Uri Uri, string Body, string? BearerToken);

/// <summary>A canned HTTP response.</summary>
public sealed record CannedResponse(HttpStatusCode Status, string Body);
