using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

/// <summary>Container item-stack framing that uses a VarInt item id and an unnamed NBT root.</summary>
public sealed class ItemStackNbtRootCodecTests
{
    private static ClientboundContainerSetSlotPacket Slot(ItemStack item) =>
        new(ContainerId: 1, StateId: 5, Slot: 3, Item: item);

    [Fact]
    public void EmptyStack_RoundTrips()
    {
        var decoded = ItemCodecRoundTrip.Cycle(ContainerCodecs.ContainerSetSlotV1_20_2, Slot(ItemStack.Empty));
        Assert.True(decoded.Item.IsEmpty);
    }

    [Fact]
    public void PlainStack_RoundTripsByteExactly()
    {
        ItemStack stone = new(ItemTestRegistries.Item(ItemTestRegistries.Stone), 64);
        var decoded = ItemCodecRoundTrip.Cycle(ContainerCodecs.ContainerSetSlotV1_20_2, Slot(stone));
        Assert.Equal(64, decoded.Item.Count);
        Assert.Equal(ItemTestRegistries.Stone, decoded.Item.Item.NetworkId);
        Assert.True(decoded.Item.Components.HasNoPatch);
        ItemCodecRoundTrip.AssertByteStable(ContainerCodecs.ContainerSetSlotV1_20_2, Slot(stone));
    }

    [Fact]
    public void NbtStack_RoundTripsAsEscapeHatchComponent()
    {
        var tag = new NbtCompound();
        tag.PutInt("RepairCost", 3);
        ItemStack sword = new(ItemTestRegistries.Item(ItemTestRegistries.DiamondSword), 1,
            DataComponentMap.Empty.With(DataComponents.LegacyNbt, new LegacyNbtComponent(tag)));
        var decoded = ItemCodecRoundTrip.Cycle(ContainerCodecs.ContainerSetSlotV1_20_2, Slot(sword));
        Assert.True(decoded.Item.Components.TryGet(DataComponents.LegacyNbt, out LegacyNbtComponent? legacy));
        Assert.True(legacy!.Nbt.TryGet("RepairCost", out NbtInt? repairCost));
        Assert.Equal(3, repairCost!.Value);
        ItemCodecRoundTrip.AssertByteStable(ContainerCodecs.ContainerSetSlotV1_20_2, Slot(sword));
    }

    [Fact]
    public void NbtStack_UnnamedRootFrameIsPinned()
    {
        var tag = new NbtCompound();
        tag.PutInt("RepairCost", 3);
        ItemStack sword = new(ItemTestRegistries.Item(ItemTestRegistries.DiamondSword), 1,
            DataComponentMap.Empty.With(DataComponents.LegacyNbt, new LegacyNbtComponent(tag)));

        byte[] expected =
        [
            0x01, 0x05, 0x00, 0x03,
            0x01,
            0x02,
            0x01,
            0x0A,
            0x03, 0x00, 0x0A, .. "RepairCost"u8, 0x00, 0x00, 0x00, 0x03,
            0x00,
        ];

        Assert.Equal(expected, ItemCodecRoundTrip.Encode(ContainerCodecs.ContainerSetSlotV1_20_2, Slot(sword)));
    }

    [Fact]
    public void ContainerClick_RoundTripsFullStacks()
    {
        ItemStack stone = new(ItemTestRegistries.Item(ItemTestRegistries.Stone), 32);
        var click = new ServerboundContainerClickPacket(
            ContainerId: 2, StateId: 7, Slot: 10, Button: 0, Mode: 0, ActionNumber: 0,
            LegacyClickedItem: null,
            ChangedSlots: [new PredictedSlot(10, stone)],
            CarriedItem: ItemStack.Empty);
        var decoded = ItemCodecRoundTrip.Cycle(ContainerCodecs.ContainerClickV1_20_2, click);
        Assert.Equal(2, decoded.ContainerId);
        Assert.Equal(7, decoded.StateId);
        PredictedSlot slot = Assert.Single(decoded.ChangedSlots);
        Assert.Equal(10, slot.Slot);
        Assert.Equal(32, slot.Stack.Count);
        Assert.True(decoded.CarriedItem is { IsEmpty: true });
    }
}
