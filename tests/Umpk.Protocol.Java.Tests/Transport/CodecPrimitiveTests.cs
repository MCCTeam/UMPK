using System.Buffers;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Transport;

/// <summary>Primitive packet reader/writer symmetry and bound-frame validation.</summary>
public sealed class CodecPrimitiveTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(2097151)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void VarInt_RoundTrips(int value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        writer.WriteVarInt(value);
        var reader = new PacketReader(buffer.WrittenSpan);
        Assert.Equal(value, reader.ReadVarInt());
        Assert.Equal(0, reader.Remaining);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void VarLong_RoundTrips(long value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        writer.WriteVarLong(value);
        var reader = new PacketReader(buffer.WrittenSpan);
        Assert.Equal(value, reader.ReadVarLong());
    }

    [Fact]
    public void Uuid_RoundTripsBigEndian()
    {
        var id = Guid.NewGuid();
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        writer.WriteUuid(id);
        Assert.Equal(16, buffer.WrittenCount);
        var reader = new PacketReader(buffer.WrittenSpan);
        Assert.Equal(id, reader.ReadUuid());
    }

    [Theory]
    [InlineData(BlockPosLayout.Packed114)]
    [InlineData(BlockPosLayout.PrePacked114)]
    public void BlockPosition_RoundTripsBothLayouts(BlockPosLayout layout)
    {
        var position = new Umpk.Geometry.BlockPos(-30000000, 200, 29999999);
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        writer.WriteBlockPos(position, layout);
        var reader = new PacketReader(buffer.WrittenSpan);
        Assert.Equal(position, reader.ReadBlockPos(layout));
    }

    [Fact]
    public void OptionalStruct_AbsentValueRemainsAbsent()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        writer.WriteOptionalStruct<float>(null, static (ref PacketWriter nested, float value) => nested.WriteFloat(value));
        Assert.Equal(1, buffer.WrittenCount);

        var reader = new PacketReader(buffer.WrittenSpan);
        float? decoded = reader.ReadOptionalStruct(static (ref PacketReader nested) => nested.ReadFloat());
        Assert.Null(decoded);
        Assert.Equal(0, reader.Remaining);

        var encodedAgain = new ArrayBufferWriter<byte>();
        var secondWriter = new PacketWriter(encodedAgain);
        secondWriter.WriteOptionalStruct(decoded, static (ref PacketWriter nested, float value) => nested.WriteFloat(value));
        Assert.Equal(new byte[] { 0 }, encodedAgain.WrittenSpan.ToArray());
    }

    [Fact]
    public void OptionalStruct_PresentZeroIsDistinctFromAbsent()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        writer.WriteOptionalStruct<float>(0f, static (ref PacketWriter nested, float value) => nested.WriteFloat(value));

        var reader = new PacketReader(buffer.WrittenSpan);
        float? decoded = reader.ReadOptionalStruct(static (ref PacketReader nested) => nested.ReadFloat());
        Assert.NotNull(decoded);
        Assert.Equal(0f, decoded.Value);
    }

    [Fact]
    public void BoundFrame_TrailingBytesReportPacketIdentity()
    {
        BoundPacketCodec bound = BoundPacketCodec.Create(
            0x00,
            StatusPackets.Serverbound.StatusRequest,
            StatusCodecs.Request);

        var exception = Assert.Throws<ProtocolViolationException>(() =>
            bound.Decode([0xAB], PacketCodecContext.Registryless));
        Assert.Equal(0x00, exception.WireId);
        Assert.Equal(1, exception.RemainingBytes);
        Assert.Contains("status_request", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkerCodec_DecodeReportsMissingImplementation()
    {
        var type = new MarkerPacketType(
            ProtocolPhase.Play,
            PacketFlow.Clientbound,
            Identifier.Minecraft("add_entity"));
        BoundPacketCodec marker = BoundPacketCodec.Marker(0x01, type);
        Assert.False(marker.IsImplemented);
        Assert.Throws<NotImplementedCodecException>(() =>
            marker.Decode([1, 2, 3], PacketCodecContext.Registryless));
    }
}
