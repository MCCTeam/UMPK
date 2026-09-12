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
        /// <summary>Play-phase ping request (<c>minecraft:ping_request</c>, 770/776).</summary>
        public static readonly PacketType<ServerboundPlayPingRequestPacket> PingRequest =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("ping_request"));
    }
}

/// <summary>Play-phase ping request (770/776 serverbound): an 8-byte time the server echoes in pong-response.</summary>
public sealed record ServerboundPlayPingRequestPacket(long Time) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Serverbound.PingRequest;
}
