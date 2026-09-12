using Umpk.Protocol.Java.Packets;
using Umpk.Protocol.Java.Signing;
using Xunit;

namespace Umpk.Protocol.Java.Tests.Login;

/// <summary>Unit tests for the 1.19.3+ last-seen acknowledgement tracker: the offset counter, the 20-bit bitset packing, the ring-buffer window, consecutive-duplicate de-dupe, and the standalone-ack overflow. The bitset layout is verified against the vanilla convention (slot <c>(tail + j) % 20</c> maps to bit <c>j</c>, oldest first) so the produced acknowledgement is byte-consistent with what the server expects.</summary>
public sealed class LastSeenMessagesTrackerTests
{
    private static byte[] Sig(byte id) => [id];

    [Fact]
    public void EmptyWindow_GeneratesZeroOffsetEmptyBitset()
    {
        var tracker = new LastSeenMessagesTracker();

        LastSeenMessagesUpdate update = tracker.Generate(out IReadOnlyList<byte[]> acknowledged);

        Assert.Equal(0, update.Offset);
        Assert.Equal(new byte[] { 0, 0, 0 }, update.Acknowledged);
        Assert.Equal(0, update.Checksum);
        Assert.Empty(acknowledged);
    }

    [Fact]
    public void Add_AdvancesOffset_AndGenerateResetsIt()
    {
        var tracker = new LastSeenMessagesTracker();
        Assert.False(tracker.Add(Sig(1), out _));
        Assert.False(tracker.Add(Sig(2), out _));

        LastSeenMessagesUpdate first = tracker.Generate(out _);
        Assert.Equal(2, first.Offset);

        // Offset resets on every acknowledgement; a second Generate with no new adds reports 0.
        LastSeenMessagesUpdate second = tracker.Generate(out _);
        Assert.Equal(0, second.Offset);
    }

    [Fact]
    public void ThreeAdds_PackBitsetNewestAtHighestIndex_AndAcknowledgeOldestFirst()
    {
        var tracker = new LastSeenMessagesTracker();
        tracker.Add(Sig(10), out _);
        tracker.Add(Sig(11), out _);
        tracker.Add(Sig(12), out _);

        LastSeenMessagesUpdate update = tracker.Generate(out IReadOnlyList<byte[]> acknowledged);

        // Slots 0,1,2 filled; tail=3, so they map to bits 17,18,19 -> byte[2] = 0x0E.
        Assert.Equal(new byte[] { 0x00, 0x00, 0x0E }, update.Acknowledged);
        Assert.Equal(3, update.Offset);
        Assert.Equal(3, acknowledged.Count);
        Assert.Equal(Sig(10), acknowledged[0]); // oldest first
        Assert.Equal(Sig(12), acknowledged[2]);
    }

    [Fact]
    public void ConsecutiveDuplicateSignature_IsIgnored()
    {
        var tracker = new LastSeenMessagesTracker();
        Assert.False(tracker.Add(Sig(1), out _));
        Assert.False(tracker.Add(Sig(1), out _)); // same as last -> not tracked

        LastSeenMessagesUpdate update = tracker.Generate(out IReadOnlyList<byte[]> acknowledged);
        Assert.Equal(1, update.Offset);
        Assert.Single(acknowledged);
    }

    [Fact]
    public void NonConsecutiveDuplicate_IsTracked()
    {
        // The vanilla de-dupe only compares against the immediately preceding signature, so an A B A sequence advances three times (the second A is not consecutive with the first).
        var tracker = new LastSeenMessagesTracker();
        tracker.Add(Sig(1), out _);
        tracker.Add(Sig(2), out _);
        tracker.Add(Sig(1), out _);

        LastSeenMessagesUpdate update = tracker.Generate(out _);
        Assert.Equal(3, update.Offset);
    }

    [Fact]
    public void FullWindow_KeepsLastTwenty_WithAllBitsSet()
    {
        var tracker = new LastSeenMessagesTracker();
        for (int i = 0; i < 25; i++)
        {
            Assert.False(tracker.Add(Sig((byte)i), out _)); // 25 <= threshold, no overflow flush
        }

        LastSeenMessagesUpdate update = tracker.Generate(out IReadOnlyList<byte[]> acknowledged);

        Assert.Equal(25, update.Offset);                 // offset counts every tracked add
        Assert.Equal(LastSeenMessagesTracker.WindowSize, acknowledged.Count);
        Assert.Equal(new byte[] { 0xFF, 0xFF, 0x0F }, update.Acknowledged); // all 20 bits set
    }

    [Fact]
    public void OverflowThreshold_SignalsStandaloneAck_AndResetsOffset()
    {
        var tracker = new LastSeenMessagesTracker();
        for (int i = 0; i < LastSeenMessagesTracker.PendingAckThreshold; i++)
            Assert.False(tracker.Add(Sig((byte)i), out _));

        // The 65th add crosses the threshold: it requests a standalone ack carrying the accumulated offset and resets the counter.
        bool overflow = tracker.Add(Sig(200), out int standaloneAckOffset);
        Assert.True(overflow);
        Assert.Equal(LastSeenMessagesTracker.PendingAckThreshold + 1, standaloneAckOffset);

        LastSeenMessagesUpdate update = tracker.Generate(out _);
        Assert.Equal(0, update.Offset); // reset by the standalone flush
    }
}
