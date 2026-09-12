using Umpk.Game.Entities;
using Umpk.Game.Items;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Item;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text.Serialization;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Entity;

/// <summary>Round-trip tests for the entity-metadata codec (both eras) and the set_entity_data packet.</summary>
public class EntityMetadataTests
{
    private static EntityDataEntry E(int index, MetadataValue value) => new(index, value);

    // The packed 1.8 format.

    [Fact]
    public void Legacy_AllScalarTypes_RoundTrip()
    {
        var list = new EntityMetadataList(
        [
            E(0, MetadataValue.Byte(-3)),
            E(1, MetadataValue.VarInt(70000)),
            E(2, MetadataValue.Float(1.5f)),
            E(3, MetadataValue.String("hello")),
            E(4, MetadataValue.Position(new BlockPos(1, 2, 3))),
            E(5, MetadataValue.Rotations(new Rotations(10f, 20f, 30f))),
        ], []);

        var p = new ClientboundSetEntityDataPacket(9, list);
        var d = CodecRoundTrip.Cycle(EntityDataCodecs.SetEntityDataV1_8, p);
        Assert.Equal(6, d.Metadata.Entries.Count);
        Assert.Equal(70000, d.Metadata.Entries[1].Value.AsVarInt());
        Assert.Equal("hello", d.Metadata.Entries[3].Value.AsString());
        Assert.Equal(new BlockPos(1, 2, 3), d.Metadata.Entries[4].Value.AsPosition());
    }

    [Fact]
    public void Legacy_ItemSlot_DecodesToItemStackAndContinues()
    {
        var slot = new ItemStack(
            ItemTestRegistries.LegacyItem(ItemTestRegistries.LegacyBaseItemId, 0),
            1);
        var list = new EntityMetadataList([E(10, MetadataValue.Slot(slot)), E(11, MetadataValue.Byte(1))], []);
        var p = new ClientboundSetEntityDataPacket(1, list);
        byte[] wire = Encode(EntityDataCodecs.SetEntityDataV1_8, p, ItemTestRegistries.Context);
        ClientboundSetEntityDataPacket d = Decode(
            EntityDataCodecs.SetEntityDataV1_8,
            wire,
            ItemTestRegistries.Context);
        Assert.Equal(2, d.Metadata.Entries.Count); // parsing continued past the item
        ItemStack back = Assert.IsType<ItemStack>(d.Metadata.Entries[0].Value.AsSlot());
        Assert.Equal(slot.Item.Id, back.Item.Id);
    }

    [Fact]
    public void Legacy_EmptyList_IsJustTerminator()
    {
        var p = new ClientboundSetEntityDataPacket(1, new EntityMetadataList([], []));
        byte[] bytes = CodecRoundTrip.Encode(EntityDataCodecs.SetEntityDataV1_8, p);
        // entity id VarInt (1) + terminator 0x7f (1)
        Assert.True(bytes.Length == 2);
        Assert.Equal(0x7f, bytes[^1]);
        Assert.Empty(CodecRoundTrip.Cycle(EntityDataCodecs.SetEntityDataV1_8, p).Metadata.Entries);
    }

    // The modern format.

    [Theory]
    [InlineData("V1_21_5")]
    [InlineData("V26_1")]
    public void Modern_MixedTypes_RoundTrip(string era)
    {
        var codec = era == "V26_1" ? EntityDataCodecs.SetEntityDataV26_1 : EntityDataCodecs.SetEntityDataV1_21_5;
        var list = new EntityMetadataList(
        [
            E(0, MetadataValue.Byte(0x20)),
            E(1, MetadataValue.VarInt(300)),
            E(2, MetadataValue.Float(3.25f)),
            E(3, MetadataValue.Boolean(true)),
            E(4, MetadataValue.OptionalComponent(null)),
            E(5, MetadataValue.Pose(EntityPose.Swimming)),
            E(6, MetadataValue.OptionalVarInt(null)),
            E(7, MetadataValue.OptionalVarInt(41)),
            E(8, MetadataValue.OptionalBlockState(null)),
            E(9, MetadataValue.BlockState(12)),
            E(10, MetadataValue.Direction(Direction.North)),
        ], []);
        var p = new ClientboundSetEntityDataPacket(5, list);
        var d = CodecRoundTrip.Cycle(codec, p);
        Assert.Equal(list.Entries.Count, d.Metadata.Entries.Count);
        Assert.Equal(300, d.Metadata.Entries[1].Value.AsVarInt());
        Assert.False(d.Metadata.Entries[4].Value.HasValue);
        Assert.Equal(EntityPose.Swimming, d.Metadata.Entries[5].Value.AsPose());
        Assert.Null(d.Metadata.Entries[6].Value.AsOptionalVarInt());
        Assert.Equal(41, d.Metadata.Entries[7].Value.AsOptionalVarInt());
    }

    [Fact]
    public void Modern_ItemSerializer_DecodesARealStackAndContinues()
    {
        var stack = new ItemStack(ItemTestRegistries.Item(ItemTestRegistries.Stone), 3);
        var list = new EntityMetadataList(
        [
            E(8, MetadataValue.Slot(stack)),
            E(9, MetadataValue.Byte(5)),
        ], []);
        var packet = new ClientboundSetEntityDataPacket(1, list);

        byte[] wire = Encode(EntityDataCodecs.SetEntityDataV1_21_5, packet, ItemTestRegistries.Context);
        ClientboundSetEntityDataPacket decoded = Decode(
            EntityDataCodecs.SetEntityDataV1_21_5,
            wire,
            ItemTestRegistries.Context);

        Assert.Empty(decoded.Metadata.RawTail);
        Assert.Equal(2, decoded.Metadata.Entries.Count);
        ItemStack carried = Assert.IsType<ItemStack>(decoded.Metadata.Entries[0].Value.AsSlot());
        Assert.Equal("minecraft:stone", carried.Item.Id.ToString());
        Assert.Equal(3, carried.Count);
        Assert.Equal(MetadataValueKind.Byte, decoded.Metadata.Entries[1].Value.Kind);
    }

    [Fact]
    public void ItemSerializer_DecodesAcrossLegacyFlatteningAndNbtWireLayouts()
    {
        AssertItemRoundTrip(
            EntityDataCodecs.SetEntityDataV1_9,
            new ItemStack(ItemTestRegistries.LegacyItem(ItemTestRegistries.LegacyBaseItemId, 0), 1));
        AssertItemRoundTrip(
            EntityDataCodecs.SetEntityDataV1_13,
            new ItemStack(ItemTestRegistries.Item(ItemTestRegistries.Stone), 2));
        AssertItemRoundTrip(
            EntityDataCodecs.SetEntityDataV1_13_2,
            new ItemStack(ItemTestRegistries.Item(ItemTestRegistries.DiamondSword), 1));
        AssertItemRoundTrip(
            EntityDataCodecs.SetEntityDataV1_20_2,
            new ItemStack(ItemTestRegistries.Item(ItemTestRegistries.Stone), 4));
    }

    [Fact]
    public void Modern_ItemSerializer_EmptyStackIsSafe()
        => AssertItemRoundTrip(EntityDataCodecs.SetEntityDataV1_21_5, ItemStack.Empty);

    [Fact]
    public void Modern_InvalidItem_FallsBackToRawTail()
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        writer.WriteVarInt(1);
        writer.WriteByte(8);
        writer.WriteVarInt(7);
        writer.WriteVarInt(1);
        writer.WriteVarInt(999999);
        writer.WriteVarInt(0);
        writer.WriteVarInt(0);
        writer.WriteByte(0xFF);

        ClientboundSetEntityDataPacket decoded = Decode(
            EntityDataCodecs.SetEntityDataV1_21_5,
            buffer.WrittenSpan.ToArray(),
            ItemTestRegistries.Context);

        Assert.Empty(decoded.Metadata.Entries);
        Assert.NotEmpty(decoded.Metadata.RawTail);
    }

    private static void AssertItemRoundTrip(
        PacketCodec<ClientboundSetEntityDataPacket> codec,
        ItemStack stack)
    {
        var packet = new ClientboundSetEntityDataPacket(
            4,
            new EntityMetadataList([E(8, MetadataValue.Slot(stack))], []));
        byte[] wire = Encode(codec, packet, ItemTestRegistries.Context);
        ClientboundSetEntityDataPacket decoded = Decode(codec, wire, ItemTestRegistries.Context);
        ItemStack carried = Assert.IsType<ItemStack>(decoded.Metadata.Entries[0].Value.AsSlot());
        Assert.Equal(stack.IsEmpty, carried.IsEmpty);
        if (!stack.IsEmpty)
        {
            Assert.Equal(stack.Item.Id, carried.Item.Id);
            Assert.Equal(stack.Count, carried.Count);
        }
    }

    private static byte[] Encode<T>(PacketCodec<T> codec, T value, PacketCodecContext context)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        codec.Encode(ref writer, value, context);
        return buffer.WrittenSpan.ToArray();
    }

    private static T Decode<T>(PacketCodec<T> codec, byte[] wire, PacketCodecContext context)
    {
        var reader = new PacketReader(wire);
        T decoded = codec.Decode(ref reader, context);
        Assert.Equal(0, reader.Remaining);
        return decoded;
    }

    [Fact]
    public void Modern_Component_UsesNbtWireLayout()
    {
        var comp = Umpk.Text.Component.Text("hi");
        var list = new EntityMetadataList([E(2, MetadataValue.Component(comp))], []);
        var p = new ClientboundSetEntityDataPacket(1, list);
        var d = CodecRoundTrip.Cycle(EntityDataCodecs.SetEntityDataV1_21_5, p);
        Assert.Single(d.Metadata.Entries);
        Assert.Equal(MetadataValueKind.Component, d.Metadata.Entries[0].Value.Kind);
    }

    [Fact]
    public void SpawnMob_Legacy_WithMetadataTail_RoundTrips()
    {
        var meta = new EntityMetadataList([E(0, MetadataValue.Byte(0)), E(6, MetadataValue.Float(20f))], []);
        var p = new ClientboundAddMobPacket(5, 54, 1.0, 2.0, 3.0, 90f, 0f, 45f, 10, 20, 30, meta);
        var d = CodecRoundTrip.Cycle(EntitySpawnCodecs.AddMobV1_8, p);
        Assert.Equal(2, d.Metadata.Entries.Count);
        Assert.Equal(20f, d.Metadata.Entries[1].Value.AsFloat());
    }

    [Fact]
    public void SerializerTables_HaveExpectedItemIndices()
    {
        Assert.Equal(ModernMetadataSerializer.ItemStack, ModernMetadataTable.V1_21_5.Resolve(7));
        Assert.Equal(ModernMetadataSerializer.ItemStack, ModernMetadataTable.V26_1.Resolve(7));
        // 26.1 shifted the global-pos serializer later (particle/sound-variant additions).
        Assert.Equal(ModernMetadataSerializer.OptionalGlobalPos, ModernMetadataTable.V1_21_5.Resolve(29));
        Assert.Equal(ModernMetadataSerializer.OptionalGlobalPos, ModernMetadataTable.V26_1.Resolve(33));
    }
}
