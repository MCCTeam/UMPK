using System.Buffers;
using Umpk.Client.Actions;
using Umpk.Client.Events;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Hosting;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class LegacyRemoteMountRuntimeTests
{
    private static readonly Vec3d RiderStart = new(1.5, 64, 1.5);
    private static readonly Vec3d VehicleStart = new(3.5, 64.5, 3.5);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FramedGraphEventsObserveCommittedAttachTransferAndDetach(bool riderFirst)
    {
        await using var session = await BlockUseSession.Create(47, "stone");
        (Entity rider, Entity first) = await Pair(session, riderFirst, false);
        await Spawn(session, 44, new Vec3d(5.5, 64, 5.5));
        await session.Send(new ClientboundAddMobPacket(45, 90, 7.5, 64, 7.5, 0, 0, 0, 0, 0, 0, new EntityMetadataList([], [])));
        Assert.Empty(await session.Barrier());
        Entity second = session.Client.State.Entities.Get(44)!;
        var events = new List<(int Vehicle, Entity? Parent, int[] First, int[] Second)>();
        using IDisposable subscription = session.Client.Events.Subscribe<EntityPassengersChanged>(message =>
            events.Add((message.VehicleId, rider.Vehicle, first.Passengers.Select(actor => actor.Id).ToArray(), second.Passengers.Select(actor => actor.Id).ToArray())));
        int observed = 0;

        await Link(session, 999, 43, false);
        await Link(session, 42, -1, false);
        await Link(session, 45, 43, true);
        Assert.Empty(events);
        Assert.Same(first, session.Client.State.Entities.Get(45)!.LeashHolder);
        await Link(session, 42, 43, false);
        Expect([43], first);
        await Link(session, 42, 43, false);
        await Link(session, 42, 44, true);
        await Link(session, 44, 43, false); // Occupied endpoint is rejected without changing the graph.
        Assert.Equal(observed, events.Count);
        await Link(session, 42, 44, false);
        Expect([43, 44], second);
        await Link(session, 42, 999, false); // Missing destination is the native nullable detach.
        Expect([44], null);
        await Link(session, 42, 999, false);
        await Link(session, 42, -1, false);
        Assert.Equal(observed, events.Count);
        await Link(session, 42, 43, false);
        Expect([43], first);
        await Link(session, 42, -1, false);
        Expect([43], null);
        await End(session);

        void Expect(int[] vehicleIds, Entity? parent)
        {
            Assert.Equal(observed + vehicleIds.Length, events.Count);
            foreach (int id in vehicleIds)
            {
                var snapshot = events[observed++];
                Assert.Equal(id, snapshot.Vehicle);
                Assert.Same(parent, snapshot.Parent);
                Assert.Equal(ReferenceEquals(parent, first) ? [42] : Array.Empty<int>(), snapshot.First);
                Assert.Equal(ReferenceEquals(parent, second) ? [42] : Array.Empty<int>(), snapshot.Second);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TickOwnedRemovedParentDetachPublishesTheCommittedGraph(bool riderFirst)
    {
        await using var session = await BlockUseSession.Create(47, "stone");
        (Entity rider, Entity vehicle) = await Pair(session, riderFirst, false);
        await Link(session, 42, 43, false);
        var events = new List<(int Vehicle, Entity? Parent, int Passengers)>();
        using IDisposable subscription = session.Client.Events.Subscribe<EntityPassengersChanged>(message =>
            events.Add((message.VehicleId, rider.Vehicle, vehicle.Passengers.Count)));
        await session.Send(new ClientboundSetEntityDataPacket(43, new EntityMetadataList([new(6, MetadataValue.Float(0))], [])));
        Assert.Empty(await session.Barrier());
        for (int tick = 0; tick < 21; tick++)
        {
            await session.Tick();
            Assert.Empty(await session.Barrier());
        }
        await session.Client.InvokeAsync(client =>
        {
            Assert.True(client.State.EntityMembership.TryGetLegacyMembership(vehicle, out var membership));
            Assert.True(membership.Removed);
            Assert.Same(vehicle, client.State.Entities.Get(43));
            Assert.Null(rider.Vehicle);
            Assert.Empty(vehicle.Passengers);
            return true;
        }, session.Token);
        var snapshot = Assert.Single(events);
        Assert.Equal(43, snapshot.Vehicle);
        Assert.Null(snapshot.Parent);
        Assert.Equal(0, snapshot.Passengers);
        await End(session);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FramedModeZeroUpdatesBothRemoteGraphDirectionsBeforeAnyTick(bool riderFirst, bool pending)
    {
        await using var session = await BlockUseSession.Create(47, "stone");
        (Entity rider, Entity vehicle) = await Pair(session, riderFirst, pending);
        await Link(session, 42, 43, false);
        await session.Client.InvokeAsync(client =>
        {
            Assert.Same(vehicle, rider.Vehicle);
            Assert.Same(rider, Assert.Single(vehicle.Passengers));
            Assert.Equal(RiderStart, rider.Position);
            return true;
        }, session.Token);
        await End(session);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SessionTickMatchesMeasuredPlayerRiderPositionAndPreservesPendingRotation(bool riderFirst, bool pending)
    {
        await using var session = await BlockUseSession.Create(47, "stone");
        (Entity rider, Entity vehicle) = await Pair(session, riderFirst, pending);
        await Link(session, 42, 43, false);
        // Retained native tick-player-{vehicle,rider}-first-pending{0,1} rows have identical rider pose bits. Expected values are raw observations, not an offset formula.
        for (int tick = 1; tick <= 4; tick++)
        {
            await session.Tick();
            Assert.Empty(await session.Barrier());
            await session.Client.InvokeAsync(client =>
            {
                Assert.Equal(0x400c000000000000L, BitConverter.DoubleToInt64Bits(rider.Position.X));
                Assert.Equal(0x40505fffffd9999aL, BitConverter.DoubleToInt64Bits(rider.Position.Y));
                Assert.Equal(0x400c000000000000L, BitConverter.DoubleToInt64Bits(rider.Position.Z));
                Assert.Equal(pending ? new[] { 30F, 60F, 90F, 90F }[tick - 1] : 0F, rider.Yaw);
                Assert.Equal(pending ? new[] { 15F, 30F, 45F, 45F }[tick - 1] : 0F, rider.Pitch);
                Assert.Equal(Vec3d.Zero, rider.Velocity);
                Assert.True(client.State.EntityMotion.TryGetRemotePlayerInterpolation(rider, out var interpolation));
                Assert.Equal(pending ? Math.Max(0, 3 - tick) : 0, interpolation.RemainingSteps);
                if (pending)
                    Assert.Equal(new Vec3d(3.5, 65, 3.5), interpolation.Target);
                Assert.Equal(pending ? new Vec3d(3.5, 65, 3.5) : RiderStart, client.State.PacketBases.Get(rider));
                Assert.Equal(VehicleStart, vehicle.Position);
                Assert.True(client.State.EntityGeometry.TryGet(rider, JavaGameData.EntityGeometryData(47), out Aabb bounds, out bool blocks, 47));
                Assert.True(blocks);
                Assert.Equal(0x4009999998000000L, BitConverter.DoubleToInt64Bits(bounds.MinX));
                Assert.Equal(0x40505fffffd9999aL, BitConverter.DoubleToInt64Bits(bounds.MinY));
                Assert.Equal(0x4050d33332d9999aL, BitConverter.DoubleToInt64Bits(bounds.MaxY));
                Assert.True(client.State.EntityMembership.IsWorldQueryMember(rider));
                return true;
            }, session.Token);
        }
        Vec3d mounted = rider.Position;
        await Link(session, 42, -1, false);
        Assert.Null(rider.Vehicle);
        Assert.Empty(vehicle.Passengers);
        Assert.Equal(mounted, rider.Position);
        await session.Tick();
        Assert.Empty(await session.Barrier());
        Assert.Equal(mounted, rider.Position);
        await End(session);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PublicPlacementUsesTheRiderBoxSeparatelyFromItsVehicle(bool riderFirst, bool blocked)
    {
        await using var session = await BlockUseSession.Create(47, "stone", physics: true);
        var vehiclePosition = new Vec3d(1.5, 65.5, 1.5);
        if (riderFirst)
            await Spawn(session, 42, new Vec3d(5.5, 64, 5.5));
        await Spawn(session, 43, vehiclePosition);
        if (!riderFirst)
            await Spawn(session, 42, new Vec3d(5.5, 64, 5.5));
        await Link(session, 42, 43, false);
        await session.Tick();
        Assert.Empty(await session.Barrier());
        var clicked = new BlockPos(blocked ? 1 : 2, 67, 1);
        await session.Block(clicked, "stone");
        await session.Hold("stone");
        Assert.Empty(await session.Barrier());
        Task action = session.Client.Actions.Interaction.PlaceBlockAsync(clicked, Direction.Up, new Vec3d(.5, 1, .5), Hand.Main, ct: session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
        List<IPacket> packets = await session.Barrier();
        await action.WaitAsync(session.Token);
        Assert.Equal(blocked ? 0 : 2, packets.Count);
        if (!blocked)
        {
            Assert.Equal(clicked, Assert.IsType<ServerboundLegacyBlockPlacePacket>(packets[0]).Position);
            Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(packets[1]).Hand);
        }
        BlockUsePrediction prediction = await session.Client.InvokeAsync(client => Assert.IsType<InteractionActions>(client.Actions.Interaction).PredictBlockUse(clicked, Direction.Up, new Vec3d(.5, 1, .5), Hand.Main), session.Token);
        Assert.Equal(!blocked, prediction.SendPacket);
        Assert.Equal(blocked ? BlockUseResult.Fail : BlockUseResult.Success, prediction.Result);
        Assert.Equal(blocked ? BlockUseSwing.None : BlockUseSwing.Packet, prediction.Swing);
        Assert.True(session.Client.State.World.GetBlock(clicked.Above()).IsAir);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
        await End(session);
    }

    [Theory]
    [InlineData(90, "pig")]
    [InlineData(100, "horse")]
    public async Task ModeOneLeashAndModeZeroMountAreIndependent(int type, string name)
    {
        await using var session = await BlockUseSession.Create(47, "stone");
        (_, Entity vehicle) = await Pair(session, false, false);
        await session.Send(new ClientboundAddMobPacket(44, type, 5.5, 64, 5.5, 0, 0, 0, 0, 0, 0, new EntityMetadataList([], [])));
        Assert.Empty(await session.Barrier());
        Entity mob = session.Client.State.Entities.Get(44)!;
        Assert.Equal(name, mob.Type.Id.Path);
        await Link(session, 44, 43, false);
        await Link(session, 44, 42, true);
        Assert.Same(vehicle, mob.Vehicle);
        Assert.Same(session.Client.State.Entities.Get(42), mob.LeashHolder);
        await Link(session, 44, 999, true);
        Assert.Null(mob.LeashHolder);
        Assert.Same(vehicle, mob.Vehicle);
        await Link(session, 44, 42, true);
        await Link(session, 44, -1, false);
        Assert.Null(mob.Vehicle);
        Assert.Empty(vehicle.Passengers);
        Assert.Same(session.Client.State.Entities.Get(42), mob.LeashHolder);
        await Link(session, 44, -1, true);
        Assert.Null(mob.LeashHolder);
        await End(session);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModeOneDoesNotMountOrLeashKnownNonMobSources(bool boat)
    {
        await using var session = await BlockUseSession.Create(47, "stone");
        (Entity rider, Entity vehicle) = await Pair(session, false, false);
        Entity source = rider;
        if (boat)
        {
            await Object(session, 44, 1, RiderStart);
            source = session.Client.State.Entities.Get(44)!;
        }
        await Link(session, 42, 43, false);
        await Link(session, source.Id, 43, true);
        Assert.Null(source.LeashHolder);
        Assert.Same(vehicle, rider.Vehicle);
        Assert.Null(vehicle.Vehicle);
        await Link(session, source.Id, -1, true);
        Assert.Same(vehicle, rider.Vehicle);
        await End(session);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task MissingEndpointsAreNotQueuedForLaterArrivals(int missing)
    {
        await using var session = await BlockUseSession.Create(47, "stone");
        if (missing == 0)
            await Spawn(session, 43, VehicleStart);
        if (missing == 1)
            await Spawn(session, 42, RiderStart);
        await Link(session, 42, 43, false);
        if (missing != 1)
            await Spawn(session, 42, RiderStart);
        if (missing != 0)
            await Spawn(session, 43, VehicleStart);
        Assert.Empty(await session.Barrier());
        Entity rider = session.Client.State.Entities.Get(42)!, vehicle = session.Client.State.Entities.Get(43)!;
        Assert.Null(rider.Vehicle);
        Assert.Empty(vehicle.Passengers);
        await session.Tick();
        Assert.Empty(await session.Barrier());
        Assert.Equal(RiderStart, rider.Position);
        await End(session);
    }

    [Fact]
    public async Task RepeatTransferAndMissingDestinationKeepTheGraphConsistent()
    {
        await using var session = await BlockUseSession.Create(47, "stone");
        (Entity rider, Entity vehicle) = await Pair(session, false, true);
        await Spawn(session, 44, new Vec3d(5.5, 65, 5.5));
        await Link(session, 42, 43, false);
        await Link(session, 42, 43, false);
        Assert.Same(rider, Assert.Single(vehicle.Passengers));
        await session.Tick();
        Assert.Empty(await session.Barrier());
        await Link(session, 42, 44, false);
        Entity other = session.Client.State.Entities.Get(44)!;
        Assert.Empty(vehicle.Passengers);
        Assert.Same(other, rider.Vehicle);
        Assert.Same(rider, Assert.Single(other.Passengers));
        await session.Tick();
        Assert.Empty(await session.Barrier());
        Assert.Equal(new Vec3d(5.5, 65.99999996423722, 5.5), rider.Position);
        Vec3d before = rider.Position;
        await Link(session, 42, 999, false);
        Assert.Null(rider.Vehicle);
        Assert.Empty(other.Passengers);
        Assert.Equal(before, rider.Position);
        await End(session);
    }

    [Theory]
    [InlineData(42)]
    [InlineData(43)]
    public async Task RemovalAndSameIdReplacementDropOnlyTheOldInstanceGraphAndControllers(int replaced)
    {
        await using var session = await BlockUseSession.Create(47, "stone");
        (Entity rider, Entity vehicle) = await Pair(session, false, true);
        await Link(session, 42, 43, false);
        await session.Tick();
        Assert.Empty(await session.Barrier());
        Entity old = session.Client.State.Entities.Get(replaced)!;
        await Spawn(session, replaced, new Vec3d(7.5, 66, 7.5));
        Assert.Empty(await session.Barrier());
        Entity replacement = session.Client.State.Entities.Get(replaced)!;
        Assert.NotSame(old, replacement);
        Assert.Null(old.Vehicle);
        Assert.Empty(old.Passengers);
        Assert.Null(rider.Vehicle);
        Assert.Empty(vehicle.Passengers);
        Assert.Null(replacement.Vehicle);
        Assert.Empty(replacement.Passengers);
        await session.Client.InvokeAsync(client =>
        {
            Assert.False(client.State.EntityMotion.TryGetRemotePlayerInterpolation(old, out _));
            Assert.True(client.State.EntityMotion.TryGetRemotePlayerInterpolation(replacement, out var pending));
            Assert.Equal(0, pending.RemainingSteps);
            Assert.Equal(new Vec3d(7.5, 66, 7.5), client.State.PacketBases.Get(replacement));
            return true;
        }, session.Token);
        await session.Send(new ClientboundRemoveEntitiesPacket([replaced]));
        Assert.Empty(await session.Barrier());
        Assert.Null(session.Client.State.Entities.Get(replaced));
        await End(session);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyChunkRegistrationDoesNotInventAnEligibleFirstTick(bool terrain)
    {
        await using var session = await BlockUseSession.Create(47, "stone", terrain: terrain);
        (Entity rider, Entity vehicle) = await Pair(session, true, true);
        await Link(session, 42, 43, false);
        await session.Tick();
        Assert.Empty(await session.Barrier());
        await session.Client.InvokeAsync(client =>
        {
            Assert.True(client.State.EntityMotion.TryGetRemotePlayerInterpolation(rider, out var pending));
            Assert.Equal(terrain ? 2 : 3, pending.RemainingSteps);
            Assert.Equal(terrain ? 0x40505fffffd9999aL : 0x4050000000000000L, BitConverter.DoubleToInt64Bits(rider.Position.Y));
            Assert.True(client.State.EntityMembership.TryGetLegacyMembership(rider, out var member));
            Assert.True(member.Added);
            Assert.True(member.InEntityList);
            Assert.Equal(terrain ? 4 : 0, member.Y);
            Assert.Equal(terrain ? 4 : (int?)null, member.ActualSection);
            Assert.Equal(terrain, client.State.EntityMembership.IsWorldQueryMember(rider));
            return true;
        }, session.Token);
        await session.Tick();
        Assert.Empty(await session.Barrier());
        Assert.Equal(0x40505fffffd9999aL, BitConverter.DoubleToInt64Bits(rider.Position.Y));
        await End(session);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(10, false)]
    [InlineData(90, true)]
    [InlineData(100, true)]
    public async Task UnownedVehicleMotionAndLaterDismountStayUnknown(int type, bool mob)
    {
        await using var session = await BlockUseSession.Create(47, "stone", physics: true);
        await Spawn(session, 42, RiderStart);
        if (mob)
            await session.Send(new ClientboundAddMobPacket(43, type, 3.5, 64.5, 3.5, 0, 0, 0, 0, 0, 0, new EntityMetadataList([], [])));
        else
            await Object(session, 43, type, VehicleStart);
        await Link(session, 42, 43, false);
        Entity rider = session.Client.State.Entities.Get(42)!;
        await session.Tick();
        Assert.Empty(await session.Barrier());
        await session.Client.InvokeAsync(client =>
        {
            Assert.False(client.State.EntityGeometry.IsPositionKnown(rider));
            return true;
        }, session.Token);
        await Link(session, 42, -1, false);
        await session.Tick();
        Assert.Empty(await session.Barrier());
        Assert.False(await session.Client.InvokeAsync(client => client.State.EntityGeometry.IsPositionKnown(rider), session.Token));
        await End(session);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ContradictoryEdgesAreUnsupportedSafetyCasesAndCannotRestoreKnownPose(int mode)
    {
        await using var session = await BlockUseSession.Create(47, "stone");
        (Entity rider, Entity vehicle) = await Pair(session, false, false);
        await Spawn(session, 44, new Vec3d(5.5, 64, 5.5));
        await Spawn(session, 45, new Vec3d(7.5, 64, 7.5));
        await Link(session, 42, 43, false);
        if (mode == 2)
            await Link(session, 45, 44, false);
        await Link(session, mode == 1 ? 43 : 42, mode == 0 ? 42 : mode == 1 ? 42 : 44, false);
        Assert.Same(vehicle, rider.Vehicle);
        Assert.Same(rider, Assert.Single(vehicle.Passengers));
        await session.Tick();
        Assert.Empty(await session.Barrier());
        Assert.False(await session.Client.InvokeAsync(client => client.State.EntityGeometry.IsPositionKnown(rider), session.Token));
        Assert.Equal(4, session.Client.State.Entities.Count);
        await End(session);
    }

    [Fact]
    public async Task RemoteRiderEvidenceDoesNotResolveTheLocalMountGuardAndRespawnDropsOldOwners()
    {
        await using var session = await BlockUseSession.Create(47, "stone");
        (Entity rider, Entity vehicle) = await Pair(session, false, true);
        await Link(session, 42, 43, false);
        await Link(session, session.Client.State.Self.EntityId, 43, false);
        Assert.True(await session.Client.InvokeAsync(client => client.State.EntityMotion.IsLocalMountUnresolved, session.Token));
        Assert.Same(vehicle, rider.Vehicle);
        await Link(session, session.Client.State.Self.EntityId, -1, true);
        Assert.True(await session.Client.InvokeAsync(client => client.State.EntityMotion.IsLocalMountUnresolved, session.Token));
        await Link(session, session.Client.State.Self.EntityId, -1, false);
        Assert.False(await session.Client.InvokeAsync(client => client.State.EntityMotion.IsLocalMountUnresolved, session.Token));
        await session.Send(new ClientboundRespawnPacket(null, 0, new LegacyRespawnFields(-1, 2, 0, "flat")));
        Assert.Empty(await session.Barrier());
        Assert.Empty(session.Client.State.Entities.All);
        Assert.Null(rider.Vehicle);
        Assert.Empty(vehicle.Passengers);
        Assert.False(await session.Client.InvokeAsync(client => client.State.EntityMotion.TryGetRemotePlayerInterpolation(rider, out _), session.Token));
        await End(session);
    }

    [Theory]
    [InlineData(42, false)]
    [InlineData(42, true)]
    [InlineData(43, false)]
    [InlineData(43, true)]
    public async Task UnmeasuredDeadOrMalformedHealthRiderContextsStayUnknown(int actorId, bool malformed)
    {
        await using var session = await BlockUseSession.Create(47, "stone");
        (Entity rider, _) = await Pair(session, false, true);
        await Link(session, 42, 43, false);
        await session.Send(new ClientboundSetEntityDataPacket(actorId, new EntityMetadataList([new(6, malformed ? MetadataValue.Byte(0) : MetadataValue.Float(0))], [])));
        Assert.Empty(await session.Barrier());
        await session.Tick();
        Assert.Empty(await session.Barrier());
        Assert.False(await session.Client.InvokeAsync(client => client.State.EntityGeometry.IsPositionKnown(rider), session.Token));
        await End(session);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnownedVehicleCannotSupplyAStaleDefinitePlacementObstruction(bool riderFirst)
    {
        await using var session = await BlockUseSession.Create(47, "stone", physics: true);
        if (riderFirst)
            await Spawn(session, 42, new Vec3d(5.5, 64, 5.5));
        await Object(session, 43, 1, new Vec3d(1.5, 65.5, 1.5));
        if (!riderFirst)
            await Spawn(session, 42, new Vec3d(5.5, 64, 5.5));
        await Link(session, 42, 43, false);
        await session.Tick();
        Assert.Empty(await session.Barrier());
        await session.Hold("stone");
        List<IPacket> packets = await session.Use(Hand.Main);
        Assert.IsType<ServerboundLegacyBlockPlacePacket>(Assert.Single(packets));
        BlockUsePrediction prediction = await session.Client.InvokeAsync(client => Assert.IsType<InteractionActions>(client.Actions.Interaction).PredictBlockUse(BlockUseSession.Target, Direction.Up, new Vec3d(.5, 1, .5), Hand.Main), session.Token);
        Assert.Equal(BlockUsePrediction.Unknown, prediction);
        await End(session);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnresolvedRelationCannotBecomeKnownAbsenceAfterOrdinaryPacketsAndChunkReplacement(bool replace)
    {
        await using var session = await BlockUseSession.Create(47, "stone", physics: true);
        (Entity rider, _) = await Pair(session, false, true);
        await Link(session, 42, 43, false);
        await session.Tick();
        Assert.Empty(await session.Barrier());
        await session.Hold("stone");
        var retained = await session.Client.InvokeAsync(client =>
        {
            Assert.True(client.State.EntityMembership.TryGetLegacyMembership(rider, out var member));
            return member;
        }, session.Token);
        Vec3d expectedBase = new(3.5, 65, 3.5);
        int expectedSteps = 2;
        await Link(session, 42, 42, false);
        await AssertUnknown("rejected relation");
        await session.Send(new ClientboundTeleportEntityPacket(42, 7.5, 64, 7.5, 90, 45, false, null, 0));
        Assert.Empty(await session.Barrier());
        expectedBase = new(7.5, 64, 7.5);
        expectedSteps = 3;
        await AssertUnknown("ordinary teleport");
        for (int tick = 0; tick < 3; tick++)
        {
            await session.Tick();
            Assert.Empty(await session.Barrier());
        }
        await AssertUnknown("ordinary ticks");
        await session.Forget();
        await session.Terrain();
        await session.Block(BlockUseSession.Target, "stone");
        await session.Block(BlockUseSession.Target.Above(), "air");
        await session.Block(new BlockPos(2, 64, 2), "stone");
        Assert.Empty(await session.Barrier());
        await AssertUnknown("replacement chunk before reentry");
        await session.Tick();
        Assert.Empty(await session.Barrier());
        await AssertUnknown("replacement chunk after reentry");
        if (replace)
        {
            await Spawn(session, 42, new Vec3d(7.5, 64, 7.5));
            Assert.Empty(await session.Barrier());
            Assert.NotSame(rider, session.Client.State.Entities.Get(42));
        }
        else
        {
            await session.Send(new ClientboundRemoveEntitiesPacket([42, 43]));
            Assert.Empty(await session.Barrier());
        }
        List<IPacket> clear = await session.Use(Hand.Main);
        Assert.Equal(2, clear.Count);
        Assert.IsType<ServerboundLegacyBlockPlacePacket>(clear[0]);
        Assert.IsType<ServerboundSwingPacket>(clear[1]);
        await End(session);
        return;

        async Task AssertUnknown(string phase)
        {
            List<IPacket> packets = await session.Use(Hand.Main);
            Assert.True(packets.Count == 1, $"{phase}: expected one use-only frame, received {packets.Count}.");
            Assert.IsType<ServerboundLegacyBlockPlacePacket>(packets[0]);
            BlockUsePrediction prediction = await session.Client.InvokeAsync(client => Assert.IsType<InteractionActions>(client.Actions.Interaction).PredictBlockUse(BlockUseSession.Target, Direction.Up, new Vec3d(.5, 1, .5), Hand.Main), session.Token);
            Assert.Equal(BlockUsePrediction.Unknown, prediction);
            await session.Client.InvokeAsync(client =>
            {
                Assert.False(client.State.EntityGeometry.IsPositionKnown(rider));
                Assert.True(client.State.EntityMembership.TryGetLegacyMembership(rider, out var membership));
                Assert.False(membership.RegistrationKnown);
                Assert.Equal(retained with { RegistrationKnown = false }, membership);
                Assert.Null(client.State.EntityMembership.IsWorldQueryMember(rider));
                Assert.True(client.State.EntityMotion.TryGetRemotePlayerInterpolation(rider, out var interpolation));
                Assert.Equal(expectedSteps, interpolation.RemainingSteps);
                Assert.Equal(expectedBase, interpolation.Target);
                Assert.Equal(expectedBase, client.State.PacketBases.Get(rider));
                return true;
            }, session.Token);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KnownLivingDetachRetainsPoseAndPendingTargetThenFinishesItsExistingInterpolation(bool riderFirst)
    {
        await using var session = await BlockUseSession.Create(47, "stone");
        (Entity rider, Entity vehicle) = await Pair(session, riderFirst, true);
        await Link(session, 42, 43, false);
        await session.Tick();
        Assert.Empty(await session.Barrier());
        Vec3d mounted = rider.Position;
        await Link(session, 42, -1, false);
        Assert.Null(rider.Vehicle);
        Assert.Empty(vehicle.Passengers);
        Assert.Equal(mounted, rider.Position);
        await session.Client.InvokeAsync(client =>
        {
            Assert.True(client.State.EntityMotion.TryGetRemotePlayerInterpolation(rider, out var pending));
            Assert.Equal((new Vec3d(3.5, 65, 3.5), 2), pending);
            return true;
        }, session.Token);
        await session.Tick();
        await session.Tick();
        Assert.Empty(await session.Barrier());
        Assert.Equal(new Vec3d(3.5, 65, 3.5), rider.Position);
        Assert.Equal(90F, rider.Yaw);
        Assert.Equal(45F, rider.Pitch);
        await End(session);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VehicleInterpolationFinishesBeforeItsRiderRegardlessOfInsertionOrder(bool riderFirst)
    {
        await using var session = await BlockUseSession.Create(47, "stone");
        (Entity rider, Entity vehicle) = await Pair(session, riderFirst, true);
        await session.Send(new ClientboundTeleportEntityPacket(43, 5.5, 65.5, 5.5, 90, 0, false, null, 0));
        await Link(session, 42, 43, false);
        // Source-composed control: bex.m interpolation precedes pk.al in adm.g; these are literal arithmetic boundary values, not new native measurements.
        double[] x = [4.166666666666667, 4.833333333333334, 5.5];
        double[] y = [65.83333329757055, 66.16666663090388, 66.49999996423722];
        for (int tick = 0; tick < 3; tick++)
        {
            await session.Tick();
            Assert.Empty(await session.Barrier());
            Assert.Equal(x[tick], vehicle.Position.X);
            Assert.Equal(new Vec3d(x[tick], y[tick], x[tick]), rider.Position);
        }
        await End(session);
    }

    [Fact]
    public async Task UnresolvedLocalMountNeverUsesRemoteEvidenceToProvePlacementSuccess()
    {
        await using var session = await BlockUseSession.Create(47, "stone", physics: true);
        await Pair(session, false, false);
        await Link(session, 42, 43, false);
        await session.Hold("stone");
        await Link(session, session.Client.State.Self.EntityId, 43, false);
        List<IPacket> unknown = await session.Use(Hand.Main);
        Assert.IsType<ServerboundLegacyBlockPlacePacket>(Assert.Single(unknown));
        await Link(session, session.Client.State.Self.EntityId, -1, false);
        Assert.Equal(2, (await session.Use(Hand.Main)).Count);
        await End(session);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(100)]
    public async Task UnsupportedLocalLeashHolderDoesNotInventAClearFromAnAbsentRemoteLookup(int type)
    {
        await using var session = await BlockUseSession.Create(47, "stone");
        await Spawn(session, 42, RiderStart);
        await session.Send(new ClientboundAddMobPacket(44, type, 5.5, 64, 5.5, 0, 0, 0, 0, 0, 0, new EntityMetadataList([], [])));
        await Link(session, 44, 42, true);
        Entity source = session.Client.State.Entities.Get(44)!, observedHolder = session.Client.State.Entities.Get(42)!;
        Assert.Same(observedHolder, source.LeashHolder);
        await Link(session, 44, session.Client.State.Self.EntityId, true);
        // Local holder state is outside this remote-only model. Preserve the last observation without asserting the native current holder.
        Assert.Same(observedHolder, source.LeashHolder);
        Assert.Null(source.Vehicle);
        await End(session);
    }

    private static async Task Object(BlockUseSession session, int id, int type, Vec3d position)
    {
        await session.Send(new ClientboundAddEntityPacket(id, Guid.Empty, type, position.X, position.Y, position.Z, 0, 0, 0, 0, 0, 0, 0, null));
        Assert.Empty(await session.Barrier());
    }

    private static async Task<(Entity Rider, Entity Vehicle)> Pair(BlockUseSession session, bool riderFirst, bool pending)
    {
        if (riderFirst)
        {
            await Spawn(session, 42, RiderStart);
            await Spawn(session, 43, VehicleStart, 90);
        }
        else
        {
            await Spawn(session, 43, VehicleStart, 90);
            await Spawn(session, 42, RiderStart);
        }
        if (pending)
            await session.Send(new ClientboundTeleportEntityPacket(42, 3.5, 65, 3.5, 90, 45, false, null, 0));
        Assert.Empty(await session.Barrier());
        return (session.Client.State.Entities.Get(42)!, session.Client.State.Entities.Get(43)!);
    }

    private static Task Spawn(BlockUseSession session, int id, Vec3d position, float yaw = 0)
        => session.Send(new ClientboundAddPlayerPacket(id, new Guid(id, 0, 0, new byte[8]), position.X, position.Y, position.Z, yaw, 0, 0, new EntityMetadataList([], [])));

    private static async Task Link(BlockUseSession session, int source, int destination, bool leash)
    {
        var packet = new ClientboundSetEntityLinkPacket(source, destination, leash);
        if (source == 42 && destination is 43 or -1 && !leash)
        {
            // Exact retained native hl packet bodies, SHA-256 e1b6bfb5... and 83937776... respectively.
            byte[] bytes = Convert.FromHexString(destination == 43 ? "0000002A0000002B00" : "0000002AFFFFFFFF00");
            var codec = BoundDescriptorCodec.Clientbound(47, "set_entity_link");
            var context = new PacketCodecContext(JavaGameData.Registries(47), IConnectionCodecState.Empty);
            var decoded = Assert.IsType<ClientboundSetEntityLinkPacket>(codec.Decode(bytes, context));
            Assert.Equal(packet, decoded);
            var output = new ArrayBufferWriter<byte>();
            var writer = new PacketWriter(output);
            codec.Encode(ref writer, decoded, context);
            Assert.Equal(bytes, output.WrittenSpan.ToArray());
        }
        await session.Send(packet);
        Assert.Empty(await session.Barrier());
    }

    private static async Task End(BlockUseSession session)
    {
        await session.Client.DisconnectAsync(session.Token);
        await Assert.ThrowsAsync<ConnectionClosedException>(async () =>
        {
            while (true)
                await session.Server.NextFrameAsync(session.Token);
        });
    }
}
