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
        /// <summary>Modern player info remove (<c>minecraft:player_info_remove</c>, 770/776).</summary>
        public static readonly PacketType<ClientboundPlayerInfoRemovePacket> PlayerInfoRemove =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("player_info_remove"));
    }
}

/// <summary>Modern player info remove (770/776): a list of player uuids to drop.</summary>
public sealed record ClientboundPlayerInfoRemovePacket(IReadOnlyList<Guid> ProfileIds) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.PlayerInfoRemove;
}
