using System.Buffers;
using Umpk.Client.Actions;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Game.Entities;
using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class SpectatorPlacementRuntimeTests
{
    private static readonly Guid PeerId = Guid.Parse("413f8370-28fc-4f8e-a4df-cf636cdbb716");
    private static readonly Guid VehicleId = Guid.Parse("41acff65-e606-42a6-b999-b5dfe986dd81");
    private static readonly int[] Protocols = [498, 760, 761, 764, 770, 777];
    private static readonly int[] CachedProtocols = [761, 764, 770, 777];

    public static IEnumerable<object[]> MountedCases() => Protocols.SelectMany(protocol => new[] { "spectator", "survival", "missing" }.Select(info => new object[] { protocol, info }));

    [Theory]
    [MemberData(nameof(MountedCases))]
    public async Task KnownSpectatorDoesNotRequireUnsupportedRiderBounds(int protocol, string info)
    {
        await using BlockUseSession session = await BlockUseSession.Create(protocol, "stone", physics: true);
        await session.Hold("stone");
        await Spawn(session, protocol, 42, PeerId, new(1.5, 66, 1.5));
        if (info != "missing")
            await Info(session, protocol, info == "spectator" ? GameMode.Spectator : GameMode.Survival);
        if (protocol >= 761 && info != "missing")
            await AssertUse(session, protocol, info == "spectator" ? BlockUseResult.Success : BlockUseResult.Fail);
        await Mount(session, protocol);

        await AssertUse(session, protocol, info == "spectator" ? BlockUseResult.Success : BlockUseResult.Unknown);

        await AssertUnchanged(session);
    }

    public static IEnumerable<object[]> DetachedCacheCases() => CachedProtocols.SelectMany(protocol => new[] { false, true }.Select(spectator => new object[] { protocol, spectator }));

    [Theory]
    [MemberData(nameof(DetachedCacheCases))]
    public async Task MountedPlacementRetainsTheNativeCachedInfoObject(int protocol, bool spectator)
    {
        await using BlockUseSession session = await BlockUseSession.Create(protocol, "stone", physics: true);
        await session.Hold("stone");
        await Spawn(session, protocol, 42, PeerId, new(1.5, 66, 1.5));
        await Info(session, protocol, spectator ? GameMode.Spectator : GameMode.Survival);
        await AssertUse(session, protocol, spectator ? BlockUseResult.Success : BlockUseResult.Fail);
        await Send(session, protocol, new ClientboundPlayerInfoRemovePacket([PeerId]));
        await Info(session, protocol, spectator ? GameMode.Survival : GameMode.Spectator);
        await Mount(session, protocol);

        await AssertUse(session, protocol, spectator ? BlockUseResult.Success : BlockUseResult.Unknown);

        Assert.Equal(spectator ? GameMode.Survival : GameMode.Spectator, Assert.Single(session.Client.State.TabList.Entries).GameMode);
        await AssertUnchanged(session);
    }

    public static IEnumerable<object[]> UncachedCases() => CachedProtocols.SelectMany(protocol => new[] { "spectator", "survival", "missing" }.Select(info => new object[] { protocol, info }));

    [Theory]
    [MemberData(nameof(UncachedCases))]
    public async Task UnresolvedRiderDoesNotInitializeTheModernNativeInfoCache(int protocol, string info)
    {
        await using BlockUseSession session = await BlockUseSession.Create(protocol, "stone", physics: true);
        await session.Hold("stone");
        await Spawn(session, protocol, 42, PeerId, new(1.5, 66, 1.5));
        if (info != "missing")
            await Info(session, protocol, info == "spectator" ? GameMode.Spectator : GameMode.Survival);
        await Mount(session, protocol);

        await AssertUse(session, protocol, BlockUseResult.Unknown);

        await Send(session, protocol, new ClientboundPlayerInfoRemovePacket([PeerId]));
        GameMode replacement = info == "spectator" ? GameMode.Survival : GameMode.Spectator;
        await Info(session, protocol, replacement);
        Assert.Equal(replacement == GameMode.Spectator, await session.Client.InvokeAsync(client => client.State.EntityMotion.IsSpectator(client.State.Entities.Get(42)!, client.State.TabList), session.Token));
        await AssertUnchanged(session);
    }

    private static readonly BlockPos MiningTarget = new(2, 66, 5);

    [Theory]
    [MemberData(nameof(MountedCases))]
    public async Task KnownMountedSpectatorDoesNotCancelHeldOrInitialViewMining(int protocol, string info)
    {
        await using BlockUseSession session = await PrepareMining(protocol);
        await AssertMiningLifetime(session, protocol, info == "spectator", async () =>
        {
            await Spawn(session, protocol, 42, PeerId, new(1.5, 66, 1.5));
            if (info != "missing")
                await Info(session, protocol, info == "spectator" ? GameMode.Spectator : GameMode.Survival);
            if (protocol >= 761 && info != "missing")
                await PrimePlayerInfoFromPlacement(session, info == "spectator");
            await Mount(session, protocol);
        });
    }

    [Theory]
    [MemberData(nameof(DetachedCacheCases))]
    public async Task MountedViewMiningRetainsTheNativeCachedInfoObject(int protocol, bool spectator)
    {
        await using BlockUseSession session = await PrepareMining(protocol);
        await AssertMiningLifetime(session, protocol, spectator, async () =>
        {
            await Spawn(session, protocol, 42, PeerId, new(1.5, 66, 1.5));
            await Info(session, protocol, spectator ? GameMode.Spectator : GameMode.Survival);
            await PrimePlayerInfoFromPlacement(session, spectator);
            await Send(session, protocol, new ClientboundPlayerInfoRemovePacket([PeerId]));
            await Info(session, protocol, spectator ? GameMode.Survival : GameMode.Spectator);
            await Mount(session, protocol);
        });
        Assert.Equal(spectator ? GameMode.Survival : GameMode.Spectator, Assert.Single(session.Client.State.TabList.Entries).GameMode);
        Assert.Equal(spectator, await session.Client.InvokeAsync(client => client.State.EntityMotion.IsKnownSpectator(client.State.Entities.Get(42)!, client.State.TabList), session.Token));
    }

    [Theory]
    [MemberData(nameof(UncachedCases))]
    public async Task MountedViewMiningDoesNotInitializeTheModernNativeInfoCache(int protocol, string info)
    {
        await using BlockUseSession session = await PrepareMining(protocol);
        await Spawn(session, protocol, 42, PeerId, new(1.5, 66, 1.5));
        if (info != "missing")
            await Info(session, protocol, info == "spectator" ? GameMode.Spectator : GameMode.Survival);
        await Mount(session, protocol);
        Assert.False(await session.Client.InvokeAsync(client => client.State.EntityMotion.IsKnownSpectator(client.State.Entities.Get(42)!, client.State.TabList), session.Token));
        int beforePackets = session.ObservedOutbound.Count;
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task initial = session.Client.Actions.Interaction.DigBlockInViewAsync(MiningTarget, ct: caller.Token);
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => initial.WaitAsync(session.Token));
            await session.Client.InvokeAsync(_ => true, session.Token);
            Assert.Empty(await session.Barrier());
            Assert.False(await session.Client.InvokeAsync(client => client.State.EntityMotion.IsKnownSpectator(client.State.Entities.Get(42)!, client.State.TabList), session.Token));
            Assert.DoesNotContain(session.ObservedOutbound.Skip(beforePackets), packet => packet is ServerboundPlayerActionPacket or ServerboundSwingPacket or ServerboundPunchPacket or ServerboundInteractPacket);
            await Send(session, protocol, new ClientboundPlayerInfoRemovePacket([PeerId]));
            GameMode replacement = info == "spectator" ? GameMode.Survival : GameMode.Spectator;
            await Info(session, protocol, replacement);
            Assert.Equal(replacement == GameMode.Spectator, await session.Client.InvokeAsync(client => client.State.EntityMotion.IsSpectator(client.State.Entities.Get(42)!, client.State.TabList), session.Token));
            Assert.Empty(await MiningTick(session));
            Assert.False(session.Client.State.World.GetBlock(MiningTarget).IsAir);
            Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
            Assert.Equal(MiningTarget, (await session.Client.Snapshots.RaycastViewAsync(ct: session.Token))?.BlockPos);
        }
        finally
        {
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => initial.WaitAsync(session.Token));
        }
    }

    private static async Task<BlockUseSession> PrepareMining(int protocol)
    {
        BlockUseSession session = await BlockUseSession.Create(protocol, "stone", physics: true);
        try
        {
            await session.Hold("stone");
            await session.Block(MiningTarget, "stone");
            await session.Tick();
            Assert.Empty(await session.Barrier());
            Assert.True(session.Client.State.Self.OnGround);
            Assert.Equal(new Vec3d(2.5, 65, 2.5), session.Client.State.Self.Position);
            Assert.True(session.Client.State.EntityMembership.HasColumn(MiningTarget));
            Assert.Equal(MiningTarget, (await session.Client.Snapshots.RaycastViewAsync(ct: session.Token))?.BlockPos);
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    private static async Task PrimePlayerInfoFromPlacement(BlockUseSession session, bool spectator)
    {
        BlockUsePrediction prediction = await session.Client.InvokeAsync(client => client.Actions.Interaction.PredictBlockUse(BlockUseSession.Target, Direction.Up, new(.5, 1, .5), Hand.Main), session.Token);
        Assert.Equal(spectator ? BlockUseResult.Success : BlockUseResult.Fail, prediction.Result);
        Assert.Equal(spectator, await session.Client.InvokeAsync(client => client.State.EntityMotion.IsKnownSpectator(client.State.Entities.Get(42)!, client.State.TabList), session.Token));
    }

    private static async Task AssertMiningLifetime(BlockUseSession session, int protocol, bool continueMining, Func<Task> mount)
    {
        using var heldCaller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task held = session.Client.Actions.Interaction.DigBlockInViewAsync(MiningTarget, ct: heldCaller.Token);
        try
        {
            await session.Client.InvokeAsync(_ => true, session.Token);
            AssertMiningStarted(await MiningTick(session), protocol);
            await mount();
            List<IPacket> afterMount = await MiningTick(session);
            if (continueMining)
            {
                AssertMiningContinued(afterMount, protocol);
                Assert.False(held.IsCompleted);
                AssertMiningContinued(await MiningTick(session), protocol);
                heldCaller.Cancel();
                AssertMiningAborted(await MiningTick(session), protocol);
            }
            else
                AssertMiningAborted(afterMount, protocol);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => held.WaitAsync(session.Token));
            Assert.Empty(await MiningTick(session));
        }
        finally
        {
            heldCaller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => held.WaitAsync(session.Token));
        }
        using var initialCaller = CancellationTokenSource.CreateLinkedTokenSource(session.Token);
        Task initial = session.Client.Actions.Interaction.DigBlockInViewAsync(MiningTarget, ct: initialCaller.Token);
        try
        {
            if (continueMining)
            {
                await session.Client.InvokeAsync(_ => true, session.Token);
                AssertMiningStarted(await MiningTick(session), protocol);
                AssertMiningContinued(await MiningTick(session), protocol);
                Assert.False(initial.IsCompleted);
                initialCaller.Cancel();
                AssertMiningAborted(await MiningTick(session), protocol);
            }
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => initial.WaitAsync(session.Token));
            Assert.Empty(await MiningTick(session));
        }
        finally
        {
            initialCaller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => initial.WaitAsync(session.Token));
        }
        Assert.False(session.Client.State.World.GetBlock(MiningTarget).IsAir);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
        Assert.Equal(MiningTarget, (await session.Client.Snapshots.RaycastViewAsync(ct: session.Token))?.BlockPos);
    }

    private static async Task<List<IPacket>> MiningTick(BlockUseSession session)
    {
        int beforePackets = session.ObservedOutbound.Count;
        long beforeTick = session.Client.State.SessionTick;
        await session.Tick();
        await session.Barrier();
        Assert.Equal(beforeTick + 1, session.Client.State.SessionTick);
        return session.ObservedOutbound.Skip(beforePackets).Where(packet => packet is ServerboundPlayerActionPacket or ServerboundSwingPacket or ServerboundPunchPacket or ServerboundInteractPacket).ToList();
    }

    private static void AssertMiningStarted(IReadOnlyList<IPacket> packets, int protocol)
    {
        Assert.Equal(3, packets.Count);
        var start = Assert.IsType<ServerboundPlayerActionPacket>(packets[0]);
        Assert.Equal(0, start.Action);
        Assert.Equal(MiningTarget, start.Position);
        Assert.Equal((byte)Direction.North, start.Direction);
        if (protocol >= 759)
            Assert.True(start.Sequence > 0);
        else
            Assert.Null(start.Sequence);
        AssertMiningSwing(packets[1], protocol);
        AssertMiningSwing(packets[2], protocol);
    }

    private static void AssertMiningContinued(IReadOnlyList<IPacket> packets, int protocol)
    {
        Assert.Equal(0, packets.Count(packet => packet is ServerboundPlayerActionPacket));
        AssertMiningSwing(Assert.Single(packets), protocol);
    }

    private static void AssertMiningAborted(IReadOnlyList<IPacket> packets, int protocol)
    {
        var abort = Assert.IsType<ServerboundPlayerActionPacket>(Assert.Single(packets));
        Assert.Equal(protocol == 777 ? 2 : 1, abort.Action);
        Assert.Equal(MiningTarget, abort.Position);
        Assert.Equal((byte)Direction.Down, abort.Direction);
        if (protocol >= 759)
            Assert.Equal(0, abort.Sequence);
        else
            Assert.Null(abort.Sequence);
    }

    private static void AssertMiningSwing(IPacket packet, int protocol)
    {
        if (protocol == 777)
            Assert.IsType<ServerboundPunchPacket>(packet);
        else
            Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(packet).Hand);
    }

    private static async Task Mount(BlockUseSession session, int protocol)
    {
        await Spawn(session, protocol, 43, VehicleId, new(8.5, 66, 1.5));
        await Send(session, protocol, new ClientboundSetPassengersPacket(43, [42]));
        Assert.Empty(await session.Barrier());
        await session.Client.InvokeAsync(client =>
        {
            Entity actor = Assert.IsType<Entity>(client.State.Entities.Get(42));
            Entity vehicle = Assert.IsType<Entity>(client.State.Entities.Get(43));
            Assert.Same(vehicle, actor.Vehicle);
            Assert.Same(actor, Assert.Single(vehicle.Passengers));
            Assert.False(client.State.EntityMotion.IsRiderGeometrySupported(actor));
            Assert.True(client.State.EntityGeometry.IsPositionKnown(actor));
            Assert.True(client.State.EntityMembership.IsWorldQueryMember(actor));
            return true;
        }, session.Token);
    }

    private static Task Spawn(BlockUseSession session, int protocol, int id, Guid uuid, Vec3d position)
    {
        IPacket spawn = protocol < 764
            ? new ClientboundAddPlayerPacket(id, uuid, position.X, position.Y, position.Z, 0, 0, 0, new EntityMetadataList([], []))
            : new ClientboundAddEntityPacket(id, uuid, session.Client.State.Registries!.EntityTypes[Identifier.Minecraft("player")].NetworkId, position.X, position.Y, position.Z, 0, 0, 0, 0, 0, 0, 0, null);
        return Send(session, protocol, spawn);
    }

    private static async Task Info(BlockUseSession session, int protocol, GameMode mode)
    {
        IPacket info = protocol < 761
            ? new ClientboundLegacyPlayerListItemPacket(LegacyPlayerListAction.AddPlayer, [new(PeerId, "SpectatorPeer", [], (int)mode, 0, null)])
            : new ClientboundPlayerInfoUpdatePacket(PlayerInfoActions.AddPlayer | PlayerInfoActions.UpdateGameMode, [new(PeerId, "SpectatorPeer", [], false, null, mode, false, 0, null, 0, true)]);
        await Send(session, protocol, info);
        Assert.Empty(await session.Barrier());
        Assert.Equal(mode, Assert.Single(session.Client.State.TabList.Entries).GameMode);
    }

    private static async Task AssertUse(BlockUseSession session, int protocol, BlockUseResult result)
    {
        Assert.Empty(await session.Barrier());
        BlockUsePrediction prediction = await session.Client.InvokeAsync(client => client.Actions.Interaction.PredictBlockUse(BlockUseSession.Target, Direction.Up, new(.5, 1, .5), Hand.Main), session.Token);
        Assert.True(prediction.SendPacket);
        Assert.Equal(result, prediction.Result);
        Assert.Equal(result == BlockUseResult.Success ? protocol == 777 ? BlockUseSwing.Local : BlockUseSwing.Packet : BlockUseSwing.None, prediction.Swing);
        List<IPacket> packets = await session.Use(Hand.Main);
        Assert.Equal(result == BlockUseResult.Success && protocol != 777 ? 2 : 1, packets.Count);
        var use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
        Assert.Equal((BlockUseSession.Target, (int)Direction.Up, 0), (use.Position, use.Face, use.Hand));
        Assert.Equal((.5F, 1F, .5F), (use.CursorX, use.CursorY, use.CursorZ));
        if (packets.Count == 2)
            Assert.Equal(0, Assert.IsType<ServerboundSwingPacket>(packets[1]).Hand);
        Assert.DoesNotContain(packets, packet => packet is ServerboundPunchPacket);
    }

    private static async Task AssertUnchanged(BlockUseSession session)
    {
        Assert.True(session.Client.State.World.GetBlock(BlockUseSession.Target.Above()).IsAir);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
        Assert.Empty(await session.Barrier());
    }

    private static async Task Send(BlockUseSession session, int protocol, IPacket packet)
    {
        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(protocol, packet.Type.Id.Path);
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        byte[] bytes = Encode(codec, context, packet);
        object decoded = codec.Decode(bytes, context);
        switch (packet)
        {
            case ClientboundAddPlayerPacket player:
                var observedPlayer = Assert.IsType<ClientboundAddPlayerPacket>(decoded);
                Assert.Equal((player.EntityId, player.Uuid, player.X, player.Y, player.Z), (observedPlayer.EntityId, observedPlayer.Uuid, observedPlayer.X, observedPlayer.Y, observedPlayer.Z));
                break;
            case ClientboundAddEntityPacket generic:
                var observedEntity = Assert.IsType<ClientboundAddEntityPacket>(decoded);
                Assert.Equal((generic.EntityId, generic.Uuid, generic.TypeId, generic.X, generic.Y, generic.Z), (observedEntity.EntityId, observedEntity.Uuid, observedEntity.TypeId, observedEntity.X, observedEntity.Y, observedEntity.Z));
                break;
            case ClientboundSetPassengersPacket passengers:
                var observedPassengers = Assert.IsType<ClientboundSetPassengersPacket>(decoded);
                Assert.Equal(passengers.VehicleId, observedPassengers.VehicleId);
                Assert.Equal(passengers.Passengers, observedPassengers.Passengers);
                break;
            case ClientboundLegacyPlayerListItemPacket legacyInfo:
                var observedLegacy = Assert.IsType<ClientboundLegacyPlayerListItemPacket>(decoded);
                Assert.Equal(legacyInfo.Action, observedLegacy.Action);
                Assert.Equal((PeerId, legacyInfo.Entries[0].GameMode), (Assert.Single(observedLegacy.Entries).ProfileId, observedLegacy.Entries[0].GameMode));
                break;
            case ClientboundPlayerInfoUpdatePacket modernInfo:
                var observedInfo = Assert.IsType<ClientboundPlayerInfoUpdatePacket>(decoded);
                Assert.Equal(modernInfo.Actions, observedInfo.Actions);
                Assert.Equal((PeerId, modernInfo.Entries[0].GameMode), (Assert.Single(observedInfo.Entries).ProfileId, observedInfo.Entries[0].GameMode));
                break;
            case ClientboundPlayerInfoRemovePacket remove:
                Assert.Equal(remove.ProfileIds, Assert.IsType<ClientboundPlayerInfoRemovePacket>(decoded).ProfileIds);
                break;
            default:
                throw new InvalidOperationException("Unexpected spectator regression packet");
        }
        Assert.Equal(bytes, Encode(codec, context, decoded));
        await session.Send(Assert.IsAssignableFrom<IPacket>(decoded));
    }

    private static byte[] Encode(BoundPacketCodec codec, PacketCodecContext context, object packet)
    {
        var bytes = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(bytes);
        codec.Encode(ref writer, packet, context);
        return bytes.WrittenSpan.ToArray();
    }
}
