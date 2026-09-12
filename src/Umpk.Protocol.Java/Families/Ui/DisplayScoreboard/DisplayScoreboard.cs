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
        /// <summary>Legacy 1.8 display scoreboard (<c>minecraft:display_scoreboard</c>, 47).</summary>
        public static readonly PacketType<ClientboundLegacyDisplayObjectivePacket> LegacyDisplayObjective =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("display_scoreboard"));
    }
}

/// <summary>Legacy 1.8 display scoreboard (47): a byte slot (0 list, 1 sidebar, 2 below-name) and the objective name.</summary>
public sealed record ClientboundLegacyDisplayObjectivePacket(byte Slot, string ObjectiveName) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.LegacyDisplayObjective;
}
