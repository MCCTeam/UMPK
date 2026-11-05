using System.Buffers;
using Umpk.Client.Actions;
using Umpk.Client.Tests.Support;
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

public sealed class ComponentCanPlaceOn766To769SessionTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (int protocol in new[] { 766, 767, 768, 769 })
            foreach (string scene in new[] { "allow", "deny", "missing", "tag-unknown", "nbt-unknown" })
                yield return [protocol, scene];
        foreach (int protocol in new[] { 765, 770 })
            foreach (string scene in new[] { "allow", "deny" })
                yield return [protocol, scene];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ReceivedPlacementPermissionUsesTheExistingNativePredicateOwner(int protocol, string scene)
    {
        await using BlockUseSession session = await BlockUseSession.Create(protocol, "stone", physics: true);
        await session.Mode(GameMode.Adventure, instantBuild: false);
        var registries = session.Client.State.Registries!;
        ItemStack stack = new(registries.Items[Identifier.Minecraft("stone")], 1);
        if (protocol == 765)
        {
            var tag = new NbtCompound();
            tag.Put("CanPlaceOn", new NbtList(NbtTagType.String) { new NbtString(scene == "allow" ? "minecraft:stone" : "minecraft:dirt") });
            stack = stack.With(DataComponents.LegacyNbt, new LegacyNbtComponent(tag));
        }
        else if (scene != "missing")
        {
            HolderSetRef blocks = scene == "tag-unknown"
                ? new HolderSetRef(Identifier.Minecraft("unobserved_placement_probe"), [])
                : new HolderSetRef(null, [registries.Blocks[Identifier.Minecraft(scene == "deny" ? "dirt" : "stone")].NetworkId]);
            NbtCompound? nbt = scene == "nbt-unknown" ? new NbtCompound { ["probe"] = new NbtInt(1) } : null;
            var predicate = new BlockPredicateEntry(blocks, null, nbt)
            {
                ComponentMatchers = protocol >= 770 ? new([], []) : null,
            };
            stack = stack.With(DataComponents.CanPlaceOn, new AdventureModePredicateComponent([predicate], ShowInTooltip: false));
        }

        BoundPacketCodec codec = BoundDescriptorCodec.Clientbound(protocol, "container_set_slot");
        var context = new PacketCodecContext(registries, IConnectionCodecState.Empty);
        byte[] bytes = Encode(codec, new ClientboundContainerSetSlotPacket(0, 7, 36, stack), context);
        var received = Assert.IsType<ClientboundContainerSetSlotPacket>(codec.Decode(bytes, context));
        Assert.Equal(bytes, Encode(codec, received, context));
        Assert.Equal((0, 7, 36, 1), (received.ContainerId, received.StateId, received.Slot, received.Item.Count));
        Assert.Equal(Identifier.Minecraft("stone"), received.Item.Item.Id);
        if (protocol is >= 766 and <= 769 && scene != "missing")
        {
            AdventureModePredicateComponent permission = Assert.IsType<AdventureModePredicateComponent>(received.Item.Components.Get(DataComponents.CanPlaceOn));
            Assert.False(permission.ShowInTooltip);
            BlockPredicateEntry predicate = Assert.Single(permission.Predicates);
            Assert.Null(predicate.Properties);
            Assert.Null(predicate.ComponentMatchers);
            Assert.Equal(scene == "nbt-unknown", predicate.Nbt is not null);
            if (scene == "tag-unknown")
                Assert.Equal(Identifier.Minecraft("unobserved_placement_probe"), predicate.Blocks!.Tag);
            else
                Assert.Equal(new[] { registries.Blocks[Identifier.Minecraft(scene == "deny" ? "dirt" : "stone")].NetworkId }, predicate.Blocks!.Ids);
        }
        else if (scene == "missing")
            Assert.Null(received.Item.Components.Get(DataComponents.CanPlaceOn));
        await session.Send(received);
        Assert.Empty(await session.Barrier());
        Assert.True(session.Client.State.EntityMembership.HasColumn(BlockUseSession.Target));
        Assert.True(session.Client.State.EntityMembership.HasColumn(BlockUseSession.Target.Above()));
        if (scene == "tag-unknown")
            Assert.Null(await session.Client.InvokeAsync(client => client.State.RegistryTags.Contains(RegistryIds.Block,
                Identifier.Minecraft("unobserved_placement_probe"), client.State.Registries!.Blocks[Identifier.Minecraft("stone")].NetworkId), session.Token));

        BlockUsePrediction expected = protocol == 765 || scene is "tag-unknown" or "nbt-unknown"
            ? BlockUsePrediction.Unknown
            : scene == "allow" ? BlockUsePrediction.Success(localSwing: false) : BlockUsePrediction.Pass;
        BlockUsePrediction prediction = await session.Client.InvokeAsync(client => client.Actions.Interaction.PredictBlockUse(
            BlockUseSession.Target, Direction.Up, new Vec3d(.5, 1, .5), Hand.Main), session.Token);
        Assert.Equal(expected, prediction);
        List<IPacket> packets = await session.Use(Hand.Main);
        ServerboundUseItemOnPacket use = Assert.IsType<ServerboundUseItemOnPacket>(packets[0]);
        Assert.Equal((BlockUseSession.Target, (int)Direction.Up, 0), (use.Position, use.Face, use.Hand));
        Assert.Equal((.5F, 1F, .5F), (use.CursorX, use.CursorY, use.CursorZ));
        Assert.False(use.Inside);
        Assert.False(use.WorldBorderHit);
        Assert.True(use.Sequence > 0);
        if (expected.Result == BlockUseResult.Success)
            Assert.Collection(packets,
                packet => Assert.IsType<ServerboundUseItemOnPacket>(packet),
                packet =>
                {
                    var swing = Assert.IsType<ServerboundSwingPacket>(packet);
                    Assert.True(swing.HasHand);
                    Assert.Equal(0, swing.Hand);
                });
        else
            Assert.Single(packets);
        Assert.Empty(await session.Barrier());
        if (expected.Result == BlockUseResult.Pass)
            Assert.Equal(1, session.Client.State.Inventory.GetSlot(36).Count);
        Assert.True(session.Client.State.World.GetBlock(BlockUseSession.Target.Above()).IsAir);
        Assert.Equal(ClientStatus.Playing, session.Client.Status);
    }

    private static byte[] Encode(BoundPacketCodec codec, object packet, PacketCodecContext context)
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(output);
        codec.Encode(ref writer, packet, context);
        return output.WrittenSpan.ToArray();
    }
}
