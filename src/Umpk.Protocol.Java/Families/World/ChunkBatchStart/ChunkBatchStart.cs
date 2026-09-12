using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Chunk batch start (<c>minecraft:chunk_batch_start</c>).</summary>
        public static readonly PacketType<ClientboundChunkBatchStartPacket> ChunkBatchStart =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("chunk_batch_start"));
    }
}

/// <summary>Chunk batch start: no fields.</summary>
public sealed record ClientboundChunkBatchStartPacket : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.ChunkBatchStart;
}
