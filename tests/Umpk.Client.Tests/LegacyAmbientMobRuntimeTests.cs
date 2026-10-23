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

public sealed class LegacyAmbientMobRuntimeTests
{
    [Theory]
    [InlineData("bat", 65, .5F, .9F)]
    [InlineData("squid", 94, .8F, .8F)]
    public async Task SpawnOwnsNativeBoundsBeforeMetadataAndPreservesFloatVelocity(string family, int type, float width, float height)
    {
        ApplierHarness harness = Create();
        await LegacyPlacementTerrain.Receive(harness, 315);
        await Spawn(harness, type, new Vec3d(1.5, 65.125, 1.5), 777, -333, 16);
        Entity actor = harness.State.Entities.Get(42)!;

        Assert.Equal(Identifier.Minecraft(family), actor.Type.Id);
        Assert.Equal(new Vec3d(777 / 8000.0F, -333 / 8000.0F, 16 / 8000.0F), actor.Velocity);
        Assert.True(harness.State.EntityMembership.IsWorldQueryMember(actor));
        Assert.True(Known(harness, actor, out Aabb bounds));
        Assert.Equal(VanillaEntityBounds.Create(actor.Position, width, height).Box, bounds);
    }

    [Theory]
    [InlineData(65)]
    [InlineData(94)]
    public async Task NativeListInsertionWaitsForOwnColumnAndReentryFollowsTraversal(int type)
    {
        ApplierHarness harness = Create();
        await Spawn(harness, type, new Vec3d(1.5, 65.125, 1.5));
        Entity actor = harness.State.Entities.Get(42)!;
        Vec3d opening = actor.Position;
        Assert.False(harness.State.EntityMembership.IsWorldQueryMember(actor));
        await Move(harness, new Vec3d(3.5, 66.125, 1.5));
        Tick(harness);
        Assert.Equal(opening, actor.Position);

        await LegacyPlacementTerrain.Receive(harness, 315);
        Assert.False(harness.State.EntityMembership.IsWorldQueryMember(actor));
        harness.State.TickEntities(315, JavaGameData.BlockShapes(315), [], LocalBody);
        Assert.Equal(opening, actor.Position);
        harness.State.EntityMembership.FinishWorldPhase();
        Assert.True(harness.State.EntityMembership.IsWorldQueryMember(actor));
        Tick(harness);
        Assert.Equal(opening.X + 2.0 / 3, actor.Position.X);
    }

    [Fact]
    public async Task HangingTickRetainsCachedBoxUntilAnExplicitPositionSetter()
    {
        ApplierHarness harness = Create();
        await LegacyPlacementTerrain.Receive(harness, 315);
        await Spawn(harness, 65, new Vec3d(1.5, 65.125, 1.5));
        Entity actor = harness.State.Entities.Get(42)!;
        Assert.True(Known(harness, actor, out Aabb opening));

        Tick(harness);

        Assert.Equal(66.0 - .9F, actor.Position.Y);
        Assert.True(Known(harness, actor, out Aabb retained));
        Assert.Equal(opening, retained);
        Assert.NotEqual(actor.Position.Y, retained.MinY);
        harness.State.EntityGeometry.ObservePosition(actor);
        Assert.True(Known(harness, actor, out Aabb refreshed));
        Assert.Equal(actor.Position.Y, refreshed.MinY);
        Assert.Equal(VanillaEntityBounds.Create(actor.Position, .5F, .9F).Box, refreshed);
    }

    [Theory]
    [InlineData(65)]
    [InlineData(94)]
    public async Task ReplacementChunkClearsActualSectionWithoutStoppingNativeListTick(int type)
    {
        ApplierHarness harness = Create();
        await LegacyPlacementTerrain.Receive(harness, 315);
        await Spawn(harness, type, new Vec3d(1.5, 65.125, 1.5));
        Entity actor = harness.State.Entities.Get(42)!;
        await LegacyPlacementTerrain.Receive(harness, 315);
        Assert.False(harness.State.EntityMembership.IsWorldQueryMember(actor));
        Assert.True(harness.State.EntityMembership.CanTick(actor));
        await Move(harness, new Vec3d(3.5, 65.125, 1.5));

        Tick(harness);

        Assert.Equal(1.5 + 2.0 / 3, actor.Position.X);
        Assert.False(harness.State.EntityMembership.IsWorldQueryMember(actor));
    }

    [Theory]
    [InlineData(65, false)]
    [InlineData(94, false)]
    [InlineData(65, true)]
    [InlineData(94, true)]
    public async Task SourceBackedZeroHealthAndStatusThreeRemoveAtTwentiethEligibleTick(int type, bool status)
    {
        ApplierHarness harness = Create();
        await LegacyPlacementTerrain.Receive(harness, 315);
        await Spawn(harness, type, new Vec3d(1.5, 65.125, 1.5));
        Entity actor = harness.State.Entities.Get(42)!;
        if (status)
            await Apply(harness, new ClientboundEntityEventPacket(42, 3));
        else
            await Metadata(harness, 7, MetadataValue.Float(0));
        for (int tick = 0; tick < 19; tick++)
            Tick(harness);
        Assert.True(harness.State.EntityMembership.IsWorldQueryMember(actor));

        Tick(harness);

        Assert.False(harness.State.EntityMembership.CanTick(actor));
        Assert.False(harness.State.EntityMembership.IsWorldQueryMember(actor));
        Assert.Same(actor, harness.State.Entities.Get(42));
        await Spawn(harness, type, actor.Position);
        Entity replacement = harness.State.Entities.Get(42)!;
        Assert.NotSame(actor, replacement);
        Assert.True(harness.State.EntityMembership.CanTick(replacement));
        Assert.True(harness.State.EntityMembership.IsWorldQueryMember(replacement));
    }

    [Theory]
    [InlineData(65, false)]
    [InlineData(94, false)]
    [InlineData(65, true)]
    [InlineData(94, true)]
    public async Task SourceBackedNaNDoesNotAdvanceDeathButIsNotAliveForUnloadReentry(int type, bool nan)
    {
        ApplierHarness harness = Create();
        await LegacyPlacementTerrain.Receive(harness, 315);
        await Spawn(harness, type, new Vec3d(1.5, 65.125, 1.5));
        Entity actor = harness.State.Entities.Get(42)!;
        await Metadata(harness, 7, MetadataValue.Float(nan ? float.NaN : 0));
        if (nan)
        {
            for (int tick = 0; tick < 21; tick++)
                Tick(harness);
            Assert.True(harness.State.EntityMembership.IsWorldQueryMember(actor));
        }
        await Apply(harness, new ClientboundForgetLevelChunkPacket(new ChunkPos(0, 0)));
        Tick(harness);
        Assert.False(harness.State.EntityMembership.IsWorldQueryMember(actor));
        await LegacyPlacementTerrain.Receive(harness, 315);
        Tick(harness);
        Assert.False(harness.State.EntityMembership.CanTick(actor));
        Assert.False(harness.State.EntityMembership.IsWorldQueryMember(actor));
        Assert.Same(actor, harness.State.Entities.Get(42));
    }

    [Theory]
    [InlineData(65)]
    [InlineData(94)]
    public async Task MalformedHealthKindRetainsUnknownMembership(int type)
    {
        ApplierHarness harness = Create();
        await LegacyPlacementTerrain.Receive(harness, 315);
        await Spawn(harness, type, new Vec3d(1.5, 65.125, 1.5));
        Entity actor = harness.State.Entities.Get(42)!;
        await Metadata(harness, 7, MetadataValue.Byte(0));

        Assert.Null(harness.State.EntityMembership.IsWorldQueryMember(actor));
    }

    [Theory]
    [InlineData(210)]
    [InlineData(316)]
    public async Task UnreconciledAdjacentProfilesKeepAmbientGeometryUnknown(int protocol)
    {
        ApplierHarness harness = Create(protocol);
        await Spawn(harness, 65, new Vec3d(1.5, 65.125, 1.5), protocol: protocol);

        Assert.False(harness.State.EntityGeometry.TryGet(harness.State.Entities.Get(42)!, JavaGameData.EntityGeometryData(protocol), out _, out _, protocol));
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("malformed")]
    [InlineData("intersecting")]
    [InlineData("mounted")]
    public async Task UnresolvedLocalPushContextKeepsSquidGeometryUnknown(string context)
    {
        ApplierHarness harness = Create();
        await LegacyPlacementTerrain.Receive(harness, 315);
        await Spawn(harness, 94, new Vec3d(1.5, 65.125, 1.5));
        Entity actor = harness.State.Entities.Get(42)!;
        if (context == "mounted")
            harness.State.EntityMotion.ObserveLocalPassengers(99, 100, [99]);
        Aabb? localBody = context switch
        {
            "absent" => null,
            "malformed" => new Aabb(double.NaN, 0, 0, 1, 2, 1),
            "mounted" => LocalBody,
            _ => VanillaEntityBounds.Create(actor.Position, .6F, 1.8F).Box,
        };

        harness.State.TickEntities(315, JavaGameData.BlockShapes(315), [], localBody);

        Assert.False(Known(harness, actor, out _));
    }

    [Fact]
    public async Task UnmeasuredNonhangingCurrentAtTickEntryCannotBorrowDryTargetContext()
    {
        ApplierHarness harness = Create();
        await LegacyPlacementTerrain.Receive(harness, 315);
        Vec3d opening = new(1.5, 65.125, 1.5);
        await Spawn(harness, 65, opening);
        Entity actor = harness.State.Entities.Get(42)!;
        await Metadata(harness, 12, MetadataValue.Byte(0));
        await Apply(harness, new ClientboundBlockUpdatePacket(new BlockPos(1, 65, 1), 8 << 4));
        await Move(harness, new Vec3d(7.5, 65.125, 1.5));

        Tick(harness);

        Assert.False(Known(harness, actor, out _));
        Assert.Equal(opening, actor.Position);
    }

    [Theory]
    [InlineData(65)]
    [InlineData(94)]
    public async Task SourceBackedEmptyClientColumnRetainsAddedAndListWhileActualSectionIsAbsent(int type)
    {
        ApplierHarness harness = Create();
        await LegacyPlacementTerrain.Receive(harness, 315);
        await Spawn(harness, type, new Vec3d(15.5, 65.125, 1.5));
        Entity actor = harness.State.Entities.Get(42)!;
        Vec3d target = new(19.5, 65.125, 1.5);
        await Move(harness, target);
        AssertOwned(3, 0, 4);
        double x = 15.5;
        for (int remaining = 2; remaining >= 1; remaining--)
        {
            x += (target.X - x) / (remaining + 1);
            Tick(harness);
            Assert.Equal(new Vec3d(x, type == 65 ? 66.0 - .9F : 65.125, 1.5), actor.Position);
            AssertOwned(remaining, 0, null);
            Assert.False(harness.State.EntityMembership.IsWorldQueryMember(actor));
            Assert.True(harness.State.EntityMembership.CanTick(actor));
        }
        await LegacyPlacementTerrain.Receive(harness, 315, chunkX: 1);
        AssertOwned(1, 0, null);

        Tick(harness);

        Assert.Equal(new Vec3d(19.5, type == 65 ? 66.0 - .9F : 65.125, 1.5), actor.Position);
        AssertOwned(0, 1, 4);
        Assert.True(harness.State.EntityMembership.IsWorldQueryMember(actor));
        return;

        void AssertOwned(int steps, int registeredX, int? actualSection)
        {
            Assert.True(harness.State.EntityMotion.TryGetLegacyAmbientInterpolation(actor, out EntityMotionState.LegacyAmbientInterpolationSnapshot motion));
            Assert.True(motion.PositionKnown);
            Assert.Equal(target, motion.Target);
            Assert.Equal(steps, motion.RemainingSteps);
            Assert.Equal(target, harness.State.PacketBases.Get(actor));
            Assert.True(harness.State.EntityMembership.TryGetLegacyMembership(actor, out EntityMembershipState.LegacyMembershipSnapshot member));
            Assert.True(member.InEntityList);
            Assert.True(member.Added);
            Assert.False(member.Removed);
            Assert.Equal((registeredX, 4, 0), (member.X, member.Y, member.Z));
            Assert.Equal(actualSection, member.ActualSection);
        }
    }

    [Fact]
    public async Task SourceBackedUnknownTravelRetainsTheCompletedInterpolationPhaseButCannotPublishItsCompletePosition()
    {
        ApplierHarness harness = Create();
        await LegacyPlacementTerrain.Receive(harness, 315);
        Vec3d opening = new(1.5, 65.125, 1.5);
        await Spawn(harness, 94, opening);
        Entity actor = harness.State.Entities.Get(42)!;
        Assert.True(Known(harness, actor, out Aabb openingBounds));
        await Apply(harness, new ClientboundBlockUpdatePacket(new BlockPos(2, 65, 1), 165 << 4));
        Vec3d target = new(3.5, 65.125, 1.5);
        await Move(harness, target);
        Assert.True(harness.State.EntityMotion.TryGetLegacyAmbientInterpolation(actor, out EntityMotionState.LegacyAmbientInterpolationSnapshot before));
        Assert.Equal(3, before.RemainingSteps);
        Assert.True(harness.State.EntityMembership.TryGetLegacyMembership(actor, out EntityMembershipState.LegacyMembershipSnapshot beforeMembership));

        Tick(harness);

        Assert.False(Known(harness, actor, out Aabb retainedBounds));
        Assert.Equal(openingBounds, retainedBounds);
        Assert.Equal(opening, actor.Position);
        Assert.Equal(target, harness.State.PacketBases.Get(actor));
        Assert.True(harness.State.EntityMotion.TryGetLegacyAmbientInterpolation(actor, out EntityMotionState.LegacyAmbientInterpolationSnapshot partial));
        Assert.False(partial.PositionKnown);
        Assert.Equal(target, partial.Target);
        Assert.Equal(2, partial.RemainingSteps);
        Assert.True(harness.State.EntityMembership.TryGetLegacyMembership(actor, out EntityMembershipState.LegacyMembershipSnapshot partialMembership));
        Assert.Equal(beforeMembership with { RegistrationKnown = false }, partialMembership);
        Tick(harness);
        Assert.True(harness.State.EntityMotion.TryGetLegacyAmbientInterpolation(actor, out EntityMotionState.LegacyAmbientInterpolationSnapshot suspended));
        Assert.Equal(partial, suspended);
        Assert.False(suspended.PositionKnown);

        Vec3d laterTarget = new(7.5, 65.125, 1.5);
        await Move(harness, laterTarget);
        Assert.Equal(laterTarget, harness.State.PacketBases.Get(actor));
        Assert.Equal(opening, actor.Position);
        Assert.True(harness.State.EntityMotion.TryGetLegacyAmbientInterpolation(actor, out EntityMotionState.LegacyAmbientInterpolationSnapshot later));
        Assert.Equal(laterTarget, later.Target);
        Assert.Equal(3, later.RemainingSteps);
        Assert.False(later.PositionKnown);
        Assert.True(harness.State.EntityMembership.TryGetLegacyMembership(actor, out EntityMembershipState.LegacyMembershipSnapshot laterMembership));
        Assert.Equal(beforeMembership with { RegistrationKnown = false }, laterMembership);

        await Spawn(harness, 94, new Vec3d(10.5, 65.125, 10.5));
        Entity replacement = harness.State.Entities.Get(42)!;
        Assert.NotSame(actor, replacement);
        Assert.False(harness.State.EntityMotion.TryGetLegacyAmbientInterpolation(actor, out _));
        Assert.False(harness.State.EntityMembership.TryGetLegacyMembership(actor, out _));
        Assert.True(harness.State.EntityMotion.TryGetLegacyAmbientInterpolation(replacement, out EntityMotionState.LegacyAmbientInterpolationSnapshot recovered));
        Assert.True(recovered.PositionKnown);
        Assert.Equal(Vec3d.Zero, recovered.Target);
        Assert.Equal(0, recovered.RemainingSteps);
        Assert.True(Known(harness, replacement, out _));
        Assert.True(harness.State.EntityMembership.IsWorldQueryMember(replacement));
    }

    private static readonly Aabb LocalBody = new(-.3, 0, -.3, .3, 1.8, .3);

    private static ApplierHarness Create(int protocol = 315)
    {
        var harness = new ApplierHarness(BoundPacketApplierHarness.Version(protocol), new ClientFeatures { Terrain = true, Entities = true, Physics = false, Pathfinding = false });
        harness.State.Registries = JavaGameData.Registries(protocol);
        WorldFactory.InstallWorld(harness.State, new CommonWorldSetup("minecraft:overworld", 0), protocol);
        return harness;
    }

    private static Task Spawn(ApplierHarness harness, int type, Vec3d position, short x = 0, short y = 0, short z = 0, int protocol = 315)
        => harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(protocol, "add_mob", new ClientboundAddMobPacket(42, type, position.X, position.Y, position.Z, 0, 0, 0, x, y, z, new EntityMetadataList([], []), Guid.Empty))).AsTask();

    private static Task Move(ApplierHarness harness, Vec3d target)
        => Apply(harness, new ClientboundTeleportEntityPacket(42, target.X, target.Y, target.Z, 0, 0, false, null, 0));

    private static Task Metadata(ApplierHarness harness, int index, MetadataValue value)
        => Apply(harness, new ClientboundSetEntityDataPacket(42, new EntityMetadataList([new(index, value)], [])));

    private static Task Apply(ApplierHarness harness, IPacket packet)
        => harness.ApplyAsync(BoundDescriptorCodec.RoundTrip(315, packet.Type.Id.Path, packet)).AsTask();

    private static bool Known(ApplierHarness harness, Entity actor, out Aabb bounds)
        => harness.State.EntityGeometry.TryGet(actor, JavaGameData.EntityGeometryData(315), out bounds, out _, 315);

    private static void Tick(ApplierHarness harness)
    {
        harness.State.TickEntities(315, JavaGameData.BlockShapes(315), [], LocalBody);
        harness.State.EntityMembership.FinishWorldPhase();
    }
}
