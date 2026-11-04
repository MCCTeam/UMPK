using System.Buffers;
using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Data.Java;
using Umpk.Game.Blocks;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

using Session = Support.BlockUseSession;

public sealed class LegacyQuartzPlacementSessionRuntimeTests
{
    private static readonly int[] Protocols = [47, 107, 108, 109, 110, 210, 315, 316, 335, 338, 340];
    private static readonly int[] Boundaries = [47, 107, 315, 340];
    private static readonly (int Metadata, string Incoming, string NativeAction)[] NativeSlots =
    [
        (-32768, "009b01800000", "009b01000000"),
        (-1, "009b01ffff00", "009b01000000"),
        (0, "009b01000000", "009b01000000"),
        (1, "009b01000100", "009b01000100"),
        (2, "009b01000200", "009b01000200"),
        (3, "009b01000300", "009b01000300"),
        (4, "009b01000400", "009b01000400"),
        (5, "009b01000500", "009b01000500"),
        (15, "009b01000f00", "009b01000f00"),
        (16, "009b01001000", "009b01001000"),
        (32767, "009b017fff00", "009b017fff00"),
    ];

    public static IEnumerable<object[]> BasicCases() => Protocols.SelectMany(protocol => new[] { 0, 1, 2 }.Select(metadata => new object[] { protocol, metadata }));
    public static IEnumerable<object[]> MetadataCases() => Boundaries.SelectMany(protocol => NativeSlots.Select(row => new object[] { protocol, row.Metadata }));
    public static IEnumerable<object[]> FaceCases() => Boundaries.SelectMany(protocol => Enum.GetValues<Direction>().Select(face => new object[] { protocol, face }));
    public static IEnumerable<object[]> ContextCases() => Boundaries.SelectMany(protocol => new[] { "neighbor-solid", "self", "synthesized-terrain", "no-physics", "custom-nbt", "clicked-air", "floating-side", "world-height", "adventure" }.Select(scene => new object[] { protocol, scene }));
    public static IEnumerable<object[]> ModeCases() => Boundaries.SelectMany(protocol => new[] { GameMode.Creative, GameMode.Spectator }.Select(mode => new object[] { protocol, mode }));
    public static IEnumerable<object[]> CooldownCases() => new[] { 107, 315, 340 }.SelectMany(protocol => new[] { false, true }.Select(blocked => new object[] { protocol, blocked }));
    public static IEnumerable<object[]> HandCases() => Boundaries.Select(protocol => new object[] { protocol });
    public static IEnumerable<object[]> LifetimeCases() => new[] { 47, 340 }.SelectMany(protocol => new[] { "caller-cancel", "session-ended", "reconnect" }.Select(scene => new object[] { protocol, scene }));

    [Fact]
    public void TheFiniteLegacyInventoryHasOneBaseQuartzItemAndFiveNativeFullBoxStates()
    {
        Assert.Equal(11, Protocols.Length);
        foreach (int protocol in Protocols)
        {
            RegistryAccess registries = JavaGameData.Registries(protocol);
            RegistryEntry<ItemDefinition> item = Assert.Single(registries.Items, entry => entry.Id == Identifier.Minecraft("quartz_block"));
            Assert.Equal(155 << 16, item.NetworkId);
            Assert.True(JavaGameData.BlockInteractionData(protocol).TryGetItem(item.Id, out ItemUseOnInfo contract));
            Assert.Equal(ItemUseOnKind.Unknown, contract.Kind);
            Assert.Equal(item.Id, contract.PlacedBlock);
            var blocks = new RegistryBlockDataSource(registries.Blocks, isLegacy: true);
            IBlockShapeSource shapes = JavaGameData.BlockShapes(protocol);
            for (int metadata = 0; metadata <= 4; metadata++)
            {
                BlockState state = blocks.GetLegacyState(155, metadata);
                Assert.True(state.IsValid);
                Assert.Equal((155, metadata), (state.LegacyId, state.LegacyMeta));
                Assert.Equal(new Aabb(0, 0, 0, 1, 1, 1), Assert.Single(shapes.GetCollisionShapes(state).ToArray()));
            }
        }
    }

    [Theory]
    [MemberData(nameof(BasicCases))]
    public async Task EveryLegacyDatasetUsesTheRealPlacementCallerForTheThreeInventoryVariants(int protocol, int metadata)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await Hold(session, metadata);
        await AssertUse(session, BlockUsePrediction.Success(false), rawMetadata: metadata);
        AssertReceivedStateRetained(session, metadata);
    }

    [Theory]
    [MemberData(nameof(MetadataCases))]
    public async Task RawReceivedMetadataRoundTripsWhileOnlyNativeActionCaptureClampsNegatives(int protocol, int metadata)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await Hold(session, metadata);
        await AssertUse(session, BlockUsePrediction.Success(false), rawMetadata: metadata);
        AssertReceivedStateRetained(session, metadata);
    }

    [Theory]
    [MemberData(nameof(FaceCases))]
    public async Task LinesItemKeepsEveryNativeFaceAndUsesItsFullPlacementBox(int protocol, Direction face)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await Hold(session, 2);
        BlockPos destination = Session.Target.Offset(face);
        await session.Block(destination, "air");
        Assert.Empty(await session.Barrier());
        await AssertUse(session, BlockUsePrediction.Success(false), face: face, rawMetadata: 2);
        // The public world and inventory are received state. These assertions do not claim native BlockItem local placement or stack consumption.
        Assert.True(session.Client.State.World.GetBlock(destination).IsAir);
        Assert.Equal(2, session.Client.State.Inventory.GetSlot(36).Damage);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
    }

    [Theory]
    [MemberData(nameof(ContextCases))]
    public async Task PreSendObstructionAndPostSendPermissionFailureKeepDifferentPacketDecisions(int protocol, string scene)
    {
        await using Session session = await Session.Create(protocol, "stone", terrain: scene != "synthesized-terrain", physics: scene != "no-physics");
        await Hold(session, 2, custom: scene == "custom-nbt");
        BlockPos target = Session.Target;
        Direction face = Direction.Up;
        BlockUsePrediction expected = BlockUsePrediction.Success(false);
        if (scene == "neighbor-solid")
        {
            await session.Block(target.Above(), "stone");
            expected = BlockUsePrediction.Rejected;
        }
        if (scene == "self")
        {
            target = new(2, 64, 2);
            expected = BlockUsePrediction.Rejected;
        }
        if (scene is "synthesized-terrain" or "no-physics" or "custom-nbt")
            expected = BlockUsePrediction.Unknown;
        if (scene == "clicked-air")
            await session.Block(target, "air");
        if (scene == "floating-side")
        {
            face = Direction.East;
            await session.Block(target.Offset(face).Below(), "air");
        }
        if (scene == "world-height")
        {
            target = new(1, session.Client.State.World.Dimension.MaxY - 1, 1);
            await session.Block(target, "stone");
            expected = BlockUsePrediction.Unknown;
        }
        if (scene == "adventure")
        {
            await session.Mode(GameMode.Adventure, instantBuild: false);
            expected = BlockUsePrediction.Fail;
        }
        Assert.Empty(await session.Barrier());
        int state = session.Client.State.World.GetBlock(target).StateId;
        await AssertUse(session, expected, target, face, rawMetadata: 2);
        Assert.Equal(state, session.Client.State.World.GetBlock(target).StateId);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
    }

    [Theory]
    [MemberData(nameof(ModeCases))]
    public async Task CreativeAndSpectatorKeepTheExistingNativeModeOrder(int protocol, GameMode mode)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await session.Mode(mode, instantBuild: mode == GameMode.Creative);
        await Hold(session, -1);
        await AssertUse(session, BlockUsePrediction.Success(false), rawMetadata: -1);
        AssertReceivedStateRetained(session, -1);
    }

    [Theory]
    [MemberData(nameof(CooldownCases))]
    public async Task NativePlacementPrecheckRunsBeforeItemCooldown(int protocol, bool blocked)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await Hold(session, 2);
        if (blocked)
            await session.Block(Session.Target.Above(), "stone");
        await session.Cooldown();
        await AssertUse(session, blocked ? BlockUsePrediction.Rejected : BlockUsePrediction.Pass, rawMetadata: 2);
    }

    [Theory]
    [MemberData(nameof(HandCases))]
    public async Task OffhandAvailabilityIsCheckedBeforeAnyMainHandFallback(int protocol)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        if (protocol == 47)
        {
            await Hold(session, 2);
            await Assert.ThrowsAsync<ActionNotSupportedException>(() => session.Client.Actions.Interaction.PlaceBlockAsync(Session.Target, Direction.Up, new Vec3d(.5, 1, .5), Hand.Off, ct: session.Token));
            Assert.Empty(await session.Barrier());
            Assert.Equal(2, session.Client.State.Inventory.GetSlot(36).Damage);
        }
        else
        {
            await Hold(session, 2, hand: Hand.Off);
            Assert.True(session.Client.State.Inventory.GetSlot(36).IsEmpty);
            await AssertUse(session, BlockUsePrediction.Success(false), hand: Hand.Off, rawMetadata: 2);
            Assert.Equal(2, session.Client.State.Inventory.GetSlot(45).Damage);
        }
    }

    [Theory]
    [MemberData(nameof(LifetimeCases))]
    public async Task QuartzUsesTheExistingCallerCancellationAndSessionLifetime(int protocol, string scene)
    {
        await using Session session = await Session.Create(protocol, "stone", physics: true);
        await Hold(session, 2);
        if (scene == "caller-cancel")
        {
            using var caller = new CancellationTokenSource();
            caller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.Client.Actions.Interaction.PlaceBlockAsync(Session.Target, Direction.Up, new Vec3d(.5, 1, .5), ct: caller.Token));
            Assert.Empty(await session.Barrier());
            await AssertUse(session, BlockUsePrediction.Success(false), rawMetadata: 2);
            return;
        }
        await session.Client.DisconnectAsync(session.Token);
        Assert.Equal(ClientStatus.Disconnected, session.Client.Status);
        if (scene == "session-ended")
        {
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => session.Client.Actions.Interaction.PlaceBlockAsync(Session.Target, Direction.Up, new Vec3d(.5, 1, .5), ct: session.Token));
            Assert.Equal("The client is not connected.", failure.Message);
            return;
        }
        await session.Restart("stone");
        await Hold(session, 2);
        await AssertUse(session, BlockUsePrediction.Success(false), rawMetadata: 2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FlatteningKeepsOrdinaryQuartzAndUnknownNbtAtTheAdjacentProtocol(bool custom)
    {
        await using Session session = await Session.Create(393, "stone", physics: true);
        await session.Hold("quartz_block", custom);
        await AssertUse(session, custom ? BlockUsePrediction.Unknown : BlockUsePrediction.Success(false));
    }

    [Fact]
    public async Task Protocol47AirUseCapturesTheNativeStackWithoutChangingReceivedMetadata()
    {
        await using Session session = await Session.Create(47, "stone", physics: true);
        foreach (int metadata in new[] { -32768, -1 })
        {
            await Hold(session, metadata);
            Task action = session.Client.Actions.Interaction.UseItemAsync(ct: session.Token);
            await action.WaitAsync(session.Token);
            List<IPacket> packets = await session.Barrier();
            var use = Assert.IsType<ServerboundLegacyBlockPlacePacket>(Assert.Single(packets));
            Assert.Equal(new BlockPos(-1, -1, -1), use.Position);
            Assert.Equal(255, use.Face);
            Assert.Equal(((byte)0, (byte)0, (byte)0), (use.CursorX, use.CursorY, use.CursorZ));
            Assert.Equal(Identifier.Minecraft("quartz_block"), use.HeldItem.Item.Id);
            Assert.Equal(0, use.HeldItem.Damage);
            Assert.Equal(1, use.HeldItem.Count);
            var vector = Assert.Single(NativeSlots, row => row.Metadata == metadata);
            byte[] expected = [.. Enumerable.Repeat(byte.MaxValue, 9), .. Convert.FromHexString(vector.NativeAction), 0, 0, 0];
            Assert.Equal(expected, Support.BoundDescriptorCodec.EncodeServerbound(47, use));
            Assert.True(action.IsCompletedSuccessfully);
            Assert.Equal(metadata, session.Client.State.Inventory.GetSlot(36).Damage);
            Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
            Assert.Equal(ClientStatus.Playing, session.Client.Status);
        }
    }

    private static async Task Hold(Session session, int metadata, bool custom = false, Hand hand = Hand.Main)
    {
        var vector = Assert.Single(NativeSlots, row => row.Metadata == metadata);
        byte[] payload = [0, 0, (byte)(hand == Hand.Main ? 36 : 45), .. Convert.FromHexString(vector.Incoming)];
        BoundPacketCodec codec = Support.BoundDescriptorCodec.Clientbound(session.Protocol, session.Protocol == 47 ? "set_slot" : "container_set_slot");
        var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
        var packet = Assert.IsType<ClientboundContainerSetSlotPacket>(codec.Decode(payload, context));
        Assert.Equal(hand == Hand.Main ? 36 : 45, packet.Slot);
        Assert.Equal(Identifier.Minecraft("quartz_block"), packet.Item.Item.Id);
        Assert.Equal(155 << 16, packet.Item.Item.NetworkId);
        Assert.Equal(metadata, packet.Item.Damage);
        Assert.Equal(1, packet.Item.Count);
        Assert.Equal(metadata == 0, packet.Item.Components.HasNoPatch);
        Assert.Equal(payload, Encode(codec, packet, context));
        if (custom)
            packet = packet with { Item = packet.Item.With(DataComponents.LegacyNbt, new LegacyNbtComponent(new NbtCompound { ["display"] = new NbtCompound { ["Name"] = new NbtString("unknown quartz context") } })) };
        await session.Send(packet);
        Assert.Empty(await session.Barrier());
        ItemStack received = session.Client.State.Inventory.GetSlot(hand == Hand.Main ? 36 : 45);
        Assert.Equal(metadata, received.Damage);
        Assert.Equal(packet.Item.Components.Get(DataComponents.LegacyNbt), received.Components.Get(DataComponents.LegacyNbt));
    }

    private static byte[] Encode(BoundPacketCodec codec, IPacket packet, PacketCodecContext context)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        codec.Encode(ref writer, packet, context);
        return buffer.WrittenSpan.ToArray();
    }

    private static void AssertReceivedStateRetained(Session session, int metadata)
    {
        // These are the existing received-state contracts, independently of vanilla's separate local block/stack prediction.
        Assert.True(session.Client.State.World.GetBlock(Session.Target.Above()).IsAir);
        Assert.Equal(metadata, session.Client.State.Inventory.GetSlot(36).Damage);
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
    }

    private static async Task AssertUse(Session session, BlockUsePrediction expected, BlockPos? position = null, Direction face = Direction.Up, Hand hand = Hand.Main, int rawMetadata = 0)
    {
        Assert.Empty(await session.Barrier());
        BlockPos target = position ?? Session.Target;
        Vec3d cursor = face switch
        {
            Direction.Down => new(.5, 0, .5),
            Direction.Up => new(.5, 1, .5),
            Direction.North => new(.5, .5, 0),
            Direction.South => new(.5, .5, 1),
            Direction.West => new(0, .5, .5),
            _ => new(1, .5, .5),
        };
        BlockUsePrediction actual = await session.Client.InvokeAsync(client => client.Actions.Interaction.PredictBlockUse(target, face, cursor, hand), session.Token);
        Task action = session.Client.Actions.Interaction.PlaceBlockAsync(target, face, cursor, hand, ct: session.Token);
        await session.Client.InvokeAsync(_ => true, session.Token);
        List<IPacket> packets = await session.Barrier();
        await action.WaitAsync(session.Token);
        Assert.True(action.IsCompletedSuccessfully);
        Assert.Equal(!expected.SendPacket ? 0 : expected.Swing == BlockUseSwing.Packet ? 2 : 1, packets.Count);
        if (expected.SendPacket)
        {
            if (session.Protocol == 47)
            {
                var use = Assert.IsType<ServerboundLegacyBlockPlacePacket>(packets[0]);
                Assert.Equal((target, (int)face), (use.Position, use.Face));
                Assert.Equal(((byte)(cursor.X * 16), (byte)(cursor.Y * 16), (byte)(cursor.Z * 16)), (use.CursorX, use.CursorY, use.CursorZ));
                Assert.Equal(Identifier.Minecraft("quartz_block"), use.HeldItem.Item.Id);
                Assert.Equal(Math.Max(0, rawMetadata), use.HeldItem.Damage);
                Assert.Equal(1, use.HeldItem.Count);
                LegacyNbtComponent? receivedTag = session.Client.State.Inventory.GetSlot(36).Components.Get(DataComponents.LegacyNbt);
                Assert.Equal(receivedTag, use.HeldItem.Components.Get(DataComponents.LegacyNbt));
                byte[] wire = Support.BoundDescriptorCodec.EncodeServerbound(session.Protocol, use);
                var vector = Assert.Single(NativeSlots, row => row.Metadata == rawMetadata);
                byte[] nativeStack = Convert.FromHexString(vector.NativeAction);
                Assert.Equal(nativeStack.AsSpan(0, receivedTag is null ? 6 : 5).ToArray(), wire.AsSpan(9, receivedTag is null ? 6 : 5).ToArray());
            }
            else
            {
                var use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
                Assert.Equal((target, (int)face, (int)hand), (use.Position, use.Face, use.Hand));
                Assert.Equal(((float)cursor.X, (float)cursor.Y, (float)cursor.Z), (use.CursorX, use.CursorY, use.CursorZ));
                Assert.False(use.Inside);
                Assert.False(use.WorldBorderHit);
            }
            if (expected.Swing == BlockUseSwing.Packet)
            {
                var swing = Assert.IsType<ServerboundSwingPacket>(packets[1]);
                Assert.Equal((int)hand, swing.Hand);
                Assert.Equal(session.Protocol >= 107, swing.HasHand);
            }
        }
        Assert.DoesNotContain(packets, packet => packet is ServerboundPlayerActionPacket or ServerboundPunchPacket);
        Assert.Equal(expected, actual);
        Assert.Equal(ClientStatus.Playing, session.Client.Status);
    }
}
