using System.Buffers;
using Umpk.TestKit.Transport;
using Umpk.Transport;
using Xunit;

namespace Umpk.Core.Tests.Transport;

public sealed class MessageSendReservationContractTests
{
    [Fact]
    public async Task ReservationHoldsCapacityUntilCommittedOrDisposed()
    {
        await using TestMessageConnectionPair pair = TestMessageConnectionPair.Create(capacity: 1);
        var provider = Assert.IsAssignableFrom<IMessageSendReservationProvider>(pair.First);

        MessageSendReservationResult first = await provider.ReserveSendAsync(3);
        MessageSendReservationResult rejected = await provider.ReserveSendAsync(1);

        Assert.Equal(MessageAdmissionStatus.Accepted, first.Status);
        Assert.NotNull(first.Reservation);
        Assert.Equal(MessageAdmissionStatus.CapacityExceeded, rejected.Status);
        Assert.Null(rejected.Reservation);

        first.Reservation.Dispose();
        first.Reservation.Dispose();
        MessageSendReservationResult afterRelease = await provider.ReserveSendAsync(1);
        Assert.Equal(MessageAdmissionStatus.Accepted, afterRelease.Status);
        afterRelease.Reservation!.Dispose();
    }

    [Fact]
    public async Task MismatchedCommitConsumesReservationAndLeavesOwnershipWithCaller()
    {
        await using TestMessageConnectionPair pair = TestMessageConnectionPair.Create(capacity: 1);
        var provider = Assert.IsAssignableFrom<IMessageSendReservationProvider>(pair.First);
        MessageSendReservationResult result = await provider.ReserveSendAsync(1);
        var owner = new TrackingMemoryOwner(2);
        var message = new OwnedMessage(owner, 2);

        await Assert.ThrowsAsync<ArgumentException>(() => result.Reservation!.CommitAsync(message).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => result.Reservation!.CommitAsync(message).AsTask());

        Assert.Equal(0, owner.DisposeCount);
        MessageSendReservationResult replacement = await provider.ReserveSendAsync(2);
        Assert.Equal(MessageAdmissionStatus.Accepted, replacement.Status);
        replacement.Reservation!.Dispose();
        message.Dispose();
        Assert.Equal(1, owner.DisposeCount);
    }

    [Fact]
    public async Task CommitTransfersOwnershipWithoutASecondCapacityDecision()
    {
        await using TestMessageConnectionPair pair = TestMessageConnectionPair.Create(capacity: 1);
        var provider = Assert.IsAssignableFrom<IMessageSendReservationProvider>(pair.First);
        MessageSendReservationResult result = await provider.ReserveSendAsync(3);
        var owner = new TrackingMemoryOwner(3);
        var message = new OwnedMessage(owner, 3);

        MessageAdmissionResult admission = await result.Reservation!.CommitAsync(message);
        OwnedMessage received = Assert.IsType<OwnedMessage>(await pair.Second.ReceiveAsync());

        Assert.Equal(MessageAdmissionStatus.Accepted, admission.Status);
        Assert.True(admission.OwnershipTransferred);
        Assert.Same(message, received);
        received.Dispose();
        result.Reservation.Dispose();
        Assert.Equal(1, owner.DisposeCount);
    }

    [Fact]
    public async Task ClosedCommitLeavesMessageOwnershipWithCaller()
    {
        await using TestMessageConnectionPair pair = TestMessageConnectionPair.Create(capacity: 1);
        var provider = Assert.IsAssignableFrom<IMessageSendReservationProvider>(pair.First);
        MessageSendReservationResult result = await provider.ReserveSendAsync(1);
        await pair.First.CloseAsync(TimeSpan.Zero);
        var owner = new TrackingMemoryOwner(1);
        var message = new OwnedMessage(owner, 1);

        MessageAdmissionResult admission = await result.Reservation!.CommitAsync(message);

        Assert.Equal(MessageAdmissionStatus.Closed, admission.Status);
        Assert.False(admission.OwnershipTransferred);
        Assert.Equal(0, owner.DisposeCount);
        message.Dispose();
        result.Reservation.Dispose();
        Assert.Equal(1, owner.DisposeCount);
    }

    [Fact]
    public async Task PreCancelledReservationDoesNotConsumeCapacity()
    {
        await using TestMessageConnectionPair pair = TestMessageConnectionPair.Create(capacity: 1);
        var provider = Assert.IsAssignableFrom<IMessageSendReservationProvider>(pair.First);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.ReserveSendAsync(1, cancellation.Token).AsTask());

        MessageSendReservationResult result = await provider.ReserveSendAsync(1);
        Assert.Equal(MessageAdmissionStatus.Accepted, result.Status);
        result.Reservation!.Dispose();
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
