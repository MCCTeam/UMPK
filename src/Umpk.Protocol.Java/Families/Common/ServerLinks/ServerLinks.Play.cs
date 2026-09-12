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
        /// <summary>Server links (<c>minecraft:server_links</c>, 770/776).</summary>
        public static readonly PacketType<ClientboundServerLinksPacket> ServerLinks =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("server_links"));
    }
}

/// <summary>Server links (770/776): a list of link entries shown on the pause/disconnect screen.</summary>
public sealed record ClientboundServerLinksPacket(IReadOnlyList<ServerLinkEntry> Links) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.ServerLinks;
}

/// <summary>One server link entry. A known-type link carries a VarInt built-in id (<see cref="KnownTypeId"/> set, <see cref="Label"/> null); a custom link carries a label component (<see cref="Label"/> set, <see cref="KnownTypeId"/> null). Both carry a url.</summary>
public sealed record ServerLinkEntry(int? KnownTypeId, Component? Label, string Url);
