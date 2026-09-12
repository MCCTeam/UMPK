using Microsoft.Extensions.Logging;
using Umpk.Hosting;

namespace Umpk.Client.Plugins;

/// <summary>The live membership of plugins on a <see cref="UmpkClient"/>: which <see cref="IClientPlugin"/>s currently have a live, attached <see cref="ClientPluginContext"/>, and which will attach at the start of the client's next session. Seeded at construction from whatever <see cref="UmpkClientBuilder.AddPlugin"/> saw, but that seed only matters once: every session attach after that reads the CURRENT membership, so a plugin <see cref="AddAsync"/>-ed at runtime re-attaches on the client's next session and one <see cref="RemoveAsync"/>-ed at runtime does not, regardless of what the builder was given.</summary>
public sealed class ClientPluginCollection
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, IClientPlugin> _members = [];
    private readonly List<string> _order = [];
    private readonly Dictionary<string, PluginHost> _hosts = [];
    private readonly ISessionScheduler _scheduler;
    private readonly Func<IClientPlugin, PluginHost> _hostFactory;
    private readonly ILogger _logger;
    private bool _live;
    private bool _tearingDown;

    internal ClientPluginCollection(
        IReadOnlyList<IClientPlugin> initial,
        ISessionScheduler scheduler,
        Func<IClientPlugin, PluginHost> hostFactory,
        ILogger logger)
    {
        _scheduler = scheduler;
        _hostFactory = hostFactory;
        _logger = logger;

        foreach (IClientPlugin plugin in initial)
            AddMemberOrThrow(plugin);

    }

    /// <summary>The ids of every current member, attached or not, in registration order.</summary>
    public IReadOnlyList<string> Ids
    {
        get
        {
            lock (_gate)
                return [.. _order];

        }
    }

    /// <summary>True when a plugin with this id is currently a member, whether or not it is attached.</summary>
    public bool Contains(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        lock (_gate)
            return _members.ContainsKey(id);

    }

    /// <summary>Adds <paramref name="plugin"/> to the membership. With no live session (or while one is tearing down) this only records the plugin and completes immediately; it attaches at the start of the client's next session. With a session live and running normally, the attach is marshalled onto the session loop, so <see cref="IClientPlugin.Attach"/> genuinely runs there, and the returned task completes only once <c>Attach</c> has returned. A throwing <c>Attach</c> is logged and swallowed, exactly like session-start attach, so it does not fail this call.</summary>
    /// <exception cref="ArgumentException">A plugin with the same <see cref="IClientPlugin.Id"/> is already a member.</exception>
    public Task<ClientPluginRegistration> AddAsync(IClientPlugin plugin, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        bool attachNow;
        lock (_gate)
        {
            AddMemberOrThrow(plugin);
            attachNow = _live && !_tearingDown;
        }

        return attachNow
            ? AttachNowAsync(plugin, ct)
            : Task.FromResult(new ClientPluginRegistration(this, plugin.Id));
    }

    /// <summary>Removes the plugin with <paramref name="id"/> from the membership, so it will not attach on the client's next session. If it is currently attached, it is also detached: marshalled onto the session loop while a session is live and running normally, or run inline with no live session or while the session is tearing down (that window's detach already happens off the loop, in <see cref="UmpkClient"/>'s own teardown, and queuing behind it instead of joining it is how this stays awaitable rather than racing that teardown). Awaiting the returned task guarantees the detach, if there was one to do, has completed.</summary>
    /// <returns>True if a plugin with this id was a member, whether or not it was currently attached.</returns>
    public Task<bool> RemoveAsync(string id, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(id);

        bool wasMember;
        PluginHost? host;
        bool inline;
        lock (_gate)
        {
            wasMember = _members.Remove(id);
            _order.Remove(id);
            _hosts.Remove(id, out host);
            inline = !_live || _tearingDown;
        }

        if (host is null)
            return Task.FromResult(wasMember);

        if (inline)
        {
            host.Detach();
            return Task.FromResult(true);
        }

        return DetachNowAsync(host, ct);
    }

    /// <summary>Attaches every current member for a new session, marshalled onto the session loop so <see cref="IClientPlugin.Attach"/> genuinely runs there. Called once by <see cref="UmpkClient.ConnectAsync"/> per session, before the receive and tick loops start.</summary>
    internal async Task BeginSessionAsync(CancellationToken ct)
    {
        IClientPlugin[] snapshot;
        lock (_gate)
        {
            _live = true;
            _tearingDown = false;
            snapshot = new IClientPlugin[_order.Count];
            for (int i = 0; i < _order.Count; i++)
                snapshot[i] = _members[_order[i]];

        }

        await _scheduler.InvokeAsync(
            () =>
            {
                foreach (IClientPlugin plugin in snapshot)
                    AttachHost(plugin);

            },
            ct).ConfigureAwait(false);
    }

    /// <summary>Detaches every currently-attached plugin for the session that is ending. Runs inline: the caller (<see cref="UmpkClient"/>'s own teardown) is already off the session loop by the time this runs, so there is no loop left to marshal through. Marks the collection as tearing down for the duration, so a <see cref="RemoveAsync"/> racing this call also goes inline instead of queuing behind a session that is going away.</summary>
    internal void EndSession()
    {
        PluginHost[] snapshot;
        lock (_gate)
        {
            _tearingDown = true;
            snapshot = new PluginHost[_hosts.Count];
            _hosts.Values.CopyTo(snapshot, 0);
            _hosts.Clear();
        }

        foreach (PluginHost host in snapshot)
            host.Detach();

        lock (_gate)
        {
            _live = false;
            _tearingDown = false;
        }
    }

    /// <summary>A snapshot of the currently-attached hosts, for the tick loop to iterate without holding the lock.</summary>
    internal PluginHost[] SnapshotHosts()
    {
        lock (_gate)
        {
            PluginHost[] snapshot = new PluginHost[_hosts.Count];
            _hosts.Values.CopyTo(snapshot, 0);
            return snapshot;
        }
    }

    internal bool TryGetHost(string id, out PluginHost? host)
    {
        lock (_gate)
            return _hosts.TryGetValue(id, out host);

    }

    private void AddMemberOrThrow(IClientPlugin plugin)
    {
        if (!_members.TryAdd(plugin.Id, plugin))
            throw new ArgumentException($"A plugin with id '{plugin.Id}' is already registered.", nameof(plugin));

        _order.Add(plugin.Id);
    }

    private async Task<ClientPluginRegistration> AttachNowAsync(IClientPlugin plugin, CancellationToken ct)
    {
        await _scheduler.InvokeAsync(() => AttachHost(plugin), ct).ConfigureAwait(false);
        return new ClientPluginRegistration(this, plugin.Id);
    }

    private async Task<bool> DetachNowAsync(PluginHost host, CancellationToken ct)
    {
        await _scheduler.InvokeAsync(host.Detach, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>Builds a fresh host for <paramref name="plugin"/> and calls <see cref="IClientPlugin.Attach"/>. Always runs already-marshalled onto the session loop, from either <see cref="BeginSessionAsync"/> or <see cref="AttachNowAsync"/>.</summary>
    private void AttachHost(IClientPlugin plugin)
    {
        PluginHost host = _hostFactory(plugin);
        lock (_gate)
        {
            if (!_members.ContainsKey(plugin.Id))
            {
                // Removed before its turn to attach came up (raced a concurrent RemoveAsync); nothing was ever handed to the plugin, so there is nothing to detach either.
                return;
            }

            _hosts[plugin.Id] = host;
        }

        try
        {
            plugin.Attach(host.Context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Plugin {Plugin} threw during Attach.", plugin.Id);
        }
    }
}

/// <summary>A handle to one plugin's membership in a <see cref="ClientPluginCollection"/>, returned by <see cref="ClientPluginCollection.AddAsync"/>. <see cref="IsAttached"/> and <see cref="Context"/> read the collection's LIVE state rather than a snapshot taken at add time, so they answer differently across a reconnect on the same client: attached while a session has this plugin live, null between sessions.</summary>
public sealed class ClientPluginRegistration : IAsyncDisposable
{
    private readonly ClientPluginCollection _owner;

    internal ClientPluginRegistration(ClientPluginCollection owner, string id)
    {
        _owner = owner;
        Id = id;
    }

    /// <summary>The plugin's id.</summary>
    public string Id { get; }

    /// <summary>True while this plugin currently has a live, attached <see cref="ClientPluginContext"/>.</summary>
    public bool IsAttached => _owner.TryGetHost(Id, out _);

    /// <summary>The plugin's live context, or null when no live session currently has it attached.</summary>
    public ClientPluginContext? Context => _owner.TryGetHost(Id, out PluginHost? host) ? host!.Context : null;

    /// <summary>Removes the plugin from the collection; equivalent to <see cref="ClientPluginCollection.RemoveAsync"/>.</summary>
    public async ValueTask DisposeAsync() => await _owner.RemoveAsync(Id).ConfigureAwait(false);
}
