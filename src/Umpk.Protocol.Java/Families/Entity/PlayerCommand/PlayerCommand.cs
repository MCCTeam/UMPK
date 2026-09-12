using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Serverbound
    {
        /// <summary>Entity action / player command (<c>minecraft:player_command</c>).</summary>
        public static readonly PacketType<ServerboundPlayerCommandPacket> PlayerCommand =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("player_command"));
    }
}

/// <summary>Entity action / player command: entity id, action VarInt, data VarInt.</summary>
public sealed record ServerboundPlayerCommandPacket(int EntityId, int Action, int Data) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Serverbound.PlayerCommand;
}
