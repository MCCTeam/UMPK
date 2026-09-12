using System.Buffers;
using System.Buffers.Binary;

namespace Umpk.Nbt;

/// <summary>Encodes NBT to an <see cref="IBufferWriter{Byte}"/> in big-endian using the three supported root framings. A decode followed by an encode of the same flavor is byte-identical to the input, which is why <see cref="NbtCompound"/> preserves order.</summary>
public static class NbtWriter
{
    /// <summary>Writes <paramref name="tag"/> as a root using the given flavor.</summary>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="NbtFormatException">The tag cannot be encoded in the chosen flavor.</exception>
    public static void Write(IBufferWriter<byte> output, NbtTag tag, NbtWireFormat format)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(tag);

        switch (format)
        {
            case NbtWireFormat.JavaNamedRoot:
                if (tag is NbtEnd)
                {
                    // Disk/legacy-network form writes a bare End byte for the null marker.
                    WriteByte(output, (byte)NbtTagType.End);
                    return;
                }

                if (tag is not NbtCompound)
                    throw new NbtFormatException("Named-root NBT must have a compound root tag");

                WriteByte(output, (byte)tag.Type);
                ModifiedUtf8.Write(output, string.Empty);
                WriteBody(output, tag);
                return;

            case NbtWireFormat.JavaUnnamedRoot:
            case NbtWireFormat.JavaRootTagOrString:
                WriteByte(output, (byte)tag.Type);
                if (tag is not NbtEnd)
                    WriteBody(output, tag);

                return;

            default:
                throw new NbtFormatException($"Unknown NBT wire format {format}");
        }
    }

    /// <summary>Encodes <paramref name="tag"/> to a new byte array using the given flavor.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="tag"/> is null.</exception>
    /// <exception cref="NbtFormatException">The tag cannot be encoded in the chosen flavor.</exception>
    public static byte[] ToArray(NbtTag tag, NbtWireFormat format)
    {
        ArgumentNullException.ThrowIfNull(tag);
        var buffer = new ArrayBufferWriter<byte>();
        Write(buffer, tag, format);
        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteBody(IBufferWriter<byte> output, NbtTag tag)
    {
        switch (tag)
        {
            case NbtEnd:
                break;

            case NbtByte b:
                WriteByte(output, unchecked((byte)b.Value));
                break;

            case NbtShort s:
                WriteShort(output, s.Value);
                break;

            case NbtInt i:
                WriteInt(output, i.Value);
                break;

            case NbtLong l:
                WriteLong(output, l.Value);
                break;

            case NbtFloat f:
                WriteInt(output, BitConverter.SingleToInt32Bits(f.Value));
                break;

            case NbtDouble d:
                WriteLong(output, BitConverter.DoubleToInt64Bits(d.Value));
                break;

            case NbtByteArray ba:
                WriteInt(output, ba.Value.Length);
                WriteByteArrayBody(output, ba.Value);
                break;

            case NbtString str:
                ModifiedUtf8.Write(output, str.Value);
                break;

            case NbtList list:
                WriteList(output, list);
                break;

            case NbtCompound compound:
                WriteCompound(output, compound);
                break;

            case NbtIntArray ia:
                WriteInt(output, ia.Value.Length);
                foreach (int v in ia.Value)
                    WriteInt(output, v);

                break;

            case NbtLongArray la:
                WriteInt(output, la.Value.Length);
                foreach (long v in la.Value)
                    WriteLong(output, v);

                break;

            default:
                throw new NbtFormatException($"Cannot encode NBT tag of runtime type {tag.GetType().Name}");
        }
    }

    /// <summary>Writes a list. When the contents are mixed, <see cref="NbtList.ElementType"/> reports Compound and every element that is not already a plain compound is wrapped as <c>{"": value}</c>, which is the heterogeneous-list convention used by component NBT. The byte representation is stable across supported component eras, so this is not era-gated.</summary>
    private static void WriteList(IBufferWriter<byte> output, NbtList list)
    {
        NbtTagType elementType = list.ElementType;
        WriteByte(output, (byte)elementType);
        WriteInt(output, list.Count);

        bool wrap = elementType == NbtTagType.Compound;
        foreach (NbtTag element in list)
            WriteBody(output, wrap ? WrapIfNeeded(element) : element);

    }

    // A compound passes through unless it already looks like a wrapper. Wrapper-shaped compounds are wrapped again so the reader returns the original compound rather than its contents.
    private static NbtTag WrapIfNeeded(NbtTag element)
    {
        if (element is NbtCompound compound && !IsWrapper(compound))
            return compound;

        var wrapper = new NbtCompound();
        wrapper.Put(string.Empty, element);
        return wrapper;
    }

    private static bool IsWrapper(NbtCompound compound)
        => compound.Count == 1 && compound.ContainsKey(string.Empty);

    private static void WriteCompound(IBufferWriter<byte> output, NbtCompound compound)
    {
        foreach (KeyValuePair<string, NbtTag> member in compound)
        {
            WriteByte(output, (byte)member.Value.Type);
            ModifiedUtf8.Write(output, member.Key);
            WriteBody(output, member.Value);
        }

        WriteByte(output, (byte)NbtTagType.End);
    }

    private static void WriteByteArrayBody(IBufferWriter<byte> output, sbyte[] data)
    {
        Span<byte> span = output.GetSpan(data.Length);
        for (int i = 0; i < data.Length; i++)
            span[i] = unchecked((byte)data[i]);

        output.Advance(data.Length);
    }

    private static void WriteByte(IBufferWriter<byte> output, byte value)
    {
        Span<byte> span = output.GetSpan(1);
        span[0] = value;
        output.Advance(1);
    }

    private static void WriteShort(IBufferWriter<byte> output, short value)
    {
        Span<byte> span = output.GetSpan(2);
        BinaryPrimitives.WriteInt16BigEndian(span, value);
        output.Advance(2);
    }

    private static void WriteInt(IBufferWriter<byte> output, int value)
    {
        Span<byte> span = output.GetSpan(4);
        BinaryPrimitives.WriteInt32BigEndian(span, value);
        output.Advance(4);
    }

    private static void WriteLong(IBufferWriter<byte> output, long value)
    {
        Span<byte> span = output.GetSpan(8);
        BinaryPrimitives.WriteInt64BigEndian(span, value);
        output.Advance(8);
    }
}
