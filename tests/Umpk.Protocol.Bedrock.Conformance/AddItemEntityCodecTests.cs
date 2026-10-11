using Xunit;

namespace Umpk.Protocol.Bedrock.Conformance;

public sealed class AddItemEntityCodecTests
{
    [Fact]
    public void AddItemEntityHasPrimarySchemaClientboundWitness()
    {
        var packet = new BedrockAddItemEntityPacket(
            10L,
            3UL,
            new BedrockItemInstance(306, 5, 0U, 2, 0U, []),
            new BedrockVector3(1f, 2f, 3f),
            new BedrockVector3(0f, 1f, 0f),
            [new BedrockMetadataEntry(0U, 7U, 0, BedrockMetadataValue.FromLong(0L))],
            false);
        byte[] payload = BedrockActorCodecs.Encode(packet);

        BedrockAddItemEntityPacket decoded = Assert.IsType<BedrockAddItemEntityPacket>(Decode(15, BedrockPacketDirection.Clientbound, payload, out Bedrock2169CodecBinding binding));

        Assert.Equal(10L, decoded.UniqueEntityId);
        Assert.Equal(3UL, decoded.RuntimeEntityId);
        Assert.Equal(306, decoded.Item.NetworkId);
        Assert.False(decoded.IsFromFishing);
        Assert.Equal(BedrockPacketDirection.Clientbound, binding.ExpectedDirection);
        Assert.Equal(payload, BedrockActorCodecs.Encode(decoded));
    }

    private static object Decode(int packetId, BedrockPacketDirection direction, byte[] payload, out Bedrock2169CodecBinding binding)
    {
        var profile = new BedrockProtocolProfile(
            new GameVersion(GameEdition.Bedrock, "1.26.45", 2169),
            BedrockPacketDialect.Base2168,
            new BedrockDataProfileId("bedrock-1.26.45-2169"),
            new BedrockBehaviorProfileId("bedrock-1.26.45-2169"));
        var context = new BedrockSessionCodecContext(
            new BedrockCodecContext(profile, direction, default, default),
            BedrockRegistrySnapshot.Empty,
            BedrockCodecLimits.Default);
        var member = new BedrockBatchMember(new BedrockPacketHeader(packetId, default, default), payload);
        return Bedrock2169CodecRegistry.Decode(member, context, out binding);
    }
}
