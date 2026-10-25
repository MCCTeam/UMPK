using System.Buffers;
using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class LegacyBatFluidConfidenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownCurrentKeepsBodyAndPendingTargetKnownWithoutBorrowingRetainedVelocity(bool pending)
    {
        ApplierHarness harness = await Prepare(pending);
        Entity actor = harness.State.Entities.Get(42)!;
        Vec3d observedVelocity = actor.Velocity;
        await UnknownCell(harness);

        Tick(harness);

        AssertBody(harness, actor, pending ? 3.5 : 1.5, pending ? 2 : 0, velocityKnown: false);
        Assert.Equal(observedVelocity, actor.Velocity);
        await Apply(harness, new ClientboundBlockUpdatePacket(new BlockPos(1, 65, 1), 0));
        Tick(harness);
        AssertBody(harness, actor, pending ? 5.5 : 1.5, pending ? 1 : 0, velocityKnown: false);
        Assert.Equal(observedVelocity, actor.Velocity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ARealVelocityPacketRecoversVelocityWithoutReplacingThePendingBodyTarget(bool pending)
    {
        ApplierHarness harness = await Prepare(pending);
        Entity actor = harness.State.Entities.Get(42)!;
        await UnknownCell(harness);
        Tick(harness);
        await Apply(harness, new ClientboundBlockUpdatePacket(new BlockPos(1, 65, 1), 0));
        await Apply(harness, new ClientboundSetEntityMotionPacket(42, 2000, -1000, 16));
        AssertBody(harness, actor, pending ? 3.5 : 1.5, pending ? 2 : 0, velocityKnown: true);
        Vec3d packetVelocity = new(2000 / 8000.0, -1000 / 8000.0, 16 / 8000.0);
        Assert.Equal(packetVelocity, actor.Velocity);

        Tick(harness);

        AssertBody(harness, actor, pending ? 5.5 : 1.5, pending ? 1 : 0, velocityKnown: true);
        double x = pending ? packetVelocity.X : packetVelocity.X * .98;
        double y = pending ? packetVelocity.Y : packetVelocity.Y * .98;
        Assert.Equal(new Vec3d(x, y * .6F, 0), actor.Velocity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeHangingZeroRecoversVelocityWhileKeepingTheSeparatelyCachedBox(bool pending)
    {
        ApplierHarness harness = await Prepare(pending);
        Entity actor = harness.State.Entities.Get(42)!;
        await UnknownCell(harness);
        Tick(harness);
        await Apply(harness, Metadata(12, MetadataValue.Byte(1)));

        Tick(harness);

        AssertBody(harness, actor, pending ? 5.5 : 1.5, pending ? 1 : 0, velocityKnown: true, hanging: true);
        Assert.Equal(Vec3d.Zero, actor.Velocity);
        Assert.True(Known(harness, actor, out Aabb cached));
        Assert.Equal(65.125, cached.MinY);
        Assert.NotEqual(actor.Position.Y, cached.MinY);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NeitherVelocityPacketsNorHangingCanRepairAnAlreadyUnknownBody(bool hanging)
    {
        ApplierHarness harness = await Prepare(pending: true);
        Entity actor = harness.State.Entities.Get(42)!;
        Vec3d opening = actor.Position;
        Vec3d observedBeforeAuthorityLoss = actor.Velocity;
        await Spawn(harness, 99, 94, new Vec3d(12.5, 65.125, 12.5));
        await Apply(harness, new ClientboundSetPassengersPacket(99, [42]));
        Tick(harness);
        Assert.False(Known(harness, actor, out _));
        await Apply(harness, new ClientboundSetPassengersPacket(99, []));
        await Apply(harness, hanging ? Metadata(12, MetadataValue.Byte(1)) : new ClientboundSetEntityMotionPacket(42, 2000, 0, 0));

        Tick(harness);

        Assert.False(Known(harness, actor, out _));
        Assert.Equal(opening, actor.Position);
        Assert.Null(harness.State.EntityMembership.IsWorldQueryMember(actor));
        Assert.True(harness.State.EntityMotion.TryGetLegacyAmbientInterpolation(actor, out var motion));
        Assert.False(motion.PositionKnown);
        Assert.Equal(!hanging, motion.VelocityKnown);
        Assert.Equal(hanging ? observedBeforeAuthorityLoss : new Vec3d(.25, 0, 0), actor.Velocity);
        Assert.Equal(3, motion.RemainingSteps);
    }

    [Theory]
    [InlineData("vehicle")]
    [InlineData("passenger")]
    [InlineData("local relation")]
    public async Task UnsupportedAuthorityCannotUseTheRemoteUnmountedBodyProof(string context)
    {
        ApplierHarness harness = await Prepare(pending: true);
        Entity actor = harness.State.Entities.Get(42)!;
        await Spawn(harness, 99, 94, new Vec3d(12.5, 65.125, 12.5));
        await Apply(harness, context switch
        {
            "vehicle" => new ClientboundSetPassengersPacket(99, [42]),
            "passenger" => new ClientboundSetPassengersPacket(42, [99]),
            _ => new ClientboundSetPassengersPacket(99, [harness.State.Self.EntityId]),
        });

        Tick(harness);

        Assert.False(Known(harness, actor, out _));
        Assert.Null(harness.State.EntityMembership.IsWorldQueryMember(actor));
        Assert.True(harness.State.EntityMotion.TryGetLegacyAmbientInterpolation(actor, out var motion));
        Assert.False(motion.PositionKnown);
        Assert.False(motion.VelocityKnown);
    }

    [Fact]
    public async Task MalformedBatFlagsKeepBodyAndVelocityUnknown()
    {
        ApplierHarness harness = await Prepare(pending: true);
        Entity actor = harness.State.Entities.Get(42)!;
        await Apply(harness, Metadata(12, MetadataValue.VarInt(0)));

        Tick(harness);

        Assert.False(Known(harness, actor, out _));
        Assert.Null(harness.State.EntityMembership.IsWorldQueryMember(actor));
        Assert.True(harness.State.EntityMotion.TryGetLegacyAmbientInterpolation(actor, out var motion));
        Assert.False(motion.PositionKnown);
        Assert.False(motion.VelocityKnown);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemovalAndReplacementDiscardUnknownVelocityAndTheOldPendingTarget(bool removeFirst)
    {
        ApplierHarness harness = await Prepare(pending: true);
        Entity old = harness.State.Entities.Get(42)!;
        await UnknownCell(harness);
        Tick(harness);
        if (removeFirst)
            await Apply(harness, new ClientboundRemoveEntitiesPacket([42]));
        await Apply(harness, new ClientboundBlockUpdatePacket(new BlockPos(1, 65, 1), 0));
        await Spawn(harness, 42, 65, new Vec3d(10.5, 65.125, 10.5));
        Entity replacement = harness.State.Entities.Get(42)!;

        Assert.NotSame(old, replacement);
        Assert.False(harness.State.EntityMotion.TryGetLegacyAmbientInterpolation(old, out _));
        Assert.False(harness.State.EntityMembership.TryGetLegacyMembership(old, out _));
        Assert.True(harness.State.EntityMotion.TryGetLegacyAmbientInterpolation(replacement, out var motion));
        Assert.True(motion.PositionKnown);
        Assert.True(motion.VelocityKnown);
        Assert.Equal(Vec3d.Zero, motion.Target);
        Assert.Equal(0, motion.RemainingSteps);
        Assert.Equal(Vec3d.Zero, replacement.Velocity);
        Assert.True(Known(harness, replacement, out _));
        Assert.True(harness.State.EntityMembership.IsWorldQueryMember(replacement));
    }

    [Fact]
    public async Task AColumnSynthesizedByABlockUpdateCannotSupplyNativeFluidContext()
    {
        ApplierHarness harness = Create();
        await LegacyPlacementTerrain.Receive(harness, 315);
        await Spawn(harness, 42, 65, new Vec3d(15.875, 65.125, 1.5));
        await Apply(harness, Metadata(12, MetadataValue.Byte(0)));
        await Apply(harness, new ClientboundSetEntityMotionPacket(42, 1000, 1000, 0));
        var missingCell = new BlockPos(16, 65, 1);
        await Apply(harness, new ClientboundBlockUpdatePacket(missingCell, (9 << 4) | 7));
        Assert.NotNull(harness.State.World.GetColumn(missingCell));
        Assert.False(harness.State.EntityMembership.HasColumn(missingCell));
        Entity actor = harness.State.Entities.Get(42)!;
        Vec3d position = actor.Position;

        Tick(harness);

        Assert.Equal(position, actor.Position);
        Assert.Equal(new Vec3d(.125 * .98, .125 * .98 * .6F, 0), actor.Velocity);
        Assert.True(Known(harness, actor, out _));
        Assert.True(harness.State.EntityMotion.TryGetLegacyAmbientInterpolation(actor, out var motion));
        Assert.True(motion.PositionKnown);
        Assert.True(motion.VelocityKnown);
    }

    [Theory]
    [InlineData(false, 151)]
    [InlineData(true, 151)]
    [InlineData(false, 180)]
    [InlineData(true, 180)]
    public async Task AFluidTickKeepsBatObstructionAndNativeUseSwingDecisionsObservable(bool near, int state)
    {
        await using BlockUseSession session = await BlockUseSession.Create(315, "stone", physics: true);
        await session.Hold("stone");
        Vec3d position = near ? new Vec3d(1.5, 66.125, 1.5) : new Vec3d(10.5, 66.125, 10.5);
        var metadata = new EntityMetadataList([new(12, MetadataValue.Byte(0))], []);
        await session.Send(new ClientboundAddMobPacket(42, 65, position.X, position.Y, position.Z, 0, 0, 0, 1000, 1000, 0, metadata, Guid.Empty));
        await session.Send(new ClientboundBlockUpdatePacket(BlockPos.Containing(position), state));
        Assert.Empty(await session.Barrier());

        await session.Tick();
        Assert.Empty(await session.Barrier());

        await session.Client.InvokeAsync(client =>
        {
            Entity actor = Assert.Single(client.State.Entities.All);
            Assert.Equal(position, actor.Position);
            Assert.True(client.State.EntityMembership.IsWorldQueryMember(actor));
            Assert.True(client.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(315), out _, out _, 315));
            Assert.True(client.State.EntityMotion.TryGetLegacyAmbientInterpolation(actor, out var motion));
            Assert.True(motion.PositionKnown);
            Assert.True(motion.VelocityKnown);
            return true;
        }, session.Token);
        List<IPacket> sent = await session.Use(Hand.Main);
        if (near)
            Assert.Empty(sent);
        else
            Assert.Collection(sent, packet => Assert.IsType<ServerboundUseItemOnPacket>(packet), packet => Assert.IsType<ServerboundSwingPacket>(packet));
    }

    private static readonly Aabb LocalBody = new(-.3, 0, -.3, .3, 1.8, .3);

    private static ApplierHarness Create()
    {
        var harness = new ApplierHarness(BoundPacketApplierHarness.Version(315), new ClientFeatures { Terrain = true, Entities = true, Physics = false, Pathfinding = false });
        harness.State.Registries = JavaGameData.Registries(315);
        WorldFactory.InstallWorld(harness.State, new CommonWorldSetup("minecraft:overworld", 0), 315);
        return harness;
    }

    private static async Task<ApplierHarness> Prepare(bool pending)
    {
        ApplierHarness harness = Create();
        await LegacyPlacementTerrain.Receive(harness, 315);
        await Spawn(harness, 42, 65, new Vec3d(1.5, 65.125, 1.5));
        await Apply(harness, Metadata(12, MetadataValue.Byte(0)));
        await Apply(harness, new ClientboundSetEntityMotionPacket(42, 1000, 1000, 0));
        if (pending)
            await Apply(harness, new ClientboundTeleportEntityPacket(42, 7.5, 65.125, 1.5, 0, 0, false, null, 0));
        return harness;
    }

    private static async Task UnknownCell(ApplierHarness harness)
    {
        var cell = new BlockPos(1, 65, 1);
        await Apply(harness, new ClientboundBlockUpdatePacket(cell, 65535));
        Assert.True(harness.State.EntityMembership.HasColumn(cell));
        Assert.NotNull(harness.State.World.GetColumn(cell));
        Assert.False(harness.State.World.GetBlock(cell).IsValid);
    }

    private static ClientboundSetEntityDataPacket Metadata(int index, MetadataValue value)
        => new(42, new EntityMetadataList([new(index, value)], []));

    private static Task Spawn(ApplierHarness harness, int id, int type, Vec3d position)
        => Apply(harness, new ClientboundAddMobPacket(id, type, position.X, position.Y, position.Z, 0, 0, 0, 0, 0, 0, new EntityMetadataList([], []), Guid.Empty));

    private static async Task Apply(ApplierHarness harness, IPacket packet)
    {
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(315, packet.Type.Id.Path);
        var context = new PacketCodecContext(harness.State.Registries!, IConnectionCodecState.Empty);
        var output = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(output);
        codec.Encode(ref writer, packet, context);
        byte[] body = output.WrittenSpan.ToArray();
        object decoded = codec.Decode(body, context);
        switch (packet)
        {
            case ClientboundAddMobPacket mob:
                var add = Assert.IsType<ClientboundAddMobPacket>(decoded);
                Assert.Equal((mob.EntityId, mob.TypeId, mob.X, mob.Y, mob.Z, mob.Uuid), (add.EntityId, add.TypeId, add.X, add.Y, add.Z, add.Uuid));
                AssertMetadata(mob.Metadata, add.Metadata);
                break;
            case ClientboundSetEntityDataPacket data:
                var metadata = Assert.IsType<ClientboundSetEntityDataPacket>(decoded);
                Assert.Equal(data.EntityId, metadata.EntityId);
                AssertMetadata(data.Metadata, metadata.Metadata);
                break;
            case ClientboundSetPassengersPacket passengers:
                var mount = Assert.IsType<ClientboundSetPassengersPacket>(decoded);
                Assert.Equal(passengers.VehicleId, mount.VehicleId);
                Assert.Equal(passengers.Passengers, mount.Passengers);
                break;
            case ClientboundRemoveEntitiesPacket removal:
                Assert.Equal(removal.EntityIds, Assert.IsType<ClientboundRemoveEntitiesPacket>(decoded).EntityIds);
                break;
            default:
                Assert.Equal(packet, decoded);
                break;
        }
        var readback = new ArrayBufferWriter<byte>();
        var readbackWriter = new PacketWriter(readback);
        codec.Encode(ref readbackWriter, decoded, context);
        Assert.Equal(body, readback.WrittenSpan.ToArray());
        await harness.ApplyAsync(decoded);
    }

    private static void AssertMetadata(EntityMetadataList expected, EntityMetadataList actual)
    {
        Assert.Empty(actual.RawTail);
        Assert.Equal(expected.Entries.Count, actual.Entries.Count);
        for (int index = 0; index < expected.Entries.Count; index++)
        {
            Assert.Equal(expected.Entries[index].Index, actual.Entries[index].Index);
            Assert.Equal(expected.Entries[index].Value, actual.Entries[index].Value);
        }
    }

    private static bool Known(ApplierHarness harness, Entity actor, out Aabb box)
        => harness.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(315), out box, out _, 315);

    private static void AssertBody(ApplierHarness harness, Entity actor, double x, int steps, bool velocityKnown, bool hanging = false)
    {
        Assert.Equal(new Vec3d(x, hanging ? 66.0 - .9F : 65.125, 1.5), actor.Position);
        Assert.True(Known(harness, actor, out _));
        Assert.True(harness.State.EntityMembership.IsWorldQueryMember(actor));
        Assert.True(harness.State.EntityMotion.TryGetLegacyAmbientInterpolation(actor, out var motion));
        Assert.True(motion.PositionKnown);
        Assert.Equal(velocityKnown, motion.VelocityKnown);
        Assert.Equal(steps, motion.RemainingSteps);
        Assert.Equal(steps == 0 ? Vec3d.Zero : new Vec3d(7.5, 65.125, 1.5), motion.Target);
        Assert.Equal(steps == 0 ? new Vec3d(1.5, 65.125, 1.5) : new Vec3d(7.5, 65.125, 1.5), harness.State.PacketBases.Get(actor));
    }

    private static void Tick(ApplierHarness harness)
    {
        harness.State.TickEntities(315, JavaGameData.BlockShapes(315), [], LocalBody);
        harness.State.EntityMembership.FinishWorldPhase();
    }
}
