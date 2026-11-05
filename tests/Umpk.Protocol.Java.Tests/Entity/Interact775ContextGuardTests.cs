using Umpk.Geometry;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Entity;

/// <summary>Required 26.1+ packet context and a native LP prefix that the legacy action layout incorrectly accepts.</summary>
public sealed class Interact775ContextGuardTests
{
    [Theory]
    [InlineData(775, 0)]
    [InlineData(775, 1)]
    [InlineData(775, 2)]
    [InlineData(775, 3)]
    [InlineData(775, 4)]
    [InlineData(775, 5)]
    [InlineData(775, 6)]
    [InlineData(776, 0)]
    [InlineData(776, 1)]
    [InlineData(776, 2)]
    [InlineData(776, 3)]
    [InlineData(776, 4)]
    [InlineData(776, 5)]
    [InlineData(776, 6)]
    [InlineData(777, 0)]
    [InlineData(777, 1)]
    [InlineData(777, 2)]
    [InlineData(777, 3)]
    [InlineData(777, 4)]
    [InlineData(777, 5)]
    [InlineData(777, 6)]
    public void MissingOrUnrepresentableContextCannotBecomeAnInteraction(int protocol, int kind)
    {
        var known = new ServerboundInteractPacket(300, 0, 0, Vec3d.Zero, false);
        ServerboundInteractPacket packet = kind switch
        {
            0 => known with { Action = -1 },
            1 => known with { Action = 1 },
            2 => known with { Action = 2 },
            3 => known with { Hand = null },
            4 => known with { Hand = -1 },
            5 => known with { Hand = 2 },
            6 => known with { InteractAt = null },
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Serverbound, "interact");
        Assert.Throws<ProtocolViolationException>(() => bound.Encode(packet));
    }

    [Theory]
    [InlineData(775)]
    [InlineData(776)]
    [InlineData(777)]
    public void NativeLpPrefixCannotBeAcceptedAsTheLegacyActionLayout(int protocol)
    {
        // Original native unit-Y/main-hand/false body; seven bytes are insufficient for LP plus its final flag.
        byte[] complete = Convert.FromHexString("ac0200f9ff7ffffff900");
        BoundPacketCodec bound = BoundCodec.At(protocol, PacketFlow.Serverbound, "interact");
        Assert.Throws<ProtocolViolationException>(() => bound.DecodeFrame(complete.AsSpan(0, 7).ToArray()));
    }
}
