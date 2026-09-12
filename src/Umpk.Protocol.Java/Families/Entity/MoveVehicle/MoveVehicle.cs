using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Serverbound
    {
        /// <summary>Vehicle move (<c>minecraft:move_vehicle</c>).</summary>
        public static readonly PacketType<ServerboundMoveVehiclePacket> MoveVehicle =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("move_vehicle"));
    }
}

/// <summary>Vehicle move (serverbound): x/y/z double, yaw/pitch float, on-ground bool.</summary>
public sealed record ServerboundMoveVehiclePacket(double X, double Y, double Z, float Yaw, float Pitch, bool OnGround) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Serverbound.MoveVehicle;
}
