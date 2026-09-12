using Microsoft.Extensions.Logging;
using Umpk.Hosting;

namespace Umpk.Client;

/// <summary>The membership of <see cref="IClientExtension"/>s on a <see cref="UmpkClientSupervisor"/>: extensions that stay activated for as long as they are registered, spanning every reconnect the supervisor makes. This is the supervisor-scoped counterpart to <see cref="Plugins.ClientPluginCollection"/>, which is scoped to one <see cref="UmpkClient"/>'s one session; see <see cref="ClientExtensionContext"/> for the layering between the two.</summary>
public sealed class ClientExtensionCollection
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Entry> _members = [];
    private readonly List<string> _order = [];
    private readonly UmpkClientSupervisor _supervisor;
    private readonly ClientSupervisorOptions _options;

    internal ClientExtensionCollection(UmpkClientSupervisor supervisor, ClientSupervisorOptions options)
    {
        _supervisor = supervisor;
        _options = options;
    }

    /// <summary>The ids of every current member, in registration order.</summary>
    public IReadOnlyList<string> Ids
    {
        get
        {
            lock (_gate)
                return [.. _order];

        }
    }

    /// <summary>True when an extension with this id is currently a member.</summary>
    public bool Contains(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        lock (_gate)
            return _members.ContainsKey(id);

    }

    /// <summary>Registers <paramref name="extension"/> and calls <see cref="IClientExtension.ActivateAsync"/> immediately, whether or not a session is currently live. If a session IS live, the extension is additionally bridged into that session right away: <see cref="ClientExtensionContext.SessionCreated"/> is not replayed (the client already dialed before this extension existed), but <see cref="ClientExtensionContext.SessionStarted"/> fires once the bridge attaches.</summary>
    /// <exception cref="ArgumentException">An extension with the same <see cref="IClientExtension.Id"/> is already registered.</exception>
    public async Task<ClientExtensionRegistration> AddAsync(IClientExtension extension, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(extension);
        string id = extension.Id;

        var deactivatedCts = new CancellationTokenSource();
        ILoggerFactory loggerFactory = _options.LoggerFactory;
        var cron = new CronScheduler(_options.TimeProvider, loggerFactory.CreateLogger($"Umpk.Client.Extensions.{id}.Cron"));
        ILogger logger = loggerFactory.CreateLogger($"Umpk.Client.Extensions.{id}");
        var context = new ClientExtensionContext(id, _supervisor, logger, cron, deactivatedCts.Token);
        var entry = new Entry(extension, context, cron, deactivatedCts);

        lock (_gate)
        {
            if (!_members.TryAdd(id, entry))
            {
                deactivatedCts.Dispose();
                cron.DisposeAll();
                throw new ArgumentException($"An extension with id '{id}' is already registered.", nameof(extension));
            }

            _order.Add(id);
        }

        await extension.ActivateAsync(context, ct).ConfigureAwait(false);

        UmpkClient? client = _supervisor.Client;
        if (client is not null)
        {
            ExtensionSessionPlugin bridge = CreateBridge(entry, client);
            lock (_gate)
                entry.LiveBridge = bridge;

            await client.Plugins.AddAsync(bridge, ct).ConfigureAwait(false);
        }

        return new ClientExtensionRegistration(this, id);
    }

    /// <summary>Removes the extension with <paramref name="id"/>: if it currently has a live session, that session is ended for it first (<see cref="ClientExtensionContext.SessionEnded"/> fires and the bridge detaches from the live client), then <see cref="IClientExtension.DeactivateAsync"/> runs under a bounded wait (<see cref="ClientSupervisorOptions.ExtensionTeardownTimeout"/>), then its cron scheduler is disposed and <see cref="ClientExtensionContext.Deactivated"/> is cancelled.</summary>
    /// <returns>True if an extension with this id was a member.</returns>
    public async Task<bool> RemoveAsync(string id, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        Entry? entry;
        lock (_gate)
        {
            if (!_members.Remove(id, out entry))
                return false;

            _order.Remove(id);
        }

        await EndLiveSessionAsync(entry, ct).ConfigureAwait(false);
        await DeactivateOneAsync(entry, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>Bridges every currently-registered extension into a freshly built, still-unconnected client for a new session attempt: raises <see cref="ClientExtensionContext.SessionCreated"/> and adds this attempt's bridge to <paramref name="client"/>'s own <see cref="Plugins.ClientPluginCollection"/>. Called by <see cref="UmpkClientSupervisor"/> after building a client and before dialing it.</summary>
    internal async Task<IReadOnlyList<ExtensionSessionPlugin>> AttachForNewAttemptAsync(UmpkClient client, CancellationToken ct)
    {
        List<Entry> snapshot;
        lock (_gate)
        {
            snapshot = new List<Entry>(_order.Count);
            foreach (string id in _order)
                snapshot.Add(_members[id]);

        }

        var bridges = new List<ExtensionSessionPlugin>(snapshot.Count);
        foreach (Entry entry in snapshot)
        {
            entry.Context.RaiseSessionCreated(client);
            ExtensionSessionPlugin bridge = CreateBridge(entry, client);
            lock (_gate)
            {
                // A concurrent RemoveAsync may already have taken this entry out of _members; leaving LiveBridge set on an orphaned entry is harmless (nothing reads it again), so this assigns unconditionally rather than re-checking membership.
                entry.LiveBridge = bridge;
            }

            await client.Plugins.AddAsync(bridge, ct).ConfigureAwait(false);
            bridges.Add(bridge);
        }

        return bridges;
    }

    /// <summary>
    /// Deactivates every currently-registered extension in REVERSE registration order, each under the same bounded wait <see cref="RemoveAsync"/> uses. Called by <see cref="UmpkClientSupervisor.StopAsync"/> after the live client is released and before the supervisor's terminal completion, so a host awaiting <see cref="UmpkClientSupervisor.Completed"/> may then assume every extension is down.
    /// <para>Membership is dropped in the same lock that takes the snapshot, so a deactivated extension is no longer a member. A host that removes one afterwards (a plugin unloaded during shutdown) gets <c>false</c> from <see cref="RemoveAsync"/> instead of a second teardown of the same entry, which would cancel a token source this method has already disposed.</para>
    /// </summary>
    internal async Task DeactivateAllAsync(CancellationToken ct)
    {
        List<Entry> snapshot;
        lock (_gate)
        {
            snapshot = new List<Entry>(_order.Count);
            for (int i = _order.Count - 1; i >= 0; i--)
                snapshot.Add(_members[_order[i]]);

            _members.Clear();
            _order.Clear();
        }

        foreach (Entry entry in snapshot)
            await DeactivateOneAsync(entry, ct).ConfigureAwait(false);

    }

    /// <summary>Runs one extension's deactivate step, then its cron disposal and <see cref="ClientExtensionContext.Deactivated"/> cancellation.</summary>
    private async Task DeactivateOneAsync(Entry entry, CancellationToken ct)
    {
        bool abandoned = await DeactivateWithTimeoutAsync(entry, ct).ConfigureAwait(false);

        entry.Cron.DisposeAll();

        // On the abandoned path the extension may still hold the token, and disposing the source would make any later access on it throw. Let it be collected with the extension instead.
        if (!abandoned)
            try
            {
                entry.DeactivatedCts.Cancel();
            }
            finally
            {
                entry.DeactivatedCts.Dispose();
            }

    }

    /// <summary>Runs <see cref="IClientExtension.DeactivateAsync"/> with <see cref="ClientExtensionContext.Deactivated"/> as its token, bounded by <see cref="ClientSupervisorOptions.ExtensionTeardownTimeout"/> on <see cref="ClientSupervisorOptions.TimeProvider"/>. On timeout the token is cancelled (so the extension, and anything else watching it, can still notice and wind down) and the wait is abandoned rather than extended. The abandoned task's eventual fault is observed so it cannot resurface as an unobserved exception during a later collection.</summary>
    /// <returns>True if the deactivate call was abandoned after a timeout.</returns>
    private async Task<bool> DeactivateWithTimeoutAsync(Entry entry, CancellationToken ct)
    {
        Task task = entry.Extension.DeactivateAsync(entry.Context.Deactivated).AsTask();
        try
        {
            await task.WaitAsync(_options.ExtensionTeardownTimeout, _options.TimeProvider, ct).ConfigureAwait(false);
            return false;
        }
        catch (TimeoutException)
        {
            entry.Context.Logger.LogWarning(
                "Extension {Id} did not deactivate within {Timeout}; abandoning it.",
                entry.Context.Id, _options.ExtensionTeardownTimeout);

            // Nobody will await the abandoned task, and cancelling Deactivated below usually faults it. Observe the fault so it does not resurface as an unobserved-exception crash at the next GC.
            _ = task.ContinueWith(
                static t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);

            try
            {
                entry.DeactivatedCts.Cancel();
            }
            catch (Exception ex)
            {
                entry.Context.Logger.LogDebug(ex, "Cancelling the Deactivated token of extension {Id} threw.", entry.Context.Id);
            }

            return true;
        }
        catch (Exception ex)
        {
            entry.Context.Logger.LogWarning(ex, "Extension {Id} threw during deactivation.", entry.Context.Id);
            return false;
        }
    }

    private ExtensionSessionPlugin CreateBridge(Entry entry, UmpkClient client) =>
        new(entry.Context, client, onSessionEnded: () => ClearLiveBridge(entry));

    private void ClearLiveBridge(Entry entry)
    {
        lock (_gate)
            entry.LiveBridge = null;

    }

    private async Task EndLiveSessionAsync(Entry entry, CancellationToken ct)
    {
        ExtensionSessionPlugin? bridge;
        lock (_gate)
            bridge = entry.LiveBridge;

        if (bridge is null)
            return;

        bridge.EndSessionOnce(_supervisor.Client?.LastDisconnect);

        UmpkClient? client = _supervisor.Client;
        if (client is not null)
            await client.Plugins.RemoveAsync(entry.Context.Id, ct).ConfigureAwait(false);

    }

    private sealed class Entry(IClientExtension extension, ClientExtensionContext context, CronScheduler cron, CancellationTokenSource deactivatedCts)
    {
        public IClientExtension Extension { get; } = extension;

        public ClientExtensionContext Context { get; } = context;

        public CronScheduler Cron { get; } = cron;

        public CancellationTokenSource DeactivatedCts { get; } = deactivatedCts;

        public ExtensionSessionPlugin? LiveBridge { get; set; }
    }
}

/// <summary>A handle to one extension's membership in a <see cref="ClientExtensionCollection"/>.</summary>
public sealed class ClientExtensionRegistration : IAsyncDisposable
{
    private readonly ClientExtensionCollection _owner;

    internal ClientExtensionRegistration(ClientExtensionCollection owner, string id)
    {
        _owner = owner;
        Id = id;
    }

    /// <summary>The extension's id.</summary>
    public string Id { get; }

    /// <summary>Removes the extension; equivalent to <see cref="ClientExtensionCollection.RemoveAsync"/>.</summary>
    public async ValueTask DisposeAsync() => await _owner.RemoveAsync(Id).ConfigureAwait(false);
}
