using Xunit;

namespace Umpk.Protocol.Bedrock.Tests.Codecs;

public sealed class BootstrapPacketCodecTests
{
    [Fact]
    public void RequestNetworkSettingsUsesBigEndianProtocolVersion()
    {
        Assert.Equal("00000879", Convert.ToHexString(BedrockBootstrapCodecs.Encode(new RequestNetworkSettingsPacket(2169))).ToLowerInvariant());
        Assert.Equal(new RequestNetworkSettingsPacket(2169), BedrockBootstrapCodecs.DecodeRequestNetworkSettings(Convert.FromHexString("00000879")));
    }

    [Theory]
    [InlineData(0, "00000000")]
    [InlineData(3, "00000003")]
    [InlineData(-1, "ffffffff")]
    public void PlayStatusUsesBigEndianStatusAndPreservesUnknownValues(int status, string expectedHex)
    {
        var packet = new PlayStatusPacket((PlayStatusCode)status);
        byte[] expected = Convert.FromHexString(expectedHex);

        Assert.Equal(expected, BedrockBootstrapCodecs.Encode(packet));
        Assert.Equal(packet, BedrockBootstrapCodecs.DecodePlayStatus(expected));
    }

    [Fact]
    public void RequestChunkRadiusUsesZigZagRadiusAndUnsignedMaximum()
    {
        var packet = new RequestChunkRadiusPacket(-1, 32);
        byte[] expected = Convert.FromHexString("0120");

        Assert.Equal(expected, BedrockBootstrapCodecs.Encode(packet));
        Assert.Equal(packet, BedrockBootstrapCodecs.DecodeRequestChunkRadius(expected));
    }

    [Fact]
    public void ChunkRadiusUpdateUsesZigZagRadius()
    {
        var packet = new ChunkRadiusUpdatePacket(8);
        byte[] expected = Convert.FromHexString("10");

        Assert.Equal(expected, BedrockBootstrapCodecs.Encode(packet));
        Assert.Equal(packet, BedrockBootstrapCodecs.DecodeChunkRadiusUpdate(expected));
    }

    [Fact]
    public void SetLocalPlayerAsInitializedKeepsUnsignedRuntimeEntityIdWidth()
    {
        var packet = new SetLocalPlayerAsInitializedPacket(0x1_0000_0000UL);
        byte[] expected = Convert.FromHexString("8080808010");

        Assert.Equal(expected, BedrockBootstrapCodecs.Encode(packet));
        Assert.Equal(packet, BedrockBootstrapCodecs.DecodeSetLocalPlayerAsInitialized(expected));
    }

    [Theory]
    [InlineData(false, "00")]
    [InlineData(true, "01")]
    public void ClientCacheStatusUsesStrictBoolean(bool enabled, string expectedHex)
    {
        var packet = new ClientCacheStatusPacket(enabled);
        byte[] expected = Convert.FromHexString(expectedHex);

        Assert.Equal(expected, BedrockBootstrapCodecs.Encode(packet));
        Assert.Equal(packet, BedrockBootstrapCodecs.DecodeClientCacheStatus(expected));
    }

    [Fact]
    public void JoinPacketCodecsRejectTrailingBytes()
    {
        Assert.Throws<BedrockCodecException>(() => BedrockBootstrapCodecs.DecodePlayStatus(Convert.FromHexString("0000000000")));
        Assert.Throws<BedrockCodecException>(() => BedrockBootstrapCodecs.DecodeRequestChunkRadius(Convert.FromHexString("012000")));
        Assert.Throws<BedrockCodecException>(() => BedrockBootstrapCodecs.DecodeChunkRadiusUpdate(Convert.FromHexString("1000")));
        Assert.Throws<BedrockCodecException>(() => BedrockBootstrapCodecs.DecodeSetLocalPlayerAsInitialized(Convert.FromHexString("808080801000")));
        Assert.Throws<BedrockCodecException>(() => BedrockBootstrapCodecs.DecodeClientCacheStatus(Convert.FromHexString("0100")));
    }

    [Fact]
    public void ClientCacheStatusRejectsNonBooleanByte()
    {
        Assert.Throws<BedrockCodecException>(() => BedrockBootstrapCodecs.DecodeClientCacheStatus([0x02]));
    }

    [Fact]
    public void NetworkSettingsUsesLittleEndianFields()
    {
        byte[] bytes = Convert.FromHexString("0002000001200000003f");

        NetworkSettingsPacket packet = BedrockBootstrapCodecs.DecodeNetworkSettings(bytes);

        Assert.Equal((ushort)512, packet.CompressionThreshold);
        Assert.Equal(BedrockCompressionAlgorithm.Deflate, packet.CompressionAlgorithm);
        Assert.Equal(bytes, BedrockBootstrapCodecs.Encode(packet));
    }

    [Fact]
    public void LoginPreservesTwoLittleStringsInsideVarUIntEnvelope()
    {
        var packet = new LoginPacket(2169, "a", "bc");
        byte[] expected = Convert.FromHexString("000008790b0100000061020000006263");

        Assert.Equal(expected, BedrockBootstrapCodecs.Encode(packet));
        Assert.Equal(packet, BedrockBootstrapCodecs.DecodeLogin(expected));
    }

    [Fact]
    public void DisconnectVisibilityControlsMessages()
    {
        var visible = new DisconnectPacket(2, false, "bye", "filtered");
        var hidden = new DisconnectPacket(2, true, null, null);

        Assert.Equal(visible, BedrockBootstrapCodecs.DecodeDisconnect(BedrockBootstrapCodecs.Encode(visible)));
        Assert.Equal(hidden, BedrockBootstrapCodecs.DecodeDisconnect(BedrockBootstrapCodecs.Encode(hidden)));
    }

    [Fact]
    public void LevelChunkKeepsOptionalLimitAndUnconditionalBlobCount()
    {
        var packet = new LevelChunkPacket(-1, 2, 0, 3, null, false, [], [0xaa]);
        byte[] expected = Convert.FromHexString("0104000300000001aa");

        Assert.Equal(expected, BedrockWorldCodecs.Encode(packet));
        Assert.Equal(packet, BedrockWorldCodecs.DecodeLevelChunk(expected));
    }
}
