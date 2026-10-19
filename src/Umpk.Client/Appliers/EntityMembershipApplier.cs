using System.Buffers.Binary;
using Umpk.Client.Actions;
using Umpk.Client.Internal;
using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Client.Appliers;

/// <summary>Observes received chunk headers for entity ticking, including sessions that do not retain terrain.</summary>
internal sealed class EntityMembershipApplier : IApplier
{
    public ValueTask<bool> TryApplyAsync(object packet, ApplierContext context, CancellationToken ct)
    {
        if (context.State.WorldDimension is null)
            return ValueTask.FromResult(false);
        switch (packet)
        {
            case ClientboundLevelChunkPacket chunk:
                if (IsLegacyUnload(chunk))
                    context.State.EntityMembership.UnloadColumn(new ChunkPos(chunk.ChunkX, chunk.ChunkZ));
                else if (LegacyPlayerGeometryProfile.Supports(context.Version.Version.Protocol)
                    && LegacyPlayerGeometryProfile.ForProtocol(context.Version.Version.Protocol).RetainsColumnForChunkPayloads)
                    context.State.EntityMembership.EnsureColumn(new ChunkPos(chunk.ChunkX, chunk.ChunkZ));
                else if (chunk.FullChunk)
                    context.State.EntityMembership.LoadColumn(new ChunkPos(chunk.ChunkX, chunk.ChunkZ));
                break;
            case ClientboundMapChunkBulkPacket bulk:
                foreach (var column in bulk.Columns)
                    context.State.EntityMembership.LoadColumn(column.Position);
                break;
            case ClientboundForgetLevelChunkPacket forget:
                context.State.EntityMembership.UnloadColumn(forget.Chunk);
                break;
            default:
                return ValueTask.FromResult(false);
        }
        return ValueTask.FromResult(!context.Features.Terrain);
    }

    // Original 1.8 chunk listener: a ground-up packet with a zero primary mask unloads the column, even when a biome tail is present.
    internal static bool IsLegacyUnload(ClientboundLevelChunkPacket packet)
        => packet.WireEra == ChunkWireEra.Legacy1_8 && packet.FullChunk
            && packet.RawBody is { Length: >= 11 } body && BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(9, 2)) == 0;
}
