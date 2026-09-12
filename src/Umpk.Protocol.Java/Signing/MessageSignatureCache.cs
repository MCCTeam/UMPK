namespace Umpk.Protocol.Java.Signing;

/// <summary>The per-connection compression cache for <c>LastSeenMessages</c> entries on inbound <c>player_chat</c>. A v3 (1.19.3+) last-seen entry rides the wire either as a full 256-byte signature or as a small integer id into this cache (<see cref="Umpk.Protocol.Java.Packets.PackedMessageSignature"/>), because the server has usually already sent that exact signature down this same connection and does not repeat it. Resolving a cache-id entry back to the signature bytes the sender actually signed over requires this side to hold the SAME cache contents the server's per-connection cache holds, built by replaying the same push order.</summary>
/// <remarks>
/// <para>One instance belongs to each connection, not each sender. Every inbound <c>player_chat</c> updates the same cache regardless of which peer sent it, because a later message from peer B can reference a signature that arrived on an earlier message from peer A. A cache scoped to one <see cref="SignedChatVerifier"/> would miss that cross-sender case. Callers should therefore construct one cache per session and share it across every peer's verifier, the same way the session shares one connection.</para>
/// <para>Push a successfully resolved message's own signature and fully resolved last-seen entries before the chain validator, so a message the chain check goes on to reject is still cached: the server's own cache was populated purely by having broadcast the frame, not by whether this client's chain check later accepted it. <see cref="Push"/> must be called on that same unconditional schedule for the cache to keep mirroring the server's.</para>
/// </remarks>
public sealed class MessageSignatureCache
{
    /// <summary>The protocol's default cache capacity.</summary>
    public const int DefaultCapacity = 128;

    private readonly byte[]?[] _entries;

    /// <summary>Creates a cache with the vanilla default capacity of <see cref="DefaultCapacity"/> slots.</summary>
    public MessageSignatureCache()
        : this(DefaultCapacity)
    {
    }

    /// <summary>Creates a cache with the given capacity.</summary>
    public MessageSignatureCache(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _entries = new byte[capacity][];
    }

    /// <summary>True once this cache is known to have fallen out of sync with the server's own per-connection cache: either an unresolvable cache-id reference was seen (a slot that was never pushed, or an out-of-range id), or the caller separately detected a missed <c>player_chat</c> frame that would have advanced the server's cache without this side observing the corresponding push (a chat-stream gap). The cache cannot recover within that connection because every slot assigned after the missed push is unknown.</summary>
    /// <remarks>
    /// <para><b>The practical effect is a verification blackout for the rest of the session, not merely failure of later cache-id references.</b> The triggering message cannot advance its sender's verified message index. Because v3 signatures bind that running index, subsequent messages from that sender fail verification and the chain remains broken until reconnect.</para>
    /// <para>Once <see cref="IsDesynced"/> is true, <see cref="Unpack"/> also returns null unconditionally for every id, including slots that still contain bytes, rather than risk resolving an id against a shifted cache position.</para>
    /// <para>Advancing the chain index for an unchecked message would claim chain continuity without verifying its signature. Verification therefore fails closed while message delivery remains available. A fresh <see cref="MessageSignatureCache"/> created on reconnect starts trusted again.</para>
    /// </remarks>
    public bool IsDesynced { get; private set; }

    /// <summary>The human-readable cause the FIRST call to <see cref="MarkDesynced(string)"/> recorded, or null if this cache has never been desynced. A later desync call's reason is discarded: once desynced there is nothing more specific to learn from a second trigger, so this always names the FIRST cause, matching what actually put the cache in its unrecoverable state.</summary>
    public string? DesyncReason { get; private set; }

    /// <summary>Marks this cache desynced, recording <paramref name="reason"/> as the cause (see <see cref="DesyncReason"/>). Idempotent with respect to <see cref="IsDesynced"/> itself: a call after the cache is already desynced still returns normally but does not overwrite <see cref="DesyncReason"/>. Callers that want to know whether THIS call was the one that actually caused the transition (to log a one-time notice rather than repeating it for every later message) should read <see cref="IsDesynced"/> before calling and compare it to after, which is exactly what <c>ChatApplier.ApplyPlayerChatAsync</c> does.</summary>
    public void MarkDesynced(string reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        if (!IsDesynced)
            DesyncReason = reason;

        IsDesynced = true;
    }

    /// <summary>Resolves a wire-carried cache id back to the signature bytes it names, or null when this cache has nothing at that slot (including an out-of-range id, which vanilla's own array would fault on; this reports it the same way a slot that was never pushed does, since both mean the reference cannot be honoured), or when <see cref="IsDesynced"/> is true (every id is refused once this cache's contents are no longer trusted to match the server's).</summary>
    /// <remarks>Returns a defensive copy rather than the internal array. <see cref="Push"/> also copies on the way in. <c>ChatMessageDeleted.ResolvedSignature</c> exposes the returned array publicly, so sharing the stored array would let a consumer corrupt future cache resolutions.</remarks>
    public byte[]? Unpack(int id) =>
        !IsDesynced && id >= 0 && id < _entries.Length && _entries[id] is { } stored
            ? (byte[])stored.Clone()
            : null;

    /// <summary>Records one inbound message: the resolved last-seen signatures it carried, in wire order, followed by the message's own signature. This order matches the connection cache's update order. <paramref name="ownSignature"/> is null only when the caller has no signature to record (there is no vanilla case for this on <c>player_chat</c>, since every frame that reaches this point has one; the parameter stays nullable so a caller cannot be forced to fabricate one).</summary>
    /// <remarks>Stores DEFENSIVE COPIES, not the arrays handed in. The caller's arrays are typically a decoded packet's own <c>Signature</c>/<c>FullSignature</c> fields, and this cache holds on to whatever it is given for the rest of the connection; nothing about <c>byte[]</c> stops a consumer elsewhere from later mutating (or pooling and reusing) an array it still holds a reference to, which would silently corrupt an already-committed slot if this cache shared the reference instead of copying.</remarks>
    public void Push(IReadOnlyList<byte[]> lastSeenSignatures, byte[]? ownSignature)
    {
        ArgumentNullException.ThrowIfNull(lastSeenSignatures);

        var queue = new List<byte[]>(lastSeenSignatures.Count + 1);
        foreach (byte[] entry in lastSeenSignatures)
            queue.Add((byte[])entry.Clone());

        if (ownSignature is not null)
            queue.Add((byte[])ownSignature.Clone());

        PushQueue(queue);
    }

    /// <summary>The cache shuffle walks the array from slot 0, replacing each slot with the queue's LAST element (so the queue's tail, which is the most-recently-added item, the message's own signature, lands first at the lowest slots), and whenever a displaced occupant is not itself one of the items being pushed, requeue it at the FRONT so it survives and gets placed again once the new items run out, rather than being silently dropped.</summary>
    private void PushQueue(List<byte[]> queue)
    {
        var queued = new HashSet<byte[]>(queue, SignatureBytesComparer.Instance);
        for (int i = 0; queue.Count > 0 && i < _entries.Length; i++)
        {
            byte[]? old = _entries[i];
            int lastIndex = queue.Count - 1;
            _entries[i] = queue[lastIndex];
            queue.RemoveAt(lastIndex);
            if (old is not null && !queued.Contains(old))
                queue.Insert(0, old);

        }
    }

    /// <summary>Value equality over raw signature bytes, since the wire hands out a fresh array each time.</summary>
    private sealed class SignatureBytesComparer : IEqualityComparer<byte[]>
    {
        public static readonly SignatureBytesComparer Instance = new();

        public bool Equals(byte[]? x, byte[]? y) =>
            x is null ? y is null : y is not null && x.AsSpan().SequenceEqual(y);

        public int GetHashCode(byte[] obj)
        {
            HashCode hash = default;
            foreach (byte b in obj)
                hash.Add(b);

            return hash.ToHashCode();
        }
    }
}
