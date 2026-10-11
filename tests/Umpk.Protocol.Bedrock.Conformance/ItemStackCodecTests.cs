using Xunit;

namespace Umpk.Protocol.Bedrock.Conformance;

public sealed class ItemStackCodecTests
{
    [Fact]
    public void ItemStackRequestBindsServerboundWithExactWitness()
    {
        byte[] payload = Convert.FromHexString("0102010000050C0000070000003B0000000000000000000000");

        var packet = Assert.IsType<BedrockItemStackRequestPacket>(Decode(147, BedrockPacketDirection.Serverbound, payload, out Bedrock2169CodecBinding binding));

        Assert.Equal(BedrockPacketDirection.Serverbound, binding.ExpectedDirection);
        Assert.Single(packet.Requests);
        Assert.Equal(1, packet.Requests[0].RequestId);
        Assert.Equal(payload, BedrockItemStackCodecs.Encode(packet));
    }

    [Fact]
    public void ItemStackResponseBindsClientboundWithExactWitness()
    {
        byte[] payload = Convert.FromHexString("0100020101010C0001050503010112000000");

        var packet = Assert.IsType<BedrockItemStackResponsePacket>(Decode(148, BedrockPacketDirection.Clientbound, payload, out Bedrock2169CodecBinding binding));

        Assert.Equal(BedrockPacketDirection.Clientbound, binding.ExpectedDirection);
        Assert.Single(packet.Responses);
        Assert.True(packet.Responses[0].IsOk);
        Assert.Equal(1, packet.Responses[0].RequestId);
        Assert.Equal(payload, BedrockItemStackCodecs.Encode(packet));
    }

    [Fact]
    public void ItemStackResponseErrorBindsClientboundWithExactWitness()
    {
        byte[] payload = Convert.FromHexString("0101040100");

        var packet = Assert.IsType<BedrockItemStackResponsePacket>(Decode(148, BedrockPacketDirection.Clientbound, payload, out Bedrock2169CodecBinding binding));

        Assert.Equal(BedrockPacketDirection.Clientbound, binding.ExpectedDirection);
        Assert.False(packet.Responses[0].IsOk);
        Assert.Equal(payload, BedrockItemStackCodecs.Encode(packet));
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
