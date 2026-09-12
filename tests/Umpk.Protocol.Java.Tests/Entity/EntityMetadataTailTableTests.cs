using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Entity;

/// <summary>Round-trip and serializer-table tests for the modern-tail entity-metadata eras (protocols 768/769, 773, 774) whose serializer order changes relative to the V1_21_5 table. Each test pins the expected table entry for its protocol.</summary>
public class EntityMetadataTailTableTests
{
    private static EntityDataEntry E(int index, MetadataValue value) => new(index, value);

    public static IEnumerable<object[]> TailCodecs =>
    [
        [EntityDataCodecs.SetEntityDataV1_21_2],
        [EntityDataCodecs.SetEntityDataV1_21_9],
        [EntityDataCodecs.SetEntityDataV1_21_11],
    ];

    [Theory]
    [MemberData(nameof(TailCodecs))]
    public void MixedTypes_RoundTrip(PacketCodec<ClientboundSetEntityDataPacket> codec)
    {
        // Uses only serializer ids 0-15, which are identical across every modern table, so the same payload round-trips through every tail era.
        var list = new EntityMetadataList(
        [
            E(0, MetadataValue.Byte(0x20)),
            E(1, MetadataValue.VarInt(300)),
            E(2, MetadataValue.Float(3.25f)),
            E(8, MetadataValue.Boolean(true)),
            E(9, MetadataValue.OptionalComponent(null)),
            E(10, MetadataValue.Position(new BlockPos(4, 5, 6))),
            E(12, MetadataValue.Direction(Direction.North)),
            E(14, MetadataValue.BlockState(12)),
        ], []);
        var p = new ClientboundSetEntityDataPacket(5, list);
        var d = CodecRoundTrip.Cycle(codec, p);
        Assert.Equal(list.Entries.Count, d.Metadata.Entries.Count);

        // The decoded entry indices must match the authored indices at each list position, not just the values: this catches an index mis-parse that would otherwise pass a value-only assertion.
        int[] expectedIndices = [0, 1, 2, 8, 9, 10, 12, 14];
        for (int i = 0; i < expectedIndices.Length; i++)
            Assert.Equal(expectedIndices[i], d.Metadata.Entries[i].Index);

        Assert.Equal(300, d.Metadata.Entries[1].Value.AsVarInt());
        Assert.Equal(new BlockPos(4, 5, 6), d.Metadata.Entries[5].Value.AsPosition());
        Assert.Equal(Direction.North, d.Metadata.Entries[6].Value.AsDirection());
    }

    [Fact]
    public void Table_768_HasPre1215Order()
    {
        // 768/769 keep COMPOUND_TAG at id 16 (V1_21_5 shape) but stop earlier: OPTIONAL_GLOBAL_POS at 25 (vs 29 on 1.21.5), no sound-variant or copper-golem serializers. 31 entries total.
        Assert.Equal(ModernMetadataSerializer.CompoundTag, ModernMetadataTable.V1_21_2.Resolve(16));
        Assert.Equal(ModernMetadataSerializer.Particle, ModernMetadataTable.V1_21_2.Resolve(17));
        Assert.Equal(ModernMetadataSerializer.OptionalGlobalPos, ModernMetadataTable.V1_21_2.Resolve(25));
        Assert.Equal(ModernMetadataSerializer.Quaternion, ModernMetadataTable.V1_21_2.Resolve(30));
        Assert.Equal(ModernMetadataSerializer.Unknown, ModernMetadataTable.V1_21_2.Resolve(31));
    }

    [Fact]
    public void Table_773_774_ResolvableProfileIds()
    {
        // 1.21.9 dropped COMPOUND_TAG's mid-list slot: PARTICLE moves to id 16, and RESOLVABLE_PROFILE lands at id 36 (773) / 37 (774) once the serializer set gained it.
        Assert.Equal(ModernMetadataSerializer.Particle, ModernMetadataTable.V1_21_9.Resolve(16));
        Assert.Equal(ModernMetadataSerializer.ResolvableProfile, ModernMetadataTable.V1_21_9.Resolve(36));
        Assert.Equal(ModernMetadataSerializer.ResolvableProfile, ModernMetadataTable.V1_21_11.Resolve(37));
        // The V1_21_5 table has no serializer at those ids, which is exactly why 773/774 need their own.
        Assert.Equal(ModernMetadataSerializer.Unknown, ModernMetadataTable.V1_21_5.Resolve(36));
    }

    [Fact]
    public void ResolvableProfile_RawTails_AtShiftedId()
    {
        // A metadata stream whose entry uses RESOLVABLE_PROFILE (id 36 on 1.21.9) must raw-tail from that entry onward and re-encode byte-exact, proving the shifted id resolves to the raw-tail serializer rather than being mis-decoded as a scalar.
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var w = new PacketWriter(buffer);
        w.WriteVarInt(1);        // entity id
        w.WriteByte(0);          // index 0
        w.WriteVarInt(0);        // serializer BYTE
        w.WriteSByte(9);         // value
        w.WriteByte(1);          // index 1
        w.WriteVarInt(36);       // serializer RESOLVABLE_PROFILE on 1.21.9 (raw-tail trigger)
        w.WriteBytes([0x01, 0x02, 0x03]);
        w.WriteByte(0xFF);       // terminator (inside the raw tail)
        byte[] wire = buffer.WrittenSpan.ToArray();

        var reader = new PacketReader(wire);
        var decoded = EntityDataCodecs.SetEntityDataV1_21_9.Decode(ref reader, PacketCodecContext.Registryless);
        Assert.Equal(0, reader.Remaining);
        Assert.Single(decoded.Metadata.Entries);
        Assert.NotEmpty(decoded.Metadata.RawTail);

        byte[] reencoded = CodecRoundTrip.Encode(EntityDataCodecs.SetEntityDataV1_21_9, decoded);
        Assert.Equal(wire, reencoded);
    }

    [Fact]
    public void TailWireLayouts_HaveDistinctSerializerTableMembers()
    {
        // 768/769 (V1_21_2), 773 (V1_21_9), and 774 (V1_21_11) each need their own serializer-table member because their serializer order changes relative to V1_21_5. The reused-key eras ride an existing member (771/772 -> V1_21_5, 775 -> V26_1) and 1.8 is the legacy packed format, so every table-bearing member is a reference-distinct instance. The registration bindings reference these members directly, so their existence and distinctness is the contract for resolving a serializer id within one era.
        var members = new HashSet<PacketCodec<ClientboundSetEntityDataPacket>>
        {
            EntityDataCodecs.SetEntityDataV1_8,
            EntityDataCodecs.SetEntityDataV1_21_2,
            EntityDataCodecs.SetEntityDataV1_21_9,
            EntityDataCodecs.SetEntityDataV1_21_11,
            EntityDataCodecs.SetEntityDataV1_21_5,
            EntityDataCodecs.SetEntityDataV26_1,
        };
        Assert.Equal(6, members.Count);
    }
}
