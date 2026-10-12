using Xunit;

namespace Umpk.Protocol.Bedrock.Tests.Codecs;

public sealed class BedrockLoadingScreenCodecTests
{
    // Exact BDS 1.26.45.1 typed packet312 observations bind these payloads to Start1 and End2 with absent identifiers.
    [Theory]
    [InlineData("0200", 1)]
    [InlineData("0400", 2)]
    public void ObservedInitialNotificationsRoundTrip(string payloadHex, int expectedType)
    {
        byte[] payload = Convert.FromHexString(payloadHex);

        BedrockLoadingScreenPacket decoded = BedrockLoadingScreenCodecs.Decode(payload);

        Assert.Equal((BedrockLoadingScreenType)expectedType, decoded.Type);
        Assert.Null(decoded.LoadingScreenId);
        Assert.Equal(payload, BedrockLoadingScreenCodecs.Encode(decoded));
    }

    // Engaged identifiers follow the frozen protocol-2169 schema; these cases are synthetic rather than live observations.
    [Theory]
    [InlineData("020100000000", 0U)]
    [InlineData("020178563412", 0x12345678U)]
    [InlineData("0201FFFFFFFF", uint.MaxValue)]
    public void PresentIdentifierUsesFixedLittleEndianBytes(string payloadHex, uint expectedId)
    {
        byte[] payload = Convert.FromHexString(payloadHex);

        BedrockLoadingScreenPacket decoded = BedrockLoadingScreenCodecs.Decode(payload);

        Assert.Equal(BedrockLoadingScreenType.StartLoadingScreen, decoded.Type);
        Assert.Equal(expectedId, decoded.LoadingScreenId);
        Assert.Equal(payload, BedrockLoadingScreenCodecs.Encode(decoded));
    }

    [Fact]
    public void UnknownTypePreservesAbsentIdentifier()
    {
        byte[] payload = [0, 0];

        BedrockLoadingScreenPacket decoded = BedrockLoadingScreenCodecs.Decode(payload);

        Assert.Equal(BedrockLoadingScreenType.Unknown, decoded.Type);
        Assert.Null(decoded.LoadingScreenId);
        Assert.Equal(payload, BedrockLoadingScreenCodecs.Encode(decoded));
    }

    [Theory]
    [InlineData("")]
    [InlineData("02")]
    [InlineData("0201")]
    [InlineData("0201785634")]
    [InlineData("0202")]
    [InlineData("020000")]
    [InlineData("02017856341200")]
    [InlineData("820000")]
    [InlineData("FFFFFFFF1F00")]
    [InlineData("0600")]
    [InlineData("0100")]
    public void RejectsMalformedTruncatedOrUnsupportedPayload(string payloadHex)
    {
        Assert.Throws<BedrockCodecException>(() => BedrockLoadingScreenCodecs.Decode(Convert.FromHexString(payloadHex)));
    }

    [Fact]
    public void RejectsPayloadBeyondBound()
    {
        Assert.Throws<BedrockCodecException>(() => BedrockLoadingScreenCodecs.Decode(new byte[11]));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void EncodingRejectsUnsupportedType(int type)
    {
        Assert.Throws<BedrockCodecException>(() => BedrockLoadingScreenCodecs.Encode(new BedrockLoadingScreenPacket((BedrockLoadingScreenType)type, null)));
    }
}
