using System.Buffers.Binary;

namespace Umpk.Nbt;

/// <summary>Decodes NBT from a big-endian byte span, enforcing depth and size limits through an <see cref="NbtAccounter"/>. Truncated input, absurd declared lengths, illegal type ids, and depth bombs raise <see cref="NbtFormatException"/> or a derived type rather than range or memory faults.</summary>
public static class NbtReader
{
    /// <summary>Reads a root tag from <paramref name="source"/> using the given flavor and a default accounter.</summary>
    /// <exception cref="NbtFormatException">The input is malformed, truncated, or exceeds a limit.</exception>
    public static NbtTag Read(ReadOnlySpan<byte> source, NbtWireFormat format)
        => Read(source, format, NbtAccounter.CreateDefault());

    /// <summary>Convenience overload reading from a byte array.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <exception cref="NbtFormatException">The input is malformed, truncated, or exceeds a limit.</exception>
    public static NbtTag Read(byte[] source, NbtWireFormat format)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Read(source.AsSpan(), format, NbtAccounter.CreateDefault());
    }

    /// <summary>Reads a root tag from <paramref name="source"/>. The whole root value must be consumed; trailing bytes raise <see cref="NbtFormatException"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="accounter"/> is null.</exception>
    /// <exception cref="NbtFormatException">The input is malformed, truncated, or exceeds a limit.</exception>
    public static NbtTag Read(ReadOnlySpan<byte> source, NbtWireFormat format, NbtAccounter accounter)
    {
        ArgumentNullException.ThrowIfNull(accounter);
        int offset = 0;
        NbtTag result = ReadRoot(source, ref offset, format, accounter);
        if (offset != source.Length)
            throw new NbtFormatException(
                $"Trailing NBT data: {source.Length - offset} bytes remain after the root tag");

        return result;
    }

    /// <summary>Reads a root tag and reports how many bytes were consumed via <paramref name="bytesRead"/>, without requiring the span to be fully consumed. Useful when NBT is embedded in a larger frame.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="accounter"/> is null.</exception>
    /// <exception cref="NbtFormatException">The input is malformed, truncated, or exceeds a limit.</exception>
    public static NbtTag Read(ReadOnlySpan<byte> source, NbtWireFormat format, NbtAccounter accounter, out int bytesRead)
    {
        ArgumentNullException.ThrowIfNull(accounter);
        int offset = 0;
        NbtTag result = ReadRoot(source, ref offset, format, accounter);
        bytesRead = offset;
        return result;
    }

    private static NbtTag ReadRoot(ReadOnlySpan<byte> source, ref int offset, NbtWireFormat format, NbtAccounter accounter)
    {
        byte typeId = ReadByte(source, ref offset);
        if (typeId == (byte)NbtTagType.End)
        {
            // Both network flavors admit a bare End byte as the null-NBT marker.
            return NbtEnd.Instance;
        }

        NbtTagType type = ValidateType(typeId);

        if (format == NbtWireFormat.JavaNamedRoot)
        {
            // Root name string precedes the body; vanilla writes "" but we skip whatever is present.
            _ = ModifiedUtf8.Read(source, ref offset);
            if (type != NbtTagType.Compound)
                throw new NbtFormatException("Named-root NBT must have a compound root tag");

        }

        return ReadBody(source, ref offset, type, accounter);
    }

    private static NbtTag ReadBody(ReadOnlySpan<byte> source, ref int offset, NbtTagType type, NbtAccounter accounter)
    {
        switch (type)
        {
            case NbtTagType.End:
                accounter.AccountBytes(8);
                return NbtEnd.Instance;

            case NbtTagType.Byte:
                accounter.AccountBytes(9);
                return new NbtByte(unchecked((sbyte)ReadByte(source, ref offset)));

            case NbtTagType.Short:
                accounter.AccountBytes(10);
                return new NbtShort(ReadShort(source, ref offset));

            case NbtTagType.Int:
                accounter.AccountBytes(12);
                return new NbtInt(ReadInt(source, ref offset));

            case NbtTagType.Long:
                accounter.AccountBytes(16);
                return new NbtLong(ReadLong(source, ref offset));

            case NbtTagType.Float:
                accounter.AccountBytes(12);
                return new NbtFloat(BitConverter.Int32BitsToSingle(ReadInt(source, ref offset)));

            case NbtTagType.Double:
                accounter.AccountBytes(16);
                return new NbtDouble(BitConverter.Int64BitsToDouble(ReadLong(source, ref offset)));

            case NbtTagType.ByteArray:
                return ReadByteArray(source, ref offset, accounter);

            case NbtTagType.String:
                return ReadString(source, ref offset, accounter);

            case NbtTagType.List:
                return ReadList(source, ref offset, accounter);

            case NbtTagType.Compound:
                return ReadCompound(source, ref offset, accounter);

            case NbtTagType.IntArray:
                return ReadIntArray(source, ref offset, accounter);

            case NbtTagType.LongArray:
                return ReadLongArray(source, ref offset, accounter);

            default:
                throw new NbtFormatException($"Unknown NBT tag type {(byte)type}");
        }
    }

    private static NbtByteArray ReadByteArray(ReadOnlySpan<byte> source, ref int offset, NbtAccounter accounter)
    {
        accounter.AccountBytes(24);
        int length = ReadArrayLength(source, ref offset);
        accounter.AccountBytes(1, length);
        EnsureAvailable(source, offset, length);
        var data = new sbyte[length];
        for (int i = 0; i < length; i++)
            data[i] = unchecked((sbyte)source[offset + i]);

        offset += length;
        return new NbtByteArray(data);
    }

    private static NbtIntArray ReadIntArray(ReadOnlySpan<byte> source, ref int offset, NbtAccounter accounter)
    {
        accounter.AccountBytes(24);
        int length = ReadArrayLength(source, ref offset);
        accounter.AccountBytes(4, length);
        EnsureAvailableWide(source, offset, (long)length * 4);
        var data = new int[length];
        for (int i = 0; i < length; i++)
        {
            data[i] = BinaryPrimitives.ReadInt32BigEndian(source.Slice(offset, 4));
            offset += 4;
        }

        return new NbtIntArray(data);
    }

    private static NbtLongArray ReadLongArray(ReadOnlySpan<byte> source, ref int offset, NbtAccounter accounter)
    {
        accounter.AccountBytes(24);
        int length = ReadArrayLength(source, ref offset);
        accounter.AccountBytes(8, length);
        EnsureAvailableWide(source, offset, (long)length * 8);
        var data = new long[length];
        for (int i = 0; i < length; i++)
        {
            data[i] = BinaryPrimitives.ReadInt64BigEndian(source.Slice(offset, 8));
            offset += 8;
        }

        return new NbtLongArray(data);
    }

    private static NbtString ReadString(ReadOnlySpan<byte> source, ref int offset, NbtAccounter accounter)
    {
        accounter.AccountBytes(36);
        string value = ModifiedUtf8.Read(source, ref offset);
        accounter.AccountBytes(2, value.Length);
        return value.Length == 0 ? NbtString.Empty : new NbtString(value);
    }

    private static NbtList ReadList(ReadOnlySpan<byte> source, ref int offset, NbtAccounter accounter)
    {
        accounter.PushDepth();
        try
        {
            accounter.AccountBytes(37);
            byte elementTypeId = ReadByte(source, ref offset);
            int count = ReadInt(source, ref offset);
            if (elementTypeId == (byte)NbtTagType.End && count > 0)
                throw new NbtFormatException("Missing type on ListTag");

            if (count < 0)
                throw new NbtFormatException($"Negative NBT list length {count}");

            NbtTagType elementType = ValidateType(elementTypeId);
            accounter.AccountBytes(4, count);
            var items = new List<NbtTag>(Math.Min(count, 1024));
            for (int i = 0; i < count; i++)
            {
                NbtTag element = ReadBody(source, ref offset, elementType, accounter);

                // A compound-typed list may be the heterogeneous wire form, where every element that is not naturally a compound is wrapped as {"": value}. Unwrap that form on read. Without this a mixed list decodes as compounds keyed on the empty string instead of the values it actually carries, which is silent corruption rather than a visible failure.
                items.Add(elementType == NbtTagType.Compound ? TryUnwrap(element) : element);
            }

            return new NbtList(items, elementType);
        }
        finally
        {
            accounter.PopDepth();
        }
    }

    private static NbtCompound ReadCompound(ReadOnlySpan<byte> source, ref int offset, NbtAccounter accounter)
    {
        accounter.PushDepth();
        try
        {
            accounter.AccountBytes(48);
            var compound = new NbtCompound();
            while (true)
            {
                byte typeId = ReadByte(source, ref offset);
                if (typeId == (byte)NbtTagType.End)
                    break;

                NbtTagType type = ValidateType(typeId);
                accounter.AccountBytes(28);
                string name = ModifiedUtf8.Read(source, ref offset);
                accounter.AccountBytes(2, name.Length);
                NbtTag value = ReadBody(source, ref offset, type, accounter);
                if (!compound.ContainsKey(name))
                    accounter.AccountBytes(36);

                compound.Put(name, value);
            }

            return compound;
        }
        finally
        {
            accounter.PopDepth();
        }
    }

    private static int ReadArrayLength(ReadOnlySpan<byte> source, ref int offset)
    {
        int length = ReadInt(source, ref offset);
        if (length < 0)
            throw new NbtFormatException($"Negative NBT array length {length}");

        return length;
    }

    // A compound with exactly one empty-string key is the heterogeneous-list wrapper.
    private static NbtTag TryUnwrap(NbtTag element)
        => element is NbtCompound compound
            && compound.Count == 1
            && compound.TryGet(string.Empty, out NbtTag? inner)
                ? inner
                : element;

    private static NbtTagType ValidateType(byte typeId)
    {
        if (typeId > (byte)NbtTagType.LongArray)
            throw new NbtFormatException($"Unknown NBT tag type {typeId}");

        return (NbtTagType)typeId;
    }

    private static void EnsureAvailable(ReadOnlySpan<byte> source, int offset, int count)
    {
        if (count < 0 || offset + count > source.Length)
            throw new NbtFormatException(
                $"Truncated NBT: need {count} bytes at offset {offset}, {source.Length - offset} available");

    }

    private static void EnsureAvailableWide(ReadOnlySpan<byte> source, int offset, long count)
    {
        if (count < 0 || offset + count > source.Length)
            throw new NbtFormatException(
                $"Truncated NBT: need {count} bytes at offset {offset}, {source.Length - offset} available");

    }

    private static byte ReadByte(ReadOnlySpan<byte> source, ref int offset)
    {
        if (offset >= source.Length)
            throw new NbtFormatException("Truncated NBT: expected a byte");

        return source[offset++];
    }

    private static short ReadShort(ReadOnlySpan<byte> source, ref int offset)
    {
        EnsureAvailable(source, offset, 2);
        short value = BinaryPrimitives.ReadInt16BigEndian(source.Slice(offset, 2));
        offset += 2;
        return value;
    }

    private static int ReadInt(ReadOnlySpan<byte> source, ref int offset)
    {
        EnsureAvailable(source, offset, 4);
        int value = BinaryPrimitives.ReadInt32BigEndian(source.Slice(offset, 4));
        offset += 4;
        return value;
    }

    private static long ReadLong(ReadOnlySpan<byte> source, ref int offset)
    {
        EnsureAvailable(source, offset, 8);
        long value = BinaryPrimitives.ReadInt64BigEndian(source.Slice(offset, 8));
        offset += 8;
        return value;
    }
}
