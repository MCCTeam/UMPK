using System.Buffers;

namespace Umpk.Protocol.Bedrock;

internal sealed class LevelChunkPacket : IEquatable<LevelChunkPacket>
{
    internal LevelChunkPacket(
        int x,
        int z,
        int dimension,
        uint subChunkCount,
        int? highestSubchunkCount,
        bool cacheEnabled,
        IReadOnlyList<ulong> blobHashes,
        byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(blobHashes);
        ArgumentNullException.ThrowIfNull(payload);
        X = x;
        Z = z;
        Dimension = dimension;
        SubChunkCount = subChunkCount;
        HighestSubchunkCount = highestSubchunkCount;
        CacheEnabled = cacheEnabled;
        BlobHashes = blobHashes.ToArray();
        Payload = (byte[])payload.Clone();
    }

    internal int X { get; }

    internal int Z { get; }

    internal int Dimension { get; }

    internal uint SubChunkCount { get; }

    internal int? HighestSubchunkCount { get; }

    internal bool CacheEnabled { get; }

    internal IReadOnlyList<ulong> BlobHashes { get; }

    internal byte[] Payload { get; }

    public bool Equals(LevelChunkPacket? other) =>
        other is not null
        && X == other.X
        && Z == other.Z
        && Dimension == other.Dimension
        && SubChunkCount == other.SubChunkCount
        && HighestSubchunkCount == other.HighestSubchunkCount
        && CacheEnabled == other.CacheEnabled
        && BlobHashes.SequenceEqual(other.BlobHashes)
        && Payload.AsSpan().SequenceEqual(other.Payload);

    public override bool Equals(object? obj) => Equals(obj as LevelChunkPacket);

    public override int GetHashCode() => HashCode.Combine(X, Z, Dimension, SubChunkCount, HighestSubchunkCount, CacheEnabled);
}

internal static class BedrockWorldCodecs
{
    internal static byte[] Encode(LevelChunkPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        var output = new ArrayBufferWriter<byte>();
        var writer = new BedrockWriter(output);
        writer.WriteZigZag32(packet.X);
        writer.WriteZigZag32(packet.Z);
        writer.WriteZigZag32(packet.Dimension);
        writer.WriteUInt32VarInt(packet.SubChunkCount);
        writer.WriteBoolean(packet.HighestSubchunkCount.HasValue);
        if (packet.HighestSubchunkCount.HasValue)
            writer.WriteZigZag32(packet.HighestSubchunkCount.Value);

        writer.WriteBoolean(packet.CacheEnabled);
        writer.WriteUInt32VarInt(checked((uint)packet.BlobHashes.Count));
        foreach (ulong blobHash in packet.BlobHashes)
            writer.WriteUInt64LittleEndian(blobHash);

        writer.WriteByteArray(packet.Payload);
        return output.WrittenSpan.ToArray();
    }

    internal static LevelChunkPacket DecodeLevelChunk(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        BedrockCodecLimits actualLimits = limits ?? BedrockCodecLimits.Default;
        var reader = new BedrockReader(payload, actualLimits);
        int x = reader.ReadZigZag32();
        int z = reader.ReadZigZag32();
        int dimension = reader.ReadZigZag32();
        uint subChunkCount = reader.ReadUInt32VarInt();
        int? highestSubchunkCount = reader.ReadBoolean() ? reader.ReadZigZag32() : null;
        bool cacheEnabled = reader.ReadBoolean();
        uint blobCount = reader.ReadUInt32VarInt();
        if (blobCount > actualLimits.MaxCollectionElements)
            throw new BedrockCodecException("The chunk blob count exceeds its element limit.");

        var blobHashes = new ulong[blobCount];
        for (int index = 0; index < blobHashes.Length; index++)
            blobHashes[index] = reader.ReadUInt64LittleEndian();

        byte[] chunkPayload = reader.ReadByteArray();
        if (!reader.End)
            throw new BedrockCodecException("The level chunk packet has unread bytes.");

        return new LevelChunkPacket(x, z, dimension, subChunkCount, highestSubchunkCount, cacheEnabled, blobHashes, chunkPayload);
    }
}
