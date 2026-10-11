using System.Buffers;
using Umpk.TestKit.Transport;
using Umpk.Transport;
using Xunit;

namespace Umpk.Core.Tests.Transport;

public sealed class TestMessageConnectionTests
{
    [Fact]
    public async Task AcceptedMessageTransfersToTheReceiver()
    {
        await using TestMessageConnectionPair pair = TestMessageConnectionPair.Create(capacity: 1);
        var owner = new TrackingMemoryOwner(3);
        var sent = new OwnedMessage(owner, 3);

        MessageAdmissionResult admission = await pair.First.SendAsync(sent);
        OwnedMessage received = Assert.IsType<OwnedMessage>(await pair.Second.ReceiveAsync());

        Assert.Equal(MessageAdmissionStatus.Accepted, admission.Status);
        Assert.Same(sent, received);
        Assert.Equal(0, owner.DisposeCount);

        received.Dispose();
        Assert.Equal(1, owner.DisposeCount);
    }

    [Fact]
    public async Task CapacityRejectionLeavesOwnershipWithTheCaller()
    {
        await using TestMessageConnectionPair pair = TestMessageConnectionPair.Create(capacity: 1);
        var acceptedOwner = new TrackingMemoryOwner(1);
        var rejectedOwner = new TrackingMemoryOwner(1);
        var accepted = new OwnedMessage(acceptedOwner, 1);
        var rejected = new OwnedMessage(rejectedOwner, 1);

        Assert.Equal(MessageAdmissionStatus.Accepted, (await pair.First.SendAsync(accepted)).Status);
        Assert.Equal(MessageAdmissionStatus.CapacityExceeded, (await pair.First.SendAsync(rejected)).Status);
        Assert.Equal(0, rejectedOwner.DisposeCount);

        rejected.Dispose();
        Assert.Equal(1, rejectedOwner.DisposeCount);

        (await pair.Second.ReceiveAsync())!.Dispose();
    }

    [Fact]
    public async Task PreCancelledSendLeavesOwnershipWithTheCaller()
    {
        await using TestMessageConnectionPair pair = TestMessageConnectionPair.Create(capacity: 1);
        var owner = new TrackingMemoryOwner(1);
        var message = new OwnedMessage(owner, 1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => pair.First.SendAsync(message, cancellation.Token).AsTask());
        Assert.Equal(0, owner.DisposeCount);

        message.Dispose();
        Assert.Equal(1, owner.DisposeCount);
    }

    [Fact]
    public async Task OnlyOneReceiveMayBeActivePerEndpoint()
    {
        await using TestMessageConnectionPair pair = TestMessageConnectionPair.Create(capacity: 1);
        using var cancellation = new CancellationTokenSource();
        ValueTask<OwnedMessage?> first = pair.Second.ReceiveAsync(cancellation.Token);

        await Assert.ThrowsAsync<InvalidOperationException>(() => pair.Second.ReceiveAsync().AsTask());

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.AsTask());
        Assert.False(pair.Second.Completion.IsCompleted);
    }

    [Fact]
    public async Task AbortPublishesOneStickyTypedCompletion()
    {
        await using TestMessageConnectionPair pair = TestMessageConnectionPair.Create(capacity: 1);
        var cause = new InvalidOperationException("protocol failed");
        MessageConnectionTermination termination = MessageConnectionTermination.ProtocolFailure(cause);

        pair.First.Abort(termination);

        MessageConnectionTermination first = await pair.First.Completion;
        MessageConnectionTermination firstAgain = await pair.First.Completion;
        MessageConnectionTermination second = await pair.Second.Completion;
        Assert.Same(termination, first);
        Assert.Same(first, firstAgain);
        Assert.Same(termination, second);
        Assert.Same(cause, first.Cause);
    }

    [Fact]
    public async Task ZeroTimeoutCloseIsBoundedAndDisposesQueuedMessages()
    {
        await using TestMessageConnectionPair pair = TestMessageConnectionPair.Create(capacity: 1);
        var owner = new TrackingMemoryOwner(1);
        var message = new OwnedMessage(owner, 1);
        Assert.Equal(MessageAdmissionStatus.Accepted, (await pair.First.SendAsync(message)).Status);

        await pair.First.CloseAsync(TimeSpan.Zero);

        Assert.Equal(1, owner.DisposeCount);
        Assert.Equal(MessageConnectionTerminationReason.LocalGracefulClose, (await pair.First.Completion).Reason);
        Assert.Equal(MessageConnectionTerminationReason.PeerClosed, (await pair.Second.Completion).Reason);
        Assert.Null(await pair.Second.ReceiveAsync());
    }

    [Fact]
    public async Task PeerCanDrainAnAdmittedMessageBeforeCloseCompletes()
    {
        await using TestMessageConnectionPair pair = TestMessageConnectionPair.Create(capacity: 1);
        var owner = new TrackingMemoryOwner(1);
        var message = new OwnedMessage(owner, 1);
        Assert.Equal(MessageAdmissionStatus.Accepted, (await pair.First.SendAsync(message)).Status);

        ValueTask close = pair.First.CloseAsync(TimeSpan.FromSeconds(5));
        Assert.False(close.IsCompleted);

        OwnedMessage received = (await pair.Second.ReceiveAsync())!;
        received.Dispose();
        await close;

        Assert.Equal(1, owner.DisposeCount);
        Assert.Equal(MessageConnectionTerminationReason.LocalGracefulClose, (await pair.First.Completion).Reason);
        Assert.Equal(MessageConnectionTerminationReason.PeerClosed, (await pair.Second.Completion).Reason);
    }

    [Fact]
    public async Task CancellingCloseWaitDoesNotReverseTheClose()
    {
        await using TestMessageConnectionPair pair = TestMessageConnectionPair.Create(capacity: 1);
        var owner = new TrackingMemoryOwner(1);
        var message = new OwnedMessage(owner, 1);
        Assert.Equal(MessageAdmissionStatus.Accepted, (await pair.First.SendAsync(message)).Status);
        using var cancellation = new CancellationTokenSource();

        ValueTask close = pair.First.CloseAsync(TimeSpan.FromSeconds(5), cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => close.AsTask());
        Assert.False(pair.First.Completion.IsCompleted);

        var rejected = new OwnedMessage(new TrackingMemoryOwner(1), 1);
        Assert.Equal(MessageAdmissionStatus.Closed, (await pair.Second.SendAsync(rejected)).Status);
        rejected.Dispose();

        (await pair.Second.ReceiveAsync())!.Dispose();
        Assert.Equal(MessageConnectionTerminationReason.LocalGracefulClose, (await pair.First.Completion).Reason);
        Assert.Equal(MessageConnectionTerminationReason.PeerClosed, (await pair.Second.Completion).Reason);
    }

    [Fact]
    public async Task AbortCompletesPendingReceiveAndDisposesExactlyOnce()
    {
        TestMessageConnectionPair pair = TestMessageConnectionPair.Create(capacity: 1);
        var owner = new TrackingMemoryOwner(1);
        var message = new OwnedMessage(owner, 1);
        Assert.Equal(MessageAdmissionStatus.Accepted, (await pair.First.SendAsync(message)).Status);
        ValueTask<OwnedMessage?> pending = pair.First.ReceiveAsync();

        pair.First.Abort(MessageConnectionTermination.Aborted());

        Assert.Null(await pending.AsTask().WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(1, owner.DisposeCount);

        await pair.DisposeAsync();
        await pair.DisposeAsync();
        Assert.Equal(1, owner.DisposeCount);
    }

    private sealed class TrackingMemoryOwner(int length) : IMemoryOwner<byte>
    {
        private readonly byte[] _buffer = new byte[length];

        public int DisposeCount { get; private set; }

        public Memory<byte> Memory => _buffer;

        public void Dispose()
        {
            DisposeCount++;
        }
    }
}
