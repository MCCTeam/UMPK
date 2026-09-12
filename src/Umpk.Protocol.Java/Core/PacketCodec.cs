namespace Umpk.Protocol.Java.Codecs;

/// <summary>Decodes a value from a <see cref="PacketReader"/> under a codec context.</summary>
public delegate T PacketDecoder<out T>(ref PacketReader reader, PacketCodecContext context);

/// <summary>Encodes a value into a <see cref="PacketWriter"/> under a codec context.</summary>
public delegate void PacketEncoder<in T>(ref PacketWriter writer, T value, PacketCodecContext context);

/// <summary>A version-specific serializer for one payload shape. Serialization lives outside the packet record in these composable codec objects. Codecs are constructed once (era members are static readonly fields) and then allocation-free on the hot path: the encode/decode delegates close over era layout values, never over per-call state.</summary>
/// <typeparam name="T">The value type this codec serializes.</typeparam>
public sealed class PacketCodec<T>
{
    private readonly PacketEncoder<T> _encode;

    private readonly PacketDecoder<T> _decode;

    private PacketCodec(PacketEncoder<T> encode, PacketDecoder<T> decode, WireShape shape)
    {
        _encode = encode;
        _decode = decode;
        Shape = shape;
    }

    /// <summary>What this codec reads, carried on the codec object so the codec-identity pin can render a column no bind-site spelling can reach. <see cref="WireShape.Opaque"/> until the codec declares one.</summary>
    public WireShape Shape { get; }

    /// <summary>Builds a codec from an encode and a decode delegate.</summary>
    public static PacketCodec<T> Of(PacketEncoder<T> encode, PacketDecoder<T> decode)
    {
        ArgumentNullException.ThrowIfNull(encode);
        ArgumentNullException.ThrowIfNull(decode);
        return new PacketCodec<T>(encode, decode, WireShape.Opaque);
    }

    /// <summary>Builds a codec that also declares what it reads.</summary>
    /// <param name="encode">The encode half.</param>
    /// <param name="decode">The decode half.</param>
    /// <param name="shape">The field list this codec reads, and the era value it closed over.</param>
    /// <returns>The codec.</returns>
    public static PacketCodec<T> Of(PacketEncoder<T> encode, PacketDecoder<T> decode, WireShape shape)
    {
        ArgumentNullException.ThrowIfNull(encode);
        ArgumentNullException.ThrowIfNull(decode);
        ArgumentNullException.ThrowIfNull(shape);
        return new PacketCodec<T>(encode, decode, shape);
    }

    /// <summary>Encodes a value into the writer.</summary>
    public void Encode(ref PacketWriter writer, T value, PacketCodecContext context) =>
        _encode(ref writer, value, context);

    /// <summary>Decodes a value from the reader.</summary>
    public T Decode(ref PacketReader reader, PacketCodecContext context) => _decode(ref reader, context);

    /// <summary>Adapts this codec to a different representation via a bijective mapping. Used to layer a packet record over a lower-level payload codec without duplicating the wire logic.</summary>
    public PacketCodec<TOut> Map<TOut>(Func<T, TOut> from, Func<TOut, T> to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        PacketEncoder<T> encode = _encode;
        PacketDecoder<T> decode = _decode;

        // The mapping is over the REPRESENTATION, not the wire, so the mapped codec reads exactly what this one reads and inherits its shape rather than falling back to opaque.
        return PacketCodec<TOut>.Of(
            (ref PacketWriter w, TOut value, PacketCodecContext c) => encode(ref w, to(value), c),
            (ref PacketReader r, PacketCodecContext c) => from(decode(ref r, c)),
            Shape);
    }
}
