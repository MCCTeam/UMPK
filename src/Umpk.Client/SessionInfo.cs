using Umpk.Protocol.Java;

namespace Umpk.Client;

/// <summary>Immutable facts about the negotiated session: the profile, server endpoint, and version. Server-provided metadata that arrives after join lives on the self state and other state modules.</summary>
public sealed record SessionInfo
{
    /// <summary>The profile the client logged in as.</summary>
    public required GameProfile Profile { get; init; }

    /// <summary>The server endpoint dialed for this session.</summary>
    public required ServerEndpoint Endpoint { get; init; }

    /// <summary>The negotiated protocol version.</summary>
    public required JavaVersion Version { get; init; }

    /// <summary>The current protocol phase.</summary>
    public ProtocolPhase Phase { get; init; } = ProtocolPhase.Handshake;
}

/// <summary>The reason a session ended.</summary>
public sealed record DisconnectInfo
{
    /// <summary>The transport-level close reason.</summary>
    public required CloseReason Reason { get; init; }

    /// <summary>The server-provided disconnect message, when one was sent.</summary>
    public Umpk.Text.Component? Message { get; init; }

    /// <summary>The underlying fault, when the session ended with an error.</summary>
    public Exception? Fault { get; init; }

    /// <summary>True when the local side initiated the disconnect.</summary>
    public bool WasLocal => Reason == CloseReason.Local;

    /// <summary>This disconnect classified into the cases a consumer branches on. See <see cref="DisconnectKind"/> for why a kick and a transport fault must not be one bucket.</summary>
    public DisconnectKind Kind => Classify(Reason, WasLocal);

    /// <summary>True when the server deliberately disconnected us. Distinct from a broken connection: a kick is a decision, it normally carries the server's own <see cref="Message"/>, and reconnecting straight into one is how a client ends up in a kick loop.</summary>
    public bool IsKick => Kind == DisconnectKind.Kick;

    /// <summary>Classifies a close reason. Public so a plugin or host can classify a <see cref="CloseReason"/> the same way this record does without duplicating the rule.</summary>
    public static DisconnectKind Classify(CloseReason reason, bool wasLocal) => wasLocal
        ? DisconnectKind.LocalStop
        : reason switch
        {
            CloseReason.Local => DisconnectKind.LocalStop,
            CloseReason.Cancelled => DisconnectKind.Cancelled,
            CloseReason.DisconnectMessage => DisconnectKind.Kick,
            CloseReason.Transferred => DisconnectKind.Transferred,
            _ => DisconnectKind.ConnectionLost,
        };
}
