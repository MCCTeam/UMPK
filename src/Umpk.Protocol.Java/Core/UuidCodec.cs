using System.Buffers.Binary;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>UUID wire conversions. Minecraft transmits a UUID as two big-endian 64-bit halves (most, least) matching Java's <c>UUID.getMostSignificantBits</c> / <c>getLeastSignificantBits</c>. The.NET <see cref="Guid"/> in-memory layout is not big-endian, so both halves are byte-order corrected explicitly here.</summary>
internal static class UuidCodec
{
    /// <summary>Builds a <see cref="Guid"/> from Java most/least-significant 64-bit halves.</summary>
    public static Guid FromMostLeast(long most, long least)
    {
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteInt64BigEndian(bytes[..8], most);
        BinaryPrimitives.WriteInt64BigEndian(bytes[8..], least);
        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>Splits a <see cref="Guid"/> into Java most/least-significant 64-bit halves.</summary>
    public static (long Most, long Least) ToMostLeast(Guid value)
    {
        Span<byte> bytes = stackalloc byte[16];
        value.TryWriteBytes(bytes, bigEndian: true, out _);
        long most = BinaryPrimitives.ReadInt64BigEndian(bytes[..8]);
        long least = BinaryPrimitives.ReadInt64BigEndian(bytes[8..]);
        return (most, least);
    }
}
