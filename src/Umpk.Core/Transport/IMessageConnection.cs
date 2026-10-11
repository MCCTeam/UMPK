namespace Umpk.Transport;

/// <summary>A protocol-neutral ordered message connection with explicit buffer ownership and a terminal outcome that never reports teardown as a successful connection.</summary>
/// <remarks>Send and receive admission decisions are separate from the terminal <see cref="Completion"/> task, so a caller can tell accepted sends, bounded overload, and connection teardown apart without guessing from exceptions.</remarks>
public interface IMessageConnection : IAsyncDisposable
{
    /// <summary>Gets the task that completes once with the single terminal outcome for this established connection.</summary>
    /// <value>The terminal outcome, kept separate from connection establishment and receive availability.</value>
    Task<MessageConnectionTermination> Completion { get; }

    /// <summary>Receives the next inbound message, transferring buffer ownership to the caller.</summary>
    /// <param name="cancellationToken">Cancels the wait for the next message without tearing the connection down.</param>
    /// <returns>The next message, which the caller owns and must dispose, or <see langword="null"/> once the connection has terminated and no further messages will arrive.</returns>
    /// <exception cref="InvalidOperationException">Another receive is already active on this connection.</exception>
    ValueTask<OwnedMessage?> ReceiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends one message, transferring buffer ownership to the connection only when the result reports <see cref="MessageAdmissionStatus.Accepted"/>.</summary>
    /// <param name="message">The message to send; the caller keeps ownership unless the returned result reports <see cref="MessageAdmissionStatus.Accepted"/>.</param>
    /// <param name="cancellationToken">Cancels the admission wait before capacity is granted; once the message is accepted the send itself is no longer cancellable by this token.</param>
    /// <returns>The admission outcome describing whether ownership transferred.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    ValueTask<MessageAdmissionResult> SendAsync(OwnedMessage message, CancellationToken cancellationToken = default);

    /// <summary>Closes the connection gracefully, waiting up to <paramref name="drainTimeout"/> for accepted work to drain before tearing the transport down.</summary>
    /// <param name="drainTimeout">The upper bound for draining accepted sends and receives; expiry aborts the remainder rather than blocking indefinitely.</param>
    /// <param name="cancellationToken">Cancels this caller's wait without reversing the close; the connection still rejects new work and continues its bounded drain.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="drainTimeout"/> is negative.</exception>
    /// <exception cref="OperationCanceledException">The caller cancelled its wait before the close reached a terminal outcome.</exception>
    ValueTask CloseAsync(TimeSpan drainTimeout, CancellationToken cancellationToken = default);

    /// <summary>Aborts the connection immediately with the given terminal outcome, waking every waiter without further delivery.</summary>
    /// <param name="termination">The terminal outcome reported to <see cref="Completion"/> and to any pending waiter.</param>
    /// <exception cref="ArgumentNullException"><paramref name="termination"/> is <see langword="null"/>.</exception>
    void Abort(MessageConnectionTermination termination);
}
