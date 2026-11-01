using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Entity;

/// <summary>Original native 26.1/26.3 packet decoding maps out-of-range hand ids to main hand and re-encodes canonical bytes.</summary>
public sealed class Interact775HandFallbackTests
{
    [Theory]
    [InlineData(775, -1)]
    [InlineData(775, 2)]
    [InlineData(776, -1)]
    [InlineData(776, 2)]
    [InlineData(777, -1)]
    [InlineData(777, 2)]
    public void OutOfRangeWireHandUsesNativeMainHandFallback(int protocol, int rawHand)
    {
        byte[] original = Convert.FromHexString(rawHand == -1 ? "ac02ffffffff0f0001" : "ac02020001");
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Serverbound, "interact");
        var packet = Assert.IsType<ServerboundInteractPacket>(bound.DecodeFrame(original));
        Assert.Equal(300, packet.EntityId);
        Assert.Equal(0, packet.Action);
        Assert.Equal(0, packet.Hand);
        Assert.Equal(Vec3d.Zero, packet.InteractAt);
        Assert.True(packet.UsingSecondaryAction);
        Assert.Equal(Convert.FromHexString("ac02000001"), bound.Encode(packet));
    }
}
