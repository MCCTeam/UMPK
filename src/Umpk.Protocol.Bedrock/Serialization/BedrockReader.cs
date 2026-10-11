using System.Buffers.Binary;
using System.Text;

namespace Umpk.Protocol.Bedrock;

internal ref struct BedrockReader
{
    private static readonly UTF8Encoding s_utf8 = new(false, true);
    private readonly ReadOnlySpan<byte> _source;
    private readonly BedrockCodecLimits _limits;
    private int _offset;

    internal BedrockReader(ReadOnlySpan<byte> source, BedrockCodecLimits? limits = null)
    {
        _source = source;
        _limits = limits ?? BedrockCodecLimits.Default;
        _limits.Validate();
        _offset = 0;
    }

    internal bool End => _offset == _source.Length;

    internal int Remaining => _source.Length - _offset;

    internal int Consumed => _offset;

    internal byte Peek()
    {
        EnsureAvailable(1);
        return _source[_offset];
    }

    internal byte ReadByte()
    {
        EnsureAvailable(1);
        return _source[_offset++];
    }

    internal bool ReadBoolean() => ReadByte() switch
    {
        0 => false,
        1 => true,
        byte value => throw new BedrockCodecException($"Invalid Boolean value {value}.")
    };

    internal short ReadInt16LittleEndian()
    {
        ReadOnlySpan<byte> bytes = ReadExact(2);
        return BinaryPrimitives.ReadInt16LittleEndian(bytes);
    }

    internal ushort ReadUInt16LittleEndian()
    {
        ReadOnlySpan<byte> bytes = ReadExact(2);
        return BinaryPrimitives.ReadUInt16LittleEndian(bytes);
    }

    internal int ReadInt32LittleEndian() => BinaryPrimitives.ReadInt32LittleEndian(ReadExact(4));

    internal uint ReadUInt32LittleEndian() => BinaryPrimitives.ReadUInt32LittleEndian(ReadExact(4));

    internal long ReadInt64LittleEndian() => BinaryPrimitives.ReadInt64LittleEndian(ReadExact(8));

    internal ulong ReadUInt64LittleEndian() => BinaryPrimitives.ReadUInt64LittleEndian(ReadExact(8));

    internal int ReadInt32BigEndian() => BinaryPrimitives.ReadInt32BigEndian(ReadExact(4));

    internal float ReadSingleLittleEndian() => BitConverter.Int32BitsToSingle(ReadInt32LittleEndian());

    internal double ReadDoubleLittleEndian() => BitConverter.Int64BitsToDouble(ReadInt64LittleEndian());

    internal uint ReadUInt32VarInt()
    {
        ulong value = ReadUnsignedVarInt(5, uint.MaxValue, out int byteCount);
        if (byteCount != GetUnsignedVarIntLength(value))
            throw new BedrockCodecException("The UInt32 VarInt is not canonical.");

        return (uint)value;
    }

    internal ulong ReadUInt64VarInt()
    {
        ulong value = ReadUnsignedVarInt(10, ulong.MaxValue, out int byteCount);
        if (byteCount != GetUnsignedVarIntLength(value))
            throw new BedrockCodecException("The UInt64 VarInt is not canonical.");

        return value;
    }

    internal int ReadZigZag32()
    {
        uint encoded = ReadUInt32VarInt();
        return unchecked((int)((encoded >> 1) ^ (uint)-(int)(encoded & 1)));
    }

    internal long ReadZigZag64()
    {
        ulong encoded = ReadUInt64VarInt();
        return unchecked((long)((encoded >> 1) ^ (ulong)-(long)(encoded & 1)));
    }

    internal string ReadString() => ReadUtf8(ReadLength(ReadUInt32VarInt(), _limits.MaxStringBytes, "string"));

    internal string ReadLittleString()
    {
        int length = ReadInt32LittleEndian();
        if (length < 0 || length > _limits.MaxStringBytes)
            throw new BedrockCodecException($"Invalid little string length {length}.");

        return ReadUtf8(length);
    }

    internal string ReadShortString()
    {
        short length = ReadInt16LittleEndian();
        if (length < 0 || length > _limits.MaxStringBytes)
            throw new BedrockCodecException($"Invalid short string length {length}.");

        return ReadUtf8(length);
    }

    internal byte[] ReadByteArray() => ReadExact(ReadLength(ReadUInt32VarInt(), _limits.MaxByteArrayBytes, "byte array")).ToArray();

    internal ReadOnlySpan<byte> ReadExact(int count)
    {
        EnsureAvailable(count);
        ReadOnlySpan<byte> result = _source.Slice(_offset, count);
        _offset += count;
        return result;
    }

    internal BedrockLargeBitSet ReadLargeBitSet(int logicalBitCount, int encodedByteCount)
    {
        if (logicalBitCount <= 0 || encodedByteCount <= 0 || encodedByteCount * 7 < logicalBitCount)
            throw new ArgumentOutOfRangeException(nameof(logicalBitCount));

        ReadOnlySpan<byte> groups = ReadExact(encodedByteCount);
        for (int index = 0; index < groups.Length; index++)
        {
            bool expectedContinuation = index < groups.Length - 1;
            bool continuation = (groups[index] & 0x80) != 0;
            if (continuation != expectedContinuation)
                throw new BedrockCodecException("The large bit set has an invalid continuation marker.");
        }

        return BedrockLargeBitSet.FromSevenBitGroups(groups, logicalBitCount);
    }

    internal Guid ReadUuid()
    {
        ulong most = ReadUInt64LittleEndian();
        ulong least = ReadUInt64LittleEndian();
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, most);
        BinaryPrimitives.WriteUInt64BigEndian(bytes[8..], least);
        return new Guid(bytes, bigEndian: true);
    }

    private ulong ReadUnsignedVarInt(int maxBytes, ulong maxValue, out int byteCount)
    {
        ulong value = 0;
        for (int index = 0; index < maxBytes; index++)
        {
            byte current = ReadByte();
            int shift = index * 7;
            ulong payload = (ulong)(current & 0x7f);
            if (shift >= 64 || (shift == 63 && payload > 1) || (payload << shift) > maxValue - value)
                throw new BedrockCodecException("The unsigned VarInt overflows its destination type.");

            value |= payload << shift;
            if ((current & 0x80) == 0)
            {
                byteCount = index + 1;
                return value;
            }
        }

        throw new BedrockCodecException("The unsigned VarInt does not terminate within its byte limit.");
    }

    private string ReadUtf8(int length)
    {
        try
        {
            return s_utf8.GetString(ReadExact(length));
        }
        catch (DecoderFallbackException exception)
        {
            throw new BedrockCodecException("The string contains invalid UTF-8.", exception);
        }
    }

    private void EnsureAvailable(int count)
    {
        if (count < 0 || count > Remaining)
            throw new BedrockCodecException($"Truncated input at offset {_offset}: need {count} bytes, have {Remaining}.");
    }

    private static int ReadLength(uint value, int maximum, string kind)
    {
        if (value > maximum)
            throw new BedrockCodecException($"The {kind} length {value} exceeds the limit {maximum}.");

        return checked((int)value);
    }

    private static int GetUnsignedVarIntLength(ulong value)
    {
        int length = 1;
        while (value >= 0x80)
        {
            value >>= 7;
            length++;
        }

        return length;
    }
}
