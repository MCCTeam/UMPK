using System.IO.Compression;

namespace Umpk.Protocol.Bedrock;

internal enum BedrockCompressionAlgorithm : ushort
{
    Deflate = 0,
    Snappy = 1,
}

internal readonly record struct BedrockCompressionSettings(ushort Threshold, BedrockCompressionAlgorithm Algorithm)
{
    internal static BedrockCompressionSettings Disabled { get; } = new(ushort.MaxValue, BedrockCompressionAlgorithm.Deflate);
}

internal static class BedrockCompressionCodec
{
    private const byte DeflateDiscriminator = 0x00;
    private const byte SnappyDiscriminator = 0x01;
    private const byte UncompressedDiscriminator = 0xff;

    internal static byte[] Encode(ReadOnlySpan<byte> batch, BedrockCompressionSettings settings, BedrockCodecLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (batch.Length > limits.MaxBatchBytes)
            throw new BedrockCodecException("The batch exceeds its byte limit before compression.");

        if (batch.Length < settings.Threshold)
            return Prefix(UncompressedDiscriminator, batch);

        if (settings.Algorithm is not BedrockCompressionAlgorithm.Deflate)
            throw new BedrockCodecException($"Compression algorithm {settings.Algorithm} is not implemented.");

        using var output = new MemoryStream();
        output.WriteByte(DeflateDiscriminator);
        using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true))
            deflate.Write(batch);

        return output.ToArray();
    }

    internal static byte[] Decode(ReadOnlySpan<byte> encoded, BedrockCompressionSettings settings, BedrockCodecLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (encoded.IsEmpty || encoded.Length > limits.MaxBatchBytes)
            throw new BedrockCodecException($"Invalid compressed batch length {encoded.Length}.");

        byte discriminator = encoded[0];
        if (discriminator == UncompressedDiscriminator)
        {
            if (encoded.Length - 1 > limits.MaxDecompressedBytes)
                throw new BedrockCodecException("The uncompressed batch exceeds its byte limit.");

            return encoded[1..].ToArray();
        }

        if (discriminator == SnappyDiscriminator)
            throw new BedrockCodecException("Snappy compression was negotiated but no supported implementation is installed.");

        if (discriminator != DeflateDiscriminator || settings.Algorithm is not BedrockCompressionAlgorithm.Deflate)
            throw new BedrockCodecException($"Unsupported compression discriminator 0x{discriminator:x2}.");

        try
        {
            using var input = new MemoryStream(encoded[1..].ToArray(), writable: false);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            Span<byte> buffer = stackalloc byte[8192];
            while (true)
            {
                int read = deflate.Read(buffer);
                if (read == 0)
                    break;

                if (output.Length > limits.MaxDecompressedBytes - read)
                    throw new BedrockCodecException("The decompressed batch exceeds its byte limit.");

                output.Write(buffer[..read]);
            }

            if (input.Position != input.Length)
                throw new BedrockCodecException("The compressed batch contains trailing bytes.");

            return output.ToArray();
        }
        catch (InvalidDataException exception)
        {
            throw new BedrockCodecException("The DEFLATE batch is malformed.", exception);
        }
    }

    private static byte[] Prefix(byte discriminator, ReadOnlySpan<byte> payload)
    {
        byte[] result = new byte[payload.Length + 1];
        result[0] = discriminator;
        payload.CopyTo(result.AsSpan(1));
        return result;
    }
}
