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
        /// <summary>Set player team (<c>minecraft:set_player_team</c>; wire differs across 47/770/776).</summary>
        public static readonly PacketType<ClientboundSetPlayerTeamPacket> SetPlayerTeam =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("set_player_team"));
    }
}

/// <summary>Set player team. Name + method; add/change carry <see cref="TeamParameters"/>; add/add-players/remove-players carry a member list. The wire layout differs across eras: 47 uses strings for visibility and a byte color; 770 uses VarInt enums and a ChatFormatting color; 776 reorders the parameters and uses an optional TeamColor (the 26.2 Teams change).</summary>
public sealed record ClientboundSetPlayerTeamPacket(
    string Name,
    TeamMethod Method,
    TeamParameters? Parameters,
    IReadOnlyList<string> Players) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.SetPlayerTeam;
}
