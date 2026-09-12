using System.Buffers;
using System.IO.Pipelines;
using Umpk.Protocol.Java.Crypto;

namespace Umpk.Protocol.Java.Transport;

/// <summary>Reads length-prefixed frames from a <see cref="PipeReader"/>, applying (in order): decryption (AES-CFB8, if enabled), VarInt length framing, and decompression (if enabled). Multi-segment inbound frames are linearized into pooled buffers before handoff (the framing rule); the common single-segment case avoids the copy. The decrypted, decompressed payload is delivered as a pooled buffer that the caller returns to the pool via <see cref="ReturnFrame"/>.</summary>
internal sealed class FrameReader(PipeReader reader, int maxFrameLength)
{
    private readonly PipeReader _reader = reader;

    // Maximum accepted uncompressed frame length (JavaConnectionOptions.MaxFrameLength). Applied to the uncompressed frame body and, on the compressed path, to the declared uncompressed length before inflation. Defaults to the protocol limit of 8,388,608 bytes.
    private readonly int _maxFrameLength = maxFrameLength;

    // Decrypt-and-accumulate buffer. Because AES-CFB8 is a stateful stream cipher, each byte must be decrypted exactly once, in order. We copy pipe bytes here, decrypt in place, and frame from this buffer. When encryption is off this buffer is skipped entirely.
    private byte[] _plain = ArrayPool<byte>.Shared.Rent(8 * 1024);

    private int _plainStart;   // first unconsumed byte

    private int _plainEnd;     // one past last valid byte

    private AesCfb8? _decryptor;

    private int _compressionThreshold = -1;

    private bool _validateDecompressed;

    public void EnableDecryption(AesCfb8 decryptor) => _decryptor = decryptor;

    // The server-side validation flag rejects a compressed frame declaring an uncompressed length below the threshold. Clients pass false.
    public void EnableDecompression(int threshold, bool validateDecompressed = false)
    {
        _compressionThreshold = threshold;
        _validateDecompressed = validateDecompressed;
    }

    public bool CompressionEnabled => _compressionThreshold >= 0;

    /// <summary>Rents/returns pooled frame payload buffers.</summary>
    public static void ReturnFrame(byte[] buffer) => ArrayPool<byte>.Shared.Return(buffer);

    /// <summary>Reads the next frame. Returns the wire-format length-prefixed frame's inner content: the raw length-prefixed frame body (post-decrypt) is split into a payload span. When compression is enabled the compressed-format header is stripped. The returned payload's buffer must be returned via <see cref="ReturnFrame"/>. Returns null on clean end of stream.</summary>
    public async ValueTask<FramePayload?> ReadFrameAsync(CancellationToken ct)
    {
        while (true)
        {
            // Try to frame from what we already have buffered.
            if (TryExtractFrame(out FramePayload payload))
                return payload;

            // Need more bytes from the pipe.
            ReadResult result = await _reader.ReadAsync(ct).ConfigureAwait(false);
            ReadOnlySequence<byte> buffer = result.Buffer;

            if (!buffer.IsEmpty)
                AppendAndDecrypt(buffer);

            // We consumed everything the pipe handed us into our own accumulation buffer.
            _reader.AdvanceTo(buffer.End);

            if (result.IsCompleted)
            {
                // Attempt one final frame from remaining buffered bytes.
                if (TryExtractFrame(out FramePayload finalPayload))
                    return finalPayload;

                if (_plainEnd - _plainStart > 0)
                    throw new InvalidDataException("Stream ended mid-frame.");

                return null;
            }
        }
    }

    private void AppendAndDecrypt(in ReadOnlySequence<byte> buffer)
    {
        int incoming = checked((int)buffer.Length);
        EnsureCapacity(incoming);

        int writeAt = _plainEnd;
        foreach (ReadOnlyMemory<byte> segment in buffer)
        {
            segment.Span.CopyTo(_plain.AsSpan(writeAt));
            writeAt += segment.Length;
        }

        // Decrypt the newly appended region in place (order-preserving).
        if (_decryptor is not null)
        {
            Span<byte> fresh = _plain.AsSpan(_plainEnd, incoming);
            _decryptor.Decrypt(fresh, fresh);
        }

        _plainEnd += incoming;
    }

    private bool TryExtractFrame(out FramePayload payload)
    {
        payload = default;
        int available = _plainEnd - _plainStart;
        if (available == 0)
            return false;

        ReadOnlySpan<byte> view = _plain.AsSpan(_plainStart, available);

        // The frame-length VarInt is limited to three bytes (21 bits); wider prefixes are corrupt.
        if (!TryReadFrameLength(view, out int frameLength, out int headerBytes))
            return false;

        if (frameLength < 0)
            throw new InvalidDataException($"Negative frame length {frameLength}.");

        // Reject an oversized frame before we accumulate its body. MaxFrameLength (JavaConnectionOptions) was documented as a protocol-violation ceiling but never enforced; enforce it here so a peer cannot stream an arbitrarily large single frame and grow the accumulation buffer.
        if (frameLength > _maxFrameLength)
            throw new ProtocolViolationException(
                $"Frame length {frameLength} exceeds the maximum {_maxFrameLength}.");

        if (available - headerBytes < frameLength)
        {
            return false; // frame body not fully arrived yet
        }

        ReadOnlySpan<byte> frameBody = view.Slice(headerBytes, frameLength);

        byte[] resultBuffer;
        int resultLength;
        if (CompressionEnabled)
        {
            // The declared uncompressed length is capped at the configured frame maximum before inflation.
            resultBuffer = CompressionCodec.Decompress(
                frameBody, _maxFrameLength, out resultLength, _compressionThreshold, _validateDecompressed);
        }
        else
        {
            resultLength = frameBody.Length;
            resultBuffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, resultLength));
            frameBody.CopyTo(resultBuffer);
        }

        _plainStart += headerBytes + frameLength;
        CompactIfNeeded();

        payload = new FramePayload(resultBuffer, resultLength, frameLength);
        return true;
    }

    private void EnsureCapacity(int incoming)
    {
        int used = _plainEnd - _plainStart;
        if (_plainStart > 0 && used + incoming > _plain.Length)
        {
            // Slide unconsumed bytes to the front first.
            Array.Copy(_plain, _plainStart, _plain, 0, used);
            _plainEnd = used;
            _plainStart = 0;
        }

        if (_plainEnd + incoming > _plain.Length)
        {
            int newSize = _plain.Length;
            while (_plainEnd + incoming > newSize)
                newSize *= 2;

            byte[] grown = ArrayPool<byte>.Shared.Rent(newSize);
            Array.Copy(_plain, 0, grown, 0, _plainEnd);
            ArrayPool<byte>.Shared.Return(_plain);
            _plain = grown;
        }
    }

    private void CompactIfNeeded()
    {
        if (_plainStart == _plainEnd)
        {
            _plainStart = 0;
            _plainEnd = 0;
        }
    }

    public void Release()
    {
        if (_plain.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(_plain);
            _plain = [];
        }
    }

    // The frame-length prefix is at most three bytes (21 bits).
    private const int MaxFrameLengthVarIntBytes = 3;

    internal static bool TryReadFrameLength(ReadOnlySpan<byte> span, out int value, out int bytesRead)
    {
        switch (VarInt.Read(span, MaxFrameLengthVarIntBytes, out value, out bytesRead))
        {
            case VarIntStatus.Ok:
                return true;
            case VarIntStatus.TooLong:
                throw new ProtocolViolationException("Frame length prefix is wider than 21-bit.");
            default:
                // Not a fault: the prefix straddles a read boundary and the rest is still in the pipe.
                value = 0;
                bytesRead = 0;
                return false;
        }
    }
}

/// <summary>A decoded frame payload from <see cref="FrameReader"/>. Buffer is pool-owned.</summary>
internal readonly struct FramePayload(byte[] buffer, int length, int wireFrameLength)
{
    public byte[] Buffer { get; } = buffer;

    public int Length { get; } = length;

    /// <summary>Length of the on-wire (post-decrypt, pre-decompress) frame body, for stats.</summary>
    public int WireFrameLength { get; } = wireFrameLength;

    public ReadOnlySpan<byte> Span => Buffer.AsSpan(0, Length);
}
