using System.Buffers;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

/// <summary><c>minecraft:pot_decorations</c> was listed-but-untyped on 766, 767, 768 and 769 - the pre-1.21.5 era-table family, even though the very same codec (<see cref="ItemComponentCodecs.PotDecorations"/>) has been bound on 770-776 (the <c>Build</c> family) all along. A decorated-pot item stack carrying real sherds therefore cost the WHOLE packet on exactly those four protocols: decoding raised <see cref="UnmodeledItemComponentException"/> and <see cref="JavaConnection"/> dropped the frame.</summary>
/// <remarks>
/// On protocol 766, <c>advancement grant &lt;player&gt; everything</c> sends <c>minecraft:update_advancements</c> carrying a decorated-pot advancement's icon with a populated <c>pot_decorations</c> patch entry, and the packet was dropped (the READ LOOP survives - see <c>UnmodeledComponentSessionSurvivalTests</c> - the lost data is that one packet's advancement state).
/// <para>The payload writes a VarInt count (at most 4) followed by that many VarInt item-registry ids through 26.2; 26.3 replaces it with exactly four optional item-stack templates (see <see cref="PotDecorationsStacks_777_FourOptionalTemplates_RoundTrip"/>). The wire shape moves once, at 777, so the old form needs no era gate below it and the new form binds only there.</para>
/// <para>Every frame here is composed at the FIELD level and pushed through the codec the registrar actually binds for that protocol (<see cref="BoundCodec"/> -&gt; <c>BoundPacketCodec.Decode</c>, the live dispatcher's entry point), matching <c>UnmodeledComponentRecoveryTests</c>' convention, never through the codec under test directly.</para>
/// </remarks>
public class PotDecorationsComponentTests
{
    private const string SetSlot = "minecraft:container_set_slot";

    /// <summary>The <c>minecraft:pot_decorations</c> wire id for every era that declares the component. Protocols 766-769 require explicit typed coverage; 770-776 are controls using the same typed representation.</summary>
    public static TheoryData<int, int> PotDecorationsWireIds => new()
    {
        { 766, 50 }, { 767, 51 }, { 768, 61 }, { 769, 61 },
        { 770, 65 }, { 771, 65 }, { 773, 65 }, { 774, 72 }, { 775, 74 }, { 776, 74 },
    };

    /// <summary>A decorated pot with four real sherds decodes without discarding the packet on every era that declares the component.</summary>
    [Theory]
    [MemberData(nameof(PotDecorationsWireIds))]
    public void DecoratedPot_DecodesRatherThanCostingThePacket(int protocol, int wireId)
    {
        byte[] frame = SetSlotFrame(
            protocol, slot: 6, itemId: ItemTestRegistries.Stone, componentWireId: wireId, write: WriteFourSherds);

        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, SetSlot);
        var packet = Assert.IsType<ClientboundContainerSetSlotPacket>(bound.DecodeFrame(frame));

        Assert.True(packet.Item.Components.TryGet(DataComponents.PotDecorations, out PotDecorationsComponent? pot));
        Assert.Equal([950, 951, 952, 953], pot!.SherdItemIds);

        // Frame-exact both ways.
        Assert.Equal(frame, bound.Encode(packet));
    }

    /// <summary>A PARTIALLY decorated pot (vanilla omits a side by writing <c>minecraft:brick</c> for it, never by shortening the list: the canonical form always emits exactly four entries. The wire form only caps the count at 4 and does not require it, so a shorter list must still decode losslessly for a non-vanilla or future sender).</summary>
    [Theory]
    [InlineData(766, 50)]
    [InlineData(769, 61)]
    public void PartiallyDecoratedPot_ShorterListStillDecodes(int protocol, int wireId)
    {
        byte[] frame = SetSlotFrame(
            protocol,
            slot: 2,
            itemId: ItemTestRegistries.Stone,
            componentWireId: wireId,
            write: static (ref PacketWriter w) =>
            {
                w.WriteVarInt(1);
                w.WriteVarInt(42);
            });

        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, SetSlot);
        var packet = Assert.IsType<ClientboundContainerSetSlotPacket>(bound.DecodeFrame(frame));

        Assert.True(packet.Item.Components.TryGet(DataComponents.PotDecorations, out PotDecorationsComponent? pot));
        Assert.Equal([42], pot!.SherdItemIds);
        Assert.Equal(frame, bound.Encode(packet));
    }

    // Helpers.

    private delegate void PayloadWriter(ref PacketWriter writer);

    private static void WriteFourSherds(ref PacketWriter w)
    {
        w.WriteVarInt(4);
        w.WriteVarInt(950);
        w.WriteVarInt(951);
        w.WriteVarInt(952);
        w.WriteVarInt(953);
    }

    /// <summary>Composes a container_set_slot frame at the field level. 766/767 write a SIGNED BYTE container id; 768+ write a VarInt (see <c>ItemComponentTable</c>'s remarks on where the 766/767 vs 768+ split actually falls for THIS field - it is the container-id width, unrelated to the component-table split, which is why the threshold here is 768 and not 770).</summary>
    private static byte[] SetSlotFrame(
        int protocol, int slot, int itemId, int componentWireId, PayloadWriter write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);

        if (protocol < 768)
            w.WriteByte(0);

        else
            w.WriteVarInt(0);

        w.WriteVarInt(1);            // state id
        w.WriteShort((short)slot);
        w.WriteVarInt(1);            // count
        w.WriteVarInt(itemId);
        w.WriteVarInt(1);            // added
        w.WriteVarInt(0);            // removed
        w.WriteVarInt(componentWireId);
        write(ref w);

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>26.3 replaces the counted id list with exactly four optional item-stack templates (back, left, right, front). Live-observed on 777: a decorated-pot advancement icon with back absent, a heart sherd, right absent, and an explorer sherd (wire ids 1609/1605, real sherds the item test registries do not carry, so this frame uses stone/diamond in the same shape).</summary>
    [Fact]
    public void PotDecorationsStacks_777_FourOptionalTemplates_RoundTrip()
    {
        // Template stacks: holder id, count, empty patch. Stone and diamond stand in for the two real sherds.
        byte[] frame = SetSlotTemplateFrame(
            slot: 6,
            itemId: ItemTestRegistries.Stone,
            componentWireId: 76,
            write: static (ref PacketWriter w) =>
            {
                w.WriteBool(false); // back: absent (brick face)
                w.WriteBool(true); // left: present
                w.WriteVarInt(ItemTestRegistries.Stone);
                w.WriteVarInt(1);
                w.WriteVarInt(0);
                w.WriteVarInt(0);
                w.WriteBool(false); // right: absent
                w.WriteBool(true); // front: present
                w.WriteVarInt(ItemTestRegistries.DiamondSword);
                w.WriteVarInt(1);
                w.WriteVarInt(0);
                w.WriteVarInt(0);
            });

        BoundPacketCodec bound = BoundCodec.At(777, PacketFlow.Clientbound, SetSlot);
        var packet = Assert.IsType<ClientboundContainerSetSlotPacket>(bound.DecodeFrame(frame));

        Assert.True(packet.Item.Components.TryGet(DataComponents.PotDecorations, out PotDecorationsComponent? pot));
        Assert.NotNull(pot!.FaceStacks);
        Assert.Equal(4, pot.FaceStacks!.Count);
        Assert.True(pot.FaceStacks[0].IsEmpty);
        Assert.Equal(ItemTestRegistries.Stone, pot.FaceStacks[1].Item.NetworkId);
        Assert.True(pot.FaceStacks[2].IsEmpty);
        Assert.Equal(ItemTestRegistries.DiamondSword, pot.FaceStacks[3].Item.NetworkId);
        Assert.Equal([ItemTestRegistries.Stone, ItemTestRegistries.DiamondSword], pot.SherdItemIds);

        // Frame-exact both ways.
        Assert.Equal(frame, bound.Encode(packet));
    }

    /// <summary>A hand-built component carrying only sherd ids has no 777 wire form: the writer must refuse, not emit a counted list the era cannot parse.</summary>
    [Fact]
    public void PotDecorationsStacks_777_BareIds_RejectEncode()
    {
        ItemComponentTable table = ItemPacketCodecShared.Table777;
        ItemComponentCodec codec = table.ByKey(DataComponents.PotDecorations);

        Assert.ThrowsAny<Exception>(() => EncodeBareIds(codec));
    }

    private static void EncodeBareIds(ItemComponentCodec codec)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        codec.Encode(ref writer, new PotDecorationsComponent([1, 2]), ItemTestRegistries.Context);
    }

    /// <summary>Composes a 777 container_set_slot frame at the field level: VarInt container id, VarInt state id, short slot, then the count-first top-level stack whose single patch entry is the payload under test. Only the four nested pot faces use item-stack templates.</summary>
    private static byte[] SetSlotTemplateFrame(
        int slot, int itemId, int componentWireId, PayloadWriter write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);

        w.WriteVarInt(0);            // container id
        w.WriteVarInt(1);            // state id
        w.WriteShort((short)slot);
        w.WriteVarInt(1);            // top-level stack count
        w.WriteVarInt(itemId);       // top-level item holder id
        w.WriteVarInt(1);            // added
        w.WriteVarInt(0);            // removed
        w.WriteVarInt(componentWireId);
        write(ref w);

        return buffer.WrittenSpan.ToArray();
    }
}
