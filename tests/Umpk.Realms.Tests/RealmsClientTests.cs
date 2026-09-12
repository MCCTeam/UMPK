using System.Net;
using Umpk.Realms.Tests.Fakes;
using Xunit;

namespace Umpk.Realms.Tests;

public sealed class RealmsClientTests
{
    private static readonly Guid ProfileId = Guid.ParseExact("4566e69fc90748ee8d71d7ba5aa00d20", "N");
    private const string AccessToken = "MC_TOKEN_SECRET";
    private const string Username = "Dinnerbone";
    private const string ClientVersion = "1.21.11";

    private static readonly RealmsSessionCredential Credential =
        new(AccessToken, ProfileId, Username, ClientVersion);

    private static RealmsClient NewClient(ScriptedRealmsHandler handler) =>
        new(new RealmsClientOptions
        {
            Credential = Credential,
            HttpHandlerFactory = handler,
        });

    private const string WorldsBody = """
        {"servers":[
          {"id":1234,"owner":"Notch","ownerUUID":"069a79f444e94726a5befca90e38aaf5","name":"My Realm","motd":"Welcome!","state":"OPEN","daysLeft":30,"expired":false,"expiredTrial":false,"worldType":"NORMAL","maxPlayers":10,"activeSlot":1,"member":false},
          {"id":5678,"owner":"Jeb_","name":"Closed One","state":"CLOSED","expired":true,"expiredTrial":false,"worldType":"MINIGAME","maxPlayers":8,"member":true}
        ]}
        """;

    [Fact]
    public async Task ListWorlds_ParsesRepresentativeBody()
    {
        var handler = new ScriptedRealmsHandler().On(HttpMethod.Get, "/worlds", HttpStatusCode.OK, WorldsBody);
        using RealmsClient client = NewClient(handler);

        IReadOnlyList<RealmWorld> worlds = await client.ListWorldsAsync(CancellationToken.None);

        Assert.Equal(2, worlds.Count);

        RealmWorld open = worlds[0];
        Assert.Equal(1234, open.Id);
        Assert.Equal("My Realm", open.Name);
        Assert.Equal("Welcome!", open.Motd);
        Assert.Equal("Notch", open.Owner);
        Assert.Equal(Guid.ParseExact("069a79f444e94726a5befca90e38aaf5", "N"), open.OwnerUuid);
        Assert.Equal(RealmState.Open, open.State);
        Assert.Equal("NORMAL", open.WorldType);
        Assert.False(open.Expired);
        Assert.Equal(30, open.DaysLeft);
        Assert.Equal(10, open.MaxPlayers);
        Assert.Equal(1, open.ActiveSlot);
        Assert.False(open.Member);

        RealmWorld closed = worlds[1];
        Assert.Equal(5678, closed.Id);
        Assert.Equal(RealmState.Closed, closed.State);
        Assert.True(closed.Expired);
        Assert.Null(closed.OwnerUuid);
        Assert.Null(closed.ActiveSlot);
        Assert.True(closed.Member);
    }

    [Fact]
    public async Task ListWorlds_EmptyServers_ReturnsEmptyList()
    {
        var handler = new ScriptedRealmsHandler().On(HttpMethod.Get, "/worlds", HttpStatusCode.OK, """{"servers":[]}""");
        using RealmsClient client = NewClient(handler);

        IReadOnlyList<RealmWorld> worlds = await client.ListWorldsAsync(CancellationToken.None);

        Assert.Empty(worlds);
    }

    [Fact]
    public async Task JoinWorld_ResolvesAddressAndPackFields()
    {
        const string body = """{"address":"127.0.0.1:25565","resourcePackUrl":"https://packs/example.zip","resourcePackHash":"abc123"}""";
        var handler = new ScriptedRealmsHandler().On(HttpMethod.Get, "/worlds/v1/1234/join/pc", HttpStatusCode.OK, body);
        using RealmsClient client = NewClient(handler);

        RealmServerAddress address = await client.JoinWorldAsync(1234, CancellationToken.None);

        Assert.Equal("127.0.0.1", address.Host);
        Assert.Equal(25565, address.Port);
        Assert.Equal("https://packs/example.zip", address.ResourcePackUrl);
        Assert.Equal("abc123", address.ResourcePackHash);
        Assert.Equal("127.0.0.1:25565", address.ToString());

        RecordedRealmsRequest request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Contains("/worlds/v1/1234/join/pc", request.Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JoinWorld_MissingAddress_Throws()
    {
        var handler = new ScriptedRealmsHandler().On(HttpMethod.Get, "/worlds/v1/9/join/pc", HttpStatusCode.OK, "{}");
        using RealmsClient client = NewClient(handler);

        RealmsServiceException ex = await Assert.ThrowsAsync<RealmsServiceException>(
            () => client.JoinWorldAsync(9, CancellationToken.None));
        Assert.Equal("join", ex.Operation);
        Assert.Equal(RealmsErrorKind.InvalidResponse, ex.Kind);
    }

    [Fact]
    public async Task AgreeToTerms_PostsToTosEndpoint()
    {
        var handler = new ScriptedRealmsHandler().On(HttpMethod.Post, "/mco/tos/agreed", HttpStatusCode.NoContent, "");
        using RealmsClient client = NewClient(handler);

        await client.AgreeToTermsAsync(CancellationToken.None);

        RecordedRealmsRequest request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("/mco/tos/agreed", request.Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.NotNull(request.Cookie);
        Assert.Contains("sid=token:", request.Cookie!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("COMPATIBLE", RealmsCompatibility.Compatible)]
    [InlineData("OUTDATED", RealmsCompatibility.Outdated)]
    [InlineData("OTHER", RealmsCompatibility.Other)]
    [InlineData("\"COMPATIBLE\"", RealmsCompatibility.Compatible)]
    [InlineData("something-weird", RealmsCompatibility.Unknown)]
    public async Task CheckClientCompatible_ParsesVerdict(string body, RealmsCompatibility expected)
    {
        var handler = new ScriptedRealmsHandler().On(HttpMethod.Get, "/mco/client/compatible", HttpStatusCode.OK, body);
        using RealmsClient client = NewClient(handler);

        RealmsCompatibility result = await client.CheckClientCompatibleAsync(CancellationToken.None);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, 401)]
    [InlineData(HttpStatusCode.Forbidden, 403)]
    [InlineData(HttpStatusCode.ServiceUnavailable, 503)]
    public async Task ListWorlds_ErrorStatus_ThrowsTypedException(HttpStatusCode status, int expectedCode)
    {
        var handler = new ScriptedRealmsHandler().On(HttpMethod.Get, "/worlds", status, "");
        using RealmsClient client = NewClient(handler);

        RealmsServiceException ex = await Assert.ThrowsAsync<RealmsServiceException>(
            () => client.ListWorldsAsync(CancellationToken.None));
        Assert.Equal("worlds", ex.Operation);
        Assert.Equal(expectedCode, ex.StatusCode);
    }

    [Fact]
    public async Task Requests_CarrySessionCookieAndUserAgent()
    {
        var handler = new ScriptedRealmsHandler().On(HttpMethod.Get, "/worlds", HttpStatusCode.OK, """{"servers":[]}""");
        using RealmsClient client = NewClient(handler);

        await client.ListWorldsAsync(CancellationToken.None);

        RecordedRealmsRequest request = Assert.Single(handler.Requests);
        Assert.Equal(
            "sid=token:MC_TOKEN_SECRET:4566e69fc90748ee8d71d7ba5aa00d20;user=Dinnerbone;version=1.21.11",
            request.Cookie);
        Assert.Equal("Java/1.6.0_27", request.UserAgent);
    }

    [Fact]
    public void Credential_ToString_RedactsAccessToken()
    {
        string text = Credential.ToString();

        Assert.DoesNotContain(AccessToken, text, StringComparison.Ordinal);
        Assert.Contains("<redacted>", text, StringComparison.Ordinal);
        Assert.Contains(Username, text, StringComparison.Ordinal);
        Assert.Contains(ClientVersion, text, StringComparison.Ordinal);
    }
}
