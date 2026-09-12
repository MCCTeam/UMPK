using System.Buffers;
using Umpk.Geometry;
using Umpk.Protocol.Java.Codecs;

namespace Umpk.Client.Plugins;

/// <summary>
/// Builds a plugin-channel payload out of Minecraft's wire primitives, so a consumer never hand-rolls VarInt or the packed block-position layout.
/// <para>A thin, fluent shell over <see cref="PacketWriter"/>, which does all the actual encoding. It exists for the same two reasons the reader does: it is a CLASS, so it can be built across an <c>await</c> and passed around, which a <see langword="ref struct"/> writer cannot be; and it carries the negotiated version's <see cref="BlockPosLayout"/>, so a caller does not have to know that 1.14 moved the Y bits.</para>
/// <para>Every method takes a fresh <see cref="PacketWriter"/> over the same buffer, which costs nothing: the writer holds only the buffer reference, and the buffer holds the position.</para>
/// <para>Hand <see cref="Written"/> straight to <see cref="ClientChannels.SendAsync"/>; it is a view over this writer's own buffer, so take <see cref="ToArray"/> instead if it has to outlive further writes.</para>
/// </summary>
public sealed class PluginPayloadWriter
{
    private readonly ArrayBufferWriter<byte> _buffer = new();
    private readonly BlockPosLayout _layout;

    /// <summary>Starts an empty payload. Prefer <see cref="ClientChannels.Write"/>, which supplies the negotiated version's <paramref name="layout"/>; this constructor is for a nested blob whose layout the caller already knows.</summary>
    public PluginPayloadWriter(BlockPosLayout layout) => _layout = layout;

    /// <summary>How many bytes have been written so far.</summary>
    public int Length => _buffer.WrittenCount;

    /// <summary>The bytes written so far, as a view over this writer's buffer.</summary>
    public ReadOnlyMemory<byte> Written => _buffer.WrittenMemory;

    /// <summary>Writes a boolean as one byte.</summary>
    public PluginPayloadWriter WriteBool(bool value)
    {
        new PacketWriter(_buffer).WriteBool(value);
        return this;
    }

    /// <summary>Writes one unsigned byte.</summary>
    public PluginPayloadWriter WriteByte(byte value)
    {
        new PacketWriter(_buffer).WriteByte(value);
        return this;
    }

    /// <summary>Writes one signed byte.</summary>
    public PluginPayloadWriter WriteSByte(sbyte value)
    {
        new PacketWriter(_buffer).WriteSByte(value);
        return this;
    }

    /// <summary>Writes a big-endian signed 16-bit integer.</summary>
    public PluginPayloadWriter WriteShort(short value)
    {
        new PacketWriter(_buffer).WriteShort(value);
        return this;
    }

    /// <summary>Writes a big-endian unsigned 16-bit integer.</summary>
    public PluginPayloadWriter WriteUShort(ushort value)
    {
        new PacketWriter(_buffer).WriteUShort(value);
        return this;
    }

    /// <summary>Writes a big-endian signed 32-bit integer.</summary>
    public PluginPayloadWriter WriteInt(int value)
    {
        new PacketWriter(_buffer).WriteInt(value);
        return this;
    }

    /// <summary>Writes a big-endian signed 64-bit integer.</summary>
    public PluginPayloadWriter WriteLong(long value)
    {
        new PacketWriter(_buffer).WriteLong(value);
        return this;
    }

    /// <summary>Writes a big-endian 32-bit float.</summary>
    public PluginPayloadWriter WriteFloat(float value)
    {
        new PacketWriter(_buffer).WriteFloat(value);
        return this;
    }

    /// <summary>Writes a big-endian 64-bit double.</summary>
    public PluginPayloadWriter WriteDouble(double value)
    {
        new PacketWriter(_buffer).WriteDouble(value);
        return this;
    }

    /// <summary>Writes a VarInt.</summary>
    public PluginPayloadWriter WriteVarInt(int value)
    {
        new PacketWriter(_buffer).WriteVarInt(value);
        return this;
    }

    /// <summary>Writes a VarLong.</summary>
    public PluginPayloadWriter WriteVarLong(long value)
    {
        new PacketWriter(_buffer).WriteVarLong(value);
        return this;
    }

    /// <summary>Writes a VarInt-length-prefixed UTF-8 string.</summary>
    public PluginPayloadWriter WriteString(string value)
    {
        new PacketWriter(_buffer).WriteString(value);
        return this;
    }

    /// <summary>Writes a 128-bit UUID (two big-endian longs, most significant first).</summary>
    public PluginPayloadWriter WriteUuid(Guid value)
    {
        new PacketWriter(_buffer).WriteUuid(value);
        return this;
    }

    /// <summary>Writes a block position packed into one long, in the negotiated version's layout.</summary>
    public PluginPayloadWriter WriteBlockPos(BlockPos value)
    {
        new PacketWriter(_buffer).WriteBlockPos(value, _layout);
        return this;
    }

    /// <summary>Writes raw bytes with no length prefix.</summary>
    public PluginPayloadWriter WriteBytes(ReadOnlySpan<byte> value)
    {
        new PacketWriter(_buffer).WriteBytes(value);
        return this;
    }

    /// <summary>Writes a VarInt length prefix followed by the bytes.</summary>
    public PluginPayloadWriter WriteByteArray(ReadOnlySpan<byte> value)
    {
        new PacketWriter(_buffer).WriteByteArray(value);
        return this;
    }

    /// <summary>Copies the bytes written so far into a fresh array.</summary>
    public byte[] ToArray() => _buffer.WrittenSpan.ToArray();
}
