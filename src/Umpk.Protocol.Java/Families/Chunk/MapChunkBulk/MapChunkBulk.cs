using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;

namespace Umpk.Protocol.Java.Packets;

public static partial class PlayPackets
{
    public static partial class Clientbound
    {
        /// <summary>Bulk chunk delivery (<c>minecraft:map_chunk_bulk</c>), protocol 47 only. A 1.8 server sends nearly all of its terrain through this packet rather than through <see cref="LevelChunk"/>.</summary>
        public static readonly PacketType<ClientboundMapChunkBulkPacket> MapChunkBulk =
            new(ProtocolPhase.Play, PacketFlow.Clientbound, Identifier.Minecraft("map_chunk_bulk"));
    }
}

/// <summary>Bulk chunk delivery (clientbound play, <c>minecraft:map_chunk_bulk</c>, protocol 47 only): several full columns in one frame. On 1.8 this is how nearly all terrain arrives, so a consumer that ignores it sees an almost entirely empty world.</summary>
/// <remarks>Like <see cref="ClientboundLevelChunkPacket"/> the structural decode is deliberately lossy for the gameplay column (light and biomes ride verbatim), so the production encoder replays <see cref="RawBody"/>, and <c>ChunkCodecs.DecodeStructure</c> rebuilds the structural model from those bytes for the correctness-verification path.</remarks>
/// <param name="SkyLight">Whether these columns carry sky light (the packet-level flag).</param>
/// <param name="Columns">The decoded columns, in wire order. Each is a full ground-up column.</param>
/// <param name="RawBody">The exact wire body captured on decode; null for a code-built packet.</param>
public sealed record ClientboundMapChunkBulkPacket(
    bool SkyLight,
    IReadOnlyList<ChunkColumn> Columns,
    byte[]? RawBody = null) : IPacket
{
    /// <inheritdoc />
    public PacketType Type => PlayPackets.Clientbound.MapChunkBulk;
}
