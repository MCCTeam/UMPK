using System.Buffers.Binary;

namespace Umpk.TestKit.Corpus;

/// <summary>The on-disk <c>.umpkcap</c> binary format (the recorded unit, serialized). The file is a small fixed header followed by a length-agnostic sequence of frame records; each record carries the direction/phase/wire id/timing metadata and the pre-decode body bytes.</summary>
/// <remarks>
/// Layout (all multi-byte integers little-endian):
/// <list type="bullet">
/// <item>magic: 6 bytes, ASCII "UMPKC1".</item>
/// <item>formatVersion: 1 byte.</item>
/// <item>recordedAtUnixMs: 8 bytes (informational; NOT part of the content hash).</item>
/// <item>protocol: 4 bytes.</item>
/// <item>frameCount: 4 bytes.</item>
/// <item>then frameCount records, each: direction phase sequence timestampTicks
/// wireId(4, zig-zag-free plain int32) bodyLength body(bodyLength bytes).</item>
/// </list>
/// The content hash (see the manifest) is SHA-256 over just the concatenated record bytes, so it is stable across re-records of identical traffic regardless of the header timestamp.
/// </remarks>
public static class UmpkCapFormat
{
    /// <summary>The current on-disk format version.</summary>
    public const int Version = 1;

    /// <summary>The 6-byte magic prefix identifying a v1 capture.</summary>
    public static ReadOnlySpan<byte> Magic => "UMPKC1"u8;

    internal const int HeaderLength = 6 + 1 + 8 + 4 + 4;
    internal const int RecordPrefixLength = 1 + 1 + 8 + 8 + 4 + 4;

    internal static void WriteHeader(Span<byte> dst, long recordedAtUnixMs, int protocol, int frameCount)
    {
        Magic.CopyTo(dst);
        dst[6] = (byte)Version;
        BinaryPrimitives.WriteInt64LittleEndian(dst[7..], recordedAtUnixMs);
        BinaryPrimitives.WriteInt32LittleEndian(dst[15..], protocol);
        BinaryPrimitives.WriteInt32LittleEndian(dst[19..], frameCount);
    }

    internal static void WriteRecordPrefix(Span<byte> dst, RecordedFrame frame)
    {
        dst[0] = (byte)frame.Direction;
        dst[1] = (byte)frame.Phase;
        BinaryPrimitives.WriteInt64LittleEndian(dst[2..], frame.Sequence);
        BinaryPrimitives.WriteInt64LittleEndian(dst[10..], frame.TimestampTicks);
        BinaryPrimitives.WriteInt32LittleEndian(dst[18..], frame.WireId);
        BinaryPrimitives.WriteInt32LittleEndian(dst[22..], frame.Body.Length);
    }
}
