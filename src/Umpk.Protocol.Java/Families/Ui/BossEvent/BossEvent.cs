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
        // boss bar

        /// <summary>Boss event (<c>minecraft:boss_event</c>, 770/776).</summary>
        public static readonly PacketType<ClientboundBossEventPacket> BossEvent =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("boss_event"));
    }
}

/// <summary>Boss event (770/776): a uuid and an operation. Only the operation's fields are present. Colors and overlays use the <see cref="BossBarColor"/>/<see cref="BossBarOverlay"/> vocabulary; flags use <see cref="BossBarFlags"/>.</summary>
public sealed record ClientboundBossEventPacket(
    Guid Id,
    BossEventOperation Operation,
    Component? Title,
    float Progress,
    BossBarColor Color,
    BossBarOverlay Overlay,
    BossBarFlags Flags) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.BossEvent;
}

/// <summary>The boss-event operation discriminator.</summary>
public enum BossEventOperation
{
    /// <summary>Add a boss bar (title, progress, color, overlay, flags).</summary>
    Add = 0,

    /// <summary>Remove the boss bar.</summary>
    Remove = 1,

    /// <summary>Update the progress fraction.</summary>
    UpdateProgress = 2,

    /// <summary>Update the title component.</summary>
    UpdateName = 3,

    /// <summary>Update the color and overlay.</summary>
    UpdateStyle = 4,

    /// <summary>Update the flags.</summary>
    UpdateProperties = 5,
}
