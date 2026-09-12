using Umpk.Protocol.Java.Packets;

namespace Umpk.Protocol.Java.Signing;

/// <summary>The 1.19.3+ (v3) last-seen acknowledgement tracker. It records the signatures of inbound signed messages into a fixed 20-slot ring and, at chat-send time, produces the offset + 20-bit acknowledgement bitset plus the ordered acknowledged signatures the signed body must cover. The older add-to-front <see cref="LastSeenMessagesCollector"/> models the distinct 1.19.1/1.19.2 (v2) window shape.</summary>
/// <remarks>The tracker is driven from two threads (the read loop pushes inbound signatures via <see cref="Add"/>, the send path pulls an update via <see cref="Generate"/>), so every mutation is guarded by a lock. The 20-slot window and the "offset resets on every acknowledgement" rule are the vanilla constants.</remarks>
public sealed class LastSeenMessagesTracker
{
    /// <summary>The vanilla v3 tracked-window size (20 entries, a 20-bit acknowledgement bitset).</summary>
    public const int WindowSize = 20;

    /// <summary>The vanilla standalone-acknowledgement threshold: once this many messages accumulate since the last acknowledgement, the client flushes a standalone <c>chat_ack</c> so the offset cannot grow unbounded.</summary>
    public const int PendingAckThreshold = 64;

    private readonly byte[]?[] _entries = new byte[WindowSize][];

    private readonly object _gate = new();

    private byte[]? _lastSignature;

    private int _tail;

    private int _offset;

    /// <summary>Records an inbound signed message's signature. Duplicate consecutive signatures (the vanilla de-dupe against the last tracked message) are ignored and do not advance the offset. Returns true when the accumulated offset has crossed <see cref="PendingAckThreshold"/> and the caller should send a standalone <see cref="ServerboundChatAckPacket"/> with <paramref name="standaloneAckOffset"/>; that path resets the offset (the tracked window is untouched).</summary>
    /// <param name="signature">The inbound message's signature.</param>
    /// <param name="standaloneAckOffset">The offset to flush, when the return value is true.</param>
    /// <param name="acknowledge">Whether the client displayed the message. False takes the ring slot without recording the signature, so the offset advances but the acknowledgement bitset does not claim the message was seen. A fully filtered, blocked, or hidden-as-insecure message is counted but not acknowledged.</param>
    public bool Add(byte[] signature, out int standaloneAckOffset, bool acknowledge = true)
    {
        ArgumentNullException.ThrowIfNull(signature);
        lock (_gate)
        {
            if (_lastSignature is not null && _lastSignature.AsSpan().SequenceEqual(signature))
            {
                standaloneAckOffset = 0;
                return false;
            }

            _lastSignature = signature;
            int index = _tail;
            _tail = (index + 1) % WindowSize;
            _entries[index] = acknowledge ? signature : null;
            _offset++;

            if (_offset > PendingAckThreshold)
            {
                standaloneAckOffset = _offset;
                _offset = 0;
                return true;
            }

            standaloneAckOffset = 0;
            return false;
        }
    }

    /// <summary>Produces the acknowledgement window for an outbound signed message: the offset accumulated since the last acknowledgement (reset to zero here), the 20-bit bitset marking which tracked slots are live, and the acknowledged signatures in the same oldest-to-newest slot order the signed body requires. The checksum is 0 (skip verification), matching the vanilla client.</summary>
    public LastSeenMessagesUpdate Generate(out IReadOnlyList<byte[]> acknowledged)
    {
        lock (_gate)
        {
            int offset = _offset;
            _offset = 0;

            var bitset = new byte[(WindowSize + 7) / 8];
            var acked = new List<byte[]>(WindowSize);
            for (int j = 0; j < WindowSize; j++)
            {
                int slot = (_tail + j) % WindowSize;
                if (_entries[slot] is { } signature)
                {
                    bitset[j / 8] |= (byte)(1 << (j % 8));
                    acked.Add(signature);
                }
            }

            acknowledged = acked;
            return new LastSeenMessagesUpdate(offset, bitset, Checksum: 0);
        }
    }
}
