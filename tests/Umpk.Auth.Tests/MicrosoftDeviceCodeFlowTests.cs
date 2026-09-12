using System.Net;
using System.Net.Http;
using Umpk.Auth;
using Umpk.Auth.Tests.Fakes;
using Xunit;

namespace Umpk.Auth.Tests;

public sealed class MicrosoftDeviceCodeFlowTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The device-code flow sends no redirect URI (Microsoft documents it as a flow that needs no registered redirect URI), never consults <see cref="MinecraftAuthOptions.BrowserRedirectUri"/>, and never creates a loopback listener, even when the redirect URI is set to the shape that the browser flow now refuses outright.</summary>
    [Fact]
    public async Task DeviceCode_IgnoresBrowserRedirectUri_AndNeverCreatesAListener()
    {
        var handler = new ScriptedHttpHandler();
        handler.On(HttpMethod.Post, "oauth2/v2.0/devicecode", HttpStatusCode.OK,
            """{"device_code":"DEV123","user_code":"WXYZ-1234","verification_uri":"https://microsoft.com/link","message":"Go to the link and enter the code.","expires_in":900,"interval":5}""");
        handler.On(HttpMethod.Post, "oauth2/v2.0/token", HttpStatusCode.OK,
            """{"access_token":"MSA_ACCESS","refresh_token":"MSA_REFRESH","expires_in":3600}""");
        handler.AddChain();

        var time = new TestTimeProvider(Start);
        var receiverFactory = new FailIfCreatedReceiverFactory();
        using var flow = new MinecraftAuthFlow(new MinecraftAuthOptions
        {
            FlowKind = AuthFlowKind.MicrosoftDeviceCode,
            HttpHandlerFactory = handler,
            TimeProvider = time,
            // The exact shape the browser flow refuses. Device code must not care.
            BrowserRedirectUri = new Uri("http://127.0.0.1:0/signin/"),
            LoopbackReceiverFactory = receiverFactory,
        });

        JavaSession session = await RunWithVirtualTime(
            flow.LoginAsync(new FakeAuthInteraction(), CancellationToken.None), time);

        Assert.Equal(AuthKind.Microsoft, session.Kind);
        Assert.Equal(MicrosoftChainScript.ProfileName, session.Profile.Name);
        Assert.False(receiverFactory.WasCreated, "device code must never open a loopback listener");

        // No device-code request may carry a redirect_uri.
        foreach (RecordedRequest req in handler.Requests.Where(
            r => r.Uri.AbsoluteUri.Contains("oauth2/v2.0/", StringComparison.Ordinal)))
            Assert.DoesNotContain("redirect_uri", req.Body, StringComparison.Ordinal);

    }

    private sealed class FailIfCreatedReceiverFactory : ILoopbackCodeReceiverFactory
    {
        public bool WasCreated { get; private set; }

        public ILoopbackCodeReceiver Create()
        {
            WasCreated = true;
            throw new InvalidOperationException("The device-code flow must not create a loopback listener.");
        }
    }

    [Fact]
    public async Task DeviceCode_HappyPath_PollsThenResolvesSession()
    {
        var handler = new ScriptedHttpHandler();
        handler.On(HttpMethod.Post, "oauth2/v2.0/devicecode", HttpStatusCode.OK,
            """{"device_code":"DEV123","user_code":"WXYZ-1234","verification_uri":"https://microsoft.com/link","message":"Go to the link and enter the code.","expires_in":900,"interval":5}""");

        // First two token polls are pending, third returns the MSA token.
        handler.OnSequence(HttpMethod.Post, "oauth2/v2.0/token",
            new CannedResponse(HttpStatusCode.BadRequest, """{"error":"authorization_pending"}"""),
            new CannedResponse(HttpStatusCode.BadRequest, """{"error":"authorization_pending"}"""),
            new CannedResponse(HttpStatusCode.OK, """{"access_token":"MSA_ACCESS","refresh_token":"MSA_REFRESH","expires_in":3600}"""));
        handler.AddChain();

        var time = new TestTimeProvider(Start);
        var interaction = new FakeAuthInteraction();
        using var flow = new MinecraftAuthFlow(new MinecraftAuthOptions
        {
            FlowKind = AuthFlowKind.MicrosoftDeviceCode,
            HttpHandlerFactory = handler,
            TimeProvider = time,
        });

        JavaSession session = await RunWithVirtualTime(flow.LoginAsync(interaction, CancellationToken.None), time);

        Assert.Equal(AuthKind.Microsoft, session.Kind);
        Assert.Equal(MicrosoftChainScript.ProfileName, session.Profile.Name);
        Assert.Equal(MicrosoftChainScript.McAccessToken, session.AccessToken);
        Assert.Equal("MSA_REFRESH", session.RefreshToken);
        Assert.NotNull(interaction.ShownPrompt);
        Assert.Equal("WXYZ-1234", interaction.ShownPrompt!.UserCode);
        Assert.Equal(new Uri("https://microsoft.com/link"), interaction.ShownPrompt.VerificationUri);

        // The token endpoint was polled at least three times (two pending + success).
        int tokenPolls = handler.Requests.Count(r => r.Uri.AbsoluteUri.Contains("oauth2/v2.0/token", StringComparison.Ordinal));
        Assert.True(tokenPolls >= 3, $"expected >= 3 token polls, got {tokenPolls}");
    }

    [Fact]
    public async Task DeviceCode_Declined_ThrowsTypedException()
    {
        var handler = new ScriptedHttpHandler();
        handler.On(HttpMethod.Post, "oauth2/v2.0/devicecode", HttpStatusCode.OK,
            """{"device_code":"DEV","user_code":"AAAA","verification_uri":"https://microsoft.com/link","message":"m","expires_in":900,"interval":5}""");
        handler.On(HttpMethod.Post, "oauth2/v2.0/token", HttpStatusCode.BadRequest,
            """{"error":"authorization_declined"}""");

        var time = new TestTimeProvider(Start);
        using var flow = new MinecraftAuthFlow(new MinecraftAuthOptions
        {
            FlowKind = AuthFlowKind.MicrosoftDeviceCode,
            HttpHandlerFactory = handler,
            TimeProvider = time,
        });

        var ex = await Assert.ThrowsAsync<DeviceCodeAuthorizationException>(
            () => RunWithVirtualTime(flow.LoginAsync(new FakeAuthInteraction(), CancellationToken.None), time));
        Assert.Equal(DeviceCodeFailure.Declined, ex.Failure);
    }

    [Fact]
    public async Task DeviceCode_Expired_ThrowsTypedException()
    {
        var handler = new ScriptedHttpHandler();
        handler.On(HttpMethod.Post, "oauth2/v2.0/devicecode", HttpStatusCode.OK,
            """{"device_code":"DEV","user_code":"AAAA","verification_uri":"https://microsoft.com/link","message":"m","expires_in":900,"interval":5}""");
        handler.On(HttpMethod.Post, "oauth2/v2.0/token", HttpStatusCode.BadRequest,
            """{"error":"expired_token"}""");

        var time = new TestTimeProvider(Start);
        using var flow = new MinecraftAuthFlow(new MinecraftAuthOptions
        {
            FlowKind = AuthFlowKind.MicrosoftDeviceCode,
            HttpHandlerFactory = handler,
            TimeProvider = time,
        });

        var ex = await Assert.ThrowsAsync<DeviceCodeAuthorizationException>(
            () => RunWithVirtualTime(flow.LoginAsync(new FakeAuthInteraction(), CancellationToken.None), time));
        Assert.Equal(DeviceCodeFailure.Expired, ex.Failure);
    }

    [Fact]
    public async Task DeviceCode_SlowDown_KeepsPollingAndSucceeds()
    {
        var handler = new ScriptedHttpHandler();
        handler.On(HttpMethod.Post, "oauth2/v2.0/devicecode", HttpStatusCode.OK,
            """{"device_code":"DEV","user_code":"AAAA","verification_uri":"https://microsoft.com/link","message":"m","expires_in":900,"interval":5}""");
        handler.OnSequence(HttpMethod.Post, "oauth2/v2.0/token",
            new CannedResponse(HttpStatusCode.BadRequest, """{"error":"slow_down"}"""),
            new CannedResponse(HttpStatusCode.OK, """{"access_token":"MSA_ACCESS","refresh_token":"R","expires_in":3600}"""));
        handler.AddChain();

        var time = new TestTimeProvider(Start);
        using var flow = new MinecraftAuthFlow(new MinecraftAuthOptions
        {
            FlowKind = AuthFlowKind.MicrosoftDeviceCode,
            HttpHandlerFactory = handler,
            TimeProvider = time,
        });

        JavaSession session = await RunWithVirtualTime(flow.LoginAsync(new FakeAuthInteraction(), CancellationToken.None), time);
        Assert.Equal(MicrosoftChainScript.ProfileName, session.Profile.Name);
    }

    /// <summary>Runs a login task under virtual time: repeatedly advances the fake clock so the polling <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/> completes without real waiting.</summary>
    internal static async Task<JavaSession> RunWithVirtualTime(Task<JavaSession> login, TestTimeProvider time)
    {
        // Cap the pump so a never-resolving regression fails fast with a clear message instead of hanging until the CI timeout. Each iteration advances the virtual clock 10s; 1000 polls is far beyond any real device-code poll interval / expiry budget.
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
