using Umpk.Game.Items;
using Umpk.Game.Players;
using Umpk.Game.Scoreboard;
using Umpk.Geometry;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class UiPackets
{
    public static partial class Serverbound
    {
        /// <summary>Seen advancements (<c>minecraft:seen_advancements</c>, 770/776).</summary>
        public static readonly PacketType<ServerboundSeenAdvancementsPacket> SeenAdvancements =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("seen_advancements"));
    }
}

/// <summary>Seen advancements (770/776): an action, and for opened-tab a tab id.</summary>
public sealed record ServerboundSeenAdvancementsPacket(SeenAdvancementsAction Action, Identifier? Tab) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Serverbound.SeenAdvancements;
}

/// <summary>The seen-advancements action (opened-tab=0, closed-screen=1).</summary>
public enum SeenAdvancementsAction
{
    /// <summary>A tab was opened (carries the tab id).</summary>
    OpenedTab = 0,

    /// <summary>The advancements screen was closed.</summary>
    ClosedScreen = 1,
}
