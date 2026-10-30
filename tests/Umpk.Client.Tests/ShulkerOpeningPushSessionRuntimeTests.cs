using System.Buffers;
using Umpk.Client.Actions;
using Umpk.Client.Events;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

public sealed class ShulkerOpeningPushSessionRuntimeTests
{
    private static readonly Guid PeerUuid = Guid.Parse("2f9b7a43-8223-48a4-b455-d1f53445e03c");
    private static readonly Vec3d SourcePosition = new(8.5, 70, 8.5);
    // Original native geometry fixture, scale1, raw peek100, first eligible opening phase: float bits widened exactly. This is a measured lid delta, not a measured pushed trajectory.
    private const double OpeningDistance = 0.006152838468551636;

    public static IEnumerable<object[]> FaceCases()
    {
        foreach (int protocol in new[] { 770, 777 })
            foreach (Direction face in Enum.GetValues<Direction>())
                yield return [protocol, face];
    }

    [Theory]
    [MemberData(nameof(FaceCases))]
    public async Task OpeningMovesTheOwnedRemoteBodyThroughTheSessionPhase(int protocol, Direction face)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: false);
        await SpawnSource(session, face);
        Vec3d original = TargetPosition(face);
        await SpawnPlayer(session, original);
        await Tick(session);
        Entity target = Assert.IsType<Entity>(session.Client.State.Entities.Get(43));
        Assert.Equal(original, target.Position);
        Vec3d expected = original.Add(OpeningVector(face));
        List<Vec3d> published = [];
        using IDisposable subscription = session.Client.Events.Subscribe<EntityMoved>(value =>
        {
            if (value.EntityId == 43)
                published.Add(target.Position);
        });
        await Peek(session, 100);
        Assert.Equal(original, target.Position);
        await Tick(session);
        Assert.Equal(expected, target.Position);
        Assert.Contains(expected, published);
        Assert.Equal(original, session.Client.State.PacketBases.Get(target));
        Assert.True(Known(session, target));
        Assert.True(session.Client.State.EntityGeometry.TryGet(target, JavaGameData.EntityGeometryData(protocol), out Aabb bounds, out _, protocol));
        Assert.Equal(Aabb.OfSize(expected.X, expected.Y, expected.Z, .6F, 1.8F), bounds);
        await Disconnect(session);
    }

    [Theory]
    [InlineData("constructor-no-physics")]
    [InlineData("spectator")]
    [InlineData("other-shulker")]
    [InlineData("same-vehicle")]
    public async Task NativeQueryAndMoverExclusionsDoNotInventAPush(string reason)
    {
        await using Session session = await Session.Create(777, "stone", physics: false);
        await SpawnSource(session, Direction.West);
        Vec3d original = TargetPosition(Direction.West);
        if (reason == "other-shulker")
            await Spawn(session, 43, "shulker", original);
        else
            await SpawnPlayer(session, original, reason == "spectator" ? GameMode.Spectator : GameMode.Survival);
        if (reason != "constructor-no-physics")
            await Tick(session);
        if (reason == "same-vehicle")
            await Send(session, new ClientboundSetPassengersPacket(42, [43]));
        Entity target = Assert.IsType<Entity>(session.Client.State.Entities.Get(43));
        Vec3d before = target.Position;
        await Peek(session, 100);
        await Tick(session);
        Assert.Equal(before, target.Position);
        // The shared-vehicle control proves exclusion only; it does not establish the unimplemented modern rider layout.
        await Disconnect(session);
    }

    [Fact]
    public async Task ClosingDoesNotPushAndKnownDistantBoundsStayIrrelevant()
    {
        await using Session session = await Session.Create(777, "stone", physics: false);
        await SpawnSource(session, Direction.West);
        await SpawnPlayer(session, TargetPosition(Direction.West));
        await Tick(session);
        await Peek(session, 100);
        await Tick(session);
        Entity target = Assert.IsType<Entity>(session.Client.State.Entities.Get(43));
        Vec3d afterOpening = target.Position;
        await Peek(session, 0);
        await Tick(session);
        Assert.Equal(afterOpening, target.Position);
        await Send(session, new ClientboundEntityPositionSyncPacket(43, new PositionMoveRotation(new Vec3d(12.5, 70, 12.5), Vec3d.Zero, 0, 0), false) { Path = new EntityPositionPath.Linear(new Vec3d(12.5, 70, 12.5)) });
        for (int i = 0; i < 4; i++)
            await Tick(session);
        Vec3d far = target.Position;
        await Peek(session, 100);
        await Tick(session);
        Assert.Equal(far, target.Position);
        Assert.True(Known(session, target));
        await Disconnect(session);
    }

    [Theory]
    [InlineData("border")]
    [InlineData("non-unit-speed")]
    [InlineData("unsupported-target")]
    public async Task UnownedMoverContextInvalidatesAffectedBounds(string context)
    {
        await using Session session = await Session.Create(777, "stone", physics: false);
        await SpawnSource(session, Direction.West);
        if (context == "unsupported-target")
            await Spawn(session, 43, "end_crystal", TargetPosition(Direction.West));
        else
            await SpawnPlayer(session, TargetPosition(Direction.West));
        await Tick(session);
        if (context == "border")
            await Send(session, new ClientboundInitializeBorderPacket(0, 0, 20, 20, 0, 29999984, 5, 15));
        if (context == "non-unit-speed")
            await session.Block(new BlockPos(9, 70, 8), "honey_block");
        await Peek(session, 100);
        await Tick(session);
        Entity target = Assert.IsType<Entity>(session.Client.State.Entities.Get(43));
        Assert.False(Known(session, target));
        await Disconnect(session);
        if (context == "border")
        {
            await session.Restart("stone");
            await SpawnSource(session, Direction.West);
            Vec3d original = TargetPosition(Direction.West);
            await SpawnPlayer(session, original);
            await session.Block(new BlockPos(10, 70, 8), "stone");
            await Send(session, new ClientboundEntityPositionSyncPacket(43, new PositionMoveRotation(original, Vec3d.Zero, 0, 0), true) { Path = new EntityPositionPath.Linear(original) });
            await Tick(session);
            await Peek(session, 100);
            for (int i = 0; i < 20; i++)
                await Tick(session);
            Assert.False(Known(session, session.Client.State.Entities.Get(43)!));
            await Disconnect(session);
        }
    }

    [Fact]
    public async Task FreezeAndOneGrantedStepUseTheExistingClock()
    {
        await using Session session = await Session.Create(777, "stone", physics: false);
        await SpawnSource(session, Direction.West);
        Vec3d original = TargetPosition(Direction.West);
        await SpawnPlayer(session, original);
        await Tick(session);
        await Send(session, new ClientboundTickingStatePacket(20F, true));
        await Peek(session, 100);
        await Tick(session);
        Entity target = Assert.IsType<Entity>(session.Client.State.Entities.Get(43));
        Assert.Equal(original, target.Position);
        await Send(session, new ClientboundTickingStepPacket(1));
        await Tick(session);
        Assert.Equal(original.Add(OpeningVector(Direction.West)), target.Position);
        Vec3d afterStep = target.Position;
        await Tick(session);
        Assert.Equal(afterStep, target.Position);
        await Disconnect(session);
    }

    [Fact]
    public async Task SynthesizedStorageAfterUnloadDoesNotTickTheShulker()
    {
        await using Session session = await Session.Create(777, "stone", physics: false);
        await SpawnSource(session, Direction.West);
        Vec3d original = TargetPosition(Direction.West);
        await SpawnPlayer(session, original);
        await Tick(session);
        await session.Forget();
        await session.Block(new BlockPos(8, 70, 8), "air");
        Assert.Empty(await session.Barrier());
        Assert.NotNull(session.Client.State.World.GetColumn(new BlockPos(8, 70, 8)));
        Assert.False(session.Client.State.EntityMembership.HasColumn(new BlockPos(8, 70, 8)));
        await Peek(session, 100);
        await Tick(session);
        Assert.Equal(original, session.Client.State.Entities.Get(43)!.Position);
        await Disconnect(session);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnprovenArrivalOrReloadTickOrderDoesNotRetainKnownBounds(bool reload)
    {
        await using Session session = await Session.Create(777, "stone", terrain: reload, physics: false);
        await SpawnSource(session, Direction.West);
        await SpawnPlayer(session, TargetPosition(Direction.West));
        if (reload)
        {
            await Tick(session);
            await session.Forget();
        }
        await session.Terrain();
        await Peek(session, 100);
        await Tick(session);
        Assert.False(Known(session, session.Client.State.Entities.Get(43)!));
        await Disconnect(session);
    }

    [Fact]
    public async Task PredictedPushShiftsEverySteppedKnotWithoutChangingThePacketBase()
    {
        await using Session session = await Session.Create(777, "stone", physics: false);
        await SpawnSource(session, Direction.West);
        Vec3d start = TargetPosition(Direction.West);
        await SpawnPlayer(session, start);
        await Tick(session);
        Vec3d end = start.Add(1, 0, 0);
        await Send(session, new ClientboundEntityPositionSyncPacket(43, new PositionMoveRotation(end, Vec3d.Zero, 0, 0), false)
        {
            Path = new EntityPositionPath.Stepped([new(start, 0), new(start.Add(.5, 0, 0), 2), new(end, 4)]),
        });
        await Peek(session, 100);
        await Tick(session);
        Entity target = Assert.IsType<Entity>(session.Client.State.Entities.Get(43));
        Vec3d push = OpeningVector(Direction.West);
        Assert.Equal(start.Add(.25, 0, 0).Add(push), target.Position);
        Assert.Equal(end, session.Client.State.PacketBases.Get(target));
        await Peek(session, 0);
        foreach (double offset in new[] { .625, .875, 1D })
        {
            await Tick(session);
            Assert.Equal(start.Add(offset, 0, 0).Add(push), target.Position);
            Assert.Equal(end, session.Client.State.PacketBases.Get(target));
        }
        await Disconnect(session);
        await session.Restart("stone");
        await SpawnPlayer(session, start);
        await SpawnSource(session, Direction.West);
        await Tick(session);
        await Send(session, new ClientboundEntityPositionSyncPacket(43, new PositionMoveRotation(end, Vec3d.Zero, 0, 0), false)
        {
            Path = new EntityPositionPath.Stepped([new(start, 0), new(start.Add(.5, 0, 0), 2), new(end, 4)]),
        });
        await Peek(session, 100);
        await Tick(session);
        target = Assert.IsType<Entity>(session.Client.State.Entities.Get(43));
        // Original commonTick moves this earlier-enrolled player outside the opening slab before the source ticks.
        Assert.Equal(start.Add(.25, 0, 0), target.Position);
        Assert.Equal(end, session.Client.State.PacketBases.Get(target));
        Assert.True(Known(session, target));
        await Peek(session, 0);
        foreach (double offset in new[] { .625, .875, 1D })
        {
            await Tick(session);
            Assert.Equal(start.Add(offset, 0, 0), target.Position);
            Assert.Equal(end, session.Client.State.PacketBases.Get(target));
            Assert.True(Known(session, target));
        }
        await Disconnect(session);
    }

    [Fact]
    public async Task RemovalReplacementAndReusedSlotsRetainNativeTickEnrollment()
    {
        await using Session session = await Session.Create(777, "stone", physics: false);
        await SpawnSource(session, Direction.West);
        Vec3d point = TargetPosition(Direction.West);
        await SpawnPlayer(session, point);
        await Tick(session);
        Entity old = session.Client.State.Entities.Get(43)!;
        await Send(session, new ClientboundRemoveEntitiesPacket([43]));
        await SpawnPlayer(session, point);
        Assert.NotSame(old, session.Client.State.Entities.Get(43));
        await Peek(session, 100);
        await Tick(session);
        Assert.Equal(point, session.Client.State.Entities.Get(43)!.Position);
        await Disconnect(session);
        await session.Restart("stone");
        await SpawnPlayer(session, new Vec3d(12.5, 70, 12.5), id: 44);
        await SpawnSource(session, Direction.West);
        await SpawnPlayer(session, point);
        await Tick(session);
        await Send(session, new ClientboundRemoveEntitiesPacket([44]));
        await SpawnPlayer(session, point, id: 45);
        await Peek(session, 100);
        await Tick(session);
        // The freed Dictionary slot precedes the source, but native enrollment appends this new player's constructor-noPhysics tick after it.
        Assert.Equal(point, session.Client.State.Entities.Get(45)!.Position);
        Assert.Equal(point.Add(OpeningVector(Direction.West)), session.Client.State.Entities.Get(43)!.Position);
        await Send(session, new ClientboundRemoveEntitiesPacket([42, 43]));
        await SpawnPlayer(session, point);
        await Tick(session);
        var endpoint = new Vec3d(9.5, 70, 8.5);
        await Send(session, new ClientboundEntityPositionSyncPacket(43, new PositionMoveRotation(endpoint, Vec3d.Zero, 0, 0), false) { Path = new EntityPositionPath.Linear(endpoint) });
        await SpawnSource(session, Direction.West);
        await Peek(session, 100);
        await Tick(session);
        // Native replacement-source enrollment follows the player's first pending interpolation step; the moved body is already outside the first opening slab.
        Assert.Equal(new Vec3d(9.4, 70, 8.5), session.Client.State.Entities.Get(43)!.Position);
        Assert.Equal(endpoint, session.Client.State.PacketBases.Get(session.Client.State.Entities.Get(43)!));
        await Peek(session, 0);
        await Send(session, new ClientboundEntityPositionSyncPacket(43, new PositionMoveRotation(point, Vec3d.Zero, 0, 0), false) { Path = new EntityPositionPath.Linear(point) });
        await Tick(session);
        await Tick(session);
        foreach (Vec3d sourcePoint in new[] { SourcePosition.Add(32, 0, 0), SourcePosition })
            await Send(session, new ClientboundEntityPositionSyncPacket(42, new PositionMoveRotation(sourcePoint, Vec3d.Zero, 0, 0), false) { Path = new EntityPositionPath.Linear(sourcePoint) });
        await Peek(session, 100);
        await Tick(session);
        Assert.False(Known(session, session.Client.State.Entities.Get(43)!));
        await Disconnect(session);
        await session.Restart("stone");
        await SpawnSource(session, Direction.West);
        await SpawnPlayer(session, point);
        await Peek(session, 100);
        await Tick(session);
        Assert.Equal(point, session.Client.State.Entities.Get(43)!.Position);
        await Disconnect(session);
    }

    [Fact]
    public async Task PushedBoundsChangeTheActualPlacementDecisionAndSwingGroup()
    {
        await using Session session = await Session.Create(770, "stone", physics: true);
        await SpawnSource(session, Direction.West);
        await SpawnPlayer(session, TargetPosition(Direction.West));
        await Tick(session);
        await Tick(session);
        var anchor = new BlockPos(10, 69, 8);
        await session.Block(anchor, "stone");
        await session.Hold("stone");
        await AssertPlacement(BlockUseResult.Success, expectedPackets: 2);
        await Peek(session, 100);
        for (int i = 0; i < 20; i++)
            await Tick(session);
        await AssertPlacement(BlockUseResult.Fail, expectedPackets: 1);
        await Disconnect(session);

        async Task AssertPlacement(BlockUseResult expected, int expectedPackets)
        {
            BlockUsePrediction prediction = await session.Client.InvokeAsync(client => client.Actions.Interaction.PredictBlockUse(anchor, Direction.Up, new Vec3d(.5, 1, .5), Hand.Main), session.Token);
            Assert.Equal(expected, prediction.Result);
            Task action = session.Client.Actions.Interaction.PlaceBlockAsync(anchor, Direction.Up, new Vec3d(.5, 1, .5), Hand.Main, ct: session.Token);
            await session.Client.InvokeAsync(_ => true, session.Token);
            List<IPacket> packets = await session.Barrier();
            Assert.Equal(expectedPackets, packets.Count);
            var use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
            Assert.Equal(anchor, use.Position);
            Assert.Equal((int)Direction.Up, use.Face);
            Assert.Equal((int)Hand.Main, use.Hand);
            Assert.Equal((.5F, 1F, .5F), (use.CursorX, use.CursorY, use.CursorZ));
            Assert.True(use.Sequence > 0);
            if (expectedPackets == 2)
                Assert.Equal((int)Hand.Main, Assert.IsType<ServerboundSwingPacket>(packets[1]).Hand);
            Assert.False(action.IsCompleted);
            await session.Send(new ClientboundBlockChangedAckPacket(use.Sequence));
            await action.WaitAsync(session.Token);
            Assert.Empty(await session.Barrier());
        }
    }

    [Fact]
    public async Task UnknownLocalPushSurvivesSourceRemovalUntilAbsoluteCorrection()
    {
        var target = new BlockPos(9, 71, 11);
        await using (Session noPhysics = await Session.Create(770, "stone", physics: false))
        {
            await PrepareViewRoom(noPhysics, target);
            await AssertViewMiningStartsAndCancels(noPhysics, target);
            await Disconnect(noPhysics);
        }

        await using Session session = await Session.Create(770, "stone", physics: true);
        await PrepareViewRoom(session, target);
        await SpawnSource(session, Direction.West);
        await Tick(session);
        await Peek(session, 100);
        await Tick(session);
        await Send(session, new ClientboundRemoveEntitiesPacket([42]));
        Assert.Empty(await session.Client.Snapshots.EntitiesAsync(session.Token));
        // The block-only API retains its contract; a held view attack also needs a proven self origin.
        Assert.Equal(target, (await session.Client.Snapshots.RaycastViewAsync(ct: session.Token))?.BlockPos);
        using (var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token))
        {
            int before = session.ObservedOutbound.Count;
            Task mining = session.Client.Actions.Interaction.DigBlockInViewAsync(target, ct: caller.Token);
            try
            {
                await session.Client.InvokeAsync(_ => true, session.Token);
                await session.Tick();
                await session.Barrier();
                Assert.Empty(MiningPackets(session, before));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(TimeSpan.FromSeconds(2), session.Token));
            }
            finally
            {
                caller.Cancel();
                if (!mining.IsCompleted)
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(session.Token));
                await session.Barrier();
            }
        }
        await CorrectSelf(session, TargetPosition(Direction.West), 3);
        await Tick(session);
        Assert.Equal(TargetPosition(Direction.West), (await session.Client.Snapshots.SelfAsync(session.Token)).Position);
        Assert.Equal((double)1.62F, (await session.Client.Snapshots.SelfAsync(session.Token)).EyeHeight);
        Assert.Empty(await session.Client.Snapshots.EntitiesAsync(session.Token));
        await AssertViewMiningStartsAndCancels(session, target);
        await Disconnect(session);
    }

    private static async Task PrepareViewRoom(Session session, BlockPos target)
    {
        // Use the native push fixture's standing target, with a loaded stone floor and explicit air above it before the owned pose phase.
        for (int x = 8; x <= 10; x++)
            for (int z = 7; z <= 12; z++)
            {
                await session.Block(new BlockPos(x, 69, z), "stone");
                for (int y = 70; y <= 73; y++)
                    await session.Block(new BlockPos(x, y, z), "air");
            }
        await CorrectSelf(session, TargetPosition(Direction.West), 2);
        await Tick(session);
        var self = await session.Client.Snapshots.SelfAsync(session.Token);
        Assert.Equal(TargetPosition(Direction.West), self.Position);
        Assert.Equal((0F, 0F), (self.Yaw, self.Pitch));
        Assert.Equal((double)1.62F, self.EyeHeight);
        await session.Block(target, "bedrock");
        Assert.Empty(await session.Barrier());
        Assert.True((await session.Client.Snapshots.BlockAsync(target, session.Token)).ChunkLoaded);
        Assert.Equal(Identifier.Minecraft("bedrock"), (await session.Client.Snapshots.BlockAsync(target, session.Token)).State.Block.Id);
        Assert.Equal(target, (await session.Client.Snapshots.RaycastViewAsync(ct: session.Token))?.BlockPos);
    }

    private static async Task CorrectSelf(Session session, Vec3d position, int teleportId)
    {
        await Send(session, new ClientboundPlayerPositionPacket(position.X, position.Y, position.Z, 0, 0, 0, teleportId,
            new PositionMoveRotation(position, Vec3d.Zero, 0, 0)));
        Assert.Equal(position, (await session.Client.Snapshots.SelfAsync(session.Token)).Position);
    }

    private static IPacket[] MiningPackets(Session session, int after)
        => session.ObservedOutbound.Skip(after).Where(packet => packet is ServerboundPlayerActionPacket or ServerboundSwingPacket or ServerboundPunchPacket or ServerboundInteractPacket).ToArray();

    private static async Task AssertViewMiningStartsAndCancels(Session session, BlockPos target)
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        int before = session.ObservedOutbound.Count;
        Task mining = session.Client.Actions.Interaction.DigBlockInViewAsync(target, ct: caller.Token);
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            await session.Tick();
            await session.Barrier();
            IPacket[] packets = MiningPackets(session, before);
            Assert.Equal(3, packets.Length);
            var start = Assert.IsType<ServerboundPlayerActionPacket>(packets[0]);
            Assert.Equal(0, start.Action);
            Assert.Equal(target, start.Position);
            Assert.Equal((byte)Direction.North, start.Direction);
            Assert.True(start.Sequence > 0);
            Assert.Equal((int)Hand.Main, Assert.IsType<ServerboundSwingPacket>(packets[1]).Hand);
            Assert.Equal((int)Hand.Main, Assert.IsType<ServerboundSwingPacket>(packets[2]).Hand);
            Assert.False(mining.IsCompleted);
        }
        finally
        {
            int afterStart = session.ObservedOutbound.Count;
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mining.WaitAsync(session.Token));
            await session.Barrier();
            var abort = Assert.IsType<ServerboundPlayerActionPacket>(Assert.Single(MiningPackets(session, afterStart)));
            Assert.Equal(1, abort.Action);
            Assert.Equal(target, abort.Position);
            Assert.Equal((byte)Direction.Down, abort.Direction);
            Assert.Equal(0, abort.Sequence);
        }
    }

    private static bool Known(Session session, Entity target) => session.Client.State.EntityGeometry.IsPositionKnown(target)
        && session.Client.State.EntityGeometry.TryGet(target, JavaGameData.EntityGeometryData(session.Protocol), out _, out _, session.Protocol);

    private static Vec3d TargetPosition(Direction face) => face.Opposite() switch
    {
        Direction.Up => new(8.5, 71, 8.5),
        Direction.Down => new(8.5, 68.2, 8.5),
        Direction.North => new(8.5, 70, 7.7),
        Direction.South => new(8.5, 70, 9.3),
        Direction.West => new(7.7, 70, 8.5),
        Direction.East => new(9.3, 70, 8.5),
        _ => throw new InvalidOperationException("Unknown native face."),
    };

    private static Vec3d OpeningVector(Direction face)
    {
        Direction direction = face.Opposite();
        return new(OpeningDistance * direction.StepX(), OpeningDistance * direction.StepY(), OpeningDistance * direction.StepZ());
    }

    private static async Task SpawnSource(Session session, Direction face)
    {
        await Spawn(session, 42, "shulker", SourcePosition);
        await Send(session, new ClientboundSetEntityDataPacket(42, new EntityMetadataList([new(16, MetadataValue.Direction(face)), new(17, MetadataValue.Byte(0))], [])));
    }

    private static async Task SpawnPlayer(Session session, Vec3d position, GameMode mode = GameMode.Survival, int id = 43)
    {
        await Send(session, new ClientboundPlayerInfoUpdatePacket(PlayerInfoActions.AddPlayer | PlayerInfoActions.UpdateGameMode,
            [new(PlayerUuid(id), "PushPeer", [], false, null, mode, false, 0, null, 0, false)]));
        await Spawn(session, id, "player", position);
    }

    private static async Task Spawn(Session session, int id, string kind, Vec3d position)
    {
        int type = session.Client.State.Registries!.EntityTypes[Identifier.Minecraft(kind)].NetworkId;
        await Send(session, new ClientboundAddEntityPacket(id, kind == "player" ? PlayerUuid(id) : Guid.Empty, type, position.X, position.Y, position.Z, 0, 0, 0, 0, 0, 0, 0, null));
    }

    private static Guid PlayerUuid(int id) => id == 43 ? PeerUuid : Guid.Parse($"00000000-0000-0000-0000-{id:000000000000}");

    private static Task Peek(Session session, sbyte value) => Send(session, new ClientboundSetEntityDataPacket(42, new EntityMetadataList([new(17, MetadataValue.Byte(value))], [])));

    private static async Task Tick(Session session)
    {
        await session.Tick();
        Assert.Empty(await session.Barrier());
    }

    private static async Task Send(Session session, IPacket packet)
    {
        BoundPacketCodec codec = Support.BoundDescriptorCodec.Clientbound(session.Protocol, packet.Type.Id.Path);
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        byte[] bytes = Encode(codec, packet, context);
        object decoded = codec.Decode(bytes, context);
        switch (packet)
        {
            case ClientboundAddEntityPacket add:
                var actor = Assert.IsType<ClientboundAddEntityPacket>(decoded);
                Assert.Equal((add.EntityId, add.Uuid, add.TypeId), (actor.EntityId, actor.Uuid, actor.TypeId));
                Assert.Equal(new Vec3d(add.X, add.Y, add.Z), new Vec3d(actor.X, actor.Y, actor.Z));
                break;
            case ClientboundPlayerPositionPacket position:
                var selfPosition = Assert.IsType<ClientboundPlayerPositionPacket>(decoded);
                Assert.Equal(position.ModernValues, selfPosition.ModernValues);
                Assert.Equal(position.RelativeFlags, selfPosition.RelativeFlags);
                Assert.Equal(position.TeleportId, selfPosition.TeleportId);
                break;
            case ClientboundSetEntityDataPacket metadata:
                var decodedMetadata = Assert.IsType<ClientboundSetEntityDataPacket>(decoded).Metadata;
                Assert.Equal(metadata.Metadata.Entries.Select(entry => (entry.Index, entry.Value)), decodedMetadata.Entries.Select(entry => (entry.Index, entry.Value)));
                Assert.Equal(metadata.Metadata.RawTail, decodedMetadata.RawTail);
                foreach (EntityDataEntry entry in decodedMetadata.Entries)
                {
                    int nativeSerializer = entry.Value.Kind switch
                    {
                        MetadataValueKind.Byte => 0,
                        MetadataValueKind.Direction => 12,
                        _ => throw new InvalidOperationException("Unclassified native push metadata serializer."),
                    };
                    Assert.Equal(nativeSerializer, entry.WireSerializerId);
                }
                break;
            case ClientboundEntityPositionSyncPacket sync:
                var observed = Assert.IsType<ClientboundEntityPositionSyncPacket>(decoded);
                Assert.Equal(sync.Values, observed.Values);
                if (sync.Path is EntityPositionPath.Stepped path)
                    Assert.Equal(path.Steps, Assert.IsType<EntityPositionPath.Stepped>(observed.Path).Steps);
                else
                    Assert.Equal(sync.Path, observed.Path);
                break;
            case ClientboundSetPassengersPacket passengers:
                Assert.Equal(passengers.Passengers, Assert.IsType<ClientboundSetPassengersPacket>(decoded).Passengers);
                break;
            case ClientboundPlayerInfoUpdatePacket info:
                Assert.Equal(info.Entries[0].GameMode, Assert.IsType<ClientboundPlayerInfoUpdatePacket>(decoded).Entries[0].GameMode);
                break;
            case ClientboundRemoveEntitiesPacket removed:
                Assert.Equal(removed.EntityIds, Assert.IsType<ClientboundRemoveEntitiesPacket>(decoded).EntityIds);
                break;
            case ClientboundTickingStatePacket ticking:
                Assert.Equal(ticking, Assert.IsType<ClientboundTickingStatePacket>(decoded));
                break;
            case ClientboundTickingStepPacket step:
                Assert.Equal(step, Assert.IsType<ClientboundTickingStepPacket>(decoded));
                break;
            case ClientboundInitializeBorderPacket border:
                Assert.Equal(border, Assert.IsType<ClientboundInitializeBorderPacket>(decoded));
                break;
            default:
                throw new InvalidOperationException("Unclassified framed push fixture.");
        }
        Assert.Equal(bytes, Encode(codec, decoded, context));
        await session.Send(packet);
        Assert.Empty(await session.Barrier());
    }

    private static byte[] Encode(BoundPacketCodec codec, object packet, PacketCodecContext context)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(output);
        codec.Encode(ref writer, packet, context);
        return output.WrittenSpan.ToArray();
    }

    private static async Task Disconnect(Session session)
    {
        await session.Client.DisconnectAsync(session.Token);
        Assert.Equal(ClientStatus.Disconnected, session.Client.Status);
        Assert.False(session.Client.State.EntityMembership.HasColumn(Session.Target));
        var closed = await Assert.ThrowsAsync<ConnectionClosedException>(async () => { await session.Server.NextFrameAsync(session.Token); });
        Assert.Equal(CloseReason.SocketEof, closed.Reason);
    }
}
