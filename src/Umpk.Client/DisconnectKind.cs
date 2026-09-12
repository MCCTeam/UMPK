using Umpk.Protocol.Java;

namespace Umpk.Client;

/// <summary>What ended a session, classified for the decisions a host or a reconnect policy actually make. <see cref="CloseReason"/> is a transport-level enum with seven values; this collapses it into the four cases a consumer branches on, and in particular separates a SERVER KICK (a deliberate decision by the remote side, which arrives with the server's own text) from a TRANSPORT FAULT (the connection broke, and there is no message because nobody sent one). <see cref="DisconnectInfo.Classify"/> centralizes the mapping from the seven transport-level <see cref="CloseReason"/> values to these host-facing outcomes.</summary>
public enum DisconnectKind
{
    /// <summary>Local code asked for the disconnect (a clean stop, a reconnect, or host shutdown).</summary>
    LocalStop,

    /// <summary>The operation was cancelled.</summary>
    Cancelled,

    /// <summary>The server sent a disconnect packet: a deliberate kick, usually carrying a reason.</summary>
    Kick,

    /// <summary>The server handed the connection to another host (1.20.5+ transfer).</summary>
    Transferred,

    /// <summary>The connection broke: end of stream, idle timeout, or a protocol violation.</summary>
    ConnectionLost,
}
