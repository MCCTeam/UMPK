namespace Umpk.TestKit.Corpus;

/// <summary>One recorded unit: the decrypted, decompressed frame payload (wire id + packet body) plus direction/phase/timing metadata. This is pre-decode ground truth: the bytes are exactly what crossed the wire after the compression and encryption transforms were undone, before any codec ran. Re-encoding a decoded packet must reproduce <see cref="WireId"/> + <see cref="Body"/> byte-for-byte (the conformance assertion).</summary>
/// <remarks>The compression and encryption transforms are deliberately outside the recorded unit: BCL zlib output is not bit-identical to Java's Deflater, so byte-identity is only meaningful on the payload. The transforms carry their own unit tests in Umpk.Protocol.Java.Tests.</remarks>
public sealed class RecordedFrame
{
    /// <summary>Creates a recorded frame. <paramref name="body"/> is retained by reference.</summary>
    public RecordedFrame(
        long sequence,
        CorpusDirection direction,
        CorpusPhase phase,
        int wireId,
        byte[] body,
        long timestampTicks)
    {
        ArgumentNullException.ThrowIfNull(body);
        Sequence = sequence;
        Direction = direction;
        Phase = phase;
        WireId = wireId;
        Body = body;
        TimestampTicks = timestampTicks;
    }

    /// <summary>The zero-based ordinal of this frame within its scenario recording.</summary>
    public long Sequence { get; }

    /// <summary>Direction the frame travelled.</summary>
    public CorpusDirection Direction { get; }

    /// <summary>The phase the connection was in when the frame was observed.</summary>
    public CorpusPhase Phase { get; }

    /// <summary>The frame's VarInt wire id (packet id within its phase/flow).</summary>
    public int WireId { get; }

    /// <summary>The packet body: the raw frame payload after the wire id (pre-decode ground truth).</summary>
    public byte[] Body { get; }

    /// <summary>A monotonic timestamp in <see cref="System.Diagnostics.Stopwatch"/> ticks relative to the recording start, for observing cadence (keep-alive spacing etc.). Not load-bearing for conformance.</summary>
    public long TimestampTicks { get; }
}
