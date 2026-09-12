using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class PlayPackets
{
    public static partial class Serverbound
    {
        /// <summary>Standalone chat acknowledgement (<c>minecraft:chat_ack</c>, 1.19.3+): advances the server's view of the client's last-seen message offset when the client has seen messages but is not sending chat, so the last-seen window does not overflow.</summary>
        public static readonly PacketType<ServerboundChatAckPacket> ChatAck =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("chat_ack"));
    }
}

/// <summary>The 1.19.3+ standalone chat acknowledgement: a single VarInt offset advancing the server's view of the client's last-seen message count when the client acknowledges messages without sending chat.</summary>
public sealed record ServerboundChatAckPacket(int Offset) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => PlayPackets.Serverbound.ChatAck;
}
