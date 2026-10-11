using System.Buffers;

namespace Umpk.Protocol.Bedrock;

// UpdateSubChunkBlocks carries bulk layered block edits for one sub-chunk with exact block positions, session runtime IDs, raw update flags, and falling-block transition data.
// Positions use zigzag32 coordinates so negative worlds survive the round trip.
// Runtime IDs and flags stay opaque here; resolution against the live palette belongs to later evidence, so unknown and custom IDs are preserved and never read as air.
// The synced entity ID is a signed long carried as a raw unsigned varint so every wire bit survives; -1 marks edits with no falling-block transition.
// The transition ordinal selects none, create, or destroy; any other ordinal is rejected.
// The standard list updates storage layer 0 and the extra list updates storage layer 1, which carries waterlogged blocks.
internal enum BedrockSubChunkBlockTransition : uint
{
    None = 0,
    Create = 1,
    Destroy = 2,
}

internal sealed class BedrockSubChunkBlockChange : IEquatable<BedrockSubChunkBlockChange>
{
    internal BedrockSubChunkBlockChange(int x, int y, int z, uint blockRuntimeId, uint flags, long syncedEntityId, BedrockSubChunkBlockTransition transition)
    {
        if (transition is not (BedrockSubChunkBlockTransition.None or BedrockSubChunkBlockTransition.Create or BedrockSubChunkBlockTransition.Destroy))
            throw new BedrockCodecException($"The sub-chunk block transition {(uint)transition} is outside the supported range 0 to 2.");
        X = x;
        Y = y;
        Z = z;
        BlockRuntimeId = blockRuntimeId;
        Flags = flags;
        SyncedEntityId = syncedEntityId;
        Transition = transition;
    }

    internal int X { get; }

    internal int Y { get; }

    internal int Z { get; }

    internal uint BlockRuntimeId { get; }

    internal uint Flags { get; }

    internal long SyncedEntityId { get; }

    internal BedrockSubChunkBlockTransition Transition { get; }

    public bool Equals(BedrockSubChunkBlockChange? other) =>
        other is not null
        && X == other.X
        && Y == other.Y
        && Z == other.Z
        && BlockRuntimeId == other.BlockRuntimeId
        && Flags == other.Flags
        && SyncedEntityId == other.SyncedEntityId
        && Transition == other.Transition;

    public override bool Equals(object? obj) => Equals(obj as BedrockSubChunkBlockChange);

    public override int GetHashCode() => HashCode.Combine(X, Y, Z, BlockRuntimeId, Flags, SyncedEntityId, Transition);
}

internal sealed class BedrockUpdateSubChunkBlocksPacket : IEquatable<BedrockUpdateSubChunkBlocksPacket>
{
    internal BedrockUpdateSubChunkBlocksPacket(int x, int y, int z, IReadOnlyList<BedrockSubChunkBlockChange> blocks, IReadOnlyList<BedrockSubChunkBlockChange> extra)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(extra);
        X = x;
        Y = y;
        Z = z;
        Blocks = [.. blocks];
        Extra = [.. extra];
    }

    internal int X { get; }

    internal int Y { get; }

    internal int Z { get; }

    internal IReadOnlyList<BedrockSubChunkBlockChange> Blocks { get; }

    internal IReadOnlyList<BedrockSubChunkBlockChange> Extra { get; }

    public bool Equals(BedrockUpdateSubChunkBlocksPacket? other) =>
        other is not null
        && X == other.X
        && Y == other.Y
        && Z == other.Z
        && Blocks.SequenceEqual(other.Blocks)
        && Extra.SequenceEqual(other.Extra);

    public override bool Equals(object? obj) => Equals(obj as BedrockUpdateSubChunkBlocksPacket);

    public override int GetHashCode() => HashCode.Combine(X, Y, Z, Blocks.Count, Extra.Count);
}

internal static class BedrockUpdateSubChunkBlocksCodecs
{
    internal const int MaxEntriesPerLayer = 4096;

    internal static byte[] Encode(BedrockUpdateSubChunkBlocksPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        var output = new ArrayBufferWriter<byte>();
        var writer = new BedrockWriter(output);
        writer.WriteZigZag32(packet.X);
        writer.WriteZigZag32(packet.Y);
        writer.WriteZigZag32(packet.Z);
        writer.WriteUInt32VarInt(checked((uint)packet.Blocks.Count));
        foreach (BedrockSubChunkBlockChange change in packet.Blocks)
            WriteChange(writer, change);
        writer.WriteUInt32VarInt(checked((uint)packet.Extra.Count));
        foreach (BedrockSubChunkBlockChange change in packet.Extra)
            WriteChange(writer, change);
        return output.WrittenSpan.ToArray();
    }

    internal static BedrockUpdateSubChunkBlocksPacket Decode(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        BedrockCodecLimits actualLimits = limits ?? BedrockCodecLimits.Default;
        var reader = new BedrockReader(payload, actualLimits);
        int x = reader.ReadZigZag32();
        int y = reader.ReadZigZag32();
        int z = reader.ReadZigZag32();
        uint blockCount = reader.ReadUInt32VarInt();
        if (blockCount > actualLimits.MaxCollectionElements)
            throw new BedrockCodecException($"The sub-chunk block count {blockCount} exceeds its element limit.");
        if (blockCount > MaxEntriesPerLayer)
            throw new BedrockCodecException($"The sub-chunk block count {blockCount} exceeds the {MaxEntriesPerLayer} cells of one sub-chunk layer.");
        var blocks = new BedrockSubChunkBlockChange[blockCount];
        for (int index = 0; index < blocks.Length; index++)
            blocks[index] = ReadChange(ref reader);
        uint extraCount = reader.ReadUInt32VarInt();
        if (extraCount > actualLimits.MaxCollectionElements)
            throw new BedrockCodecException($"The sub-chunk extra block count {extraCount} exceeds its element limit.");
        if (extraCount > MaxEntriesPerLayer)
            throw new BedrockCodecException($"The sub-chunk extra block count {extraCount} exceeds the {MaxEntriesPerLayer} cells of one sub-chunk layer.");
        var extra = new BedrockSubChunkBlockChange[extraCount];
        for (int index = 0; index < extra.Length; index++)
            extra[index] = ReadChange(ref reader);
        if (!reader.End)
            throw new BedrockCodecException("The update sub-chunk blocks packet has unread bytes.");
        return new BedrockUpdateSubChunkBlocksPacket(x, y, z, blocks, extra);
    }

    private static void WriteChange(BedrockWriter writer, BedrockSubChunkBlockChange change)
    {
        writer.WriteZigZag32(change.X);
        writer.WriteZigZag32(change.Y);
        writer.WriteZigZag32(change.Z);
        writer.WriteUInt32VarInt(change.BlockRuntimeId);
        writer.WriteUInt32VarInt(change.Flags);
        writer.WriteUInt64VarInt(unchecked((ulong)change.SyncedEntityId));
        writer.WriteUInt32VarInt((uint)change.Transition);
    }

    private static BedrockSubChunkBlockChange ReadChange(ref BedrockReader reader)
    {
        int x = reader.ReadZigZag32();
        int y = reader.ReadZigZag32();
        int z = reader.ReadZigZag32();
        uint blockRuntimeId = reader.ReadUInt32VarInt();
        uint flags = reader.ReadUInt32VarInt();
        long syncedEntityId = unchecked((long)reader.ReadUInt64VarInt());
        uint transition = reader.ReadUInt32VarInt();
        if (transition > 2)
            throw new BedrockCodecException($"The sub-chunk block transition {transition} is outside the supported range 0 to 2.");
        return new BedrockSubChunkBlockChange(x, y, z, blockRuntimeId, flags, syncedEntityId, (BedrockSubChunkBlockTransition)transition);
    }
}
