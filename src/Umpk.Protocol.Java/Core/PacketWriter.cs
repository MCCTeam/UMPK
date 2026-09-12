using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Text;
using Umpk.Text.Serialization;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>A forward-only writer over an <see cref="IBufferWriter{T}"/> that mirrors <see cref="PacketReader"/>. A <see langword="ref struct"/>: no intermediate byte-array builders. Version-sensitive primitives take the variation as an explicit parameter, exactly as the reader does.</summary>
public ref struct PacketWriter
{
    private readonly IBufferWriter<byte> _output;

    /// <summary>Wraps a buffer writer that receives the encoded bytes.</summary>
    public PacketWriter(IBufferWriter<byte> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
    }

    /// <summary>Writes a single byte.</summary>
    public readonly void WriteByte(byte value)
    {
        Span<byte> span = _output.GetSpan(1);
        span[0] = value;
        _output.Advance(1);
    }

    /// <summary>Writes a signed byte.</summary>
    public readonly void WriteSByte(sbyte value) => WriteByte(unchecked((byte)value));

    /// <summary>Writes a boolean as one byte.</summary>
    public readonly void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

    /// <summary>Writes a big-endian signed 16-bit integer.</summary>
    public readonly void WriteShort(short value)
    {
        Span<byte> span = _output.GetSpan(2);
        BinaryPrimitives.WriteInt16BigEndian(span, value);
        _output.Advance(2);
    }

    /// <summary>Writes a big-endian unsigned 16-bit integer.</summary>
    public readonly void WriteUShort(ushort value)
    {
        Span<byte> span = _output.GetSpan(2);
        BinaryPrimitives.WriteUInt16BigEndian(span, value);
        _output.Advance(2);
    }

    /// <summary>Writes a big-endian signed 32-bit integer.</summary>
    public readonly void WriteInt(int value)
    {
        Span<byte> span = _output.GetSpan(4);
        BinaryPrimitives.WriteInt32BigEndian(span, value);
        _output.Advance(4);
    }

    /// <summary>Writes a big-endian signed 64-bit integer.</summary>
    public readonly void WriteLong(long value)
    {
        Span<byte> span = _output.GetSpan(8);
        BinaryPrimitives.WriteInt64BigEndian(span, value);
        _output.Advance(8);
    }

    /// <summary>Writes a big-endian 32-bit float.</summary>
    public readonly void WriteFloat(float value)
    {
        Span<byte> span = _output.GetSpan(4);
        BinaryPrimitives.WriteSingleBigEndian(span, value);
        _output.Advance(4);
    }

    /// <summary>Writes a big-endian 64-bit double.</summary>
    public readonly void WriteDouble(double value)
    {
        Span<byte> span = _output.GetSpan(8);
        BinaryPrimitives.WriteDoubleBigEndian(span, value);
        _output.Advance(8);
    }

    /// <summary>Writes a variable-length signed 32-bit integer (Minecraft VarInt).</summary>
    public readonly void WriteVarInt(int value)
    {
        Span<byte> span = _output.GetSpan(5);
        uint v = (uint)value;
        int i = 0;
        while ((v & 0xFFFFFF80u) != 0)
        {
            span[i++] = (byte)(v | 0x80);
            v >>= 7;
        }

        span[i++] = (byte)v;
        _output.Advance(i);
    }

    /// <summary>Writes a variable-length signed 64-bit integer (Minecraft VarLong).</summary>
    public readonly void WriteVarLong(long value)
    {
        Span<byte> span = _output.GetSpan(10);
        ulong v = (ulong)value;
        int i = 0;
        while ((v & 0xFFFFFFFFFFFFFF80u) != 0)
        {
            span[i++] = (byte)(v | 0x80);
            v >>= 7;
        }

        span[i++] = (byte)v;
        _output.Advance(i);
    }

    /// <summary>Writes a length-prefixed UTF-8 string, enforcing a character cap.</summary>
    public readonly void WriteString(string value, int maxLength = 32767)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > maxLength)
            throw new ProtocolViolationException($"String length {value.Length} exceeds the cap of {maxLength} chars.");

        int byteCount = Encoding.UTF8.GetByteCount(value);
        WriteVarInt(byteCount);
        Span<byte> span = _output.GetSpan(byteCount);
        Encoding.UTF8.GetBytes(value, span);
        _output.Advance(byteCount);
    }

    /// <summary>Writes a 16-byte big-endian UUID (the modern raw form).</summary>
    public readonly void WriteUuid(Guid value)
    {
        (long most, long least) = UuidCodec.ToMostLeast(value);
        WriteLong(most);
        WriteLong(least);
    }

    /// <summary>Writes a degrees angle as an unsigned byte (0..256 covering 0..360).</summary>
    public readonly void WriteAngle(float degrees) => WriteByte((byte)((int)Math.Round(degrees * 256.0f / 360.0f) & 0xFF));

    /// <summary>Writes a packed <see cref="BlockPos"/> using the given era layout.</summary>
    public readonly void WriteBlockPos(BlockPos pos, BlockPosLayout layout)
    {
        long value = layout switch
        {
            BlockPosLayout.Packed114 =>
                ((long)(pos.X & 0x3FFFFFF) << 38) | ((long)(pos.Z & 0x3FFFFFF) << 12) | (pos.Y & 0xFFFL),
            BlockPosLayout.PrePacked114 =>
                ((long)(pos.X & 0x3FFFFFF) << 38) | ((long)(pos.Y & 0xFFF) << 26) | (pos.Z & 0x3FFFFFFL),
            _ => throw new ArgumentOutOfRangeException(nameof(layout)),
        };
        WriteLong(value);
    }

    /// <summary>Writes an NBT tag with the given root framing.</summary>
    public readonly void WriteNbt(NbtTag tag, NbtWireFormat format)
    {
        ArgumentNullException.ThrowIfNull(tag);
        NbtWriter.Write(_output, tag, format);
    }

    /// <summary>Writes a text component as network NBT.</summary>
    public readonly void WriteComponent(Component component, ComponentWireEra era, NbtWireFormat format)
    {
        ArgumentNullException.ThrowIfNull(component);
        NbtTag tag = ComponentNbt.To(component, era);
        WriteNbt(tag, format);
    }

    /// <summary>Writes raw bytes verbatim.</summary>
    public readonly void WriteBytes(ReadOnlySpan<byte> bytes)
    {
        Span<byte> span = _output.GetSpan(bytes.Length);
        bytes.CopyTo(span);
        _output.Advance(bytes.Length);
    }

    /// <summary>Writes a VarInt-prefixed byte array.</summary>
    public readonly void WriteByteArray(ReadOnlySpan<byte> bytes)
    {
        WriteVarInt(bytes.Length);
        WriteBytes(bytes);
    }

    /// <summary>Writes a presence flag then the value when it is non-null.</summary>
    public void WriteOptional<T>(T? value, WriterAction<T> write) where T : class
    {
        ArgumentNullException.ThrowIfNull(write);
        if (value is null)
        {
            WriteBool(false);
            return;
        }

        WriteBool(true);
        write(ref this, value);
    }

    /// <summary>Writes a presence flag then the value when the nullable has one.</summary>
    public void WriteOptionalStruct<T>(T? value, WriterAction<T> write) where T : struct
    {
        ArgumentNullException.ThrowIfNull(write);
        if (value is not { } present)
        {
            WriteBool(false);
            return;
        }

        WriteBool(true);
        write(ref this, present);
    }

    /// <summary>Writes a VarInt-prefixed list, invoking <paramref name="write"/> per element.</summary>
    public void WriteList<T>(IReadOnlyList<T> values, WriterAction<T> write)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(write);
        WriteVarInt(values.Count);
        for (int i = 0; i < values.Count; i++)
            write(ref this, values[i]);

    }
}

/// <summary>A per-element write delegate usable with the ref-struct <see cref="PacketWriter"/>.</summary>
public delegate void WriterAction<in T>(ref PacketWriter writer, T value);
