using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Session;

public class SessionPacketBindingTests
{
    [Theory]
    [InlineData(393)]
    [InlineData(401)]
    [InlineData(404)]
    [InlineData(477)]
    public void StopSound_RoundTripsOptionalFields(int protocol)
    {
        var bound = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:stop_sound");
        var packet = new ClientboundStopSoundPacket(3, "minecraft:entity.pig.ambient");
        byte[] wire = bound.Encode(packet);
        Assert.Equal(0x03, wire[0]);
        Assert.Equal(0x03, wire[1]);
        Assert.Equal(packet, bound.DecodeFrame(wire));
        var nameOnly = new ClientboundStopSoundPacket(null, "minecraft:block.anvil.land");
        Assert.Equal(0x02, bound.Encode(nameOnly)[0]);
        Assert.Equal(nameOnly, bound.DecodeFrame(bound.Encode(nameOnly)));
    }

    [Fact]
    public void ServerData_OptionalMotdAndPreviewFlagFrame() =>
        Assert.Equal(ServerDataFrame(true, false),
                     BoundCodec.At(759, PacketFlow.Clientbound, "minecraft:server_data").Encode(LegacyServerData()));
    [Fact]
    public void ServerData_ContainsPreviewAndSecureChatFlags() =>
        Assert.Equal(ServerDataFrame(true, true),
                     BoundCodec.At(760, PacketFlow.Clientbound, "minecraft:server_data").Encode(LegacyServerData()));
    [Fact]
    public void ServerData_ContainsSecureChatFlagOnly() =>
        Assert.Equal(ServerDataFrame(false, true),
                     BoundCodec.At(761, PacketFlow.Clientbound, "minecraft:server_data").Encode(LegacyServerData()));
    [Theory]
    [InlineData(759)]
    [InlineData(760)]
    [InlineData(761)]
    public void ServerData_RoundTripsIconAndFlags(int protocol)
    {
        var bound = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:server_data");
        var back = Assert.IsType<ClientboundServerDataPacket>(bound.DecodeFrame(bound.Encode(LegacyServerData())));
        Assert.True(back.HasMotd);
        Assert.Equal("UMPK L4 corpus", back.Motd.ToPlainText());
        Assert.Equal("iVBORw0KGgo=", back.IconBase64);
        Assert.Null(back.IconBytes);
        Assert.Equal(protocol != 761, back.PreviewsChat);
        Assert.Equal(protocol != 759, back.EnforcesSecureChat);
    }

    [Theory]
    [InlineData(762)]
    [InlineData(763)]
    public void ServerData_UsesDirectMotdBody(int protocol)
    {
        var bound = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:server_data");
        var packet = new ClientboundServerDataPacket(Umpk.Text.Component.Text("UMPK L4 corpus"),
                                                     null)
        {
            EnforcesSecureChat = true,
            MotdJsonVerbatim = "{\"text\":\"UMPK L4 corpus\"}"
        };
        byte[] wire = bound.Encode(packet);
        Assert.Equal(0x19, wire[0]);
        Assert.Equal(28, wire.Length);
        WireFrameAssertions.DoesNotRoundTrip(BoundCodec.At(759, PacketFlow.Clientbound, "minecraft:server_data"), wire);
    }
    private static ClientboundServerDataPacket LegacyServerData() => new(Umpk.Text.Component.Text("UMPK L4 corpus"),
                                                                         null)
    {
        HasMotd = true,
        MotdJsonVerbatim = "{\"text\":\"UMPK L4 corpus\"}",
        IconBase64 = "iVBORw0KGgo=",
        PreviewsChat = true,
        EnforcesSecureChat = true
    };
    private static byte[] ServerDataFrame(bool previews, bool secure)
    {
        var w = new WireFrameAssertions.FrameWriter();
        w.U8(1);
        w.Str("{\"text\":\"UMPK L4 corpus\"}");
        w.U8(1);
        w.Str("iVBORw0KGgo=");
        if (previews)
            w.U8(1);
        if (secure)
            w.U8(1);
        return w.ToArray();
    }
}
