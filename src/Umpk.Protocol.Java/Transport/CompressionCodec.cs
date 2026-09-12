using System.Buffers;
using System.IO.Compression;

namespace Umpk.Protocol.Java.Transport;

/// <summary>Zlib compression per the Minecraft protocol threshold rules, over BCL <see cref="ZLibStream"/>. Once compression is enabled with threshold T, every frame's body is prefixed with a VarInt "uncompressed data length": 0 means the payload that follows is raw (packet was smaller than T), non-zero means the payload is zlib-compressed and decompresses to that many bytes.</summary>
internal static class CompressionCodec
{
    /// <summary>Decompresses a compressed-format frame body into a pooled buffer. The caller owns the returned array and must return it to <see cref="ArrayPool{T}.Shared"/>.</summary>
    /// <param name="body">The frame body: VarInt dataLength followed by raw-or-compressed bytes.</param>
    /// <param name="maxLength">Upper bound on the uncompressed length (guards malicious sizes).</param>
    /// <param name="length">Receives the meaningful length of the returned buffer.</param>
    /// <param name="threshold">The negotiated compression threshold, used only when <paramref name="validate"/> is set.</param>
    /// <param name="validate">When set (server role), rejects a compressed frame whose declared uncompressed length is below the threshold. Clients pass <see langword="false"/>: the client role accepts the server's declaration without this check.</param>
    public static byte[] Decompress(ReadOnlySpan<byte> body, int maxLength, out int length, int threshold = 0, bool validate = false)
    {
        int dataLength = ReadVarIntSpan(body, out int headerBytes);
        ReadOnlySpan<byte> rest = body[headerBytes..];

        if (dataLength == 0)
        {
            // dataLength 0 means the rest is the raw payload. This branch needs no threshold check; the raw length is bounded by the frame length (21-bit cap).
            length = rest.Length;
            byte[] raw = ArrayPool<byte>.Shared.Rent(Math.Max(1, length));
            rest.CopyTo(raw);
            return raw;
        }

        if (dataLength < 0 || dataLength > maxLength)
            throw new InvalidDataException(
                $"Compressed frame declares uncompressed length {dataLength} outside [0, {maxLength}].");

        if (validate && dataLength < threshold)
        {
            // A conformant peer sends payloads below the threshold on the dataLength-0 raw path.
            throw new InvalidDataException(
                $"Compressed frame declares uncompressed length {dataLength} below the threshold {threshold}.");
        }

        byte[] output = ArrayPool<byte>.Shared.Rent(dataLength);
        try
        {
            using var input = new MemoryStream(rest.ToArray(), writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            int total = 0;
            while (total < dataLength)
            {
                int read = zlib.Read(output, total, dataLength - total);
                if (read <= 0)
                    break;

                total += read;
            }

            if (total != dataLength)
                throw new InvalidDataException(
                    $"Compressed frame declared {dataLength} bytes but decompressed to {total}.");

            length = dataLength;
            return output;
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(output);
            throw;
        }
    }

    /// <summary>Writes a compressed-format frame body (dataLength VarInt + raw-or-compressed bytes) for the given uncompressed payload into <paramref name="output"/>. When the payload is at or above <paramref name="threshold"/> it is zlib-compressed; otherwise a 0 dataLength is written and the payload is copied raw. Vanilla compresses when <c>length &gt;= threshold</c>.</summary>
    public static void WriteBody(ReadOnlySpan<byte> payload, int threshold, IBufferWriter<byte> output)
    {
        // An empty payload must take the raw path even at threshold 0: the compressed branch would write dataLength = 0, which decodes as "raw", so the zlib bytes would be mis-read as the raw payload. (Unreachable on the wire today because a wire-id VarInt is always present, but kept unambiguous.)
        if (payload.Length < threshold || payload.Length == 0)
        {
            // dataLength = 0, then raw payload.
            Span<byte> header = output.GetSpan(VarInt.MaxBytes);
            int n = VarInt.Write(0, header);
            output.Advance(n);
            payload.CopyTo(output.GetSpan(payload.Length));
            output.Advance(payload.Length);
            return;
        }

        // dataLength = uncompressed length, then zlib-compressed payload.
        Span<byte> lenHeader = output.GetSpan(VarInt.MaxBytes);
        int hn = VarInt.Write(payload.Length, lenHeader);
        output.Advance(hn);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionMode.Compress, leaveOpen: true))
            zlib.Write(payload);

        compressed.Position = 0;
        int compLen = (int)compressed.Length;
        Span<byte> dest = output.GetSpan(compLen);
        int read = compressed.Read(dest);
        output.Advance(read);
    }

    internal static int ReadVarIntSpan(ReadOnlySpan<byte> span, out int bytesRead) =>
        VarInt.Read(span, VarInt.MaxBytes, out int value, out bytesRead) switch
        {
            VarIntStatus.Ok => value,
            VarIntStatus.TooLong => throw new InvalidDataException("VarInt is too long (more than 5 bytes)."),
            _ => throw new InvalidDataException("Frame body ended before the uncompressed-length VarInt terminated."),
        };
}
