using System.Net;
using System.Net.Http;
using Umpk.Auth;
using Umpk.Auth.Tests.Fakes;
using Xunit;

namespace Umpk.Auth.Tests;

public sealed class RefreshAndResumeTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TryResume_ValidCachedSession_ReturnedWithoutNetwork()
    {
        var handler = new ScriptedHttpHandler(); // no routes: any HTTP would throw
        var store = new InMemoryTokenStore();
        var valid = new JavaSession(
            new GameProfile(Guid.NewGuid(), "dinnerbone"),
            "GOOD_TOKEN",
            Start + TimeSpan.FromHours(1),
            "R",
            AuthKind.Microsoft);
        await store.SetAsync("session:DINNERBONE", valid, CancellationToken.None);

        using var flow = new MinecraftAuthFlow(new MinecraftAuthOptions
        {
            HttpHandlerFactory = handler,
            TimeProvider = new TestTimeProvider(Start),
            TokenStore = store,
        });

        JavaSession? resumed = await flow.TryResumeAsync("dinnerbone", CancellationToken.None);
        Assert.NotNull(resumed);
        Assert.Equal("GOOD_TOKEN", resumed!.AccessToken);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task TryResume_ExpiredMicrosoftSession_RefreshesAndRecaches()
    {
        var handler = new ScriptedHttpHandler();
        handler.On(HttpMethod.Post, "oauth2/v2.0/token", HttpStatusCode.OK,
            """{"access_token":"NEW_MSA","refresh_token":"NEW_REFRESH","expires_in":3600}""");
        handler.AddChain();

        var store = new InMemoryTokenStore();
        var expired = new JavaSession(
            new GameProfile(Guid.NewGuid(), "dinnerbone"),
            "OLD_TOKEN",
            Start - TimeSpan.FromHours(1),
            "OLD_REFRESH",
            AuthKind.Microsoft);
        await store.SetAsync("session:DINNERBONE", expired, CancellationToken.None);

        using var flow = new MinecraftAuthFlow(new MinecraftAuthOptions
        {
            HttpHandlerFactory = handler,
            TimeProvider = new TestTimeProvider(Start),
            TokenStore = store,
        });

        JavaSession? resumed = await flow.TryResumeAsync("dinnerbone", CancellationToken.None);
        Assert.NotNull(resumed);
        Assert.Equal(MicrosoftChainScript.McAccessToken, resumed!.AccessToken);
        Assert.Equal("NEW_REFRESH", resumed.RefreshToken);

        // The refresh request used grant_type=refresh_token with the old refresh token.
        RecordedRequest refreshReq = handler.Requests.First(r => r.Uri.AbsoluteUri.Contains("oauth2/v2.0/token", StringComparison.Ordinal));
        Assert.Contains("grant_type=refresh_token", refreshReq.Body, StringComparison.Ordinal);
        Assert.Contains("refresh_token=OLD_REFRESH", refreshReq.Body, StringComparison.Ordinal);

        // The refreshed session was re-cached.
        JavaSession? recached = await store.GetAsync<JavaSession>("session:DINNERBONE", CancellationToken.None);
        Assert.Equal("NEW_REFRESH", recached!.RefreshToken);
    }

    [Fact]
    public async Task TryResume_NoCachedSession_ReturnsNull()
    {
        var handler = new ScriptedHttpHandler();
        using var flow = new MinecraftAuthFlow(new MinecraftAuthOptions
        {
            HttpHandlerFactory = handler,
            TimeProvider = new TestTimeProvider(Start),
            TokenStore = new InMemoryTokenStore(),
        });

        Assert.Null(await flow.TryResumeAsync("nobody", CancellationToken.None));
    }

    [Fact]
    public async Task Invalidate_RemovesSessionAndCertificates()
    {
        var store = new InMemoryTokenStore();
        var session = new JavaSession(new GameProfile(Guid.NewGuid(), "dinnerbone"), "T", Start + TimeSpan.FromHours(1), "R", AuthKind.Microsoft);
        await store.SetAsync("session:DINNERBONE", session, CancellationToken.None);

        using var flow = new MinecraftAuthFlow(new MinecraftAuthOptions
        {
            HttpHandlerFactory = new ScriptedHttpHandler(),
            TimeProvider = new TestTimeProvider(Start),
            TokenStore = store,
        });

        await flow.InvalidateAsync("dinnerbone", CancellationToken.None);
        Assert.Null(await store.GetAsync<JavaSession>("session:DINNERBONE", CancellationToken.None));
    }
}
