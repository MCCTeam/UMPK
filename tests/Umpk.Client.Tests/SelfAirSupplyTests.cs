using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Internal;
using Umpk.Client.Navigation;
using Umpk.Client.State;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Pathfinding;
using Umpk.Physics;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>
/// The local player's air supply (breath): the metadata path, the local per-tick rule, and the precedence between them.
/// <para><c>EntityMetadataKeys.AirSupply</c> existed and <c>JavaEntityMetadataKeys</c> mapped it at index 1 on all 50 protocols, but nothing consumed it for the LOCAL player: self is not a member of the shared <c>EntityStore</c>, so <c>EntityApplier</c>'s <c>set_entity_data</c> arm looked the id up, missed, and returned true anyway. Self air frames must update the local state rather than only decode, and <c>SelfState</c> carried no air field to put it in. A bot could not see its own breath, so nothing underwater could be breath-aware.</para>
/// </summary>
/// <remarks>Maximum air is 300. While the eye is underwater and no immunity applies, air decreases by one per tick; at -20 it resets to zero and drowning damage applies. Out of water it refills by four per tick. Protocols from 1.21.5 rely on synchronized air state, while UMPK predicts locally on every era; see <c>AirSupplyRule</c> for the reconciliation boundary.</remarks>
public sealed class SelfAirSupplyTests
{
    private const int SelfEntityId = 1;
    private const int OtherEntityId = 42;

    /// <summary>1.21.8. Modern metadata format, real generated block data for the water column.</summary>
    private const int ModernProtocol = 772;

    /// <summary>1.8. The one era whose air field is a 16-bit SHORT rather than a VarInt.</summary>
    private const int LegacyProtocol = 47;

    private const int FloorY = 60;
    private const int WaterTopY = 70;

    // The surface itself

    /// <summary>A fresh session starts with a full lung, because vanilla's own data watcher is DEFINED at the maximum, not at zero. A client that reported 0 before the first frame arrived would read as "already drowning" to anything breath-aware.</summary>
    [Fact]
    public void AFreshSelfState_StartsWithAFullAirSupply()
    {
        var self = new SelfState();

        Assert.Equal(300, self.MaxAirSupply);
        Assert.Equal(300, self.AirSupply);
    }

    /// <summary>The snapshot projects it, like every other tracked self field.</summary>
    [Fact]
    public void TheSelfSnapshot_CarriesTheAirSupply()
    {
        var state = new ClientState(new ClientFeatures().Normalized());
        state.Self.AirSupply = 137;

        Umpk.Client.Snapshots.SelfSnapshot snapshot = Umpk.Client.Snapshots.SelfSnapshot.Project(state);

        Assert.Equal(137, snapshot.AirSupply);
        Assert.Equal(300, snapshot.MaxAirSupply);
    }

    // The metadata path, from bytes

    /// <summary>
    /// The self-air frame, decoded through the codec the catalog binds, on both metadata wire formats.
    /// <para>Protocol 47 bytes: <c>01</c> VarInt entity id 1 | <c>21</c> the packed 1.8 header <c>(type 1 &lt;&lt; 5) | index 1</c>, type 1 being the DataWatcher's 16-bit short | <c>0089</c> short 137 | <c>7F</c> the 1.8 list terminator. Protocol 772 bytes: <c>01</c> VarInt entity id 1 | <c>01</c> index byte 1 | <c>01</c> VarInt serializer id 1 (<c>INT</c>) | <c>8901</c> VarInt 137 | <c>FF</c> the modern terminator.</para>
    /// <para>Index 1 on both, and on every protocol between them: air is the second field vanilla declares on <c>Entity</c> and nothing has ever been inserted above it (pinned across all 50 protocols by <c>EntityMetadataKeyTableTests</c>).</para>
    /// </summary>
    [Theory]
    [InlineData(LegacyProtocol, "012100897F")]
    [InlineData(ModernProtocol, "0101018901FF")]
    public async Task AWireDecodedSelfAirFrame_LandsOnSelfState(int protocol, string frameHex)
    {
        ApplierHarness harness = MetadataHarness(protocol);

        ClientboundSetEntityDataPacket decoded = DecodeMetadata(harness, protocol, frameHex);
        Assert.Equal(SelfEntityId, decoded.EntityId);

        Assert.True(await harness.TryApplyAsync(decoded), "nothing in the applier chain owned the frame");

        Assert.Equal(137, harness.State.Self.AirSupply);
    }

    /// <summary>The control against over-claiming the packet. <c>SelfApplier</c> is registered BEFORE <c>EntityApplier</c> (<c>ApplierCatalog</c>), so an arm that owned every <c>set_entity_data</c> frame rather than only the self one would silently stop every other entity's metadata from ever reaching the entity store, and no assertion about self could see it.</summary>
    [Fact]
    public async Task AnotherEntitysAirFrame_StillReachesTheEntityStore_AndLeavesSelfAlone()
    {
        ApplierHarness harness = MetadataHarness(ModernProtocol);

        await harness.ApplyAsync(new ClientboundAddEntityPacket(
            OtherEntityId, Guid.NewGuid(), 0, 0, 64, 0, 0, 0, 0, 0, 0, 0, 0, null));

        // The same six-byte shape as the modern self frame, addressed to entity 42 (0x2A) instead.
        ClientboundSetEntityDataPacket decoded = DecodeMetadata(harness, ModernProtocol, "2A01018901FF");
        Assert.Equal(OtherEntityId, decoded.EntityId);
        Assert.True(await harness.TryApplyAsync(decoded));

        Assert.True(harness.State.Entities.TryGet(OtherEntityId, out Umpk.Game.Entities.Entity? other));
        Assert.NotNull(other);
        Assert.True(other!.Metadata.TryGet(EntityMetadataKeys.AirSupply, out int otherAir));
        Assert.Equal(137, otherAir);

        Assert.Equal(300, harness.State.Self.AirSupply);
    }

    /// <summary>A self frame that carries fields OTHER than air must not disturb the tracked value. The wire only carries only dirty entries, so "air absent" is the normal case, not an edge one.</summary>
    [Fact]
    public async Task ASelfFrameWithoutAir_LeavesTheTrackedValueAlone()
    {
        ApplierHarness harness = MetadataHarness(ModernProtocol);
        harness.State.Self.AirSupply = 42;

        // 01 entity 1 | 00 index 0 (shared flags) | 00 serializer 0 (BYTE) | 20 flags | FF terminator.
        ClientboundSetEntityDataPacket decoded = DecodeMetadata(harness, ModernProtocol, "010000 20FF".Replace(" ", ""));
        Assert.True(await harness.TryApplyAsync(decoded));

        Assert.Equal(42, harness.State.Self.AirSupply);
    }

    // The local per-tick rule, over a real physics engine in real water

    /// <summary>The drain and the drowning reset, as a literal table over vanilla's own arithmetic: one tick of eye-in-water costs exactly one tick of air (<c>decreaseAirSupply</c>, <c>air - 1</c>), and the tick that would reach -20 resets the value to 0 instead. The reset is all the client does; the drowning DAMAGE that accompanies it is the server's, and arrives as a health frame.</summary>
    [Theory]
    [InlineData(1, 299)]
    [InlineData(60, 240)]
    [InlineData(300, 0)]
    [InlineData(319, -19)]
    [InlineData(320, 0)]
    [InlineData(321, -1)]
    public async Task Submerged_TheLocalTickDrainsOneTickOfAirPerTick(int ticks, int expected)
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartSubmergedAsync();
        await using (loop)
        {
            TickAir(holder, ticks);
            Assert.Equal(expected, harness.State.Self.AirSupply);
        }
    }

    /// <summary>Out of the water the lung refills four times as fast and stops at the maximum at four units per tick. Driven from a drained value so the cap is reached rather than assumed.</summary>
    [Theory]
    [InlineData(100, 1, 104)]
    [InlineData(100, 10, 140)]
    [InlineData(100, 50, 300)]
    [InlineData(300, 5, 300)]
    public async Task OutOfWater_TheLocalTickRefillsFourPerTickAndStopsAtTheMaximum(int start, int ticks, int expected)
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartDryAsync();
        await using (loop)
        {
            harness.State.Self.AirSupply = start;
            TickAir(holder, ticks);
            Assert.Equal(expected, harness.State.Self.AirSupply);
        }
    }

    /// <summary>The immunity guard: a player whose abilities say <c>invulnerable</c> (creative and spectator) does not lose air at all. This is not cosmetic. The server's value does not change either, so no metadata is sent, so an unguarded prediction would drain to the drowning point and stay there with nothing to correct it.</summary>
    [Fact]
    public async Task AnInvulnerablePlayer_DoesNotLoseAirUnderwater()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartSubmergedAsync();
        await using (loop)
        {
            harness.State.Self.Invulnerable = true;

            TickAir(holder, 200);

            Assert.Equal(300, harness.State.Self.AirSupply);
        }
    }

    /// <summary>The same guard's other half: water breathing OR conduit power suspends drowning. The effect id is resolved through the session's own <c>minecraft:mob_effect</c> registry rather than hardcoded, because the numbering shifted from 1-based to 0-based at protocol 764 (pinned by <c>WireDecodedMobEffectTests</c>), so a literal would be wrong on one side or the other.</summary>
    [Theory]
    [InlineData("water_breathing")]
    [InlineData("conduit_power")]
    public async Task AWaterBreathingPlayer_DoesNotLoseAirUnderwater(string effect)
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartSubmergedAsync();
        await using (loop)
        {
            Assert.True(
                JavaGameData.Registries(ModernProtocol).MobEffects.TryGetNetworkId(
                    Identifier.Minecraft(effect), out int effectId),
                $"minecraft:{effect} is not in the protocol {ModernProtocol} mob-effect registry");
            harness.State.Self.ApplyEffect(new ActiveEffect(effectId, 0, 600, 0));

            TickAir(holder, 200);

            Assert.Equal(300, harness.State.Self.AirSupply);
        }
    }

    // The two paths together, through a real client tick loop

    /// <summary>
    /// The whole contract on one live session: the server's frame lands on <c>SelfState</c>, the client's own tick then moves the value on from there, and a later server frame OVERWRITES whatever the prediction had reached. Last write wins, and both writes happen on the session loop.
    /// <para>Driving the real <see cref="UmpkClient"/> tick loop is the point: the rule could be correct at its own seam and never be called, which is indistinguishable from a client that does not predict.</para>
    /// </summary>
    [Fact]
    public async Task OnALiveSession_TheServerFrameLands_TheTickMovesItOn_AndTheNextFrameWins()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        var ticks = new ManualTicks();
        await using Umpk.TestKit.Server.FakeJavaServer server = Umpk.TestKit.Server.FakeJavaServer.Create();
        await using UmpkClient client = SessionClient(server, ticks, physics: true);

        await SessionJoinAsync(client, server, ct);

        await SendAirAsync(server, SelfEntityId, 137, ct);
        await WaitForAsync(() => client.State.Self.AirSupply == 137, ct);

        // Five ticks out of water: 137 + 4*5. The player is in an empty column, so it is falling, which is exactly "not underwater" and refills.
        for (int i = 0; i < 5; i++)
            await ticks.FireAsync(ct);

        await WaitForAsync(() => client.State.Self.AirSupply >= 157, ct);
        await Task.Delay(200, ct);
        Assert.Equal(157, client.State.Self.AirSupply);

        // The server disagrees, and the server is right.
        await SendAirAsync(server, SelfEntityId, 42, ct);
        await WaitForAsync(() => client.State.Self.AirSupply == 42, ct);
    }

    /// <summary>With the physics feature off there is no engine to ask whether the eyes are in water, so the client must not invent a value: the tracked air is whatever the server last said and nothing else.</summary>
    [Fact]
    public async Task WithPhysicsDisabled_TheClientReportsOnlyWhatTheServerSent()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        var ticks = new ManualTicks();
        await using Umpk.TestKit.Server.FakeJavaServer server = Umpk.TestKit.Server.FakeJavaServer.Create();
        await using UmpkClient client = SessionClient(server, ticks, physics: false);

        await SessionJoinAsync(client, server, ct);

        await SendAirAsync(server, SelfEntityId, 137, ct);
        await WaitForAsync(() => client.State.Self.AirSupply == 137, ct);

        for (int i = 0; i < 25; i++)
            await ticks.FireAsync(ct);

        await Task.Delay(300, ct);
        Assert.Equal(137, client.State.Self.AirSupply);
    }

    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    /// <summary>One client tick's worth of air. The air rule runs BEFORE movement, so it reads the water state the previous tick's movement left behind.</summary>
    private static void TickAir(PhysicsEngineHolder holder, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            holder.TickAirSupply();
            holder.TickIdle();
        }
    }

    private static ApplierHarness MetadataHarness(int protocol)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out JavaVersion? version));
        var harness = new ApplierHarness(version!, new ClientFeatures { Entities = true });
        harness.State.Registries = JavaGameData.Registries(protocol);
        harness.State.Self.EntityId = SelfEntityId;
        return harness;
    }

    private static ClientboundSetEntityDataPacket DecodeMetadata(ApplierHarness harness, int protocol, string frameHex)
    {
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(protocol, "set_entity_data");
        var context = new PacketCodecContext(harness.State.Registries!, IConnectionCodecState.Empty);
        return (ClientboundSetEntityDataPacket)codec.Decode(Convert.FromHexString(frameHex), context);
    }

    /// <summary>A session whose player is settled at the bottom of a real water column, eyes under.</summary>
    private static async Task<(ApplierHarness, PhysicsEngineHolder, IAsyncDisposable)> StartSubmergedAsync()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartAsync();
        FillWater(harness);
        Seed(harness, holder, new Vec3d(6.5, FloorY + 2.0, 6.5));

        // Settle first, so the engine's own water sensing is established before any air tick runs and the counts below are not measuring the fall.
        for (int i = 0; i < 40; i++)
            holder.TickIdle();

        Assert.NotNull(holder.EngineState);
        Assert.True(holder.EngineState!.Value.IsUnderWater, "the player is not actually submerged");
        Assert.Equal(300, harness.State.Self.AirSupply);
        return (harness, holder, loop);
    }

    /// <summary>The dry twin: the same floor, no water.</summary>
    private static async Task<(ApplierHarness, PhysicsEngineHolder, IAsyncDisposable)> StartDryAsync()
    {
        (ApplierHarness harness, PhysicsEngineHolder holder, IAsyncDisposable loop) = await StartAsync();
        FillFloor(harness);
        Seed(harness, holder, new Vec3d(6.5, FloorY + 1.0, 6.5));

        for (int i = 0; i < 20; i++)
            holder.TickIdle();

        Assert.NotNull(holder.EngineState);
        Assert.False(holder.EngineState!.Value.IsUnderWater);
        return (harness, holder, loop);
    }

    private static void Seed(ApplierHarness harness, PhysicsEngineHolder holder, Vec3d position)
    {
        harness.State.Self.Position = position;
        harness.State.Self.Velocity = Vec3d.Zero;
        harness.State.Self.HasSpawned = true;
        holder.ResyncPosition();
    }

    private static void FillFloor(ApplierHarness harness)
    {
        Registry<BlockDefinition> blocks = JavaGameData.Registries(ModernProtocol).Blocks;
        Assert.True(blocks.TryGetValue(Identifier.Minecraft("stone"), out BlockDefinition? stone));
        Umpk.Game.World.World world = harness.State.World;
        for (int x = 3; x <= 10; x++)
            for (int z = 3; z <= 10; z++)
                world.SetBlockStateId(new BlockPos(x, FloorY, z), stone!.DefaultStateId);

    }

    private static void FillWater(ApplierHarness harness)
    {
        FillFloor(harness);
        Registry<BlockDefinition> blocks = JavaGameData.Registries(ModernProtocol).Blocks;
        Assert.True(blocks.TryGetValue(Identifier.Minecraft("water"), out BlockDefinition? water));
        Umpk.Game.World.World world = harness.State.World;
        for (int x = 3; x <= 10; x++)
            for (int z = 3; z <= 10; z++)
                for (int y = FloorY + 1; y <= WaterTopY; y++)
                    world.SetBlockStateId(new BlockPos(x, y, z), water!.DefaultStateId);

    }

    private static async Task<(ApplierHarness, PhysicsEngineHolder, IAsyncDisposable)> StartAsync()
    {
        Assert.True(JavaVersions.TryGetByProtocol(ModernProtocol, out JavaVersion? version));
        var harness = new ApplierHarness(version!, new ClientFeatures { Physics = true, Entities = true });
        harness.State.Registries = JavaGameData.Registries(ModernProtocol);

        var scheduler = new ChannelSessionScheduler();
        var services = new ClientSessionServices
        {
            Version = version!,
            Options = new ClientOptions(),
            Policies = new ClientPolicies(),
            State = harness.State,
            Wire = new WireIndex(version!),
            Logger = NullLogger.Instance,
            Scheduler = scheduler,
        };

        var holder = new PhysicsEngineHolder(services, JavaGameData.BlockShapes(ModernProtocol), NullLogger.Instance);
        harness.PositionResync = holder.ResyncPosition;

        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: 0, Dimension: "minecraft:overworld", Seed: 0, GameType: 0, PreviousGameType: -1,
            IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);
        await harness.ApplyAsync(new ClientboundLoginPacket(
            PlayerId: SelfEntityId, Hardcore: false, Dimensions: ["minecraft:overworld"], MaxPlayers: 20,
            ViewDistance: 8, SimulationDistance: 8, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: spawn, OnlineMode: false, EnforcesSecureChat: false, Legacy: null));

        holder.EnsureEngine();
        return (harness, holder, scheduler);
    }

    private static JavaVersion SessionVersion => JavaVersions.V1_21_11;

    private static UmpkClient SessionClient(
        Umpk.TestKit.Server.FakeJavaServer server, Umpk.Hosting.ITickSource ticks, bool physics) =>
        new UmpkClientBuilder()
            .UseVersion(SessionVersion)
            .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
            .UseConnectionFactory(new SessionPipeFactory(server.ClientPipe))
            .UseStaticRegistries(JavaGameData.Registries(SessionVersion.Version.Protocol))
            .UseTickSource(ticks)
            .ConfigureFeatures(f =>
            {
                f.Terrain = true;
                f.Physics = physics;
                f.Pathfinding = false;
            })
            .Build();

    private static async Task SessionJoinAsync(
        UmpkClient client, Umpk.TestKit.Server.FakeJavaServer server, CancellationToken ct)
    {
        Task connect = client.ConnectAsync(new ServerEndpoint("test", 25565), ct);

        await server.NextFrameAsync(ct); // handshake
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
        await Umpk.Client.Tests.Support.ScriptedServer.SendPlayReadinessFrameAsync(server, SessionVersion.Protocol, ct);
        await connect.WaitAsync(Budget, ct);

        // Everything the client writes from here has to be read off the pipe or the writer blocks.
        _ = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                    await server.NextFrameAsync(ct).ConfigureAwait(false);

            }
            catch (Exception)
            {
                // The pipe closing at the end of the test is the expected way out.
            }
        }, ct);

        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable joinSubscription = client.Events.Subscribe<Umpk.Client.Events.JoinedGame>(
            _ => joined.TrySetResult());
        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: 0, Dimension: "minecraft:overworld", Seed: 0, GameType: 0, PreviousGameType: -1,
            IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);
        await SendAsync(server, ProtocolPhase.Play, new ClientboundLoginPacket(
            PlayerId: SelfEntityId, Hardcore: false, Dimensions: ["minecraft:overworld"], MaxPlayers: 20,
            ViewDistance: 8, SimulationDistance: 8, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: spawn, OnlineMode: false, EnforcesSecureChat: false, Legacy: null), ct);
        await joined.Task.WaitAsync(Budget, ct);

        // The tick gate needs the player's own column and a spawn position, or nothing ticks at all.
        await client.InvokeAsync(c => c.State.World.LoadColumn(new ChunkPos(0, 0)), ct);

        var corrected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable positionSubscription = client.Events.Subscribe<Umpk.Client.Events.PositionCorrected>(
            _ => corrected.TrySetResult());
        var target = new Vec3d(8.5, 200.0, 8.5);
        await SendAsync(server, ProtocolPhase.Play, new ClientboundPlayerPositionPacket(
            target.X, target.Y, target.Z, 0f, 0f, RelativeFlags: 0, TeleportId: 1,
            ModernValues: new PositionMoveRotation(target, Vec3d.Zero, 0f, 0f)), ct);
        await corrected.Task.WaitAsync(Budget, ct);
    }

    /// <summary>A <c>set_entity_data</c> frame carrying nothing but the air field, as the server sends it.</summary>
    private static Task SendAirAsync(
        Umpk.TestKit.Server.FakeJavaServer server, int entityId, int air, CancellationToken ct)
    {
        Assert.True(
            JavaGameData.EntityMetadataKeys(SessionVersion.Version.Protocol)
                .TryResolveIndex(default, EntityMetadataKeys.AirSupply, out int index));
        var list = new EntityMetadataList([new EntityDataEntry(index, MetadataValue.VarInt(air))], []);
        return SendAsync(server, ProtocolPhase.Play, new ClientboundSetEntityDataPacket(entityId, list), ct);
    }

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(5, ct).ConfigureAwait(false);
        }
    }

    private static async Task SendAsync<TPacket>(
        Umpk.TestKit.Server.FakeJavaServer server, ProtocolPhase phase, TPacket packet, CancellationToken ct)
        where TPacket : class, IPacket
    {
        ProtocolDescriptor descriptor = SessionVersion.Protocol;
        Assert.True(descriptor.TryGetRegistry(phase, PacketFlow.Clientbound, out PhaseRegistry registry));
        foreach ((int wireId, PacketType type) in registry.Packets)
        {
            if (type.Id != packet.Type.Id)
                continue;

            Assert.True(registry.TryGetInbound(wireId, out BoundPacketCodec codec));
            Assert.True(codec.IsImplemented, $"{packet.Type.Id} is a marker in {phase}.");
            var buffer = new System.Buffers.ArrayBufferWriter<byte>();
            var writer = new PacketWriter(buffer);
            codec.Encode(ref writer, packet, PacketCodecContext.Registryless);
            await server.SendFrameAsync(wireId, buffer.WrittenSpan.ToArray(), ct);
            return;
        }

        throw new Xunit.Sdk.XunitException($"{packet.Type.Id} is not registered clientbound in {phase}.");
    }

    private sealed class SessionPipeFactory(System.IO.Pipelines.IDuplexPipe pipe) : IConnectionFactory
    {
        public ValueTask<System.IO.Pipelines.IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct)
            => ValueTask.FromResult(pipe);
    }

    /// <summary>A tick source the test advances by hand, so a tick lands exactly where it is wanted.</summary>
    private sealed class ManualTicks : Umpk.Hosting.ITickSource
    {
        private readonly System.Threading.Channels.Channel<long> _ticks =
            System.Threading.Channels.Channel.CreateUnbounded<long>();

        private long _next;

        public TimeSpan TickInterval => TimeSpan.FromMilliseconds(50);

        public IAsyncEnumerable<long> Ticks(CancellationToken cancellationToken = default) =>
            _ticks.Reader.ReadAllAsync(cancellationToken);

        public ValueTask FireAsync(CancellationToken ct) =>
            _ticks.Writer.WriteAsync(Interlocked.Increment(ref _next), ct);
    }
}
