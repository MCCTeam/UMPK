using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Serverbound
    {
        /// <summary>1.8 steer vehicle (<c>minecraft:steer_vehicle</c>).</summary>
        public static readonly PacketType<ServerboundSteerVehiclePacket> SteerVehicle =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("steer_vehicle"));
    }
}

/// <summary>1.8 steer vehicle: strafe float, forward float, flags byte (jump 0x01, unmount 0x02).</summary>
public sealed record ServerboundSteerVehiclePacket(float Strafe, float Forward, byte Flags) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Serverbound.SteerVehicle;
}
