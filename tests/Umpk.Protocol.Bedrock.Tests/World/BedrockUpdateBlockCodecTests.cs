using Xunit;

namespace Umpk.Protocol.Bedrock.Tests.World;

public sealed class BedrockUpdateBlockCodecTests
{
    [Fact]
    public void RoundTripsPositiveCoordinates()
    {
        var packet = new BedrockUpdateBlockPacket(10, 64, -20, 42u, 2u, 0u);

        byte[] encoded = BedrockUpdateBlockCodecs.EncodeUpdateBlock(packet);
        BedrockUpdateBlockPacket decoded = BedrockUpdateBlockCodecs.DecodeUpdateBlock(encoded);

        Assert.Equal(packet, decoded);
        Assert.Equal(encoded, BedrockUpdateBlockCodecs.EncodeUpdateBlock(decoded));
    }

    [Fact]
    public void RoundTripsNegativeCoordinatesAndLayers()
    {
        var first = new BedrockUpdateBlockPacket(-300, -64, 123456, 987654u, 3u, 0u);
        var second = new BedrockUpdateBlockPacket(-1, -65, -1, 7u, 11u, 1u);

        Assert.Equal(first, BedrockUpdateBlockCodecs.DecodeUpdateBlock(BedrockUpdateBlockCodecs.EncodeUpdateBlock(first)));
        Assert.Equal(second, BedrockUpdateBlockCodecs.DecodeUpdateBlock(BedrockUpdateBlockCodecs.EncodeUpdateBlock(second)));
    }

    [Fact]
    public void PreservesUnknownFlagBits()
    {
        var packet = new BedrockUpdateBlockPacket(0, 0, 0, 1u, 16u, 0u);

        BedrockUpdateBlockPacket decoded = BedrockUpdateBlockCodecs.DecodeUpdateBlock(BedrockUpdateBlockCodecs.EncodeUpdateBlock(packet));

        Assert.Equal(16u, decoded.Flags);
        Assert.Equal(packet, decoded);
    }

    [Fact]
    public void PreservesExtremeRuntimeIds()
    {
        var packet = new BedrockUpdateBlockPacket(5, 6, 7, uint.MaxValue, 2u, 1u);

        Assert.Equal(packet, BedrockUpdateBlockCodecs.DecodeUpdateBlock(BedrockUpdateBlockCodecs.EncodeUpdateBlock(packet)));
    }

    [Fact]
    public void LiveHashRuntimeDecodesWithAssertedValuesAndRoundTrips()
    {
        byte[] payload = Convert.FromHexString("1594017686819E9F050200");

        BedrockUpdateBlockPacket decoded = BedrockUpdateBlockCodecs.DecodeUpdateBlock(payload);

        Assert.Equal(-11, decoded.X);
        Assert.Equal(74, decoded.Y);
        Assert.Equal(59, decoded.Z);
        Assert.Equal(1407680646u, decoded.BlockRuntimeId);
        Assert.Equal((uint)BedrockUpdateBlockFlags.Network, decoded.Flags);
        Assert.Equal(0u, decoded.Layer);
        Assert.Equal(payload, BedrockUpdateBlockCodecs.EncodeUpdateBlock(decoded));
    }

    [Fact]
    public void RejectsLayerOutsideZeroOrOne()
    {
        Assert.Throws<BedrockCodecException>(() => new BedrockUpdateBlockPacket(0, 0, 0, 1u, 2u, 2u));
    }

    [Fact]
    public void RejectsTruncatedPayload()
    {
        byte[] encoded = BedrockUpdateBlockCodecs.EncodeUpdateBlock(new BedrockUpdateBlockPacket(1, 2, 3, 4u, 2u, 0u));

        Assert.Throws<BedrockCodecException>(() => BedrockUpdateBlockCodecs.DecodeUpdateBlock(encoded[..^1]));
    }

    [Fact]
    public void RejectsTrailingBytes()
    {
        byte[] encoded = BedrockUpdateBlockCodecs.EncodeUpdateBlock(new BedrockUpdateBlockPacket(1, 2, 3, 4u, 2u, 0u));
        byte[] trailing = [.. encoded, 0x00];

        Assert.Throws<BedrockCodecException>(() => BedrockUpdateBlockCodecs.DecodeUpdateBlock(trailing));
    }
}
