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
        /// <summary>Legacy 1.8 scoreboard objective (<c>minecraft:scoreboard_objective</c>, 47).</summary>
        public static readonly PacketType<ClientboundLegacyObjectivePacket> LegacyObjective =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("scoreboard_objective"));
    }
}

/// <summary>Legacy 1.8 scoreboard objective (47). Strings only: name (16), mode byte, then for add/change a value string (32) and a render-type string (16, e.g. <c>integer</c>/<c>hearts</c>).</summary>
public sealed record ClientboundLegacyObjectivePacket(
    string ObjectiveName,
    ScoreboardObjectiveMode Mode,
    string? Value,
    string? RenderType) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.LegacyObjective;
}
