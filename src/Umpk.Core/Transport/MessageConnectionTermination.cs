namespace Umpk.Transport;

/// <summary>Why a message connection reached its terminal state.</summary>
public enum MessageConnectionTerminationReason
{
    /// <summary>The termination cause is not known; never used for a healthy close.</summary>
    Unknown,
    /// <summary>The peer closed the session as observed by the transport.</summary>
    PeerClosed,
    /// <summary>The local side completed a graceful close and drained within its deadline.</summary>
    LocalGracefulClose,
    /// <summary>The local side aborted the connection, discarding undrained work.</summary>
    Aborted,
    /// <summary>A local cancellation ended the connection or an operation that owned it.</summary>
    Cancelled,
    /// <summary>The peer or local framing violated the protocol and the session could not continue.</summary>
    ProtocolFailure,
    /// <summary>The underlying transport failed, timed out, or exceeded a bounded queue.</summary>
    TransportFailure
}

/// <summary>The single terminal outcome of a message connection, pairing a reason with its optional cause.</summary>
public sealed class MessageConnectionTermination
{
    private MessageConnectionTermination(MessageConnectionTerminationReason reason, Exception? cause)
    {
        Reason = reason;
        Cause = cause;
    }

    /// <summary>Gets the shared peer-closed outcome, which carries no cause.</summary>
    public static MessageConnectionTermination PeerClosed { get; } = new(MessageConnectionTerminationReason.PeerClosed, null);

    /// <summary>Gets the shared graceful-close outcome, which carries no cause.</summary>
    public static MessageConnectionTermination LocalGracefulClose { get; } = new(MessageConnectionTerminationReason.LocalGracefulClose, null);

    /// <summary>Gets why the connection terminated.</summary>
    public MessageConnectionTerminationReason Reason { get; }

    /// <summary>Gets the underlying failure when <see cref="Reason"/> is a failure, or <see langword="null"/> for clean closes and aborts without a recorded cause.</summary>
    public Exception? Cause { get; }

    /// <summary>Creates an abort outcome with an optional cause.</summary>
    /// <param name="cause">The error that prompted the abort, or <see langword="null"/> when the abort was deliberate.</param>
    /// <returns>The abort outcome.</returns>
    public static MessageConnectionTermination Aborted(Exception? cause = null)
    {
        return new MessageConnectionTermination(MessageConnectionTerminationReason.Aborted, cause);
    }

    /// <summary>Creates a cancellation outcome with an optional cause.</summary>
    /// <param name="cause">The cancellation that ended the connection or its owning operation, or <see langword="null"/> when it was not captured.</param>
    /// <returns>The cancellation outcome.</returns>
    public static MessageConnectionTermination Cancelled(OperationCanceledException? cause = null)
    {
        return new MessageConnectionTermination(MessageConnectionTerminationReason.Cancelled, cause);
    }

    /// <summary>Creates a protocol-failure outcome preserving the violation that ended the session.</summary>
    /// <param name="cause">The framing or protocol violation that ended the session.</param>
    /// <returns>The protocol-failure outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="cause"/> is <see langword="null"/>.</exception>
    public static MessageConnectionTermination ProtocolFailure(Exception cause)
    {
        ArgumentNullException.ThrowIfNull(cause);
        return new MessageConnectionTermination(MessageConnectionTerminationReason.ProtocolFailure, cause);
    }

    /// <summary>Creates a transport-failure outcome preserving the fault that ended the session.</summary>
    /// <param name="cause">The transport fault that ended the session.</param>
    /// <returns>The transport-failure outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="cause"/> is <see langword="null"/>.</exception>
    public static MessageConnectionTermination TransportFailure(Exception cause)
    {
        ArgumentNullException.ThrowIfNull(cause);
        return new MessageConnectionTermination(MessageConnectionTerminationReason.TransportFailure, cause);
    }
}
