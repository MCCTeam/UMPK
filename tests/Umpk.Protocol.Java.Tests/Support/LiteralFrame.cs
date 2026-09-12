using Umpk.Protocol.Java.Codecs;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Support;

/// <summary>Hand-built wire primitives for frame-shape tests; deliberately independent of production writers.</summary>
internal static class LiteralFrame
{
    internal static readonly Guid SampleUuid = new("12345678-9abc-def0-1122-334455667788");

    internal static byte[] VarInt(int value)
    {
        var bytes = new List<byte>();
        uint remaining = (uint)value;
        do
        {
            byte current = (byte)(remaining & 0x7F);
            remaining >>= 7;
            bytes.Add(remaining != 0 ? (byte)(current | 0x80) : current);
        }
        while (remaining != 0);

        return [.. bytes];
    }

    internal static byte[] Str(string value)
    {
        byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(value);
        return [.. VarInt(utf8.Length), .. utf8];
    }

    internal static byte[] I32(int value) =>
        [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    internal static byte[] I16(short value) => [(byte)(value >> 8), (byte)value];

    internal static byte[] I64(long value)
    {
        var bytes = new byte[8];
        for (int index = 0; index < bytes.Length; index++)
            bytes[index] = (byte)(value >> (56 - (8 * index)));

        return bytes;
    }

    internal static byte[] F32(float value) => BigEndianBytes(BitConverter.GetBytes(value));

    internal static byte[] F64(double value) => BigEndianBytes(BitConverter.GetBytes(value));

    internal static byte[] PackedBlockPosition(int x, int y, int z) =>
        I64(((long)(x & 0x3FFFFFF) << 38) | ((long)(y & 0xFFF) << 26) | (z & 0x3FFFFFFL));

    internal static byte[] Uuid(Guid value)
    {
        (long high, long low) = HighLow(value);
        return [.. I64(high), .. I64(low)];
    }

    internal static byte[] Cat(params byte[][] parts)
    {
        var all = new List<byte>();
        foreach (byte[] part in parts)
            all.AddRange(part);

        return [.. all];
    }

    internal static BoundPacketCodec Clientbound(int protocol, string identifier) =>
        BoundCodec.At(protocol, PacketFlow.Clientbound, "minecraft:" + identifier);

    internal static BoundPacketCodec Serverbound(int protocol, string identifier) =>
        BoundCodec.At(protocol, PacketFlow.Serverbound, "minecraft:" + identifier);

    internal static void Rejects(BoundPacketCodec codec, byte[] frame, string because)
    {
        object decoded;
        try
        {
            decoded = codec.DecodeFrame(frame);
        }
        catch (Exception)
        {
            return;
        }

        Assert.False(frame.SequenceEqual(codec.Encode(decoded)), because);
    }

    private static byte[] BigEndianBytes(byte[] bytes)
    {
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);

        return bytes;
    }

    private static (long High, long Low) HighLow(Guid value)
    {
        Span<byte> bytes = stackalloc byte[16];
        value.TryWriteBytes(bytes, bigEndian: true, out _);
        long high = 0;
        long low = 0;
        for (int index = 0; index < 8; index++)
        {
            high = (high << 8) | bytes[index];
            low = (low << 8) | bytes[index + 8];
        }

        return (high, low);
    }
}
