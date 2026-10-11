using Umpk.Nbt;
using Xunit;

namespace Umpk.Protocol.Bedrock.Conformance;

public sealed class ItemExtraDataCodecTests
{
    [Fact]
    public void PlacementAndDestructionStringsUseSignedFixedCountsAndShortStrings()
    {
        var value = new BedrockItemExtraData(null, 0, ["stone"], ["dirt"], null);
        byte[] expected = Convert.FromHexString("000001000000050073746f6e6501000000040064697274");

        Assert.Equal(expected, BedrockItemExtraDataCodec.Encode(value));
        BedrockItemExtraData decoded = BedrockItemExtraDataCodec.Decode(expected, hasBlockingTick: false);
        Assert.Null(decoded.Tag);
        Assert.Equal(value.CanPlaceOn, decoded.CanPlaceOn);
        Assert.Equal(value.CanDestroy, decoded.CanDestroy);
    }

    [Fact]
    public void ItemNbtUsesMarkerVersionAndFixedLittleEndianRoot()
    {
        var tag = new NbtCompound();
        tag.PutInt("x", 300);
        var value = new BedrockItemExtraData(tag, 1, [], [], 7);
        byte[] expected = Convert.FromHexString("ffff010a0000030100782c0100000000000000000000000700000000000000");

        Assert.Equal(expected, BedrockItemExtraDataCodec.Encode(value));
        BedrockItemExtraData decoded = BedrockItemExtraDataCodec.Decode(expected, hasBlockingTick: true);
        Assert.Equal(tag, decoded.Tag);
        Assert.Equal(7, decoded.BlockingTick);
    }
}
