using Umpk.Nbt;
using Xunit;

namespace Umpk.Protocol.Bedrock.Tests.Nbt;

public sealed class BedrockNbtCodecTests
{
    private static readonly NbtCompound Value = CreateValue();

    [Fact]
    public void NetworkRootCompoundMatchesIndependentUtf8Bytes() =>
        AssertRootBytes(BedrockNbtWireFormat.NetworkLittleVarInt, "0a00030178d80408017302c3a900");

    [Fact]
    public void LittleEndianRootCompoundMatchesIndependentUtf8Bytes() =>
        AssertRootBytes(BedrockNbtWireFormat.LittleEndian, "0a0000030100782c010000080100730200c3a900");

    private static void AssertRootBytes(BedrockNbtWireFormat format, string hex)
    {
        byte[] expected = Convert.FromHexString(hex);

        byte[] encoded = BedrockNbtCodec.Encode(Value, format);
        NbtTag decoded = BedrockNbtCodec.Decode(expected, format, BedrockCodecLimits.Default);

        Assert.Equal(expected, encoded);
        Assert.Equal(Value, decoded);
    }

    [Fact]
    public void NegativeFixedArrayLengthFailsBeforeAllocation()
    {
        byte[] bytes = Convert.FromHexString("0b0000ffffffff");

        Assert.Throws<BedrockCodecException>(() => BedrockNbtCodec.Decode(bytes, BedrockNbtWireFormat.LittleEndian, BedrockCodecLimits.Default));
    }

    [Fact]
    public void NestedListHonorsDepthLimit()
    {
        var list = new NbtList();
        list.Add(new NbtList());
        byte[] bytes = BedrockNbtCodec.Encode(list, BedrockNbtWireFormat.NetworkLittleVarInt);
        var limits = BedrockCodecLimits.Default with { MaxNbtDepth = 1 };

        Assert.Throws<NbtDepthLimitException>(() => BedrockNbtCodec.Decode(bytes, BedrockNbtWireFormat.NetworkLittleVarInt, limits));
    }

    [Fact]
    public void MixedListsUseCompoundWrappersWithoutLosingValues()
    {
        var list = new NbtList { new NbtString("a"), new NbtInt(2) };

        byte[] bytes = BedrockNbtCodec.Encode(list, BedrockNbtWireFormat.NetworkLittleVarInt);
        NbtTag decoded = BedrockNbtCodec.Decode(bytes, BedrockNbtWireFormat.NetworkLittleVarInt, BedrockCodecLimits.Default);

        Assert.Equal(list, decoded);
    }

    private static NbtCompound CreateValue()
    {
        var value = new NbtCompound();
        value.PutInt("x", 300);
        value.PutString("s", "é");
        return value;
    }
}
