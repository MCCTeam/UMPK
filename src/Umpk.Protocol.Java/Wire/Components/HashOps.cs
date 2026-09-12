using System.Buffers.Binary;
using Umpk.Protocol.Java.Crypto;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>The structural hashing model for the 1.21.5+ hashed-slot container-click optimization. A component's hash is a CRC32C over a canonical, type-tagged structural encoding of its data form, not a CRC of its wire bytes. It uses fixed tag bytes, little-endian numeric encoding, the same map-entry sort by (key-hash, value-hash), and the same string encoding (tag, 4-byte char count, UTF-16 chars little-endian).</summary>
/// <remarks><c>HashedPatchMap</c> transmits these values as per-component integer hashes.</remarks>
internal readonly struct HashOps
{
    // Structural type tags used by the hashing format.
    private const byte TagEmpty = 1;

    private const byte TagMapStart = 2;

    private const byte TagMapEnd = 3;

    private const byte TagListStart = 4;

    private const byte TagListEnd = 5;

    private const byte TagByte = 6;

    private const byte TagShort = 7;

    private const byte TagInt = 8;

    private const byte TagLong = 9;

    private const byte TagFloat = 10;

    private const byte TagDouble = 11;

    private const byte TagString = 12;

    private const byte TagBoolean = 13;

    private const byte TagByteArrayStart = 14;

    private const byte TagByteArrayEnd = 15;

    private const byte TagIntArrayStart = 16;

    private const byte TagIntArrayEnd = 17;

    private const byte TagLongArrayStart = 18;

    private const byte TagLongArrayEnd = 19;

    private readonly bool _forceSoftware;

    /// <summary>Creates a HashOps instance. <paramref name="forceSoftwareCrc"/> forces the software CRC path.</summary>
    public HashOps(bool forceSoftwareCrc = false) => _forceSoftware = forceSoftwareCrc;

    /// <summary>The precomputed hash of the empty value (tag 1).</summary>
    public int Empty => Crc([TagEmpty]);

    /// <summary>The precomputed hash of an empty map (tags 2,3).</summary>
    public int EmptyMap => Crc([TagMapStart, TagMapEnd]);

    /// <summary>The precomputed hash of an empty list (tags 4,5).</summary>
    public int EmptyList => Crc([TagListStart, TagListEnd]);

    /// <summary>Hashes a signed byte (tag 6, then the byte).</summary>
    public int Byte(sbyte value) => Crc([TagByte, unchecked((byte)value)]);

    /// <summary>Hashes a 16-bit integer (tag 7, then little-endian short).</summary>
    public int Short(short value)
    {
        Span<byte> b = [TagShort, 0, 0];
        BinaryPrimitives.WriteInt16LittleEndian(b[1..], value);
        return Crc(b);
    }

    /// <summary>Hashes a 32-bit integer (tag 8, then little-endian int).</summary>
    public int Int(int value)
    {
        Span<byte> b = [TagInt, 0, 0, 0, 0];
        BinaryPrimitives.WriteInt32LittleEndian(b[1..], value);
        return Crc(b);
    }

    /// <summary>Hashes a 64-bit integer (tag 9, then little-endian long).</summary>
    public int Long(long value)
    {
        Span<byte> b = [TagLong, 0, 0, 0, 0, 0, 0, 0, 0];
        BinaryPrimitives.WriteInt64LittleEndian(b[1..], value);
        return Crc(b);
    }

    /// <summary>Hashes a 32-bit float (tag 10, then little-endian raw int bits).</summary>
    public int Float(float value)
    {
        Span<byte> b = [TagFloat, 0, 0, 0, 0];
        BinaryPrimitives.WriteInt32LittleEndian(b[1..], BitConverter.SingleToInt32Bits(value));
        return Crc(b);
    }

    /// <summary>Hashes a 64-bit double (tag 11, then little-endian raw long bits).</summary>
    public int Double(double value)
    {
        Span<byte> b = [TagDouble, 0, 0, 0, 0, 0, 0, 0, 0];
        BinaryPrimitives.WriteInt64LittleEndian(b[1..], BitConverter.DoubleToInt64Bits(value));
        return Crc(b);
    }

    /// <summary>Hashes a boolean (tag 13, then 0/1).</summary>
    public int Boolean(bool value) => Crc([TagBoolean, value ? (byte)1 : (byte)0]);

    /// <summary>Hashes a string: tag 12, then the char count as a little-endian 4-byte int (UTF-16 char count, matching UTF-16 length), then each character as two little-endian bytes. Surrogate pairs count as two characters.</summary>
    public int String(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        int charCount = value.Length;
        var buffer = new byte[1 + 4 + (charCount * 2)];
        buffer[0] = TagString;
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(1, 4), charCount);
        int pos = 5;
        for (int i = 0; i < charCount; i++)
        {
            char c = value[i];
            buffer[pos++] = (byte)c;
            buffer[pos++] = (byte)(c >> 8);
        }

        return Crc(buffer);
    }

    /// <summary>Hashes a map: tag 2, then each (keyHash, valueHash) pair as 8 raw bytes (two 4-byte little-endian hashes), sorted by (key-hash, value-hash) compared as unsigned 32-bit values widened to longs, then tag 3.</summary>
    public int Map(IReadOnlyList<(int Key, int Value)> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var sorted = new (int Key, int Value)[entries.Count];
        for (int i = 0; i < entries.Count; i++)
            sorted[i] = entries[i];

        Array.Sort(sorted, static (a, b) =>
        {
            int keyCmp = PadToLong(a.Key).CompareTo(PadToLong(b.Key));
            return keyCmp != 0 ? keyCmp : PadToLong(a.Value).CompareTo(PadToLong(b.Value));
        });

        var buffer = new byte[2 + (sorted.Length * 8)];
        buffer[0] = TagMapStart;
        int pos = 1;
        foreach ((int key, int value) in sorted)
        {
            WriteHashBytes(buffer.AsSpan(pos), key);
            WriteHashBytes(buffer.AsSpan(pos + 4), value);
            pos += 8;
        }

        buffer[pos] = TagMapEnd;
        return Crc(buffer);
    }

    /// <summary>Hashes a list: tag 4, then each element hash as 4 raw bytes in order, then tag 5.</summary>
    public int List(IReadOnlyList<int> elementHashes)
    {
        ArgumentNullException.ThrowIfNull(elementHashes);
        var buffer = new byte[2 + (elementHashes.Count * 4)];
        buffer[0] = TagListStart;
        int pos = 1;
        foreach (int h in elementHashes)
        {
            WriteHashBytes(buffer.AsSpan(pos), h);
            pos += 4;
        }

        buffer[pos] = TagListEnd;
        return Crc(buffer);
    }

    /// <summary>Hashes a byte array: tag 14, then the raw bytes, then tag 15.</summary>
    public int ByteList(ReadOnlySpan<byte> bytes)
    {
        var buffer = new byte[2 + bytes.Length];
        buffer[0] = TagByteArrayStart;
        bytes.CopyTo(buffer.AsSpan(1));
        buffer[^1] = TagByteArrayEnd;
        return Crc(buffer);
    }

    /// <summary>Hashes an int array: tag 16, then each int little-endian, then tag 17.</summary>
    public int IntList(IReadOnlyList<int> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var buffer = new byte[2 + (values.Count * 4)];
        buffer[0] = TagIntArrayStart;
        int pos = 1;
        foreach (int v in values)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(pos, 4), v);
            pos += 4;
        }

        buffer[pos] = TagIntArrayEnd;
        return Crc(buffer);
    }

    /// <summary>Hashes a long array: tag 18, then each long little-endian, then tag 19.</summary>
    public int LongList(IReadOnlyList<long> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var buffer = new byte[2 + (values.Count * 8)];
        buffer[0] = TagLongArrayStart;
        int pos = 1;
        foreach (long v in values)
        {
            BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(pos, 8), v);
            pos += 8;
        }

        buffer[pos] = TagLongArrayEnd;
        return Crc(buffer);
    }

    // A CRC32C hash is written as four little-endian bytes.
    private static void WriteHashBytes(Span<byte> destination, int hash) =>
        BinaryPrimitives.WriteInt32LittleEndian(destination, hash);

    // Ordering widens the hash as an unsigned 32-bit value to a non-negative long.
    private static long PadToLong(int hash) => hash & 0xFFFFFFFFL;

    private int Crc(ReadOnlySpan<byte> data) => unchecked((int)Crc32C.Compute(data, _forceSoftware));
}
