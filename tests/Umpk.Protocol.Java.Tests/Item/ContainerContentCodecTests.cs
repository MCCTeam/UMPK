using Umpk.Game.Items;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Item;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

/// <summary>
/// Protocol 735-763 (1.16 - 1.20.1) container receive codecs. Two sub-eras both carrying the flattened present-id item stack (present-bool + VarInt id + byte count + NAMED-root optional NBT; the unnamed network root only arrives at 1.20.2):
/// <list type="bullet">
/// <item>1.16 - 1.17 (735 - 755): no state id. Byte-identical to the 1.14 form
/// (<see cref="ContainerCodecs.ContainerSetContentV1_14"/> / <see cref="ContainerCodecs.ContainerSetSlotV1_14"/>).</item>
/// <item>1.17.1 - 1.20.1 (756 - 763): the state id, the VarInt-prefixed list framing and the trailing
/// carried stack were added at 1.17.1 (<see cref="ContainerCodecs.ContainerSetContentV1_17_1"/> / <see cref="ContainerCodecs.ContainerSetSlotV1_17_1"/>). 1.20.2/1.20.3 keep that field layout but switch the stack's NBT root to the unnamed network form, so they bind their own <see cref="ContainerCodecs.ContainerSetContentV1_20_2"/> / <see cref="ContainerCodecs.ContainerSetSlotV1_20_2"/> members.</item>
/// </list>
/// The wire bytes below are authored directly from each packet's wire contract. Every stack here is EMPTY-tagged, which cannot distinguish the two NBT root flavors (a null tag is a bare TAG_End byte in both); NbtCarryingStackCodecTests covers stacks that carry real NBT.
/// </summary>
public class ContainerContentCodecTests
{
    // A single flattened present-id stone stack: present(0x01), id VarInt(0x01), count byte(0x40), no-tag end byte(0x00). An empty slot is a single present(0x00) byte.
    private static readonly byte[] StoneStack = [0x01, 0x01, 0x40, 0x00];
    private const byte EmptySlot = 0x00;

    private static ItemStack Stone(int count) => new(ItemTestRegistries.Item(ItemTestRegistries.Stone), count);

    private static T DecodeExact<T>(PacketCodec<T> codec, byte[] bytes)
    {
        var reader = new PacketReader(bytes);
        T decoded = codec.Decode(ref reader, ItemTestRegistries.Context);
        Assert.Equal(0, reader.Remaining);
        return decoded;
    }

    [Fact]
    public void SetSlot_1_16_NoStateId_MatchesDecompiledWire()
    {
        // 1.16 read(): readByte containerId, readShort slot, readItem. No state id.
        byte[] wire = [0x01, 0x00, 0x03, .. StoneStack];
        var decoded = DecodeExact(ContainerCodecs.ContainerSetSlotV1_14, wire);

        Assert.Equal(1, decoded.ContainerId);
        Assert.Equal(0, decoded.StateId);
        Assert.Equal(3, decoded.Slot);
        Assert.Equal(ItemTestRegistries.Stone, decoded.Item.Item.NetworkId);
        Assert.Equal(64, decoded.Item.Count);
        Assert.Equal(wire, ItemCodecRoundTrip.Encode(ContainerCodecs.ContainerSetSlotV1_14, decoded));
    }

    [Fact]
    public void SetSlot_1_16_Empty_RoundTrips()
    {
        var slot = new ClientboundContainerSetSlotPacket(ContainerId: 0, StateId: 0, Slot: 5, Item: ItemStack.Empty);
        var cycled = ItemCodecRoundTrip.Cycle(ContainerCodecs.ContainerSetSlotV1_14, slot);
        Assert.True(cycled.Item.IsEmpty);
        ItemCodecRoundTrip.AssertByteStable(ContainerCodecs.ContainerSetSlotV1_14, slot);
    }

    [Fact]
    public void SetSlot_1_17_1_StateId_MatchesDecompiledWire()
    {
        // 1.17.1 read(): readByte containerId, readVarInt stateId, readShort slot, readItem.
        byte[] wire = [0x01, 0x05, 0x00, 0x03, .. StoneStack];
        var decoded = DecodeExact(ContainerCodecs.ContainerSetSlotV1_17_1, wire);

        Assert.Equal(1, decoded.ContainerId);
        Assert.Equal(5, decoded.StateId);
        Assert.Equal(3, decoded.Slot);
        Assert.Equal(ItemTestRegistries.Stone, decoded.Item.Item.NetworkId);
        Assert.Equal(64, decoded.Item.Count);
        Assert.Equal(wire, ItemCodecRoundTrip.Encode(ContainerCodecs.ContainerSetSlotV1_17_1, decoded));
    }

    [Fact]
    public void SetContent_1_16_ShortCountNoCarried_MatchesDecompiledWire()
    {
        // 1.16 read(): readUnsignedByte containerId, readShort count, count * readItem. No state, no carried.
        byte[] wire = [0x01, 0x00, 0x02, .. StoneStack, EmptySlot];
        var decoded = DecodeExact(ContainerCodecs.ContainerSetContentV1_14, wire);

        Assert.Equal(1, decoded.ContainerId);
        Assert.Equal(0, decoded.StateId);
        Assert.Equal(2, decoded.Items.Count);
        Assert.Equal(64, decoded.Items[0].Count);
        Assert.True(decoded.Items[1].IsEmpty);
        Assert.True(decoded.CarriedItem.IsEmpty);
        Assert.Equal(wire, ItemCodecRoundTrip.Encode(ContainerCodecs.ContainerSetContentV1_14, decoded));
    }

    [Fact]
    public void SetContent_1_17_1_StateIdVarIntListCarried_MatchesDecompiledWire()
    {
        // 1.17.1: unsigned-byte container id, VarInt state id, count-prefixed item collection, readItem carriedItem.
        byte[] wire = [0x01, 0x05, 0x02, .. StoneStack, EmptySlot, EmptySlot];
        var decoded = DecodeExact(ContainerCodecs.ContainerSetContentV1_17_1, wire);

        Assert.Equal(1, decoded.ContainerId);
        Assert.Equal(5, decoded.StateId);
        Assert.Equal(2, decoded.Items.Count);
        Assert.Equal(64, decoded.Items[0].Count);
        Assert.True(decoded.Items[1].IsEmpty);
        Assert.True(decoded.CarriedItem.IsEmpty);
        Assert.Equal(wire, ItemCodecRoundTrip.Encode(ContainerCodecs.ContainerSetContentV1_17_1, decoded));
    }

    [Fact]
    public void SetContent_1_17_1_WithCarried_RoundTrips()
    {
        var content = new ClientboundContainerSetContentPacket(
            ContainerId: 2, StateId: 7, Items: [Stone(64), ItemStack.Empty, Stone(1)], CarriedItem: Stone(16));
        var cycled = ItemCodecRoundTrip.Cycle(ContainerCodecs.ContainerSetContentV1_17_1, content);
        Assert.Equal(3, cycled.Items.Count);
        Assert.Equal(16, cycled.CarriedItem.Count);
        ItemCodecRoundTrip.AssertByteStable(ContainerCodecs.ContainerSetContentV1_17_1, content);
    }

    // Unknown item id: matches the existing members' client policy exactly.

    // The flattened stack readers (ReadVarIntIdStack, shared with the 1.14-1.15.2 members and the 764/765 members) fully consume the slot (present, id, count, NBT) and then resolve the id against the session item registry, raising ProtocolViolationException on an id the registry cannot resolve. This is the deliberate client fail policy shared by every stack codec; these 735-763 members inherit it unchanged because they reuse those exact readers. On a matching-version vanilla server every container id resolves, so the live session never faults (confirmed against the 735-763 legs). A raw-id-preserving tolerance would need a synthetic-registry facility in the game model, which is out of scope here.
    [Fact]
    public void SetContent_UnknownItemId_FollowsSharedClientPolicy()
    {
        // containerId(0x01), stateId(0x05), count VarInt(0x01), one slot with id 0x63 (99, absent from the test registry), carried empty. The frame is well-formed; only the id is unresolvable.
        byte[] wire = [0x01, 0x05, 0x01, 0x01, 0x63, 0x01, 0x00, EmptySlot];
        Assert.Throws<ProtocolViolationException>(
            () => DecodeExact(ContainerCodecs.ContainerSetContentV1_17_1, wire));
    }
}
