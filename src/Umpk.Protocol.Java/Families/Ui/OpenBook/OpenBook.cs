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
        /// <summary>Open book (<c>minecraft:open_book</c>, 770/776).</summary>
        public static readonly PacketType<ClientboundOpenBookPacket> OpenBook =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("open_book"));
    }
}

/// <summary>Open book (770/776): the hand to open the book in (main=0, off=1).</summary>
public sealed record ClientboundOpenBookPacket(int Hand) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.OpenBook;
}
