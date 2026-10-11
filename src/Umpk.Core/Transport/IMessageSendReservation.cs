namespace Umpk.Transport;

/// <summary>A held reservation for exactly one complete outbound message, released by committing or disposing.</summary>
/// <remarks>A reservation pins bounded queue capacity before encoding or encryption advances, so a large message cannot consume cipher state it has no room to send; disposing without committing returns the held capacity.</remarks>
public interface IMessageSendReservation : IDisposable
{
    /// <summary>Gets the exact complete-message length in bytes held at the connection boundary.</summary>
    int MessageLength { get; }

    /// <summary>Commits one message of exactly <see cref="MessageLength"/> bytes to the held capacity without a caller cancellation point.</summary>
    /// <param name="message">The message to commit; ownership transfers only when the result reports <see cref="MessageAdmissionStatus.Accepted"/>.</param>
    /// <returns>The admission outcome, where a length mismatch throws instead of returning a refusal.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="message"/> length differs from <see cref="MessageLength"/>.</exception>
    /// <exception cref="InvalidOperationException">The reservation was already committed or disposed.</exception>
    ValueTask<MessageAdmissionResult> CommitAsync(OwnedMessage message);
}
