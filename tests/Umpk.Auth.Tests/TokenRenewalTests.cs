using System.Globalization;
using System.Net;
using System.Text.Json;
using Umpk;
using Umpk.Auth;
using Umpk.Auth.Tests.Fakes;
using Umpk.Protocol.Java.Signing;
using Xunit;

namespace Umpk.Auth.Tests;

public sealed class TokenRenewalTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Resume_UsesMinecraftExpiry_NotMicrosoftExpiry()
    {
        var store = new InMemoryTokenStore();
        await SeedAsync(store, Session(Start.AddHours(-1)));
        using var flow = Flow(Handler(), store, new TestTimeProvider(Start));

        JavaSession? renewed = await flow.TryResumeAsync("Dinnerbone", default);

        Assert.NotNull(renewed);
        Assert.Equal(Start.AddHours(24), renewed.ExpiresAt);
    }

    [Fact]
    public async Task Resume_RetainsRefreshToken_WhenResponseOmitsReplacement()
    {
        var store = new InMemoryTokenStore();
        await SeedAsync(store, Session(Start.AddHours(-1)));
        var handler = Handler("""{"access_token":"NEW_MSA","expires_in":3600}""");
        using var flow = Flow(handler, store, new TestTimeProvider(Start));

        JavaSession? renewed = await flow.TryResumeAsync("Dinnerbone", default);

        Assert.NotNull(renewed);
        Assert.Equal("OLD_REFRESH", renewed.RefreshToken);
        Assert.Equal("OLD_REFRESH", (await store.GetAsync<JavaSession>("session:DINNERBONE", default))!.RefreshToken);
    }

    [Fact]
    public async Task Certificates_RenewExpiredAccessToken_ThroughOriginalProvider()
    {
        var store = new InMemoryTokenStore();
        JavaSession original = Session(Start.AddHours(24));
        await SeedAsync(store, original);
        var clock = new TestTimeProvider(Start.AddHours(25));
        var handler = Handler();
        CertificatesRoute(handler, clock);
        using var flow = Flow(handler, store, clock);
        var provider = new AuthFlowCertificateProvider(flow, original);

        Assert.NotNull(await provider.GetCertificatesAsync(default));
        Assert.Single(RefreshRequests(handler));
        Assert.All(CertificateRequests(handler), request => Assert.Equal(MicrosoftChainScript.McAccessToken, request.BearerToken));
    }

    [Fact]
    public async Task Certificates_RefreshedAfterTriggersFetch_BeforeCertificateExpiry()
    {
        var store = new InMemoryTokenStore();
        var clock = new TestTimeProvider(Start);
        var handler = Handler();
        CertificatesRoute(handler, clock, requireRenewedToken: false);
        using var flow = Flow(handler, store, clock);
        JavaSession session = Session(Start.AddHours(24));

        PlayerCertificates initial = await flow.GetCertificatesAsync(session, default);
        clock.Advance(TimeSpan.FromHours(7));
        PlayerCertificates renewed = await flow.GetCertificatesAsync(session, default);

        Assert.False(initial.IsExpired(clock.GetUtcNow()));
        Assert.Equal(2, CertificateRequests(handler).Count());
        Assert.True(renewed.RefreshedAfter > initial.RefreshedAfter);
    }

    [Fact]
    public async Task Resume_ByEmail_ReusesTokenRenewedByCertificateProvider()
    {
        var store = new InMemoryTokenStore();
        JavaSession original = Session(Start.AddHours(-1));
        await SeedAsync(store, original);
        await store.SetAsync("session:PLAYER@EXAMPLE.COM", original, default);
        var clock = new TestTimeProvider(Start);
        var handler = Handler();
        CertificatesRoute(handler, clock);
        using var flow = Flow(handler, store, clock);
        Assert.NotNull(await new AuthFlowCertificateProvider(flow, original).GetCertificatesAsync(default));

        JavaSession? resumed = await flow.TryResumeAsync("player@example.com", default);

        Assert.NotNull(resumed);
        Assert.Equal("NEW_REFRESH", resumed.RefreshToken);
        Assert.Single(RefreshRequests(handler));
        Assert.Equal("NEW_REFRESH", (await store.GetAsync<JavaSession>("session:PLAYER@EXAMPLE.COM", default))!.RefreshToken);
    }

    [Fact]
    public async Task ConcurrentResume_ExchangesRefreshTokenOnlyOnce()
    {
        var store = new InMemoryTokenStore();
        await SeedAsync(store, Session(Start.AddHours(-1)));
        var scripted = Handler();
        using var handler = new PausedRefreshHandler(scripted);
        using var flow = Flow(handler, store, new TestTimeProvider(Start));
        Task<JavaSession?> first = flow.TryResumeAsync("Dinnerbone", default);
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<JavaSession?> second = flow.TryResumeAsync("Dinnerbone", default);
        handler.Release.TrySetResult();

        JavaSession?[] renewed = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.All(renewed, session => Assert.NotNull(session));
        Assert.Single(RefreshRequests(scripted));
        Assert.Equal(renewed[0], renewed[1]);
    }

    [Fact]
    public async Task Certificates_UnauthorizedToken_IsRefreshedAndRetriedOnce()
    {
        var store = new InMemoryTokenStore();
        JavaSession original = Session(Start.AddHours(24));
        await SeedAsync(store, original);
        var clock = new TestTimeProvider(Start);
        var handler = Handler();
        CertificatesRoute(handler, clock);
        using var flow = Flow(handler, store, clock);

        Assert.NotNull(await new AuthFlowCertificateProvider(flow, original).GetCertificatesAsync(default));
        Assert.Single(RefreshRequests(handler));
        Assert.Equal(2, CertificateRequests(handler).Count());
    }

    [Fact]
    public async Task Certificates_RepeatedUnauthorized_IsBounded_WithoutInteractiveLogin()
    {
        var store = new InMemoryTokenStore();
        JavaSession original = Session(Start.AddHours(24));
        await SeedAsync(store, original);
        var handler = Handler();
        handler.On(HttpMethod.Post, "player/certificates", HttpStatusCode.Unauthorized, "{}");
        using var flow = Flow(handler, store, new TestTimeProvider(Start));

        Assert.Null(await new AuthFlowCertificateProvider(flow, original).GetCertificatesAsync(default));
        Assert.Single(RefreshRequests(handler));
        Assert.Equal(2, CertificateRequests(handler).Count());
        Assert.DoesNotContain(handler.Requests, request => request.Uri.AbsolutePath.EndsWith("oauth2/v2.0/authorize", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Certificates_Forbidden_DoesNotAttemptTokenRefresh()
    {
        var handler = Handler();
        handler.On(HttpMethod.Post, "player/certificates", HttpStatusCode.Forbidden, "{}");
        using var flow = Flow(handler, new InMemoryTokenStore(), new TestTimeProvider(Start));

        Assert.Null(await new AuthFlowCertificateProvider(flow, Session(Start.AddHours(24))).GetCertificatesAsync(default));
        Assert.Empty(RefreshRequests(handler));
        Assert.Single(CertificateRequests(handler));
    }

    [Fact]
    public async Task MultiDaySession_RenewsTokensAndCertificates_WithoutBrowserLogin()
    {
        var store = new InMemoryTokenStore();
        JavaSession original = Session(Start.AddHours(24));
        await SeedAsync(store, original);
        var clock = new TestTimeProvider(Start);
        var handler = Handler();
        CertificatesRoute(handler, clock, requireRenewedToken: false);
        using var flow = Flow(handler, store, clock);
        var provider = new AuthFlowCertificateProvider(flow, original);

        for (int day = 0; day < 4; day++)
        {
            Assert.NotNull(await provider.GetCertificatesAsync(default));
            clock.Advance(TimeSpan.FromHours(25));
        }

        Assert.Equal(3, RefreshRequests(handler).Count());
        Assert.Equal(4, CertificateRequests(handler).Count());
        Assert.All(RefreshRequests(handler), request => Assert.Contains("grant_type=refresh_token", request.Body, StringComparison.Ordinal));
        Assert.DoesNotContain(handler.Requests, request => request.Uri.AbsolutePath.Contains("devicecode", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailedRefresh_PreservesCachedSessionAndRefreshToken()
    {
        var store = new InMemoryTokenStore();
        JavaSession original = Session(Start.AddHours(-1));
        await SeedAsync(store, original);
        var handler = Handler("""{"error":"temporarily_unavailable"}""", HttpStatusCode.ServiceUnavailable);
        using var flow = Flow(handler, store, new TestTimeProvider(Start));

        await Assert.ThrowsAsync<AuthServiceException>(() => flow.TryResumeAsync("Dinnerbone", default));
        Assert.Equal(original, await store.GetAsync<JavaSession>("session:DINNERBONE", default));
    }

    [Fact]
    public async Task CancelledRefreshWait_DoesNotDuplicateOrEraseTheRefresh()
    {
        var store = new InMemoryTokenStore();
        await SeedAsync(store, Session(Start.AddHours(-1)));
        var scripted = Handler();
        using var handler = new PausedRefreshHandler(scripted);
        using var flow = Flow(handler, store, new TestTimeProvider(Start));
        Task<JavaSession?> first = flow.TryResumeAsync("Dinnerbone", default);
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cts = new CancellationTokenSource();
        Task<JavaSession?> waiting = flow.TryResumeAsync("Dinnerbone", cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        handler.Release.TrySetResult();

        Assert.NotNull(await first.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Single(RefreshRequests(scripted));
        Assert.Equal("NEW_REFRESH", (await store.GetAsync<JavaSession>("session:DINNERBONE", default))!.RefreshToken);
    }

    private static JavaSession Session(DateTimeOffset expiresAt) => new(
        new GameProfile(Guid.ParseExact(MicrosoftChainScript.ProfileId, "N"), "Dinnerbone"),
        "OLD_MC", expiresAt, "OLD_REFRESH", AuthKind.Microsoft);

    private static ValueTask SeedAsync(ITokenStore store, JavaSession session) => store.SetAsync("session:DINNERBONE", session, default);

    private static MinecraftAuthFlow Flow(IHttpMessageHandlerFactory handler, ITokenStore store, TimeProvider clock) => new(new MinecraftAuthOptions
    {
        HttpHandlerFactory = handler,
        TokenStore = store,
        TimeProvider = clock,
    });

    private static ScriptedHttpHandler Handler(string response = """{"access_token":"NEW_MSA","refresh_token":"NEW_REFRESH","expires_in":3600}""", HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new ScriptedHttpHandler();
        handler.On(HttpMethod.Post, "oauth2/v2.0/token", status, response);
        handler.AddChain();
        return handler;
    }

    private static void CertificatesRoute(ScriptedHttpHandler handler, TimeProvider clock, bool requireRenewedToken = true)
    {
        handler.On(HttpMethod.Post, "player/certificates", request =>
        {
            if (requireRenewedToken && request.BearerToken != MicrosoftChainScript.McAccessToken)
                return new CannedResponse(HttpStatusCode.Unauthorized, "{}");

            return new CannedResponse(HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                keyPair = new { publicKey = "PUBLIC", privateKey = "PRIVATE" },
                publicKeySignature = "SIG1",
                publicKeySignatureV2 = "SIG2",
                expiresAt = clock.GetUtcNow().AddHours(24).ToString("O", CultureInfo.InvariantCulture),
                refreshedAfter = clock.GetUtcNow().AddHours(6).ToString("O", CultureInfo.InvariantCulture),
            }));
        });
    }

    private static IEnumerable<RecordedRequest> RefreshRequests(ScriptedHttpHandler handler) =>
        handler.Requests.Where(request => request.Uri.AbsolutePath.EndsWith("/token", StringComparison.Ordinal));

    private static IEnumerable<RecordedRequest> CertificateRequests(ScriptedHttpHandler handler) =>
        handler.Requests.Where(request => request.Uri.AbsolutePath.EndsWith("/certificates", StringComparison.Ordinal));

    private sealed class PausedRefreshHandler(ScriptedHttpHandler scripted) : HttpMessageHandler, IHttpMessageHandlerFactory
    {
        private readonly HttpMessageInvoker _inner = new(scripted, disposeHandler: false);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public HttpMessageHandler CreateHandler() => this;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal))
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(ct).ConfigureAwait(false);
            }

            return await _inner.SendAsync(request, ct).ConfigureAwait(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
