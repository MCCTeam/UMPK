using System.Net;
using System.Net.Http;
using Umpk;
using Umpk.Auth;
using Umpk.Auth.Tests.Fakes;
using Umpk.Protocol.Java.Signing;
using Xunit;

namespace Umpk.Auth.Tests;

public sealed class RedactionTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void JavaSession_ToString_RedactsTokens()
    {
        var session = new JavaSession(new GameProfile(Guid.NewGuid(), "Dinnerbone"), "ACCESS_SECRET", Start, "REFRESH_SECRET", AuthKind.Microsoft);
        string printed = session.ToString();
        Assert.DoesNotContain("ACCESS_SECRET", printed, StringComparison.Ordinal);
        Assert.DoesNotContain("REFRESH_SECRET", printed, StringComparison.Ordinal);
        Assert.Contains("<redacted>", printed, StringComparison.Ordinal);
        Assert.Contains("Dinnerbone", printed, StringComparison.Ordinal);
    }

    [Fact]
    public void PlayerCertificates_ToString_RedactsPrivateKey()
    {
        var certs = new PlayerCertificates("PUBLIC_KEY", "PRIVATE_KEY_SECRET", "SIG1", "SIG2", Start, Start);
        string printed = certs.ToString();
        Assert.DoesNotContain("PRIVATE_KEY_SECRET", printed, StringComparison.Ordinal);
        Assert.Contains("<redacted>", printed, StringComparison.Ordinal);
    }

    [Fact]
    public void YggdrasilCredentials_ToString_RedactsPassword()
    {
        var creds = new YggdrasilCredentials("player", "PASSWORD_SECRET");
        string printed = creds.ToString();
        Assert.DoesNotContain("PASSWORD_SECRET", printed, StringComparison.Ordinal);
        Assert.Contains("player", printed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeviceCodeFlow_LogsAndExceptions_NeverContainTokens()
    {
        const string msaAccess = "MSA_ACCESS_TOKEN_SECRET_VALUE";
        const string msaRefresh = "MSA_REFRESH_TOKEN_SECRET_VALUE";

        var handler = new ScriptedHttpHandler();
        handler.On(HttpMethod.Post, "oauth2/v2.0/devicecode", HttpStatusCode.OK,
            """{"device_code":"DEV","user_code":"AAAA","verification_uri":"https://microsoft.com/link","message":"m","expires_in":900,"interval":5}""");
        handler.On(HttpMethod.Post, "oauth2/v2.0/token", HttpStatusCode.OK,
            $$"""{"access_token":"{{msaAccess}}","refresh_token":"{{msaRefresh}}","expires_in":3600}""");
        handler.AddChain();

        var logger = new CapturingLogger();
        var time = new TestTimeProvider(Start);
        using var flow = new MinecraftAuthFlow(new MinecraftAuthOptions
        {
            FlowKind = AuthFlowKind.MicrosoftDeviceCode,
            HttpHandlerFactory = handler,
            TimeProvider = time,
            Logger = logger,
        });

        JavaSession session = await MicrosoftDeviceCodeFlowTests.RunWithVirtualTime(
            flow.LoginAsync(new FakeAuthInteraction(), CancellationToken.None), time);

        // Sanity: the chain actually ran and produced the Minecraft token.
        Assert.Equal(MicrosoftChainScript.McAccessToken, session.AccessToken);

        Assert.DoesNotContain(msaAccess, logger.AllText, StringComparison.Ordinal);
        Assert.DoesNotContain(msaRefresh, logger.AllText, StringComparison.Ordinal);
        Assert.DoesNotContain(MicrosoftChainScript.McAccessToken, logger.AllText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedChain_ExceptionText_NeverContainsTokens()
    {
        const string msaAccess = "MSA_ACCESS_TOKEN_SECRET_VALUE";

        var handler = new ScriptedHttpHandler();
        handler.On(HttpMethod.Post, "oauth2/v2.0/token", HttpStatusCode.OK,
            $$"""{"access_token":"{{msaAccess}}","refresh_token":"R","expires_in":3600}""");
        // XBL fails, so the chain throws before completing.
        handler.On(HttpMethod.Post, "user.auth.xboxlive.com/user/authenticate", HttpStatusCode.Unauthorized, "{}");

        var store = new InMemoryTokenStore();
        var expired = new JavaSession(new GameProfile(Guid.NewGuid(), "dinnerbone"), "OLD", Start - TimeSpan.FromHours(1), "REFRESH_TOKEN_SECRET", AuthKind.Microsoft);
        await store.SetAsync("session:DINNERBONE", expired, CancellationToken.None);

        using var flow = new MinecraftAuthFlow(new MinecraftAuthOptions
        {
            HttpHandlerFactory = handler,
            TimeProvider = new TestTimeProvider(Start),
            TokenStore = store,
        });

        var ex = await Assert.ThrowsAsync<AuthServiceException>(
            () => flow.TryResumeAsync("dinnerbone", CancellationToken.None));

        Assert.DoesNotContain(msaAccess, ex.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("REFRESH_TOKEN_SECRET", ex.ToString(), StringComparison.Ordinal);
    }
}
