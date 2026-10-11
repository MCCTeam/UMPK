using Xunit;

namespace Umpk.Protocol.Bedrock.Conformance;

public sealed class VerifiedFieldCodecTests
{
    [Fact]
    public void PlayerInputListKeepsPresenceOrderSignedValuesAndUnsignedTick()
    {
        var value = new PlayerAuthInputListSlice([0, -1, 2], 300);
        byte[] expected = Convert.FromHexString("0103000104ac02");

        Assert.Equal(expected, BedrockVerifiedFieldCodecs.Encode(value));
        PlayerAuthInputListSlice decoded = BedrockVerifiedFieldCodecs.DecodePlayerAuthInputListSlice(expected);
        Assert.Equal(value.Tick, decoded.Tick);
        Assert.Equal(value.Inputs, decoded.Inputs);
    }

    [Fact]
    public void ItemV4KeepsMixedWidthsAndOpaqueExtraBoundary()
    {
        var value = new ItemV4WireValue(-2, 3, 300, -7, 128, [0x0a, 0x00]);
        byte[] expected = Convert.FromHexString("feff0300ac02010d8001020a00");

        Assert.Equal(expected, BedrockVerifiedFieldCodecs.Encode(value));
        Assert.Equal(value, BedrockVerifiedFieldCodecs.DecodeItemV4(expected));
    }

    [Fact]
    public void StackRequestKeepsOuterAndLegacyDiscriminators()
    {
        var value = new StackRequestActionHeader(300, 0x7f);
        byte[] expected = Convert.FromHexString("ac027f");

        Assert.Equal(expected, BedrockVerifiedFieldCodecs.Encode(value));
        Assert.Equal(value, BedrockVerifiedFieldCodecs.DecodeStackRequestActionHeader(expected));
    }

    [Fact]
    public void StackRequestSlotUsesFixedLittleEndianNetworkId()
    {
        var value = new StackRequestSlotWireValue(4, 5, 0x01020304);
        byte[] expected = Convert.FromHexString("040504030201");

        Assert.Equal(expected, BedrockVerifiedFieldCodecs.Encode(value));
        Assert.Equal(value, BedrockVerifiedFieldCodecs.DecodeStackRequestSlot(expected));
    }
}
