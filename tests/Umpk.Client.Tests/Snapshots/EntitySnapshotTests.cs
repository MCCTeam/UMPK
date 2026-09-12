using System.Buffers;
using System.IO.Pipelines;
using Umpk;
using Umpk.Client;
using Umpk.Client.Events;
using Umpk.Client.Snapshots;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Items;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Umpk.TestKit.Server;
using Umpk.Text;
using Xunit;

namespace Umpk.Client.Tests.Snapshots;

/// <summary><see cref="EntitySnapshot"/> and the entity/world side of <see cref="ClientSnapshots"/> keep <c>CustomName</c> as a structured <see cref="Component"/> and <c>TypeId</c> an <see cref="Identifier"/> rather than flattening either to a plain string.</summary>
public sealed class EntitySnapshotTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    private static JavaVersion Version => JavaVersions.V1_21_11;

    private static RegistryAccess Registries => JavaGameData.Registries(Version.Version.Protocol);

    private static Entity NewEntity(int id = 100, Guid? uuid = null, string typeId = "minecraft:zombie") =>
        new(id, uuid ?? Guid.NewGuid(), Registries.EntityTypes[Identifier.Parse(typeId)]);

    // EntitySnapshot.Project - pure, no loop

    [Fact]
    public void Entity_ProjectsIdentityAndKinematics()
    {
        Guid uuid = Guid.NewGuid();
        Entity entity = NewEntity(id: 42, uuid: uuid);
        entity.Position = new Vec3d(1, 2, 3);
        entity.Velocity = new Vec3d(0.1, 0.2, 0.3);
        entity.Yaw = 45f;
        entity.Pitch = -30f;
        entity.HeadYaw = 90f;
        entity.OnGround = true;
        entity.Pose = EntityPose.Crouching;

        EntitySnapshot snapshot = EntitySnapshot.Project(entity);

        Assert.Equal(42, snapshot.Id);
        Assert.Equal(uuid, snapshot.Uuid);
        Assert.Equal(new Vec3d(1, 2, 3), snapshot.Position);
        Assert.Equal(new Vec3d(0.1, 0.2, 0.3), snapshot.Velocity);
        Assert.Equal(45f, snapshot.Yaw);
        Assert.Equal(-30f, snapshot.Pitch);
        Assert.Equal(90f, snapshot.HeadYaw);
        Assert.True(snapshot.OnGround);
        Assert.Equal(EntityPose.Crouching, snapshot.Pose);
    }

    [Fact]
    public void TypeId_IsARealRegistryIdentifier_NeverMinecraftUnknown()
    {
        Entity entity = NewEntity(typeId: "minecraft:zombie");

        EntitySnapshot snapshot = EntitySnapshot.Project(entity);

        Assert.Equal(Identifier.Parse("minecraft:zombie"), snapshot.TypeId);
        Assert.NotEqual(Identifier.Parse("minecraft:unknown"), snapshot.TypeId);
    }

    [Fact]
    public void CustomName_StaysAComponentWithStyleIntact()
    {
        Entity entity = NewEntity();
        var name = new Component(new TextContent("Fancy Zombie"), new Style { Bold = true, Color = TextColor.FromRgb(0xFF0000) });
        entity.CustomName = name;

        EntitySnapshot snapshot = EntitySnapshot.Project(entity);

        Assert.Equal(name, snapshot.CustomName);
        Assert.True(snapshot.CustomName!.Style.Bold);
        Assert.Equal(0xFF0000, snapshot.CustomName.Style.Color!.Value.Rgb);
    }

    [Fact]
    public void Equipment_CarriesTheStack()
    {
        Entity entity = NewEntity();
        var sword = new ItemStack(Registries.Items[Identifier.Parse("minecraft:diamond_sword")], 1);
        entity.SetEquipment(EquipmentSlot.MainHand, sword);

        EntitySnapshot snapshot = EntitySnapshot.Project(entity);

        Assert.Same(sword, snapshot.Equipment[EquipmentSlot.MainHand]);
    }

    [Fact]
    public void Equipment_EmptySlotIsItemStackEmpty()
    {
        Entity entity = NewEntity();
        entity.SetEquipment(EquipmentSlot.OffHand, null);

        EntitySnapshot snapshot = EntitySnapshot.Project(entity);

        Assert.Equal(ItemStack.Empty, snapshot.Equipment[EquipmentSlot.OffHand]);
    }

    [Fact]
    public void Effects_ProjectAResolvedRegistryEntry()
    {
        Entity entity = NewEntity();
        RegistryEntry<MobEffectDefinition> speed = Registries.MobEffects[Identifier.Parse("minecraft:speed")];
        entity.AddOrRefreshEffect(new EffectInstance(speed, amplifier: 1, duration: 100, EffectFlags.ShowParticles));

        EntitySnapshot snapshot = EntitySnapshot.Project(entity);

        EffectSnapshot effect = Assert.Single(snapshot.Effects);
        Assert.Equal(Identifier.Parse("minecraft:speed"), effect.EffectId);
        Assert.Equal(1, effect.Amplifier);
        Assert.Equal(2, effect.Level);
        Assert.True(effect.ShowParticles);
    }

    [Fact]
    public void PassengerAndVehicleEdges_ProjectBothDirections()
    {
        var store = new EntityStore();
        Entity horse = store.Add(NewEntity(id: 1, typeId: "minecraft:horse"));
        Entity rider = store.Add(NewEntity(id: 2, typeId: "minecraft:zombie"));
        store.SetVehicle(rider, horse);

        EntitySnapshot horseSnapshot = EntitySnapshot.Project(horse);
        EntitySnapshot riderSnapshot = EntitySnapshot.Project(rider);

        Assert.Equal([2], horseSnapshot.PassengerIds);
        Assert.Null(horseSnapshot.VehicleId);
        Assert.Empty(riderSnapshot.PassengerIds);
        Assert.Equal(1, riderSnapshot.VehicleId);
    }

    // ClientSnapshots entity queries - marshalled, but against an unconnected client (no network needed)

    [Theory]
    [InlineData("zombie")]
    [InlineData("minecraft:zombie")]
    public async Task EntitiesOfType_MatchesBareAndNamespacedIds(string needle)
    {
        await using UmpkClient client = BuildIdleClient();
        client.State.Entities.Add(NewEntity(id: 1, typeId: "minecraft:zombie"));
        client.State.Entities.Add(NewEntity(id: 2, typeId: "minecraft:skeleton"));

        IReadOnlyList<EntitySnapshot> matches = await client.Snapshots.EntitiesOfTypeAsync(needle);

        EntitySnapshot match = Assert.Single(matches);
        Assert.Equal(1, match.Id);
    }

    [Fact]
    public async Task NearestEntity_ReturnsTheClosestOne()
    {
        await using UmpkClient client = BuildIdleClient();
        Entity near = NewEntity(id: 1);
        near.Position = new Vec3d(2, 0, 0);
        Entity far = NewEntity(id: 2);
        far.Position = new Vec3d(9, 0, 0);
        client.State.Entities.Add(near);
        client.State.Entities.Add(far);

        EntitySnapshot? nearest = await client.Snapshots.NearestEntityAsync(radius: 20);

        Assert.NotNull(nearest);
        Assert.Equal(1, nearest!.Id);
    }

    [Fact]
    public async Task NearestEntity_HonoursThePredicate()
    {
        await using UmpkClient client = BuildIdleClient();
        Entity near = NewEntity(id: 1, typeId: "minecraft:zombie");
        near.Position = new Vec3d(2, 0, 0);
        Entity far = NewEntity(id: 2, typeId: "minecraft:skeleton");
        far.Position = new Vec3d(9, 0, 0);
        client.State.Entities.Add(near);
        client.State.Entities.Add(far);

        EntitySnapshot? nearestSkeleton = await client.Snapshots.NearestEntityAsync(
            radius: 20, predicate: e => e.Type.Id.Path == "skeleton");

        Assert.NotNull(nearestSkeleton);
        Assert.Equal(2, nearestSkeleton!.Id);
    }

    // ClientSnapshots.RaycastViewAsync - needs a real connected session (Session + World)

    /// <summary>A ray aimed straight down at a bottom slab must stop at the slab's real (half-height) surface, not at the top of a unit cube: a unit-cube table would report the hit half a block lower (later) and on the wrong effective face.</summary>
    [Fact]
    public async Task RaycastView_UsesTheNegotiatedProtocolsShapes()
    {
        const int FloorY = 64;
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = BuildConnectableClient(server);

        await JoinAsync(client, server, ct);

        int slabState = DefaultStateId("minecraft:smooth_stone_slab");
        await client.InvokeAsync(c =>
        {
            c.State.World.LoadColumn(new ChunkPos(0, 0));
            c.State.World.SetBlockStateId(new BlockPos(0, FloorY, 0), slabState);
            return true;
        }, ct);

        // Looking straight down (pitch 90) from directly above the slab.
        await TeleportAsync(client, server, new Vec3d(0.5, FloorY + 3.0, 0.5), yaw: 0f, pitch: 90f, ct);

        BlockHitResult? hit = await client.Snapshots.RaycastViewAsync(maxDistance: 10.0);

        Assert.NotNull(hit);
        Assert.Equal(new BlockPos(0, FloorY, 0), hit!.Value.BlockPos);
        Assert.Equal(Direction.Up, hit.Value.Face);

        // The load-bearing number: a unit cube would report FloorY + 1.0 (the top of a full block). The real bottom-slab shape occupies only the lower half, so the ray travels further before it hits.
        Assert.Equal(FloorY + 0.5, hit.Value.Point.Y, 6);
    }

    [Fact]
    public async Task RaycastView_IsNullOnAMiss()
    {
        using var cts = new CancellationTokenSource(Budget);
        CancellationToken ct = cts.Token;

        await using FakeJavaServer server = FakeJavaServer.Create();
        await using UmpkClient client = BuildConnectableClient(server);

        await JoinAsync(client, server, ct);
        await client.InvokeAsync(c => c.State.World.LoadColumn(new ChunkPos(0, 0)), ct);

        // Looking straight up into open air: nothing within reach.
        await TeleportAsync(client, server, new Vec3d(0.5, 70.0, 0.5), yaw: 0f, pitch: -90f, ct);

        BlockHitResult? hit = await client.Snapshots.RaycastViewAsync(maxDistance: 10.0);

        Assert.Null(hit);
    }

    // helpers

    private static UmpkClient BuildIdleClient() => new UmpkClientBuilder()
        .UseVersion(Version)
        .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
        .Build();

    private static UmpkClient BuildConnectableClient(FakeJavaServer server) => new UmpkClientBuilder()
        .UseVersion(Version)
        .UseProfile(new GameProfile(Guid.NewGuid(), "Tester"))
        .UseConnectionFactory(new PipeConnectionFactory(server.ClientPipe))
        .UseStaticRegistries(Registries)
        .ConfigureFeatures(f =>
        {
            f.Physics = false;
            f.Pathfinding = false;
        })
        .Build();

    private static int DefaultStateId(string blockName)
    {
        Assert.True(Registries.Blocks.TryGetValue(Identifier.Parse(blockName), out var definition));
        return definition!.DefaultStateId;
    }

    private static async Task JoinAsync(UmpkClient client, FakeJavaServer server, CancellationToken ct)
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
        await Umpk.Client.Tests.Support.ScriptedServer.SendPlayReadinessFrameAsync(server, Version.Protocol, ct);
        await connect.WaitAsync(Budget, ct);

        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Events.Subscribe<JoinedGame>(_ => joined.TrySetResult());
        var spawn = new CommonPlayerSpawnInfo(
            DimensionTypeId: 0, Dimension: "minecraft:overworld", Seed: 0, GameType: 0, PreviousGameType: -1,
            IsDebug: false, IsFlat: false, LastDeathDimensionAndPos: null, PortalCooldown: 0, SeaLevel: 63);
        await SendAsync(server, ProtocolPhase.Play, new ClientboundLoginPacket(
            PlayerId: 1, Hardcore: false, Dimensions: ["minecraft:overworld"], MaxPlayers: 20,
            ViewDistance: 8, SimulationDistance: 8, ReducedDebugInfo: false, ShowDeathScreen: true,
            DoLimitedCrafting: false, SpawnInfo: spawn, OnlineMode: false, EnforcesSecureChat: false, Legacy: null), ct);
        await joined.Task.WaitAsync(Budget, ct);
    }

    private static async Task TeleportAsync(
        UmpkClient client, FakeJavaServer server, Vec3d target, float yaw, float pitch, CancellationToken ct)
    {
        var corrected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = client.Events.Subscribe<PositionCorrected>(_ => corrected.TrySetResult());
        await SendAsync(server, ProtocolPhase.Play, new ClientboundPlayerPositionPacket(
            target.X, target.Y, target.Z, yaw, pitch, RelativeFlags: 0, TeleportId: 1,
            ModernValues: new PositionMoveRotation(target, Vec3d.Zero, yaw, pitch)), ct);
        await corrected.Task.WaitAsync(Budget, ct);
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

    private sealed class PipeConnectionFactory(IDuplexPipe pipe) : IConnectionFactory
    {
        public ValueTask<IDuplexPipe> ConnectAsync(ServerEndpoint endpoint, CancellationToken ct)
            => ValueTask.FromResult(pipe);
    }
}
