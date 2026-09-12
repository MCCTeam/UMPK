using System.Net;
using Umpk.Auth;
using Umpk.Realms.Tests.Fakes;
using Xunit;

namespace Umpk.Realms.Tests;

/// <summary>Proves the auth-reuse story end to end: a <see cref="JavaSession"/> resolved through the shared Microsoft XBL/XSTS/login_with_xbox chain (the XSTS step scripted here is the same <c>rp://api.minecraftservices.com/</c> acquisition Realms relies on - no Realms-specific relying party exists) yields the Minecraft-services token, and that token flows into the Realms session cookie. This is the "stubbed Realms-XSTS acquisition" coverage: the XSTS response is stubbed and its token drives the Realms request.</summary>
public sealed class RealmsAuthReuseTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private const string ServicesToken = "MC_SERVICES_TOKEN_FROM_XSTS_CHAIN";
    private const string ProfileId = "4566e69fc90748ee8d71d7ba5aa00d20";
    private const string ProfileName = "Dinnerbone";

    [Fact]
    public async Task Session_FromMicrosoftChain_FlowsServicesTokenIntoRealmsCookie()
    {
        var handler = new ScriptedRealmsHandler();

        // Microsoft device-code + XBL/XSTS/login_with_xbox/entitlement/profile chain.
        handler.On(HttpMethod.Post, "oauth2/v2.0/devicecode", HttpStatusCode.OK,
            """{"device_code":"DEV","user_code":"WXYZ-1234","verification_uri":"https://microsoft.com/link","message":"m","expires_in":900,"interval":5}""");
        handler.On(HttpMethod.Post, "oauth2/v2.0/token", HttpStatusCode.OK,
            """{"access_token":"MSA_ACCESS","refresh_token":"MSA_REFRESH","expires_in":3600}""");
        handler.On(HttpMethod.Post, "user.auth.xboxlive.com/user/authenticate", HttpStatusCode.OK,
            """{"Token":"XBL_TOKEN","DisplayClaims":{"xui":[{"uhs":"USERHASH"}]}}""");
        handler.On(HttpMethod.Post, "xsts.auth.xboxlive.com/xsts/authorize", HttpStatusCode.OK,
            """{"Token":"XSTS_TOKEN","DisplayClaims":{"xui":[{"uhs":"USERHASH"}]}}""");
        handler.On(HttpMethod.Post, "authentication/login_with_xbox", HttpStatusCode.OK,
            $$"""{"access_token":"{{ServicesToken}}","expires_in":86400}""");
        handler.On(HttpMethod.Get, "entitlements/mcstore", HttpStatusCode.OK,
            """{"items":[{"name":"game_minecraft"}]}""");
        handler.On(HttpMethod.Get, "minecraft/profile", HttpStatusCode.OK,
            $$"""{"id":"{{ProfileId}}","name":"{{ProfileName}}"}""");

        // Realms worlds endpoint (the second consumer of the same scripted handler).
        handler.On(HttpMethod.Get, "pc.realms.minecraft.net/worlds", HttpStatusCode.OK, """{"servers":[]}""");

        var time = new TestTimeProvider(Start);
        JavaSession session;
        using (var flow = new MinecraftAuthFlow(new MinecraftAuthOptions
        {
            FlowKind = AuthFlowKind.MicrosoftDeviceCode,
            HttpHandlerFactory = handler,
            TimeProvider = time,
        }))
            session = await RunWithVirtualTime(flow.LoginAsync(new FakeAuthInteraction(), CancellationToken.None), time);

        // The services token from the (stubbed) XSTS -> login_with_xbox chain is the session access token.
        Assert.Equal(ServicesToken, session.AccessToken);
        Assert.Equal(ProfileName, session.Profile.Name);

        RealmsSessionCredential credential = RealmsSessionCredential.FromSession(session, "1.21.11");
        using var realms = new RealmsClient(new RealmsClientOptions
        {
            Credential = credential,
            HttpHandlerFactory = handler,
        });

        await realms.ListWorldsAsync(CancellationToken.None);

        RecordedRealmsRequest realmsRequest = Assert.Single(
            handler.Requests.Where(r => r.Uri.AbsoluteUri.Contains("pc.realms.minecraft.net/worlds", StringComparison.Ordinal)).ToList());
        Assert.Equal(
            $"sid=token:{ServicesToken}:{ProfileId};user={ProfileName};version=1.21.11",
            realmsRequest.Cookie);
    }

    private static async Task<JavaSession> RunWithVirtualTime(Task<JavaSession> login, TestTimeProvider time)
    {
        const int maxPolls = 1000;
        int polls = 0;
        while (!login.IsCompleted)
        {
            if (++polls > maxPolls)
                Assert.Fail($"device-code flow did not complete within {maxPolls} virtual-time polls");

            time.Advance(TimeSpan.FromSeconds(10));
            await Task.WhenAny(login, Task.Delay(10)).ConfigureAwait(false);
        }

        return await login.ConfigureAwait(false);
    }
}
