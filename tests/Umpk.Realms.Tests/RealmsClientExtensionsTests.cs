using System.Net;
using Umpk.Realms.Tests.Fakes;
using Xunit;

namespace Umpk.Realms.Tests;

/// <summary>Pins <see cref="RealmsClientExtensions.ResolveWorldAsync"/>: list, match by <see cref="RealmWorld.Match"/>, then join. Nothing here needs a try/catch of its own; list and join failures already arrive as a classified <see cref="RealmsException"/>, so this extension only adds the "no match" case as <see cref="RealmsErrorKind.WorldNotFound"/>.</summary>
public sealed class RealmsClientExtensionsTests
{
    private static readonly Guid ProfileId = Guid.NewGuid();

    private const string WorldsBody = """
        {"servers":[
          {"id":1234,"name":"My Realm","state":"OPEN","maxPlayers":10,"member":false},
          {"id":5678,"name":"Other Realm","state":"OPEN","maxPlayers":10,"member":false}
        ]}
        """;

    private static RealmsClient NewClient(ScriptedRealmsHandler handler) =>
        new(new RealmsClientOptions
        {
            Credential = new RealmsSessionCredential("MC_TOKEN_SECRET", ProfileId, "Dinnerbone", "1.21.11"),
            HttpHandlerFactory = handler,
        });

    // The join URL ("/worlds/v1/1234/join/pc") contains "/worlds" as a substring, and ScriptedRealmsHandler matches the first registered route whose substring is found. Registering /join before /worlds keeps every test below routing correctly regardless of match order elsewhere in the suite.
    private static ScriptedRealmsHandler NewHandler() =>
        new ScriptedRealmsHandler()
            .On(HttpMethod.Get, "/worlds/v1/1234/join/pc", HttpStatusCode.OK, """{"address":"realm.example:25566"}""")
            .On(HttpMethod.Get, "/worlds", HttpStatusCode.OK, WorldsBody);

    [Fact]
    public async Task ByName_ListsThenJoins()
    {
        ScriptedRealmsHandler handler = NewHandler();
        using RealmsClient client = NewClient(handler);

        RealmServerAddress address = await client.ResolveWorldAsync("My Realm", CancellationToken.None);

        Assert.Equal("realm.example", address.Host);
        Assert.Equal(25566, address.Port);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("/worlds", handler.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.DoesNotContain("/join/", handler.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains("/worlds/v1/1234/join/pc", handler.Requests[1].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ById_ListsThenJoins()
    {
        ScriptedRealmsHandler handler = NewHandler();
        using RealmsClient client = NewClient(handler);

        RealmServerAddress address = await client.ResolveWorldAsync("1234", CancellationToken.None);

        Assert.Equal("realm.example", address.Host);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task NoMatch_ThrowsWorldNotFound_AndNeverJoins()
    {
        ScriptedRealmsHandler handler = NewHandler();
        using RealmsClient client = NewClient(handler);

        RealmsException ex = await Assert.ThrowsAsync<RealmsException>(
            () => client.ResolveWorldAsync("Ghost Realm", CancellationToken.None));

        Assert.Equal(RealmsErrorKind.WorldNotFound, ex.Kind);
        Assert.Contains("Ghost Realm", ex.Message, StringComparison.Ordinal);

        RecordedRealmsRequest request = Assert.Single(handler.Requests);
        Assert.Contains("/worlds", request.Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListFails_PropagatesKind()
    {
        var handler = new ScriptedRealmsHandler()
            .On(HttpMethod.Get, "/worlds", HttpStatusCode.Forbidden, """{"errorCode":6002}""");
        using RealmsClient client = NewClient(handler);

        RealmsServiceException ex = await Assert.ThrowsAsync<RealmsServiceException>(
            () => client.ResolveWorldAsync("My Realm", CancellationToken.None));

        Assert.Equal(RealmsErrorKind.TermsNotAgreed, ex.Kind);
    }

    [Fact]
    public async Task JoinFails_PropagatesKind()
    {
        var handler = new ScriptedRealmsHandler()
            .On(HttpMethod.Get, "/worlds/v1/1234/join/pc", HttpStatusCode.Unauthorized, "")
            .On(HttpMethod.Get, "/worlds", HttpStatusCode.OK, WorldsBody);
        using RealmsClient client = NewClient(handler);

        RealmsServiceException ex = await Assert.ThrowsAsync<RealmsServiceException>(
            () => client.ResolveWorldAsync("My Realm", CancellationToken.None));

        Assert.Equal(RealmsErrorKind.Unauthorized, ex.Kind);
    }

    [Fact]
    public async Task EmptySelector_Throws()
    {
        ScriptedRealmsHandler handler = NewHandler();
        using RealmsClient client = NewClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => client.ResolveWorldAsync("", CancellationToken.None));
    }
}
