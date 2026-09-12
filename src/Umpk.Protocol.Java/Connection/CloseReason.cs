namespace Umpk.Protocol.Java;

/// <summary>Why a <see cref="JavaConnection"/> closed. Carried by <see cref="ConnectionClosedException"/>.</summary>
public enum CloseReason
{
    /// <summary>Local code requested the close.</summary>
    Local,

    /// <summary>The socket reached end of stream.</summary>
    SocketEof,

    /// <summary>The peer sent a disconnect packet (surfaced as data where read as termination).</summary>
    DisconnectMessage,

    /// <summary>A protocol violation was detected while framing or decoding.</summary>
    ProtocolViolation,

    /// <summary>The operation was cancelled.</summary>
    Cancelled,

    /// <summary>The connection was transferred to another host (1.20.5+ transfer packet).</summary>
    Transferred,

    /// <summary>The read-idle timeout elapsed with no inbound traffic.</summary>
    IdleTimeout,
}
