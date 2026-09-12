using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Transport;

public sealed class VehicleInputWireShapeTests
{
    [Theory]
    [InlineData(107)]
    [InlineData(404)]
    public void MoveVehicle_HasNoGroundFlag(int protocol)
    {
        byte[] f = [
            0x3f, 0xf8, 0, 0, 0, 0, 0, 0, 0x40, 0x50, 0, 0, 0,    0,    0, 0,
            0xc0, 0x02, 0, 0, 0, 0, 0, 0, 0x42, 0xb4, 0, 0, 0xc1, 0x20, 0, 0
        ];
        var p = Assert.IsType<ServerboundMoveVehiclePacket>(
            BoundCodec.At(protocol, PacketFlow.Serverbound, "minecraft:move_vehicle").DecodeFrame(f));
        Assert.Equal(1.5, p.X);
        Assert.Equal(-2.25, p.Z);
        Assert.Equal(90f, p.Yaw);
        Assert.Equal(f, BoundCodec.At(protocol, PacketFlow.Serverbound, "minecraft:move_vehicle").Encode(p));
    }

    [Theory]
    [InlineData(107)]
    [InlineData(404)]
    public void PaddleBoat_DecodesBothBooleans(int protocol)
    {
        var p = Assert.IsType<ServerboundPaddleBoatPacket>(
            BoundCodec.At(protocol, PacketFlow.Serverbound, "minecraft:paddle_boat").DecodeFrame([1, 0]));
        Assert.True(p.Left);
        Assert.False(p.Right);
    }

    [Theory]
    [InlineData(107)]
    [InlineData(404)]
    public void PlayerInput_UsesSteerBody(int protocol)
    {
        byte[] f = [0xbf, 0x7a, 0xe1, 0x48, 0x3f, 0x7a, 0xe1, 0x48, 2];
        var p = Assert.IsType<ServerboundSteerVehiclePacket>(
            BoundCodec.At(protocol, PacketFlow.Serverbound, "minecraft:player_input").DecodeFrame(f));
        Assert.Equal(-0.98f, p.Strafe);
        Assert.Equal(0.98f, p.Forward);
        Assert.Equal(2, p.Flags);
        Assert.Equal(f, BoundCodec.At(protocol, PacketFlow.Serverbound, "minecraft:player_input").Encode(p));
    }

    [Theory]
    [InlineData(107)]
    [InlineData(404)]
    public void PlayerAbilities_DecodesBothFlows(int protocol)
    {
        byte[] f = [0x0d, 0x3d, 0x4c, 0xcc, 0xcd, 0x3d, 0xcc, 0xcc, 0xcd];
        var cb = Assert.IsType<ClientboundPlayerAbilitiesPacket>(
            BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:player_abilities").DecodeFrame(f));
        Assert.Equal(0x0d, cb.Flags);
        Assert.Equal(0.05f, cb.FlyingSpeed);
        var sb = Assert.IsType<ServerboundLegacyPlayerAbilitiesPacket>(
            BoundCodec.At(protocol, PacketFlow.Serverbound, "minecraft:player_abilities").DecodeFrame(f));
        Assert.Equal(0.1f, sb.WalkingSpeed);
    }
}
