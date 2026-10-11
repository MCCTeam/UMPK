using Xunit;

namespace Umpk.Protocol.Bedrock.Tests.Compression;

public sealed class BedrockCompressionTests
{
    [Theory]
    [InlineData(2, "ff0102")]
    [InlineData(3, "00")]
    [InlineData(4, "00")]
    public void ThresholdSelectionUsesUncompressedBelowAndDeflateAtEquality(int length, string prefixHex)
    {
        byte[] payload = Enumerable.Range(1, length).Select(static value => (byte)value).ToArray();
        var settings = new BedrockCompressionSettings(3, BedrockCompressionAlgorithm.Deflate);

        byte[] encoded = BedrockCompressionCodec.Encode(payload, settings, BedrockCodecLimits.Default);

        Assert.StartsWith(prefixHex, Convert.ToHexString(encoded).ToLowerInvariant());
        Assert.Equal(payload, BedrockCompressionCodec.Decode(encoded, settings, BedrockCodecLimits.Default));
    }

    [Fact]
    public void RawDeflateHasNoZlibWrapper()
    {
        byte[] payload = Enumerable.Repeat((byte)0x41, 32).ToArray();

        byte[] encoded = BedrockCompressionCodec.Encode(payload, new BedrockCompressionSettings(0, BedrockCompressionAlgorithm.Deflate), BedrockCodecLimits.Default);

        Assert.Equal(0, encoded[0]);
        Assert.Equal(payload, BedrockCompressionCodec.Decode(encoded, new BedrockCompressionSettings(0, BedrockCompressionAlgorithm.Deflate), BedrockCodecLimits.Default));
    }

    [Fact]
    public void DecoderAcceptsIndependentRawDeflateVector()
    {
        byte[] encoded = Convert.FromHexString("004b4a4d29ca4fced64d42a501");

        byte[] decoded = BedrockCompressionCodec.Decode(encoded, new BedrockCompressionSettings(0, BedrockCompressionAlgorithm.Deflate), BedrockCodecLimits.Default);

        Assert.Equal("bedrock-bedrock-bedrock", System.Text.Encoding.ASCII.GetString(decoded));
    }

    [Fact]
    public void DecoderStopsADecompressionBombAtConfiguredLimit()
    {
        byte[] payload = new byte[4096];
        byte[] encoded = BedrockCompressionCodec.Encode(payload, new BedrockCompressionSettings(0, BedrockCompressionAlgorithm.Deflate), BedrockCodecLimits.Default);
        var limits = BedrockCodecLimits.Default with { MaxDecompressedBytes = 1024 };

        Assert.Throws<BedrockCodecException>(() => BedrockCompressionCodec.Decode(encoded, new BedrockCompressionSettings(0, BedrockCompressionAlgorithm.Deflate), limits));
    }

    [Theory]
    [InlineData("01")]
    [InlineData("7f")]
    public void UnsupportedOrUnknownAlgorithmFailsExplicitly(string hex)
    {
        Assert.Throws<BedrockCodecException>(() => BedrockCompressionCodec.Decode(Convert.FromHexString(hex), new BedrockCompressionSettings(0, BedrockCompressionAlgorithm.Deflate), BedrockCodecLimits.Default));
    }
}
