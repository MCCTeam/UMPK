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
        // player list

        /// <summary>Modern player info update (<c>minecraft:player_info_update</c>, 770/776).</summary>
        public static readonly PacketType<ClientboundPlayerInfoUpdatePacket> PlayerInfoUpdate =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("player_info_update"));
    }
}

/// <summary>Modern player info update (770/776): a fixed BitSet of actions, then per-entry fields for each set action in ordinal order. The action count (bit width) is era knowledge: 1.21.5+ has 8.</summary>
public sealed record ClientboundPlayerInfoUpdatePacket(
    PlayerInfoActions Actions,
    IReadOnlyList<PlayerInfoEntry> Entries) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.PlayerInfoUpdate;
}
