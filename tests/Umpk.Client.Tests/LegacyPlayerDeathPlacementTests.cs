using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Items;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Physics;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class LegacyPlayerDeathPlacementTests
{
    [Fact]
    public async Task APartialMetadataKeySourceCannotProveDefaultHealth()
    {
        var harness = new ApplierHarness(BoundPacketApplierHarness.Version(404));
        harness.State.Registries = JavaGameData.Registries(404);
        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(404, "add_player", new ClientboundAddPlayerPacket(42, Guid.Empty, 1.5, 64, 1.5, 0, 0, 0, new EntityMetadataList([], []))));
        var actor = new Entity(42, Guid.Empty, harness.State.Entities.Get(42)!.Type, new MissingHealthKeys()) { Position = new Vec3d(1.5, 64, 1.5) };
        var membership = new EntityMembershipState();
        membership.LoadColumn(new ChunkPos(0, 0));
        membership.Register(actor, 404, isPlayer: true);

        Assert.Null(membership.IsWorldQueryMember(actor));
    }

    public static IEnumerable<object[]> Cases()
    {
        foreach (object[] row in LegacyPlayerMembershipRuntimeTests.Protocols())
            foreach (bool malformed in new[] { false, true })
                yield return [row[1], row[2], malformed];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task PlacementUsesWorldLifetimeWithoutRemovingPacketLookup(int protocol, int caseIndex, bool malformed)
    {
        Assert.True(JavaVersions.TryGetByProtocol(protocol, out var version));
        var harness = new ApplierHarness(version, new ClientFeatures { Terrain = true, Entities = true, Inventory = true, Physics = false, Pathfinding = false });
        harness.State.Registries = JavaGameData.Registries(protocol);
        WorldFactory.InstallWorld(harness.State, new CommonWorldSetup("minecraft:overworld", 0), protocol);
        using System.Text.Json.JsonDocument document = LegacyPlayerMembershipRuntimeTests.ReadNative();
        System.Text.Json.JsonElement native = document.RootElement.GetProperty("cases")[caseIndex];
        var chunk = native.GetProperty("rows").EnumerateArray().Single(row => row.GetProperty("position").GetInt32() == 0 && row.GetProperty("scene").GetInt32() == 1).GetProperty("packets")[0];
        await harness.ApplyAsync(LegacyPlayerMembershipRuntimeTests.DecodeNative(harness, protocol, native, chunk));
        await Spawn();
        Entity actor = harness.State.Entities.Get(42)!;
        Assert.True(JavaGameData.EntityMetadataKeys(protocol).TryResolveIndex(actor.Type, EntityMetadataKeys.Health, out int healthIndex));
        await Metadata(malformed ? MetadataValue.Byte(0) : MetadataValue.Float(0));
        Assert.True(harness.State.Registries.Blocks.TryGetValue(Identifier.Minecraft("stone"), out var stone));
        var target = new BlockPos(1, 64, 1);
        harness.State.World.SetBlockStateId(target, stone.DefaultStateId);
        Assert.True(harness.State.Registries.Items.TryGet(Identifier.Minecraft("stone"), out var item));
        harness.State.Inventory.SetSlot(0, 36, new ItemStack(item, 32));
        await using var scheduler = new ChannelSessionScheduler();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var sink = new RecordingSink();
        var services = new ClientSessionServices { Version = version, Options = new(), Policies = new(), State = harness.State, Wire = new(version), Logger = NullLogger.Instance, Scheduler = scheduler, ReadPhysicsState = () => new PhysicsState { BoundingBox = Aabb.OfSize(.5, 64, -1.5, .6, 1.8) }, CurrentSessionCancellation = () => lifetime.Token };
        var actions = new InteractionActions(sink, services, new SequenceTracker());
        for (int tick = 0; tick < 19; tick++)
            Tick();
        await Place();
        int initialPackets = malformed || protocol >= 393 ? 1 : 0;
        Assert.Equal(initialPackets, sink.Packets.Count);

        Tick();
        if (malformed)
        {
            Assert.Null(harness.State.EntityMembership.IsWorldQueryMember(actor));
            await Metadata(MetadataValue.Float(20));
            Tick();
            Assert.Null(harness.State.EntityMembership.IsWorldQueryMember(actor));
        }
        else
        {
            Assert.False(harness.State.EntityMembership.CanTick(actor));
            Assert.False(harness.State.EntityMembership.IsWorldQueryMember(actor));
        }
        await Place();
        Assert.Equal(initialPackets + (malformed ? 1 : 2), sink.Packets.Count);
        if (!malformed)
            Assert.IsType<ServerboundSwingPacket>(sink.Packets[initialPackets + 1]);
        Assert.Same(actor, harness.State.Entities.Get(42));

        await Spawn();
        Entity replacement = harness.State.Entities.Get(42)!;
        Assert.NotSame(actor, replacement);
        Assert.True(harness.State.EntityMembership.CanTick(replacement));
        Assert.True(harness.State.EntityMembership.IsWorldQueryMember(replacement));
        int previous = sink.Packets.Count;
        await Place();
        Assert.Equal(previous + (protocol < 393 ? 0 : 1), sink.Packets.Count);
        foreach (object packet in sink.Packets)
        {
            byte[] body = BoundDescriptorCodec.EncodeServerbound(protocol, Assert.IsAssignableFrom<Umpk.Protocol.Java.IPacket>(packet));
            if (protocol == 47 && packet is ServerboundSwingPacket)
                Assert.Empty(body);
            else
                Assert.NotEmpty(body);
        }
        Assert.Equal(32, harness.State.Inventory.GetSlot(36).Count);
        Assert.True(harness.State.World.GetBlock(target.Above()).IsAir);
        return;

        Task Spawn() => harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "add_player", new ClientboundAddPlayerPacket(42, Guid.Empty, 1.5, 65, 1.5, 0, 0, 0, new EntityMetadataList([], [])))).AsTask();
        Task Metadata(MetadataValue value) => harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "set_entity_data", new ClientboundSetEntityDataPacket(42, new EntityMetadataList([new(healthIndex, value)], [])))).AsTask();
        void Tick()
        {
            harness.State.TickEntities(protocol, JavaGameData.BlockShapes(protocol), []);
            harness.State.EntityMembership.FinishWorldPhase();
        }
        Task Place() => actions.PlaceBlockAsync(target, Direction.Up, new Vec3d(.5, 1, .5), ct: lifetime.Token);
    }

    private sealed class MissingHealthKeys : IMetadataKeySource
    {
        public bool TryResolveIndex(RegistryEntry<EntityTypeDefinition> entityType, MetadataKey key, out int index)
        {
            index = 0;
            return false;
        }
    }
}
