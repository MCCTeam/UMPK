using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class PlayPackets
{
    public static partial class Serverbound
    {
        /// <summary>Client tick end (<c>minecraft:client_tick_end</c>, 1.21.2+): vanilla's client sends it at the end of every client tick. The server uses it to notice a tick in which no movement arrived and zero its record of the player's movement.</summary>
        public static readonly PacketType<ServerboundClientTickEndPacket> ClientTickEnd =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("client_tick_end"));
    }
}

/// <summary>Client tick end (1.21.2+): an empty payload marking the end of one client tick.</summary>
public sealed record ServerboundClientTickEndPacket : IPacket
{
    /// <inheritdoc />
    public PacketType Type => PlayPackets.Serverbound.ClientTickEnd;
}
