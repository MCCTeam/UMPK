using Umpk.Client.Tests.Support;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>The login-phase plugin-query claim. Everything here is asserted on the BYTES the client puts on the wire, decoded by hand against the documented <c>custom_query_answer</c> shape (VarInt transaction id, bool understood, then the answer body when understood), because the whole point of the feature is the frame a forwarding proxy blocks on.</summary>
public sealed class LoginQueryResponderTests
{
    private static readonly Identifier VelocityPlayerInfo = new("velocity", "player_info");
    private static readonly Identifier FmlLoginWrapper = new("fml", "loginwrapper");

    [Fact]
    public async Task ClaimedChannel_AnswersWithTheResponderPayload()
    {
        using var cts = new CancellationTokenSource(PluginSessionHarness.Budget);
        await using FakeJavaServer server = FakeJavaServer.Create();
        UmpkClient client = PluginSessionHarness.BuildClient(server);
        await using UmpkClient owned = client;

        byte[] seen = [];
        client.LoginQueries.Register(VelocityPlayerInfo, (payload, _) =>
        {
            seen = payload.ToArray();
            return ValueTask.FromResult<ReadOnlyMemory<byte>?>(new byte[] { 0xAA, 0xBB, 0xCC });
        });

        (int transaction, bool understood, byte[] body) = await RunQueryAsync(
            client, server, VelocityPlayerInfo, [0x01, 0x02], cts.Token);

        Assert.Equal(7, transaction);
        Assert.True(understood);
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC }, body);
        Assert.Equal(new byte[] { 0x01, 0x02 }, seen);
    }

    /// <summary>A client that claims no query support must answer every query consistently on every protocol.</summary>
    [Fact]
    public async Task UnclaimedChannel_StillAnswersNotUnderstood()
    {
        using var cts = new CancellationTokenSource(PluginSessionHarness.Budget);
        await using FakeJavaServer server = FakeJavaServer.Create();
        UmpkClient client = PluginSessionHarness.BuildClient(server);
        await using UmpkClient owned = client;

        (int transaction, bool understood, byte[] body) = await RunQueryAsync(
            client, server, FmlLoginWrapper, [0x7F], cts.Token);

        Assert.Equal(7, transaction);
        Assert.False(understood);
        Assert.Empty(body);
    }

    /// <summary>A claim on one channel must not answer for a different one.</summary>
    [Fact]
    public async Task ClaimOnAnotherChannel_DoesNotAnswerThisOne()
    {
        using var cts = new CancellationTokenSource(PluginSessionHarness.Budget);
        await using FakeJavaServer server = FakeJavaServer.Create();
        UmpkClient client = PluginSessionHarness.BuildClient(server);
        await using UmpkClient owned = client;

        client.LoginQueries.Register(VelocityPlayerInfo, (_, _) =>
            ValueTask.FromResult<ReadOnlyMemory<byte>?>(new byte[] { 0x01 }));

        (_, bool understood, byte[] body) = await RunQueryAsync(
            client, server, FmlLoginWrapper, [], cts.Token);

        Assert.False(understood);
        Assert.Empty(body);
    }

    [Fact]
    public async Task ResponderReturningNull_AnswersNotUnderstood()
    {
        using var cts = new CancellationTokenSource(PluginSessionHarness.Budget);
        await using FakeJavaServer server = FakeJavaServer.Create();
        UmpkClient client = PluginSessionHarness.BuildClient(server);
        await using UmpkClient owned = client;

        client.LoginQueries.Register(VelocityPlayerInfo, (_, _) =>
            ValueTask.FromResult<ReadOnlyMemory<byte>?>(null));

        (_, bool understood, byte[] body) = await RunQueryAsync(
            client, server, VelocityPlayerInfo, [], cts.Token);

        Assert.False(understood);
        Assert.Empty(body);
    }

    /// <summary>A responder is host code on the driver's read loop. One that throws degrades to the vanilla answer rather than killing a login that would otherwise join.</summary>
    [Fact]
    public async Task ThrowingResponder_AnswersNotUnderstood()
    {
        using var cts = new CancellationTokenSource(PluginSessionHarness.Budget);
        await using FakeJavaServer server = FakeJavaServer.Create();
        UmpkClient client = PluginSessionHarness.BuildClient(server);
        await using UmpkClient owned = client;

        client.LoginQueries.Register(VelocityPlayerInfo, (_, _) => throw new InvalidOperationException("boom"));

        (_, bool understood, byte[] body) = await RunQueryAsync(
            client, server, VelocityPlayerInfo, [], cts.Token);

        Assert.False(understood);
        Assert.Empty(body);
    }

    /// <summary>An empty but PRESENT answer is not the same frame as "not understood".</summary>
    [Fact]
    public async Task EmptyAnswer_IsStillUnderstood()
    {
        using var cts = new CancellationTokenSource(PluginSessionHarness.Budget);
        await using FakeJavaServer server = FakeJavaServer.Create();
        UmpkClient client = PluginSessionHarness.BuildClient(server);
        await using UmpkClient owned = client;

        client.LoginQueries.Register(VelocityPlayerInfo, (_, _) =>
            ValueTask.FromResult<ReadOnlyMemory<byte>?>(ReadOnlyMemory<byte>.Empty));

        (_, bool understood, byte[] body) = await RunQueryAsync(
            client, server, VelocityPlayerInfo, [], cts.Token);

        Assert.True(understood);
        Assert.Empty(body);
    }

    [Fact]
    public async Task Claims_AreReleasedWhenTheSessionEnds()
    {
        using var cts = new CancellationTokenSource(PluginSessionHarness.Budget);
        await using FakeJavaServer server = FakeJavaServer.Create();
        UmpkClient client = PluginSessionHarness.BuildClient(server);
        await using UmpkClient owned = client;

        client.LoginQueries.Register(VelocityPlayerInfo, (_, _) =>
            ValueTask.FromResult<ReadOnlyMemory<byte>?>(new byte[] { 0x01 }));
        Assert.True(client.LoginQueries.IsClaimed(VelocityPlayerInfo));

        await PluginSessionHarness.JoinAsync(client, server, cts.Token);
        await client.DisconnectAsync();

        Assert.Equal(0, client.LoginQueries.Count);
        Assert.False(client.LoginQueries.IsClaimed(VelocityPlayerInfo));
    }

    [Fact]
    public void Register_RefusesASecondClaimOnTheSameChannel()
    {
        var responders = new LoginQueryResponders();
        responders.Register(VelocityPlayerInfo, (_, _) => ValueTask.FromResult<ReadOnlyMemory<byte>?>(null));

        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            responders.Register(VelocityPlayerInfo, (_, _) => ValueTask.FromResult<ReadOnlyMemory<byte>?>(null)));

        Assert.Contains("velocity:player_info", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DisposingAHandle_ReleasesOnlyThatClaim()
    {
        var responders = new LoginQueryResponders();
        IDisposable velocity = responders.Register(
            VelocityPlayerInfo, (_, _) => ValueTask.FromResult<ReadOnlyMemory<byte>?>(null));
        responders.Register(FmlLoginWrapper, (_, _) => ValueTask.FromResult<ReadOnlyMemory<byte>?>(null));

        velocity.Dispose();
        velocity.Dispose(); // idempotent

        Assert.False(responders.IsClaimed(VelocityPlayerInfo));
        Assert.True(responders.IsClaimed(FmlLoginWrapper));
        Assert.Equal(1, responders.Count);
    }

    /// <summary>A stale handle must not unregister a claim someone else made after the channel was released.</summary>
    [Fact]
    public void DisposingAStaleHandle_LeavesTheLiveClaimAlone()
    {
        var responders = new LoginQueryResponders();
        IDisposable first = responders.Register(
            VelocityPlayerInfo, (_, _) => ValueTask.FromResult<ReadOnlyMemory<byte>?>(null));
        responders.Clear();
        responders.Register(VelocityPlayerInfo, (_, _) => ValueTask.FromResult<ReadOnlyMemory<byte>?>(null));

        first.Dispose();

        Assert.True(responders.IsClaimed(VelocityPlayerInfo));
    }

    /// <summary>Drives handshake and login start, injects one <c>custom_query</c> on <paramref name="channel"/>, and returns the client's decoded answer frame. The rest of login and configuration is then driven to completion so the connect task finishes rather than being abandoned mid-handshake.</summary>
    private static async Task<(int Transaction, bool Understood, byte[] Body)> RunQueryAsync(
        UmpkClient client, FakeJavaServer server, Identifier channel, byte[] payload, CancellationToken ct)
    {
        Task connect = client.ConnectAsync(new ServerEndpoint("test", 25565), ct);

        await server.NextFrameAsync(ct); // handshake
        server.ServerConnection.SetPhase(ProtocolPhase.Login);
        await server.NextFrameAsync(ct); // hello

        await PluginSessionHarness.SendAsync(
            server, ProtocolPhase.Login, new ClientboundLoginCustomQueryPacket(7, channel, payload), ct);

        InboundFrame answer = await server.NextFrameAsync(ct);
        Assert.Equal(AnswerWireId(), answer.WireId);

        var reader = new PacketReader(answer.Payload);
        int transaction = reader.ReadVarInt();
        bool understood = reader.ReadBool();
        byte[] body = reader.ReadRemaining().ToArray();

        await PluginSessionHarness.SendAsync(server, ProtocolPhase.Login,
            new ClientboundLoginFinishedPacket(Guid.NewGuid(), "Tester", [], null), ct);
        await server.NextFrameAsync(ct); // login_acknowledged
        server.ServerConnection.SetPhase(ProtocolPhase.Configuration);
        await server.NextFrameAsync(ct); // client_information
        await PluginSessionHarness.SendAsync(
            server, ProtocolPhase.Configuration, new ClientboundFinishConfigurationPacket(), ct);
        await server.NextFrameAsync(ct); // finish_configuration
        server.ServerConnection.SetPhase(ProtocolPhase.Play);
        await ScriptedServer.SendPlayReadinessFrameAsync(server, PluginSessionHarness.Version.Protocol, ct);

        await connect.WaitAsync(PluginSessionHarness.Budget, ct);
        return (transaction, understood, body);
    }

    private static int AnswerWireId()
    {
        ProtocolDescriptor descriptor = PluginSessionHarness.Version.Protocol;
        Assert.True(descriptor.TryGetRegistry(ProtocolPhase.Login, PacketFlow.Serverbound, out PhaseRegistry registry));
        foreach ((int wireId, PacketType type) in registry.Packets)
            if (type.Id == Identifier.Minecraft("custom_query_answer"))
                return wireId;

        throw new Xunit.Sdk.XunitException("custom_query_answer is not registered serverbound in login.");
    }
}
