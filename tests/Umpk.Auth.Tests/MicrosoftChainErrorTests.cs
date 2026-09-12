using System.Net;
using System.Net.Http;
using Umpk.Auth;
using Umpk.Auth.Tests.Fakes;
using Xunit;

namespace Umpk.Auth.Tests;

public sealed class MicrosoftChainErrorTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static ScriptedHttpHandler HandlerWithMsaAndXbl()
    {
        var handler = new ScriptedHttpHandler();
        handler.On(HttpMethod.Post, "oauth2/v2.0/token", HttpStatusCode.OK,
            """{"access_token":"MSA","refresh_token":"R","expires_in":3600}""");
        handler.On(HttpMethod.Post, "user.auth.xboxlive.com/user/authenticate", HttpStatusCode.OK,
            """{"Token":"XBL","DisplayClaims":{"xui":[{"uhs":"UHS"}]}}""");
        return handler;
    }

    [Theory]
    [InlineData(2148916227L, XstsErrorReason.AccountBanned)]
    [InlineData(2148916233L, XstsErrorReason.NoXboxAccount)]
    [InlineData(2148916235L, XstsErrorReason.RegionUnavailable)]
    [InlineData(2148916236L, XstsErrorReason.AdultVerificationRequired)]
    [InlineData(2148916237L, XstsErrorReason.AdultVerificationRequired)]
    [InlineData(2148916238L, XstsErrorReason.ChildAccount)]
    [InlineData(9999999999L, XstsErrorReason.Unknown)]
    public async Task Xsts_XErr_MapsToTypedReason(long xErr, XstsErrorReason expected)
    {
        ScriptedHttpHandler handler = HandlerWithMsaAndXbl();
        handler.On(HttpMethod.Post, "xsts.auth.xboxlive.com/xsts/authorize", HttpStatusCode.Unauthorized,
            $$"""{"Identity":"0","XErr":{{xErr}},"Message":"","Redirect":""}""");

        using var flow = NewRefreshFlow(handler);
        var ex = await Assert.ThrowsAsync<XstsAuthorizationException>(
            () => flow.TryResumeAsync("dinnerbone", CancellationToken.None));
        Assert.Equal(expected, ex.Reason);
        Assert.Equal(xErr, ex.XErr);
    }

    [Fact]
    public async Task NoEntitlement_ThrowsTypedException()
    {
        ScriptedHttpHandler handler = HandlerWithMsaAndXbl();
        handler.On(HttpMethod.Post, "xsts.auth.xboxlive.com/xsts/authorize", HttpStatusCode.OK,
            """{"Token":"XSTS","DisplayClaims":{"xui":[{"uhs":"UHS"}]}}""");
        handler.On(HttpMethod.Post, "authentication/login_with_xbox", HttpStatusCode.OK,
            """{"access_token":"MC","expires_in":86400}""");
        handler.On(HttpMethod.Get, "entitlements/mcstore", HttpStatusCode.OK, """{"items":[]}""");

        using var flow = NewRefreshFlow(handler);
        await Assert.ThrowsAsync<NoMinecraftEntitlementException>(
            () => flow.TryResumeAsync("dinnerbone", CancellationToken.None));
    }

    // Seeds a cache with an expired Microsoft session carrying a refresh token, so TryResumeAsync drives the refresh-then-chain path (which is where the XSTS/entitlement errors surface).
    private MinecraftAuthFlow NewRefreshFlow(ScriptedHttpHandler handler)
    {
        var store = new InMemoryTokenStore();
        var expired = new JavaSession(
            new GameProfile(Guid.NewGuid(), "dinnerbone"),
            "OLD_TOKEN",
            Start - TimeSpan.FromHours(1),
            "REFRESH_TOKEN",
            AuthKind.Microsoft);
        store.SetAsync(SessionKey("dinnerbone"), expired, CancellationToken.None).AsTask().GetAwaiter().GetResult();

        return new MinecraftAuthFlow(new MinecraftAuthOptions
        {
            HttpHandlerFactory = handler,
            TimeProvider = new TestTimeProvider(Start),
            TokenStore = store,
        });
    }

    private static string SessionKey(string hint) => "session:" + hint.ToUpperInvariant();
}
