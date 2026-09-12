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
        /// <summary>Play-phase pong response (<c>minecraft:pong_response</c>, 770/776).</summary>
        public static readonly PacketType<ClientboundPlayPongResponsePacket> PongResponse =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("pong_response"));
    }
}

/// <summary>Play-phase pong response (770/776): an 8-byte payload echoing the client's ping-request time.</summary>
public sealed record ClientboundPlayPongResponsePacket(long Time) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Clientbound.PongResponse;
}
