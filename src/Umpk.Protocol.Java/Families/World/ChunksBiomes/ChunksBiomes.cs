using Umpk.Game.Players;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class WorldPackets
{
    public static partial class Clientbound
    {
        /// <summary>Chunk biomes update (<c>minecraft:chunks_biomes</c>).</summary>
        public static readonly PacketType<ClientboundChunksBiomesPacket> ChunksBiomes =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("chunks_biomes"));
    }
}

/// <summary>Chunk biomes update: a list of per-chunk biome buffers.</summary>
public sealed record ClientboundChunksBiomesPacket(IReadOnlyList<ChunkBiomeEntry> Chunks) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => WorldPackets.Clientbound.ChunksBiomes;
}

/// <summary>One chunk's biome data: the chunk position and the raw biome section buffer.</summary>
public readonly record struct ChunkBiomeEntry(ChunkPos Chunk, byte[] Buffer);
