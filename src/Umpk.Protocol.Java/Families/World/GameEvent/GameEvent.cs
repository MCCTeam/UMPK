using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Game event / change game state (<c>minecraft:game_event</c> / 1.8 <c>game_state_change</c>).</summary>
        public static readonly PacketType<ClientboundGameEventPacket> GameEvent =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("game_event"));
    }
}

// Game events, level events, and particles

/// <summary>Change game state / game event: an event-type byte and a float parameter.</summary>
public sealed record ClientboundGameEventPacket(byte Event, float Param) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.GameEvent;
}
