using System.Buffers;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Tests.Support;
using Umpk.Text;
using Xunit;
using static Umpk.Protocol.Java.Tests.Support.LiteralFrame;

namespace Umpk.Protocol.Java.Tests.Transport;

/// <summary>Rejects incompatible vehicle-input wire layouts.</summary>
public sealed class VehicleInputCompatibilityTests
{
    [Fact]
    public void MoveVehicle_Rejects_TheOnGroundBool()
    {
        byte[] withOnGround = Cat(F64(1.5), F64(64.0), F64(-2.25), F32(90f), F32(-10f), [1]);
        Rejects(Serverbound(107, "move_vehicle"), withOnGround, "1.9-1.13.2 move_vehicle ends after the pitch float");
    }
}
