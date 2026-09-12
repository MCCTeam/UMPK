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
        /// <summary>Play-phase ping (<c>minecraft:ping</c>, 770/776).</summary>
        public static readonly PacketType<ClientboundPlayPingPacket> Ping =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("ping"));
    }
}

/// <summary>Play-phase ping (770/776): a 4-byte id echoed back by pong.</summary>
public sealed record ClientboundPlayPingPacket(int Id) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.Ping;
}
