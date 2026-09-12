using System.Buffers;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>Level-chunk codecs. The modern (770/776) codec decodes <c>ClientboundLevelChunkWithLightPacket</c> into <see cref="ChunkColumn"/> sections; the 1.8 codec decodes the flat-ushort chunk into the direct section representation.</summary>
/// <remarks>Two encode paths exist. The production relay path (<see cref="EncodeRaw"/>, wired into the codec) replays the retained wire body on <see cref="ClientboundLevelChunkPacket.RawBody"/> verbatim: the proxy-grade path that never mutates a chunk. Alongside it, decode also builds a structural <see cref="ChunkWireBody"/> (block/biome palettes, section counts, heightmaps captured field-by-field; block entities and light preserved as a narrow verbatim tail), and <see cref="EncodeStructural"/> re-serialises a frame from that model with <see cref="ClientboundLevelChunkPacket.RawBody"/> explicitly bypassed. The conformance/fidelity suite exercises the structural path so a decode corruption of the palettes or block states cannot hide behind a verbatim copy of the recorded bytes.</remarks>
// This file holds the shared core: the structural-encode dispatch, the parameterized paletted-container and section primitive (ReadSectionWire / WritePalettedContainer / ReadPalettedContainer), and the modern (770/776) plus legacy (1.8) codecs. The per-era chapters live in the sibling ChunkCodecs.V*.cs partials (V1_9To1_13, V1_14To1_15, V1_16To1_17, V1_18To1_21_1, V1_21_2), each keeping only its era-specific framing and routing its sections through this file's primitive.
public static partial class ChunkCodecs
{
    /// <summary>Rebuilds the structural wire model from a decoded packet's retained bytes. This is the correctness-verification entry point: the conformance suite decodes a recorded frame, rebuilds the model here and re-encodes it with the verbatim carrier bypassed, so a decode that corrupted a palette cannot hide behind a copy of the recorded bytes. It calls the same private structural decoders the production path calls, so there is one implementation reached twice.</summary>
    /// <param name="packet">A packet decoded from the wire, carrying its raw body.</param>
    /// <returns>The structural model for the packet's era.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="packet"/> is null.</exception>
    /// <exception cref="NotSupportedException">The packet was built in code and has no wire body.</exception>
    public static ChunkWireBody DecodeStructure(ClientboundLevelChunkPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        byte[] raw = packet.RawBody
            ?? throw new NotSupportedException("A code-built chunk packet has no wire body to rebuild.");

        var r = new PacketReader(raw);
        return packet.WireEra switch
        {
            ChunkWireEra.Modern => DecodeModernStructure(ref r, packet.SectionLayout),
            ChunkWireEra.NbtHeightmap => DecodeNbtHeightmapStructure(ref r),
            ChunkWireEra.NamedHeightmap757 => DecodeStructure(ref r, NbtWireFormat.JavaNamedRoot),
            ChunkWireEra.Prefixed764 => DecodeStructure(ref r, NbtWireFormat.JavaUnnamedRoot),
            ChunkWireEra.Bitmask1_16 => DecodeBitmaskStructure(ref r, ChunkBitmaskWire.V1_16),
            ChunkWireEra.Bitmask1_16_2 => DecodeBitmaskStructure(ref r, ChunkBitmaskWire.V1_16_2),
            ChunkWireEra.Bitmask1_17 => DecodeBitmaskStructure(ref r, ChunkBitmaskWire.V1_17),
            ChunkWireEra.LeadingBiomes => DecodePre118Structure(ref r, biomesBeforeBuffer: true),
            ChunkWireEra.TrailingBiomes => DecodePre118Structure(ref r, biomesBeforeBuffer: false),
            ChunkWireEra.Pre1_13 => DecodePre1_13Structure(ref r),
            _ => DecodeLegacyStructure(ref r),
        };
    }

    /// <summary>The bulk sibling's rebuild. <c>map_chunk_bulk</c> is a different record with its own raw body and its own body subtype, so it needs its own entry point rather than an unreachable arm above.</summary>
    /// <param name="packet">A bulk packet decoded from the wire.</param>
    /// <returns>The structural model.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="packet"/> is null.</exception>
    /// <exception cref="NotSupportedException">The packet was built in code and has no wire body.</exception>
    public static ChunkWireBody DecodeStructure(ClientboundMapChunkBulkPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        byte[] raw = packet.RawBody
            ?? throw new NotSupportedException("A code-built bulk packet has no wire body to rebuild.");

        var r = new PacketReader(raw);
        return DecodeMapChunkBulkStructure(ref r);
    }

    /// <summary>Structurally re-encodes a chunk frame from its <see cref="ChunkWireBody"/>, ignoring the packet's retained bytes. One virtual call: a body type without an encoder does not compile.</summary>
    /// <param name="body">The structural wire model.</param>
    /// <returns>The re-serialised frame bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is null.</exception>
    public static byte[] EncodeStructural(ChunkWireBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        body.EncodeInto(ref writer);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Reads the pre-1.18 chunk tail, whose block-entity NBT carries absolute coordinates.</summary>
    private static IReadOnlyList<ChunkBlockEntity> ReadLegacyBlockEntities(byte[] tail)
    {
        try
        {
            var reader = new PacketReader(tail);
            int count = reader.ReadVarInt();
            if (count < 0 || count > 4096)
                return [];

            var result = new List<ChunkBlockEntity>(count);
            for (int i = 0; i < count; i++)
            {
                if (reader.ReadNbt(NbtWireFormat.JavaNamedRoot) is not NbtCompound nbt
                    || !nbt.TryGet("x", out NbtInt? x)
                    || !nbt.TryGet("y", out NbtInt? y)
                    || !nbt.TryGet("z", out NbtInt? z))
                    continue;

                result.Add(new ChunkBlockEntity(new BlockPos(x.Value, y.Value, z.Value), nbt));
            }

            return result;
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Reads a legacy chunk tail after its length-prefixed section buffer.</summary>
    private static IReadOnlyList<ChunkBlockEntity> ReadLegacyBlockEntitiesAfterBuffer(byte[] tail, int prefixBytes = 0)
    {
        var offsets = new List<int> { prefixBytes, 0 };
        try
        {
            var reader = new PacketReader(tail);
            int biomeCount = reader.ReadVarInt();
            if (biomeCount >= 0 && biomeCount <= reader.Remaining)
            {
                for (int i = 0; i < biomeCount; i++)
                    _ = reader.ReadVarInt();

                offsets.Add(tail.Length - reader.Remaining);
            }
        }
        catch (Exception)
        {
            // This layout does not start with the 1.17 variable-length biome array.
        }

        foreach (int offset in offsets.Distinct())
        {
            try
            {
                var reader = new PacketReader(tail);
                if (offset < 0 || offset > reader.Remaining)
                    continue;

                _ = reader.ReadBytes(offset);
                int dataLength = reader.ReadVarInt();
                if (dataLength < 0 || dataLength > reader.Remaining)
                    continue;

                _ = reader.ReadBytes(dataLength);
                IReadOnlyList<ChunkBlockEntity> result = ReadLegacyBlockEntities(reader.ReadRemaining().ToArray());
                if (result.Count > 0)
                    return result;

            }
            catch (Exception)
            {
                // Try the next documented chunk-tail layout.
            }
        }

        return [];
    }

    // Mirror of ReadSectionWire: non-empty short, the fluid short when the section carries one, then the block container and (when present) the biome container, all under the same section layout.
    private static void WriteSectionWire(ref PacketWriter w, ChunkSectionWire section, ChunkSectionLayout layout)
    {
        w.WriteShort(section.NonEmptyBlockCount);
        if (section.FluidCount is { } fluid)
            w.WriteShort(fluid);

        WritePalettedContainer(ref w, section.Blocks, layout.Framing);
        if (layout.HasBiomeContainer)
            WritePalettedContainer(ref w, section.Biomes, layout.Framing);

    }

    // The section buffer every structural encoder writes: the sections under one era's layout, then that era's verbatim trailing pad, length-prefixed as one blob. Four encoders open-coded this loop with their layout spelled out at the call site.
    private static void WriteSectionBuffer(
        ref PacketWriter w, IReadOnlyList<ChunkSectionWire> sections, byte[] padding, ChunkSectionLayout layout)
    {
        var sectionBuffer = new ArrayBufferWriter<byte>();
        var sectionWriter = new PacketWriter(sectionBuffer);
        foreach (ChunkSectionWire section in sections)
            WriteSectionWire(ref sectionWriter, section, layout);

        sectionWriter.WriteBytes(padding);

        w.WriteVarInt(sectionBuffer.WrittenCount);
        w.WriteBytes(sectionBuffer.WrittenSpan);
    }

    // Mirror of ReadPalettedContainer: bits byte, single value or palette, then the packed long array under the given framing. A prefixed framing writes the VarInt length (a single-value container's is a lone VarInt 0); the fixed framing writes none. PrefixedChecked and PrefixedRaw produce the same bytes here and differ only in what the read side validates, so the encode side takes the whole framing and collapses it itself rather than being handed an already-collapsed bool.
    private static void WritePalettedContainer(ref PacketWriter w, ChunkPalettedContainerWire container, ChunkLongArrayFraming framing)
    {
        bool prefixed = framing != ChunkLongArrayFraming.FixedSize;
        w.WriteByte(container.BitsPerEntry);
        if (container.IsSingleValue)
        {
            w.WriteVarInt(container.SingleValue);
            if (prefixed)
                w.WriteVarInt(container.Data.Length);

            return;
        }

        if (container.Palette is { } palette)
        {
            w.WriteVarInt(palette.Length);
            foreach (int entry in palette)
                w.WriteVarInt(entry);

        }

        if (prefixed)
            w.WriteVarInt(container.Data.Length);

        foreach (long value in container.Data)
            w.WriteLong(value);

    }

    internal static void EncodeModernStructure(ref PacketWriter w, ModernChunkWireBody body)
    {
        w.WriteInt(body.X);
        w.WriteInt(body.Z);

        w.WriteVarInt(body.Heightmaps.Count);
        foreach (ChunkHeightmapWire heightmap in body.Heightmaps)
        {
            w.WriteVarInt(heightmap.TypeId);
            w.WriteVarInt(heightmap.Data.Length);
            foreach (long value in heightmap.Data)
                w.WriteLong(value);

        }

        // The buffer is built first so its exact byte length prefixes it, matching the wire.
        WriteSectionBuffer(
            ref w,
            body.Sections,
            body.SectionPadding,
            body.HasFluidCount ? ChunkSectionLayout.V26_1 : ChunkSectionLayout.V1_21_5);
        w.WriteBytes(body.Tail);
    }

    internal static void EncodeLegacyStructure(ref PacketWriter w, LegacyChunkWireBody body)
    {
        w.WriteInt(body.X);
        w.WriteInt(body.Z);
        w.WriteBool(body.GroundUp);
        w.WriteUShort(body.PrimaryMask);

        var data = new ArrayBufferWriter<byte>();
        var dataWriter = new PacketWriter(data);
        WriteLegacyChunkBlob(ref dataWriter, body);

        w.WriteVarInt(data.WrittenCount);
        w.WriteBytes(data.WrittenSpan);
    }

    /// <summary>Mirror of <see cref="DecodeLegacyChunkBlob"/>: block states re-packed from the decoded sections, then the verbatim light/biome remainder. Written without any length prefix, which is what the bulk form needs; <see cref="EncodeLegacyStructure"/> adds the VarInt length the single-chunk form has.</summary>
    private static void WriteLegacyChunkBlob(ref PacketWriter w, LegacyChunkWireBody body)
    {
        foreach (int[] states in body.SectionBlockStates)
            for (int yy = 0; yy < 16; yy++)
                for (int zz = 0; zz < 16; zz++)
                    for (int xx = 0; xx < 16; xx++)
                        WriteUShortLe(ref w, (ushort)states[ChunkSection.BlockIndex(xx, yy, zz)]);

        w.WriteBytes(body.Remainder);
    }

    private static void WriteUShortLe(ref PacketWriter w, ushort value)
    {
        w.WriteByte((byte)(value & 0xFF));
        w.WriteByte((byte)((value >> 8) & 0xFF));
    }
}
