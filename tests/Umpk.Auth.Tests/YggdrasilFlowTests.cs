using System.Net;
using System.Net.Http;
using Umpk.Auth;
using Umpk.Auth.Tests.Fakes;
using Xunit;

namespace Umpk.Auth.Tests;

public sealed class YggdrasilFlowTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Yggdrasil_Authenticate_ReturnsSessionFromSelectedProfile()
    {
        var handler = new ScriptedHttpHandler();
        handler.On(HttpMethod.Post, "authserver/authenticate", HttpStatusCode.OK,
            """{"accessToken":"YGG_ACCESS","clientToken":"CT","selectedProfile":{"id":"4566e69fc90748ee8d71d7ba5aa00d20","name":"Dinnerbone"}}""");

        using var flow = new MinecraftAuthFlow(new MinecraftAuthOptions
        {
            FlowKind = AuthFlowKind.Yggdrasil,
            YggdrasilBaseUrl = new Uri("https://example.com/api/yggdrasil/"),
            HttpHandlerFactory = handler,
            TimeProvider = new TestTimeProvider(Start),
        });

        var interaction = new FakeAuthInteraction { Credentials = new YggdrasilCredentials("user@example.com", "hunter2") };
        JavaSession session = await flow.LoginAsync(interaction, CancellationToken.None);

        Assert.Equal(AuthKind.Yggdrasil, session.Kind);
        Assert.Equal("Dinnerbone", session.Profile.Name);
        Assert.Equal("YGG_ACCESS", session.AccessToken);

        RecordedRequest req = handler.Requests.Single();
        Assert.Equal("https://example.com/api/yggdrasil/authserver/authenticate", req.Uri.AbsoluteUri);
        Assert.Contains("\"username\":\"user@example.com\"", req.Body, StringComparison.Ordinal);
        Assert.Contains("\"agent\":{\"name\":\"Minecraft\",\"version\":1}", req.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Yggdrasil_WithoutBaseUrl_Throws()
    {
        using var flow = new MinecraftAuthFlow(new MinecraftAuthOptions
        {
            FlowKind = AuthFlowKind.Yggdrasil,
            HttpHandlerFactory = new ScriptedHttpHandler(),
            TimeProvider = new TestTimeProvider(Start),
        });

        await Assert.ThrowsAsync<AuthException>(
            () => flow.LoginAsync(new FakeAuthInteraction(), CancellationToken.None));
    }
}
