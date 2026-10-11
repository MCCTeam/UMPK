using System.Buffers;
using Umpk.Nbt;

namespace Umpk.Protocol.Bedrock;

// The trailing chunk payload follows the inline block sections with biome sections, border columns, and block actors.
// Biome entries either carry an explicit storage or repeat the previous storage with a single 0xFF marker byte.
// A repeat marker without a previous storage is malformed. Border data is a count byte followed by that many column bytes.
// Block actors are a sequence of network NBT roots; an empty remainder or a lone End root means no actors.
internal sealed class BedrockBorderBlockData : IEquatable<BedrockBorderBlockData>
{
    internal BedrockBorderBlockData(byte[] columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        Columns = (byte[])columns.Clone();
    }

    internal byte[] Columns { get; }

    public bool Equals(BedrockBorderBlockData? other) =>
        other is not null && Columns.AsSpan().SequenceEqual(other.Columns);

    public override bool Equals(object? obj) => Equals(obj as BedrockBorderBlockData);

    public override int GetHashCode() => Columns.Length;
}

internal sealed class BedrockBlockActorData : IEquatable<BedrockBlockActorData>
{
    internal BedrockBlockActorData(int x, int y, int z, string? identifier, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        X = x;
        Y = y;
        Z = z;
        Identifier = identifier;
        Payload = (byte[])payload.Clone();
    }

    internal int X { get; }

    internal int Y { get; }

    internal int Z { get; }

    internal string? Identifier { get; }

    internal byte[] Payload { get; }

    public bool Equals(BedrockBlockActorData? other) =>
        other is not null
        && X == other.X
        && Y == other.Y
        && Z == other.Z
        && Identifier == other.Identifier
        && Payload.AsSpan().SequenceEqual(other.Payload);

    public override bool Equals(object? obj) => Equals(obj as BedrockBlockActorData);

    public override int GetHashCode() => HashCode.Combine(X, Y, Z, Payload.Length);
}

internal sealed class BedrockLevelChunkTrailer : IEquatable<BedrockLevelChunkTrailer>
{
    internal BedrockLevelChunkTrailer(
        IReadOnlyList<BedrockChunkSection> blockSections,
        IReadOnlyList<BedrockBlockStorage> biomeSections,
        BedrockBorderBlockData border,
        IReadOnlyList<BedrockBlockActorData> actors)
    {
        ArgumentNullException.ThrowIfNull(blockSections);
        ArgumentNullException.ThrowIfNull(biomeSections);
        ArgumentNullException.ThrowIfNull(border);
        ArgumentNullException.ThrowIfNull(actors);
        BlockSections = [.. blockSections];
        BiomeSections = [.. biomeSections];
        Border = border;
        Actors = [.. actors];
    }

    internal IReadOnlyList<BedrockChunkSection> BlockSections { get; }

    internal IReadOnlyList<BedrockBlockStorage> BiomeSections { get; }

    internal BedrockBorderBlockData Border { get; }

    internal IReadOnlyList<BedrockBlockActorData> Actors { get; }

    public bool Equals(BedrockLevelChunkTrailer? other) =>
        other is not null
        && BlockSections.Count == other.BlockSections.Count
        && BiomeSections.Count == other.BiomeSections.Count
        && Border.Equals(other.Border)
        && Actors.SequenceEqual(other.Actors);

    public override bool Equals(object? obj) => Equals(obj as BedrockLevelChunkTrailer);

    public override int GetHashCode() => HashCode.Combine(BlockSections.Count, BiomeSections.Count, Actors.Count);
}

internal static class BedrockLevelChunkTrailerCodecs
{
    internal const int MaxSectionCount = 256;
    internal const int MaxBlockActors = 4096;
    private const byte RepeatPreviousBiome = 0xFF;

    internal static BedrockLevelChunkTrailer DecodeTrailer(
        ReadOnlySpan<byte> payload,
        int blockSectionCount,
        int biomeSectionCount,
        BedrockCodecLimits? limits = null)
    {
        if (blockSectionCount < 0 || blockSectionCount > MaxSectionCount)
            throw new BedrockCodecException($"The block section count {blockSectionCount} is outside the supported range 0 to {MaxSectionCount}.");
        if (biomeSectionCount < 0 || biomeSectionCount > MaxSectionCount)
            throw new BedrockCodecException($"The biome section count {biomeSectionCount} is outside the supported range 0 to {MaxSectionCount}.");
        BedrockCodecLimits actualLimits = limits ?? BedrockCodecLimits.Default;
        var reader = new BedrockReader(payload, actualLimits);
        var blocks = new BedrockChunkSection[blockSectionCount];
        for (int index = 0; index < blocks.Length; index++)
            blocks[index] = BedrockChunkPayloadCodecs.DecodeSection(ref reader);
        var biomes = new BedrockBlockStorage[biomeSectionCount];
        BedrockBlockStorage? previous = null;
        for (int index = 0; index < biomes.Length; index++)
        {
            if (!reader.End && reader.Peek() == RepeatPreviousBiome)
            {
                if (previous is null)
                    throw new BedrockCodecException("The biome data repeats a previous storage before any storage was decoded.");
                reader.ReadByte();
                biomes[index] = previous;
                continue;
            }

            BedrockBlockStorage storage = BedrockChunkPayloadCodecs.DecodeBiomeStoragePrefix(ref reader);
            biomes[index] = storage;
            previous = storage;
        }

        byte borderCount = reader.ReadByte();
        byte[] borderColumns = reader.ReadExact(borderCount).ToArray();
        var actors = ReadActors(ref reader, actualLimits);
        if (!reader.End)
            throw new BedrockCodecException("The level chunk trailing payload has unread bytes.");
        return new BedrockLevelChunkTrailer(blocks, biomes, new BedrockBorderBlockData(borderColumns), actors);
    }

    internal static byte[] EncodeTrailer(BedrockLevelChunkTrailer trailer)
    {
        ArgumentNullException.ThrowIfNull(trailer);
        if (trailer.BlockSections.Count > MaxSectionCount || trailer.BiomeSections.Count > MaxSectionCount)
            throw new BedrockCodecException("The trailer section count exceeds its supported range.");
        if (trailer.Actors.Count > MaxBlockActors)
            throw new BedrockCodecException($"The trailer actor count {trailer.Actors.Count} exceeds its supported maximum {MaxBlockActors}.");
        var output = new ArrayBufferWriter<byte>();
        var writer = new BedrockWriter(output);
        foreach (BedrockChunkSection section in trailer.BlockSections)
            writer.WriteBytes(BedrockChunkPayloadCodecs.EncodeSection(section));
        BedrockBlockStorage? previous = null;
        foreach (BedrockBlockStorage biome in trailer.BiomeSections)
        {
            if (previous is not null && ReferenceEquals(biome, previous))
            {
                writer.WriteByte(RepeatPreviousBiome);
                continue;
            }

            writer.WriteBytes(BedrockChunkPayloadCodecs.EncodeStorage(biome));
            previous = biome;
        }

        if (trailer.Border.Columns.Length > byte.MaxValue)
            throw new BedrockCodecException("The border column count exceeds its single-byte range.");
        writer.WriteByte((byte)trailer.Border.Columns.Length);
        writer.WriteBytes(trailer.Border.Columns);
        foreach (BedrockBlockActorData actor in trailer.Actors)
            writer.WriteBytes(actor.Payload);
        return output.WrittenSpan.ToArray();
    }

    private static List<BedrockBlockActorData> ReadActors(ref BedrockReader reader, BedrockCodecLimits limits)
    {
        var actors = new List<BedrockBlockActorData>();
        while (!reader.End)
        {
            if (actors.Count >= MaxBlockActors)
                throw new BedrockCodecException($"The block actor count exceeds its supported maximum {MaxBlockActors}.");
            NbtTag tag = BedrockNbtCodec.DecodePrefix(ref reader, BedrockNbtWireFormat.NetworkLittleVarInt, limits);
            if (tag is NbtEnd)
            {
                if (actors.Count != 0)
                    throw new BedrockCodecException("The block actor sequence carries an End root after entries.");
                return actors;
            }

            if (tag is not NbtCompound compound)
                throw new BedrockCodecException($"The block actor entry has NBT type {tag.Type} instead of a compound.");
            if (!compound.TryGet("x", out NbtTag? xTag) || xTag is not NbtInt x)
                throw new BedrockCodecException("The block actor entry carries no integer x coordinate.");
            if (!compound.TryGet("y", out NbtTag? yTag) || yTag is not NbtInt y)
                throw new BedrockCodecException("The block actor entry carries no integer y coordinate.");
            if (!compound.TryGet("z", out NbtTag? zTag) || zTag is not NbtInt z)
                throw new BedrockCodecException("The block actor entry carries no integer z coordinate.");
            string? identifier = compound.TryGet("id", out NbtTag? idTag) && idTag is NbtString id ? id.Value : null;
            actors.Add(new BedrockBlockActorData(x.Value, y.Value, z.Value, identifier, BedrockNbtCodec.Encode(compound, BedrockNbtWireFormat.NetworkLittleVarInt)));
        }

        return actors;
    }
}
