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
        // scoreboard

        /// <summary>Modern set-objective (<c>minecraft:set_objective</c>, 770/776).</summary>
        public static readonly PacketType<ClientboundSetObjectivePacket> SetObjective =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("set_objective"));
    }
}

/// <summary>Modern set-objective (770/776). Mode 0 adds, 1 removes, 2 changes. For add/change the display name, render type, and optional number format are present; for remove only the name is.</summary>
public sealed record ClientboundSetObjectivePacket(
    string ObjectiveName,
    ScoreboardObjectiveMode Mode,
    Component? DisplayName,
    ObjectiveRenderType RenderType,
    ScoreNumberFormat? NumberFormat) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.SetObjective;
}
