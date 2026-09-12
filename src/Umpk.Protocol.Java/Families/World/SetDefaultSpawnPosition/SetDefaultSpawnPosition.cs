using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Default spawn position (<c>minecraft:set_default_spawn_position</c>).</summary>
        public static readonly PacketType<ClientboundSetDefaultSpawnPositionPacket> SetDefaultSpawnPosition =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("set_default_spawn_position"));
    }
}

/// <summary>Default spawn position. 1.8/1.21.5 carry a packed <see cref="Position"/> plus a single spawn <see cref="Angle"/>. 26.2 adds the spawn dimension key and splits the angle into yaw and pitch.</summary>
public sealed record ClientboundSetDefaultSpawnPositionPacket(
    BlockPos Position,
    float Angle,
    string? Dimension,
    float Pitch) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.SetDefaultSpawnPosition;
}
