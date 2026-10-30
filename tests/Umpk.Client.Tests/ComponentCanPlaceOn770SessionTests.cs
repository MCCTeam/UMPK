using System.Buffers;
using Umpk.Client.Actions;
using Umpk.Client.Tests.Support;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Game.Players;
using Umpk.Game.Registries;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

public sealed class ComponentCanPlaceOn770SessionTests
{
    private const string NativeCanPlaceOn = "AAcAJAEBAQALAQAAAAAA";

    [Theory]
    [InlineData(770, "native-allow")]
    [InlineData(770, "nonmatch-with-can-break")]
    [InlineData(770, "can-break-only")]
    [InlineData(770, "activation-first")]
    [InlineData(770, "unobserved-tag")]
    [InlineData(771, "unsupported-era")]
    public async Task FramedCanPlaceOnKeepsTheNativePostSendResult(int protocol, string scene)
    {
        await using BlockUseSession session = await BlockUseSession.Create(protocol, scene == "activation-first" ? "stone_button" : "stone", physics: true);
        await session.Mode(GameMode.Adventure, instantBuild: false);
        if (scene == "native-allow")
        {
            byte[] native = Convert.FromBase64String(NativeCanPlaceOn);
            BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(protocol, "container_set_slot");
            var context = new PacketCodecContext(session.Client.State.Registries!, IConnectionCodecState.Empty);
            var slot = Assert.IsType<ClientboundContainerSetSlotPacket>(codec.Decode(native, context));
            Assert.Equal(native, Encode(codec, slot, context));
            Assert.Equal(0, slot.ContainerId);
            Assert.Equal(7, slot.StateId);
            Assert.Equal(36, slot.Slot);
            Assert.Equal("minecraft:stone", slot.Item.Item.Id.ToString());
            AdventureModePredicateComponent permission = Assert.IsType<AdventureModePredicateComponent>(slot.Item.Components.Get(DataComponents.CanPlaceOn));
            BlockPredicateEntry predicate = Assert.Single(permission.Predicates);
            Assert.Null(predicate.Blocks);
            Assert.Null(predicate.Properties);
            Assert.Null(predicate.Nbt);
            AssertEmptyMatchers(predicate);
            await session.Send(slot);
        }
        else
        {
            ItemStack stack = new(session.Client.State.Registries!.Items[Identifier.Minecraft("stone")], 1);
            if (scene == "nonmatch-with-can-break")
                stack = stack.With(DataComponents.CanPlaceOn, Permission(session, "dirt"))
                    .With(DataComponents.CanBreak, Permission(session, "stone"));
            if (scene == "can-break-only")
                stack = stack.With(DataComponents.CanBreak, Permission(session, "stone"));
            if (scene == "unobserved-tag")
                stack = stack.With(DataComponents.CanPlaceOn, new AdventureModePredicateComponent([
                    new BlockPredicateEntry(new HolderSetRef(Identifier.Minecraft("placement_probe"), []), null, null)
                    {
                        ComponentMatchers = new([], []),
                    },
                ]));
            await session.Send(new ClientboundContainerSetSlotPacket(0, 7, 36, stack));
        }
        Assert.Empty(await session.Barrier());
        ItemStack applied = session.Client.State.Inventory.GetSlot(36);
        Assert.Equal("minecraft:stone", applied.Item.Id.ToString());
        Assert.Equal(1, applied.Count);
        AdventureModePredicateComponent? canPlace = applied.Components.Get(DataComponents.CanPlaceOn);
        if (scene is "native-allow" or "nonmatch-with-can-break" or "unobserved-tag")
            AssertEmptyMatchers(Assert.Single(Assert.IsType<AdventureModePredicateComponent>(canPlace).Predicates));
        else
            Assert.Null(canPlace);
        if (scene is "nonmatch-with-can-break" or "can-break-only")
        {
            BlockPredicateEntry canBreak = Assert.Single(Assert.IsType<AdventureModePredicateComponent>(applied.Components.Get(DataComponents.CanBreak)).Predicates);
            Assert.Equal(new[] { session.Client.State.Registries!.Blocks[Identifier.Minecraft("stone")].NetworkId }, canBreak.Blocks!.Ids);
        }
        if (scene == "nonmatch-with-can-break")
            Assert.Equal(new[] { session.Client.State.Registries!.Blocks[Identifier.Minecraft("dirt")].NetworkId }, Assert.Single(canPlace!.Predicates).Blocks!.Ids);
        if (scene == "unobserved-tag")
            Assert.Null(await session.Client.InvokeAsync(client => client.State.RegistryTags.Contains(RegistryIds.Block,
                Identifier.Minecraft("placement_probe"), client.State.Registries!.Blocks[Identifier.Minecraft("stone")].NetworkId), session.Token));
        BlockUsePrediction prediction = await session.Client.InvokeAsync(client => client.Actions.Interaction.PredictBlockUse(
            BlockUseSession.Target, Direction.Up, new Vec3d(.5, 1, .5), Hand.Main), session.Token);
        Assert.True(prediction.SendPacket);
        List<IPacket> sent = await session.Use(Hand.Main);
        var use = Assert.IsType<ServerboundUseItemOnPacket>(sent[0]);
        Assert.Equal((BlockUseSession.Target, (int)Direction.Up, 0), (use.Position, use.Face, use.Hand));
        Assert.Equal((.5F, 1F, .5F), (use.CursorX, use.CursorY, use.CursorZ));
        Assert.False(use.Inside);
        Assert.False(use.WorldBorderHit);
        BlockUsePrediction expected = scene switch
        {
            "native-allow" or "activation-first" => BlockUsePrediction.Success(localSwing: false),
            "nonmatch-with-can-break" or "can-break-only" => BlockUsePrediction.Pass,
            _ => BlockUsePrediction.Unknown,
        };
        // Session.Use already observed the USE frame, required a pending action and supplied its exact sequence ACK.
        Assert.True(expected == prediction, $"scene={scene}; expected={expected.Result}; actual={prediction.Result}; send={prediction.SendPacket}; swing={prediction.Swing}");
        if (expected.Result == BlockUseResult.Success)
            Assert.Collection(sent,
                packet => Assert.IsType<ServerboundUseItemOnPacket>(packet),
                packet =>
                {
                    var swing = Assert.IsType<ServerboundSwingPacket>(packet);
                    Assert.True(swing.HasHand);
                    Assert.Equal(0, swing.Hand);
                });
        else
            Assert.Single(sent);
        Assert.Empty(await session.Barrier());
        Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
        Assert.True(session.Client.State.World.GetBlock(BlockUseSession.Target.Above()).IsAir);
        Assert.Equal(ClientStatus.Playing, session.Client.Status);
    }

    private static AdventureModePredicateComponent Permission(BlockUseSession session, string block)
        => new([
            new BlockPredicateEntry(new HolderSetRef(null, [session.Client.State.Registries!.Blocks[Identifier.Minecraft(block)].NetworkId]), null, null)
            {
                ComponentMatchers = new([], []),
            },
        ]);

    private static void AssertEmptyMatchers(BlockPredicateEntry predicate)
    {
        BlockPredicateComponentMatchers matchers = Assert.IsType<BlockPredicateComponentMatchers>(predicate.ComponentMatchers);
        Assert.Empty(matchers.Exact);
        Assert.Empty(matchers.Partial);
    }

    private static byte[] Encode(BoundPacketCodec codec, object packet, PacketCodecContext context)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(output);
        codec.Encode(ref writer, packet, context);
        return output.WrittenSpan.ToArray();
    }
}
