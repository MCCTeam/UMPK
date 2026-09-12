using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Entity;

public class ExperienceOrbBindingTests
{
    [Theory]
    [InlineData(107)]
    [InlineData(404)]
    [InlineData(477)]
    [InlineData(578)]
    [InlineData(735)]
    [InlineData(754)]
    [InlineData(758)]
    [InlineData(763)]
    [InlineData(767)]
    [InlineData(769)]
    public void AddExperienceOrb_RoundTripsStableWireForm(int protocol)
    {
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:add_experience_orb");
        var packet = new ClientboundAddExperienceOrbPacket(0x1234, 133.5, 65.0, -37.75, 1234);
        byte[] wire = bound.Encode(packet);
        Assert.Equal(2 + (8 * 3) + 2, wire.Length);
        Assert.Equal(packet, bound.DecodeFrame(wire));
    }
}
