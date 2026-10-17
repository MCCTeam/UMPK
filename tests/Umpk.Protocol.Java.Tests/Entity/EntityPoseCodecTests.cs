using System.Text.Json;
using Umpk.Game.Entities;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.TestKit;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Entity;

public sealed class EntityPoseCodecTests
{
    public static IEnumerable<object[]> NativeLegs()
    {
        using JsonDocument document = NativeFixture();
        foreach (JsonElement leg in document.RootElement.GetProperty("legs").EnumerateArray())
            yield return [leg.GetProperty("version").GetString()!, leg.GetProperty("protocol").GetInt32()];
    }

    [Theory]
    [MemberData(nameof(NativeLegs))]
    public void BoundCodecMatchesNativePoseMeaningsAndBytes(string version, int protocol)
    {
        using JsonDocument document = NativeFixture();
        JsonElement leg = document.RootElement.GetProperty("legs").EnumerateArray().Single(row => row.GetProperty("version").GetString() == version);
        BoundPacketCodec codec = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:set_entity_data");
        foreach (JsonElement pose in leg.GetProperty("poses").EnumerateArray())
        {
            byte[] wire = Convert.FromHexString(pose.GetProperty("frame_hex").GetString()!);
            var decoded = Assert.IsType<ClientboundSetEntityDataPacket>(codec.DecodeFrame(wire));
            Assert.Equal(pose.GetProperty("expected_pose").GetString(), Assert.Single(decoded.Metadata.Entries).Value.AsPose().ToString());
            Assert.Empty(decoded.Metadata.RawTail);
            Assert.Equal(wire, codec.Encode(decoded));
        }
        foreach (JsonElement pose in leg.GetProperty("invalid").EnumerateArray())
        {
            byte[] wire = Convert.FromHexString(pose.GetProperty("frame_hex").GetString()!);
            if (pose.GetProperty("rejected").GetBoolean())
                Assert.Throws<ProtocolViolationException>(() => codec.DecodeFrame(wire));
            else
            {
                var decoded = Assert.IsType<ClientboundSetEntityDataPacket>(codec.DecodeFrame(wire));
                EntityDataEntry entry = Assert.Single(decoded.Metadata.Entries);
                Assert.Equal(pose.GetProperty("expected_pose").GetString(), entry.Value.AsPose().ToString());
                Assert.Equal(pose.GetProperty("id").GetInt32(), entry.Value.AsPoseWireId());
                Assert.Equal(wire, codec.Encode(decoded));
            }
        }
    }

    private static JsonDocument NativeFixture() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(FixturePaths.RepoRoot(), "tests", "Umpk.Protocol.Java.Tests", "Fixtures", "EntityPose.native.json")));

    [Theory]
    [InlineData(false, "01061206FF", EntityPose.Dying)]
    [InlineData(true, "0106120AFF", EntityPose.Roaring)]
    [InlineData(true, "0106120DFF", EntityPose.Digging)]
    public void NativePoseOrdinalsDecodeToCanonicalMeanings(bool wardenEra, string hex, EntityPose expected)
    {
        PacketCodec<ClientboundSetEntityDataPacket> codec = wardenEra ? EntityDataCodecs.SetEntityDataV1_19 : EntityDataCodecs.SetEntityDataV1_14;
        byte[] wire = Convert.FromHexString(hex);

        ClientboundSetEntityDataPacket decoded = CodecRoundTrip.Decode(codec, wire);

        Assert.Equal(expected, Assert.Single(decoded.Metadata.Entries).Value.AsPose());
        Assert.Equal(wire, CodecRoundTrip.Encode(codec, decoded));
    }

    [Fact]
    public void CanonicalDyingWritesTheOlderNativeOrdinal()
    {
        var packet = new ClientboundSetEntityDataPacket(1, new EntityMetadataList([new EntityDataEntry(6, MetadataValue.Pose(EntityPose.Dying))], []));

        byte[] wire = CodecRoundTrip.Encode(EntityDataCodecs.SetEntityDataV1_14, packet);

        Assert.Equal(Convert.FromHexString("01061206FF"), wire);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(18)]
    [InlineData(128)]
    [InlineData(int.MaxValue)]
    public void ModernUnknownPoseFallsBackToStandingAndRetainsItsBytes(int id)
    {
        byte[] wire = PoseFrame(ModernMetadataTable.V1_21_2, id);

        ClientboundSetEntityDataPacket decoded = CodecRoundTrip.Decode(EntityDataCodecs.SetEntityDataV1_20_5, wire);

        Assert.Equal(EntityPose.Standing, Assert.Single(decoded.Metadata.Entries).Value.AsPose());
        Assert.Equal(wire, CodecRoundTrip.Encode(EntityDataCodecs.SetEntityDataV1_20_5, decoded));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(18)]
    public void EarlierEnumReadersRejectAnInvalidPose(int id)
    {
        byte[] wire = PoseFrame(ModernMetadataTable.V1_19_4, id);

        Assert.Throws<ProtocolViolationException>(() => CodecRoundTrip.Decode(EntityDataCodecs.SetEntityDataV1_20_3, wire));
    }

    private static byte[] PoseFrame(ModernMetadataTable table, int id)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        writer.WriteVarInt(1);
        writer.WriteByte(6);
        writer.WriteVarInt(table.SerializerId(ModernMetadataSerializer.Pose));
        writer.WriteVarInt(id);
        writer.WriteByte(255);
        return buffer.WrittenSpan.ToArray();
    }
}
