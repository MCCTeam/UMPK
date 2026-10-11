using Xunit;

namespace Umpk.Protocol.Bedrock.Conformance;

public sealed class CreativeContentCodecTests
{
    [Fact]
    public void CreativeContentBindsClientboundWithExactWitness()
    {
        byte[] payload = Convert.FromHexString("01010174000000000000010102050000140000");

        var packet = Assert.IsType<BedrockCreativeContentPacket>(Decode(145, BedrockPacketDirection.Clientbound, payload, out Bedrock2169CodecBinding binding));

        Assert.Equal(BedrockPacketDirection.Clientbound, binding.ExpectedDirection);
        Assert.Single(packet.Groups);
        Assert.Single(packet.Entries);
        Assert.Equal(1, packet.Entries[0].EntryId);
        Assert.Equal(1, packet.Entries[0].Item.NetworkId);
        Assert.Equal(payload, BedrockCreativeContentCodecs.Encode(packet));
    }

    private static object Decode(int packetId, BedrockPacketDirection direction, byte[] payload, out Bedrock2169CodecBinding binding)
    {
        BedrockSessionCodecContext context = CreateContext(direction);
        var member = new BedrockBatchMember(new BedrockPacketHeader(packetId, default, default), payload);
        return Bedrock2169CodecRegistry.Decode(member, context, out binding);
    }

    private static BedrockSessionCodecContext CreateContext(BedrockPacketDirection direction)
    {
        var profile = new BedrockProtocolProfile(
            new GameVersion(GameEdition.Bedrock, "1.26.45", 2169),
            BedrockPacketDialect.Base2168,
            new BedrockDataProfileId("bedrock-1.26.45-2169"),
            new BedrockBehaviorProfileId("bedrock-1.26.45-2169"));
        return new BedrockSessionCodecContext(
            new BedrockCodecContext(profile, direction, default, default),
            BedrockRegistrySnapshot.Empty,
            BedrockCodecLimits.Default);
    }
}
