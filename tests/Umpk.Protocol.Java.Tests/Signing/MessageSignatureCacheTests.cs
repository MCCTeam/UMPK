using Umpk.Protocol.Java.Signing;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Signing;

/// <summary>The per-connection <see cref="MessageSignatureCache"/> in isolation. The bridge-level reproduction (an inbound <c>player_chat</c> whose last-seen entry is a cache-id reference) lives in <c>Umpk.Client.Tests.SignedChatVerificationTests</c>; these tests pin the cache's own push/unpack shuffle algorithm.</summary>
public class MessageSignatureCacheTests
{
    private static byte[] Sig(int seed)
    {
        var bytes = new byte[256];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)((i * 31) + seed);

        return bytes;
    }

    [Fact]
    public void Unpack_EmptyCache_ReturnsNullForEverySlot()
    {
        var cache = new MessageSignatureCache(4);
        Assert.Null(cache.Unpack(0));
        Assert.Null(cache.Unpack(3));
    }

    [Fact]
    public void Unpack_OutOfRangeId_ReturnsNullRatherThanThrowing()
    {
        // Vanilla's own array would throw ArrayIndexOutOfBoundsException here. UMPK deliberately reports this the same way as "never pushed" rather than crashing the session over a malformed id: both mean the reference cannot be honoured, and SignedChatVerification treats either as unresolvable.
        var cache = new MessageSignatureCache(4);
        Assert.Null(cache.Unpack(4));
        Assert.Null(cache.Unpack(-1));
        Assert.Null(cache.Unpack(1000));
    }

    [Fact]
    public void Push_OwnSignatureOnly_LandsAtSlotZero()
    {
        var cache = new MessageSignatureCache(4);
        byte[] sig = Sig(1);

        cache.Push([], sig);

        Assert.Equal(sig, cache.Unpack(0));
    }

    /// <summary>After message #1's push (empty last-seen, its own signature), message #1's signature must be resolvable at its assigned slot because that is what a server's <c>player_chat</c> for message #2 will reference by id.</summary>
    [Fact]
    public void Push_ThenUnpackTheSamePushedSignature_RoundTrips()
    {
        var cache = new MessageSignatureCache(128);
        byte[] sig1 = Sig(0x10);

        cache.Push([], sig1);

        // Vanilla places the most-recently-pushed item at the lowest available slot: entries[0] here, since the cache started empty. The queue's last element, the message's own signature, is removed first and written to entries[0]).
        Assert.Equal(sig1, cache.Unpack(0));
    }

    [Fact]
    public void Push_LastSeenEntriesThenOwnSignature_OwnSignatureLandsBeforeTheLastSeenEntries()
    {
        var cache = new MessageSignatureCache(128);
        byte[] ack = Sig(0x20);
        byte[] own = Sig(0x30);

        cache.Push([ack], own);

        // queue = [ack, own]; removeLast() pops own first (entries[0] = own), then ack (entries[1] = ack).
        Assert.Equal(own, cache.Unpack(0));
        Assert.Equal(ack, cache.Unpack(1));
    }

    [Fact]
    public void Push_BeyondCapacity_EvictsTheOldestAndKeepsTheNewest()
    {
        var cache = new MessageSignatureCache(2);
        byte[] sig1 = Sig(1);
        byte[] sig2 = Sig(2);
        byte[] sig3 = Sig(3);

        cache.Push([], sig1);
        cache.Push([], sig2);
        cache.Push([], sig3);

        // Each push places its one new item at slot 0 and shifts the previous slot-0 occupant to slot 1, evicting whatever was in slot 1. Three single-item pushes into a 2-slot cache therefore leave the two most recent signatures and drop the first.
        Assert.Equal(sig3, cache.Unpack(0));
        Assert.Equal(sig2, cache.Unpack(1));
        Assert.Null(FindSlot(cache, sig1, capacity: 2));
    }

    /// <summary>The realistic repeat-acknowledgement shape: message #1's signature (already cached from its own push) is acknowledged again in message #2's last-seen window, alongside message #2's own new signature. It must end up in the cache exactly once rather than occupying both its previous slot and a newly assigned slot.</summary>
    [Fact]
    public void Push_ASignatureAlreadyCached_ReappearingInLastSeen_DoesNotDuplicateAnEntry()
    {
        var cache = new MessageSignatureCache(4);
        byte[] sig1 = Sig(5);
        byte[] sig2 = Sig(6);

        cache.Push([], sig1);
        cache.Push([sig1], sig2);

        int occurrences = 0;
        for (int i = 0; i < 4; i++)
            if (cache.Unpack(i) is { } stored && stored.AsSpan().SequenceEqual(sig1))
                occurrences++;

        Assert.Equal(1, occurrences);
        Assert.Equal(sig2, cache.Unpack(0));
        Assert.Equal(sig1, cache.Unpack(1));
    }

    private static int? FindSlot(MessageSignatureCache cache, byte[] signature, int capacity)
    {
        for (int i = 0; i < capacity; i++)
            if (cache.Unpack(i) is { } stored && stored.AsSpan().SequenceEqual(signature))
                return i;

        return null;
    }

    /// <summary>An independent oracle for the requeue-on-displacement branch. The expected layout traces every slot write and queue operation rather than copying this implementation's output, so a mutation that flips <c>queue.Insert(0, old)</c> ("requeue at the front") to <c>queue.Add(old)</c> (<c>addLast</c>) changes the asserted layout rather than merely going unobserved.</summary>
    /// <remarks>
    /// <para>Three single-item pushes into a 4-slot cache can never distinguish <c>Insert(0, x)</c> from <c>Add(x)</c>: both produce the identical one-element list when the queue being requeued into is empty, which is exactly why every OTHER test in this file (all built from single-item pushes) is blind to that mutation. This test instead pushes a THIRD, multi-item batch (<c>lastSeen=[A, Y], own=Z</c>) on top of a cache already holding <c>[B, A]</c> from two prior single-item pushes, so TWO existing occupants (B, then A) are displaced in the SAME call and must be requeued relative to each other and to the still-pending new items (Y, Z) -- the exact situation where <c>addFirst</c> and <c>addLast</c> diverge. It also reuses A (already cached from the first push) as one of the THIRD push's own acknowledgements, so the same call simultaneously exercises the "displaced occupant is itself one of the items being re-pushed, so do not requeue it" dedup branch.</para>
    /// <para>Hand trace (capacity 4, entries indexed 0..3, <c>queued</c> is fixed at the START of each push):</para>
    /// <code>
    /// Push([], A):     queue=[A]                              -> entries=[A,_,_,_] Push([], B):     queue=[B]; displaces A (not in {B}), requeued front -> queue=[A]
    ///                                                          -> entries=[B,A,_,_]
    /// Push([A,Y], Z):  queue=[A,Y,Z], queued={A,Y,Z}
    ///   i=0: old=B (not in queued) -> entries[0]=Z; requeue front: queue=[B,A,Y]
    ///   i=1: old=A (IS in queued)  -> entries[1]=Y; NOT requeued (it's being re-pushed already)
    ///   i=2: old=null              -> entries[2]=A; queue=[B]
    ///   i=3: old=null              -> entries[3]=B; queue=[]
    ///                                                          -> entries=[Z,Y,A,B]
    /// </code>
    /// <para>Under the <c>addFirst</c>-&gt;<c>addLast</c> mutation, step i=0's requeue becomes <c>queue.Add(B)</c> (queue=[A,Y,B]) instead, which changes every later removal: the hand-traced mutant result is <c>[Z,B,Y,A]</c>.</para>
    /// </remarks>
    [Fact]
    public void Push_MultiItemPushWithRequeuedDisplacement_MatchesTheHandDerivedVanillaLayout()
    {
        var cache = new MessageSignatureCache(4);
        byte[] a = Sig(1);
        byte[] b = Sig(2);
        byte[] y = Sig(3);
        byte[] z = Sig(4);

        cache.Push([], a);
        cache.Push([], b);
        cache.Push([a, y], z);

        Assert.Equal(z, cache.Unpack(0));
        Assert.Equal(y, cache.Unpack(1));
        Assert.Equal(a, cache.Unpack(2));
        Assert.Equal(b, cache.Unpack(3));
    }

    /// <summary><see cref="MessageSignatureCache.Push"/> must store a defensive COPY, not the caller's array reference. The caller's arrays are typically a decoded packet's own <c>Signature</c>/<c>FullSignature</c> fields; nothing stops a consumer holding the same reference from mutating it later (or pooling and reusing the buffer), which would silently corrupt an already-committed slot if the cache shared the reference instead of copying.</summary>
    [Fact]
    public void Push_MutatingTheCallersArrayAfterward_DoesNotCorruptTheStoredSlot()
    {
        var cache = new MessageSignatureCache(4);
        byte[] sig = Sig(9);
        byte[] original = (byte[])sig.Clone();

        cache.Push([], sig);
        sig[0] ^= 0xFF; // the caller mutates its own array after Push returns

        Assert.Equal(original, cache.Unpack(0));
        Assert.NotEqual(sig, cache.Unpack(0));
    }

    /// <summary><see cref="MessageSignatureCache.Unpack"/> must also return a defensive COPY, not the internal array. <see cref="Push"/>'s copy closed the inbound half of this hole; <see cref="Unpack"/> still handed out the stored reference directly, and the <c>delete_chat</c> work then published exactly that reference on the public <c>ChatMessageDeleted.ResolvedSignature</c> event, reopening the corruption path from the outbound side: a consumer mutating its own copy of a published event's array silently corrupted the cache's slot for every later resolution.</summary>
    [Fact]
    public void Unpack_MutatingTheReturnedArray_DoesNotCorruptTheStoredSlot()
    {
        var cache = new MessageSignatureCache(4);
        byte[] sig = Sig(9);
        cache.Push([], sig);

        byte[]? unpacked = cache.Unpack(0);
        Assert.NotNull(unpacked);
        unpacked![0] ^= 0xFF; // the caller mutates the array Unpack handed back

        Assert.Equal(sig, cache.Unpack(0));
        Assert.NotEqual(unpacked, cache.Unpack(0));
    }

    /// <summary><see cref="MessageSignatureCache.DesyncReason"/> names the FIRST cause only. A later call (from a different trigger) must not overwrite it, so the reason always matches what actually put the cache in its unrecoverable state, not whatever most recently re-confirmed it.</summary>
    [Fact]
    public void MarkDesynced_RecordsOnlyTheFirstReason()
    {
        var cache = new MessageSignatureCache(4);

        cache.MarkDesynced("first cause");
        cache.MarkDesynced("second cause");

        Assert.True(cache.IsDesynced);
        Assert.Equal("first cause", cache.DesyncReason);
    }
}
