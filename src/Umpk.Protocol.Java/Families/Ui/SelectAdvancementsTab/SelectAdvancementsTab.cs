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
        /// <summary>Select advancements tab (<c>minecraft:select_advancements_tab</c>, 770/776).</summary>
        public static readonly PacketType<ClientboundSelectAdvancementsTabPacket> SelectAdvancementsTab =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("select_advancements_tab"));
    }
}

/// <summary>Select advancements tab (770/776): an optional tab id (absent closes the screen).</summary>
public sealed record ClientboundSelectAdvancementsTabPacket(Identifier? Tab) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.SelectAdvancementsTab;
}
