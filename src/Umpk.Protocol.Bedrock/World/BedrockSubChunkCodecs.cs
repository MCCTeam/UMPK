using System.Buffers;

namespace Umpk.Protocol.Bedrock;

internal enum BedrockSubChunkResult : byte
{
    Undefined = 0,
    Success = 1,
    ChunkNotFound = 2,
    InvalidDimension = 3,
    PlayerNotFound = 4,
    YIndexOutOfBounds = 5,
    SuccessAllAir = 6,
}

internal enum BedrockHeightMapDataType : byte
{
    NoData = 0,
    HasData = 1,
    TooHigh = 2,
    TooLow = 3,
    AllCopied = 4,
}

internal sealed class BedrockSubChunkOffset : IEquatable<BedrockSubChunkOffset>
{
    internal BedrockSubChunkOffset(int dx, int dy, int dz)
    {
        if (dx is < -128 or > 127)
            throw new ArgumentOutOfRangeException(nameof(dx), $"The sub-chunk offset {dx} is outside the signed byte range.");
        if (dy is < -128 or > 127)
            throw new ArgumentOutOfRangeException(nameof(dy), $"The sub-chunk offset {dy} is outside the signed byte range.");
        if (dz is < -128 or > 127)
            throw new ArgumentOutOfRangeException(nameof(dz), $"The sub-chunk offset {dz} is outside the signed byte range.");
        Dx = dx;
        Dy = dy;
        Dz = dz;
    }

    internal int Dx { get; }

    internal int Dy { get; }

    internal int Dz { get; }

    public bool Equals(BedrockSubChunkOffset? other) =>
        other is not null && Dx == other.Dx && Dy == other.Dy && Dz == other.Dz;

    public override bool Equals(object? obj) => Equals(obj as BedrockSubChunkOffset);

    public override int GetHashCode() => HashCode.Combine(Dx, Dy, Dz);
}

internal sealed class BedrockSubChunkPosition : IEquatable<BedrockSubChunkPosition>
{
    internal BedrockSubChunkPosition(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    internal int X { get; }

    internal int Y { get; }

    internal int Z { get; }

    public bool Equals(BedrockSubChunkPosition? other) =>
        other is not null && X == other.X && Y == other.Y && Z == other.Z;

    public override bool Equals(object? obj) => Equals(obj as BedrockSubChunkPosition);

    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
}

internal sealed class BedrockSubChunkEntry : IEquatable<BedrockSubChunkEntry>
{
    internal BedrockSubChunkEntry(
        BedrockSubChunkOffset offset,
        BedrockSubChunkResult result,
        byte[]? payload,
        BedrockHeightMapDataType heightMapType,
        byte[]? heightMap,
        BedrockHeightMapDataType renderHeightMapType,
        byte[]? renderHeightMap,
        ulong? blobId)
    {
        ArgumentNullException.ThrowIfNull(offset);
        if (heightMap is not null && heightMap.Length != BedrockSubChunkCodecs.HeightMapLength)
            throw new ArgumentException($"The height map carries {heightMap.Length} bytes instead of {BedrockSubChunkCodecs.HeightMapLength}.", nameof(heightMap));
        if (renderHeightMap is not null && renderHeightMap.Length != BedrockSubChunkCodecs.HeightMapLength)
            throw new ArgumentException($"The render height map carries {renderHeightMap.Length} bytes instead of {BedrockSubChunkCodecs.HeightMapLength}.", nameof(renderHeightMap));
        Offset = offset;
        Result = result;
        Payload = payload is null ? null : (byte[])payload.Clone();
        HeightMapType = heightMapType;
        HeightMap = heightMap is null ? null : (byte[])heightMap.Clone();
        RenderHeightMapType = renderHeightMapType;
        RenderHeightMap = renderHeightMap is null ? null : (byte[])renderHeightMap.Clone();
        BlobId = blobId;
    }

    internal BedrockSubChunkOffset Offset { get; }

    internal BedrockSubChunkResult Result { get; }

    internal byte[]? Payload { get; }

    internal BedrockHeightMapDataType HeightMapType { get; }

    internal byte[]? HeightMap { get; }

    internal BedrockHeightMapDataType RenderHeightMapType { get; }

    internal byte[]? RenderHeightMap { get; }

    internal ulong? BlobId { get; }

    public bool Equals(BedrockSubChunkEntry? other) =>
        other is not null
        && Offset.Equals(other.Offset)
        && Result == other.Result
        && NullableBytesEqual(Payload, other.Payload)
        && HeightMapType == other.HeightMapType
        && NullableBytesEqual(HeightMap, other.HeightMap)
        && RenderHeightMapType == other.RenderHeightMapType
        && NullableBytesEqual(RenderHeightMap, other.RenderHeightMap)
        && BlobId == other.BlobId;

    public override bool Equals(object? obj) => Equals(obj as BedrockSubChunkEntry);

    public override int GetHashCode() => HashCode.Combine(Offset, Result, HeightMapType, RenderHeightMapType, BlobId);

    private static bool NullableBytesEqual(byte[]? first, byte[]? second)
    {
        if (first is null || second is null)
            return first is null && second is null;
        return first.AsSpan().SequenceEqual(second);
    }
}

internal sealed class BedrockSubChunkResponsePacket : IEquatable<BedrockSubChunkResponsePacket>
{
    internal BedrockSubChunkResponsePacket(
        bool cacheEnabled,
        int dimension,
        BedrockSubChunkPosition center,
        IReadOnlyList<BedrockSubChunkEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(center);
        ArgumentNullException.ThrowIfNull(entries);
        CacheEnabled = cacheEnabled;
        Dimension = dimension;
        Center = center;
        Entries = [.. entries];
    }

    internal bool CacheEnabled { get; }

    internal int Dimension { get; }

    internal BedrockSubChunkPosition Center { get; }

    internal IReadOnlyList<BedrockSubChunkEntry> Entries { get; }

    public bool Equals(BedrockSubChunkResponsePacket? other) =>
        other is not null
        && CacheEnabled == other.CacheEnabled
        && Dimension == other.Dimension
        && Center.Equals(other.Center)
        && Entries.SequenceEqual(other.Entries);

    public override bool Equals(object? obj) => Equals(obj as BedrockSubChunkResponsePacket);

    public override int GetHashCode() => HashCode.Combine(CacheEnabled, Dimension, Center, Entries.Count);
}

internal sealed class BedrockSubChunkRequestPacket : IEquatable<BedrockSubChunkRequestPacket>
{
    internal BedrockSubChunkRequestPacket(
        int dimension,
        IReadOnlyList<BedrockSubChunkOffset> offsets,
        BedrockSubChunkPosition center)
    {
        ArgumentNullException.ThrowIfNull(offsets);
        ArgumentNullException.ThrowIfNull(center);
        Dimension = dimension;
        Offsets = [.. offsets];
        Center = center;
    }

    internal int Dimension { get; }

    internal IReadOnlyList<BedrockSubChunkOffset> Offsets { get; }

    internal BedrockSubChunkPosition Center { get; }

    public bool Equals(BedrockSubChunkRequestPacket? other) =>
        other is not null
        && Dimension == other.Dimension
        && Offsets.SequenceEqual(other.Offsets)
        && Center.Equals(other.Center);

    public override bool Equals(object? obj) => Equals(obj as BedrockSubChunkRequestPacket);

    public override int GetHashCode() => HashCode.Combine(Dimension, Center, Offsets.Count);
}

internal static class BedrockSubChunkCodecs
{
    internal const int HeightMapLength = 256;

    internal static byte[] EncodeRequest(BedrockSubChunkRequestPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        var output = new ArrayBufferWriter<byte>();
        var writer = new BedrockWriter(output);
        writer.WriteZigZag32(packet.Dimension);
        writer.WriteUInt32VarInt(checked((uint)packet.Offsets.Count));
        foreach (BedrockSubChunkOffset offset in packet.Offsets)
        {
            writer.WriteByte(unchecked((byte)offset.Dx));
            writer.WriteByte(unchecked((byte)offset.Dy));
            writer.WriteByte(unchecked((byte)offset.Dz));
        }

        WritePosition(writer, packet.Center);
        return output.WrittenSpan.ToArray();
    }

    internal static BedrockSubChunkRequestPacket DecodeRequest(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        BedrockCodecLimits actualLimits = limits ?? BedrockCodecLimits.Default;
        var reader = new BedrockReader(payload, actualLimits);
        int dimension = reader.ReadZigZag32();
        uint offsetCount = reader.ReadUInt32VarInt();
        if (offsetCount > actualLimits.MaxCollectionElements)
            throw new BedrockCodecException($"The sub-chunk request offset count {offsetCount} exceeds its element limit.");

        var offsets = new BedrockSubChunkOffset[offsetCount];
        for (int index = 0; index < offsets.Length; index++)
            offsets[index] = new BedrockSubChunkOffset(ReadOffsetComponent(ref reader), ReadOffsetComponent(ref reader), ReadOffsetComponent(ref reader));

        BedrockSubChunkPosition center = ReadPosition(ref reader);
        if (!reader.End)
            throw new BedrockCodecException("The sub-chunk request has unread bytes.");

        return new BedrockSubChunkRequestPacket(dimension, offsets, center);
    }

    internal static byte[] EncodeResponse(BedrockSubChunkResponsePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        var output = new ArrayBufferWriter<byte>();
        var writer = new BedrockWriter(output);
        writer.WriteBoolean(packet.CacheEnabled);
        writer.WriteZigZag32(packet.Dimension);
        WritePosition(writer, packet.Center);
        writer.WriteUInt32VarInt(checked((uint)packet.Entries.Count));
        foreach (BedrockSubChunkEntry entry in packet.Entries)
            WriteEntry(writer, entry);

        return output.WrittenSpan.ToArray();
    }

    internal static BedrockSubChunkResponsePacket DecodeResponse(ReadOnlySpan<byte> payload, BedrockCodecLimits? limits = null)
    {
        BedrockCodecLimits actualLimits = limits ?? BedrockCodecLimits.Default;
        var reader = new BedrockReader(payload, actualLimits);
        bool cacheEnabled = reader.ReadBoolean();
        int dimension = reader.ReadZigZag32();
        BedrockSubChunkPosition center = ReadPosition(ref reader);
        uint entryCount = reader.ReadUInt32VarInt();
        if (entryCount > actualLimits.MaxCollectionElements)
            throw new BedrockCodecException($"The sub-chunk entry count {entryCount} exceeds its element limit.");

        var entries = new BedrockSubChunkEntry[entryCount];
        for (int index = 0; index < entries.Length; index++)
            entries[index] = ReadEntry(ref reader);

        if (!reader.End)
            throw new BedrockCodecException("The sub-chunk response has unread bytes.");

        return new BedrockSubChunkResponsePacket(cacheEnabled, dimension, center, entries);
    }

    private static void WritePosition(BedrockWriter writer, BedrockSubChunkPosition position)
    {
        writer.WriteInt32LittleEndian(position.X);
        writer.WriteInt32LittleEndian(position.Y);
        writer.WriteInt32LittleEndian(position.Z);
    }

    private static BedrockSubChunkPosition ReadPosition(ref BedrockReader reader)
    {
        return new BedrockSubChunkPosition(reader.ReadInt32LittleEndian(), reader.ReadInt32LittleEndian(), reader.ReadInt32LittleEndian());
    }

    private static int ReadOffsetComponent(ref BedrockReader reader)
    {
        return unchecked((sbyte)reader.ReadByte());
    }

    private static void WriteEntry(BedrockWriter writer, BedrockSubChunkEntry entry)
    {
        writer.WriteByte(unchecked((byte)entry.Offset.Dx));
        writer.WriteByte(unchecked((byte)entry.Offset.Dy));
        writer.WriteByte(unchecked((byte)entry.Offset.Dz));
        writer.WriteByte((byte)entry.Result);
        writer.WriteBoolean(entry.Payload is not null);
        if (entry.Payload is not null)
            writer.WriteByteArray(entry.Payload);

        writer.WriteByte((byte)entry.HeightMapType);
        writer.WriteBoolean(entry.HeightMap is not null);
        if (entry.HeightMap is not null)
            writer.WriteBytes(entry.HeightMap);

        writer.WriteByte((byte)entry.RenderHeightMapType);
        writer.WriteBoolean(entry.RenderHeightMap is not null);
        if (entry.RenderHeightMap is not null)
            writer.WriteBytes(entry.RenderHeightMap);

        writer.WriteBoolean(entry.BlobId.HasValue);
        if (entry.BlobId.HasValue)
            writer.WriteUInt64LittleEndian(entry.BlobId.Value);
    }

    private static BedrockSubChunkEntry ReadEntry(ref BedrockReader reader)
    {
        var offset = new BedrockSubChunkOffset(ReadOffsetComponent(ref reader), ReadOffsetComponent(ref reader), ReadOffsetComponent(ref reader));
        BedrockSubChunkResult result = ReadResult(reader.ReadByte());
        byte[]? payload = reader.ReadBoolean() ? reader.ReadByteArray() : null;
        BedrockHeightMapDataType heightMapType = ReadHeightMapType(reader.ReadByte());
        byte[]? heightMap = reader.ReadBoolean() ? reader.ReadExact(HeightMapLength).ToArray() : null;
        BedrockHeightMapDataType renderHeightMapType = ReadHeightMapType(reader.ReadByte());
        byte[]? renderHeightMap = reader.ReadBoolean() ? reader.ReadExact(HeightMapLength).ToArray() : null;
        ulong? blobId = reader.ReadBoolean() ? reader.ReadUInt64LittleEndian() : null;
        return new BedrockSubChunkEntry(offset, result, payload, heightMapType, heightMap, renderHeightMapType, renderHeightMap, blobId);
    }

    private static BedrockSubChunkResult ReadResult(byte value)
    {
        return value switch
        {
            0 => BedrockSubChunkResult.Undefined,
            1 => BedrockSubChunkResult.Success,
            2 => BedrockSubChunkResult.ChunkNotFound,
            3 => BedrockSubChunkResult.InvalidDimension,
            4 => BedrockSubChunkResult.PlayerNotFound,
            5 => BedrockSubChunkResult.YIndexOutOfBounds,
            6 => BedrockSubChunkResult.SuccessAllAir,
            _ => throw new BedrockCodecException($"The sub-chunk result {value} is not supported."),
        };
    }

    private static BedrockHeightMapDataType ReadHeightMapType(byte value)
    {
        return value switch
        {
            0 => BedrockHeightMapDataType.NoData,
            1 => BedrockHeightMapDataType.HasData,
            2 => BedrockHeightMapDataType.TooHigh,
            3 => BedrockHeightMapDataType.TooLow,
            4 => BedrockHeightMapDataType.AllCopied,
            _ => throw new BedrockCodecException($"The height map data type {value} is not supported."),
        };
    }
}

internal sealed class BedrockLevelChunkBody : IEquatable<BedrockLevelChunkBody>
{
    internal BedrockLevelChunkBody(IReadOnlyList<BedrockChunkSection> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);
        Sections = [.. sections];
    }

    internal IReadOnlyList<BedrockChunkSection> Sections { get; }

    public bool Equals(BedrockLevelChunkBody? other) =>
        other is not null && Sections.Count == other.Sections.Count;

    public override bool Equals(object? obj) => Equals(obj as BedrockLevelChunkBody);

    public override int GetHashCode() => Sections.Count;
}

internal static class BedrockLevelChunkAssembly
{
    internal const int MaxSubChunkCount = 64;

    internal static BedrockLevelChunkBody Assemble(LevelChunkPacket packet, BedrockCodecLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (packet.CacheEnabled)
            throw new BedrockCodecException("The cache-enabled level chunk payload carries blob references instead of sub-chunk sections and is not supported.");
        if (packet.SubChunkCount > MaxSubChunkCount)
            throw new BedrockCodecException($"The level chunk section count {packet.SubChunkCount} exceeds the supported maximum {MaxSubChunkCount}.");

        var reader = new BedrockReader(packet.Payload, limits);
        var sections = new BedrockChunkSection[packet.SubChunkCount];
        for (int index = 0; index < sections.Length; index++)
            sections[index] = BedrockChunkPayloadCodecs.DecodeSection(ref reader);

        if (!reader.End)
            throw new BedrockCodecException("The level chunk payload has unread bytes after its declared sections.");

        return new BedrockLevelChunkBody(sections);
    }
}

internal sealed class BedrockSubChunkMatchResult
{
    internal BedrockSubChunkMatchResult(bool stale, IReadOnlyList<BedrockSubChunkEntry> matched, IReadOnlyList<BedrockSubChunkEntry> unsolicited)
    {
        ArgumentNullException.ThrowIfNull(matched);
        ArgumentNullException.ThrowIfNull(unsolicited);
        Stale = stale;
        Matched = [.. matched];
        Unsolicited = [.. unsolicited];
    }

    internal bool Stale { get; }

    internal IReadOnlyList<BedrockSubChunkEntry> Matched { get; }

    internal IReadOnlyList<BedrockSubChunkEntry> Unsolicited { get; }
}

internal sealed class BedrockSubChunkCorrelator
{
    internal const int MaxPendingRequests = 256;
    internal const int MaxPendingOffsets = 4096;

    private readonly Dictionary<PendingKey, byte> _pending = new();
    private long _generation;
    private int _requests;
    private bool _hasGeneration;

    internal bool TryRegister(BedrockSubChunkRequestPacket request, long generation)
    {
        ArgumentNullException.ThrowIfNull(request);
        ResetForGeneration(generation);
        if (_requests >= MaxPendingRequests)
            return false;
        if (_pending.Count + request.Offsets.Count > MaxPendingOffsets)
            return false;

        foreach (BedrockSubChunkOffset offset in request.Offsets)
            _pending[new PendingKey(generation, request.Dimension, request.Center, offset)] = 0;

        _requests++;
        return true;
    }

    internal BedrockSubChunkMatchResult Match(BedrockSubChunkResponsePacket response, long generation)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (!_hasGeneration || generation != _generation)
            return new BedrockSubChunkMatchResult(true, [], []);

        var matched = new List<BedrockSubChunkEntry>();
        var unsolicited = new List<BedrockSubChunkEntry>();
        foreach (BedrockSubChunkEntry entry in response.Entries)
        {
            var key = new PendingKey(generation, response.Dimension, response.Center, entry.Offset);
            if (_pending.Remove(key))
                matched.Add(entry);
            else
                unsolicited.Add(entry);
        }

        return new BedrockSubChunkMatchResult(false, matched, unsolicited);
    }

    internal void Invalidate(long generation)
    {
        _pending.Clear();
        _requests = 0;
        _generation = generation;
        _hasGeneration = true;
    }

    private void ResetForGeneration(long generation)
    {
        if (_hasGeneration && generation == _generation)
            return;

        _pending.Clear();
        _requests = 0;
        _generation = generation;
        _hasGeneration = true;
    }

    private readonly record struct PendingKey(long Generation, int Dimension, int CenterX, int CenterY, int CenterZ, int Dx, int Dy, int Dz)
    {
        internal PendingKey(long generation, int dimension, BedrockSubChunkPosition center, BedrockSubChunkOffset offset)
            : this(generation, dimension, center.X, center.Y, center.Z, offset.Dx, offset.Dy, offset.Dz)
        {
        }
    }
}
