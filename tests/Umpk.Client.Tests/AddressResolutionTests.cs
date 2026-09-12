using System.Buffers;
using System.IO.Pipelines;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>Resolving an address must affect both the connection and the handshake. SRV redirection is attempted only for the default port, and resolution failures leave the requested endpoint unchanged.</summary>
public sealed class AddressResolutionTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    private static JavaVersion Version => JavaVersions.V1_21_11;

    /// <summary>The default-port case: the resolver's answer must be what gets dialed AND what goes out in the handshake, and the session must remember the resolved endpoint, not the one the caller typed.</summary>
    [Fact]
    public async Task ConnectAsyncDialsAndHandshakesTheResolvedAddress_WhenNoExplicitPortWasGiven()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await using FakeJavaServer server = FakeJavaServer.Create();
        var resolved = new ServerEndpoint("srv-target.example", 25599);
        var resolver = new RecordingResolver(resolved);
        var factory = new RecordingConnectionFactory(server.ClientPipe);
        await using UmpkClient client = Client(server, resolver, factory);

        var requested = new ServerEndpoint("mc.example"); // default port
        ServerboundHandshakePacket handshake = await ConnectThroughLoginAsync(client, server, requested, ct);

        Assert.Equal(1, resolver.InvocationCount);
        Assert.Equal(requested, resolver.LastRequested);
        Assert.Equal(resolved, factory.Requested);
        Assert.Equal(resolved.Host, handshake.ServerAddress);
        Assert.Equal(resolved.Port, handshake.ServerPort);
        Assert.Equal(resolved, client.Session!.Endpoint);
    }

    /// <summary>The explicit-port case: only the default port permits address redirection, so an endpoint with a non-default port must never reach <see cref="IServerAddressResolver.ResolveAsync"/> at all. The resolver here is armed to return an endpoint that would fail this test outright if it were ever consulted, so an unconditional-resolve implementation cannot pass this by accident.</summary>
    [Fact]
    public async Task ConnectAsyncSkipsTheResolver_WhenAnExplicitPortWasGiven()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await using FakeJavaServer server = FakeJavaServer.Create();
        var resolver = new RecordingResolver(new ServerEndpoint("should-never-be-dialed.example", 1));
        var factory = new RecordingConnectionFactory(server.ClientPipe);
        await using UmpkClient client = Client(server, resolver, factory);

        var requested = new ServerEndpoint("mc.example", 25599); // explicit, non-default port
        ServerboundHandshakePacket handshake = await ConnectThroughLoginAsync(client, server, requested, ct);

        Assert.Equal(0, resolver.InvocationCount);
        Assert.Equal(requested, factory.Requested);
        Assert.Equal(requested.Host, handshake.ServerAddress);
        Assert.Equal(requested.Port, handshake.ServerPort);
    }

    /// <summary>A resolver that throws must not fail the connect. Vanilla's own redirect lookup wraps the whole attempt in <c>catch (Throwable)</c> and treats a failed lookup exactly like "no SRV record": the address is used as given.</summary>
    [Fact]
    public async Task ConnectAsyncFallsBackToTheGivenAddress_WhenTheResolverThrows()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await using FakeJavaServer server = FakeJavaServer.Create();
        var resolver = RecordingResolver.Throwing(new InvalidOperationException("resolver exploded"));
        var factory = new RecordingConnectionFactory(server.ClientPipe);
        await using UmpkClient client = Client(server, resolver, factory);

        var requested = new ServerEndpoint("mc.example"); // default port, so the resolver IS consulted
        await ConnectThroughLoginAsync(client, server, requested, ct);

        Assert.Equal(ClientStatus.Playing, client.Status);
        Assert.Equal(requested, factory.Requested);
    }

    private static UmpkClient Client(FakeJavaServer server, IServerAddressResolver resolver, IConnectionFactory factory) =>
        new UmpkClientBuilder()
            .UseVersion(Version)
            .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
            .UseConnectionFactory(factory)
            .UseAddressResolver(resolver)
            .UseStaticRegistries(JavaGameData.Registries(Version.Version.Protocol))
            .Build();

    /// <summary>Drives a connect through handshake, login and configuration (mirroring <c>UnloadedChunkTickGateTests.JoinAsync</c>) and returns the decoded handshake/intention packet, which is frame 1 of what the client sends. Stops once <see cref="UmpkClient.ConnectAsync"/> itself completes; none of these tests need the play-phase join packet.</summary>
    private static async Task<ServerboundHandshakePacket> ConnectThroughLoginAsync(
        UmpkClient client, FakeJavaServer server, ServerEndpoint endpoint, CancellationToken ct)
    {
        Task connect = client.ConnectAsync(endpoint, ct);

        InboundFrame handshakeFrame = await server.NextFrameAsync(ct); // handshake
        ServerboundHandshakePacket handshake = DecodeHandshake(handshakeFrame);

        server.ServerConnection.SetPhase(ProtocolPhase.Login);
        await server.NextFrameAsync(ct); // hello
        await SendAsync(server, ProtocolPhase.Login,
            new ClientboundLoginFinishedPacket(Guid.NewGuid(), "Tester", [], null), ct);
        await server.NextFrameAsync(ct); // login_acknowledged
        server.ServerConnection.SetPhase(ProtocolPhase.Configuration);
        await server.NextFrameAsync(ct); // client_information
        await SendAsync(server, ProtocolPhase.Configuration, new ClientboundFinishConfigurationPacket(), ct);
        await server.NextFrameAsync(ct); // finish_configuration
        server.ServerConnection.SetPhase(ProtocolPhase.Play);
        await Support.ScriptedServer.SendPlayReadinessFrameAsync(server, Version.Protocol, ct);
        await connect.WaitAsync(Budget, ct);

        return handshake;
    }

    private static ServerboundHandshakePacket DecodeHandshake(InboundFrame frame)
    {
        var reader = new PacketReader(frame.Payload);
        return HandshakeCodecs.Intention.Decode(ref reader, PacketCodecContext.Registryless);
    }

    private static async Task SendAsync<TPacket>(
        FakeJavaServer server, ProtocolPhase phase, TPacket packet, CancellationToken ct)
        where TPacket : class, IPacket
    {
        ProtocolDescriptor descriptor = Version.Protocol;
        Assert.True(descriptor.TryGetRegistry(phase, PacketFlow.Clientbound, out PhaseRegistry registry));
        foreach ((int wireId, PacketType type) in registry.Packets)
        {
            if (type.Id != packet.Type.Id)
                continue;

            Assert.True(registry.TryGetInbound(wireId, out BoundPacketCodec codec));
            Assert.True(codec.IsImplemented, $"{packet.Type.Id} is a marker in {phase}.");
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(buffer);
            codec.Encode(ref writer, packet, PacketCodecContext.Registryless);
            await server.SendFrameAsync(wireId, buffer.WrittenSpan.ToArray(), ct);
            return;
        }

        throw new Xunit.Sdk.XunitException($"{packet.Type.Id} is not registered clientbound in {phase}.");
    }

    private sealed class RecordingConnectionFactory(IDuplexPipe pipe) : IConnectionFactory
    {
        public ServerEndpoint? Requested { get; private set; }

        public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct)
        {
            Requested = endpoint;
            return ValueTask.FromResult(pipe);
        }
    }

    /// <summary>Records every call it receives and either returns a fixed target or throws.</summary>
    private sealed class RecordingResolver : IServerAddressResolver
    {
        private readonly ServerEndpoint? _target;
        private readonly Exception? _fault;

        public RecordingResolver(ServerEndpoint target)
        {
            _target = target;
        }

        private RecordingResolver(Exception fault)
        {
            _fault = fault;
        }

        public static RecordingResolver Throwing(Exception fault) => new(fault);

        public int InvocationCount { get; private set; }

        public ServerEndpoint? LastRequested { get; private set; }

        public ValueTask<ServerEndpoint> ResolveAsync(ServerEndpoint endpoint, CancellationToken ct)
        {
            InvocationCount++;
            LastRequested = endpoint;
            if (_fault is not null)
                throw _fault;

            return ValueTask.FromResult(_target!);
        }
    }
}
