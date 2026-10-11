namespace Umpk.Transport;

/// <summary>The outcome of one send-capacity reservation, carrying the live reservation only when capacity was granted.</summary>
public readonly struct MessageSendReservationResult
{
    private MessageSendReservationResult(MessageAdmissionStatus status, IMessageSendReservation? reservation)
    {
        Status = status;
        Reservation = reservation;
    }

    /// <summary>Gets the reservation outcome.</summary>
    public MessageAdmissionStatus Status { get; }

    /// <summary>Gets the live reservation when <see cref="Status"/> is <see cref="MessageAdmissionStatus.Accepted"/>, or <see langword="null"/> for every refusal.</summary>
    public IMessageSendReservation? Reservation { get; }

    /// <summary>Gets whether capacity was granted and a live reservation is held.</summary>
    /// <value><see langword="true"/> only when <see cref="Status"/> is <see cref="MessageAdmissionStatus.Accepted"/> with a non-null <see cref="Reservation"/>.</value>
    public bool IsReserved => Status == MessageAdmissionStatus.Accepted && Reservation is not null;

    /// <summary>Creates a granted outcome holding a live reservation.</summary>
    /// <param name="reservation">The live reservation pinning capacity for one message.</param>
    /// <returns>The granted outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reservation"/> is <see langword="null"/>.</exception>
    public static MessageSendReservationResult Accepted(IMessageSendReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        return new MessageSendReservationResult(MessageAdmissionStatus.Accepted, reservation);
    }

    /// <summary>Gets the closed outcome, which holds no reservation and leaves the message with the caller.</summary>
    public static MessageSendReservationResult Closed { get; } = new(MessageAdmissionStatus.Closed, null);

    /// <summary>Gets the capacity-exceeded outcome, which holds no reservation and leaves the message with the caller.</summary>
    public static MessageSendReservationResult CapacityExceeded { get; } = new(MessageAdmissionStatus.CapacityExceeded, null);
}
