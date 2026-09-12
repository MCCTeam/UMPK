using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Login;

public class LoginCustomQueryBindingTests
{
    [Theory]
    [InlineData(393)]
    [InlineData(401)]
    [InlineData(404)]
    [InlineData(477)]
    public void LoginCustomQuery_RoundTripsChannelAndData(int protocol)
    {
        var bound = BoundCodec.At(protocol, ProtocolPhase.Login, PacketFlow.Clientbound, "minecraft:custom_query");
        var packet = new ClientboundLoginCustomQueryPacket(9, Identifier.Parse("fml:handshake"), [0x01, 0x02, 0x03]);
        byte[] wire = bound.Encode(packet);
        Assert.Equal(0x09, wire[0]);
        Assert.Equal([0x01, 0x02, 0x03], wire[^3..]);
        var back = Assert.IsType<ClientboundLoginCustomQueryPacket>(bound.DecodeFrame(wire));
        Assert.Equal(9, back.TransactionId);
        Assert.Equal(Identifier.Parse("fml:handshake"), back.Channel);
        Assert.Equal<byte[]>([0x01, 0x02, 0x03], back.Data);
    }
}
