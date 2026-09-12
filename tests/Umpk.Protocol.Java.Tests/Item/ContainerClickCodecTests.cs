using Umpk.Game.Inventory;
using Umpk.Game.Items;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Item;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

/// <summary>
/// Serverbound container-click era codecs across the 1.14-1.20.1 band (protocols 477-763). Three sub-eras use full VarInt-id stacks:
/// <list type="bullet">
/// <item>1.14 - 1.16.5 (477 - 754): single clicked item, short action number, VarInt mode, no sync map,
/// no state id (<see cref="ContainerCodecs.ContainerClickV1_14"/>).</item>
/// <item>1.17 (755): changed-slots sync map + carried stack, VarInt mode, NO state id and NO action
/// number (<see cref="ContainerCodecs.ContainerClickV1_17"/>).</item>
/// <item>1.17.1 - 1.20.3 (756 - 765): as 1.17 plus the state id
/// (<see cref="ContainerCodecs.ContainerClickV1_17_1"/>).</item>
/// </list>
/// </summary>
public class ContainerClickCodecTests
{
    private static readonly byte[] Stone32 = [0x01, 0x01, 0x20, 0x00];

    private static ItemStack Stone(int count) => new(ItemTestRegistries.Item(ItemTestRegistries.Stone), count);

    private static T DecodeExact<T>(PacketCodec<T> codec, byte[] bytes)
    {
        var reader = new PacketReader(bytes);
        T decoded = codec.Decode(ref reader, ItemTestRegistries.Context);
        Assert.Equal(0, reader.Remaining);
        return decoded;
    }

    [Fact]
    public void ClickV1_14_MatchesDecompiledWire()
    {
        // write(): writeByte id, writeShort slot, writeByte button, writeShort action, writeEnum mode (VarInt ordinal), writeItem clicked. id=1 slot=10 button=0 action=3 mode=1 clicked=stone(32).
        byte[] wire = [0x01, 0x00, 0x0A, 0x00, 0x00, 0x03, 0x01, .. Stone32];
        var packet = new ServerboundContainerClickPacket(
            ContainerId: 1, StateId: 0, Slot: 10, Button: 0, Mode: 1, ActionNumber: 3,
            LegacyClickedItem: Stone(32), ChangedSlots: [], CarriedItem: null);

        Assert.Equal(wire, ItemCodecRoundTrip.Encode(ContainerCodecs.ContainerClickV1_14, packet));

        var decoded = DecodeExact(ContainerCodecs.ContainerClickV1_14, wire);
        Assert.Equal(1, decoded.ContainerId);
        Assert.Equal(10, decoded.Slot);
        Assert.Equal(3, decoded.ActionNumber);
        Assert.Equal(1, decoded.Mode);
        Assert.Equal(32, decoded.LegacyClickedItem!.Count);
        Assert.Empty(decoded.ChangedSlots);
    }

    [Fact]
    public void ClickV1_17_MatchesDecompiledWire_NoStateId()
    {
        // write(): writeByte id, writeShort slot, writeByte button, writeEnum mode, writeMap changedSlots (VarInt count, short slot + item per entry), writeItem carried. NO state id, NO action number.
        byte[] wire = [0x01, 0x00, 0x0A, 0x00, 0x01, 0x01, 0x00, 0x0A, .. Stone32, 0x00];
        var packet = new ServerboundContainerClickPacket(
            ContainerId: 1, StateId: 0, Slot: 10, Button: 0, Mode: 1, ActionNumber: 0,
            LegacyClickedItem: null, ChangedSlots: [new PredictedSlot(10, Stone(32))], CarriedItem: ItemStack.Empty);

        Assert.Equal(wire, ItemCodecRoundTrip.Encode(ContainerCodecs.ContainerClickV1_17, packet));

        var decoded = DecodeExact(ContainerCodecs.ContainerClickV1_17, wire);
        Assert.Equal(1, decoded.ContainerId);
        Assert.Equal(0, decoded.StateId);
        PredictedSlot slot = Assert.Single(decoded.ChangedSlots);
        Assert.Equal(10, slot.Slot);
        Assert.Equal(32, slot.Stack.Count);
        Assert.True(decoded.CarriedItem is { IsEmpty: true });
    }

    // Protocol 756 (1.17.1) adds the state id compared with 1.17.

    [Fact]
    public void ClickV1_17_1_HasStateId_OneByteLongerThanV1_17()
    {
        var packet = new ServerboundContainerClickPacket(
            ContainerId: 1, StateId: 7, Slot: 10, Button: 0, Mode: 1, ActionNumber: 0,
            LegacyClickedItem: null, ChangedSlots: [new PredictedSlot(10, Stone(32))], CarriedItem: ItemStack.Empty);

        byte[] withState = ItemCodecRoundTrip.Encode(ContainerCodecs.ContainerClickV1_17_1, packet);
        byte[] noState = ItemCodecRoundTrip.Encode(ContainerCodecs.ContainerClickV1_17, packet);
        // The only wire difference is the single-byte state-id VarInt (7) right after the container id.
        Assert.Equal(noState.Length + 1, withState.Length);
        Assert.Equal((byte)0x07, withState[1]);

        var decoded = DecodeExact(ContainerCodecs.ContainerClickV1_17_1, withState);
        Assert.Equal(7, decoded.StateId);
    }
}
