using System.Buffers;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Umpk.Transport;
using Umpk.Transport.RakNet;
using Xunit;

namespace Umpk.Transport.RakNet.Tests;

public sealed class RakNetMessageConnectionTests
{
    [Fact]
    public async Task AcceptedSendTransfersAndDisposesOwnership()
    {
        await using ConnectionHarness harness = new();
        var owner = new TrackingMemoryOwner([0xfe]);
        var message = new OwnedMessage(owner, 1);

        MessageAdmissionResult result = await harness.Adapter.SendAsync(message);

        Assert.Equal(MessageAdmissionStatus.Accepted, result.Status);
        Assert.Equal(1, owner.DisposeCount);
        Assert.NotEmpty(harness.ReceivePeerDatagram());
    }

    [Fact]
    public async Task CapacityRejectionLeavesOwnershipWithCaller()
    {
        var limits = new global::RakNet.RakNetLimits(
            maxPendingReliableDatagrams: 1,
            maxOutboundMessages: 1,
            maxOutboundBytes: 1);
        await using ConnectionHarness harness = new(limits);
        var firstOwner = new TrackingMemoryOwner([1]);
        var secondOwner = new TrackingMemoryOwner([2]);

        Assert.Equal(MessageAdmissionStatus.Accepted,
            (await harness.Adapter.SendAsync(new OwnedMessage(firstOwner, 1))).Status);
        var rejected = new OwnedMessage(secondOwner, 1);
        MessageAdmissionResult result = await harness.Adapter.SendAsync(rejected);

        Assert.Equal(MessageAdmissionStatus.CapacityExceeded, result.Status);
        Assert.Equal(0, secondOwner.DisposeCount);
        rejected.Dispose();
        Assert.Equal(1, secondOwner.DisposeCount);
    }

    [Fact]
    public async Task PreCancelledSendLeavesOwnershipWithCaller()
    {
        await using ConnectionHarness harness = new();
        var owner = new TrackingMemoryOwner([0xfe]);
        var message = new OwnedMessage(owner, 1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            harness.Adapter.SendAsync(message, cancellation.Token).AsTask());

        Assert.Equal(0, owner.DisposeCount);
        message.Dispose();
    }

    [Fact]
    public async Task ReservationRequiresExactLengthAndReleasesCapacity()
    {
        var limits = new global::RakNet.RakNetLimits(maxOutboundMessages: 1);
        await using ConnectionHarness harness = new(limits);
        var provider = Assert.IsAssignableFrom<IMessageSendReservationProvider>(harness.Adapter);
        MessageSendReservationResult result = await provider.ReserveSendAsync(1);
        var owner = new TrackingMemoryOwner([1, 2]);
        var message = new OwnedMessage(owner, 2);

        await Assert.ThrowsAsync<ArgumentException>(() => result.Reservation!.CommitAsync(message).AsTask());

        Assert.Equal(0, owner.DisposeCount);
        Assert.Equal(MessageAdmissionStatus.Accepted, (await provider.ReserveSendAsync(2)).Status);
        message.Dispose();
    }

    [Fact]
    public async Task ReservationAccountsForExactBytesUntilReleased()
    {
        var limits = new global::RakNet.RakNetLimits(
            maxOutboundMessages: 2,
            maxOutboundBytes: 3);
        await using ConnectionHarness harness = new(limits);
        var provider = Assert.IsAssignableFrom<IMessageSendReservationProvider>(harness.Adapter);
        MessageSendReservationResult first = await provider.ReserveSendAsync(2);

        Assert.Equal(2, first.Reservation!.MessageLength);
        Assert.Equal(2, harness.Connection.PendingReliableBytes);
        Assert.Equal(MessageAdmissionStatus.CapacityExceeded, (await provider.ReserveSendAsync(2)).Status);

        first.Reservation.Dispose();
        Assert.Equal(0, harness.Connection.PendingReliableBytes);
        MessageSendReservationResult fullCapacity = await provider.ReserveSendAsync(3);
        Assert.Equal(MessageAdmissionStatus.Accepted, fullCapacity.Status);
        Assert.Equal(3, harness.Connection.PendingReliableBytes);
        fullCapacity.Reservation!.Dispose();
        Assert.Equal(0, harness.Connection.PendingReliableBytes);
    }

    [Fact]
    public async Task ClosedReservationCommitLeavesOwnershipWithCaller()
    {
        await using ConnectionHarness harness = new();
        var provider = Assert.IsAssignableFrom<IMessageSendReservationProvider>(harness.Adapter);
        MessageSendReservationResult result = await provider.ReserveSendAsync(1);
        harness.Adapter.Abort(MessageConnectionTermination.Aborted());
        var owner = new TrackingMemoryOwner([0xfe]);
        var message = new OwnedMessage(owner, 1);

        MessageAdmissionResult admission = await result.Reservation!.CommitAsync(message);

        Assert.Equal(MessageAdmissionStatus.Closed, admission.Status);
        Assert.Equal(0, owner.DisposeCount);
        message.Dispose();
    }

    [Fact]
    public async Task ReceiveCopiesIntoOwnedMessage()
    {
        await using ConnectionHarness harness = new();
        harness.Deliver(0, 0xfe);

        using OwnedMessage received = Assert.IsType<OwnedMessage>(await harness.Adapter.ReceiveAsync());

        Assert.Equal(new byte[] { 0xfe }, received.Memory.ToArray());
    }

    [Fact]
    public async Task OnlyOneReceiveMayBeActive()
    {
        await using ConnectionHarness harness = new();
        ValueTask<OwnedMessage?> firstReceive = harness.Adapter.ReceiveAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Adapter.ReceiveAsync().AsTask());

        harness.Adapter.Abort(MessageConnectionTermination.Aborted());
        Assert.Null(await firstReceive);
    }

    [Fact]
    public async Task PeerDisconnectMapsToPeerClosed()
    {
        await using ConnectionHarness harness = new(handler: new global::RakNet.DialerConnectionHandler());
        harness.Deliver(0, global::RakNet.Messages.Id.DisconnectNotification);

        Assert.Null(await harness.Adapter.ReceiveAsync());
        Assert.Equal(MessageConnectionTerminationReason.PeerClosed, (await harness.Adapter.Completion).Reason);
    }

    [Fact]
    public async Task PeerTerminationBeforeAbortKeepsPeerResult()
    {
        await using ConnectionHarness harness = new(handler: new global::RakNet.DialerConnectionHandler());
        harness.Deliver(0, global::RakNet.Messages.Id.DisconnectNotification);

        harness.Adapter.Abort(MessageConnectionTermination.ProtocolFailure(
            new InvalidDataException("later local failure")));

        Assert.Equal(MessageConnectionTerminationReason.PeerClosed, (await harness.Adapter.Completion).Reason);
    }

    [Fact]
    public async Task AbortPublishesExactCoreTermination()
    {
        await using ConnectionHarness harness = new();
        MessageConnectionTermination expected = MessageConnectionTermination.ProtocolFailure(
            new InvalidDataException("bad batch"));

        harness.Adapter.Abort(expected);

        Assert.Same(expected, await harness.Adapter.Completion);
    }

    [Fact]
    public async Task FirstTerminationRemainsSticky()
    {
        await using ConnectionHarness harness = new();
        MessageConnectionTermination expected = MessageConnectionTermination.ProtocolFailure(
            new InvalidDataException("bad batch"));

        harness.Adapter.Abort(expected);
        harness.Adapter.Abort(MessageConnectionTermination.Aborted(new Exception("later")));

        Assert.Same(expected, await harness.Adapter.Completion);
    }

    [Fact]
    public async Task CancelledGracefulCloseWaitStillRejectsNewSends()
    {
        await using ConnectionHarness harness = new();
        Assert.Equal(
            MessageAdmissionStatus.Accepted,
            (await harness.Adapter.SendAsync(Owned([0xfe]))).Status);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            harness.Adapter.CloseAsync(TimeSpan.FromMinutes(1), cancellation.Token).AsTask());

        var owner = new TrackingMemoryOwner([0xfe]);
        var rejected = new OwnedMessage(owner, 1);
        Assert.Equal(MessageAdmissionStatus.Closed, (await harness.Adapter.SendAsync(rejected)).Status);
        Assert.Equal(0, owner.DisposeCount);
        rejected.Dispose();
        harness.Adapter.Abort(MessageConnectionTermination.Aborted());
    }

    [Fact]
    public async Task ZeroTimeoutCloseCompletesAsLocalGracefulClose()
    {
        await using ConnectionHarness harness = new();
        Assert.Equal(
            MessageAdmissionStatus.Accepted,
            (await harness.Adapter.SendAsync(Owned([0xfe]))).Status);

        await harness.Adapter.CloseAsync(TimeSpan.Zero);

        Assert.Equal(
            MessageConnectionTerminationReason.LocalGracefulClose,
            (await harness.Adapter.Completion).Reason);
    }

    [Fact]
    public async Task CapacityLogDoesNotContainMessagePayload()
    {
        var limits = new global::RakNet.RakNetLimits(maxOutboundMessages: 1);
        var logger = new CapturingLogger();
        await using ConnectionHarness harness = new(limits, logger: logger);
        Assert.Equal(
            MessageAdmissionStatus.Accepted,
            (await harness.Adapter.SendAsync(Owned([0x41]))).Status);

        var rejected = Owned([0x42]);
        Assert.Equal(MessageAdmissionStatus.CapacityExceeded, (await harness.Adapter.SendAsync(rejected)).Status);

        LogEntry entry = Assert.Single(logger.Entries, value => value.EventId.Id == 4102);
        Assert.DoesNotContain("41", entry.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("42", entry.Message, StringComparison.OrdinalIgnoreCase);
        rejected.Dispose();
    }

    [Fact]
    public async Task DirectIpv4FactoryConnectionCompletesHandshake()
    {
        await using global::RakNet.Listener listener = global::RakNet.ListenConfigExtensions.Listen(
            new global::RakNet.ListenConfig { DisableCookies = true },
            "127.0.0.1:0");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task<global::RakNet.Conn> accept = listener.AcceptAsync(timeout.Token);
        var factory = new RakNetConnectionFactory();

        await using IMessageConnection client = await factory.ConnectAsync(listener.LocalEndPoint, timeout.Token);
        await using global::RakNet.Conn server = await accept;

        Assert.Equal(listener.LocalEndPoint, server.LocalEndPoint);
    }

    [Fact]
    public async Task FactoryRejectsIpv6BeforeDial()
    {
        var factory = new RakNetConnectionFactory();

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            factory.ConnectAsync(new IPEndPoint(IPAddress.IPv6Loopback, 19133)).AsTask());
    }

    [Fact]
    public async Task AsyncDisposalIsIdempotent()
    {
        ConnectionHarness harness = new();

        await harness.Adapter.DisposeAsync();
        await harness.Adapter.DisposeAsync();

        await harness.DisposeSocketsAsync();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public async Task OutOfRangeReservationReturnsCapacityExceeded(int length)
    {
        await using ConnectionHarness harness = new();
        var provider = Assert.IsAssignableFrom<IMessageSendReservationProvider>(harness.Adapter);

        MessageSendReservationResult result = await provider.ReserveSendAsync(length);

        Assert.Equal(MessageAdmissionStatus.CapacityExceeded, result.Status);
        Assert.Null(result.Reservation);
    }

    [Fact]
    public async Task OverLimitSendLeavesOwnershipWithCaller()
    {
        var limits = new global::RakNet.RakNetLimits(maxLogicalMessageBytes: 2);
        await using ConnectionHarness harness = new(limits);
        var owner = new TrackingMemoryOwner([1, 2, 3]);
        var message = new OwnedMessage(owner, 3);

        MessageAdmissionResult result = await harness.Adapter.SendAsync(message);

        Assert.Equal(MessageAdmissionStatus.CapacityExceeded, result.Status);
        Assert.Equal(0, owner.DisposeCount);
        message.Dispose();
    }

    [Fact]
    public void ConnectionOptionsMapBothOutboundByteBudgets()
    {
        var options = new RakNetConnectionOptions
        {
            MaximumPendingReliableBytes = 2048,
            MaximumOutboundBytes = 4096,
        };

        global::RakNet.RakNetLimits limits = options.ToLimits();

        Assert.Equal(2048, limits.MaxPendingReliableBytes);
        Assert.Equal(4096, limits.MaxOutboundBytes);
    }

    [Fact]
    public async Task PendingReliableByteBudgetRejectsBeforeOutboundBudget()
    {
        var limits = new global::RakNet.RakNetLimits(
            maxPendingReliableBytes: 2,
            maxOutboundBytes: 1024 * 1024);
        await using ConnectionHarness harness = new(limits);
        var provider = Assert.IsAssignableFrom<IMessageSendReservationProvider>(harness.Adapter);

        MessageSendReservationResult result = await provider.ReserveSendAsync(3);

        Assert.Equal(MessageAdmissionStatus.CapacityExceeded, result.Status);
    }

    [Fact]
    public async Task OutboundByteBudgetRejectsBeforePendingReliableBudget()
    {
        var limits = new global::RakNet.RakNetLimits(
            maxPendingReliableBytes: 1024 * 1024,
            maxOutboundBytes: 2);
        await using ConnectionHarness harness = new(limits);
        var provider = Assert.IsAssignableFrom<IMessageSendReservationProvider>(harness.Adapter);

        MessageSendReservationResult result = await provider.ReserveSendAsync(3);

        Assert.Equal(MessageAdmissionStatus.CapacityExceeded, result.Status);
    }

    [Fact]
    public async Task InboundCapacityExceededMapsToTransportFailure()
    {
        var limits = new global::RakNet.RakNetLimits(maxInboundMessages: 1);
        await using ConnectionHarness harness = new(limits);
        harness.Deliver(0, 0xfe);
        harness.Deliver(1, 0xfe);

        MessageConnectionTermination termination = await harness.Adapter.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(MessageConnectionTerminationReason.TransportFailure, termination.Reason);
    }

    private static OwnedMessage Owned(byte[] bytes)
    {
        return new OwnedMessage(new TrackingMemoryOwner(bytes), bytes.Length);
    }

    private sealed class ConnectionHarness : IAsyncDisposable
    {
        private readonly Socket _local;
        private readonly Socket _peer;

        public ConnectionHarness(
            global::RakNet.RakNetLimits? limits = null,
            global::RakNet.IConnectionHandler? handler = null,
            ILogger<RakNetMessageConnection>? logger = null)
        {
            _local = BoundSocket();
            _peer = BoundSocket();
            _peer.ReceiveTimeout = 2000;
            Connection = new global::RakNet.Conn(
                _local,
                (IPEndPoint)_peer.LocalEndPoint!,
                1200,
                handler ?? new UnhandledConnectionHandler(),
                ownsSocket: false,
                limits: limits);
            Adapter = new RakNetMessageConnection(
                Connection,
                logger ?? NullLogger<RakNetMessageConnection>.Instance);
        }

        public global::RakNet.Conn Connection { get; }

        public RakNetMessageConnection Adapter { get; }

        public void Deliver(uint sequence, byte payload)
        {
            var packet = new global::RakNet.Packet
            {
                Reliability = global::RakNet.Reliability.Unreliable,
                Content = [payload]
            };
            using var buffer = new MemoryStream();
            buffer.WriteByte(global::RakNet.RakNetConstants.BitFlagDatagram);
            global::RakNet.Binary.WriteUint24(buffer, sequence);
            packet.Write(buffer);
            Connection.Receive(buffer.ToArray());
        }

        public byte[] ReceivePeerDatagram()
        {
            byte[] buffer = new byte[1500];
            EndPoint source = new IPEndPoint(IPAddress.Any, 0);
            int length = _peer.ReceiveFrom(buffer, ref source);
            return buffer[..length];
        }

        public async ValueTask DisposeAsync()
        {
            await Adapter.DisposeAsync();
            await DisposeSocketsAsync();
        }

        public ValueTask DisposeSocketsAsync()
        {
            _local.Dispose();
            _peer.Dispose();
            return ValueTask.CompletedTask;
        }

        private static Socket BoundSocket()
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            return socket;
        }
    }

    private sealed record LogEntry(EventId EventId, LogLevel Level, string Message, Exception? Exception);

    private sealed class CapturingLogger : ILogger<RakNetMessageConnection>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(eventId, logLevel, formatter(state, exception), exception));
        }
    }

    private sealed class UnhandledConnectionHandler : global::RakNet.IConnectionHandler
    {
        public (bool handled, Exception? error) Handle(global::RakNet.Conn conn, byte[] b) => (false, null);

        public bool LimitsEnabled() => true;

        public void Close(global::RakNet.Conn conn)
        {
        }
    }

    private sealed class TrackingMemoryOwner : IMemoryOwner<byte>
    {
        private readonly byte[] _buffer;

        public TrackingMemoryOwner(byte[] content)
        {
            _buffer = content;
        }

        public int DisposeCount { get; private set; }

        public Memory<byte> Memory => _buffer;

        public void Dispose()
        {
            DisposeCount++;
        }
    }
}
