using System.Buffers;
using Umpk.Nbt;

namespace Umpk.Protocol.Bedrock;

internal sealed class BedrockBlockStorage
{
    internal const int CellCount = 4096;

    internal BedrockBlockStorage(int bitsPerBlock, bool isRuntime, int[] palette, int[] cells)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(cells);
        BitsPerBlock = bitsPerBlock;
        IsRuntime = isRuntime;
        Palette = (int[])palette.Clone();
        Cells = (int[])cells.Clone();
    }

    internal int BitsPerBlock { get; }

    internal bool IsRuntime { get; }

    internal int[] Palette { get; }

    internal int[] Cells { get; }

    internal int Get(int x, int y, int z)
    {
        if ((uint)x >= 16 || (uint)y >= 16 || (uint)z >= 16)
            throw new BedrockCodecException($"Block cell ({x}, {y}, {z}) is outside its 16-cube.");
        return Palette[Cells[(x << 8) | (z << 4) | y]];
    }
}

internal sealed class BedrockChunkSection
{
    internal BedrockChunkSection(int sectionY, IReadOnlyList<BedrockBlockStorage> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        SectionY = sectionY;
        Layers = [.. layers];
    }

    internal int SectionY { get; }

    internal IReadOnlyList<BedrockBlockStorage> Layers { get; }
}

internal static class BedrockChunkPayloadCodecs
{
    private const int SectionVersion = 9;
    private const int MaxPaletteEntries = BedrockBlockStorage.CellCount;
    private const byte CopyLastHeader = 0xFF;

    internal static BedrockBlockStorage DecodeStorage(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        var reader = new BedrockReader(payload, limits);
        BedrockBlockStorage storage = DecodeStorage(ref reader, biome: false);
        if (!reader.End)
            throw new BedrockCodecException("The block storage has unread bytes.");
        return storage;
    }

    internal static BedrockBlockStorage DecodeBiomeStorage(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        var reader = new BedrockReader(payload, limits);
        BedrockBlockStorage storage = DecodeStorage(ref reader, biome: true);
        if (!reader.End)
            throw new BedrockCodecException("The biome storage has unread bytes.");
        return storage;
    }

    internal static BedrockBlockStorage DecodeBiomeStoragePrefix(ref BedrockReader reader) => DecodeStorage(ref reader, biome: true);

    internal static byte[] EncodeStorage(BedrockBlockStorage storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        var output = new ArrayBufferWriter<byte>();
        WriteStorage(new BedrockWriter(output), storage);
        return output.WrittenSpan.ToArray();
    }

    internal static BedrockChunkSection DecodeSection(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        var reader = new BedrockReader(payload, limits);
        BedrockChunkSection section = DecodeSection(ref reader);
        if (!reader.End)
            throw new BedrockCodecException("The chunk section has unread bytes.");
        return section;
    }

    internal static BedrockChunkSection DecodeSection(ref BedrockReader reader)
    {
        int version = reader.ReadByte();
        if (version != SectionVersion)
            throw new BedrockCodecException($"The chunk section version {version} is not supported; only version {SectionVersion} is bound.");
        int layerCount = reader.ReadByte();
        if (layerCount is < 1 or > 2)
            throw new BedrockCodecException($"The chunk section layer count {layerCount} is outside the supported range 1 to 2.");
        int sectionY = unchecked((sbyte)reader.ReadByte());
        var layers = new BedrockBlockStorage[layerCount];
        for (int index = 0; index < layers.Length; index++)
            layers[index] = DecodeStorage(ref reader, biome: false);
        return new BedrockChunkSection(sectionY, layers);
    }

    internal static byte[] EncodeSection(BedrockChunkSection section)
    {
        ArgumentNullException.ThrowIfNull(section);
        if (section.Layers.Count is < 1 or > 2)
            throw new BedrockCodecException($"The chunk section layer count {section.Layers.Count} is outside the supported range 1 to 2.");
        var output = new ArrayBufferWriter<byte>();
        var writer = new BedrockWriter(output);
        writer.WriteByte(SectionVersion);
        writer.WriteByte((byte)section.Layers.Count);
        writer.WriteByte(unchecked((byte)section.SectionY));
        foreach (BedrockBlockStorage layer in section.Layers)
            WriteStorage(writer, layer);
        return output.WrittenSpan.ToArray();
    }

    internal const int MaxSubChunkActors = 256;

    internal static (BedrockChunkSection Section, IReadOnlyList<BedrockBlockActorData> Actors) DecodeSubChunkPayload(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        BedrockCodecLimits actualLimits = limits ?? BedrockCodecLimits.Default;
        var reader = new BedrockReader(payload, actualLimits);
        BedrockChunkSection section = DecodeSection(ref reader);
        List<BedrockBlockActorData> actors = ReadSubChunkActors(ref reader, actualLimits);
        if (!reader.End)
            throw new BedrockCodecException("The sub-chunk payload has unread bytes after its block actors.");
        return (section, actors);
    }

    internal static byte[] EncodeSubChunkPayload(BedrockChunkSection section, IReadOnlyList<BedrockBlockActorData> actors)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(actors);
        var output = new ArrayBufferWriter<byte>();
        var writer = new BedrockWriter(output);
        writer.WriteBytes(EncodeSection(section));
        foreach (BedrockBlockActorData actor in actors)
            writer.WriteBytes(actor.Payload);
        return output.WrittenSpan.ToArray();
    }

    private static List<BedrockBlockActorData> ReadSubChunkActors(ref BedrockReader reader, BedrockCodecLimits limits)
    {
        var actors = new List<BedrockBlockActorData>();
        while (!reader.End)
        {
            if (actors.Count >= MaxSubChunkActors)
                throw new BedrockCodecException($"The sub-chunk actor count exceeds its supported maximum {MaxSubChunkActors}.");
            NbtTag tag = BedrockNbtCodec.DecodePrefix(ref reader, BedrockNbtWireFormat.NetworkLittleVarInt, limits);
            if (tag is NbtEnd)
            {
                if (actors.Count != 0)
                    throw new BedrockCodecException("The sub-chunk actor sequence carries an End root after entries.");
                return actors;
            }

            if (tag is not NbtCompound compound)
                throw new BedrockCodecException($"The sub-chunk actor entry has NBT type {tag.Type} instead of a compound.");
            if (!compound.TryGet("x", out NbtTag? xTag) || xTag is not NbtInt x)
                throw new BedrockCodecException("The sub-chunk actor entry carries no integer x coordinate.");
            if (!compound.TryGet("y", out NbtTag? yTag) || yTag is not NbtInt y)
                throw new BedrockCodecException("The sub-chunk actor entry carries no integer y coordinate.");
            if (!compound.TryGet("z", out NbtTag? zTag) || zTag is not NbtInt z)
                throw new BedrockCodecException("The sub-chunk actor entry carries no integer z coordinate.");
            string? identifier = compound.TryGet("id", out NbtTag? idTag) && idTag is NbtString id ? id.Value : null;
            actors.Add(new BedrockBlockActorData(x.Value, y.Value, z.Value, identifier, BedrockNbtCodec.Encode(compound, BedrockNbtWireFormat.NetworkLittleVarInt)));
        }

        return actors;
    }

    private static BedrockBlockStorage DecodeStorage(ref BedrockReader reader, bool biome)
    {
        byte header = reader.ReadByte();
        if (header == CopyLastHeader)
            throw new BedrockCodecException("The copy-last-layer storage header repeats a previous palette and cannot be decoded independently.");
        int bits = header >> 1;
        if (!IsSupportedWidth(bits))
            throw new BedrockCodecException($"The palette width {bits} is not a supported Bedrock bit width.");
        bool runtime = (header & 1) == 1;
        uint[] words = new uint[WordCount(bits)];
        for (int index = 0; index < words.Length; index++)
            words[index] = reader.ReadUInt32LittleEndian();
        // The singleton form carries exactly one entry with no size field; captured sections open with 0x01 followed directly by the entry.
        if (bits == 0)
        {
            int single = reader.ReadZigZag32();
            if (biome && single < 0)
                throw new BedrockCodecException($"The biome palette entry {single} is negative.");
            var singletonCells = new int[BedrockBlockStorage.CellCount];
            return new BedrockBlockStorage(0, runtime, [single], singletonCells);
        }

        int paletteSize = reader.ReadZigZag32();
        if (paletteSize < 1 || paletteSize > MaxPaletteEntries)
            throw new BedrockCodecException($"The palette size {paletteSize} is outside the supported range 1 to {MaxPaletteEntries}.");
        var palette = new int[paletteSize];
        for (int index = 0; index < palette.Length; index++)
        {
            palette[index] = reader.ReadZigZag32();
            if (biome && palette[index] < 0)
                throw new BedrockCodecException($"The biome palette entry {palette[index]} is negative.");
        }

        var cells = new int[BedrockBlockStorage.CellCount];
        for (int index = 0; index < cells.Length; index++)
        {
            int paletteIndex = Extract(words, bits, index);
            if (paletteIndex >= paletteSize)
                throw new BedrockCodecException($"The cell {index} references palette entry {paletteIndex} beyond size {paletteSize}.");
            cells[index] = paletteIndex;
        }

        return new BedrockBlockStorage(bits, runtime, palette, cells);
    }

    private static void WriteStorage(BedrockWriter writer, BedrockBlockStorage storage)
    {
        if (!IsSupportedWidth(storage.BitsPerBlock))
            throw new BedrockCodecException($"The palette width {storage.BitsPerBlock} is not a supported Bedrock bit width.");
        if (storage.Palette.Length is < 1 or > MaxPaletteEntries)
            throw new BedrockCodecException($"The palette size {storage.Palette.Length} is outside the supported range 1 to {MaxPaletteEntries}.");
        if (storage.Cells.Length != BedrockBlockStorage.CellCount)
            throw new BedrockCodecException($"The storage carries {storage.Cells.Length} cells instead of {BedrockBlockStorage.CellCount}.");
        writer.WriteByte((byte)((storage.BitsPerBlock << 1) | (storage.IsRuntime ? 1 : 0)));
        uint[] words = new uint[WordCount(storage.BitsPerBlock)];
        for (int index = 0; index < storage.Cells.Length; index++)
            Insert(words, storage.BitsPerBlock, index, storage.Cells[index]);
        foreach (uint word in words)
            writer.WriteUInt32LittleEndian(word);
        if (storage.BitsPerBlock == 0)
        {
            if (storage.Palette.Length != 1)
                throw new BedrockCodecException($"The singleton palette carries {storage.Palette.Length} entries instead of 1.");
            writer.WriteZigZag32(storage.Palette[0]);
            return;
        }

        writer.WriteZigZag32(storage.Palette.Length);
        foreach (int entry in storage.Palette)
            writer.WriteZigZag32(entry);
    }

    private static int Extract(uint[] words, int bits, int index)
    {
        if (bits == 0)
            return 0;
        uint mask = bits == 32 ? uint.MaxValue : (1u << bits) - 1;
        if (bits is 3 or 5 or 6)
        {
            int entriesPerWord = EntriesPerWord(bits);
            return (int)((words[index / entriesPerWord] >> ((index % entriesPerWord) * bits)) & mask);
        }

        int bitIndex = index * bits;
        return (int)((words[bitIndex >> 5] >> (bitIndex & 31)) & mask);
    }

    private static void Insert(uint[] words, int bits, int index, int value)
    {
        if (bits == 0)
            return;
        uint mask = bits == 32 ? uint.MaxValue : (1u << bits) - 1;
        if ((uint)value > mask)
            throw new BedrockCodecException($"The cell {index} holds palette entry {value} beyond width {bits}.");
        if (bits is 3 or 5 or 6)
        {
            int entriesPerWord = EntriesPerWord(bits);
            words[index / entriesPerWord] |= ((uint)value & mask) << ((index % entriesPerWord) * bits);
            return;
        }

        int bitIndex = index * bits;
        words[bitIndex >> 5] |= ((uint)value & mask) << (bitIndex & 31);
    }

    private static bool IsSupportedWidth(int bits) => bits is 0 or 1 or 2 or 3 or 4 or 5 or 6 or 8 or 16;

    private static int EntriesPerWord(int bits) => bits switch
    {
        0 => 0,
        1 => 32,
        2 => 16,
        3 => 10,
        4 => 8,
        5 => 6,
        6 => 5,
        8 => 4,
        16 => 2,
        _ => throw new BedrockCodecException($"The palette width {bits} is not a supported Bedrock bit width."),
    };

    private static int WordCount(int bits)
    {
        if (bits == 0)
            return 0;
        int entriesPerWord = EntriesPerWord(bits);
        return (BedrockBlockStorage.CellCount + entriesPerWord - 1) / entriesPerWord;
    }
}
