using System.Buffers;
using Umpk.Game.Items;
using Umpk.Game.Items.Components;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text;
using Umpk.Text.Serialization;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

/// <summary>Regression coverage for <c>minecraft:painting/variant</c>, whose unmodeled compact payload dropped the containing inventory or entity-metadata packet.</summary>
public class PaintingVariantComponentTests
{
    private const string SetContent = "minecraft:container_set_content";
    private const string SetSlot = "minecraft:container_set_slot";

    /// <summary>The component appears from protocol 770 onward. Its registry holder wire form is stable while its component-table id moves.</summary>
    public static TheoryData<int, int> WireIds => new()
    {
        { 770, 89 }, { 771, 89 }, { 772, 89 }, { 773, 89 },
        { 774, 97 }, { 775, 102 }, { 776, 103 }, { 777, 109 },
    };

    [Theory]
    [InlineData(766)]
    [InlineData(767)]
    [InlineData(768)]
    [InlineData(769)]
    public void Component_IsAbsentBeforeItsVanillaIntroduction(int protocol)
    {
        ItemComponentTable table = protocol switch
        {
            766 => ItemComponentTable.V1_20_5(),
            767 => ItemComponentTable.V1_21(),
            768 => ItemComponentTable.V1_21_2(),
            _ => ItemComponentTable.V1_21_4(),
        };

        Assert.False(table.Contains(DataComponents.PaintingVariant));
    }

    [Theory]
    [MemberData(nameof(WireIds))]
    public void ReferenceHolder_DecodesOnEverySupportedEra(int protocol, int wireId)
    {
        byte[] frame = SetSlotFrame(wireId, static (ref PacketWriter writer) => writer.WriteVarInt(4));
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, SetSlot);

        var packet = Assert.IsType<ClientboundContainerSetSlotPacket>(bound.DecodeFrame(frame));

        Assert.True(packet.Item.Components.TryGet(DataComponents.PaintingVariant, out PaintingVariantComponent? variant));
        Assert.Equal(3, variant!.HolderId);
        Assert.Null(variant.Direct);
        Assert.Equal(frame, bound.Encode(packet));
    }

    [Theory]
    [InlineData(770, 89)]
    [InlineData(777, 109)]
    public void InlineHolder_RoundTripsEveryField(int protocol, int wireId)
    {
        byte[] frame = SetSlotFrame(wireId, static (ref PacketWriter writer) =>
        {
            writer.WriteVarInt(0);
            writer.WriteVarInt(2);
            writer.WriteVarInt(3);
            writer.WriteString("example:wide_landscape");
            writer.WriteBool(true);
            writer.WriteComponent(Component.Text("Wide Landscape"), ComponentWireEra.Modern, NbtWireFormat.JavaUnnamedRoot);
            writer.WriteBool(true);
            writer.WriteComponent(Component.Text("Example Artist"), ComponentWireEra.Modern, NbtWireFormat.JavaUnnamedRoot);
        });
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, SetSlot);

        var packet = Assert.IsType<ClientboundContainerSetSlotPacket>(bound.DecodeFrame(frame));

        Assert.True(packet.Item.Components.TryGet(DataComponents.PaintingVariant, out PaintingVariantComponent? variant));
        PaintingVariantDetails direct = Assert.IsType<PaintingVariantDetails>(variant!.Direct);
        Assert.Equal(2, direct.Width);
        Assert.Equal(3, direct.Height);
        Assert.Equal(new Identifier("example", "wide_landscape"), direct.AssetId);
        Assert.Equal(Component.Text("Wide Landscape"), direct.Title);
        Assert.Equal(Component.Text("Example Artist"), direct.Author);
        Assert.Equal(frame, bound.Encode(packet));
    }

    [Fact]
    public void ContainerSetContent_777_PaintingDoesNotDiscardFollowingSlots()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        writer.WriteVarInt(0);
        writer.WriteVarInt(1);
        writer.WriteVarInt(2);
        WriteStack(ref writer, ItemTestRegistries.Stone, componentWireId: 109, static (ref PacketWriter payload) => payload.WriteVarInt(4));
        WriteStack(ref writer, ItemTestRegistries.DiamondSword, componentWireId: null, null);
        writer.WriteVarInt(0);
        byte[] frame = buffer.WrittenSpan.ToArray();
        BoundPacketCodec bound = BoundCodec.At(777, PacketFlow.Clientbound, SetContent);

        var packet = Assert.IsType<ClientboundContainerSetContentPacket>(bound.DecodeFrame(frame));

        Assert.Equal(2, packet.Items.Count);
        Assert.True(packet.Items[0].Components.Has(DataComponents.PaintingVariant));
        Assert.Equal(ItemTestRegistries.DiamondSword, packet.Items[1].Item.NetworkId);
        Assert.Equal(frame, bound.Encode(packet));
    }

    [Fact]
    public void InlineHolder_HashUsesTheVanillaDataShape()
    {
        ItemComponentCodec codec = ItemPacketCodecShared.Table777.ByKey(DataComponents.PaintingVariant);
        var value = new PaintingVariantComponent(
            null,
            new PaintingVariantDetails(
                2,
                3,
                new Identifier("example", "wide_landscape"),
                Component.Text("Wide Landscape"),
                null));
        var ops = new HashOps(forceSoftwareCrc: true);
        int expected = ops.Map(
        [
            (ops.String("width"), ops.Int(2)),
            (ops.String("height"), ops.Int(3)),
            (ops.String("asset_id"), ops.String("example:wide_landscape")),
            (ops.String("title"), ItemCodecPrimitives.HashNbt(
                ops,
                ComponentNbt.To(Component.Text("Wide Landscape"), ComponentWireEra.Modern))),
        ]);

        Assert.Equal(expected, codec.Hash(ops, value, ItemTestRegistries.Context));
    }

    private delegate void PayloadWriter(ref PacketWriter writer);

    private static byte[] SetSlotFrame(int componentWireId, PayloadWriter write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        writer.WriteVarInt(0);
        writer.WriteVarInt(1);
        writer.WriteShort(0);
        WriteStack(ref writer, ItemTestRegistries.Stone, componentWireId, write);
        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteStack(
        ref PacketWriter writer,
        int itemId,
        int? componentWireId,
        PayloadWriter? write)
    {
        writer.WriteVarInt(1);
        writer.WriteVarInt(itemId);
        writer.WriteVarInt(componentWireId.HasValue ? 1 : 0);
        writer.WriteVarInt(0);
        if (componentWireId is not { } id)
            return;

        writer.WriteVarInt(id);
        write!(ref writer);
    }
}
