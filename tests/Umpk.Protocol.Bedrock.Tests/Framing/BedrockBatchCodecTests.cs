using Xunit;

namespace Umpk.Protocol.Bedrock.Tests.Framing;

public sealed class BedrockBatchCodecTests
{
    [Fact]
    public void HeaderUsesTenBitPacketAndTwoBitSubclients()
    {
        var header = new BedrockPacketHeader(1023, default, default);

        Assert.Equal("ff07", Convert.ToHexString(BedrockBatchCodec.EncodeMember(header, [])).ToLowerInvariant()[2..]);
        BedrockBatchMember decoded = Assert.Single(BedrockBatchCodec.DecodeMembers(Convert.FromHexString("02ff07"), BedrockCodecLimits.Default));
        Assert.Equal(1023, decoded.Header.PacketId);
    }

    [Fact]
    public void ManyMembersRetainBoundariesAndRouting()
    {
        BedrockBatchMember[] members =
        [
            new(new BedrockPacketHeader(1, default, default), [0xaa]),
            new(new BedrockPacketHeader(193, default, default), [0xbb, 0xcc]),
        ];

        byte[] encoded = BedrockBatchCodec.EncodeMembers(members, BedrockCodecLimits.Default);
        IReadOnlyList<BedrockBatchMember> decoded = BedrockBatchCodec.DecodeMembers(encoded, BedrockCodecLimits.Default);

        Assert.Equal(2, decoded.Count);
        Assert.Equal([0xaa], decoded[0].Payload);
        Assert.Equal([0xbb, 0xcc], decoded[1].Payload);
    }

    [Theory]
    [InlineData("00")]
    [InlineData("05c101")]
    [InlineData("018040")]
    public void InvalidMemberLengthsOrHeaderBitsFail(string hex)
    {
        Assert.Throws<BedrockCodecException>(() => BedrockBatchCodec.DecodeMembers(Convert.FromHexString(hex), BedrockCodecLimits.Default));
    }

    [Fact]
    public void UnsupportedSubclientsFailBeforeDispatch()
    {
        byte[] encoded = BedrockBatchCodec.EncodeMember(new BedrockPacketHeader(1, new BedrockSubclientId(1), default), []);

        Assert.Throws<BedrockCodecException>(() => BedrockBatchCodec.DecodeMembers(encoded, BedrockCodecLimits.Default));
    }
}
