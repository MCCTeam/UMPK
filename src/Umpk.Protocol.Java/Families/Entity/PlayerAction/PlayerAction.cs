using Umpk.Game.Entities;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Protocol.Java.Packets;

public static partial class EntityPackets
{
    public static partial class Serverbound
    {
        /// <summary>Player action / block dig (<c>minecraft:player_action</c> / 1.8 <c>block_dig</c>).</summary>
        public static readonly PacketType<ServerboundPlayerActionPacket> PlayerAction =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("player_action"));
    }
}

// Serverbound player abilities lives with the UI family (ServerboundPlayerAbilitiesPacket in UiPackets.cs); the UI registrar hook claims it for both eras.

/// <summary>Player action / block dig. 1.8: status byte, block position, face byte. Modern: action VarInt, block position, direction byte, sequence VarInt (<see cref="Sequence"/> set).</summary>
public sealed record ServerboundPlayerActionPacket(int Action, BlockPos Position, byte Direction, int? Sequence) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => EntityPackets.Serverbound.PlayerAction;
}
