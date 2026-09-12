using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Level event / world effect (<c>minecraft:level_event</c>).</summary>
        public static readonly PacketType<ClientboundLevelEventPacket> LevelEvent =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("level_event"));
    }
}

/// <summary>Level event / world effect: an effect id, a position, an int data field, and a global flag.</summary>
public sealed record ClientboundLevelEventPacket(int EffectId, BlockPos Position, int Data, bool Global) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.LevelEvent;
}
