using Umpk.Protocol.Bedrock;
using Xunit;

namespace Umpk.Protocol.Bedrock.Tests.Codecs;

public sealed class BedrockCreativeContentCodecTests
{
    private static BedrockCreativeItemInstance AirItem() => new(0, 0, 0u, 0, []);

    private static BedrockCreativeItemInstance StoneItem() => new(1, 5, 0u, 10, []);

    [Fact]
    public void CreativeContentUsesExactWireForm()
    {
        var packet = new BedrockCreativeContentPacket(
            [new BedrockCreativeGroup(1, "t", AirItem())],
            [new BedrockCreativeEntry(1, StoneItem(), 0)]);
        byte[] expected = Convert.FromHexString("01010174000000000000010102050000140000");

        Assert.Equal(expected, BedrockCreativeContentCodecs.Encode(packet));
        BedrockCreativeContentPacket decoded = BedrockCreativeContentCodecs.Decode(expected);
        Assert.Equal(packet, decoded);
        Assert.Single(decoded.Groups);
        Assert.Equal(1, decoded.Groups[0].Category);
        Assert.Equal("t", decoded.Groups[0].Name);
        Assert.Equal(0, decoded.Groups[0].IconItem.NetworkId);
        Assert.Single(decoded.Entries);
        Assert.Equal(1, decoded.Entries[0].EntryId);
        Assert.Equal(1, decoded.Entries[0].Item.NetworkId);
        Assert.Equal(5, decoded.Entries[0].Item.Count);
        Assert.Equal(10, decoded.Entries[0].Item.BlockRuntimeId);
        Assert.Equal(0, decoded.Entries[0].GroupIndex);
    }

    [Fact]
    public void CreativeItemPreservesNegativeNetworkAndBlockIds()
    {
        var packet = new BedrockCreativeContentPacket(
            [],
            [new BedrockCreativeEntry(1, new BedrockCreativeItemInstance(-1, 1, 0u, -2, []), 0)]);
        byte[] expected = Convert.FromHexString("00010101010000030000");

        Assert.Equal(expected, BedrockCreativeContentCodecs.Encode(packet));
        BedrockCreativeContentPacket decoded = BedrockCreativeContentCodecs.Decode(expected);
        Assert.Equal(packet, decoded);
        Assert.Equal(-1, decoded.Entries[0].Item.NetworkId);
        Assert.Equal(-2, decoded.Entries[0].Item.BlockRuntimeId);
    }

    [Fact]
    public void CreativeItemPreservesOpaqueExtraData()
    {
        var packet = new BedrockCreativeContentPacket(
            [],
            [new BedrockCreativeEntry(7, new BedrockCreativeItemInstance(5, 1, 0u, 100, [0x00, 0x00, 0x01]), 3)]);

        BedrockCreativeContentPacket decoded = BedrockCreativeContentCodecs.Decode(BedrockCreativeContentCodecs.Encode(packet));
        Assert.Equal(packet, decoded);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x01 }, decoded.Entries[0].Item.ExtraData);
        Assert.Equal(7, decoded.Entries[0].EntryId);
        Assert.Equal(3, decoded.Entries[0].GroupIndex);
    }

    [Fact]
    public void CreativeGroupAllowsEmptySeparatorName()
    {
        var packet = new BedrockCreativeContentPacket(
            [new BedrockCreativeGroup(1, string.Empty, AirItem())],
            []);

        BedrockCreativeContentPacket decoded = BedrockCreativeContentCodecs.Decode(BedrockCreativeContentCodecs.Encode(packet));
        Assert.Equal(packet, decoded);
        Assert.Equal(string.Empty, decoded.Groups[0].Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("00")]
    [InlineData("010101740000000000000101020500001400")]
    [InlineData("0101017400000000000001010205000014000000")]
    [InlineData("0001010101000003000000")]
    public void CreativeContentRejectsTruncatedOrTrailingPayloads(string payloadHex)
    {
        byte[] payload = Convert.FromHexString(payloadHex);

        Assert.Throws<BedrockCodecException>(() => BedrockCreativeContentCodecs.Decode(payload));
    }

    [Fact]
    public void CreativeContentRejectsOversizedGroupCount()
    {
        byte[] payload = Convert.FromHexString("FF0F");

        Assert.Throws<BedrockCodecException>(() => BedrockCreativeContentCodecs.Decode(payload));
    }

    [Fact]
    public void CreativeContentRejectsOversizedEntryCount()
    {
        byte[] payload = Convert.FromHexString("00FF7F");

        Assert.Throws<BedrockCodecException>(() => BedrockCreativeContentCodecs.Decode(payload));
    }
}
