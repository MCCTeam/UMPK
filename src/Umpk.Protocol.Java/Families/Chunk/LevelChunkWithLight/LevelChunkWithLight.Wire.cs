using System.Buffers;
using Umpk.Game.Registries;
using Umpk.Game.World;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java.Codecs;

public static partial class ChunkCodecs
{
    /// <summary>Adds this packet's timelines to the binding table.</summary>
    internal static void DeclareLevelChunkWithLight(PacketBindings bindings)
    {
        // The 1.8 dataset spells this packet level_chunk (the flat-ushort codec); without the legacy-name alias the 1.8 chunk frames fell through to a marker. 1.18-1.20.1 still carry a named NBT heightmap root (removed at 1.20.2). 1.21.5 switches heightmaps from one network-NBT compound to a VarInt-keyed packed map; 26.1 adds a per-section fluidCount short (26.2 reuses it). The generated bands list all thirteen steps and the supported protocols that each step governs.
        // BEGIN GENERATED BANDS Play Clientbound minecraft:level_chunk_with_light
        //   47       ChunkCodecs.V1_8 via:minecraft:level_chunk
        //   107-340  ChunkCodecs.V1_9 via:minecraft:level_chunk
        //   393-404  ChunkCodecs.V1_13 via:minecraft:level_chunk
        //   477-498  ChunkCodecs.V1_14 via:minecraft:level_chunk
        //   573-578  ChunkCodecs.V1_15 via:minecraft:level_chunk
        //   735-736  ChunkCodecs.V1_16 via:minecraft:level_chunk
        //   751-754  ChunkCodecs.V1_16_2 via:minecraft:level_chunk
        //   755-756  ChunkCodecs.V1_17 via:minecraft:level_chunk
        //   757-763  ChunkCodecs.V1_18
        //   764-767  ChunkCodecs.V1_20_2
        //   768-769  ChunkCodecs.V1_21_2
        //   770-774  ChunkCodecs.V1_21_5
        //   775-     ChunkCodecs.V26_1
        // END GENERATED BANDS
        bindings.Packet(PlayPackets.Clientbound.LevelChunk)
            .From(JavaProtocols.V1_8, ChunkCodecs.V1_8)
            .From(JavaProtocols.V1_9, ChunkCodecs.V1_9)
            .From(JavaProtocols.V1_13, ChunkCodecs.V1_13)
            .From(JavaProtocols.V1_14, ChunkCodecs.V1_14)
            .From(JavaProtocols.V1_15, ChunkCodecs.V1_15)
            .From(JavaProtocols.V1_16, ChunkCodecs.V1_16)
            .From(JavaProtocols.V1_16_2, ChunkCodecs.V1_16_2)
            .From(JavaProtocols.V1_17, ChunkCodecs.V1_17)
            .From(JavaProtocols.V1_18, ChunkCodecs.V1_18)
            .From(JavaProtocols.V1_20_2, ChunkCodecs.V1_20_2)
            .From(JavaProtocols.V1_21_2, ChunkCodecs.V1_21_2)
            .From(JavaProtocols.V1_21_5, ChunkCodecs.V1_21_5)
            .From(JavaProtocols.V26_1, ChunkCodecs.V26_1)
            .AliasedAs(Identifier.Minecraft("level_chunk"));
    }

    // 1.15 chunk-level biomes: ChunkBiomeContainer is a fixed 1024 ints (BIOMES_SIZE) = 4096 bytes, present only on full chunks.
    private const int Biomes1_15ByteLength = 1024 * 4;

    /// <summary>Pre-1.18 chunk codec for protocols 477-498 (1.14-1.14.4): biomes ride in the buffer.</summary>
    public static readonly PacketCodec<ClientboundLevelChunkPacket> V1_14 = MakePre118(biomesBeforeBuffer: false);

    /// <summary>Pre-1.18 chunk codec for protocols 573-578 (1.15-1.15.2): biomes are a separate int[1024] field written before the section buffer.</summary>
    public static readonly PacketCodec<ClientboundLevelChunkPacket> V1_15 = MakePre118(biomesBeforeBuffer: true);

    private static PacketCodec<ClientboundLevelChunkPacket> MakePre118(bool biomesBeforeBuffer) =>
        PacketCodec<ClientboundLevelChunkPacket>.Of(
            (ref PacketWriter w, ClientboundLevelChunkPacket p, PacketCodecContext _) => EncodeRaw(ref w, p),
            (ref PacketReader r, PacketCodecContext _) => DecodePre118(ref r, biomesBeforeBuffer),
            WireShape.Of(
                biomesBeforeBuffer
                    ? "int,int,bool,varint,heightmaps_nbt,biomes,sections,block_entities"
                    : "int,int,bool,varint,heightmaps_nbt,sections,block_entities",
                ChunkSectionLayout.V1_14.ToString()));

    private static ClientboundLevelChunkPacket DecodePre118(ref PacketReader r, bool biomesBeforeBuffer)
    {
        byte[] rawBody = r.ReadBytes(r.Remaining).ToArray();
        var body = new PacketReader(rawBody);
        Pre1_18ChunkWireBody wire = DecodePre118Structure(ref body, biomesBeforeBuffer);

        // Populate a queryable block grid from the already-parsed sections ALONGSIDE the verbatim wire body (RawBody still drives byte-exact relay/re-encode). 1.14-1.15.2 use the pre-1.16 STRADDLING bit storage (values cross long boundaries), so the packed indices are unpacked with the straddling walk, not the padded BitStorage the modern column installer assumes. Any structural surprise leaves a bare shell so relay is never affected.
        ChunkColumn column = TryBuildPre118Column(wire, out ChunkColumn built)
            ? built
            : ChunkColumnFactory.Create(wire.X, wire.Z, sectionCount: 0);
        return new ClientboundLevelChunkPacket(wire.X, wire.Z, column, rawBody)
        {
            WireEra = biomesBeforeBuffer ? ChunkWireEra.LeadingBiomes : ChunkWireEra.TrailingBiomes,
            SectionLayout = ChunkSectionLayout.V1_14,
            FullChunk = wire.FullChunk,
            BlockEntities = ReadLegacyBlockEntities(wire.Tail),
        };
    }

    // Builds a populated ChunkColumn from the 1.14-1.15.2 sections (straddling-unpacked), or false on a structural surprise. The chunk is 0-255 (16 sections) so the column is sized to the full height and can absorb later block updates anywhere in it; absent sections stay null (read as air).
    private static bool TryBuildPre118Column(Pre1_18ChunkWireBody wire, out ChunkColumn column)
    {
        column = null!;
        try
        {
            int mask = wire.AvailableSections & 0xFFFF;
            int highest = 32 - System.Numerics.BitOperations.LeadingZeroCount((uint)mask);
            int sectionCount = Math.Max(16, highest);
            var work = ChunkColumnFactory.Create(wire.X, wire.Z, sectionCount);

            int decoded = 0;
            for (int sy = 0; sy < 16; sy++)
            {
                if ((wire.AvailableSections & (1 << sy)) == 0)
                    continue;

                if (decoded >= wire.Sections.Count)
                    return false;

                int[] states = UnpackStraddlingStates(wire.Sections[decoded++].Blocks);
                ChunkSection section = ChunkSection.Filled(0);
                for (int i = 0; i < SectionCellCount; i++)
                    if (states[i] != 0)
                        section.SetBlockStateId(i & 15, (i >> 8) & 15, (i >> 4) & 15, states[i]);

                work.SetSection(sy, section);
            }

            column = work;
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    // Unpacks a pre-1.16 (straddling) block paletted container to 4096 raw block-state ids in YZX order. Values are packed contiguously and may span a long boundary (1.14.4/1.15.2 BitStorage). A single- value container fills with its one id; a direct container's packed values are raw ids; an indirect container maps packed indices through the palette.
    private static int[] UnpackStraddlingStates(ChunkPalettedContainerWire container)
    {
        var states = new int[SectionCellCount];
        if (container.IsSingleValue)
        {
            if (container.SingleValue != 0)
                Array.Fill(states, container.SingleValue);

            return states;
        }

        int bits = container.BitsPerEntry;
        if (bits is < 1 or > 32)
            throw new ProtocolViolationException($"Pre-1.16 chunk section bits-per-entry {bits} is out of range.");

        long[] packed = container.Data;
        int longCount = packed.Length;
        int[]? palette = container.Palette;
        ulong valueMask = bits >= 64 ? ulong.MaxValue : (1UL << bits) - 1UL;
        for (int i = 0; i < SectionCellCount; i++)
        {
            long bitIndex = (long)i * bits;
            int startLong = (int)(bitIndex >> 6);
            int startOffset = (int)(bitIndex & 63);
            int endLong = (int)(((long)(i + 1) * bits - 1) >> 6);
            if (startLong >= longCount || endLong >= longCount)
                throw new ProtocolViolationException("Pre-1.16 chunk section long array is too short for its cell count.");

            ulong value = startLong == endLong
                ? ((ulong)packed[startLong] >> startOffset) & valueMask
                : (((ulong)packed[startLong] >> startOffset) | ((ulong)packed[endLong] << (64 - startOffset))) & valueMask;

            int raw = (int)value;
            if (palette is not null)
            {
                if (raw < 0 || raw >= palette.Length)
                    throw new ProtocolViolationException("Pre-1.16 chunk section palette index is out of range.");

                raw = palette[raw];
            }

            states[i] = raw;
        }

        return states;
    }

    private static Pre1_18ChunkWireBody DecodePre118Structure(ref PacketReader r, bool biomesBeforeBuffer)
    {
        int x = r.ReadInt();
        int z = r.ReadInt();
        bool fullChunk = r.ReadBool();
        int bitmask = r.ReadVarInt();
        NbtTag heightmaps = r.ReadNbt(NbtWireFormat.JavaNamedRoot);

        // 1.15 writes the chunk-level biome array here (full chunks only), before the section buffer.
        byte[] separateBiomes = biomesBeforeBuffer && fullChunk ? r.ReadBytes(Biomes1_15ByteLength).ToArray() : [];

        int bufferLen = r.ReadVarInt();
        if (bufferLen < 0 || bufferLen > r.Remaining)
            throw new ProtocolViolationException($"Chunk section buffer length {bufferLen} exceeds the remaining payload.");

        ReadOnlySpan<byte> sectionBytes = r.ReadBytes(bufferLen);
        byte[] tail = r.ReadRemaining().ToArray();

        var sectionReader = new PacketReader(sectionBytes);
        int sectionCount = System.Numerics.BitOperations.PopCount((uint)bitmask);
        var sections = new List<ChunkSectionWire>(sectionCount);
        for (int i = 0; i < sectionCount; i++)
            sections.Add(ReadPre118Section(ref sectionReader));

        // On a full chunk the chunk-level biome array (and any pad) fills the rest of the buffer; ride it verbatim so the structural re-encode is byte-exact without palette-parsing the biomes.
        byte[] biomesAndPadding = sectionReader.ReadRemaining().ToArray();
        return new Pre1_18ChunkWireBody(x, z, fullChunk, bitmask, heightmaps, sections, biomesAndPadding, tail, separateBiomes);
    }

    // Pre-1.18 section: non-empty block-count short, then a single block paletted container framed like 768/769 (VarInt length-prefixed long array, count taken from the wire). There is no per-section biome container (biomes are chunk-level), so the shared reader attaches a placeholder single-value biome container that is never written back. Also used by the 1.16-1.17.1 bitmask decode grid.
    private static ChunkSectionWire ReadPre118Section(ref PacketReader r) =>
        ReadSectionWire(ref r, ChunkSectionLayout.V1_14);

    /// <summary>735/736 (1.16/1.16.1): VarInt mask, full-chunk + forget-old-data flags.</summary>
    public static PacketCodec<ClientboundLevelChunkPacket> V1_16 { get; } = CreateBitmask(ChunkBitmaskWire.V1_16);

    /// <summary>751/753/754 (1.16.2-1.16.5): VarInt mask, full-chunk flag, no forget-old-data.</summary>
    public static PacketCodec<ClientboundLevelChunkPacket> V1_16_2 { get; } = CreateBitmask(ChunkBitmaskWire.V1_16_2);

    /// <summary>755/756 (1.17/1.17.1): BitSet long-array mask, no full-chunk flag.</summary>
    public static PacketCodec<ClientboundLevelChunkPacket> V1_17 { get; } = CreateBitmask(ChunkBitmaskWire.V1_17);

    private static PacketCodec<ClientboundLevelChunkPacket> CreateBitmask(ChunkBitmaskWire wire) =>
        PacketCodec<ClientboundLevelChunkPacket>.Of(
            static (ref PacketWriter w, ClientboundLevelChunkPacket p, PacketCodecContext _) => EncodeRaw(ref w, p),
            (ref PacketReader r, PacketCodecContext _) => DecodeBitmask(ref r, wire),
            WireShape.OfEra("chunk_bitmask", wire));

    private static ClientboundLevelChunkPacket DecodeBitmask(ref PacketReader r, ChunkBitmaskWire era)
    {
        byte[] rawBody = r.ReadBytes(r.Remaining).ToArray();
        var body = new PacketReader(rawBody);
        BitmaskChunkWireBody wire = DecodeBitmaskStructure(ref body, era);
        // The section palettes live inside the opaque buffer (the verbatim Tail). Parse them ALONGSIDE the byte-exact wire body to populate a queryable block grid: skip the chunk-level biomes, read the length-prefixed section buffer, and decode each present section (1.16+ uses the padded, non-straddling bit storage, unlike 1.14-1.15). A structural surprise leaves a bare shell so the verbatim relay/re-encode is never affected.
        ChunkColumn column = TryBuildBitmaskColumn(wire, era, out ChunkColumn built)
            ? built
            : ChunkColumnFactory.Create(wire.X, wire.Z, sectionCount: 0);
        // 1.17 dropped the flag from the wire; every frame there is a full column, hence the ?? true.
        return new ClientboundLevelChunkPacket(wire.X, wire.Z, column, rawBody)
        {
            WireEra = BitmaskEraOf(era),
            SectionLayout = ChunkSectionLayout.V1_14,
            FullChunk = wire.FullChunk ?? true,
            // 1.16/1.17 place the full-chunk biome array before the length-prefixed section buffer. Partial 1.16 updates omit it; 1.17 frames are full columns.
            BlockEntities = ReadLegacyBlockEntitiesAfterBuffer(wire.Tail, (wire.FullChunk ?? true) ? 4096 : 0),
        };
    }

    private static ChunkWireEra BitmaskEraOf(ChunkBitmaskWire era) =>
        era.BitsetMask ? ChunkWireEra.Bitmask1_17
        : era.HasForgetOldData ? ChunkWireEra.Bitmask1_16
        : ChunkWireEra.Bitmask1_16_2;

    private static BitmaskChunkWireBody DecodeBitmaskStructure(ref PacketReader r, ChunkBitmaskWire era)
    {
        int x = r.ReadInt();
        int z = r.ReadInt();
        bool? fullChunk = era.HasFullChunk ? r.ReadBool() : null;
        bool? forgetOldData = era.HasForgetOldData ? r.ReadBool() : null;

        int? maskVarInt = null;
        long[]? maskBitset = null;
        if (era.BitsetMask)
        {
            // The bitset is a VarInt count followed by that many longs. Preserve all words, including trailing zero words, so the re-encode is byte-exact.
            int count = r.ReadVarInt();
            if (count < 0 || (long)count * 8 > r.Remaining)
                throw new ProtocolViolationException($"Chunk section BitSet long count {count} exceeds the remaining payload.");

            long[] longs = new long[count];
            for (int i = 0; i < count; i++)
                longs[i] = r.ReadLong();

            maskBitset = longs;
        }
        else
            maskVarInt = r.ReadVarInt();

        // Heightmaps are a named-root NBT compound. Order preservation makes structural re-encoding byte-exact.
        NbtTag heightmaps = r.ReadNbt(NbtWireFormat.JavaNamedRoot);

        // Everything after the heightmaps (biomes, the opaque section buffer, and the block-entity NBT list) is preserved verbatim: there are no inline palettes to decode at the packet level.
        byte[] tail = r.ReadRemaining().ToArray();
        return new BitmaskChunkWireBody(x, z, fullChunk, forgetOldData, maskVarInt, maskBitset, heightmaps, tail);
    }

    // Builds a populated ChunkColumn for 1.16-1.17.1 by parsing the opaque section buffer out of the verbatim Tail: [chunk-level biomes][VarInt bufferLen][section buffer][block-entity list]. The biome encoding is version-specific (1.16/1.16.1: fixed int[1024]; 1.16.2-1.17.1: VarInt array), gated by full-chunk presence, and must be skipped exactly to reach the buffer. Sections are the 1.16+ padded (non-straddling) paletted containers: short non-empty count, then one block container (biomes are chunk-level, no per-section biome container). Returns false on any structural surprise.
    private static bool TryBuildBitmaskColumn(BitmaskChunkWireBody wire, ChunkBitmaskWire era, out ChunkColumn column)
    {
        column = null!;
        try
        {
            var r = new PacketReader(wire.Tail);

            // skip the chunk-level biomes 1.16/1.16.1 (forgetOldData present): ChunkBiomeContainer = fixed 1024 ints, only on a full chunk. 1.16.2-1.16.5: VarInt array, only on a full chunk. 1.17/1.17.1 (no full-chunk flag): VarInt array, always present.
            bool fullChunk = wire.FullChunk ?? true;
            bool biomesPresent = !era.HasFullChunk || fullChunk;
            if (biomesPresent)
                if (era.HasForgetOldData)
                {
                    if (r.Remaining < Biomes1_15ByteLength)
                        return false;

                    _ = r.ReadBytes(Biomes1_15ByteLength);
                }
                else
                {
                    int biomeCount = r.ReadVarInt();
                    if (biomeCount < 0 || biomeCount > r.Remaining)
                        return false;

                    for (int i = 0; i < biomeCount; i++)
                        _ = r.ReadVarInt();

                }

            // section buffer
            int bufferLen = r.ReadVarInt();
            if (bufferLen < 0 || bufferLen > r.Remaining)
                return false;

            ReadOnlySpan<byte> buffer = r.ReadBytes(bufferLen);
            var body = new PacketReader(buffer);

            // present sections (ascending) from the VarInt mask (1.16-1.16.5) or BitSet (1.17+)
            List<int> presentSections = PresentSections(wire);
            if (presentSections.Count == 0)
                return false;

            int highest = presentSections[^1] + 1;
            var work = ChunkColumnFactory.Create(wire.X, wire.Z, Math.Max(16, highest));
            foreach (int sy in presentSections)
            {
                if (body.Remaining < ChunkSectionLayout.V1_14.MinSectionBytes)
                    return false;

                ChunkSectionWire sw = ReadPre118Section(ref body);
                work.SetSection(sy, BuildBlockSection(sw.Blocks));
            }

            column = work;
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    // The ascending list of present section indices, from the VarInt bitmask (1.16-1.16.5) or the BitSet long array (1.17+). Only the low 16 bits are meaningful for the pre-1.18 overworld height.
    private static List<int> PresentSections(BitmaskChunkWireBody wire)
    {
        var presentSections = new List<int>();
        if (wire.MaskBitset is { } bitset)
        {
            for (int longIndex = 0; longIndex < bitset.Length; longIndex++)
            {
                long word = bitset[longIndex];
                while (word != 0)
                {
                    int bit = System.Numerics.BitOperations.TrailingZeroCount(word);
                    presentSections.Add(longIndex * 64 + bit);
                    word &= word - 1;
                }
            }

            presentSections.Sort();
        }
        else
        {
            int mask = wire.MaskVarInt ?? 0;
            for (int sy = 0; sy < 32; sy++)
                if ((mask & (1 << sy)) != 0)
                    presentSections.Add(sy);

        }

        return presentSections;
    }

    /// <summary>The 764-767 chunk-with-light codec.</summary>
    public static PacketCodec<ClientboundLevelChunkPacket> V1_20_2 { get; } =
        PacketCodec<ClientboundLevelChunkPacket>.Of(
            static (ref PacketWriter w, ClientboundLevelChunkPacket p, PacketCodecContext _) => EncodeRaw(ref w, p),
            static (ref PacketReader r, PacketCodecContext _) => Decode(ref r, NbtWireFormat.JavaUnnamedRoot),
            WireShape.Of(
                "int,int,heightmaps_nbt,sections,block_entities,light",
                $"{NbtWireFormat.JavaUnnamedRoot},{ChunkSectionLayout.V1_20_2}"));

    /// <summary>The 1.18-1.20.1 (protocols 757-763) level-chunk-with-light codec. Wire-identical to <see cref="V1_20_2"/> except the heightmaps network-NBT compound still carries a NAMED root (the root-name removal landed at 1.20.2). Binding V1_20_2 here read the compound as an unnamed root, which misframed the (empty) root name, drove the section-buffer length off, and yielded an all-air structural column that could not store block updates.</summary>
    public static PacketCodec<ClientboundLevelChunkPacket> V1_18 { get; } =
        PacketCodec<ClientboundLevelChunkPacket>.Of(
            static (ref PacketWriter w, ClientboundLevelChunkPacket p, PacketCodecContext _) => EncodeRaw(ref w, p),
            static (ref PacketReader r, PacketCodecContext _) => Decode(ref r, NbtWireFormat.JavaNamedRoot),
            WireShape.Of(
                "int,int,heightmaps_nbt,sections,block_entities,light",
                $"{NbtWireFormat.JavaNamedRoot},{ChunkSectionLayout.V1_20_2}"));

    private static ClientboundLevelChunkPacket Decode(ref PacketReader r, NbtWireFormat heightmapRootFormat)
    {
        byte[] rawBody = r.ReadBytes(r.Remaining).ToArray();
        var body = new PacketReader(rawBody);
        Protocol764To767ChunkWireBody wire = DecodeStructure(ref body, heightmapRootFormat);
        ChunkColumn column = BuildSectionColumn(wire.X, wire.Z, wire.Sections);
        return new ClientboundLevelChunkPacket(wire.X, wire.Z, column, rawBody)
        {
            WireEra = heightmapRootFormat == NbtWireFormat.JavaNamedRoot
                ? ChunkWireEra.NamedHeightmap757
                : ChunkWireEra.Prefixed764,
            SectionLayout = ChunkSectionLayout.V1_20_2,
            BlockEntities = ReadPackedBlockEntities(wire.X, wire.Z, wire.Tail, heightmapRootFormat),
        };
    }

    private static Protocol764To767ChunkWireBody DecodeStructure(ref PacketReader r, NbtWireFormat heightmapRootFormat)
    {
        int x = r.ReadInt();
        int z = r.ReadInt();

        // One network-NBT compound: serialization key -> long-array raw heightmap data. NbtCompound preserves member order, so re-encode reproduces the exact bytes. The root is named on 1.18-1.20.1 and unnamed from 1.20.2 (the network-NBT root-name removal).
        NbtTag heightmaps = r.ReadNbt(heightmapRootFormat);

        int bufferLen = r.ReadVarInt();
        if (bufferLen < 0 || bufferLen > r.Remaining)
            throw new ProtocolViolationException($"Chunk section buffer length {bufferLen} exceeds the remaining payload.");

        ReadOnlySpan<byte> sectionBytes = r.ReadBytes(bufferLen);

        // Block entities + light data: preserved verbatim (same narrow tail as the shared member).
        byte[] tail = r.ReadRemaining().ToArray();

        // Each container's long array is VarInt length-prefixed, and the count must equal the size derived from its storage parameters.
        List<ChunkSectionWire> sections = ReadSections(sectionBytes, ChunkSectionLayout.V1_20_2, out byte[] padding);

        return new Protocol764To767ChunkWireBody(x, z, heightmaps, sections, padding, tail, heightmapRootFormat);
    }

    /// <summary>Pre-1.21.5 modern chunk-with-light codec (768/769): NBT heightmaps, no fluid counts.</summary>
    public static readonly PacketCodec<ClientboundLevelChunkPacket> V1_21_2 =
        PacketCodec<ClientboundLevelChunkPacket>.Of(
            static (ref PacketWriter w, ClientboundLevelChunkPacket p, PacketCodecContext _) => EncodeRaw(ref w, p),
            static (ref PacketReader r, PacketCodecContext _) => DecodeNbtHeightmap(ref r),
            WireShape.Of(
                "int,int,heightmaps_nbt,sections,block_entities,light",
                $"{NbtWireFormat.JavaUnnamedRoot},{ChunkSectionLayout.V1_21_2}"));

    private static ClientboundLevelChunkPacket DecodeNbtHeightmap(ref PacketReader r)
    {
        // Same shape as DecodeModern: snapshot the whole body for the verbatim relay path, then run the structural pass over the copy.
        byte[] rawBody = r.ReadBytes(r.Remaining).ToArray();
        var body = new PacketReader(rawBody);
        NbtHeightmapChunkWireBody wire = DecodeNbtHeightmapStructure(ref body);
        ChunkColumn column = BuildSectionColumn(wire.X, wire.Z, wire.Sections);
        return new ClientboundLevelChunkPacket(wire.X, wire.Z, column, rawBody)
        {
            WireEra = ChunkWireEra.NbtHeightmap,
            SectionLayout = ChunkSectionLayout.V1_21_2,
            BlockEntities = ReadPackedBlockEntities(wire.X, wire.Z, wire.Tail, NbtWireFormat.JavaUnnamedRoot),
        };
    }

    private static NbtHeightmapChunkWireBody DecodeNbtHeightmapStructure(ref PacketReader r)
    {
        int x = r.ReadInt();
        int z = r.ReadInt();

        // Pre-1.21.5 heightmaps: one network-NBT compound (unnamed root, 1.20.2+ framing). The NBT model preserves member order, so the structural re-encode reproduces the compound byte-exactly.
        NbtTag heightmaps = r.ReadNbt(NbtWireFormat.JavaUnnamedRoot);

        int bufferLen = r.ReadVarInt();
        if (bufferLen < 0 || bufferLen > r.Remaining)
            throw new ProtocolViolationException($"Chunk section buffer length {bufferLen} exceeds the remaining payload.");

        ReadOnlySpan<byte> sectionBytes = r.ReadBytes(bufferLen);
        byte[] tail = r.ReadRemaining().ToArray();

        // Before 1.21.5 each container's long array is VarInt length-prefixed, with the count taken from the wire. A single-value container serializes as a lone VarInt 0.
        List<ChunkSectionWire> sections = ReadSections(sectionBytes, ChunkSectionLayout.V1_21_2, out byte[] padding);
        return new NbtHeightmapChunkWireBody(x, z, heightmaps, sections, padding, tail);
    }

    /// <summary>1.13-1.13.2 chunk codec (protocols 393-404): no heightmaps, inline per-section light.</summary>
    public static readonly PacketCodec<ClientboundLevelChunkPacket> V1_13 =
        PacketCodec<ClientboundLevelChunkPacket>.Of(
            static (ref PacketWriter w, ClientboundLevelChunkPacket p, PacketCodecContext _) => EncodeRaw(ref w, p),
            static (ref PacketReader r, PacketCodecContext _) => DecodePre1_13Flat(ref r));

    /// <summary>1.9-1.12.2 chunk codec (protocols 107-340). The pre-flattening map_chunk header is byte-identical to 1.13 - int x, int z, bool groundUp, VarInt bitmask - followed by the VarInt-prefixed section buffer (paletted sections with inline block-light + sky-light nibble arrays and, on a full chunk, a trailing byte[256] biome array) and, from 1.9.4 (protocol 110) onward, a block-entity NBT list. All of that rides after the header as one opaque tail, so the section-internal differences from 1.13 (byte biomes instead of int[256]) and the 1.9.4 block-entity addition need no separate codec: the header decodes structurally and the body re-encodes verbatim, byte-for-byte. Shares the <see cref="Pre1_13ChunkWireBody"/> model and the <c>DecodePre1_13</c> path with the 1.13 codec.</summary>
    public static readonly PacketCodec<ClientboundLevelChunkPacket> V1_9 =
        PacketCodec<ClientboundLevelChunkPacket>.Of(
            static (ref PacketWriter w, ClientboundLevelChunkPacket p, PacketCodecContext _) => EncodeRaw(ref w, p),
            static (ref PacketReader r, PacketCodecContext _) => DecodePre1_13(ref r));

    /// <summary>1.13-1.13.2 chunk (protocols 393-404): flat block-state ids, int[256] biomes.</summary>
    private static ClientboundLevelChunkPacket DecodePre1_13Flat(ref PacketReader r) => DecodePre1_13(ref r, flatState: true);

    /// <summary>1.9-1.12.2 chunk (protocols 107-340): (id&lt;&lt;4)|meta block-state ids, byte[256] biomes.</summary>
    private static ClientboundLevelChunkPacket DecodePre1_13(ref PacketReader r) => DecodePre1_13(ref r, flatState: false);

    private static ClientboundLevelChunkPacket DecodePre1_13(ref PacketReader r, bool flatState)
    {
        byte[] rawBody = r.ReadBytes(r.Remaining).ToArray();
        var body = new PacketReader(rawBody);
        Pre1_13ChunkWireBody wire = DecodePre1_13Structure(ref body);

        // Populate a queryable block grid from the bit-packed paletted sections ALONGSIDE the verbatim tail (the tail still drives byte-exact relay/re-encode). The section buffer carries inline block-light + sky-light nibble arrays whose size depends on the dimension's sky-light presence, which is not on the wire here, so the walk is attempted with sky-light present (overworld) and retried without it (nether/end); block-state ids are read identically either way, only the fixed light-array skip differs. Any structural surprise leaves the shell column so relay is never affected.
        _ = flatState; // flat vs (id<<4)|meta only affects the value semantics, not the decode walk.
        ChunkColumn column =
            TryBuildPre1_13Column(wire, out ChunkColumn built)
                ? built
                : ChunkColumnFactory.Create(wire.X, wire.Z, sectionCount: 0);
        return new ClientboundLevelChunkPacket(wire.X, wire.Z, column, rawBody)
        {
            WireEra = ChunkWireEra.Pre1_13,
            SectionLayout = ChunkSectionLayout.V1_14,
            FullChunk = wire.FullChunk,
            BlockEntities = ReadLegacyBlockEntitiesAfterBuffer(wire.Tail),
        };
    }

    // 1.21.5 SECTION_STATES strategy: indirect palette up to 8 bits (min 4), direct otherwise.
    private const int BlockIndirectCeiling = 8;

    // SECTION_BIOMES strategy: indirect palette up to 3 bits, direct otherwise.
    private const int BiomeIndirectCeiling = 3;

    private const int SectionCellCount = 16 * 16 * 16;

    private const int BiomeCellCount = 4 * 4 * 4;

    /// <summary>Modern chunk-with-light codec (1.21.5, protocol 770): no per-section fluid count.</summary>
    public static readonly PacketCodec<ClientboundLevelChunkPacket> V1_21_5 = CreateModern(ChunkSectionLayout.V1_21_5);

    /// <summary>26.1-era chunk (protocol 776): each section carries a second <c>fluidCount</c> short after <c>nonEmptyBlockCount</c>. Version 26.2 reuses this shape.</summary>
    public static readonly PacketCodec<ClientboundLevelChunkPacket> V26_1 = CreateModern(ChunkSectionLayout.V26_1);

    /// <summary>1.8 flat-ushort chunk codec (decodes into direct sections).</summary>
    public static readonly PacketCodec<ClientboundLevelChunkPacket> V1_8 = CreateLegacy();

    private static PacketCodec<ClientboundLevelChunkPacket> CreateModern(ChunkSectionLayout layout) =>
        PacketCodec<ClientboundLevelChunkPacket>.Of(
            static (ref PacketWriter w, ClientboundLevelChunkPacket p, PacketCodecContext _) => EncodeRaw(ref w, p),
            (ref PacketReader r, PacketCodecContext _) => DecodeModern(ref r, layout),
            WireShape.OfEra("chunk_modern", layout));

    private static PacketCodec<ClientboundLevelChunkPacket> CreateLegacy() =>
        PacketCodec<ClientboundLevelChunkPacket>.Of(
            static (ref PacketWriter w, ClientboundLevelChunkPacket p, PacketCodecContext _) => EncodeRaw(ref w, p),
            static (ref PacketReader r, PacketCodecContext _) => DecodeLegacy(ref r));

    // The structural decode is lossy for the gameplay column, so the production codec re-encodes the
    // retained wire body verbatim. A packet built in code (no RawBody, no Wire) has no wire model;
    // refuse rather than emit a non-identical structurally re-serialized frame.
    private static void EncodeRaw(ref PacketWriter w, ClientboundLevelChunkPacket p)
    {
        ArgumentNullException.ThrowIfNull(p);
        if (p.RawBody is null)
            throw new NotSupportedException(
                "Chunk encode requires the decoded RawBody; use EncodeStructural for a code-built frame.");

        w.WriteBytes(p.RawBody);
    }

    private static ClientboundLevelChunkPacket DecodeModern(ref PacketReader r, ChunkSectionLayout layout)
    {
        // Snapshot the whole wire body first so the production relay path round-trips byte-exactly. The outer reader is fully drained here (exact consumption); the structural pass runs over the copy.
        byte[] rawBody = r.ReadBytes(r.Remaining).ToArray();
        var body = new PacketReader(rawBody);
        ModernChunkWireBody wire = DecodeModernStructure(ref body, layout);
        ChunkColumn column = BuildSectionColumn(wire.X, wire.Z, wire.Sections);
        return new ClientboundLevelChunkPacket(wire.X, wire.Z, column, rawBody)
        {
            WireEra = ChunkWireEra.Modern,
            SectionLayout = layout,
            BlockEntities = ReadPackedBlockEntities(wire.X, wire.Z, wire.Tail, NbtWireFormat.JavaUnnamedRoot),
        };
    }

    private static ModernChunkWireBody DecodeModernStructure(ref PacketReader r, ChunkSectionLayout layout)
    {
        int x = r.ReadInt();
        int z = r.ReadInt();

        // Heightmaps: 1.21.5 uses a VarInt-count map of (Heightmap.Types id -> long[]). Captured structurally so re-encode reproduces them. Counts are validated against the remaining payload before allocating so a malformed frame throws ProtocolViolationException rather than allocating an implausible array.
        int heightmapCount = r.ReadVarInt();
        if (heightmapCount < 0 || heightmapCount > r.Remaining + 1)
            throw new ProtocolViolationException($"Chunk heightmap count {heightmapCount} is implausible for {r.Remaining} remaining bytes.");

        var heightmaps = new List<ChunkHeightmapWire>(heightmapCount);
        for (int i = 0; i < heightmapCount; i++)
        {
            int typeId = r.ReadVarInt();
            int longs = r.ReadVarInt();
            if (longs < 0 || longs > (r.Remaining / 8) + 1)
                throw new ProtocolViolationException($"Chunk heightmap long count {longs} is implausible for {r.Remaining} remaining bytes.");

            var data = new long[longs];
            for (int j = 0; j < longs; j++)
                data[j] = r.ReadLong();

            heightmaps.Add(new ChunkHeightmapWire(typeId, data));
        }

        int bufferLen = r.ReadVarInt();
        if (bufferLen < 0 || bufferLen > r.Remaining)
            throw new ProtocolViolationException($"Chunk section buffer length {bufferLen} exceeds the remaining payload.");

        ReadOnlySpan<byte> sectionBytes = r.ReadBytes(bufferLen);

        // Block entities, then the light-update block: preserved verbatim as a narrow tail (the only not-yet-modelled span). The section buffer, where the palettes/block states live, is decoded structurally above/below, so a corruption there cannot hide behind this verbatim copy.
        byte[] tail = r.ReadRemaining().ToArray();

        List<ChunkSectionWire> sections = ReadSections(sectionBytes, layout, out byte[] padding);

        return new ModernChunkWireBody(x, z, layout.HasFluidCount, heightmaps, sections, padding, tail);
    }

    /// <summary>Decodes 1.21.5+ packed chunk block entities without consuming the following light tail.</summary>
    private static IReadOnlyList<ChunkBlockEntity> ReadPackedBlockEntities(
        int chunkX,
        int chunkZ,
        byte[] tail,
        NbtWireFormat nbtFormat)
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
                byte packed = reader.ReadByte();
                short y = reader.ReadShort();
                _ = reader.ReadVarInt(); // registry block-entity type; the NBT itself is type-independent.
                if (reader.ReadNbt(nbtFormat) is NbtCompound nbt)
                {
                    int x = (chunkX << 4) + ((packed >> 4) & 15);
                    int z = (chunkZ << 4) + (packed & 15);
                    result.Add(new ChunkBlockEntity(new BlockPos(x, y, z), nbt));
                }
            }

            return result;
        }
        catch (Exception)
        {
            // The chunk itself remains usable even if a future protocol changes this tail. RawBody preserves it for forwarding, while regular block-entity update packets can still fill state.
            return [];
        }
    }

    // One read/write pair for the paletted section container across every era whose block/biome data is decoded structurally (770/776 modern, 764-767, 768/769, and the 1.14-1.17.1 block container). Each era differs only in how its sections are framed, and that difference travels as one ChunkSectionLayout rather than as a handful of loose flags.

    // A capacity HINT only, never a limit: the list still grows past it, and a datapack dimension may legitimately be taller than any vanilla one.
    private const int MaxSectionsPerColumn = 64;

    // Drains a section buffer under one era's layout. Some servers append a short zero tail after the complete sections. Drain only well-formed sections and preserve the under-sized remainder verbatim so the buffer re-serializes byte-exactly.
    private static List<ChunkSectionWire> ReadSections(
        ReadOnlySpan<byte> sectionBytes, ChunkSectionLayout layout, out byte[] padding)
    {
        var reader = new PacketReader(sectionBytes);

        // The buffer length bounds the count, so the hint is data-driven rather than guessed.
        var sections = new List<ChunkSectionWire>(
            Math.Clamp(sectionBytes.Length / layout.MinSectionBytes, 0, MaxSectionsPerColumn));
        while (reader.Remaining >= layout.MinSectionBytes)
            sections.Add(ReadSectionWire(ref reader, layout));

        padding = reader.ReadRemaining().ToArray();
        return sections;
    }

    // One decoded section: non-empty block count, optional per-section fluid count (26.1+), then the block and (when present) biome paletted containers. Absent biome containers (477-756, biomes are chunk-level) get a placeholder single-value container that is never written back.
    private static ChunkSectionWire ReadSectionWire(ref PacketReader r, ChunkSectionLayout layout)
    {
        short nonEmpty = r.ReadShort();
        short? fluid = layout.HasFluidCount ? r.ReadShort() : null; // 26.1+: section fluid-count short
        ChunkPalettedContainerWire blocks =
            ReadPalettedContainer(ref r, BlockIndirectCeiling, SectionCellCount, blockStates: true, layout.Framing);
        ChunkPalettedContainerWire biomes = layout.HasBiomeContainer
            ? ReadPalettedContainer(ref r, BiomeIndirectCeiling, BiomeCellCount, blockStates: false, layout.Framing)
            : new ChunkPalettedContainerWire(0, 0, null, []);
        return new ChunkSectionWire(nonEmpty, fluid, blocks, biomes);
    }

    // A paletted container carries a bits byte followed by a single value when bits is zero, a palette plus packed long array in indirect mode, or a bare packed long array in direct mode. SECTION_STATES stores linear palettes (wire bits 1-4) at a fixed 4 bits (FOUR_BITS_LINEAR); biome widths are the wire byte directly. The only per-era difference is how the long array is framed (see ChunkLongArrayFraming).
    private static ChunkPalettedContainerWire ReadPalettedContainer(
        ref PacketReader r, int indirectCeiling, int cellCount, bool blockStates, ChunkLongArrayFraming framing)
    {
        byte bits = r.ReadByte();
        if (bits == 0)
        {
            int value = r.ReadVarInt();
            ReadContainerLongs(ref r, framing, storageBits: 0, cellCount, singleValue: true);
            return new ChunkPalettedContainerWire(0, value, null, []);
        }

        int[]? palette = bits <= indirectCeiling ? ReadPalette(ref r) : null;
        long[] data = ReadContainerLongs(ref r, framing, StorageBits(bits, blockStates), cellCount, singleValue: false);
        return new ChunkPalettedContainerWire(bits, 0, palette, data);
    }

    // Reads a paletted container's packed long array under the given framing. FixedSize derives the count from the storage bits and cell count (no prefix); the prefixed framings read a VarInt count, which 764-767 (PrefixedChecked) cross-checks against the storage-derived count and 768/769 (PrefixedRaw) takes as-is. A single-value container carries no longs: fixed reads nothing, prefixed reads and requires a VarInt 0.
    private static long[] ReadContainerLongs(ref PacketReader r, ChunkLongArrayFraming framing, int storageBits, int cellCount, bool singleValue)
    {
        if (framing == ChunkLongArrayFraming.FixedSize)
        {
            if (singleValue)
                return [];

            int fixedCount = LongCountFor(storageBits, cellCount);
            return ReadLongs(ref r, fixedCount);
        }

        int longCount = r.ReadVarInt();
        int bound = framing == ChunkLongArrayFraming.PrefixedChecked ? (r.Remaining / 8) + 1 : r.Remaining / 8;
        if (longCount < 0 || longCount > bound)
            throw new ProtocolViolationException(
                $"Chunk container long count {longCount} is implausible for {r.Remaining} remaining bytes.");

        if (framing == ChunkLongArrayFraming.PrefixedChecked)
        {
            int expected = singleValue ? 0 : LongCountFor(storageBits, cellCount);
            if (longCount != expected)
                throw new ProtocolViolationException(
                    $"Chunk container long count {longCount} does not match the expected {expected} for {storageBits} storage bits.");

        }
        else if (singleValue && longCount != 0)
        {
            // 768/769 single-value ZeroBitStorage serialises exactly one VarInt 0; anything else desyncs.
            throw new ProtocolViolationException(
                $"Pre-1.21.5 single-value chunk container carried {longCount} storage long(s); expected 0.");
        }

        return ReadLongs(ref r, longCount);
    }

    private static long[] ReadLongs(ref PacketReader r, int longCount)
    {
        var data = new long[longCount];
        for (int i = 0; i < longCount; i++)
            data[i] = r.ReadLong();

        return data;
    }

    // The in-memory storage width for a wire bits-per-entry: SECTION_STATES clamps linear block palettes (wire bits 1-4) up to a fixed 4 bits; hashmap block widths (5-8), the global-palette byte, and every biome width are already the storage bits.
    private static int StorageBits(int wireBits, bool blocks) => blocks && wireBits is >= 1 and <= 4 ? 4 : wireBits;

    // The packed-array length for the given storage bits and cell count: valuesPerLong = 64 / bits, longCount = ceil(cellCount / valuesPerLong).
    private static int LongCountFor(int storageBits, int cellCount)
    {
        if (storageBits <= 0 || storageBits > 64)
            throw new ProtocolViolationException($"Chunk paletted container bits-per-entry {storageBits} is out of range.");

        int valuesPerLong = 64 / storageBits;
        return (cellCount + valuesPerLong - 1) / valuesPerLong;
    }

    // Builds a wire-relative column from decoded sections. Shared by every era whose sections ride the paletted-container primitive (770/776, 768/769, 757-767): the column build is identical; only the wire framing around the sections differs per era. The column carries no absolute floor, because the frame carries none; World.LoadColumn binds it to the world's dimension on install. This used to hardcode minY: -64, the 1.18+ OVERWORLD floor, which put every nether and end column 64 blocks below the blocks it actually contained.
    private static ChunkColumn BuildSectionColumn(int x, int z, IReadOnlyList<ChunkSectionWire> sections)
    {
        ChunkColumn column = ChunkColumnFactory.Create(x, z, sections.Count);
        for (int i = 0; i < sections.Count; i++)
            column.SetSection(i, BuildSection(sections[i]));

        return column;
    }

    private static ChunkSection BuildSection(ChunkSectionWire wire)
    {
        ChunkSection section = BuildBlockSection(wire.Blocks);
        ApplyBiomes(section, wire.Biomes);
        return section;
    }

    private static ChunkSection BuildBlockSection(ChunkPalettedContainerWire blocks)
    {
        if (blocks.IsSingleValue)
            return ChunkSection.FromSingleValueBlocks(blocks.SingleValue);

        if (blocks.Palette is { } palette)
            return ChunkSection.FromIndirectBlocks(palette, StorageBits(blocks.BitsPerEntry, blocks: true), blocks.Data);

        return ChunkSection.FromDirectBlocks(blocks.BitsPerEntry, blocks.Data);
    }

    private static void ApplyBiomes(ChunkSection section, ChunkPalettedContainerWire biomes)
    {
        if (biomes.IsSingleValue)
        {
            section.SetSingleValueBiomes(biomes.SingleValue);
            return;
        }

        if (biomes.Palette is { } palette)
            section.SetIndirectBiomes(palette, biomes.BitsPerEntry, biomes.Data);

        // Direct biomes are rare and the gameplay store defaults them; the wire model retains them for re-encode regardless.
    }

    private static int[] ReadPalette(ref PacketReader r)
    {
        int len = r.ReadVarInt();
        if (len < 0 || len > r.Remaining + 1)
            throw new ProtocolViolationException($"Chunk palette length {len} is implausible for {r.Remaining} remaining bytes.");

        var palette = new int[len];
        for (int i = 0; i < len; i++)
            palette[i] = r.ReadVarInt();

        return palette;
    }

    private static ClientboundLevelChunkPacket DecodeLegacy(ref PacketReader r)
    {
        byte[] rawBody = r.ReadBytes(r.Remaining).ToArray();
        var body = new PacketReader(rawBody);
        LegacyChunkWireBody wire = DecodeLegacyStructure(ref body);
        ChunkColumn column = BuildLegacyColumn(wire);
        return new ClientboundLevelChunkPacket(wire.X, wire.Z, column, rawBody)
        {
            WireEra = ChunkWireEra.Legacy1_8,
            SectionLayout = ChunkSectionLayout.V1_14,
            FullChunk = wire.GroundUp,
        };
    }

    private static LegacyChunkWireBody DecodeLegacyStructure(ref PacketReader r)
    {
        int x = r.ReadInt();
        int z = r.ReadInt();
        bool groundUp = r.ReadBool();
        ushort primaryMask = r.ReadUShort();

        // minecraft:level_chunk frames the per-chunk blob with an explicit VarInt length. The bulk form does not; see LegacyChunkBlobLength.
        int dataSize = r.ReadVarInt();
        ReadOnlySpan<byte> data = r.ReadBytes(dataSize);
        return DecodeLegacyChunkBlob(x, z, groundUp, primaryMask, data);
    }

    /// <summary>Decodes one 1.8 per-chunk blob (the payload shape shared by <c>minecraft:level_chunk</c> and <c>minecraft:map_chunk_bulk</c>): the flat little-endian ushort block states of every section the mask marks present, followed by the light and biome bytes, which are preserved verbatim.</summary>
    private static LegacyChunkWireBody DecodeLegacyChunkBlob(
        int x, int z, bool groundUp, ushort primaryMask, ReadOnlySpan<byte> data)
    {
        var blockReader = new PacketReader(data);
        var sectionStates = new List<int[]>();
        for (int sy = 0; sy < 16; sy++)
        {
            if ((primaryMask & (1 << sy)) == 0)
                continue;

            // Block states: SizeX*SizeY*SizeZ little-endian ushorts per present section, in YZX order.
            var states = new int[SectionCellCount];
            for (int yy = 0; yy < 16; yy++)
                for (int zz = 0; zz < 16; zz++)
                    for (int xx = 0; xx < 16; xx++)
                        states[ChunkSection.BlockIndex(xx, yy, zz)] = ReadUShortLe(ref blockReader);

            sectionStates.Add(states);
        }

        // Everything after the block-state portion (block light, sky light, biomes) is preserved verbatim; the block-state exemplar is what the structural decode models.
        byte[] remainder = blockReader.ReadRemaining().ToArray();
        return new LegacyChunkWireBody(x, z, groundUp, primaryMask, sectionStates, remainder);
    }

    private static ChunkColumn BuildLegacyColumn(LegacyChunkWireBody body)
    {
        // Section count extends through the highest set mask bit: 32 minus the leading-zero count.
        int sectionCount = 32 - System.Numerics.BitOperations.LeadingZeroCount((uint)(body.PrimaryMask & 0xFFFF));
        ChunkColumn column = ChunkColumnFactory.Create(body.X, body.Z, Math.Max(sectionCount, 0));

        int present = 0;
        for (int sy = 0; sy < 16; sy++)
        {
            if ((body.PrimaryMask & (1 << sy)) == 0)
                continue;

            int[] states = body.SectionBlockStates[present++];
            var section = ChunkSection.Filled(0);
            for (int yy = 0; yy < 16; yy++)
                for (int zz = 0; zz < 16; zz++)
                    for (int xx = 0; xx < 16; xx++)
                        section.SetBlockStateId(xx, yy, zz, states[ChunkSection.BlockIndex(xx, yy, zz)]);

            column.SetSection(sy, section);
        }

        return column;
    }

    private static ushort ReadUShortLe(ref PacketReader r)
    {
        byte lo = r.ReadByte();
        byte hi = r.ReadByte();
        return (ushort)(lo | (hi << 8));
    }
}

/// <summary>How one pre-1.18 era frames the level-chunk header. The three facts move at three different releases inside 735-756, which is why they are one named shape rather than three loose flags on a factory: 1.16.2 dropped <c>forgetOldData</c>, and 1.17 replaced the VarInt section mask with a BitSet and dropped <c>fullChunk</c> at the same time.</summary>
/// <param name="HasFullChunk">The leading full-chunk bool, present through 1.16.5 and gone at 1.17.</param>
/// <param name="HasForgetOldData">The 1.16/1.16.1 forget-old-data bool.</param>
/// <param name="BitsetMask">1.17+: the section mask is a VarInt-counted long array, not a VarInt.</param>
internal readonly record struct ChunkBitmaskWire(bool HasFullChunk, bool HasForgetOldData, bool BitsetMask)
{
    /// <summary>735/736 (1.16/1.16.1).</summary>
    public static ChunkBitmaskWire V1_16 { get; } = new(HasFullChunk: true, HasForgetOldData: true, BitsetMask: false);

    /// <summary>751/753/754 (1.16.2-1.16.5).</summary>
    public static ChunkBitmaskWire V1_16_2 { get; } = new(HasFullChunk: true, HasForgetOldData: false, BitsetMask: false);

    /// <summary>755/756 (1.17/1.17.1).</summary>
    public static ChunkBitmaskWire V1_17 { get; } = new(HasFullChunk: false, HasForgetOldData: false, BitsetMask: true);

    /// <inheritdoc />
    public override string ToString() =>
        $"full={(HasFullChunk ? 1 : 0)},forget={(HasForgetOldData ? 1 : 0)},bitset={(BitsetMask ? 1 : 0)}";
}
