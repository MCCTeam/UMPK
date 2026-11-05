using System.Buffers;
using Umpk.Client.Actions;
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

public sealed class EndCrystalUsePredictionRuntimeTests
{
    private static readonly Guid PeerUuid = Guid.Parse("e7e3b805-0217-4f78-bc39-837fe152a4af");

    public static IEnumerable<object[]> CatalogCases() => JavaVersions.All.Select(version => version.Version.Protocol).Distinct().Where(protocol => protocol >= 107).Select(protocol => new object[] { protocol });

    [Theory]
    [MemberData(nameof(CatalogCases))]
    public async Task ProvenEmptyNativeQueryUsesBothBasesHandsAndTheNativeSwingGroup(int protocol)
    {
        await using Session session = await Session.Create(protocol, "obsidian", physics: true);
        await EstablishOwnedBody(session);
        foreach (string block in new[] { "obsidian", "bedrock" })
            foreach (Hand hand in new[] { Hand.Main, Hand.Off })
            {
                await session.Block(Session.Target, block);
                if (hand == Hand.Main)
                    await session.Hold("end_crystal");
                else
                    await session.Offhand("end_crystal");
                Direction face = (Direction)((protocol + (int)hand) % 6);
                await AssertPredictionAndUse(session, protocol, hand, BlockUseResult.Success, face);
            }
        await AssertOwnedDisconnect(session);
    }

    [Fact]
    public void RetainedProtocol47ProfileHasNoEndCrystalItem()
    {
        Assert.DoesNotContain(JavaGameData.Registries(47).Items, item => item.Id == Identifier.Minecraft("end_crystal"));
    }

    public static IEnumerable<object[]> ContextCases()
    {
        yield return [107, "cooldown"];
        yield return [340, "adventure"];
        yield return [340, "replaceable-above"];
        yield return [340, "replaceable-upper"];
        yield return [340, "solid-above"];
        yield return [340, "solid-upper"];
        yield return [340, "wrong-base"];
        yield return [393, "adventure"];
        yield return [393, "replaceable-above"];
        yield return [393, "solid-above"];
        yield return [393, "solid-upper"];
        yield return [754, "custom"];
        yield return [755, "unload"];
        yield return [766, "custom"];
        yield return [777, "cooldown"];
        yield return [777, "missing-terrain"];
        yield return [777, "physics-off"];
        yield return [777, "respawn"];
        yield return [777, "unload"];
    }

    [Theory]
    [MemberData(nameof(ContextCases))]
    public async Task ClearancePermissionAndMissingContextKeepTheirNativePhase(int protocol, string context)
    {
        await using Session session = await Session.Create(protocol, context == "wrong-base" ? "stone" : "obsidian", terrain: context != "missing-terrain", physics: context != "physics-off");
        if (context is not ("missing-terrain" or "physics-off"))
            await EstablishOwnedBody(session);
        await session.Hold("end_crystal", custom: context == "custom");
        if (context is "solid-above" or "replaceable-above")
            await session.Block(Session.Target.Above(), context == "solid-above" ? "stone" : "vine");
        if (context is "solid-upper" or "replaceable-upper")
            await session.Block(Session.Target.Above().Above(), context == "solid-upper" ? "stone" : "vine");
        if (context == "adventure")
            await session.Mode(GameMode.Adventure, instantBuild: false);
        if (context == "cooldown")
            await session.Cooldown();
        if (context == "unload")
        {
            await session.Forget();
            await session.Block(Session.Target, "obsidian");
            Assert.Empty(await session.Barrier());
            Assert.NotNull(session.Client.State.World.GetColumn(Session.Target));
            Assert.False(session.Client.State.EntityMembership.HasColumn(Session.Target));
        }
        if (context == "respawn")
        {
            await session.Respawn();
            await session.Block(Session.Target, "obsidian");
            Assert.Empty(await session.Barrier());
            Assert.False(session.Client.State.EntityMembership.HasColumn(Session.Target));
        }
        BlockUseResult expected = context switch
        {
            "wrong-base" or "solid-above" => BlockUseResult.Fail,
            "solid-upper" => protocol < 393 ? BlockUseResult.Fail : BlockUseResult.Success,
            "replaceable-above" => protocol < 393 ? BlockUseResult.Success : BlockUseResult.Fail,
            "replaceable-upper" => BlockUseResult.Success,
            "adventure" => protocol < 393 ? BlockUseResult.Fail : BlockUseResult.Pass,
            "cooldown" => BlockUseResult.Pass,
            "custom" when protocol < 766 => BlockUseResult.Success,
            _ => BlockUseResult.Unknown,
        };
        await AssertPredictionAndUse(session, protocol, Hand.Main, expected);
        await AssertOwnedDisconnect(session);
    }

    public static IEnumerable<object[]> QueryCases()
    {
        yield return [755, "far-item"];
        yield return [755, "far-player"];
        yield return [755, "near-item"];
        yield return [755, "near-player"];
        yield return [755, "self"];
        yield return [755, "spectator-player"];
        yield return [769, "far-xp"];
        yield return [769, "near-xp"];
        yield return [770, "far-xp"];
        yield return [770, "near-xp"];
        yield return [777, "near-player"];
        yield return [777, "spectator-player"];
        yield return [777, "unowned-actor"];
    }

    [Theory]
    [MemberData(nameof(QueryCases))]
    public async Task NullSourceQueryIncludesSelfAndNeverSkipsUnownedNonbuildingActors(int protocol, string context)
    {
        await using Session session = await Session.Create(protocol, "obsidian", physics: true);
        await EstablishOwnedBody(session);
        await session.Hold("end_crystal");
        Vec3d point = context.StartsWith("far-", StringComparison.Ordinal) ? new(8.5, 66, 8.5) : new(1.5, 66, 1.5);
        if (context == "self")
        {
            await session.Send(new ClientboundPlayerPositionPacket(point.X, point.Y, point.Z, 0, 0, 0, 2, new PositionMoveRotation(point, Vec3d.Zero, 0, 0)));
            Assert.Empty(await session.Barrier());
            Assert.Equal(point, session.Client.State.Self.Position);
            Assert.Null(session.Client.State.Entities.Get(session.Client.State.Self.EntityId));
        }
        else
        {
            bool player = context.EndsWith("player", StringComparison.Ordinal);
            string kind = player ? "player" : context.EndsWith("xp", StringComparison.Ordinal) ? "experience_orb" : context.EndsWith("item", StringComparison.Ordinal) ? "item" : "armor_stand";
            if (player)
            {
                GameMode mode = context == "spectator-player" ? GameMode.Spectator : GameMode.Survival;
                IPacket info = protocol < 761
                    ? new ClientboundLegacyPlayerListItemPacket(LegacyPlayerListAction.AddPlayer, [new(PeerUuid, "CrystalPeer", [], (int)mode, 0, null)])
                    : new ClientboundPlayerInfoUpdatePacket(PlayerInfoActions.AddPlayer | PlayerInfoActions.UpdateGameMode, [new(PeerUuid, "CrystalPeer", [], false, null, mode, false, 0, null, 0, false)]);
                await session.Send(info);
                Assert.Empty(await session.Barrier());
                Assert.True(session.Client.State.TabList.TryGet(PeerUuid, out var observed));
                Assert.Equal(mode, observed.GameMode);
            }
            if (kind is "item" or "experience_orb")
                Assert.True(JavaGameData.BlockInteractionData(protocol).IsAlwaysNonObstructingEntity(Identifier.Minecraft(kind)));
            IPacket add = player && protocol < 764
                ? new ClientboundAddPlayerPacket(42, PeerUuid, point.X, point.Y, point.Z, 0, 0, 0, new EntityMetadataList([], []))
                : kind == "experience_orb" && protocol < 770
                    ? new ClientboundAddExperienceOrbPacket(42, point.X, point.Y, point.Z, 5)
                    : new ClientboundAddEntityPacket(42, player ? PeerUuid : Guid.Empty, session.Client.State.Registries!.EntityTypes[Identifier.Minecraft(kind)].NetworkId, point.X, point.Y, point.Z, 0, 0, 0, 0, 0, 0, 0, null);
            await SendEntity(session, protocol, add);
            Entity actor = Assert.IsType<Entity>(session.Client.State.Entities.Get(42));
            Assert.Equal(Identifier.Minecraft(kind), actor.Type.Id);
            Assert.Equal(point, actor.Position);
        }
        BlockUseResult expected = context is "self" or "near-player" ? BlockUseResult.Fail
            : context is "far-player" or "spectator-player" ? BlockUseResult.Success
            : BlockUseResult.Unknown;
        await AssertPredictionAndUse(session, protocol, Hand.Main, expected);
        if (context != "self")
        {
            await SendEntity(session, protocol, new ClientboundRemoveEntitiesPacket([42]));
            Assert.Null(session.Client.State.Entities.Get(42));
            await AssertPredictionAndUse(session, protocol, Hand.Main, BlockUseResult.Success);
        }
        await AssertOwnedDisconnect(session);
    }

    [Theory]
    [InlineData(340)]
    [InlineData(777)]
    public async Task NativeAirOutsideBuildHeightDoesNotInventAPreSendPlacementRejection(int protocol)
    {
        await using Session session = await Session.Create(protocol, "obsidian", physics: true);
        await EstablishOwnedBody(session);
        await session.Hold("end_crystal");
        var target = new BlockPos(1, session.Client.State.World.Dimension.MaxY - 1, 1);
        await session.Block(target, "obsidian");
        await AssertPredictionAndUse(session, protocol, Hand.Main, BlockUseResult.Success, Direction.Down, target);
        await AssertOwnedDisconnect(session);
    }

    [Theory]
    [InlineData(107)]
    [InlineData(754)]
    public async Task LegacyLocalBodyDoesNotInventUnownedChunkListMembership(int protocol)
    {
        await using Session session = await Session.Create(protocol, "obsidian", physics: true);
        await EstablishOwnedBody(session);
        await session.Hold("end_crystal");
        var point = new Vec3d(1.5, 66, 1.5);
        await session.Send(new ClientboundPlayerPositionPacket(point.X, point.Y, point.Z, 0, 0, 0, 2, new PositionMoveRotation(point, Vec3d.Zero, 0, 0)));
        Assert.Empty(await session.Barrier());
        Assert.Equal(point, session.Client.State.Self.Position);
        Assert.Null(session.Client.State.Entities.Get(session.Client.State.Self.EntityId));
        await AssertPredictionAndUse(session, protocol, Hand.Main, BlockUseResult.Unknown);
        await AssertOwnedDisconnect(session);
    }

    private static async Task SendEntity(Session session, int protocol, IPacket packet)
    {
        BoundPacketCodec codec = Support.BoundDescriptorCodec.Clientbound(protocol, packet.Type.Id.Path);
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        byte[] bytes = Encode(codec, packet, context);
        object decoded = codec.Decode(bytes, context);
        switch (packet)
        {
            case ClientboundAddEntityPacket add:
                var observed = Assert.IsType<ClientboundAddEntityPacket>(decoded);
                Assert.Equal((add.EntityId, add.Uuid, add.TypeId), (observed.EntityId, observed.Uuid, observed.TypeId));
                Assert.Equal(new Vec3d(add.X, add.Y, add.Z), new Vec3d(observed.X, observed.Y, observed.Z));
                break;
            case ClientboundAddPlayerPacket player:
                var received = Assert.IsType<ClientboundAddPlayerPacket>(decoded);
                Assert.Equal((player.EntityId, player.Uuid), (received.EntityId, received.Uuid));
                Assert.Equal(new Vec3d(player.X, player.Y, player.Z), new Vec3d(received.X, received.Y, received.Z));
                break;
            case ClientboundAddExperienceOrbPacket orb:
                Assert.Equal(orb, Assert.IsType<ClientboundAddExperienceOrbPacket>(decoded));
                break;
            case ClientboundRemoveEntitiesPacket remove:
                Assert.Equal(remove.EntityIds, Assert.IsType<ClientboundRemoveEntitiesPacket>(decoded).EntityIds);
                break;
            default:
                throw new InvalidOperationException("Unclassified entity fixture packet.");
        }
        Assert.Equal(bytes, Encode(codec, decoded, context));
        await session.Send(packet);
        Assert.Empty(await session.Barrier());
    }

    private static async Task AssertPredictionAndUse(Session session, int protocol, Hand hand, BlockUseResult expected, Direction face = Direction.Up, BlockPos? target = null)
    {
        BlockPos position = target ?? Session.Target;
        Assert.Empty(await session.Barrier());
        ItemStack stack = session.Client.State.Inventory.GetSlot(hand == Hand.Main ? 36 : 45);
        int state = session.Client.State.World.GetBlock(position).StateId;
        Vec3d hit = new(.25, .75, .5);
        BlockUsePrediction prediction = await session.Client.InvokeAsync(client => client.Actions.Interaction.PredictBlockUse(position, face, hit, hand), session.Token);
        Assert.True(prediction.SendPacket);
        Assert.Equal(expected, prediction.Result);
        BlockUseSwing swing = expected == BlockUseResult.Success ? protocol == 777 ? BlockUseSwing.Local : BlockUseSwing.Packet : BlockUseSwing.None;
        Assert.Equal(swing, prediction.Swing);
        Task action = session.Client.Actions.Interaction.PlaceBlockAsync(position, face, hit, hand, insideBlock: true, ct: session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
        List<IPacket> packets = await session.Barrier();
        Assert.Equal(swing == BlockUseSwing.Packet ? 2 : 1, packets.Count);
        var use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
        Assert.Equal((position, (int)face, (int)hand), (use.Position, use.Face, use.Hand));
        Assert.Equal(((float)hit.X, (float)hit.Y, (float)hit.Z), (use.CursorX, use.CursorY, use.CursorZ));
        Assert.Equal(protocol >= 477, use.Inside);
        Assert.False(use.WorldBorderHit);
        if (protocol >= 759)
            Assert.True(use.Sequence > 0);
        else
            Assert.Equal(0, use.Sequence);
        if (swing == BlockUseSwing.Packet)
            Assert.Equal((int)hand, Assert.IsType<ServerboundSwingPacket>(packets[1]).Hand);
        Assert.DoesNotContain(packets, packet => packet is ServerboundPunchPacket);
        if (protocol >= 759)
        {
            Assert.False(action.IsCompleted);
            await session.Send(new ClientboundBlockChangedAckPacket(use.Sequence));
        }
        await action.WaitAsync(session.Token);
        Assert.Empty(await session.Barrier());
        Assert.Equal(stack, session.Client.State.Inventory.GetSlot(hand == Hand.Main ? 36 : 45));
        Assert.Equal(state, session.Client.State.World.GetBlock(position).StateId);
    }

    private static async Task EstablishOwnedBody(Session session)
    {
        Assert.True(session.Client.State.Features.Physics);
        Assert.True(session.Client.State.EntityMembership.HasColumn(Session.Target));
        await session.Tick();
        Assert.Empty(await session.Barrier());
        await session.Tick();
        Assert.Empty(await session.Barrier());
        Assert.True(session.Client.State.Self.OnGround);
        Assert.Equal(new Vec3d(2.5, 65, 2.5), session.Client.State.Self.Position);
    }

    private static async Task AssertOwnedDisconnect(Session session)
    {
        await session.Client.DisconnectAsync(session.Token);
        Assert.Equal(ClientStatus.Disconnected, session.Client.Status);
        Assert.False(session.Client.State.EntityMembership.HasColumn(Session.Target));
        var closed = await Assert.ThrowsAsync<ConnectionClosedException>(async () => { await session.Server.NextFrameAsync(session.Token); });
        Assert.Equal(CloseReason.SocketEof, closed.Reason);
    }

    private static byte[] Encode(BoundPacketCodec codec, object packet, PacketCodecContext context)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(output);
        codec.Encode(ref writer, packet, context);
        return output.WrittenSpan.ToArray();
    }
}
