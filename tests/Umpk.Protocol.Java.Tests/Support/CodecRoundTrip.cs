using System.Buffers;
using Umpk.Protocol.Java.Codecs;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Support;

/// <summary>Shared helpers for byte-level codec round-trip tests.</summary>
internal static class CodecRoundTrip
{
    /// <summary>Encodes a packet through a codec then decodes it, asserting frame-exactness.</summary>
    public static T Cycle<T>(PacketCodec<T> codec, T value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        codec.Encode(ref writer, value, PacketCodecContext.Registryless);

        var reader = new PacketReader(buffer.WrittenSpan);
        T decoded = codec.Decode(ref reader, PacketCodecContext.Registryless);
        Assert.Equal(0, reader.Remaining);
        return decoded;
    }

    /// <summary>Encodes a packet through a codec and returns the produced bytes.</summary>
    public static byte[] Encode<T>(PacketCodec<T> codec, T value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        codec.Encode(ref writer, value, PacketCodecContext.Registryless);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Decodes a packet from raw wire bytes, asserting the frame is fully consumed.</summary>
    public static T Decode<T>(PacketCodec<T> codec, byte[] bytes)
    {
        var reader = new PacketReader(bytes);
        T decoded = codec.Decode(ref reader, PacketCodecContext.Registryless);
        Assert.Equal(0, reader.Remaining);
        return decoded;
    }
}
