using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Client.Plugins;

/// <summary>
/// Reads Minecraft's wire primitives out of a plugin-channel payload, so a consumer never hand-rolls VarInt or the packed block-position layout.
/// <para>It is a thin, allocation-light shell over <see cref="PacketReader"/>, which does all the actual decoding: this type adds exactly two things. First, it is a CLASS, so it can be held across an <c>await</c> and stored in a field, which a <see langword="ref struct"/> reader cannot be; a channel handler is handed a <see cref="ReadOnlyMemory{T}"/> and often wants to keep reading after one. Second, it carries the negotiated version's <see cref="BlockPosLayout"/>, so a consumer does not have to know that 1.14 moved the Y bits.</para>
/// <para>Every read that runs past the end of the payload throws <see cref="Umpk.Protocol.Java.ProtocolViolationException"/>, from the same guard the codecs use, and leaves the position unchanged.</para>
/// </summary>
public sealed class PluginPayloadReader
{
    private readonly ReadOnlyMemory<byte> _payload;
    private readonly BlockPosLayout _layout;
    private int _position;

    /// <summary>Wraps a payload. Prefer <see cref="ClientChannels.Read"/>, which supplies the negotiated version's <paramref name="layout"/>; this constructor is for a nested blob whose layout the caller already knows.</summary>
    public PluginPayloadReader(ReadOnlyMemory<byte> payload, BlockPosLayout layout)
    {
        _payload = payload;
        _layout = layout;
    }

    /// <summary>Bytes not yet consumed.</summary>
    public int Remaining => _payload.Length - _position;

    /// <summary>The absolute offset of the next unread byte.</summary>
    public int Position => _position;

    /// <summary>Reads one byte as a boolean (any non-zero byte is true).</summary>
    public bool ReadBool() => Read(static (ref PacketReader r) => r.ReadBool());

    /// <summary>Reads one unsigned byte.</summary>
    public byte ReadByte() => Read(static (ref PacketReader r) => r.ReadByte());

    /// <summary>Reads one signed byte.</summary>
    public sbyte ReadSByte() => Read(static (ref PacketReader r) => r.ReadSByte());

    /// <summary>Reads a big-endian signed 16-bit integer.</summary>
    public short ReadShort() => Read(static (ref PacketReader r) => r.ReadShort());

    /// <summary>Reads a big-endian unsigned 16-bit integer.</summary>
    public ushort ReadUShort() => Read(static (ref PacketReader r) => r.ReadUShort());

    /// <summary>Reads a big-endian signed 32-bit integer.</summary>
    public int ReadInt() => Read(static (ref PacketReader r) => r.ReadInt());

    /// <summary>Reads a big-endian signed 64-bit integer.</summary>
    public long ReadLong() => Read(static (ref PacketReader r) => r.ReadLong());

    /// <summary>Reads a big-endian 32-bit float.</summary>
    public float ReadFloat() => Read(static (ref PacketReader r) => r.ReadFloat());

    /// <summary>Reads a big-endian 64-bit double.</summary>
    public double ReadDouble() => Read(static (ref PacketReader r) => r.ReadDouble());

    /// <summary>Reads a VarInt.</summary>
    public int ReadVarInt() => Read(static (ref PacketReader r) => r.ReadVarInt());

    /// <summary>Reads a VarLong.</summary>
    public long ReadVarLong() => Read(static (ref PacketReader r) => r.ReadVarLong());

    /// <summary>Reads a VarInt-length-prefixed UTF-8 string.</summary>
    public string ReadString() => Read(static (ref PacketReader r) => r.ReadString());

    /// <summary>Reads a 128-bit UUID (two big-endian longs, most significant first).</summary>
    public Guid ReadUuid() => Read(static (ref PacketReader r) => r.ReadUuid());

    /// <summary>Reads a block position packed into one long, in the negotiated version's layout.</summary>
    /// <remarks>Spelled out rather than routed through <c>Read</c>: the layout would have to be captured, and a capturing lambda allocates a closure on every call while every other read here uses a <see langword="static"/> one the compiler caches.</remarks>
    public BlockPos ReadBlockPos()
    {
        var reader = new PacketReader(_payload.Span[_position..]);
        BlockPos value = reader.ReadBlockPos(_layout);
        _position += reader.Position;
        return value;
    }

    /// <summary>Reads exactly <paramref name="count"/> bytes, as a view into the payload (no copy).</summary>
    /// <exception cref="Umpk.Protocol.Java.ProtocolViolationException">Fewer than <paramref name="count"/> bytes remain.</exception>
    public ReadOnlyMemory<byte> ReadBytes(int count)
    {
        if (count < 0 || count > Remaining)
            throw new Umpk.Protocol.Java.ProtocolViolationException(
                $"Payload read ran past the end (needed {count} byte(s) at offset {_position}, {Remaining} remaining).");

        ReadOnlyMemory<byte> slice = _payload.Slice(_position, count);
        _position += count;
        return slice;
    }

    /// <summary>Reads a VarInt-length-prefixed byte array, as a view into the payload (no copy).</summary>
    public ReadOnlyMemory<byte> ReadByteArray() => ReadBytes(ReadVarInt());

    /// <summary>Reads everything left, as a view into the payload (no copy).</summary>
    public ReadOnlyMemory<byte> ReadRemaining() => ReadBytes(Remaining);

    /// <summary>Runs one primitive read against a <see cref="PacketReader"/> over the unread remainder and advances by however much it consumed. A fresh reader per call is what lets a ref-struct decoder back a class; it costs a struct on the stack and nothing on the heap, and the delegates are <see langword="static"/> so the compiler caches each one.</summary>
    private T Read<T>(ReaderFunc<T> read)
    {
        var reader = new PacketReader(_payload.Span[_position..]);
        T value = read(ref reader);
        _position += reader.Position;
        return value;
    }
}
