using System.Buffers;
using System.Text;
using Umpk.Nbt;

namespace Umpk.Protocol.Bedrock;

internal enum BedrockNbtWireFormat
{
    NetworkLittleVarInt = 0,
    LittleEndian = 1,
}

internal static class BedrockNbtCodec
{
    private static readonly UTF8Encoding s_utf8 = new(false, true);

    internal static byte[] Encode(NbtTag tag, BedrockNbtWireFormat format)
    {
        ArgumentNullException.ThrowIfNull(tag);
        ValidateFormat(format);
        var output = new ArrayBufferWriter<byte>();
        var writer = new BedrockWriter(output);
        writer.WriteByte((byte)tag.Type);
        if (tag is not NbtEnd)
        {
            WriteNbtString(writer, string.Empty, format);
            WriteBody(writer, tag, format);
        }

        return output.WrittenSpan.ToArray();
    }

    internal static NbtTag Decode(ReadOnlySpan<byte> source, BedrockNbtWireFormat format, BedrockCodecLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ValidateFormat(format);
        limits.Validate();
        var reader = new BedrockReader(source, limits);
        NbtTag result = DecodePrefix(ref reader, format, limits);
        if (!reader.End)
            throw new BedrockCodecException($"The NBT root has {reader.Remaining} trailing bytes.");

        return result;
    }

    internal static NbtTag DecodePrefix(ref BedrockReader reader, BedrockNbtWireFormat format, BedrockCodecLimits limits)
    {
        ValidateFormat(format);
        var accounter = new NbtAccounter(limits.MaxByteArrayBytes, limits.MaxNbtDepth);
        NbtTagType type = ReadType(ref reader);
        if (type == NbtTagType.End)
            return NbtEnd.Instance;

        _ = ReadNbtString(ref reader, format, limits);
        return ReadBody(ref reader, type, format, limits, accounter);
    }

    internal static IReadOnlyList<NbtTag> DecodeSequence(ReadOnlySpan<byte> source, BedrockNbtWireFormat format, BedrockCodecLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        var reader = new BedrockReader(source, limits);
        var values = new List<NbtTag>();
        while (!reader.End)
        {
            if (values.Count >= limits.MaxCollectionElements)
                throw new BedrockCodecException("The NBT sequence exceeds its element limit.");

            values.Add(DecodePrefix(ref reader, format, limits));
        }

        return values;
    }

    private static NbtTag ReadBody(
        ref BedrockReader reader,
        NbtTagType type,
        BedrockNbtWireFormat format,
        BedrockCodecLimits limits,
        NbtAccounter accounter)
    {
        accounter.AccountBytes(8);
        return type switch
        {
            NbtTagType.Byte => new NbtByte(unchecked((sbyte)reader.ReadByte())),
            NbtTagType.Short => new NbtShort(reader.ReadInt16LittleEndian()),
            NbtTagType.Int => new NbtInt(ReadNbtInt(ref reader, format)),
            NbtTagType.Long => new NbtLong(ReadNbtLong(ref reader, format)),
            NbtTagType.Float => new NbtFloat(reader.ReadSingleLittleEndian()),
            NbtTagType.Double => new NbtDouble(reader.ReadDoubleLittleEndian()),
            NbtTagType.ByteArray => ReadByteArray(ref reader, format, limits, accounter),
            NbtTagType.String => new NbtString(ReadNbtString(ref reader, format, limits)),
            NbtTagType.List => ReadList(ref reader, format, limits, accounter),
            NbtTagType.Compound => ReadCompound(ref reader, format, limits, accounter),
            NbtTagType.IntArray => ReadIntArray(ref reader, format, limits, accounter),
            NbtTagType.LongArray => ReadLongArray(ref reader, format, limits, accounter),
            _ => throw new BedrockCodecException($"NBT tag type {type} has no body."),
        };
    }

    private static NbtByteArray ReadByteArray(ref BedrockReader reader, BedrockNbtWireFormat format, BedrockCodecLimits limits, NbtAccounter accounter)
    {
        int count = ReadLength(ref reader, format, limits.MaxCollectionElements, "NBT byte array");
        accounter.AccountBytes(1, count);
        ReadOnlySpan<byte> bytes = reader.ReadExact(count);
        var values = new sbyte[count];
        for (int index = 0; index < values.Length; index++)
            values[index] = unchecked((sbyte)bytes[index]);

        return new NbtByteArray(values);
    }

    private static NbtIntArray ReadIntArray(ref BedrockReader reader, BedrockNbtWireFormat format, BedrockCodecLimits limits, NbtAccounter accounter)
    {
        int count = ReadLength(ref reader, format, limits.MaxCollectionElements, "NBT int array");
        accounter.AccountBytes(4, count);
        var values = new int[count];
        for (int index = 0; index < values.Length; index++)
            values[index] = ReadNbtInt(ref reader, format);

        return new NbtIntArray(values);
    }

    private static NbtLongArray ReadLongArray(ref BedrockReader reader, BedrockNbtWireFormat format, BedrockCodecLimits limits, NbtAccounter accounter)
    {
        int count = ReadLength(ref reader, format, limits.MaxCollectionElements, "NBT long array");
        accounter.AccountBytes(8, count);
        var values = new long[count];
        for (int index = 0; index < values.Length; index++)
            values[index] = ReadNbtLong(ref reader, format);

        return new NbtLongArray(values);
    }

    private static NbtList ReadList(ref BedrockReader reader, BedrockNbtWireFormat format, BedrockCodecLimits limits, NbtAccounter accounter)
    {
        accounter.PushDepth();
        try
        {
            NbtTagType elementType = ReadType(ref reader);
            int count = ReadLength(ref reader, format, limits.MaxCollectionElements, "NBT list");
            if (elementType == NbtTagType.End && count != 0)
                throw new BedrockCodecException("A nonempty NBT list cannot use the End element type.");

            accounter.AccountBytes(8, count);
            var list = new NbtList(elementType);
            for (int index = 0; index < count; index++)
            {
                NbtTag element = ReadBody(ref reader, elementType, format, limits, accounter);
                list.Add(elementType == NbtTagType.Compound ? TryUnwrap(element) : element);
            }

            return list;
        }
        finally
        {
            accounter.PopDepth();
        }
    }

    private static NbtCompound ReadCompound(ref BedrockReader reader, BedrockNbtWireFormat format, BedrockCodecLimits limits, NbtAccounter accounter)
    {
        accounter.PushDepth();
        try
        {
            var compound = new NbtCompound();
            while (true)
            {
                NbtTagType type = ReadType(ref reader);
                if (type == NbtTagType.End)
                    return compound;

                if (compound.Count >= limits.MaxCollectionElements)
                    throw new BedrockCodecException("The NBT compound exceeds its member limit.");

                string name = ReadNbtString(ref reader, format, limits);
                accounter.AccountBytes(36 + (name.Length * 2L));
                compound.Put(name, ReadBody(ref reader, type, format, limits, accounter));
            }
        }
        finally
        {
            accounter.PopDepth();
        }
    }

    private static void WriteBody(BedrockWriter writer, NbtTag tag, BedrockNbtWireFormat format)
    {
        switch (tag)
        {
            case NbtEnd:
                return;
            case NbtByte value:
                writer.WriteByte(unchecked((byte)value.Value));
                return;
            case NbtShort value:
                writer.WriteInt16LittleEndian(value.Value);
                return;
            case NbtInt value:
                WriteNbtInt(writer, value.Value, format);
                return;
            case NbtLong value:
                WriteNbtLong(writer, value.Value, format);
                return;
            case NbtFloat value:
                writer.WriteSingleLittleEndian(value.Value);
                return;
            case NbtDouble value:
                writer.WriteDoubleLittleEndian(value.Value);
                return;
            case NbtByteArray value:
                WriteLength(writer, value.Value.Length, format);
                foreach (sbyte item in value.Value)
                    writer.WriteByte(unchecked((byte)item));
                return;
            case NbtString value:
                WriteNbtString(writer, value.Value, format);
                return;
            case NbtList value:
                writer.WriteByte((byte)value.ElementType);
                WriteLength(writer, value.Count, format);
                foreach (NbtTag item in value)
                    WriteBody(writer, value.ElementType == NbtTagType.Compound ? WrapIfNeeded(item) : item, format);
                return;
            case NbtCompound value:
                foreach (KeyValuePair<string, NbtTag> member in value)
                {
                    writer.WriteByte((byte)member.Value.Type);
                    WriteNbtString(writer, member.Key, format);
                    WriteBody(writer, member.Value, format);
                }

                writer.WriteByte((byte)NbtTagType.End);
                return;
            case NbtIntArray value:
                WriteLength(writer, value.Value.Length, format);
                foreach (int item in value.Value)
                    WriteNbtInt(writer, item, format);
                return;
            case NbtLongArray value:
                WriteLength(writer, value.Value.Length, format);
                foreach (long item in value.Value)
                    WriteNbtLong(writer, item, format);
                return;
            default:
                throw new BedrockCodecException($"Cannot encode NBT runtime type {tag.GetType().Name}.");
        }
    }

    private static string ReadNbtString(ref BedrockReader reader, BedrockNbtWireFormat format, BedrockCodecLimits limits)
    {
        int length = format switch
        {
            BedrockNbtWireFormat.NetworkLittleVarInt => checked((int)reader.ReadUInt32VarInt()),
            BedrockNbtWireFormat.LittleEndian => reader.ReadUInt16LittleEndian(),
            _ => throw new BedrockCodecException($"Unknown Bedrock NBT format {format}."),
        };
        if (length > limits.MaxStringBytes)
            throw new BedrockCodecException("The NBT string exceeds its byte limit.");

        try
        {
            return s_utf8.GetString(reader.ReadExact(length));
        }
        catch (DecoderFallbackException exception)
        {
            throw new BedrockCodecException("The NBT string contains invalid UTF-8.", exception);
        }
    }

    private static void WriteNbtString(BedrockWriter writer, string value, BedrockNbtWireFormat format)
    {
        byte[] bytes = s_utf8.GetBytes(value);
        if (format == BedrockNbtWireFormat.NetworkLittleVarInt)
            writer.WriteUInt32VarInt(checked((uint)bytes.Length));
        else
        {
            if (bytes.Length > ushort.MaxValue)
                throw new BedrockCodecException("The NBT string exceeds UInt16.MaxValue bytes.");

            writer.WriteUInt16LittleEndian((ushort)bytes.Length);
        }

        writer.WriteBytes(bytes);
    }

    private static int ReadLength(ref BedrockReader reader, BedrockNbtWireFormat format, int maximum, string kind)
    {
        int length = ReadNbtInt(ref reader, format);
        if (length < 0 || length > maximum)
            throw new BedrockCodecException($"Invalid {kind} length {length}.");

        return length;
    }

    private static void WriteLength(BedrockWriter writer, int length, BedrockNbtWireFormat format) => WriteNbtInt(writer, length, format);

    private static int ReadNbtInt(ref BedrockReader reader, BedrockNbtWireFormat format) =>
        format == BedrockNbtWireFormat.NetworkLittleVarInt ? reader.ReadZigZag32() : reader.ReadInt32LittleEndian();

    private static long ReadNbtLong(ref BedrockReader reader, BedrockNbtWireFormat format) =>
        format == BedrockNbtWireFormat.NetworkLittleVarInt ? reader.ReadZigZag64() : reader.ReadInt64LittleEndian();

    private static void WriteNbtInt(BedrockWriter writer, int value, BedrockNbtWireFormat format)
    {
        if (format == BedrockNbtWireFormat.NetworkLittleVarInt)
            writer.WriteZigZag32(value);
        else
            writer.WriteInt32LittleEndian(value);
    }

    private static void WriteNbtLong(BedrockWriter writer, long value, BedrockNbtWireFormat format)
    {
        if (format == BedrockNbtWireFormat.NetworkLittleVarInt)
            writer.WriteZigZag64(value);
        else
            writer.WriteInt64LittleEndian(value);
    }

    private static NbtTagType ReadType(ref BedrockReader reader)
    {
        byte value = reader.ReadByte();
        if (value > (byte)NbtTagType.LongArray)
            throw new BedrockCodecException($"Unknown NBT tag type {value}.");

        return (NbtTagType)value;
    }

    private static void ValidateFormat(BedrockNbtWireFormat format)
    {
        if (format is not (BedrockNbtWireFormat.NetworkLittleVarInt or BedrockNbtWireFormat.LittleEndian))
            throw new ArgumentOutOfRangeException(nameof(format));
    }

    private static NbtTag WrapIfNeeded(NbtTag element)
    {
        if (element is NbtCompound compound && !IsWrapper(compound))
            return compound;

        var wrapper = new NbtCompound();
        wrapper.Put(string.Empty, element);
        return wrapper;
    }

    private static NbtTag TryUnwrap(NbtTag element) =>
        element is NbtCompound compound
        && IsWrapper(compound)
        && compound.TryGet(string.Empty, out NbtTag? inner)
            ? inner
            : element;

    private static bool IsWrapper(NbtCompound compound) => compound.Count == 1 && compound.ContainsKey(string.Empty);
}
