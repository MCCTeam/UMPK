using Umpk.Client.Events;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class EntityWorldLifetimeTests
{
    public static IEnumerable<object[]> Protocols => JavaVersions.All.Select(row => row.Version.Protocol).Distinct().Order().Select(protocol => new object[] { protocol });
    public static IEnumerable<object[]> WorldModes => Protocols.SelectMany(row => new[] { new object[] { row[0], false }, new object[] { row[0], true } });

    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task DuplicateSpawnReplacesTheTrackedInstanceAndDetachesItsOldEdges(int protocol)
    {
        var harness = Create(protocol, terrain: true);
        RegistryAccess registries = harness.State.Registries!;
        Assert.True(registries.EntityTypes.TryGet(Identifier.Minecraft("armor_stand"), out RegistryEntry<EntityTypeDefinition> stand));
        Assert.True(registries.EntityTypes.TryGet(Identifier.Minecraft("item_frame"), out RegistryEntry<EntityTypeDefinition> frame));
        var first = new ClientboundAddEntityPacket(42, Guid.NewGuid(), protocol < 477 ? 78 : stand.NetworkId, 1.5, 64, 1.5, 0, 0, 0, 0, 0, 0, 0, null);
        var next = first with { Uuid = Guid.NewGuid(), TypeId = protocol < 477 ? 71 : frame.NetworkId, X = 8.5, Y = 67, Z = -3.5 };
        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "add_entity", first));
        Entity old = harness.State.Entities.Get(42)!;
        var parent = new Entity(43, Guid.NewGuid(), stand);
        var rider = new Entity(44, Guid.NewGuid(), stand);
        harness.State.Entities.Add(parent);
        harness.State.Entities.Add(rider);
        harness.State.Entities.SetPassengers(parent, [old]);
        harness.State.Entities.SetPassengers(old, [rider]);
        List<string> events = [];
        Entity? published = null;
        using IDisposable removed = harness.Events.Subscribe<EntityRemoved>(evt =>
        {
            Assert.Equal(42, evt.EntityId);
            Assert.Null(harness.State.Entities.Get(42));
            Assert.Null(old.Vehicle);
            Assert.Null(rider.Vehicle);
            Assert.Empty(parent.Passengers);
            Assert.Empty(old.Passengers);
            events.Add("removed");
        });
        using IDisposable spawned = harness.Events.Subscribe<EntitySpawned>(evt =>
        {
            published = evt.Entity;
            Assert.Same(evt.Entity, harness.State.Entities.Get(evt.EntityId));
            events.Add("spawned");
        });

        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "add_entity", next));

        Entity current = harness.State.Entities.Get(42)!;
        Assert.NotSame(old, current);
        Assert.Same(current, published);
        Assert.Equal(Identifier.Minecraft("item_frame"), current.Type.Id);
        Assert.Equal(new Vec3d(8.5, 67, -3.5), current.Position);
        Assert.Null(old.Vehicle);
        Assert.Null(rider.Vehicle);
        Assert.Empty(parent.Passengers);
        Assert.Empty(old.Passengers);
        Assert.Equal(new[] { "removed", "spawned" }, events);
        if (protocol != 47)
        {
            Assert.False(harness.State.Entities.TryGetByUuid(old.Uuid, out _));
            Assert.True(harness.State.Entities.TryGetByUuid(current.Uuid, out Entity? byUuid));
            Assert.Same(current, byUuid);
        }
    }

    [Theory]
    [MemberData(nameof(WorldModes))]
    public async Task SameDimensionRespawnRetainsTrackedEntitiesAndLoadedTerrain(int protocol, bool terrain)
    {
        var harness = Create(protocol, terrain);
        await harness.ApplyAsync(Join(harness, protocol, "minecraft:overworld"));
        var previous = harness.State.WorldOrNull;
        long playerLifetime = harness.State.PlayerLifetime;
        Entity entity = AddCow(harness);
        var target = new BlockPos(2, 64, 2);
        if (terrain)
            previous!.SetBlockStateId(target, Stone(harness));

        await harness.ApplyAsync(Respawn(harness, protocol, "minecraft:overworld"));

        Assert.Same(entity, harness.State.Entities.Get(entity.Id));
        Assert.Equal(playerLifetime + 1, harness.State.PlayerLifetime);
        Assert.Same(previous, harness.State.WorldOrNull);
        Assert.Equal(GameMode.Creative, harness.State.Self.GameMode);
        if (terrain)
            Assert.Equal(Stone(harness), harness.State.World.GetBlockStateId(target));
    }

    [Theory]
    [MemberData(nameof(WorldModes))]
    public async Task ChangedDimensionDropsPreviousEntitiesAndTheirRiderEdges(int protocol, bool terrain)
    {
        var harness = Create(protocol, terrain);
        await harness.ApplyAsync(Join(harness, protocol, "minecraft:overworld"));
        var previous = harness.State.WorldOrNull;
        long playerLifetime = harness.State.PlayerLifetime;
        Entity old = AddCow(harness);
        var rider = new Entity(43, Guid.NewGuid(), old.Type);
        harness.State.Entities.Add(rider);
        harness.State.Entities.SetPassengers(old, [rider]);

        await harness.ApplyAsync(Respawn(harness, protocol, "minecraft:the_nether"));

        Assert.Empty(harness.State.Entities.All);
        Assert.Equal(playerLifetime + 1, harness.State.PlayerLifetime);
        Assert.Null(old.Vehicle);
        Assert.Null(rider.Vehicle);
        Assert.Empty(old.Passengers);
        if (terrain)
        {
            Assert.NotSame(previous, harness.State.World);
            Assert.Equal(Identifier.Minecraft("the_nether"), harness.State.World.Dimension.DimensionName);
        }
        else
            Assert.False(harness.State.HasWorld);
    }

    [Theory]
    [MemberData(nameof(WorldModes))]
    public async Task FreshJoinAlwaysReplacesTheWorldEvenWhenTheDimensionNameMatches(int protocol, bool terrain)
    {
        var harness = Create(protocol, terrain);
        await harness.ApplyAsync(Join(harness, protocol, "minecraft:overworld"));
        var previous = harness.State.WorldOrNull;
        long playerLifetime = harness.State.PlayerLifetime;
        AddCow(harness);

        await harness.ApplyAsync(Join(harness, protocol, "minecraft:overworld"));

        Assert.Empty(harness.State.Entities.All);
        Assert.Equal(playerLifetime + 1, harness.State.PlayerLifetime);
        if (terrain)
            Assert.NotSame(previous, harness.State.World);
    }

    private static ApplierHarness Create(int protocol, bool terrain)
    {
        var harness = new ApplierHarness(BoundPacketApplierHarness.Version(protocol), new ClientFeatures { Terrain = terrain, Entities = true, Physics = false, Pathfinding = false });
        harness.State.Registries = JavaGameData.Registries(protocol);
        return harness;
    }

    private static Entity AddCow(ApplierHarness harness)
    {
        Assert.True(harness.State.Registries!.EntityTypes.TryGet(Identifier.Minecraft("cow"), out RegistryEntry<EntityTypeDefinition> cow));
        return harness.State.Entities.Add(new Entity(42, Guid.NewGuid(), cow));
    }

    private static int Stone(ApplierHarness harness)
    {
        Assert.True(harness.State.Registries!.Blocks.TryGetValue(Identifier.Minecraft("stone"), out BlockDefinition? stone));
        return stone.DefaultStateId;
    }

    private static CommonPlayerSpawnInfo Spawn(ApplierHarness harness, string dimension, int gameMode)
    {
        int id = dimension == "minecraft:the_nether" ? -1 : 0;
        if (harness.State.Registries!.DimensionTypes.TryGet(Identifier.Parse(dimension), out RegistryEntry<DimensionTypeDefinition> type))
            id = type.NetworkId;
        return new CommonPlayerSpawnInfo(id, dimension, 0, (sbyte)gameMode, -1, false, false, null, 0, 63) { DimensionTypeName = dimension };
    }

    private static NbtTag? Inline(int protocol, string dimension)
    {
        if (protocol is < 751 or > 758)
            return null;
        var value = new NbtCompound();
        value.PutInt("min_y", protocol >= 757 && dimension == "minecraft:overworld" ? -64 : 0);
        value.PutInt("height", protocol >= 757 && dimension == "minecraft:overworld" ? 384 : 256);
        value.PutBool("has_skylight", dimension == "minecraft:overworld");
        return value;
    }

    private static ClientboundLoginPacket Join(ApplierHarness harness, int protocol, string dimension)
        => new(1, false, [], 20, 8, 8, false, true, false, Spawn(harness, dimension, 0), true, false, Legacy: new LegacyLoginFields(0, 2, "flat")) { DimensionTypeNbt = Inline(protocol, dimension) };

    private static ClientboundRespawnPacket Respawn(ApplierHarness harness, int protocol, string dimension)
        => protocol < 477
            ? new(null, 0, new LegacyRespawnFields(dimension == "minecraft:the_nether" ? -1 : 0, 2, 1, "flat"))
            : new(Spawn(harness, dimension, 1), 0, null) { DimensionTypeNbt = Inline(protocol, dimension) };
}
