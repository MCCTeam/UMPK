using System.Buffers;
using System.IO.Pipelines;
using Umpk.Protocol.Java.Crypto;

namespace Umpk.Protocol.Java.Transport;

/// <summary>Writes length-prefixed frames to a <see cref="PipeWriter"/>, applying (in order): compression (if enabled), VarInt length prefix, and encryption (AES-CFB8, if enabled). Callers pass the uncompressed frame content (wire id VarInt already written into the payload). Not thread-safe; <see cref="JavaConnection"/> serializes writes through a semaphore.</summary>
internal sealed class FrameWriter(PipeWriter writer)
{
    private readonly PipeWriter _writer = writer;

    private readonly ArrayBufferWriter<byte> _bodyScratch = new(1024);

    private readonly ArrayBufferWriter<byte> _frameScratch = new(1024);

    private AesCfb8? _encryptor;

    private int _compressionThreshold = -1;

    public void EnableEncryption(AesCfb8 encryptor) => _encryptor = encryptor;

    public void EnableCompression(int threshold) => _compressionThreshold = threshold;

    /// <summary>Encodes and flushes one frame. <paramref name="content"/> is the uncompressed frame content (wire id + fields). Returns the total wire byte count written.</summary>
    public async ValueTask<int> WriteFrameAsync(ReadOnlyMemory<byte> content, CancellationToken ct)
    {
        _frameScratch.Clear();

        ReadOnlySpan<byte> body;
        if (_compressionThreshold >= 0)
        {
            _bodyScratch.Clear();
            CompressionCodec.WriteBody(content.Span, _compressionThreshold, _bodyScratch);
            body = _bodyScratch.WrittenSpan;
        }
        else
            body = content.Span;

        // Length prefix + body.
        Span<byte> lenSpan = _frameScratch.GetSpan(VarInt.MaxBytes);
        int lenBytes = VarInt.Write(body.Length, lenSpan);
        _frameScratch.Advance(lenBytes);
        body.CopyTo(_frameScratch.GetSpan(body.Length));
        _frameScratch.Advance(body.Length);

        int total = _frameScratch.WrittenCount;

        // Encrypt the whole frame (length prefix included) in place, then hand to the pipe.
        Span<byte> dest = _writer.GetSpan(total);
        ReadOnlySpan<byte> frame = _frameScratch.WrittenSpan;
        if (_encryptor is not null)
            _encryptor.Encrypt(frame, dest);

        else
            frame.CopyTo(dest);

        _writer.Advance(total);
        FlushResult flush = await _writer.FlushAsync(ct).ConfigureAwait(false);
        if (flush.IsCompleted)
            throw new ConnectionClosedException(CloseReason.SocketEof, "The write pipe completed.");

        return total;
    }
}
