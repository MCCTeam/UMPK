using System.Buffers;
using Xunit;

namespace Umpk.Protocol.Bedrock.Tests.Serialization;

public sealed class BedrockLargeBitSetTests
{
    [Fact]
    public void ProfileWidthWritesNineteenBytesEvenWhenZero()
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new BedrockWriter(output);

        writer.WriteLargeBitSet(BedrockLargeBitSet.Empty(131));

        Assert.Equal("80808080808080808080808080808080808000", Convert.ToHexString(output.WrittenSpan).ToLowerInvariant());
    }

    [Fact]
    public void BitsAcrossUInt128BoundaryRoundTrip()
    {
        var value = BedrockLargeBitSet.Empty(131).WithBit(127).WithBit(128).WithBit(130);
        var output = new ArrayBufferWriter<byte>();
        var writer = new BedrockWriter(output);
        writer.WriteLargeBitSet(value);

        var reader = new BedrockReader(output.WrittenSpan);
        BedrockLargeBitSet decoded = reader.ReadLargeBitSet(131, 19);

        Assert.True(decoded[127]);
        Assert.True(decoded[128]);
        Assert.True(decoded[130]);
        Assert.Equal(value, decoded);
        Assert.True(reader.End);
    }

    [Theory]
    [InlineData("80808080808080808080808080808080808080")]
    [InlineData("80808080808080808080808080808080808020")]
    [InlineData("808080")]
    public void DecoderRejectsNonterminationOverflowAndTruncation(string hex)
    {
        Assert.Throws<BedrockCodecException>(() => Decode(Convert.FromHexString(hex)));
    }

    private static BedrockLargeBitSet Decode(byte[] bytes)
    {
        var reader = new BedrockReader(bytes);
        return reader.ReadLargeBitSet(131, 19);
    }
}
