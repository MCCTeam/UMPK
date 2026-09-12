using Microsoft.Extensions.Logging;
using Umpk.Client.Plugins;
using Umpk.Hosting;

namespace Umpk.Client;

/// <summary>
/// The services handed to an <see cref="IClientExtension"/> at <see cref="IClientExtension.ActivateAsync"/>. It spans two lifetimes: <see cref="Supervisor"/>, <see cref="Logger"/> and <see cref="Cron"/> live for as long as the extension is registered, while <see cref="Session"/> and the <see cref="SessionCreated"/> / <see cref="SessionStarted"/> / <see cref="SessionEnded"/> events expose the per-session (reconnect-aware) surface underneath: one <see cref="UmpkClient"/> per connection, replaced on every reconnect.
/// <para>Layering. A <see cref="Plugins.IClientPlugin"/> added through <see cref="UmpkClient.Plugins"/> lives for ONE session; an <see cref="IClientExtension"/> added through <see cref="UmpkClientSupervisor.Extensions"/> lives for the whole supervised run and sees every session in turn. Use a plugin when the client instance itself is the whole scope; use an extension when state or scheduling needs to survive a reconnect.</para>
/// </summary>
public sealed class ClientExtensionContext
{
    private readonly Lock _gate = new();
    private ClientPluginContext? _session;

    internal ClientExtensionContext(
        string id, UmpkClientSupervisor supervisor, ILogger logger, ICronScheduler cron, CancellationToken deactivated)
    {
        Id = id;
        Supervisor = supervisor;
        Logger = logger;
        Cron = cron;
        Deactivated = deactivated;
    }

    /// <summary>Raised when a fresh, still-unconnected client is built for a new session (initial connect or a reconnect).</summary>
    public event EventHandler<ClientSessionCreatedEventArgs>? SessionCreated;

    /// <summary>Raised once play is reached for a session, carrying its live <see cref="ClientPluginContext"/>.</summary>
    public event EventHandler<ClientSessionStartedEventArgs>? SessionStarted;

    /// <summary>Raised when the live session ends, before that session's own per-session teardown runs.</summary>
    public event EventHandler<ClientSessionEndedEventArgs>? SessionEnded;

    /// <summary>The extension's own id.</summary>
    public string Id { get; }

    /// <summary>The supervisor this extension is registered on.</summary>
    public UmpkClientSupervisor Supervisor { get; }

    /// <summary>A per-extension logger.</summary>
    public ILogger Logger { get; }

    /// <summary>Wall-clock scheduling that keeps running while there is no session at all, and is stopped and disposed automatically when this extension deactivates.</summary>
    public ICronScheduler Cron { get; }

    /// <summary>The current live session's context, or null between sessions.</summary>
    public ClientPluginContext? Session
    {
        get
        {
            lock (_gate)
                return _session;

        }
    }

    /// <summary>True while a live session is attached.</summary>
    public bool InSession => Session is not null;

    /// <summary>Fires when this extension is removed or the owning supervisor stops.</summary>
    public CancellationToken Deactivated { get; }

    internal void RaiseSessionCreated(UmpkClient client) => Invoke(SessionCreated, new ClientSessionCreatedEventArgs(client));

    internal void RaiseSessionStarted(ClientPluginContext session)
    {
        lock (_gate)
            _session = session;

        Invoke(SessionStarted, new ClientSessionStartedEventArgs(session));
    }

    internal void RaiseSessionEnded(DisconnectInfo? info)
    {
        lock (_gate)
            _session = null;

        Invoke(SessionEnded, new ClientSessionEndedEventArgs(info));
    }

    private void Invoke<TArgs>(EventHandler<TArgs>? handler, TArgs args)
        where TArgs : EventArgs
    {
        if (handler is null)
            return;

        try
        {
            handler(this, args);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "A {Event} handler threw for extension {Id}.", typeof(TArgs).Name, Id);
        }
    }
}
