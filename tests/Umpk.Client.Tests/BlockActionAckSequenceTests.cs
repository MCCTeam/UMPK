using Umpk.Client.Internal;
using Umpk.Client.Tests.Support;
using Umpk.Data.Java;
using Umpk.Protocol.Java.Packets;
using Xunit;

namespace Umpk.Client.Tests;

/// <summary>dig/place futures complete on protocol acknowledgment. The block-change-ack applier feeds the acknowledged sequence into the shared <see cref="SequenceTracker"/>, completing awaiting actions; the tracker also supports the bounded-timeout (cancellation) fallback for eras/servers without acks.</summary>
public sealed class BlockActionAckSequenceTests
{
    [Fact]
    public async Task SequenceTracker_WaitFor_Completes_On_Acknowledge()
    {
        var tracker = new SequenceTracker();
        int seq = tracker.Next();
        Task wait = tracker.WaitForAsync(seq, CancellationToken.None);
        Assert.False(wait.IsCompleted);

        tracker.Acknowledge(seq);

        await wait.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(seq, tracker.LastAcknowledged);
    }

    [Fact]
    public async Task SequenceTracker_WaitFor_Already_Acknowledged_Completes_Immediately()
    {
        var tracker = new SequenceTracker();
        int seq = tracker.Next();
        tracker.Acknowledge(seq);

        await tracker.WaitForAsync(seq, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task SequenceTracker_WaitFor_Cancels_On_Timeout_Token()
    {
        var tracker = new SequenceTracker();
        int seq = tracker.Next();
        using var cts = new CancellationTokenSource();
        Task wait = tracker.WaitForAsync(seq, cts.Token);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
    }

    [Fact]
    public async Task BlockChangedAck_Applier_Acknowledges_And_Completes_Wait()
    {
        var harness = new ApplierHarness(JavaVersions.V1_21_5);
        int seq = harness.Sequences.Next();
        Task wait = harness.Sequences.WaitForAsync(seq, CancellationToken.None);

        await harness.ApplyAsync(new ClientboundBlockChangedAckPacket(seq));

        await wait.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(seq, harness.Sequences.LastAcknowledged);
    }
}
