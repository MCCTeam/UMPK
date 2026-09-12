using System.Buffers;
using Umpk.Game.World;
using Umpk.Nbt;
using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>The 1.13-1.13.2 (protocols 393-404) level-chunk codec. 1.13 predates the 1.14 chunk rework: the packet has no heightmaps (added at 1.14), each section carries block light + sky light inline (there is no separate update_light packet), and the chunk-level biome array is an <c>int[256]</c> at the tail of the buffer. The header carries int x, int z, bool groundUp, a VarInt bitmask, a VarInt-prefixed section buffer, and a block-entity NBT list. The section boundaries depend on dimension-specific sky-light presence, so the buffer and block-entity list are preserved verbatim after the header; the structural re-encode reproduces the frame byte-for-byte.</summary>
public static partial class ChunkCodecs
{
    // Builds a populated ChunkColumn from the pre-1.13 section buffer, or false on a structural surprise. Read the sections named by the bitmask (bits-per- block, palette, packed long array with the pre-1.16 straddling bit rule), skipping each section's inline block-light (+ sky-light) nibble arrays, and ignore whatever trailing bytes follow the last decoded section (biomes / server padding / block-entity NBT are not needed for the block grid, and the verbatim RawBody already drives byte-exact relay).
    private static bool TryBuildPre1_13Column(Pre1_13ChunkWireBody wire, out ChunkColumn column)
    {
        // Sky-light presence is dimension-dependent and not on the wire here; overworld (present) is the common case and lets a wrong hypothesis self-correct: a bad light skip mis-aligns the next section header and the plausibility guards in ReadPre1_13Section reject it, so the no-sky-light pass is retried. Single-section columns decode identically under either hypothesis.
        if (TryBuildPre1_13Column(wire, hasSkyLight: true, out column))
            return true;

        return TryBuildPre1_13Column(wire, hasSkyLight: false, out column);
    }

    private static bool TryBuildPre1_13Column(Pre1_13ChunkWireBody wire, bool hasSkyLight, out ChunkColumn column)
    {
        column = null!;
        try
        {
            var r = new PacketReader(wire.Tail);
            int dataSize = r.ReadVarInt();
            if (dataSize < 0 || dataSize > r.Remaining)
                return false;

            ReadOnlySpan<byte> data = r.ReadBytes(dataSize);
            var body = new PacketReader(data);

            // Section count = highest present section index + 1 (32 - leading-zeros of the 16-bit mask).
            int mask = wire.AvailableSections & 0xFFFF;
            int sectionCount = 32 - System.Numerics.BitOperations.LeadingZeroCount((uint)mask);
            if (sectionCount <= 0)
                return false;

            var work = ChunkColumnFactory.Create(wire.X, wire.Z, sectionCount);
            int lightBytes = hasSkyLight ? 4096 : 2048;
            int lastSection = sectionCount - 1;
            for (int sy = 0; sy < 16; sy++)
            {
                if ((wire.AvailableSections & (1 << sy)) == 0)
                    continue;

                int[]? states = ReadPre1_13Section(ref body);
                if (states is null)
                    return false;

                // Skip this section's light so the next section header aligns. The final section needs no skip (trailing biomes/padding are ignored); guard the skip against a short remainder.
                if (sy != lastSection)
                {
                    if (body.Remaining < lightBytes)
                        return false;

                    _ = body.ReadBytes(lightBytes);
                }

                ChunkSection section = ChunkSection.Filled(0);
                for (int i = 0; i < SectionCellCount; i++)
                {
                    int state = states[i];
                    if (state != 0)
                        section.SetBlockStateId(i & 15, (i >> 8) & 15, (i >> 4) & 15, state);

                }

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

    // Reads one pre-1.13 section's block-state portion (bits-per-block u8, palette, packed long array) and unpacks it to 4096 block-state ids using the pre-1.16 straddling bit layout (values may span a long boundary). Returns null on a structural surprise.
    private static int[]? ReadPre1_13Section(ref PacketReader r)
    {
        if (r.Remaining < 1)
            return null;

        int bitsPerBlock = r.ReadByte();
        if (bitsPerBlock == 0)
            bitsPerBlock = 4;

        // Palette: a VarInt length, then that many VarInt ids. Length 0 means the direct/global palette (the packed values are the block-state ids themselves).
        int paletteLen = r.ReadVarInt();
        if (paletteLen < 0 || paletteLen > r.Remaining + 1)
            return null;

        int[]? palette = paletteLen == 0 ? null : new int[paletteLen];
        for (int i = 0; i < paletteLen; i++)
            palette![i] = r.ReadVarInt();

        int longCount = r.ReadVarInt();
        if (bitsPerBlock is < 1 or > 32 || longCount < 0 || (long)longCount * 8 > r.Remaining)
            return null;

        var packed = new ulong[longCount];
        for (int i = 0; i < longCount; i++)
            packed[i] = (ulong)r.ReadLong();

        var states = new int[SectionCellCount];
        ulong valueMask = bitsPerBlock >= 64 ? ulong.MaxValue : (1UL << bitsPerBlock) - 1UL;
        for (int i = 0; i < SectionCellCount; i++)
        {
            long bitIndex = (long)i * bitsPerBlock;
            int startLong = (int)(bitIndex >> 6);
            int startOffset = (int)(bitIndex & 63);
            int endLong = (int)(((long)(i + 1) * bitsPerBlock - 1) >> 6);
            if (startLong >= longCount)
                return null;

            ulong value;
            if (startLong == endLong)
                value = (packed[startLong] >> startOffset) & valueMask;

            else
            {
                if (endLong >= longCount)
                    return null;

                value = ((packed[startLong] >> startOffset) | (packed[endLong] << (64 - startOffset))) & valueMask;
            }

            int raw = (int)value;
            if (palette is not null)
            {
                if (raw < 0 || raw >= palette.Length)
                    return null;

                raw = palette[raw];
            }

            states[i] = raw;
        }

        return states;
    }

    private static Pre1_13ChunkWireBody DecodePre1_13Structure(ref PacketReader r)
    {
        int x = r.ReadInt();
        int z = r.ReadInt();
        bool fullChunk = r.ReadBool();
        int bitmask = r.ReadVarInt();

        // Everything after the header (the VarInt-prefixed section buffer with inline light and the trailing biome array, then the block-entity NBT list) is preserved verbatim: there are no inline palettes to decode at the packet level without dimension-specific light knowledge.
        byte[] tail = r.ReadRemaining().ToArray();
        return new Pre1_13ChunkWireBody(x, z, fullChunk, bitmask, tail);
    }

    internal static void EncodePre1_13Structure(ref PacketWriter w, Pre1_13ChunkWireBody body)
    {
        w.WriteInt(body.X);
        w.WriteInt(body.Z);
        w.WriteBool(body.FullChunk);
        w.WriteVarInt(body.AvailableSections);
        w.WriteBytes(body.Tail);
    }
}
