using Microsoft.Extensions.Logging;
using Umpk.Client.Commands;
using Umpk.Client.Movement;
using Umpk.Commands;
using Umpk.Protocol.Java;

namespace Umpk.Client.Plugins;

/// <summary>Owns a plugin's per-session resources and tears them down in reverse order on detach: plugin channels, movement leases, command scope, scheduled work, event subscriptions, and raw-frame observations. The subscriptions/actions/events the plugin uses are wrapped so their disposal is tracked here.</summary>
internal sealed class PluginHost
{
    private readonly CancellationTokenSource _detached = new();
    private readonly UmpkClient _client;
    private readonly PluginChannelManager _channels;
    private readonly MovementLeaseManager _leases;
    private readonly ICommandRegistrationScope<ClientCommandSource> _commandScope;
    private readonly List<IDisposable> _leasesGranted = [];
    private readonly List<PluginChannelRegistration> _channelsRegistered = [];
    private readonly List<IDisposable> _packetObservations = [];
    private readonly ILogger _logger;

    public PluginHost(
        string id,
        UmpkClient client,
        Events.EventBus eventBus,
        ClientActions actions,
        CommandService<ClientCommandSource> commands,
        MovementLeaseManager leases,
        PluginChannelManager channels,
        ILogger logger)
    {
        Id = id;
        _client = client;
        _channels = channels;
        _leases = leases;
        _logger = logger;
        _commandScope = commands.CreateScope(id);

        Scheduler = new PluginScheduler(
            postToLoop: work => client.PostAsync(_ => work()),
            runOffLoop: RunOffLoopAsync);

        var trackedEvents = new TrackingClientEvents(eventBus, _detached.Token);
        TrackedEvents = trackedEvents;

        Context = new ClientPluginContext(
            client,
            trackedEvents,
            actions,
            Scheduler,
            _commandScope,
            AcquireMovement,
            () => _leases.CurrentOwner,
            RegisterChannel,
            SendChannel,
            ObservePackets,
            _detached.Token);
    }

    public string Id { get; }

    public PluginScheduler Scheduler { get; }

    public ClientPluginContext Context { get; }

    internal TrackingClientEvents TrackedEvents { get; }

    public void Detach()
    {
        try
        {
            _detached.Cancel();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Plugin {Plugin} detach-token cancel threw.", Id);
        }

        foreach (PluginChannelRegistration channel in _channelsRegistered)
            channel.Dispose();

        _channelsRegistered.Clear();

        foreach (IDisposable lease in _leasesGranted)
            lease.Dispose();

        _leasesGranted.Clear();

        foreach (IDisposable observation in _packetObservations)
            observation.Dispose();

        _packetObservations.Clear();

        Scheduler.DisposeAll();
        TrackedEvents.DisposeAll();
        _commandScope.Dispose();
        _detached.Dispose();
    }

    private IMovementLease? AcquireMovement(string reason)
    {
        IMovementLease? lease = _leases.TryAcquire($"{Id}:{reason}");
        if (lease is not null)
            _leasesGranted.Add(lease);

        return lease;
    }

    private PluginChannelRegistration RegisterChannel(Identifier channel, Action<ReadOnlyMemory<byte>> onMessage)
    {
        // Play, as it has always been. A plugin attaches after play has begun, so the configuration phase is already over by the time it could ask for one; a consumer that needs the configuration phase registers on UmpkClient.Channels before the dial instead.
        PluginChannelRegistration reg = _channels.Register(ProtocolPhase.Play, channel, onMessage, _detached.Token);
        _channelsRegistered.Add(reg);
        return reg;
    }

    private async ValueTask SendChannel(Identifier channel, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _detached.Token);
        await _channels.SendAsync(channel, data, linked.Token).ConfigureAwait(false);
    }

    private IDisposable ObservePackets(PacketFrameHandler handler)
    {
        IDisposable subscription = _client.ObservePackets(handler);
        _packetObservations.Add(subscription);
        return subscription;
    }

    private Task RunOffLoopAsync(Func<Task> work, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _detached.Token);
        return Task.Run(work, linked.Token);
    }
}
