using Umpk.Nbt;
using Xunit;

namespace Umpk.Protocol.Bedrock.Conformance;

public sealed class ItemRegistryCodecTests
{
    [Fact]
    public void ItemRegistryMatchesIndependentNetworkNbtBytesAndPublishesAtomically()
    {
        var item = new BedrockItemState("minecraft:stone", 1, false, 2, new NbtCompound());
        var packet = new BedrockItemRegistryPacket([item]);
        byte[] expected = Convert.FromHexString("010f6d696e6563726166743a73746f6e65010000040a0000");

        Assert.Equal(expected, BedrockItemRegistryCodec.Encode(packet));
        BedrockItemRegistryPacket decoded = BedrockItemRegistryCodec.Decode(expected);
        BedrockRegistrySnapshot published = BedrockItemRegistryCodec.Publish(decoded, BedrockRegistrySnapshot.Empty);

        Assert.Equal(1, published.Generation);
        Assert.Equal("minecraft:stone", published.Items[1]);
    }
}
