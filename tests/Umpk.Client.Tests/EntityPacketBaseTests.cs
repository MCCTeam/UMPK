using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class EntityPacketBaseTests
{
    public static IEnumerable<object[]> Protocols => JavaVersions.All.Select(row => row.Version.Protocol).Distinct().Order().Select(protocol => new object[] { protocol });
    public static IEnumerable<object[]> RelativeCases => Protocols.SelectMany(row => new[] { new object[] { row[0], false }, new object[] { row[0], true } });
    public static IEnumerable<object[]> SyncProtocols => Protocols.Where(row => (int)row[0] >= 768);
    private static readonly Vec3d Spawn = new(10.25, 64.5, -2.75);
    private static readonly Vec3d Visual = new(100, 120, 130);

    [Theory]
    [MemberData(nameof(RelativeCases))]
    public async Task RelativeMovesUseThePacketBaseDespiteAnInterveningVisualPosition(int protocol, bool afterFirstMove)
    {
        (ApplierHarness harness, Entity entity) = await CreateAsync(protocol);
        if (afterFirstMove)
            await MoveAsync(harness, protocol);
        entity.Position = Visual;

        await MoveAsync(harness, protocol);

        Assert.Equal(afterFirstMove ? new Vec3d(10.75, 65.5, -3.25) : new Vec3d(10.5, 65, -3), entity.Position);
    }

    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task RotationOnlyKeepsTheNativePacketBaseAndEraSpecificPositionTarget(int protocol)
    {
        (ApplierHarness harness, Entity entity) = await CreateAsync(protocol);
        entity.Position = Visual;
        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "move_entity_rot", new ClientboundMoveEntityRotPacket(42, 90, 45, true)));

        Assert.Equal(protocol < 573 ? Spawn : Visual, entity.Position);
        Assert.Equal(90, entity.Yaw);
        await MoveAsync(harness, protocol);
        Assert.Equal(new Vec3d(10.5, 65, -3), entity.Position);
    }

    [Theory]
    [MemberData(nameof(Protocols))]
    public async Task TeleportChangesThePacketBaseOnlyBeforeTheModernRelativeTeleportEra(int protocol)
    {
        (ApplierHarness harness, Entity entity) = await CreateAsync(protocol);
        var destination = new Vec3d(20.25, 74.5, -12.75);
        var packet = new ClientboundTeleportEntityPacket(42, destination.X, destination.Y, destination.Z, 90, 45, true,
            protocol >= 768 ? new PositionMoveRotation(destination, Vec3d.Zero, 90, 45) : null, 0);
        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "teleport_entity", packet));
        entity.Position = Visual;

        await MoveAsync(harness, protocol);

        Assert.Equal(protocol < 768 ? new Vec3d(20.5, 75, -13) : new Vec3d(10.5, 65, -3), entity.Position);
    }

    [Theory]
    [MemberData(nameof(SyncProtocols))]
    public async Task PositionSyncReplacesThePacketBaseDespiteLaterVisualMovement(int protocol)
    {
        (ApplierHarness harness, Entity entity) = await CreateAsync(protocol);
        var destination = new Vec3d(30.25, 84.5, -22.75);
        var packet = new ClientboundEntityPositionSyncPacket(42, new PositionMoveRotation(destination, Vec3d.Zero, 0, 0), true)
        {
            Path = protocol == 777 ? new EntityPositionPath.Linear(destination) : null,
        };
        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "entity_position_sync", packet));
        entity.Position = Visual;

        await MoveAsync(harness, protocol);

        Assert.Equal(new Vec3d(30.5, 85, -23), entity.Position);
    }

    private static async Task<(ApplierHarness Harness, Entity Entity)> CreateAsync(int protocol)
    {
        var harness = new ApplierHarness(BoundPacketApplierHarness.Version(protocol), new ClientFeatures { Terrain = false, Entities = true, Physics = false, Pathfinding = false });
        harness.State.Registries = JavaGameData.Registries(protocol);
        Assert.True(harness.State.Registries.EntityTypes.TryGet(Identifier.Minecraft("armor_stand"), out RegistryEntry<EntityTypeDefinition> stand));
        var spawn = new ClientboundAddEntityPacket(42, Guid.NewGuid(), protocol < 477 ? 78 : stand.NetworkId, Spawn.X, Spawn.Y, Spawn.Z, 0, 0, 0, 0, 0, 0, 0, null);
        await harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "add_entity", spawn));
        return (harness, harness.State.Entities.Get(42)!);
    }

    private static ValueTask MoveAsync(ApplierHarness harness, int protocol)
    {
        int units = protocol == 47 ? 32 : 4096;
        return harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "move_entity_pos", new ClientboundMoveEntityPosPacket(42, (short)(units / 4), (short)(units / 2), (short)(-units / 4), true)));
    }
}
