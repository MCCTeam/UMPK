using System.Buffers.Binary;
using System.Text;
using Umpk.Geometry;
using Umpk.Nbt;
using Umpk.Protocol.Java.Transport;
using Umpk.Text;
using Umpk.Text.Serialization;

namespace Umpk.Protocol.Java.Codecs;

/// <summary>A forward-only reader over a single framed, decompressed, decrypted packet payload. A <see langword="ref struct"/> over a span: no heap queue, no per-byte enqueue. Version-sensitive primitives (block positions, NBT root framing, component era) take the variation as an explicit parameter so the primitive layer carries no protocol-version knowledge at all.</summary>
public ref struct PacketReader
{
    private readonly ReadOnlySpan<byte> _payload;

    private int _position;

    /// <summary>Wraps a payload span. The span must outlive the reader.</summary>
    public PacketReader(ReadOnlySpan<byte> payload)
    {
        _payload = payload;
        _position = 0;
    }

    /// <summary>Bytes not yet consumed.</summary>
    public readonly int Remaining => _payload.Length - _position;

    /// <summary>The absolute offset of the next unread byte.</summary>
    public readonly int Position => _position;

    private void Ensure(int count)
    {
        if (count < 0 || _position + count > _payload.Length)
            throw new ProtocolViolationException(
                $"Packet decode ran past the end of the payload (needed {count} byte(s) at offset {_position}, {Remaining} remaining).");

    }

    /// <summary>Reads a single byte.</summary>
    public byte ReadByte()
    {
        Ensure(1);
        return _payload[_position++];
    }

    /// <summary>Reads a signed byte.</summary>
    public sbyte ReadSByte() => unchecked((sbyte)ReadByte());

    /// <summary>Reads a boolean (0 = false, non-zero = true).</summary>
    public bool ReadBool() => ReadByte() != 0;

    /// <summary>Reads a big-endian signed 16-bit integer.</summary>
    public short ReadShort()
    {
        Ensure(2);
        short v = BinaryPrimitives.ReadInt16BigEndian(_payload.Slice(_position, 2));
        _position += 2;
        return v;
    }

    /// <summary>Reads a big-endian unsigned 16-bit integer.</summary>
    public ushort ReadUShort()
    {
        Ensure(2);
        ushort v = BinaryPrimitives.ReadUInt16BigEndian(_payload.Slice(_position, 2));
        _position += 2;
        return v;
    }

    /// <summary>Reads a big-endian signed 32-bit integer.</summary>
    public int ReadInt()
    {
        Ensure(4);
        int v = BinaryPrimitives.ReadInt32BigEndian(_payload.Slice(_position, 4));
        _position += 4;
        return v;
    }

    /// <summary>Reads a big-endian signed 64-bit integer.</summary>
    public long ReadLong()
    {
        Ensure(8);
        long v = BinaryPrimitives.ReadInt64BigEndian(_payload.Slice(_position, 8));
        _position += 8;
        return v;
    }

    /// <summary>Reads a big-endian 32-bit float.</summary>
    public float ReadFloat()
    {
        Ensure(4);
        float v = BinaryPrimitives.ReadSingleBigEndian(_payload.Slice(_position, 4));
        _position += 4;
        return v;
    }

    /// <summary>Reads a big-endian 64-bit double.</summary>
    public double ReadDouble()
    {
        Ensure(8);
        double v = BinaryPrimitives.ReadDoubleBigEndian(_payload.Slice(_position, 8));
        _position += 8;
        return v;
    }

    /// <summary>Reads a variable-length signed 32-bit integer (Minecraft VarInt, max 5 bytes).</summary>
    public int ReadVarInt()
    {
        VarIntStatus status = VarInt.Read(_payload[_position..], VarInt.MaxBytes, out int value, out int bytesRead);
        _position += bytesRead;
        if (status == VarIntStatus.TooLong)
            throw new ProtocolViolationException("VarInt is too long (more than 5 bytes).");

        if (status == VarIntStatus.Truncated)
        {
            // Running out mid-VarInt is the same fault, and carries the same message, as any other read past the end: the position already sits at the byte that was missing.
            Ensure(1);
        }

        return value;
    }

    /// <summary>Reads a variable-length signed 64-bit integer (Minecraft VarLong, max 10 bytes).</summary>
    /// <remarks>Deliberately not on the shared accumulator: the value is 64-bit, so both the shift schedule and the cap differ, and a body parameterised on both would be two bodies wearing one name.</remarks>
    public long ReadVarLong()
    {
        long value = 0;
        int shift = 0;
        while (true)
        {
            byte b = ReadByte();
            value |= (long)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return value;

            shift += 7;
            if (shift >= 70)
                throw new ProtocolViolationException("VarLong is too long (more than 10 bytes).");

        }
    }

    /// <summary>Reads a length-prefixed UTF-8 string (VarInt byte length), enforcing a character cap.</summary>
    public string ReadString(int maxLength = 32767)
    {
        int byteLength = ReadVarInt();
        int maxBytes = maxLength * 3;
        if (byteLength < 0 || byteLength > maxBytes)
            throw new ProtocolViolationException(
                $"String byte length {byteLength} exceeds the cap of {maxBytes} ({maxLength} chars).");

        Ensure(byteLength);
        string s = Encoding.UTF8.GetString(_payload.Slice(_position, byteLength));
        _position += byteLength;
        if (s.Length > maxLength)
            throw new ProtocolViolationException($"String length {s.Length} exceeds the cap of {maxLength} chars.");

        return s;
    }

    /// <summary>Reads a 16-byte big-endian UUID (the modern raw form).</summary>
    public Guid ReadUuid()
    {
        long hi = ReadLong();
        long lo = ReadLong();
        return UuidCodec.FromMostLeast(hi, lo);
    }

    /// <summary>Reads an unsigned-angle byte and converts it to degrees (0..360).</summary>
    public float ReadAngle() => ReadByte() * 360.0f / 256.0f;

    /// <summary>Reads a packed <see cref="BlockPos"/> using the given era layout.</summary>
    public BlockPos ReadBlockPos(BlockPosLayout layout)
    {
        long value = ReadLong();
        return layout switch
        {
            BlockPosLayout.Packed114 => new BlockPos(
                (int)(value >> 38),
                (int)(value << 52 >> 52),
                (int)(value << 26 >> 38)),
            BlockPosLayout.PrePacked114 => new BlockPos(
                (int)(value >> 38),
                (int)(value << 26 >> 52),
                (int)(value << 38 >> 38)),
            _ => throw new ArgumentOutOfRangeException(nameof(layout)),
        };
    }

    /// <summary>Reads an NBT tag with the given root framing.</summary>
    public NbtTag ReadNbt(NbtWireFormat format)
    {
        NbtTag tag = NbtReader.Read(_payload[_position..], format, NbtAccounter.Unlimited(), out int bytesRead);
        _position += bytesRead;
        return tag;
    }

    /// <summary>Reads a text component encoded as network NBT (1.20.3+ uses the root-tag-or-string form).</summary>
    /// <remarks>The decode is strict about shape and literal about strings. Whether a wire position carries a JSON string or network NBT is a property of the protocol, not of the bytes: the pre-1.20.3 codecs call <c>ComponentJson.Parse</c> on a length-prefixed string and only the 1.20.3+ codecs call this. A bare string tag therefore means a literal component, and a team prefix of <c>[Admin] </c> arrives verbatim rather than being mistaken for a JSON array.</remarks>
    public Component ReadComponent(ComponentWireEra era, NbtWireFormat format)
    {
        NbtTag tag = ReadNbt(format);
        return ComponentNbt.From(tag, era);
    }

    /// <summary>Reads exactly <paramref name="count"/> bytes as a span into the payload.</summary>
    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        Ensure(count);
        ReadOnlySpan<byte> slice = _payload.Slice(_position, count);
        _position += count;
        return slice;
    }

    /// <summary>Reads all remaining bytes as a span (byte-array remainder).</summary>
    public ReadOnlySpan<byte> ReadRemaining()
    {
        ReadOnlySpan<byte> slice = _payload[_position..];
        _position = _payload.Length;
        return slice;
    }

    /// <summary>Reads a VarInt-prefixed byte array.</summary>
    public ReadOnlySpan<byte> ReadByteArray()
    {
        int len = ReadVarInt();
        return ReadBytes(len);
    }

    /// <summary>Reads a reference-type value only when a preceding boolean flag is set; returns <see langword="null"/> for absence. Constrained to reference types so absence is always representable: for an unconstrained <c>T</c> an absent value type would collapse to <c>default(T)</c> (indistinguishable from a present zero and re-encoded as present). Value-type optionals must use <see cref="ReadOptionalStruct{T}"/>, which returns a true <see cref="Nullable{T}"/>.</summary>
    public T? ReadOptional<T>(ReaderFunc<T> read) where T : class
    {
        ArgumentNullException.ThrowIfNull(read);
        return ReadBool() ? read(ref this) : null;
    }

    /// <summary>Reads a value-type value only when a preceding boolean flag is set; returns a null <see cref="Nullable{T}"/> for absence, preserving the absent/zero distinction that <see cref="ReadOptional{T}"/> cannot for value types. Mirrors <c>PacketWriter.WriteOptionalStruct</c>.</summary>
    public T? ReadOptionalStruct<T>(ReaderFunc<T> read) where T : struct
    {
        ArgumentNullException.ThrowIfNull(read);
        return ReadBool() ? read(ref this) : null;
    }

    /// <summary>Reads a VarInt-prefixed list, invoking <paramref name="read"/> per element.</summary>
    public T[] ReadList<T>(ReaderFunc<T> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        int count = ReadVarInt();
        if (count < 0 || count > Remaining + 1)
            throw new ProtocolViolationException($"List length {count} is implausible for {Remaining} remaining bytes.");

        var result = new T[count];
        for (int i = 0; i < count; i++)
            result[i] = read(ref this);

        return result;
    }
}

/// <summary>A per-element read delegate usable with the ref-struct <see cref="PacketReader"/>.</summary>
public delegate T ReaderFunc<out T>(ref PacketReader reader);
