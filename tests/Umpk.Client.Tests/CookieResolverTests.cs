using Umpk.Client.Tests.Support;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary><see cref="CookieStore.Resolver"/>, driven end to end: a real client answering a real <c>cookie_request</c> off the wire. The store could only ever answer with something a <c>store_cookie</c> had already put in it, so the first request of a proxy's forwarding handshake was unanswerable by construction.</summary>
public sealed class CookieResolverTests
{
    private static readonly Identifier Key = new("velocity", "forwarding");

    [Fact]
    public async Task Resolver_AnswersTheRequest_AndItsValueIsStored()
    {
        using var cts = new CancellationTokenSource(PluginSessionHarness.Budget);
        await using FakeJavaServer server = FakeJavaServer.Create();
        UmpkClient client = PluginSessionHarness.BuildClient(server);
        await using UmpkClient owned = client;

        var asked = new List<Identifier>();
        client.Cookies.Resolver = (key, _) =>
        {
            asked.Add(key);
            return ValueTask.FromResult<byte[]?>([0x10, 0x20, 0x30]);
        };

        ServerboundCookieResponsePacket response = await RequestCookieAsync(client, server, Key, cts.Token);

        Assert.Equal(Key, response.Key);
        Assert.Equal(new byte[] { 0x10, 0x20, 0x30 }, response.Payload);
        Assert.Equal([Key], asked);
        Assert.Equal(new byte[] { 0x10, 0x20, 0x30 }, client.Cookies.Get(Key));
    }

    /// <summary>A resolver that declines falls back to the store, so a cookie a <c>store_cookie</c> already delivered still answers. This is the arm that keeps the previous behaviour reachable.</summary>
    [Fact]
    public async Task ResolverReturningNull_FallsBackToTheStoredValue()
    {
        using var cts = new CancellationTokenSource(PluginSessionHarness.Budget);
        await using FakeJavaServer server = FakeJavaServer.Create();
        UmpkClient client = PluginSessionHarness.BuildClient(server);
        await using UmpkClient owned = client;

        client.Cookies.Set(Key, [0x55]);
        client.Cookies.Resolver = (_, _) => ValueTask.FromResult<byte[]?>(null);

        ServerboundCookieResponsePacket response = await RequestCookieAsync(client, server, Key, cts.Token);

        Assert.Equal(new byte[] { 0x55 }, response.Payload);
    }

    /// <summary>With no resolver, an unknown key is answered as absent. The reply still goes out because a server blocked on it would otherwise stall forever.</summary>
    [Fact]
    public async Task NoResolver_AnswersAbsentForAnUnknownKey()
    {
        using var cts = new CancellationTokenSource(PluginSessionHarness.Budget);
        await using FakeJavaServer server = FakeJavaServer.Create();
        UmpkClient client = PluginSessionHarness.BuildClient(server);
        await using UmpkClient owned = client;

        ServerboundCookieResponsePacket response = await RequestCookieAsync(client, server, Key, cts.Token);

        Assert.Equal(Key, response.Key);
        Assert.Null(response.Payload);
    }

    /// <summary>A throwing resolver must still produce a reply, from the store.</summary>
    [Fact]
    public async Task ThrowingResolver_StillAnswers_FromTheStore()
    {
        using var cts = new CancellationTokenSource(PluginSessionHarness.Budget);
        await using FakeJavaServer server = FakeJavaServer.Create();
        UmpkClient client = PluginSessionHarness.BuildClient(server);
        await using UmpkClient owned = client;

        client.Cookies.Set(Key, [0x77]);
        client.Cookies.Resolver = (_, _) => throw new InvalidOperationException("boom");

        ServerboundCookieResponsePacket response = await RequestCookieAsync(client, server, Key, cts.Token);

        Assert.Equal(new byte[] { 0x77 }, response.Payload);
    }

    /// <summary>The resolver's value is stored, so a second request for the same key does not ask again.</summary>
    [Fact]
    public async Task AResolvedValue_IsNotResolvedTwice()
    {
        using var cts = new CancellationTokenSource(PluginSessionHarness.Budget);
        await using FakeJavaServer server = FakeJavaServer.Create();
        UmpkClient client = PluginSessionHarness.BuildClient(server);
        await using UmpkClient owned = client;

        int calls = 0;
        client.Cookies.Resolver = (_, _) =>
        {
            calls++;
            return ValueTask.FromResult<byte[]?>(calls == 1 ? [0x01] : null);
        };

        await PluginSessionHarness.JoinAsync(client, server, cts.Token);

        ServerboundCookieResponsePacket first = await AskAsync(server, Key, cts.Token);
        ServerboundCookieResponsePacket second = await AskAsync(server, Key, cts.Token);

        Assert.Equal(new byte[] { 0x01 }, first.Payload);
        Assert.Equal(new byte[] { 0x01 }, second.Payload);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ResolveAsync_WithNoResolver_IsThePlainStoreRead()
    {
        var store = new CookieStore();
        Assert.Null(await store.ResolveAsync(Key));

        store.Set(Key, [0x09]);
        Assert.Equal(new byte[] { 0x09 }, await store.ResolveAsync(Key));
    }

    [Fact]
    public async Task OversizedResolverValue_FallsBackWithoutReplacingAValidStoredCookie()
    {
        var store = new CookieStore();
        store.Set(Key, [0x42]);
        store.Resolver = (_, _) => ValueTask.FromResult<byte[]?>(new byte[5121]);

        Assert.Equal(new byte[] { 0x42 }, await store.ResolveAsync(Key));
        Assert.Equal(new byte[] { 0x42 }, store.Get(Key));
    }

    [Fact]
    public async Task OversizedStoredValue_IsAnsweredAsAbsentOnTheWire()
    {
        using var cts = new CancellationTokenSource(PluginSessionHarness.Budget);
        await using FakeJavaServer server = FakeJavaServer.Create();
        UmpkClient client = PluginSessionHarness.BuildClient(server);
        await using UmpkClient owned = client;
        client.Cookies.Set(Key, new byte[5121]);

        ServerboundCookieResponsePacket response = await RequestCookieAsync(client, server, Key, cts.Token);

        Assert.Null(response.Payload);
    }

    private static async Task<ServerboundCookieResponsePacket> RequestCookieAsync(
        UmpkClient client, FakeJavaServer server, Identifier key, CancellationToken ct)
    {
        await PluginSessionHarness.JoinAsync(client, server, ct);
        return await AskAsync(server, key, ct);
    }

    private static async Task<ServerboundCookieResponsePacket> AskAsync(
        FakeJavaServer server, Identifier key, CancellationToken ct)
    {
        await PluginSessionHarness.SendAsync(
            server, ProtocolPhase.Play, new ClientboundCookieRequestPacket(key), ct);

        InboundFrame reply = await server.NextFrameAsync(ct);
        ProtocolDescriptor descriptor = PluginSessionHarness.Version.Protocol;
        Assert.True(descriptor.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Serverbound, out PhaseRegistry registry));
        Assert.True(registry.TryGetInbound(reply.WireId, out BoundPacketCodec codec));
        return Assert.IsType<ServerboundCookieResponsePacket>(
            codec.Decode(reply.Payload, PacketCodecContext.Registryless));
    }
}
