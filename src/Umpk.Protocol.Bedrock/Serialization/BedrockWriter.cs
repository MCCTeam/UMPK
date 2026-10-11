using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace Umpk.Protocol.Bedrock;

internal sealed class BedrockWriter
{
    private static readonly UTF8Encoding s_utf8 = new(false, true);
    private readonly IBufferWriter<byte> _output;

    internal BedrockWriter(IBufferWriter<byte> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
    }

    internal void WriteByte(byte value)
    {
        Span<byte> destination = _output.GetSpan(1);
        destination[0] = value;
        _output.Advance(1);
    }

    internal void WriteBoolean(bool value) => WriteByte(value ? (byte)1 : (byte)0);

    internal void WriteInt16LittleEndian(short value) => WriteFixed(2, span => BinaryPrimitives.WriteInt16LittleEndian(span, value));

    internal void WriteUInt16LittleEndian(ushort value) => WriteFixed(2, span => BinaryPrimitives.WriteUInt16LittleEndian(span, value));

    internal void WriteInt32LittleEndian(int value) => WriteFixed(4, span => BinaryPrimitives.WriteInt32LittleEndian(span, value));

    internal void WriteUInt32LittleEndian(uint value) => WriteFixed(4, span => BinaryPrimitives.WriteUInt32LittleEndian(span, value));

    internal void WriteInt64LittleEndian(long value) => WriteFixed(8, span => BinaryPrimitives.WriteInt64LittleEndian(span, value));

    internal void WriteUInt64LittleEndian(ulong value) => WriteFixed(8, span => BinaryPrimitives.WriteUInt64LittleEndian(span, value));

    internal void WriteInt32BigEndian(int value) => WriteFixed(4, span => BinaryPrimitives.WriteInt32BigEndian(span, value));

    internal void WriteSingleLittleEndian(float value) => WriteInt32LittleEndian(BitConverter.SingleToInt32Bits(value));

    internal void WriteDoubleLittleEndian(double value) => WriteInt64LittleEndian(BitConverter.DoubleToInt64Bits(value));

    internal void WriteUInt32VarInt(uint value) => WriteUnsignedVarInt(value);

    internal void WriteUInt64VarInt(ulong value) => WriteUnsignedVarInt(value);

    internal void WriteZigZag32(int value) => WriteUInt32VarInt(unchecked((uint)((value << 1) ^ (value >> 31))));

    internal void WriteZigZag64(long value) => WriteUInt64VarInt(unchecked((ulong)((value << 1) ^ (value >> 63))));

    internal void WriteString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        byte[] bytes = EncodeUtf8(value);
        WriteUInt32VarInt(checked((uint)bytes.Length));
        WriteBytes(bytes);
    }

    internal void WriteLittleString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        byte[] bytes = EncodeUtf8(value);
        WriteInt32LittleEndian(bytes.Length);
        WriteBytes(bytes);
    }

    internal void WriteShortString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        byte[] bytes = EncodeUtf8(value);
        if (bytes.Length > short.MaxValue)
            throw new BedrockCodecException("The short string exceeds Int16.MaxValue bytes.");

        WriteInt16LittleEndian((short)bytes.Length);
        WriteBytes(bytes);
    }

    internal void WriteByteArray(ReadOnlySpan<byte> value)
    {
        WriteUInt32VarInt(checked((uint)value.Length));
        WriteBytes(value);
    }

    internal void WriteBytes(ReadOnlySpan<byte> value)
    {
        Span<byte> destination = _output.GetSpan(value.Length);
        value.CopyTo(destination);
        _output.Advance(value.Length);
    }

    internal void WriteLargeBitSet(BedrockLargeBitSet value)
    {
        ArgumentNullException.ThrowIfNull(value);
        int encodedByteCount = (value.LogicalBitCount + 6) / 7;
        for (int index = 0; index < encodedByteCount; index++)
        {
            byte group = value.GetSevenBitGroup(index);
            if (index < encodedByteCount - 1)
                group |= 0x80;

            WriteByte(group);
        }
    }

    internal void WriteUuid(Guid value)
    {
        Span<byte> bytes = stackalloc byte[16];
        if (!value.TryWriteBytes(bytes, bigEndian: true, out int bytesWritten) || bytesWritten != 16)
            throw new InvalidOperationException("The UUID could not be encoded.");

        WriteUInt64LittleEndian(BinaryPrimitives.ReadUInt64BigEndian(bytes));
        WriteUInt64LittleEndian(BinaryPrimitives.ReadUInt64BigEndian(bytes[8..]));
    }

    private void WriteUnsignedVarInt(ulong value)
    {
        do
        {
            byte current = (byte)(value & 0x7f);
            value >>= 7;
            if (value != 0)
                current |= 0x80;

            WriteByte(current);
        }
        while (value != 0);
    }

    private void WriteFixed(int size, SpanWriter write)
    {
        Span<byte> destination = _output.GetSpan(size)[..size];
        write(destination);
        _output.Advance(size);
    }

    private static byte[] EncodeUtf8(string value)
    {
        try
        {
            return s_utf8.GetBytes(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new BedrockCodecException("The string contains invalid UTF-16.", exception);
        }
    }

    private delegate void SpanWriter(Span<byte> destination);
}
