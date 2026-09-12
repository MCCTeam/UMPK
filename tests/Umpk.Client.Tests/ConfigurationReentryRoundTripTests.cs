using System.Buffers;
using System.IO.Pipelines;
using Umpk.Client.Events;
using Umpk.Data.Java;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>The whole play-to-configuration-to-play round trip over a real <see cref="JavaConnection"/> against a scripted server, driving a real <see cref="UmpkClient"/> from <see cref="UmpkClient.ConnectAsync"/> onwards. The acknowledgement is asserted as a frame with the protocol's real serverbound wire id, not as a state change, and the client then has to answer a configuration keep-alive and finish the phase to reach the second join.</summary>
/// <remarks>For protocol 774, the server sends <c>start_configuration</c> and waits for the acknowledgement. It then sends <c>finish_configuration</c> before the second join. It does not send registry data, tags, known-pack negotiation, or a brand during re-entry because those are login-only data.</remarks>
public sealed class ConfigurationReentryRoundTripTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    private static JavaVersion Version => JavaVersions.V1_21_11;

    private sealed class PipeConnectionFactory(IDuplexPipe pipe) : IConnectionFactory
    {
        public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct)
            => ValueTask.FromResult(pipe);
    }

    [Fact]
    public async Task PlayToConfigurationAndBack_AcknowledgesOnTheWireAndReturnsToPlay()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        ProtocolDescriptor descriptor = Version.Protocol;
        await using FakeJavaServer server = FakeJavaServer.Create();

        await using UmpkClient client = new UmpkClientBuilder()
            .UseVersion(Version)
            .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
            .UseConnectionFactory(new PipeConnectionFactory(server.ClientPipe))
            .UseStaticRegistries(JavaGameData.Registries(Version.Version.Protocol))
            .ConfigureFeatures(f =>
            {
                // Physics off: the idle tick would otherwise start sending position packets on its own clock, which makes the scripted frame order non-deterministic. The phase behaviour under test is independent of it.
                f.Physics = false;
                f.Pathfinding = false;
            })
            .Build();

        var phases = new List<ProtocolPhase>();
        client.Events.Subscribe<PhaseChanged>(e => phases.Add(e.Phase));

        // JoinedGame is published at the END of the join applier, after the world is built and the spawned flag is set, so counting it is the only wait that cannot observe a half-applied join.
        int joins = 0;
        client.Events.Subscribe<JoinedGame>(_ => Interlocked.Increment(ref joins));

        Task connect = client.ConnectAsync(new ServerEndpoint("test", 25565), ct);

        InboundFrame handshake = await server.NextFrameAsync(ct);
        Assert.Equal(0x00, handshake.WireId);
        server.ServerConnection.SetPhase(ProtocolPhase.Login);

        InboundFrame hello = await server.NextFrameAsync(ct);
        Assert.Equal(WireId(descriptor, ProtocolPhase.Login, PacketFlow.Serverbound, LoginPackets.Serverbound.Hello.Id), hello.WireId);

        await SendAsync(server, descriptor, ProtocolPhase.Login,
            new ClientboundLoginFinishedPacket(Guid.NewGuid(), "Tester", [], null), ct);

        InboundFrame ack = await server.NextFrameAsync(ct);
        Assert.Equal(
            WireId(descriptor, ProtocolPhase.Login, PacketFlow.Serverbound, LoginPackets.Serverbound.LoginAcknowledged.Id),
            ack.WireId);
        server.ServerConnection.SetPhase(ProtocolPhase.Configuration);

        // The login entry announces the client information; the re-entry below must NOT, which is the one documented difference between the two entries into the same driver.
        InboundFrame clientInfo = await server.NextFrameAsync(ct);
        Assert.Equal(
            WireId(descriptor, ProtocolPhase.Configuration, PacketFlow.Serverbound, ConfigurationPackets.Serverbound.ClientInformation.Id),
            clientInfo.WireId);

        await SendAsync(server, descriptor, ProtocolPhase.Configuration, new ClientboundFinishConfigurationPacket(), ct);
        InboundFrame finishOne = await server.NextFrameAsync(ct);
        Assert.Equal(
            WireId(descriptor, ProtocolPhase.Configuration, PacketFlow.Serverbound, ConfigurationPackets.Serverbound.FinishConfiguration.Id),
            finishOne.WireId);
        server.ServerConnection.SetPhase(ProtocolPhase.Play);
        await Umpk.Client.Tests.Support.ScriptedServer.SendPlayReadinessFrameAsync(server, descriptor, ct);

        await connect.WaitAsync(Budget, ct);
        Assert.Equal(ClientStatus.Playing, client.Status);

        await SendAsync(server, descriptor, ProtocolPhase.Play, Join(1), ct);
        await WaitForAsync(() => Volatile.Read(ref joins) >= 1, ct);
        Assert.True(client.State.Self.HasSpawned);
        Assert.True(client.State.HasWorld);

        // No player_loaded is sent merely because JoinGame arrived: placement and usable terrain have not happened yet. Keep skipping tick-end frames while reading the later acknowledgement.
        int tickEnd = WireId(descriptor, ProtocolPhase.Play, PacketFlow.Serverbound, PlayPackets.Serverbound.ClientTickEnd.Id);

        // Let the 20-per-second tick loop run first, so several client_tick_end frames are provably queued ahead of the acknowledgement. Without this the skip below is exercised only by luck, which is how the first version of this test passed alone and failed inside the full parallel suite.
        await Task.Delay(250, ct);
        await SendAsync(server, descriptor, ProtocolPhase.Play, new ClientboundStartConfigurationPacket(), ct);

        // The assertion: without this response the server closes the connection fifteen seconds later on the keep-alive timeout.
        InboundFrame acknowledged = await NextInterestingFrameAsync(server, tickEnd, ct);
        Assert.Equal(
            WireId(descriptor, ProtocolPhase.Play, PacketFlow.Serverbound, PlayPackets.Serverbound.ConfigurationAcknowledged.Id),
            acknowledged.WireId);
        Assert.Empty(acknowledged.Payload.ToArray());
        server.ServerConnection.SetPhase(ProtocolPhase.Configuration);

        // The client must be decoding with the CONFIGURATION descriptor now, which a keep-alive proves: its wire id collides with an entirely different play packet, and the reply has to come back on the configuration serverbound id. This is also exactly what the server was sending unanswered.
        await SendAsync(server, descriptor, ProtocolPhase.Configuration, new ClientboundConfigKeepAlivePacket(0x5150), ct);
        // No skipping from here on: client_tick_end is gated on Self.HasSpawned, which the re-entry reset clears, so the configuration phase generates no background traffic at all. Skipping a PLAY wire id while reading CONFIGURATION frames would also be unsound, because the two tables number different packets.
        InboundFrame keepAlive = await server.NextFrameAsync(ct);
        Assert.Equal(
            WireId(descriptor, ProtocolPhase.Configuration, PacketFlow.Serverbound, ConfigurationPackets.Serverbound.KeepAlive.Id),
            keepAlive.WireId);
        var keepAliveReader = new PacketReader(keepAlive.Payload);
        Assert.Equal(0x5150, keepAliveReader.ReadLong());

        // Vanilla's re-entry sends no client_information; the server carries the existing one into the new listener's cookie. The next frame being the finish acknowledgement is what proves none was sent.
        await SendAsync(server, descriptor, ProtocolPhase.Configuration, new ClientboundFinishConfigurationPacket(), ct);
        InboundFrame finishTwo = await server.NextFrameAsync(ct);
        Assert.Equal(
            WireId(descriptor, ProtocolPhase.Configuration, PacketFlow.Serverbound, ConfigurationPackets.Serverbound.FinishConfiguration.Id),
            finishTwo.WireId);
        server.ServerConnection.SetPhase(ProtocolPhase.Play);

        await SendAsync(server, descriptor, ProtocolPhase.Play, Join(99), ct);
        await WaitForAsync(() => Volatile.Read(ref joins) >= 2, ct);
        Assert.Equal(99, client.State.Self.EntityId);

        Assert.True(client.State.Self.HasSpawned);
        Assert.True(client.State.HasWorld);
        Assert.Equal(ClientStatus.Playing, client.Status);
        Assert.Equal(ProtocolPhase.Play, client.Session!.Phase);

        // Play, into configuration, back into play, observable to a host.
        Assert.Equal(
            [ProtocolPhase.Play, ProtocolPhase.Configuration, ProtocolPhase.Play],
            phases);

        // The registries survived: the server never resent them, so anything that dropped them here would leave the session unable to resolve a dimension type for the rest of its life.
        Assert.NotNull(client.State.Registries);
        Assert.NotEmpty(client.State.Registries!.EntityTypes);
    }

    private static ClientboundLoginPacket Join(int entityId)
    {
        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: 0, Dimension: "minecraft:overworld", Seed: 0, GameType: 0, PreviousGameType: -1,
            IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);
        return new ClientboundLoginPacket(
            PlayerId: entityId, Hardcore: false, Dimensions: ["minecraft:overworld"], MaxPlayers: 20,
            ViewDistance: 8, SimulationDistance: 8, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: spawn, OnlineMode: false, EnforcesSecureChat: false, Legacy: null);
    }

    /// <summary>The next serverbound frame that is not <c>client_tick_end</c>.</summary>
    /// <remarks>From 1.21.2 the client sends <c>client_tick_end</c> on every one of its own 20-per-second ticks while the player is spawned (<c>UmpkClient.OnTickOnLoop</c>), on a clock that has nothing to do with this script. Reading the "next" frame and asserting its identity therefore races the tick loop: it passed alone and failed inside the full parallel suite, where the machine is loaded and one more tick fits before the frame under test. Skipping exactly that one id, and nothing else, keeps the assertions strict, and a frame that never arrives still fails on the test's cancellation budget rather than passing quietly.</remarks>
    private static async Task<InboundFrame> NextInterestingFrameAsync(
        FakeJavaServer server, int tickEndWireId, CancellationToken ct)
    {
        while (true)
        {
            InboundFrame frame = await server.NextFrameAsync(ct);
            if (frame.WireId != tickEndWireId)
                return frame;

        }
    }

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(5, ct).ConfigureAwait(false);
        }
    }

    private static int WireId(ProtocolDescriptor descriptor, ProtocolPhase phase, PacketFlow flow, Identifier id)
    {
        Assert.True(descriptor.TryGetRegistry(phase, flow, out PhaseRegistry registry));
        foreach ((int wireId, PacketType type) in registry.Packets)
            if (type.Id == id)
                return wireId;

        throw new Xunit.Sdk.XunitException($"{id} is not registered in {phase}/{flow}.");
    }

    private static async Task SendAsync<TPacket>(
        FakeJavaServer server, ProtocolDescriptor descriptor, ProtocolPhase phase, TPacket packet, CancellationToken ct)
        where TPacket : class, IPacket
    {
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
}
