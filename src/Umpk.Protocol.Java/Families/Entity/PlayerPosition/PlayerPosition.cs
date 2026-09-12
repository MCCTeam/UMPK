using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Clientbound
    {
        /// <summary>Player position/teleport (<c>minecraft:player_position</c>).</summary>
        public static readonly PacketType<ClientboundPlayerPositionPacket> PlayerPosition =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("player_position"));
    }
}

/// <summary>Player position/teleport. 1.8: double position, float rotation, relative-flag byte. Modern: VarInt teleport id, a <see cref="PositionMoveRotation"/>, and a relative-flag bitset.</summary>
/// <remarks><c>RelativeFlags</c> is the relative-movement bitset: bit 0 X, 1 Y, 2 Z, 3 Y_ROT, 4 X_ROT, 5 DELTA_X, 6 DELTA_Y, 7 DELTA_Z, 8 ROTATE_DELTA. It is an <see cref="int"/> because that is what the modern wire carries: narrowing it to a byte drops bit 8 (<c>ROTATE_DELTA</c>) and everything above it. The 1.8, 1.9-1.21.1 and 1.19-1.19.3 wires genuinely carry a single byte holding only bits 0-4, and their codecs reject a value that does not fit rather than truncating it.</remarks>
public sealed record ClientboundPlayerPositionPacket(
    double X,
    double Y,
    double Z,
    float Yaw,
    float Pitch,
    int RelativeFlags,
    int? TeleportId,
    PositionMoveRotation? ModernValues) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Clientbound.PlayerPosition;
}
