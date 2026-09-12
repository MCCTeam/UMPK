namespace Umpk.Client;

/// <summary>The high-level lifecycle state of a <see cref="UmpkClient"/>. <see cref="Authenticating"/> and <see cref="Reconnecting"/> exist so the whole lifecycle has one vocabulary, but <see cref="UmpkClient.Status"/> itself never reports either: a bare client has no authentication step of its own (that lives behind <c>ISessionAuthenticator</c>, outside <see cref="UmpkClient.ConnectAsync"/>) and never retries a dead session. Both are supervisor-only, reported by <c>Umpk.Client.Supervision.UmpkClientSupervisor</c>.</summary>
public enum ClientStatus
{
    /// <summary>Built but not yet connected.</summary>
    Created = 0,

    /// <summary>An account/session authentication step is in progress. Supervisor-only.</summary>
    Authenticating = 1,

    /// <summary>Dialing, handshaking, and logging in.</summary>
    Connecting = 2,

    /// <summary>In the configuration phase (registry sync, known packs).</summary>
    Configuring = 3,

    /// <summary>In the play phase.</summary>
    Playing = 4,

    /// <summary>A reconnect attempt is in progress. Supervisor-only.</summary>
    Reconnecting = 5,

    /// <summary>The session has ended.</summary>
    Disconnected = 6,
}
