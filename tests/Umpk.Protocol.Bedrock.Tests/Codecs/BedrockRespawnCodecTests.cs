using Xunit;

namespace Umpk.Protocol.Bedrock.Tests.Codecs;

public sealed class BedrockRespawnCodecTests
{
    [Fact]
    public void RespawnUsesExactWireForm()
    {
        var packet = new BedrockRespawnPacket(new BedrockVector3(1f, 2f, 3f), 2, 5UL);

        byte[] encoded = BedrockRespawnCodecs.Encode(packet);
        BedrockRespawnPacket decoded = BedrockRespawnCodecs.Decode(encoded);

        Assert.Equal(packet, decoded);
        Assert.Equal(new BedrockVector3(1f, 2f, 3f), decoded.Position);
        Assert.Equal(2, decoded.State);
        Assert.Equal(5UL, decoded.RuntimeEntityId);
        Assert.Equal(encoded, BedrockRespawnCodecs.Encode(decoded));
    }

    [Fact]
    public void RespawnRoundTripsBoundaryValues()
    {
        var packet = new BedrockRespawnPacket(new BedrockVector3(-123.125f, 300.5f, 0.25f), 255, ulong.MaxValue);

        byte[] encoded = BedrockRespawnCodecs.Encode(packet);
        BedrockRespawnPacket decoded = BedrockRespawnCodecs.Decode(encoded);

        Assert.Equal(packet, decoded);
        Assert.Equal(encoded, BedrockRespawnCodecs.Encode(decoded));
    }

    [Fact]
    public void RespawnDecodesKnownBytesWithAssertedValues()
    {
        byte[] payload = Convert.FromHexString("0000803F00000040000040400205");

        BedrockRespawnPacket decoded = BedrockRespawnCodecs.Decode(payload);

        Assert.Equal(1f, decoded.Position.X);
        Assert.Equal(2f, decoded.Position.Y);
        Assert.Equal(3f, decoded.Position.Z);
        Assert.Equal(2, decoded.State);
        Assert.Equal(5UL, decoded.RuntimeEntityId);
        Assert.Equal(payload, BedrockRespawnCodecs.Encode(decoded));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0000803F")]
    [InlineData("0000803F0000004000004040")]
    public void RespawnRejectsTruncatedPayloads(string payloadHex)
    {
        Assert.Throws<BedrockCodecException>(() => BedrockRespawnCodecs.Decode(Convert.FromHexString(payloadHex)));
    }

    [Fact]
    public void RespawnRejectsTrailingBytes()
    {
        byte[] encoded = BedrockRespawnCodecs.Encode(new BedrockRespawnPacket(new BedrockVector3(1f, 2f, 3f), 2, 5UL));
        byte[] trailing = [.. encoded, 0x00];

        Assert.Throws<BedrockCodecException>(() => BedrockRespawnCodecs.Decode(trailing));
    }

    [Fact]
    public void RespawnRejectsNonFiniteVectors()
    {
        byte[] encoded = BedrockRespawnCodecs.Encode(new BedrockRespawnPacket(new BedrockVector3(1f, 2f, 3f), 2, 5UL));
        encoded[0] = 0x00;
        encoded[1] = 0x00;
        encoded[2] = 0xC0;
        encoded[3] = 0x7F;

        Assert.Throws<BedrockCodecException>(() => BedrockRespawnCodecs.Decode(encoded));
    }

    [Fact]
    public void RespawnRejectsOverlongPayload()
    {
        byte[] oversized = new byte[65];

        Assert.Throws<BedrockCodecException>(() => BedrockRespawnCodecs.Decode(oversized));
    }
}
