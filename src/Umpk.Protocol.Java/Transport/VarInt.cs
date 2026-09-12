using System.Buffers;

namespace Umpk.Protocol.Java.Transport;

/// <summary>Why a VarInt read over a contiguous span stopped.</summary>
internal enum VarIntStatus
{
    /// <summary>A complete VarInt was decoded.</summary>
    Ok,

    /// <summary>The span ended while a continuation bit was still set.</summary>
    Truncated,

    /// <summary>The encoding ran past the caller's byte cap.</summary>
    TooLong,
}

/// <summary>VarInt encode/decode helpers, and the seven-bit accumulator every contiguous VarInt read in this assembly runs on. The <see cref="ReadOnlySequence{T}"/> reader stays separate because a length prefix may straddle pipe segment boundaries, which is a walk over segments rather than an index into one span.</summary>
internal static class VarInt
{
    /// <summary>Maximum encoded length of a 32-bit VarInt.</summary>
    public const int MaxBytes = 5;

    /// <summary>Number of bytes a non-negative VarInt occupies once encoded.</summary>
    public static int SizeOf(int value)
    {
        uint v = (uint)value;
        int size = 1;
        while ((v & 0xFFFFFF80u) != 0)
        {
            v >>= 7;
            size++;
        }

        return size;
    }

    /// <summary>Writes <paramref name="value"/> into <paramref name="destination"/>, returning the byte count.</summary>
    public static int Write(int value, Span<byte> destination)
    {
        uint v = (uint)value;
        int i = 0;
        while ((v & 0xFFFFFF80u) != 0)
        {
            destination[i++] = (byte)(v | 0x80);
            v >>= 7;
        }

        destination[i++] = (byte)v;
        return i;
    }

    /// <summary>Reads a VarInt from the front of a contiguous <paramref name="span"/>, reporting why it stopped instead of choosing a failure mode: the five call sites do not agree on one. The frame-length prefix caps at 3 bytes and faults the connection, the packet reader caps at 5 and throws a protocol violation, the compression header throws <see cref="InvalidDataException"/>, and the straddling reader treats a short buffer as "wait for more" rather than an error.</summary>
    /// <param name="span">The bytes to read from, starting at the VarInt's first byte.</param>
    /// <param name="maxBytes">The widest encoding this call site accepts.</param>
    /// <param name="value">The decoded value, meaningful only on <see cref="VarIntStatus.Ok"/>.</param>
    /// <param name="bytesRead">Bytes consumed: the encoding's length on success, and on either failure the bytes the accumulator looked at, so a caller that reports an offset can still advance to it.</param>
    /// <returns>Why the read stopped.</returns>
    public static VarIntStatus Read(ReadOnlySpan<byte> span, int maxBytes, out int value, out int bytesRead)
    {
        value = 0;
        bytesRead = 0;

        int shift = 0;
        while (bytesRead < span.Length)
        {
            byte b = span[bytesRead];
            value |= (b & 0x7F) << shift;
            bytesRead++;
            if ((b & 0x80) == 0)
                return VarIntStatus.Ok;

            shift += 7;
            if (bytesRead >= maxBytes)
                return VarIntStatus.TooLong;

        }

        return VarIntStatus.Truncated;
    }

    /// <summary>Attempts to read a VarInt from the front of a contiguous <paramref name="span"/>. On success, <paramref name="value"/> holds the decoded int and <paramref name="bytesRead"/> its length. Returns false when the span is truncated mid-VarInt or the VarInt exceeds 5 bytes.</summary>
    public static bool TryRead(ReadOnlySpan<byte> span, out int value, out int bytesRead)
    {
        if (Read(span, MaxBytes, out value, out bytesRead) == VarIntStatus.Ok)
            return true;

        value = 0;
        bytesRead = 0;
        return false;
    }

    /// <summary>Attempts to read a VarInt from the front of <paramref name="sequence"/>. On success, <paramref name="value"/> holds the decoded int and <paramref name="bytesRead"/> its length. Returns false when the sequence does not yet contain a complete VarInt (need more data).</summary>
    /// <exception cref="InvalidDataException">The VarInt exceeds 5 bytes (malformed).</exception>
    public static bool TryRead(in ReadOnlySequence<byte> sequence, out int value, out int bytesRead)
    {
        value = 0;
        bytesRead = 0;

        int shift = 0;
        var reader = new SequenceReader<byte>(sequence);
        while (reader.TryRead(out byte b))
        {
            value |= (b & 0x7F) << shift;
            bytesRead++;
            if ((b & 0x80) == 0)
                return true;

            shift += 7;
            if (bytesRead >= MaxBytes)
                throw new InvalidDataException("VarInt is too long (more than 5 bytes).");

        }

        // Ran out of buffered bytes before the VarInt terminated.
        value = 0;
        bytesRead = 0;
        return false;
    }
}
