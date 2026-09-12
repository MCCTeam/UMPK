using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class PlayPackets
{
    public static partial class Clientbound
    {
        /// <summary>Play-phase disconnect (<c>minecraft:disconnect</c>): the kick reason, sent immediately before the server closes the socket. Present on every supported version.</summary>
        public static readonly PacketType<ClientboundDisconnectPacket> Disconnect =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("disconnect"));
    }
}

/// <summary>
/// The play-phase disconnect: the reason the server is closing the connection, sent immediately before the socket goes away.
/// <para>From 1.20.2 the play and configuration wires are identical. Before then, both carry the same single component body.</para>
/// <para>The payload takes three forms: 47-764 JSON with legacy click/hover shapes; 765-769 network NBT with legacy shapes; and 770+ network NBT with modern <c>click_event</c>/<c>hover_event</c> action shapes.</para>
/// </summary>
public sealed record ClientboundDisconnectPacket(Component Reason) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => PlayPackets.Clientbound.Disconnect;
}
