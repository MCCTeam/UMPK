using System.Net;
using System.Net.Http;
using Umpk.Auth;
using Umpk.Auth.Tests.Fakes;
using Xunit;

namespace Umpk.Auth.Tests;

/// <summary>Regression pins for a live finding: a Microsoft session was cached ONLY under the resolved Minecraft profile name (the gamertag), but a consumer resumes by the identifier it knows, which is the configured account login (an email). The keys never matched, so every start ran a fresh interactive device-code sign-in even though a valid cached session was sitting on disk.</summary>
public sealed class SessionResumeByLoginHintTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Login_WithLoginHint_CachesUnderBothHintAndProfileName()
    {
        var store = new InMemoryTokenStore();
        JavaSession session = await LoginAsync(store, loginHint: "player@example.com");

        // The profile name (gamertag) differs from the login the caller passed.
        Assert.NotEqual("player@example.com", session.Profile.Name);

        using MinecraftAuthFlow resumeFlow = NewFlow(new ScriptedHttpHandler(), new TestTimeProvider(Start), store);

        JavaSession? byEmail = await resumeFlow.TryResumeAsync("player@example.com", CancellationToken.None);
        JavaSession? byProfile = await resumeFlow.TryResumeAsync(session.Profile.Name, CancellationToken.None);

        Assert.NotNull(byEmail);
        Assert.NotNull(byProfile);
        Assert.Equal(session.AccessToken, byEmail!.AccessToken);
        Assert.Equal(session.AccessToken, byProfile!.AccessToken);
    }

    [Fact]
    public async Task Login_WithoutLoginHint_StillCachesUnderProfileName()
    {
        var store = new InMemoryTokenStore();
        JavaSession session = await LoginAsync(store, loginHint: null);

        using MinecraftAuthFlow resumeFlow = NewFlow(new ScriptedHttpHandler(), new TestTimeProvider(Start), store);
        JavaSession? byProfile = await resumeFlow.TryResumeAsync(session.Profile.Name, CancellationToken.None);

        Assert.NotNull(byProfile);
        Assert.Equal(session.AccessToken, byProfile!.AccessToken);
    }

    [Fact]
    public async Task Login_WithLoginHintEqualToProfileName_DoesNotDoubleWrite()
    {
        var store = new InMemoryTokenStore();
        JavaSession session = await LoginAsync(store, loginHint: MicrosoftChainScript.ProfileName);

        using MinecraftAuthFlow resumeFlow = NewFlow(new ScriptedHttpHandler(), new TestTimeProvider(Start), store);
        JavaSession? resumed = await resumeFlow.TryResumeAsync(session.Profile.Name, CancellationToken.None);

        Assert.NotNull(resumed);
        Assert.Equal(session.AccessToken, resumed!.AccessToken);
    }

    private static async Task<JavaSession> LoginAsync(ITokenStore store, string? loginHint)
    {
        var handler = new ScriptedHttpHandler();
        handler.On(HttpMethod.Post, "oauth2/v2.0/devicecode", HttpStatusCode.OK,
            """{"device_code":"DEV123","user_code":"WXYZ-1234","verification_uri":"https://microsoft.com/link","message":"Go to the link and enter the code.","expires_in":900,"interval":5}""");
        handler.OnSequence(HttpMethod.Post, "oauth2/v2.0/token",
            new CannedResponse(HttpStatusCode.OK, """{"access_token":"MSA_ACCESS","refresh_token":"MSA_REFRESH","expires_in":3600}"""));
        handler.AddChain();

        var time = new TestTimeProvider(Start);
        using MinecraftAuthFlow flow = NewFlow(handler, time, store);

        return await RunWithVirtualTime(
            flow.LoginAsync(new FakeAuthInteraction(), CancellationToken.None, loginHint), time);
    }

    private static MinecraftAuthFlow NewFlow(
        IHttpMessageHandlerFactory handler, TestTimeProvider time, ITokenStore store) =>
        new(new MinecraftAuthOptions
        {
            FlowKind = AuthFlowKind.MicrosoftDeviceCode,
            HttpHandlerFactory = handler,
            TimeProvider = time,
            TokenStore = store,
        });

    private static async Task<T> RunWithVirtualTime<T>(Task<T> task, TestTimeProvider time)
    {
        while (!task.IsCompleted)
        {
            time.Advance(TimeSpan.FromSeconds(5));
            await Task.Yield();
        }

        return await task;
    }
}
