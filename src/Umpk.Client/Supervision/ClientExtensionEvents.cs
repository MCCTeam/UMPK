using Umpk.Client.Plugins;

namespace Umpk.Client;

/// <summary>Carries the freshly built, still-UNCONNECTED <see cref="UmpkClient"/> for <see cref="ClientExtensionContext.SessionCreated"/>. This fires before the client dials, so a recorder can call <see cref="UmpkClient.ObservePackets"/> early enough to capture the handshake, login and configuration frames of the session about to start. The only supported use is subscribing to that raw feed (or otherwise reading the client): it grants no power <see cref="ClientPluginContext.Client"/> does not already grant, and the client is not yet safe to drive (it has not connected).</summary>
public sealed class ClientSessionCreatedEventArgs : EventArgs
{
    internal ClientSessionCreatedEventArgs(UmpkClient client) => Client = client;

    /// <summary>The unconnected client this session is about to dial.</summary>
    public UmpkClient Client { get; }
}

/// <summary>Carries the live <see cref="ClientPluginContext"/> for <see cref="ClientExtensionContext.SessionStarted"/>, raised once play is reached for this connection. Everything acquired through it (event subscriptions, scheduled work, movement leases, plugin channels) is torn down automatically when the session ends; see <see cref="ClientExtensionContext.SessionEnded"/>.</summary>
public sealed class ClientSessionStartedEventArgs : EventArgs
{
    internal ClientSessionStartedEventArgs(ClientPluginContext session) => Session = session;

    /// <summary>The live session context.</summary>
    public ClientPluginContext Session { get; }
}

/// <summary>Raised for <see cref="ClientExtensionContext.SessionEnded"/> when the live session ends, whether by a clean local stop, a server-initiated close, or a transport fault. <see cref="Disconnect"/> carries the reason when one is known.</summary>
public sealed class ClientSessionEndedEventArgs : EventArgs
{
    internal ClientSessionEndedEventArgs(DisconnectInfo? disconnect) => Disconnect = disconnect;

    /// <summary>Why the session ended, when known.</summary>
    public DisconnectInfo? Disconnect { get; }
}
