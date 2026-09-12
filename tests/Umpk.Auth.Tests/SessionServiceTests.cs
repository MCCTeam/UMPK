using System.Net;
using System.Net.Http;
using Umpk;
using Umpk.Auth;
using Umpk.Auth.Session;
using Umpk.Auth.Tests.Fakes;
using Xunit;

namespace Umpk.Auth.Tests;

public sealed class SessionServiceTests
{
    [Fact]
    public async Task JoinServer_PostsExpectedBodyToMojangDefault_AcceptsNoContent()
    {
        var handler = new ScriptedHttpHandler();
        handler.On(HttpMethod.Post, "session/minecraft/join", HttpStatusCode.NoContent, "");

        using var service = new YggdrasilSessionService(new SessionServiceOptions { HttpHandlerFactory = handler });
        var creds = new ProfileCredentials(new GameProfile(new Guid("4566e69f-c907-48ee-8d71-d7ba5aa00d20"), "Dinnerbone"), "ACCESS_TOKEN_SECRET");

        await service.JoinServerAsync("serverhash123", creds, CancellationToken.None);

        RecordedRequest req = handler.Requests.Single();
        Assert.Equal("https://sessionserver.mojang.com/session/minecraft/join", req.Uri.AbsoluteUri);
        Assert.Contains("\"accessToken\":\"ACCESS_TOKEN_SECRET\"", req.Body, StringComparison.Ordinal);
        Assert.Contains("\"selectedProfile\":\"4566e69fc90748ee8d71d7ba5aa00d20\"", req.Body, StringComparison.Ordinal);
        Assert.Contains("\"serverId\":\"serverhash123\"", req.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JoinServer_NonSuccess_Throws()
    {
        var handler = new ScriptedHttpHandler();
        handler.On(HttpMethod.Post, "session/minecraft/join", HttpStatusCode.Forbidden,
            """{"error":"ForbiddenOperationException"}""");

        using var service = new YggdrasilSessionService(new SessionServiceOptions { HttpHandlerFactory = handler });
        var creds = new ProfileCredentials(new GameProfile(Guid.NewGuid(), "Dinnerbone"), "T");

        await Assert.ThrowsAsync<AuthServiceException>(
            () => service.JoinServerAsync("hash", creds, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task VerifyJoin_Returns200Profile_WithProperties()
    {
        var handler = new ScriptedHttpHandler();
        handler.On(HttpMethod.Get, "session/minecraft/hasJoined", HttpStatusCode.OK,
            """{"id":"4566e69fc90748ee8d71d7ba5aa00d20","name":"Dinnerbone","properties":[{"name":"textures","value":"BASE64","signature":"SIG"}]}""");

        using var service = new YggdrasilSessionService(new SessionServiceOptions { HttpHandlerFactory = handler });
        GameProfile? profile = await service.VerifyJoinAsync("Dinnerbone", "serverhash", null, CancellationToken.None);

        Assert.NotNull(profile);
        Assert.Equal("Dinnerbone", profile!.Name);
        Assert.Equal(new Guid("4566e69f-c907-48ee-8d71-d7ba5aa00d20"), profile.Id);
        ProfileProperty prop = Assert.Single(profile.Properties);
        Assert.Equal("textures", prop.Name);
        Assert.Equal("SIG", prop.Signature);

        RecordedRequest req = handler.Requests.Single();
        Assert.Contains("username=Dinnerbone", req.Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains("serverId=serverhash", req.Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyJoin_204NoContent_ReturnsNull()
    {
        var handler = new ScriptedHttpHandler();
        handler.On(HttpMethod.Get, "session/minecraft/hasJoined", HttpStatusCode.NoContent, "");

        using var service = new YggdrasilSessionService(new SessionServiceOptions { HttpHandlerFactory = handler });
        GameProfile? profile = await service.VerifyJoinAsync("Ghost", "hash", null, CancellationToken.None);
        Assert.Null(profile);
    }

    [Fact]
    public async Task VerifyJoin_IncludesClientIpWhenSupplied()
    {
        var handler = new ScriptedHttpHandler();
        handler.On(HttpMethod.Get, "session/minecraft/hasJoined", HttpStatusCode.NoContent, "");

        using var service = new YggdrasilSessionService(new SessionServiceOptions { HttpHandlerFactory = handler });
        await service.VerifyJoinAsync("Dinnerbone", "hash", IPAddress.Parse("203.0.113.7"), CancellationToken.None);

        RecordedRequest req = handler.Requests.Single();
        Assert.Contains("ip=203.0.113.7", req.Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SessionService_HonorsAuthlibInjectorBaseUrl()
    {
        var handler = new ScriptedHttpHandler();
        handler.On(HttpMethod.Post, "session/minecraft/join", HttpStatusCode.NoContent, "");

        using var service = new YggdrasilSessionService(new SessionServiceOptions
        {
            HttpHandlerFactory = handler,
            BaseUrl = new Uri("https://example.com/authlib/sessionserver/"),
        });
        var creds = new ProfileCredentials(new GameProfile(Guid.NewGuid(), "P"), "T");
        await service.JoinServerAsync("hash", creds, CancellationToken.None);

        Assert.StartsWith("https://example.com/authlib/sessionserver/session/minecraft/join",
            handler.Requests.Single().Uri.AbsoluteUri, StringComparison.Ordinal);
    }
}
