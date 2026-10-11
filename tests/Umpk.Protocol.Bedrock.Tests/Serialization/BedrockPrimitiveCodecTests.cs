using System.Buffers;
using Xunit;

namespace Umpk.Protocol.Bedrock.Tests.Serialization;

public sealed class BedrockPrimitiveCodecTests
{
    [Theory]
    [InlineData(0U, "00")]
    [InlineData(127U, "7f")]
    [InlineData(128U, "8001")]
    [InlineData(uint.MaxValue, "ffffffff0f")]
    public void UInt32VarIntMatchesIndependentBytes(uint value, string hex)
    {
        byte[] expected = Convert.FromHexString(hex);
        var output = new ArrayBufferWriter<byte>();
        var writer = new BedrockWriter(output);

        writer.WriteUInt32VarInt(value);
        var reader = new BedrockReader(expected);

        Assert.Equal(expected, output.WrittenSpan.ToArray());
        Assert.Equal(value, reader.ReadUInt32VarInt());
        Assert.True(reader.End);
    }

    [Theory]
    [InlineData(0L, "00")]
    [InlineData(-1L, "01")]
    [InlineData(1L, "02")]
    [InlineData(long.MinValue, "ffffffffffffffffff01")]
    [InlineData(long.MaxValue, "feffffffffffffffff01")]
    public void ZigZag64MatchesIndependentBytes(long value, string hex)
    {
        byte[] expected = Convert.FromHexString(hex);
        var output = new ArrayBufferWriter<byte>();
        var writer = new BedrockWriter(output);

        writer.WriteZigZag64(value);
        var reader = new BedrockReader(expected);

        Assert.Equal(expected, output.WrittenSpan.ToArray());
        Assert.Equal(value, reader.ReadZigZag64());
    }

    [Fact]
    public void MixedEndianAndUtf8FieldsRoundTripExactBytes()
    {
        byte[] expected = Convert.FromHexString("12345678f0debc9a78563412efcdab8967452301013c");
        var output = new ArrayBufferWriter<byte>();
        var writer = new BedrockWriter(output);
        var uuid = new Guid("12345678-9abc-def0-0123-456789abcdef");

        writer.WriteInt32BigEndian(0x12345678);
        writer.WriteUuid(uuid);
        writer.WriteString("<");
        var reader = new BedrockReader(expected);

        Assert.Equal(expected, output.WrittenSpan.ToArray());
        Assert.Equal(0x12345678, reader.ReadInt32BigEndian());
        Assert.Equal(uuid, reader.ReadUuid());
        Assert.Equal("<", reader.ReadString());
    }

    [Theory]
    [InlineData("8000")]
    [InlineData("8080808080")]
    [InlineData("ffffffff1f")]
    public void UInt32VarIntRejectsNonCanonicalOrOverflowBytes(string hex)
    {
        Assert.Throws<BedrockCodecException>(() => ReadUInt32VarInt(Convert.FromHexString(hex)));
    }

    [Fact]
    public void StringLengthHonorsConfiguredByteLimit()
    {
        Assert.Throws<BedrockCodecException>(() => ReadString(Convert.FromHexString("0461626364"), new BedrockCodecLimits(MaxStringBytes: 3)));
    }

    [Fact]
    public void UInt64VarIntRejectsOverflowInTenthByte()
    {
        byte[] bytes = Convert.FromHexString("ffffffffffffffffff02");

        Assert.Throws<BedrockCodecException>(() => ReadUInt64VarInt(bytes));
    }

    private static uint ReadUInt32VarInt(byte[] bytes)
    {
        var reader = new BedrockReader(bytes);
        return reader.ReadUInt32VarInt();
    }

    private static string ReadString(byte[] bytes, BedrockCodecLimits limits)
    {
        var reader = new BedrockReader(bytes, limits);
        return reader.ReadString();
    }

    private static ulong ReadUInt64VarInt(byte[] bytes)
    {
        var reader = new BedrockReader(bytes);
        return reader.ReadUInt64VarInt();
    }
}
