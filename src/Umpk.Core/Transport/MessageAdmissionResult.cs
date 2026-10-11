namespace Umpk.Transport;

/// <summary>The admission outcome for one outbound message at the connection boundary.</summary>
public enum MessageAdmissionStatus
{
    /// <summary>No admission decision was produced; the default value is never a successful send.</summary>
    Unspecified,
    /// <summary>The message was accepted and buffer ownership transferred to the connection.</summary>
    Accepted,
    /// <summary>The connection is closed, so the message was refused and the caller keeps ownership.</summary>
    Closed,
    /// <summary>The bounded queue had no room, so the message was refused and the caller keeps ownership.</summary>
    CapacityExceeded
}

/// <summary>The admission result for one outbound message, stating whether buffer ownership transferred.</summary>
public readonly struct MessageAdmissionResult
{
    private MessageAdmissionResult(MessageAdmissionStatus status)
    {
        Status = status;
    }

    /// <summary>Gets the admission outcome for the message.</summary>
    public MessageAdmissionStatus Status { get; }

    /// <summary>Gets the accepted outcome, which transfers buffer ownership to the connection.</summary>
    public static MessageAdmissionResult Accepted { get; } = new(MessageAdmissionStatus.Accepted);

    /// <summary>Gets the closed outcome, which leaves buffer ownership with the caller.</summary>
    public static MessageAdmissionResult Closed { get; } = new(MessageAdmissionStatus.Closed);

    /// <summary>Gets the capacity-exceeded outcome, which leaves buffer ownership with the caller.</summary>
    public static MessageAdmissionResult CapacityExceeded { get; } = new(MessageAdmissionStatus.CapacityExceeded);

    /// <summary>Gets whether the connection took buffer ownership, which holds only for <see cref="MessageAdmissionStatus.Accepted"/>.</summary>
    /// <value><see langword="true"/> when <see cref="Status"/> is <see cref="MessageAdmissionStatus.Accepted"/>; otherwise <see langword="false"/>.</value>
    public bool OwnershipTransferred => Status == MessageAdmissionStatus.Accepted;
}
