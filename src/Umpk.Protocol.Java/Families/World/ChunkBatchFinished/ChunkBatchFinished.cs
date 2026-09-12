using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Chunk batch finished (<c>minecraft:chunk_batch_finished</c>).</summary>
        public static readonly PacketType<ClientboundChunkBatchFinishedPacket> ChunkBatchFinished =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("chunk_batch_finished"));
    }
}

/// <summary>Chunk batch finished: the number of chunks in the just-sent batch.</summary>
public sealed record ClientboundChunkBatchFinishedPacket(int BatchSize) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.ChunkBatchFinished;
}
