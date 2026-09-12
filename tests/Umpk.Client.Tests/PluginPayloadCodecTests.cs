using System.Buffers.Binary;
using System.Text;
using Umpk.Client.Plugins;
using Umpk.Client.Tests.Support;
using Umpk.Geometry;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Codecs;
using Umpk.TestKit.Server;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary><see cref="PluginPayloadWriter"/> and <see cref="PluginPayloadReader"/>. Asserted on BYTES rather than on a round trip alone: a reader and a writer that agree with each other and with nobody else would pass every round-trip test and produce a frame no server can read.</summary>
public sealed class PluginPayloadCodecTests
{
    [Fact]
    public void WriteVarInt_ProducesTheVanillaEncoding()
    {
        // The canonical vanilla VarInt cases: one byte below 128, two bytes for 255, and the five-byte two's-complement form for -1.
        Assert.Equal([0x00], Writer().WriteVarInt(0).ToArray());
        Assert.Equal([0x7F], Writer().WriteVarInt(127).ToArray());
        Assert.Equal([0xFF, 0x01], Writer().WriteVarInt(255).ToArray());
        Assert.Equal([0xFF, 0xFF, 0xFF, 0xFF, 0x0F], Writer().WriteVarInt(-1).ToArray());
    }

    [Fact]
    public void WriteString_IsAVarIntByteLengthThenUtf8()
    {
        byte[] written = Writer().WriteString("héllo").ToArray();

        byte[] utf8 = Encoding.UTF8.GetBytes("héllo");
        Assert.Equal(utf8.Length, written[0]);
        Assert.Equal(utf8, written[1..]);
    }

    [Fact]
    public void WriteUuid_IsSixteenBigEndianBytes_MostSignificantFirst()
    {
        var uuid = new Guid("01234567-89ab-cdef-0123-456789abcdef");

        byte[] written = Writer().WriteUuid(uuid).ToArray();

        Assert.Equal(16, written.Length);
        Assert.Equal(
            "0123456789abcdef0123456789abcdef",
            Convert.ToHexStringLower(written));
    }

    /// <summary>1.14 moved the Y bits, so the same position is a different long on either side of the boundary. The whole reason the helpers carry a layout is that a consumer must not have to know this.</summary>
    [Fact]
    public void WriteBlockPos_PacksPerTheWireLayoutLayout()
    {
        var pos = new BlockPos(1, 2, 3);

        long modern = BinaryPrimitives.ReadInt64BigEndian(
            new PluginPayloadWriter(BlockPosLayout.Packed114).WriteBlockPos(pos).ToArray());
        long legacy = BinaryPrimitives.ReadInt64BigEndian(
            new PluginPayloadWriter(BlockPosLayout.PrePacked114).WriteBlockPos(pos).ToArray());

        Assert.Equal((1L << 38) | (3L << 12) | 2L, modern);
        Assert.Equal((1L << 38) | (2L << 26) | 3L, legacy);
        Assert.NotEqual(modern, legacy);
    }

    [Fact]
    public void WriteByteArray_IsALengthPrefixedBlock_AndWriteBytesIsNot()
    {
        Assert.Equal([0x03, 0x0A, 0x0B, 0x0C], Writer().WriteByteArray([0x0A, 0x0B, 0x0C]).ToArray());
        Assert.Equal([0x0A, 0x0B, 0x0C], Writer().WriteBytes([0x0A, 0x0B, 0x0C]).ToArray());
    }

    [Fact]
    public void EveryPrimitive_RoundTrips()
    {
        var uuid = Guid.NewGuid();
        var pos = new BlockPos(-40, 71, 512);
        PluginPayloadWriter writer = Writer()
            .WriteBool(true)
            .WriteByte(0xFE)
            .WriteSByte(-2)
            .WriteShort(-300)
            .WriteUShort(60000)
            .WriteInt(-123456)
            .WriteLong(-9876543210L)
            .WriteFloat(1.5f)
            .WriteDouble(-2.25)
            .WriteVarInt(300)
            .WriteVarLong(-5L)
            .WriteString("umpk")
            .WriteUuid(uuid)
            .WriteBlockPos(pos)
            .WriteByteArray([0x01, 0x02])
            .WriteBytes([0xAA]);

        var reader = new PluginPayloadReader(writer.Written, BlockPosLayout.Packed114);
        Assert.True(reader.ReadBool());
        Assert.Equal(0xFE, reader.ReadByte());
        Assert.Equal(-2, reader.ReadSByte());
        Assert.Equal(-300, reader.ReadShort());
        Assert.Equal(60000, reader.ReadUShort());
        Assert.Equal(-123456, reader.ReadInt());
        Assert.Equal(-9876543210L, reader.ReadLong());
        Assert.Equal(1.5f, reader.ReadFloat());
        Assert.Equal(-2.25, reader.ReadDouble());
        Assert.Equal(300, reader.ReadVarInt());
        Assert.Equal(-5L, reader.ReadVarLong());
        Assert.Equal("umpk", reader.ReadString());
        Assert.Equal(uuid, reader.ReadUuid());
        Assert.Equal(pos, reader.ReadBlockPos());
        Assert.Equal(new byte[] { 0x01, 0x02 }, reader.ReadByteArray().ToArray());
        Assert.Equal(new byte[] { 0xAA }, reader.ReadRemaining().ToArray());
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void ThePositionAdvances_ByExactlyWhatEachReadConsumed()
    {
        var reader = new PluginPayloadReader(
            Writer().WriteVarInt(255).WriteInt(7).Written, BlockPosLayout.Packed114);

        Assert.Equal(0, reader.Position);
        reader.ReadVarInt();
        Assert.Equal(2, reader.Position);
        reader.ReadInt();
        Assert.Equal(6, reader.Position);
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void ReadingPastTheEnd_Throws()
    {
        var reader = new PluginPayloadReader(new byte[] { 0x01 }, BlockPosLayout.Packed114);

        Assert.Throws<ProtocolViolationException>(() => reader.ReadInt());
        Assert.Throws<ProtocolViolationException>(() => reader.ReadBytes(4));
    }

    /// <summary>The factories on <see cref="ClientChannels"/> exist so a consumer never picks the layout by hand. 1.21.11 is a modern-layout version, and this pins that the factory actually supplies it rather than defaulting.</summary>
    [Fact]
    public async Task TheChannelFactories_CarryTheNegotiatedVersionsLayout()
    {
        await using FakeJavaServer server = FakeJavaServer.Create();
        UmpkClient client = PluginSessionHarness.BuildClient(server);
        await using UmpkClient owned = client;

        var pos = new BlockPos(1, 2, 3);
        PluginPayloadWriter writer = client.Channels.Write().WriteBlockPos(pos);

        Assert.Equal(
            (1L << 38) | (3L << 12) | 2L,
            BinaryPrimitives.ReadInt64BigEndian(writer.ToArray()));
        Assert.Equal(pos, client.Channels.Read(writer.Written).ReadBlockPos());
    }

    private static PluginPayloadWriter Writer() => new(BlockPosLayout.Packed114);
}
