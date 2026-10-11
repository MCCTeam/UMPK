namespace Umpk.Transport;

/// <summary>A connection that can reserve bounded capacity for one complete outbound message before its bytes are produced.</summary>
/// <remarks>Reservations let Bedrock egress claim queue room before encoding advances persistent cipher state; a connection that cannot bound its queues does not need to implement this interface.</remarks>
public interface IMessageSendReservationProvider
{
    /// <summary>Attempts to hold queue capacity for one complete outbound message of <paramref name="messageLength"/> bytes.</summary>
    /// <param name="messageLength">The exact complete-message length in bytes to hold, which must be positive and within the connection bounds.</param>
    /// <param name="cancellationToken">Cancels the wait for capacity; a cancelled wait consumes nothing and throws instead of returning a refusal.</param>
    /// <returns>The reservation outcome; the reservation is non-null only when <see cref="MessageSendReservationResult.Status"/> is <see cref="MessageAdmissionStatus.Accepted"/>.</returns>
    /// <exception cref="OperationCanceledException">The wait was cancelled before capacity was granted.</exception>
    ValueTask<MessageSendReservationResult> ReserveSendAsync(int messageLength, CancellationToken cancellationToken = default);
}
