using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Block-changed acknowledgment (<c>minecraft:block_changed_ack</c>, 1.19+).</summary>
        public static readonly PacketType<ClientboundBlockChangedAckPacket> BlockChangedAck =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("block_changed_ack"));
    }
}

/// <summary>Block-changed acknowledgment: the sequence number the client last requested.</summary>
public sealed record ClientboundBlockChangedAckPacket(int Sequence) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.BlockChangedAck;
}
