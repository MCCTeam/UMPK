using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Serverbound
    {
        /// <summary>Chunk batch received flow control (<c>minecraft:chunk_batch_received</c>).</summary>
        public static readonly PacketType<ServerboundChunkBatchReceivedPacket> ChunkBatchReceived =
            new(ProtocolPhase.Play, PacketFlow.Serverbound, Identifier.Minecraft("chunk_batch_received"));
    }
}

/// <summary>Serverbound chunk batch acknowledgment: the client's desired chunks-per-tick.</summary>
public sealed record ServerboundChunkBatchReceivedPacket(float DesiredChunksPerTick) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Serverbound.ChunkBatchReceived;
}
