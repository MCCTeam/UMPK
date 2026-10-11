using Xunit;

namespace Umpk.Protocol.Bedrock.Conformance;

public sealed class InventoryTransactionCodecTests
{
    [Fact]
    public void InventoryTransactionBindsBidirectionalWithExactWitness()
    {
        byte[] payload = Convert.FromHexString("0000010000");

        var packet = Assert.IsType<BedrockInventoryTransactionPacket>(Decode(30, BedrockPacketDirection.Clientbound, payload, out Bedrock2169CodecBinding binding));

        Assert.Null(binding.ExpectedDirection);
        Assert.Equal(BedrockInventoryTransactionType.Normal, packet.TransactionType);
        Assert.Equal(payload, BedrockInventoryTransactionCodecs.Encode(packet));
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
