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
        /// <summary>Clear titles (<c>minecraft:clear_titles</c>, 770/776).</summary>
        public static readonly PacketType<ClientboundClearTitlesPacket> ClearTitles =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("clear_titles"));
    }
}

/// <summary>Clear titles (770/776): whether the title times are reset.</summary>
public sealed record ClientboundClearTitlesPacket(bool ResetTimes) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.ClearTitles;
}
