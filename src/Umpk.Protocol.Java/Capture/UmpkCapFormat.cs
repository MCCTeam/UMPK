using System.Buffers.Binary;

namespace Umpk.Protocol.Java.Capture;

/// <summary>The on-disk <c>.umpkcap</c> binary format: the recorded unit (a decrypted, decompressed frame payload with its direction, phase, wire id and timing) serialized as a small fixed header followed by a plain sequence of records.</summary>
/// <remarks>
/// <para>
/// Layout (all multi-byte integers little-endian):
/// <list type="bullet">
/// <item>magic: 6 bytes, ASCII "UMPKC1".</item>
/// <item>formatVersion: 1 byte.</item>
/// <item>recordedAtUnixMs: 8 bytes (informational; NOT part of the content hash).</item>
/// <item>protocol: 4 bytes.</item>
/// <item>frameCount: 4 bytes.</item>
/// <item>then frameCount records, each: direction(1) phase(1) sequence(8) timestampTicks(8)
/// wireId(4, plain int32) bodyLength(4) body(bodyLength bytes).</item>
/// </list>
/// </para>
/// <para>The direction and phase bytes are an ON-DISK CONTRACT and are deliberately spelled out here rather than cast from a protocol enum, so a reordering of <see cref="PacketFlow"/> or <see cref="ProtocolPhase"/> cannot silently reinterpret every capture ever recorded. <see cref="DirectionByte"/> and <see cref="PhaseByte"/> are the only sanctioned way to produce them.</para>
/// <para>This lives in the shipping protocol assembly rather than in the test kit because captures are written by real clients in the field: a diagnostics bundle from a tester is the same format the conformance corpus uses, which is what lets a bug report be replayed through the decoder instead of merely read.</para>
/// </remarks>
public static class UmpkCapFormat
{
    /// <summary>The current on-disk format version.</summary>
    public const int Version = 1;

    /// <summary>The 6-byte magic prefix identifying a v1 capture.</summary>
    public static ReadOnlySpan<byte> Magic => "UMPKC1"u8;

    /// <summary>The byte length of the file header.</summary>
    public const int HeaderLength = 6 + 1 + 8 + 4 + 4;

    /// <summary>The byte length of the fixed part of a record, before its body.</summary>
    public const int RecordPrefixLength = 1 + 1 + 8 + 8 + 4 + 4;

    /// <summary>The offset of the frame-count field within the header.</summary>
    /// <remarks>Exposed because a streaming writer does not know the count until it closes and has to seek back and patch it. A reader trusts this field, so a writer that never patches it produces a file that looks empty rather than one that looks truncated.</remarks>
    public const int FrameCountOffset = 19;

    /// <summary>The on-disk direction byte for a protocol flow.</summary>
    public static byte DirectionByte(PacketFlow flow) => flow == PacketFlow.Serverbound ? (byte)1 : (byte)0;

    /// <summary>The on-disk phase byte for a protocol phase.</summary>
    public static byte PhaseByte(ProtocolPhase phase) => phase switch
    {
        ProtocolPhase.Handshake => 0,
        ProtocolPhase.Status => 1,
        ProtocolPhase.Login => 2,
        ProtocolPhase.Configuration => 3,
        ProtocolPhase.Play => 4,
        _ => 0,
    };

    /// <summary>Writes the file header into <paramref name="dst"/>.</summary>
    public static void WriteHeader(Span<byte> dst, long recordedAtUnixMs, int protocol, int frameCount)
    {
        Magic.CopyTo(dst);
        dst[6] = (byte)Version;
        BinaryPrimitives.WriteInt64LittleEndian(dst[7..], recordedAtUnixMs);
        BinaryPrimitives.WriteInt32LittleEndian(dst[15..], protocol);
        BinaryPrimitives.WriteInt32LittleEndian(dst[FrameCountOffset..], frameCount);
    }

    /// <summary>Writes a record's fixed prefix into <paramref name="dst"/>.</summary>
    public static void WriteRecordPrefix(
        Span<byte> dst, byte direction, byte phase, long sequence, long timestampTicks, int wireId, int bodyLength)
    {
        dst[0] = direction;
        dst[1] = phase;
        BinaryPrimitives.WriteInt64LittleEndian(dst[2..], sequence);
        BinaryPrimitives.WriteInt64LittleEndian(dst[10..], timestampTicks);
        BinaryPrimitives.WriteInt32LittleEndian(dst[18..], wireId);
        BinaryPrimitives.WriteInt32LittleEndian(dst[22..], bodyLength);
    }
}
