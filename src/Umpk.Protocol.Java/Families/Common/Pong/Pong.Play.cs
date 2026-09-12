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
        /// <summary>Play-phase pong (<c>minecraft:pong</c>, 770/776).</summary>
        public static readonly PacketType<ServerboundPlayPongPacket> Pong =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("pong"));
    }
}

/// <summary>Play-phase pong (770/776 serverbound): the 4-byte id from the server ping.</summary>
public sealed record ServerboundPlayPongPacket(int Id) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => UiPackets.Serverbound.Pong;
}
