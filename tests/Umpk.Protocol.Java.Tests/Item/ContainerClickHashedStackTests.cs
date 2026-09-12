using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

/// <summary>Tests for the 768/769 (1.21.2-1.21.4) pre-hashed serverbound container click: the changed-slots map values and the carried item are FULL component item stacks in the count-first optional form, not 770's one-way hashed stacks, so they survive decode. Protocol 770 replaces those full stacks with one-way hashes.</summary>
public class ContainerClickHashedStackTests
{
    [Fact]
    public void ContainerClickV1_21_2_FullStacks_RoundTrip()
    {
        var carried = new ItemStack(
            ItemTestRegistries.Item(ItemTestRegistries.Stone), 5,
            DataComponentMap.Empty.With(DataComponents.Damage, new DamageComponent(50)));
        var changedStack = new ItemStack(ItemTestRegistries.Item(ItemTestRegistries.DiamondSword), 1);
        var p = new ServerboundContainerClickPacket(
            ContainerId: 3, StateId: 11, Slot: 4, Button: 1, Mode: 0,
            ActionNumber: 0, LegacyClickedItem: null,
            ChangedSlots: [new PredictedSlot(4, changedStack)], CarriedItem: carried);

        ServerboundContainerClickPacket decoded = ItemCodecRoundTrip.Cycle(ContainerCodecs.ContainerClickV1_21_2, p);
        Assert.Equal(3, decoded.ContainerId);
        Assert.Equal(11, decoded.StateId);
        Assert.Equal(4, decoded.Slot);
        Assert.Equal(1, decoded.Button);
        Assert.Equal(0, decoded.Mode);

        // Unlike the hashed 770+ click, the raw stacks are reconstructable on decode.
        PredictedSlot slot = Assert.Single(decoded.ChangedSlots);
        Assert.Equal(4, slot.Slot);
        Assert.Equal(changedStack, slot.Stack);
        Assert.Equal(carried, decoded.CarriedItem);
    }

    [Fact]
    public void ContainerClickV1_21_2_ByteStable()
    {
        var carried = new ItemStack(
            ItemTestRegistries.Item(ItemTestRegistries.DiamondSword), 1,
            DataComponentMap.Empty.With(DataComponents.Damage, new DamageComponent(7)));
        var p = new ServerboundContainerClickPacket(
            ContainerId: 1, StateId: 2, Slot: 0, Button: 0, Mode: 0,
            ActionNumber: 0, LegacyClickedItem: null,
            ChangedSlots: [], CarriedItem: carried);
        ItemCodecRoundTrip.AssertByteStable(ContainerCodecs.ContainerClickV1_21_2, p);
    }

    [Fact]
    public void ContainerClickV1_21_2_EmitsFullStacks_PinnedFrame()
    {
        // The 1.21.2 layout is containerId VarInt, stateId VarInt, slot short, button byte, clickType VarInt, changed map (VarInt count, short key, full modern stack), carried full modern stack. Built with the same stack writer the codec must use.
        var changedStack = new ItemStack(ItemTestRegistries.Item(ItemTestRegistries.DiamondSword), 1);
        var carried = new ItemStack(
            ItemTestRegistries.Item(ItemTestRegistries.Stone), 5,
            DataComponentMap.Empty.With(DataComponents.Damage, new DamageComponent(50)));
        var p = new ServerboundContainerClickPacket(
            ContainerId: 1, StateId: 7, Slot: 2, Button: 0, Mode: 0,
            ActionNumber: 0, LegacyClickedItem: null,
            ChangedSlots: [new PredictedSlot(2, changedStack)], CarriedItem: carried);

        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteVarInt(1);
        w.WriteVarInt(7);
        w.WriteShort(2);
        w.WriteByte(0);
        w.WriteVarInt(0);
        w.WriteVarInt(1);
        w.WriteShort(2);
        ItemStackCodecs.WriteModernStack(ref w, changedStack, ItemTestRegistries.Context, ItemStackCodecs.ComponentsV1_21_5);
        ItemStackCodecs.WriteModernStack(ref w, carried, ItemTestRegistries.Context, ItemStackCodecs.ComponentsV1_21_5);
        byte[] expected = buffer.WrittenSpan.ToArray();

        byte[] actual = ItemCodecRoundTrip.Encode(ContainerCodecs.ContainerClickV1_21_2, p);
        Assert.Equal(expected, actual);

        // And it must NOT match the hashed 770 encoding once a component patch is present.
        byte[] hashed = ItemCodecRoundTrip.Encode(ContainerCodecs.ContainerClick770, p);
        Assert.NotEqual(hashed, actual);
    }

    [Fact]
    public void ContainerClickV1_21_2_EmptyPrediction_RoundTrips()
    {
        var p = new ServerboundContainerClickPacket(
            ContainerId: 0, StateId: 1, Slot: -999, Button: 0, Mode: 4,
            ActionNumber: 0, LegacyClickedItem: null, ChangedSlots: [], CarriedItem: null);
        ServerboundContainerClickPacket decoded = ItemCodecRoundTrip.Cycle(ContainerCodecs.ContainerClickV1_21_2, p);
        Assert.Empty(decoded.ChangedSlots);
        Assert.Equal(ItemStack.Empty, decoded.CarriedItem);
        Assert.Equal(-999, decoded.Slot);
        Assert.Equal(4, decoded.Mode);
    }
}
