using System.Buffers;
using Umpk.Protocol.Java.Codecs;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Item;

/// <summary>Round-trip helpers for the item family, run against a populated registry context.</summary>
internal static class ItemCodecRoundTrip
{
    /// <summary>Encodes then decodes through a codec under the item test context, asserting frame-exactness.</summary>
    public static T Cycle<T>(PacketCodec<T> codec, T value)
    {
        byte[] bytes = Encode(codec, value);
        var reader = new PacketReader(bytes);
        T decoded = codec.Decode(ref reader, ItemTestRegistries.Context);
        Assert.Equal(0, reader.Remaining);
        return decoded;
    }

    /// <summary>Encodes through a codec under the item test context and returns the bytes.</summary>
    public static byte[] Encode<T>(PacketCodec<T> codec, T value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new PacketWriter(buffer);
        codec.Encode(ref writer, value, ItemTestRegistries.Context);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Asserts that re-encoding the decoded value produces the identical byte stream.</summary>
    public static void AssertByteStable<T>(PacketCodec<T> codec, T value)
    {
        byte[] first = Encode(codec, value);
        var reader = new PacketReader(first);
        T decoded = codec.Decode(ref reader, ItemTestRegistries.Context);
        Assert.Equal(0, reader.Remaining);
        byte[] second = Encode(codec, decoded);
        Assert.Equal(first, second);
    }
}
