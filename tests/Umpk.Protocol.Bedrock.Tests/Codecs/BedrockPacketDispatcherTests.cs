using Umpk.Nbt;
using Xunit;

namespace Umpk.Protocol.Bedrock.Tests.Codecs;

public sealed class BedrockPacketDispatcherTests
{
    public static TheoryData<int> ToleratedPacketIds
    {
        get
        {
            var data = new TheoryData<int>();
            foreach (Bedrock2169ToleratedPacket packet in Bedrock2169JoinTolerance.Packets)
                data.Add(packet.PacketId);
            return data;
        }
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(11)]
    [InlineData(119)]
    [InlineData(122)]
    [InlineData(156)]
    public void DispatcherDecodesJoinPacketsThroughGeneratedRegistry(int packetId)
    {
        Assert.True(Bedrock2169CodecRegistry.TryGet(packetId, out Bedrock2169CodecBinding binding));
        Assert.NotNull(binding.Decoder);
    }

    [Fact]
    public void DispatcherDecodesJoinPacketsEndToEnd()
    {
        var info = new ResourcePacksInfoPacket(false, false, false, false, Guid.Empty, string.Empty, []);
        var stack = new ResourcePackStackPacket(false, [], string.Empty, [], false, false);
        var start = new StartGamePacket(7L, 9UL, [0x01]);
        var observed = new List<object>();
        var dispatcher = new BedrockPacketDispatcher((packet, _) => observed.Add(packet));

        dispatcher.Consume(Member(6, BedrockJoinCodecs.Encode(info)), Context());
        dispatcher.Consume(Member(7, BedrockJoinCodecs.Encode(stack)), Context());
        dispatcher.Consume(Member(11, BedrockJoinCodecs.Encode(start)), Context());

        Assert.Equal([info, stack, start], observed);
    }

    [Fact]
    public void DispatcherPublishesEntityIdentifiersBeforeNextMember()
    {
        var pig = new NbtCompound();
        pig.PutString("id", "minecraft:pig");
        var list = new NbtList(NbtTagType.Compound) { pig };
        var root = new NbtCompound();
        root.Put("idlist", list);
        var members = new[]
        {
            Member(119, BedrockJoinCodecs.Encode(new AvailableEntityIdentifiersPacket(root))),
            Member(5, BedrockBootstrapCodecs.Encode(new DisconnectPacket(0, true, null, null))),
        };
        bool observed = false;
        var dispatcher = new BedrockPacketDispatcher((packet, context) =>
        {
            if (packet is DisconnectPacket)
                observed = context.Registries.Entities.TryGetValue("minecraft:pig", out int index) && index == 0;
        });
        var context = Context();

        foreach (BedrockBatchMember member in members)
        {
            BedrockRegistrySnapshot? replacement = dispatcher.Consume(member, context);
            if (replacement is not null)
                context = context with { Registries = replacement };
        }

        Assert.True(observed);
        Assert.Equal(1, context.Registries.Generation);
    }

    [Fact]
    public void DispatcherRejectsUnknownPacketId()
    {
        var dispatcher = new BedrockPacketDispatcher((_, _) => { });

        Assert.Throws<BedrockCodecException>(() => dispatcher.Consume(Member(250, [0x00]), Context()));
    }

    [Theory]
    [MemberData(nameof(ToleratedPacketIds))]
    public void DispatcherToleratesObservedAmbientPacketsWithoutRetainingPayload(int packetId)
    {
        object? observed = null;
        var dispatcher = new BedrockPacketDispatcher((packet, _) => observed = packet);

        BedrockRegistrySnapshot? replacement = dispatcher.Consume(Member(packetId, [0x01, 0x02]), Context());

        Assert.Null(replacement);
        var tolerated = Assert.IsType<BedrockToleratedPacket>(observed);
        Assert.Equal(packetId, tolerated.PacketId);
        Assert.Equal(2, tolerated.PayloadBytes);
    }

    [Fact]
    public void ToleratedPacketsNeverCollideWithBoundCodecs()
    {
        foreach (Bedrock2169ToleratedPacket packet in Bedrock2169JoinTolerance.Packets)
            Assert.False(Bedrock2169CodecRegistry.TryGet(packet.PacketId, out _), $"Tolerated packet {packet.PacketId} collides with a bound codec.");
    }

    [Fact]
    public void DispatcherRejectsJoinPacketUnderWrongDirection()
    {
        var dispatcher = new BedrockPacketDispatcher((_, _) => { });
        var profile = new BedrockProtocolProfile(
            new GameVersion(GameEdition.Bedrock, "1.26.45", 2169),
            BedrockPacketDialect.Base2168,
            new BedrockDataProfileId("bedrock-1.26.45-2169"),
            new BedrockBehaviorProfileId("bedrock-1.26.45-2169"));
        var routing = new BedrockCodecContext(profile, BedrockPacketDirection.Serverbound, default, default);
        var context = new BedrockSessionCodecContext(routing, BedrockRegistrySnapshot.Empty, BedrockCodecLimits.Default);

        Assert.Throws<BedrockCodecException>(() => dispatcher.Consume(Member(6, [0x00]), context));
    }

    private static BedrockBatchMember Member(int packetId, byte[] payload) =>
        new(new BedrockPacketHeader(packetId, default, default), payload);

    private static BedrockSessionCodecContext Context()
    {
        var profile = new BedrockProtocolProfile(
            new GameVersion(GameEdition.Bedrock, "1.26.45", 2169),
            BedrockPacketDialect.Base2168,
            new BedrockDataProfileId("bedrock-1.26.45-2169"),
            new BedrockBehaviorProfileId("bedrock-1.26.45-2169"));
        var routing = new BedrockCodecContext(profile, BedrockPacketDirection.Clientbound, default, default);
        return new BedrockSessionCodecContext(routing, BedrockRegistrySnapshot.Empty, BedrockCodecLimits.Default);
    }
}
