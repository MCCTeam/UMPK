using System.Buffers;
using System.IO.Compression;
using Umpk.Protocol.Java;
using Umpk.Protocol.Java.Transport;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Transport;

/// <summary>Frame-length limits and the compression decode/encode edges. The frame reader enforces MaxFrameLength and vanilla's 21-bit length-prefix cap; server-role compression validation rejects a below-threshold compressed frame; and the threshold-0 empty-payload encode edge round-trips.</summary>
public class FrameLimitAndCompressionValidationTests
{
    private static CancellationToken Ct() => new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;

    [Fact]
    public async Task LengthPrefixWiderThan21Bit_FailsConnection()
    {
        // Vanilla Varint21FrameDecoder caps the length prefix at 3 bytes. A 4-byte length prefix is a protocol violation regardless of MaxFrameLength.
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, new JavaConnectionOptions
        {
            UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
            ReadIdleTimeout = TimeSpan.Zero,
        });
        conn.Start();

        // Four continuation bytes: 0x80 0x80 0x80 0x80 -> the reader throws at the 4th length byte.
        byte[] fourByteLength = [0x80, 0x80, 0x80, 0x80];
        await WriteRawAsync(pair.Right.Output, fourByteLength);

        await Assert.ThrowsAsync<ConnectionClosedException>(async () => await conn.ReceiveAsync(Ct()));
    }

    [Fact]
    public async Task CompressedFrame_OverMaxFrameLength_FailsBeforeInflation()
    {
        // A compressed frame declaring an uncompressed length above MaxFrameLength must be rejected before any inflation (the production ceiling is 8 MiB; this test uses a small cap).
        var pair = DuplexPipePair.Create();
        await using var conn = new JavaConnection(pair.Left, new JavaConnectionOptions
        {
            UnknownPacketPolicy = UnknownPacketPolicy.Preserve,
            ReadIdleTimeout = TimeSpan.Zero,
            MaxFrameLength = 1024,
        });
        conn.EnableCompression(64);
        conn.Start();

        // Build a compressed-format frame body whose declared uncompressed length (2000) exceeds the cap. The compressed body itself is tiny (highly compressible), so it clears the frame-length check and reaches decompression, where the declared 2000 > 1024 ceiling is enforced before inflation.
        byte[] body = BuildCompressedBody(declaredUncompressedLength: 2000, actualPayloadLength: 2000);
        await FramingTests.WriteRawFrameAsync(pair.Right.Output, body);

        await Assert.ThrowsAsync<ConnectionClosedException>(async () => await conn.ReceiveAsync(Ct()));
    }

    [Fact]
    public void CompressionValidate_RejectsBelowThresholdDeclaredLength()
    {
        // Server-role validation: a compressed frame declaring an uncompressed length below the threshold is a "badly compressed packet".
        byte[] body = BuildCompressedBody(declaredUncompressedLength: 10, actualPayloadLength: 10);

        Assert.Throws<InvalidDataException>(() =>
            CompressionCodec.Decompress(body, maxLength: 8 * 1024 * 1024, out _, threshold: 256, validate: true));

        // With validation off (client role), the same frame decodes fine.
        byte[] result = CompressionCodec.Decompress(body, maxLength: 8 * 1024 * 1024, out int len, threshold: 256, validate: false);
        Assert.Equal(10, len);
        ArrayPool<byte>.Shared.Return(result);
    }

    [Fact]
    public void CompressionValidate_AllowsAtOrAboveThreshold()
    {
        byte[] body = BuildCompressedBody(declaredUncompressedLength: 300, actualPayloadLength: 300);
        byte[] result = CompressionCodec.Decompress(body, maxLength: 8 * 1024 * 1024, out int len, threshold: 256, validate: true);
        Assert.Equal(300, len);
        ArrayPool<byte>.Shared.Return(result);
    }

    [Fact]
    public void DataLengthZero_TakesRawPath_RegardlessOfValidation()
    {
        // dataLength 0 means the rest is raw (below threshold). Vanilla does not validate this branch.
        byte[] payload = [1, 2, 3, 4, 5];
        var w = new ArrayBufferWriter<byte>();
        Span<byte> hdr = w.GetSpan(VarInt.MaxBytes);
        w.Advance(VarInt.Write(0, hdr)); // dataLength = 0
        payload.CopyTo(w.GetSpan(payload.Length));
        w.Advance(payload.Length);

        byte[] result = CompressionCodec.Decompress(w.WrittenSpan, maxLength: 8 * 1024 * 1024, out int len, threshold: 256, validate: true);
        Assert.Equal(payload, result.AsSpan(0, len).ToArray());
        ArrayPool<byte>.Shared.Return(result);
    }

    [Fact]
    public void ThresholdZero_EmptyPayload_EncodesAsRaw_AndRoundTrips()
    {
        // At threshold 0 an empty payload must not go through the compressed branch (which would write dataLength = 0 and mis-decode the zlib bytes as raw). It is written as an empty raw payload and round-trips to empty.
        var w = new ArrayBufferWriter<byte>();
        CompressionCodec.WriteBody(ReadOnlySpan<byte>.Empty, threshold: 0, w);

        // dataLength VarInt must be 0 and nothing else follows.
        Assert.Equal(new byte[] { 0x00 }, w.WrittenSpan.ToArray());

        byte[] result = CompressionCodec.Decompress(w.WrittenSpan, maxLength: 8 * 1024 * 1024, out int len);
        Assert.Equal(0, len);
        ArrayPool<byte>.Shared.Return(result);
    }

    // Builds a compressed-format frame body: VarInt(declaredUncompressedLength) + zlib(payload).
    private static byte[] BuildCompressedBody(int declaredUncompressedLength, int actualPayloadLength)
    {
        byte[] payload = new byte[actualPayloadLength];
        for (int i = 0; i < payload.Length; i++)
            payload[i] = (byte)(i % 7);

        var w = new ArrayBufferWriter<byte>();
        Span<byte> hdr = w.GetSpan(VarInt.MaxBytes);
        w.Advance(VarInt.Write(declaredUncompressedLength, hdr));

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionMode.Compress, leaveOpen: true))
            zlib.Write(payload);

        byte[] compBytes = compressed.ToArray();
        compBytes.CopyTo(w.GetSpan(compBytes.Length));
        w.Advance(compBytes.Length);
        return w.WrittenSpan.ToArray();
    }

    private static async Task WriteRawAsync(System.IO.Pipelines.PipeWriter output, byte[] bytes)
    {
        Memory<byte> mem = output.GetMemory(bytes.Length);
        bytes.CopyTo(mem);
        output.Advance(bytes.Length);
        await output.FlushAsync();
    }
}
