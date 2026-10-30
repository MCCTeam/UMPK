using System.Buffers;
using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Entity;

public sealed class AddMobTypeBoundaryTests
{
    private const string Prefix = "2a00112233445566778899aabbccddeeff";
    private const string Fields = "3ff80000000000004050500000000000c00e0000000000002040601234fffe2345";
    private static readonly Guid Uuid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

    public static IEnumerable<object[]> VarIntCases()
    {
        foreach (int protocol in new[] { 315, 316, 335, 338, 340, 393 })
            foreach ((int type, string bytes) in new (int, string)[]
            {
                (0, "00"), (65, "41"), (127, "7f"), (128, "8001"),
                (255, "ff01"), (256, "8002"), (999, "e707"), (-1, "ffffffff0f"),
            })
                yield return [protocol, type, bytes];
    }

    [Theory]
    [MemberData(nameof(VarIntCases))]
    public void NativeVarIntBandPreservesTypeAndEveryFollowingField(int protocol, int type, string typeBytes)
    {
        BoundPacketCodec codec = Bound(protocol);
        ClientboundAddMobPacket packet = Packet(type);
        byte[] expected = Convert.FromHexString(Prefix + typeBytes + Fields + "ff");

        Assert.Equal(expected, Encode(codec, packet));
        ClientboundAddMobPacket decoded = Decode(codec, expected);

        AssertFields(packet, decoded);
        Assert.Empty(decoded.Metadata.Entries);
        Assert.Empty(decoded.Metadata.RawTail);
        Assert.Equal(expected, Encode(codec, decoded));
    }

    [Theory]
    [InlineData(65, "41")]
    [InlineData(128, "80")]
    [InlineData(255, "ff")]
    public void Adjacent210RetainsItsNativeUnsignedByteType(int type, string typeBytes)
    {
        BoundPacketCodec codec = Bound(210);
        ClientboundAddMobPacket packet = Packet(type);
        byte[] expected = Convert.FromHexString(Prefix + typeBytes + Fields + "ff");

        Assert.Equal(expected, Encode(codec, packet));
        ClientboundAddMobPacket decoded = Decode(codec, expected);

        AssertFields(packet, decoded);
        Assert.Equal(expected, Encode(codec, decoded));
    }

    [Theory]
    [InlineData(315, "EntitySpawnCodecs.AddMobV1_11")]
    [InlineData(316, "EntitySpawnCodecs.AddMobV1_11")]
    [InlineData(335, "EntitySpawnCodecs.AddMobV1_12")]
    [InlineData(338, "EntitySpawnCodecs.AddMobV1_12")]
    [InlineData(340, "EntitySpawnCodecs.AddMobV1_12")]
    public void RegistrationSelectsTheNativeVarIntBand(int protocol, string expectedMember)
    {
        BoundPacketCodec codec = Bound(protocol);

        Assert.Equal(expectedMember, codec.CodecIdentity);
        Assert.StartsWith("varint,uuid,varint,", codec.Shape.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(315)]
    [InlineData(316)]
    [InlineData(335)]
    [InlineData(338)]
    [InlineData(340)]
    public void VarIntCorrectionKeepsPre114MetadataPositionPacking(int protocol)
    {
        var position = new BlockPos(-123, 70, -456);
        ClientboundAddMobPacket packet = Packet(999) with
        {
            Metadata = new EntityMetadataList([new EntityDataEntry(17, MetadataValue.Position(position))], []),
        };
        byte[] expected = Convert.FromHexString(Prefix + "e707" + Fields + "1108ffffe1411bfffe38ff");
        BoundPacketCodec codec = Bound(protocol);

        Assert.Equal(expected, Encode(codec, packet));
        ClientboundAddMobPacket decoded = Decode(codec, expected);

        AssertFields(packet, decoded);
        EntityDataEntry entry = Assert.Single(decoded.Metadata.Entries);
        Assert.Equal(17, entry.Index);
        Assert.Equal(position, entry.Value.AsPosition());
        Assert.Empty(decoded.Metadata.RawTail);
        Assert.Equal(expected, Encode(codec, decoded));
    }

    [Theory]
    [InlineData(210, 315, 231, "e7")]
    [InlineData(315, 210, 999, "e707")]
    public void AdjacentTypeWidthsCannotPreserveTheSameDecodedIdentity(int right, int wrong, int type, string typeBytes)
    {
        byte[] bytes = Convert.FromHexString(Prefix + typeBytes + Fields + "ff");
        ClientboundAddMobPacket decoded = Decode(Bound(right), bytes);
        AssertFields(Packet(type), decoded);
        Assert.Equal(bytes, Encode(Bound(right), decoded));
        BoundPacketCodec neighbour = Bound(wrong);

        bool fullyRead = neighbour.TryProbeShape(bytes, PacketCodecContext.Registryless, out int consumed, out _) && consumed == bytes.Length;
        if (fullyRead)
            Assert.NotEqual(type, Decode(neighbour, bytes).TypeId);
    }

    [Fact]
    public void RetainedOriginal315Type65FramePreservesNativeMetadataAndBytes()
    {
        // Retained original ft.b(et) frame from the controlled native bat campaign. Type65 is a native control; its single-byte encoding does not discriminate the width boundary.
        byte[] bytes = Convert.FromHexString("b818a11f189947a04bcca7d4cbe81be5e5ce414050600000000000405908000000000040681000000000000000000000000000000000000101ac02020300030600040600050600060000070240c000000801000906000a01000b00000c0001ff");
        BoundPacketCodec codec = Bound(315);

        ClientboundAddMobPacket decoded = Decode(codec, bytes);

        Assert.Equal(3128, decoded.EntityId);
        Assert.Equal(65, decoded.TypeId);
        Assert.Equal(Guid.Parse("a11f1899-47a0-4bcc-a7d4-cbe81be5e5ce"), decoded.Uuid);
        Assert.Equal((65.5, 100.125, 192.5), (decoded.X, decoded.Y, decoded.Z));
        Assert.Equal((0F, 0F, 0F), (decoded.Yaw, decoded.Pitch, decoded.HeadPitch));
        Assert.Equal((0, 0, 0), ((int)decoded.VelocityX, (int)decoded.VelocityY, (int)decoded.VelocityZ));
        Assert.Equal(13, decoded.Metadata.Entries.Count);
        Assert.Equal(300, decoded.Metadata.Entries.Single(entry => entry.Index == 1).Value.AsVarInt());
        Assert.Equal(6F, decoded.Metadata.Entries.Single(entry => entry.Index == 7).Value.AsFloat());
        Assert.Empty(decoded.Metadata.RawTail);
        Assert.Equal(bytes, Encode(codec, decoded));
    }

    private static ClientboundAddMobPacket Packet(int type) =>
        new(42, type, 1.5, 65.25, -3.75, 45, 90, 135, 0x1234, -2, 0x2345, new EntityMetadataList([], []), Uuid);

    private static void AssertFields(ClientboundAddMobPacket expected, ClientboundAddMobPacket actual)
    {
        Assert.Equal(expected.EntityId, actual.EntityId);
        Assert.Equal(expected.TypeId, actual.TypeId);
        Assert.Equal(expected.Uuid, actual.Uuid);
        Assert.Equal((expected.X, expected.Y, expected.Z), (actual.X, actual.Y, actual.Z));
        Assert.Equal((expected.Yaw, expected.Pitch, expected.HeadPitch), (actual.Yaw, actual.Pitch, actual.HeadPitch));
        Assert.Equal((expected.VelocityX, expected.VelocityY, expected.VelocityZ), (actual.VelocityX, actual.VelocityY, actual.VelocityZ));
    }

    private static BoundPacketCodec Bound(int protocol)
    {
        var builder = new ProtocolDescriptorBuilder(new GameVersion(GameEdition.Java, "boundary", protocol), new ProtocolFeatures());
        PacketRegistrar.Register(builder, ProtocolPhase.Play, PacketFlow.Clientbound, 3, "minecraft:add_mob");
        ProtocolDescriptor descriptor = builder.Build();
        Assert.True(descriptor.TryGetRegistry(ProtocolPhase.Play, PacketFlow.Clientbound, out PhaseRegistry registry));
        Assert.True(registry.TryGetInbound(3, out BoundPacketCodec codec));
        Assert.True(codec.IsImplemented);
        return codec;
    }

    private static byte[] Encode(BoundPacketCodec codec, object packet)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        codec.Encode(ref writer, packet, PacketCodecContext.Registryless);
        return buffer.WrittenSpan.ToArray();
    }

    private static ClientboundAddMobPacket Decode(BoundPacketCodec codec, byte[] bytes)
    {
        Assert.True(codec.TryProbeShape(bytes, PacketCodecContext.Registryless, out int consumed, out string fault), fault);
        Assert.Equal(bytes.Length, consumed);
        return Assert.IsType<ClientboundAddMobPacket>(codec.Decode(bytes, PacketCodecContext.Registryless));
    }
}
