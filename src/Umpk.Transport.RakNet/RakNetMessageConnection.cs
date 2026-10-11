using System.Buffers;
using Microsoft.Extensions.Logging;
using Umpk.Transport;

namespace Umpk.Transport.RakNet;

/// <summary>Adapts one fork RakNet connection to the owned-message connection seam with copy-on-receive buffers and reserve-then-commit sends.</summary>
/// <remarks>The fork owns the socket and retry timers while this adapter copies received bytes into caller-owned messages and observes fork completion. Send admission reports local acceptance rather than peer delivery, and local close completion does not by itself prove the peer observed EOF.</remarks>
internal sealed class RakNetMessageConnection : IMessageConnection, IMessageSendReservationProvider
{
    private readonly global::RakNet.Conn _connection;
    private readonly ILogger _logger;
    private readonly TaskCompletionSource<MessageConnectionTermination> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _completionObserver;
    private MessageConnectionTermination? _requestedAbort;

    internal RakNetMessageConnection(global::RakNet.Conn connection, ILogger logger)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _completionObserver = ObserveCompletionAsync();
    }

    public Task<MessageConnectionTermination> Completion => _completion.Task;

    /// <summary>Receives the next fork packet by copying it into pooled memory and transferring the resulting message to the caller.</summary>
    /// <remarks>The fork packet buffer stays with the fork while the returned message belongs to the caller, and a null result reports termination with no further messages rather than an empty message.</remarks>
    public async ValueTask<OwnedMessage?> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        byte[]? packet = await _connection.ReceiveAsync(cancellationToken).ConfigureAwait(false);
        if (packet is null)
            return null;

        IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(packet.Length);
        packet.CopyTo(owner.Memory);
        return new OwnedMessage(owner, packet.Length);
    }

    /// <summary>Sends one message by reserving capacity first and then committing the message through the single-use reservation.</summary>
    /// <remarks>Ownership moves to the connection only when the result reports acceptance, so a closed or saturated outcome leaves the caller message with the caller.</remarks>
    public async ValueTask<MessageAdmissionResult> SendAsync(
        OwnedMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        MessageSendReservationResult reservation = await ReserveSendAsync(message.Length, cancellationToken).ConfigureAwait(false);
        if (!reservation.IsReserved)
        {
            return reservation.Status == MessageAdmissionStatus.Closed
                ? MessageAdmissionResult.Closed
                : MessageAdmissionResult.CapacityExceeded;
        }

        using IMessageSendReservation sendReservation = reservation.Reservation!;
        return await sendReservation.CommitAsync(message).ConfigureAwait(false);
    }

    /// <summary>Reserves bounded send capacity for exactly one message of the requested length without encoding or sending it.</summary>
    /// <remarks>A non-positive or over-limit length and fork queue saturation both report capacity refusal rather than throwing, while a torn-down connection reports closure so the caller can tell capacity from teardown.</remarks>
    public ValueTask<MessageSendReservationResult> ReserveSendAsync(
        int messageLength,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        global::RakNet.RakNetWriteReservationResult result;
        try
        {
            result = _connection.TryReserveWrite(messageLength);
        }
        catch (ArgumentOutOfRangeException)
        {
            // The fork rejects non-positive and over-limit lengths by throwing.
            // Report a typed rejection so callers can tell capacity from programmer error.
            return CapacityExceeded();
        }
        return result.Status switch
        {
            global::RakNet.RakNetSendStatus.Accepted => ValueTask.FromResult(
                MessageSendReservationResult.Accepted(
                    new SendReservation(_connection, result.Reservation!, messageLength))),
            global::RakNet.RakNetSendStatus.Closed => ValueTask.FromResult(MessageSendReservationResult.Closed),
            global::RakNet.RakNetSendStatus.CapacityExceeded => CapacityExceeded(),
            _ => throw new InvalidOperationException($"unknown RakNet admission status {result.Status}")
        };
    }

    /// <summary>Closes the fork connection gracefully and then joins the completion observer before returning to the caller.</summary>
    /// <remarks>Completion reports that local close work finished rather than proving the peer observed EOF, and accepted sends drain only within the requested bound.</remarks>
    public async ValueTask CloseAsync(TimeSpan drainTimeout, CancellationToken cancellationToken = default)
    {
        await _connection.CloseAsync(drainTimeout, cancellationToken).ConfigureAwait(false);
        await _completionObserver.ConfigureAwait(false);
    }

    /// <summary>Aborts the connection immediately while keeping the first requested termination for the completion outcome.</summary>
    /// <remarks>The fork receives the retained cause so concurrent abort requests use one requested outcome; the fork's terminal completion remains the source of the final mapped outcome.</remarks>
    public void Abort(MessageConnectionTermination termination)
    {
        ArgumentNullException.ThrowIfNull(termination);
        MessageConnectionTermination requested = Interlocked.CompareExchange(
            ref _requestedAbort,
            termination,
            null) ?? termination;
        _connection.Abort(requested.Cause);
    }

    /// <summary>Disposes the fork connection and then joins the completion observer before returning to the caller.</summary>
    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
        await _completionObserver.ConfigureAwait(false);
    }

    private ValueTask<MessageSendReservationResult> CapacityExceeded()
    {
        RakNetLog.AdmissionExceeded(_logger);
        return ValueTask.FromResult(MessageSendReservationResult.CapacityExceeded);
    }

    private async Task ObserveCompletionAsync()
    {
        global::RakNet.RakNetTermination termination = await _connection.Completion.ConfigureAwait(false);
        RakNetLog.ConnectionEnded(_logger, termination.Reason);
        _completion.TrySetResult(MapTermination(termination));
    }

    // Fork CapacityExceeded termination means the bounded inbound queue overflowed.
    // It maps to TransportFailure because Core has no distinct capacity terminal cause.
    /// <summary>Maps one fork termination reason to the core connection termination for the completion observer.</summary>
    /// <remarks>Inbound queue saturation folds into transport failure because core has no distinct capacity terminal code, using the fork cause when present and otherwise a new I/O failure that names the fork reason.</remarks>
    private MessageConnectionTermination MapTermination(global::RakNet.RakNetTermination termination)
    {
        return termination.Reason switch
        {
            global::RakNet.RakNetTerminationReason.PeerClosed => MessageConnectionTermination.PeerClosed,
            global::RakNet.RakNetTerminationReason.LocalClosed => MessageConnectionTermination.LocalGracefulClose,
            global::RakNet.RakNetTerminationReason.Aborted => Volatile.Read(ref _requestedAbort)
                ?? MessageConnectionTermination.Aborted(termination.Cause),
            global::RakNet.RakNetTerminationReason.Cancelled => MessageConnectionTermination.Cancelled(termination.Cause as OperationCanceledException),
            global::RakNet.RakNetTerminationReason.ProtocolFailure => MessageConnectionTermination.ProtocolFailure(
                termination.Cause ?? new InvalidDataException("RakNet protocol failure")),
            global::RakNet.RakNetTerminationReason.TransportFailure or
            global::RakNet.RakNetTerminationReason.TimedOut or
            global::RakNet.RakNetTerminationReason.CapacityExceeded => MessageConnectionTermination.TransportFailure(
                termination.Cause ?? new IOException($"RakNet connection ended with {termination.Reason}")),
            _ => MessageConnectionTermination.TransportFailure(
                termination.Cause ?? new IOException($"unknown RakNet termination {termination.Reason}"))
        };
    }

    /// <summary>Holds one fork write reservation for exactly one message length until a single commit or disposal releases it.</summary>
    /// <remarks>The adapter never encodes into the reservation before CommitAsync, and disposal without a commit returns the held capacity without sending.</remarks>
    private sealed class SendReservation : IMessageSendReservation
    {
        private readonly global::RakNet.Conn _connection;
        private global::RakNet.RakNetWriteReservation? _reservation;

        public SendReservation(
            global::RakNet.Conn connection,
            global::RakNet.RakNetWriteReservation reservation,
            int messageLength)
        {
            _connection = connection;
            _reservation = reservation;
            MessageLength = messageLength;
        }

        public int MessageLength { get; }

        /// <summary>Commits one message of the reserved length exactly once after checking connection completion before touching the fork reservation.</summary>
        /// <remarks>The reservation is consumed atomically so a second commit throws. A length mismatch or a completed connection releases the handle without sending or consuming the caller message, a successful commit disposes the caller message, and an I/O failure during commit releases the handle and reports closure while the caller retains the message and the fork may be aborted.</remarks>
        public ValueTask<MessageAdmissionResult> CommitAsync(OwnedMessage message)
        {
            ArgumentNullException.ThrowIfNull(message);
            global::RakNet.RakNetWriteReservation reservation = Interlocked.Exchange(ref _reservation, null)
                ?? throw new InvalidOperationException("the send reservation has already been consumed");
            if (message.Length != MessageLength)
            {
                reservation.Dispose();
                throw new ArgumentException("message length does not match the reservation", nameof(message));
            }
            if (_connection.Completion.IsCompleted)
            {
                reservation.Dispose();
                return ValueTask.FromResult(MessageAdmissionResult.Closed);
            }

            try
            {
                reservation.Commit(message.Memory);
                message.Dispose();
                return ValueTask.FromResult(MessageAdmissionResult.Accepted);
            }
            catch (IOException) when (_connection.Completion.IsCompleted)
            {
                reservation.Dispose();
                return ValueTask.FromResult(MessageAdmissionResult.Closed);
            }
            catch (IOException exception)
            {
                // The fork terminated after admission but the adapter completion has not published yet.
                // Release the handle and abort deterministically; the caller keeps the message.
                reservation.Dispose();
                _connection.Abort(exception);
                return ValueTask.FromResult(MessageAdmissionResult.Closed);
            }
        }

        /// <summary>Releases the held fork reservation without sending when CommitAsync has not already consumed it.</summary>
        public void Dispose()
        {
            Interlocked.Exchange(ref _reservation, null)?.Dispose();
        }
    }
}
