using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class UiPackets
{
    public static partial class Clientbound
    {
        /// <summary>Modern display objective (<c>minecraft:set_display_objective</c>, 770/776).</summary>
        public static readonly PacketType<ClientboundSetDisplayObjectivePacket> SetDisplayObjective =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("set_display_objective"));
    }
}

/// <summary>Modern display objective (770/776): a VarInt display slot and the objective name ("" clears).</summary>
public sealed record ClientboundSetDisplayObjectivePacket(int Slot, string ObjectiveName) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.SetDisplayObjective;
}
