using System.Buffers;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ChunkCodecs
{
    /// <summary>The 1.8 bulk chunk codec, bound on protocol 47 only (the sole protocol whose dataset carries <c>minecraft:map_chunk_bulk</c>).</summary>
    public static readonly PacketCodec<ClientboundMapChunkBulkPacket> MapChunkBulkV1_8 =
        PacketCodec<ClientboundMapChunkBulkPacket>.Of(
            static (ref PacketWriter w, ClientboundMapChunkBulkPacket p, PacketCodecContext _) => EncodeBulkRaw(ref w, p),
            static (ref PacketReader r, PacketCodecContext _) => DecodeMapChunkBulk(ref r));

    private static ClientboundMapChunkBulkPacket DecodeMapChunkBulk(ref PacketReader r)
    {
        // Snapshot the whole wire body first so the production relay path round-trips byte-exactly, then walk the copy structurally, exactly as the single-chunk codec does.
        byte[] rawBody = r.ReadBytes(r.Remaining).ToArray();
        var body = new PacketReader(rawBody);
        MapChunkBulkWireBody wire = DecodeMapChunkBulkStructure(ref body);

        var columns = new List<ChunkColumn>(wire.Chunks.Count);
        foreach (LegacyChunkWireBody chunk in wire.Chunks)
            columns.Add(BuildLegacyColumn(chunk));

        return new ClientboundMapChunkBulkPacket(wire.SkyLight, columns, rawBody);
    }

    // The bulk frame's structural walk. The packet-level sky-light flag is the frame's own first byte and every blob's size derives from it, so the rebuild reads it back rather than being told.
    private static MapChunkBulkWireBody DecodeMapChunkBulkStructure(ref PacketReader body)
    {
        bool sky = body.ReadBool();
        int count = body.ReadVarInt();

        // Each metadata entry is 4+4+2 bytes, so an implausible count is rejected before allocating.
        const int MetadataBytesPerChunk = 10;
        if (count < 0 || (long)count * MetadataBytesPerChunk > body.Remaining)
            throw new ProtocolViolationException(
                $"Bulk chunk count {count} is implausible for {body.Remaining} remaining bytes.");

        var metadata = new (int X, int Z, ushort Mask)[count];
        for (int i = 0; i < count; i++)
        {
            int x = body.ReadInt();
            int z = body.ReadInt();
            ushort mask = body.ReadUShort();
            metadata[i] = (x, z, mask);
        }

        // Second pass: the blobs, back to back, each sized from its own mask (vanilla reads them in a separate loop for the same reason, since no blob carries its own length).
        var wires = new List<LegacyChunkWireBody>(count);
        for (int i = 0; i < count; i++)
        {
            (int x, int z, ushort mask) = metadata[i];
            int sections = System.Numerics.BitOperations.PopCount((uint)mask);
            int blobLength = LegacyChunkBlobLength(sections, sky, hasBiomes: true);
            if (blobLength > body.Remaining)
                throw new ProtocolViolationException(
                    $"Bulk chunk {i} needs {blobLength} bytes for {sections} section(s) but only {body.Remaining} remain.");

            ReadOnlySpan<byte> blob = body.ReadBytes(blobLength);

            // GroundUp is true for every bulk column: vanilla passes the biome flag as a literal true.
            wires.Add(DecodeLegacyChunkBlob(x, z, groundUp: true, mask, blob));
        }

        return new MapChunkBulkWireBody(sky, wires);
    }

    private static void EncodeBulkRaw(ref PacketWriter w, ClientboundMapChunkBulkPacket p)
    {
        ArgumentNullException.ThrowIfNull(p);
        if (p.RawBody is null)
            throw new NotSupportedException(
                "Bulk chunk encode requires the decoded RawBody; use EncodeStructural for a code-built frame.");

        w.WriteBytes(p.RawBody);
    }

    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareMapChunkBulk(PacketBindings bindings)
    {
        // bulk level chunk (1.8 only) minecraft:map_chunk_bulk exists in exactly one dataset, protocol 47, so this single From binds it there and nowhere else. It was registered with NO codec, which made it a marker, and on 1.8 that is not a narrow gap: a vanilla server delivers nearly all terrain in bulk form and sends single level_chunk frames only for stragglers. A live 1.8 session measured 44 bulk frames and 21,891,464 bytes recognised by wire id and relayed into nothing, against ONE decoded level_chunk, with the client's own column among the losses, so the world was empty under the bot's own feet. The per-chunk payload is the same shape level_chunk carries, so the codec routes it through the same 1.8 section primitives rather than duplicating them. See ChunkCodecs.MapChunkBulk.cs for the 1.8.9 javap evidence, and note in particular that the per-chunk blob length is COMPUTED from the bitmask, not read from the wire.
        bindings.Packet(PlayPackets.Clientbound.MapChunkBulk)
            .From(JavaProtocols.V1_8, ChunkCodecs.MapChunkBulkV1_8);
    }
}
