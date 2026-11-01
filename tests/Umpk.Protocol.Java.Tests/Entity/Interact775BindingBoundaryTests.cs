using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Entity;

// Source-only preview. The expected bodies require an original-class native witness before acceptance.
public sealed class Interact775BindingBoundaryTests
{
    [Theory]
    [InlineData(775, false)]
    [InlineData(775, true)]
    [InlineData(776, false)]
    [InlineData(776, true)]
    [InlineData(777, false)]
    [InlineData(777, true)]
    public void MandatoryLocationReplacesTheLegacyActionField(int protocol, bool nonzero)
    {
        Vec3d location = nonzero ? new(0, 1, 0) : Vec3d.Zero;
        byte[] expected = nonzero
            ? [0xAC, 0x02, 0x01, 0xF9, 0xFF, 0x7F, 0xFF, 0xFF, 0xF9, 0x01]
            : [0xAC, 0x02, 0x01, 0x00, 0x01];
        var packet = new ServerboundInteractPacket(300, Action: 0, Hand: 1, InteractAt: location, UsingSecondaryAction: true);
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Serverbound, "interact");
        Assert.Equal(expected, bound.Encode(packet));
        var decoded = Assert.IsType<ServerboundInteractPacket>(bound.DecodeFrame(expected));
        Assert.Equal(300, decoded.EntityId);
        Assert.Equal(0, decoded.Action);
        Assert.Equal(1, decoded.Hand);
        Assert.Equal(location, decoded.InteractAt);
        Assert.True(decoded.UsingSecondaryAction);
        Assert.Equal(expected, bound.Encode(decoded));
    }

    [Fact]
    public void Adjacent774RetainsItsActionAndHandLayout()
    {
        var packet = new ServerboundInteractPacket(300, Action: 0, Hand: 1, InteractAt: null, UsingSecondaryAction: true);
        BoundPacketCodec bound = BoundCodec.At(774, PacketFlow.Serverbound, "interact");
        byte[] expected = [0xAC, 0x02, 0x00, 0x01, 0x01];
        Assert.Equal(expected, bound.Encode(packet));
        Assert.Equal(packet, Assert.IsType<ServerboundInteractPacket>(bound.DecodeFrame(expected)));
        Assert.Equal(expected, bound.Encode(bound.DecodeFrame(expected)));
    }
}
